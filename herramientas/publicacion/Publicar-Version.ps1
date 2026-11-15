<#
.SYNOPSIS
    Saca una version de Cuaderno NODISLA: publica, FIRMA los binarios propios, compila el
    instalador FIRMADO, calcula su SHA-256 y, con -Subir, crea la release en GitHub con los dos
    ficheros adjuntos.

.DESCRIPTION
    El aviso de versiones del programa consulta releases/latest de EA8DLF/CuadernoNodisla y
    solo ofrece «Descargar e instalar» si la release trae:
      - CuadernoNodisla-Instalador-<version>.exe
      - CuadernoNodisla-Instalador-<version>.exe.sha256   (formato sha256sum: "suma  nombre")
    Sin el .sha256 el programa NO instala nada. Desde la 0.2.19 ademas exige que el instalador
    lleve la firma Authenticode de NODISLA: la suma solo dice que la descarga llego entera; la
    firma dice de quien es. Este script deja los dos ficheros juntos y el instalador firmado.

    La version sale de <Version> en Directory.Build.props; la etiqueta es v<Version>.
    Sin -Subir solo genera los ficheros en instalador\salida y no toca GitHub.

    QUE SE FIRMA (con sello de tiempo, ver Firma.ps1 y docs\20-firma-de-codigo.md):
      - los .exe y .dll propios (Nodisla.Cuaderno.*) de la carpeta publicada, antes de empaquetar;
      - el desinstalador y el instalador, que los firma Inno Setup al compilar (SignTool).

    EL SCRIPT SE NIEGA A SEGUIR SI:
      - no esta el certificado de firma en el almacen del usuario, o no vale;
      - no hay red o el servidor de sello de tiempo no contesta (no se publica sin sello);
      - algun binario propio o el instalador queda sin la firma de NODISLA;
      - con -Subir, la etiqueta v<Version> ya existe en git (local o en origin) o ya hay una
        release con ese nombre. Una version publicada no se reemplaza: se sube <Version>.

.PARAMETER Subir
    Crea la release v<Version> en GitHub con gh. Requiere 'gh auth login' hecho. La ficha de
    acceso es la de gh en este PC: nunca va en el programa. La etiqueta la crea gh: NO hay que
    crearla antes a mano (si ya existe, el script se niega).

.PARAMETER Notas
    Fichero Markdown con las notas de la version (lo que el programa ensena en el aviso).

.PARAMETER SinCompilar
    Reutiliza el instalador ya compilado en instalador\salida. Se vuelve a comprobar su firma.

.PARAMETER HuellaDeFirma
    Huella SHA-1 del certificado de firma en Cert:\CurrentUser\My. Por omision, el de NODISLA.

.PARAMETER ServidorDeSello
    Servidor de sello de tiempo RFC 3161. Por omision http://timestamp.digicert.com.

.PARAMETER SignTool
    Ruta de signtool.exe. Por omision se busca el x64 mas nuevo del SDK de Windows.

.PARAMETER Iscc
    Ruta de ISCC.exe (Inno Setup 6). Por omision se busca en la instalacion por usuario y en
    Program Files.

.EXAMPLE
    .\herramientas\publicacion\Publicar-Version.ps1
    .\herramientas\publicacion\Publicar-Version.ps1 -SinCompilar -Subir -Notas .\notas-0.2.19.md
