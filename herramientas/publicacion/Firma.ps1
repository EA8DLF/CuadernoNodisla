<#
.SYNOPSIS
    Funciones de firma Authenticode de Cuaderno NODISLA. Se cargan con «. .\Firma.ps1».

.DESCRIPTION
    Las usa Publicar-Version.ps1 y sirven tambien para comprobar a mano un instalador:

        . .\herramientas\publicacion\Firma.ps1
        Test-FirmaNodisla -Fichero .\CuadernoNodisla-Instalador-0.2.19.0.exe

    El certificado se elige por huella en el almacen del usuario (Cert:\CurrentUser\My). Aqui
    no hay contrasenas ni ficheros .pfx: la clave privada no sale del almacen de Windows.

    El certificado de NODISLA es AUTOFIRMADO. En un PC que no lo tenga instalado como raiz de
    confianza, Windows dice que la firma es «de un editor desconocido» aunque este perfecta.
    Por eso aqui no se pregunta si Windows confia en la cadena, sino tres cosas concretas:
    que el fichero no se ha tocado desde que se firmo, que lo firmo exactamente el certificado
    de la huella y que lleva sello de tiempo.
#>

# Huella SHA-1 (la que ensena Windows) del certificado de firma de NODISLA:
# «CN=Jose M. EA8DLF (SDR Nodisla)», valido hasta el 04/09/2036.
# Si se renueva el certificado hay que cambiarla aqui Y anadir la huella SHA-256 del nuevo a
# FirmantesDeConfianza (src\Nodisla.Cuaderno.Servicios\Actualizaciones\FirmaDeInstalador.cs)
# ANTES de firmar nada con el. Ver docs\20-firma-de-codigo.md.
$script:HuellaNodisla = '919A06FFBDF618A0D1CE38426547328C1FB82E74'

# Servidor publico de sello de tiempo RFC 3161.
$script:SelloDeTiempoPorOmision = 'http://timestamp.digicert.com'

# Uso «Firma de codigo» (EKU).
$script:OidFirmaDeCodigo = '1.3.6.1.5.5.7.3.3'

function Limpiar-Huella {
    param([Parameter(Mandatory)] [string] $Huella)
    return (($Huella -replace '[^0-9A-Fa-f]', '').ToUpperInvariant())
}

function Get-CertificadoDeFirma {
    <#
    .SYNOPSIS
        Devuelve el certificado de firma del almacen del usuario, o falla diciendo por que no vale.
    #>
    [CmdletBinding()]
    param([string] $Huella = $script:HuellaNodisla)

    $limpia = Limpiar-Huella $Huella
    if ($limpia.Length -ne 40) { throw "La huella del certificado de firma tiene que ser la SHA-1 de 40 digitos que ensena Windows: '$Huella'." }

    $ruta = "Cert:\CurrentUser\My\$limpia"
    if (-not (Test-Path -LiteralPath $ruta)) {
        throw "No esta el certificado de firma $limpia en Cert:\CurrentUser\My. Hay que importarlo en este usuario (ver docs\20-firma-de-codigo.md)."
    }

    $certificado = Get-Item -LiteralPath $ruta
    if (-not $certificado.HasPrivateKey) { throw "El certificado $limpia esta en el almacen pero sin clave privada: no sirve para firmar." }
    $usos = @($certificado.EnhancedKeyUsageList | ForEach-Object { $_.ObjectId })
    if ($usos -notcontains $script:OidFirmaDeCodigo) { throw "El certificado $limpia no tiene el uso «Firma de codigo» ($script:OidFirmaDeCodigo)." }
    $ahora = Get-Date
    if ($certificado.NotAfter -le $ahora) { throw "El certificado $limpia caduco el $($certificado.NotAfter). Hay que renovarlo (ver docs\20-firma-de-codigo.md)." }
    if ($certificado.NotBefore -gt $ahora) { throw "El certificado $limpia todavia no es valido (empieza el $($certificado.NotBefore))." }
    return $certificado
}

function Get-SignTool {
    <#
    .SYNOPSIS
        Busca signtool.exe (x64) del SDK de Windows. Falla si no esta.
    #>
    [CmdletBinding()]
    param([string] $Ruta)

    if ($Ruta) {
        if (-not (Test-Path -LiteralPath $Ruta)) { throw "No esta signtool.exe en $Ruta." }
        return (Resolve-Path -LiteralPath $Ruta).Path
    }

    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (Test-Path -LiteralPath $kits) {
        $encontrado = Get-ChildItem -LiteralPath $kits -Directory |
            Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' } |
            Sort-Object { [version] $_.Name } -Descending |
            ForEach-Object { Join-Path $_.FullName 'x64\signtool.exe' } |
            Where-Object { Test-Path -LiteralPath $_ } |
            Select-Object -First 1
        if ($encontrado) { return $encontrado }
    }

    $enRuta = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($enRuta) { return $enRuta.Source }
    throw 'No se encuentra signtool.exe. Viene con el SDK de Windows (componente «Windows SDK Signing Tools for Desktop Apps»); tambien se puede indicar con -SignTool.'
}