#>
[CmdletBinding()]
param(
    [switch] $Subir,
    [string] $Notas,
    [switch] $SinCompilar,
    [string] $Repositorio = 'EA8DLF/CuadernoNodisla',
    [string] $Iscc,
    [string] $HuellaDeFirma,
    [string] $ServidorDeSello,
    [string] $SignTool
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Firma.ps1')
if (-not $HuellaDeFirma) { $HuellaDeFirma = $script:HuellaNodisla }
if (-not $ServidorDeSello) { $ServidorDeSello = $script:SelloDeTiempoPorOmision }

$raiz = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $raiz

# Ejecuta una orden nativa sin que su stderr pare el script: devuelve la salida y deja el
# codigo en $LASTEXITCODE. Con $ErrorActionPreference = 'Stop', redirigir el stderr de un
# programa lo convierte en un error que corta el script antes de poder mirar el codigo.
function Invoke-Nativo {
    param([Parameter(Mandatory)] [string] $Programa, [string[]] $Argumentos = @())
    $anterior = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { return @(& $Programa @Argumentos 2>&1 | ForEach-Object { "$_" }) }
    finally { $ErrorActionPreference = $anterior }
}

# ── Version ────────────────────────────────────────────────────────────
[xml] $props = Get-Content (Join-Path $raiz 'Directory.Build.props')
$version = ($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
if (-not $version) { throw 'No hay <Version> en Directory.Build.props.' }
$etiqueta = "v$version"
$cuatro = (($version.Split('-')[0].Split('.') + @('0', '0', '0', '0'))[0..3]) -join '.'
$nombre = "CuadernoNodisla-Instalador-$cuatro.exe"
$salida = Join-Path $raiz 'instalador\salida'
$instalador = Join-Path $salida $nombre
Write-Host "Version $version (etiqueta $etiqueta), instalador $nombre"

# ── La etiqueta no puede existir ya ────────────────────────────────────
# Ya paso una vez: dos compilaciones distintas publicadas con el mismo numero. Una version
# publicada no se reemplaza; se sube <Version> y se publica otra.
# Sin -Subir (solo generar para revisar) se avisa; con -Subir, se para.
$motivos = @()
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    $motivos += 'no se encuentra git, asi que no se puede comprobar si la etiqueta ya existe'
}
else {
    $local = Invoke-Nativo git @('tag', '--list', $etiqueta)
    if ($LASTEXITCODE -ne 0) { $motivos += "no se puede consultar git en $raiz (¿no es el repositorio?)" }
    elseif (($local -join '').Trim()) { $motivos += "la etiqueta $etiqueta ya existe en este repositorio (git tag)" }

    $remota = Invoke-Nativo git @('ls-remote', '--tags', "https://github.com/$Repositorio.git", "refs/tags/$etiqueta")
    if ($LASTEXITCODE -ne 0) { $motivos += "no se puede consultar las etiquetas de github.com/$Repositorio (¿sin red o sin acceso?)" }
    elseif (($remota -join '') -match [regex]::Escape("refs/tags/$etiqueta")) { $motivos += "la etiqueta $etiqueta ya existe en github.com/$Repositorio" }
}

if ($Subir) {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'Falta GitHub CLI (gh).' }
    # "gh release view" sale con codigo distinto de cero cuando la release no existe: es el caso bueno.
    $null = Invoke-Nativo gh @('release', 'view', $etiqueta, '--repo', $Repositorio)
    if ($LASTEXITCODE -eq 0) { $motivos += "ya hay una release $etiqueta en github.com/$Repositorio" }
}

if ($motivos.Count -gt 0) {
    $texto = "La version $version no se puede publicar asi:`n  - " + ($motivos -join "`n  - ") +
        "`nUna version publicada no se reemplaza: subir <Version> en Directory.Build.props."
    if ($Subir) { throw $texto }
    Write-Warning $texto
}

# ── Certificado de firma ───────────────────────────────────────────────
$certificado = Get-CertificadoDeFirma -Huella $HuellaDeFirma
$HuellaDeFirma = $certificado.Thumbprint
Write-Host "Firma: $($certificado.Subject) [$HuellaDeFirma], caduca $($certificado.NotAfter.ToString('yyyy-MM-dd')); sello de tiempo $ServidorDeSello"

# ── Publicar, firmar y compilar el instalador ─────────────────────────
if (-not $SinCompilar) {
    $herramienta = Get-SignTool -Ruta $SignTool
    if (-not $Iscc) {
        $Iscc = @(
            (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
            (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
            (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
        ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    }
    if (-not $Iscc -or -not (Test-Path -LiteralPath $Iscc)) { throw 'No se encuentra ISCC.exe de Inno Setup 6 (indicar con -Iscc).' }

    $publicar = Join-Path $raiz 'instalador\publicar\win-x64'
    if (Test-Path $publicar) { Remove-Item $publicar -Recurse -Force }
    dotnet publish 'src\Nodisla.Cuaderno.Ui\Nodisla.Cuaderno.Ui.csproj' -c Release -r win-x64 --self-contained true -o $publicar
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish ha fallado.' }

    # Lo propio: Nodisla.Cuaderno.*.exe y .dll, tambien los satelite de idioma (en\, pt\...).
    # Lo de terceros y el .NET se quedan como vienen (los de Microsoft ya llegan firmados).
    $propios = @(Get-ChildItem -LiteralPath $publicar -Recurse -File |
        Where-Object { $_.Name -like 'Nodisla.Cuaderno.*' -and @('.exe', '.dll') -contains $_.Extension.ToLowerInvariant() } |
        ForEach-Object { $_.FullName })
    if ($propios.Count -eq 0) { throw "No hay binarios Nodisla.Cuaderno.* en $publicar." }
    Write-Host "Firmando $($propios.Count) binarios propios..."
    Set-FirmaNodisla -Ficheros $propios -Huella $HuellaDeFirma -ServidorDeSello $ServidorDeSello -SignTool $herramienta

    # Inno Setup firma el desinstalador y el instalador con la misma orden. En la orden de
    # /S, $q es una comilla y $f el fichero a firmar (los pone Inno). /DFirmar activa
    # «SignTool=nodisla» en el .iss: si la firma falla, ISCC falla y no hay instalador.
    $orden = '$q' + $herramienta + '$q ' + ((Get-ArgumentosDeSignTool -Huella $HuellaDeFirma -ServidorDeSello $ServidorDeSello) -join ' ') + ' $f'
    if (Test-Path -LiteralPath $instalador) { Remove-Item -LiteralPath $instalador -Force }
    & $Iscc '/DFirmar' "/Snodisla=$orden" 'instalador\CuadernoNodisla.iss'
    if ($LASTEXITCODE -ne 0) { throw 'ISCC ha fallado (si es en el paso de firma: red o servidor de sello de tiempo; no se publica sin sello).' }

    # Que no se haya colado nada sin firmar entre publicar y empaquetar.
    Assert-FirmaNodisla -Ficheros $propios -Huella $HuellaDeFirma
}
if (-not (Test-Path $instalador)) { throw "No esta $instalador. ¿Coincide la version del .exe publicado con Directory.Build.props?" }

# El instalador, compilado ahora o reutilizado con -SinCompilar, tiene que llevar la firma.
Assert-FirmaNodisla -Ficheros @($instalador) -Huella $HuellaDeFirma
Write-Host "Instalador firmado por $($certificado.Subject), con sello de tiempo."

# ── Suma SHA-256 (formato sha256sum, sin BOM) ──────────────────────────
# Siempre DESPUES de firmar: la firma cambia el fichero.
$suma = (Get-FileHash $instalador -Algorithm SHA256).Hash.ToLowerInvariant()
$ficheroSuma = "$instalador.sha256"
[IO.File]::WriteAllText($ficheroSuma, "$suma  $nombre`n", (New-Object Text.UTF8Encoding($false)))
Write-Host "SHA-256 $suma"
Write-Host "Ficheros listos:`n  $instalador`n  $ficheroSuma"

if (-not $Subir) {
    Write-Host 'Sin -Subir: no se ha tocado GitHub.'
    return
}

# ── Release en GitHub ──────────────────────────────────────────────────
# Aqui la etiqueta y la release no existen (se ha comprobado arriba): siempre se crea, nunca
# se reemplazan adjuntos de una version ya publicada.
$argumentos = @('release', 'create', $etiqueta, $instalador, $ficheroSuma, '--repo', $Repositorio, '--title', "Cuaderno NODISLA $version")
if ($Notas) { $argumentos += @('--notes-file', $Notas) } else { $argumentos += @('--generate-notes') }
if ($version.Contains('-')) { $argumentos += '--prerelease' }
gh @argumentos
if ($LASTEXITCODE -ne 0) { throw 'gh ha fallado.' }
Write-Host "Publicada $etiqueta en https://github.com/$Repositorio/releases/tag/$etiqueta"