function Get-ArgumentosDeSignTool {
    <#
    .SYNOPSIS
        Argumentos de «signtool sign», sin los ficheros. Los mismos para los binarios y para
        la orden que se le pasa a Inno Setup: una sola definicion de como se firma.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Huella,
        [Parameter(Mandatory)] [string] $ServidorDeSello
    )

    if ($ServidorDeSello -notmatch '^https?://[^\s"]+$') { throw "El servidor de sello de tiempo no es una direccion http(s): '$ServidorDeSello'." }
    # /s My sin /sm: almacen «Personal» del usuario. /fd y /td SHA256: resumen del fichero y
    # del sello. /tr: sello de tiempo RFC 3161 (sin sello, la firma dejaria de valer el dia
    # que caduque el certificado).
    return @('sign', '/s', 'My', '/sha1', (Limpiar-Huella $Huella), '/fd', 'SHA256', '/tr', $ServidorDeSello, '/td', 'SHA256')
}

function Test-FirmaNodisla {
    <#
    .SYNOPSIS
        Comprueba que un fichero esta firmado por el certificado de la huella, sin tocar desde
        entonces y con sello de tiempo. Devuelve $null si todo esta bien, o el motivo.

    .DESCRIPTION
        No exige que Windows confie en la cadena: el certificado es autofirmado y en un PC sin
        el instalado Get-AuthenticodeSignature da «UnknownError» (PowerShell 5.1) o
        «NotTrusted» (PowerShell 7) con la firma perfecta. Lo que no se admite nunca es
        «NotSigned» ni «HashMismatch».
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Fichero,
        [string[]] $Huella = @($script:HuellaNodisla),
        [switch] $SinSello
    )

    if (-not (Test-Path -LiteralPath $Fichero)) { return "no existe" }
    $firma = Get-AuthenticodeSignature -LiteralPath $Fichero
    $estado = [string] $firma.Status
    if ($estado -eq 'NotSigned') { return 'no esta firmado' }
    if ($estado -eq 'HashMismatch') { return 'se ha alterado despues de firmarlo (el resumen no casa)' }
    if (@('Valid', 'UnknownError', 'NotTrusted') -notcontains $estado) { return "firma no valida ($estado): $($firma.StatusMessage)" }
    if ($null -eq $firma.SignerCertificate) { return 'no se puede leer el certificado firmante' }

    $admitidas = @($Huella | ForEach-Object { Limpiar-Huella $_ })
    if ($admitidas -notcontains $firma.SignerCertificate.Thumbprint) {
        return "lo ha firmado otro certificado: $($firma.SignerCertificate.Subject) [$($firma.SignerCertificate.Thumbprint)]"
    }

    if (-not $SinSello -and $null -eq $firma.TimeStamperCertificate) { return 'esta firmado pero sin sello de tiempo' }
    return $null
}

function Assert-FirmaNodisla {
    <#
    .SYNOPSIS
        Falla si alguno de los ficheros no pasa Test-FirmaNodisla. Dice cuales y por que.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string[]] $Ficheros,
        [string[]] $Huella = @($script:HuellaNodisla)
    )

    $malos = @()
    foreach ($fichero in $Ficheros) {
        $motivo = Test-FirmaNodisla -Fichero $fichero -Huella $Huella
        if ($motivo) { $malos += "  $fichero : $motivo" }
    }

    if ($malos.Count -gt 0) {
        throw ("Hay $($malos.Count) fichero(s) sin la firma de NODISLA. NO se publica.`n" + ($malos -join "`n"))
    }
}

function Set-FirmaNodisla {
    <#
    .SYNOPSIS
        Firma los ficheros con el certificado de la huella y sello de tiempo, y comprueba que
        han quedado firmados. Falla (no deja nada a medias sin avisar) si algo no sale.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string[]] $Ficheros,
        [string] $Huella = $script:HuellaNodisla,
        [string] $ServidorDeSello = $script:SelloDeTiempoPorOmision,
        [string] $SignTool
    )

    $certificado = Get-CertificadoDeFirma -Huella $Huella
    $herramienta = Get-SignTool -Ruta $SignTool
    $base = Get-ArgumentosDeSignTool -Huella $certificado.Thumbprint -ServidorDeSello $ServidorDeSello

    # De 40 en 40: la linea de ordenes de Windows tiene limite.
    for ($i = 0; $i -lt $Ficheros.Count; $i += 40) {
        $lote = @($Ficheros[$i..([Math]::Min($i + 39, $Ficheros.Count - 1))])
        $anterior = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        $salida = & $herramienta @base @lote 2>&1 | ForEach-Object { "$_" }
        $codigo = $LASTEXITCODE
        $ErrorActionPreference = $anterior
        if ($codigo -ne 0) {
            throw ("signtool ha fallado (codigo $codigo) firmando con sello de tiempo de $ServidorDeSello.`n" +
                "Si es por la red o porque el servidor de sello no contesta, NO se publica sin sello: reintentar mas tarde o indicar otro con -ServidorDeSello.`n" +
                ($salida -join "`n"))
        }
    }

    Assert-FirmaNodisla -Ficheros $Ficheros -Huella $certificado.Thumbprint
}
