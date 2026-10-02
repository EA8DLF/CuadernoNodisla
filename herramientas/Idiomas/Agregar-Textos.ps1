<#
.SYNOPSIS
    Añade o cambia textos del programa en los seis idiomas de una vez.

.DESCRIPTION
    Lee un JSON con esta forma:

        {
          "Cabina.Conectar": { "es": "Conectar", "en": "Connect", "pt": "Ligar",
                               "fr": "Connecter", "it": "Connetti", "de": "Verbinden",
                               "nota": "Botón del equipo (opcional, va como comentario)" },
          ...
        }

    y escribe cada clave en src\Nodisla.Cuaderno.Idiomas\Recursos\<Apartado>[.<idioma>].resx,
    donde <Apartado> es lo que va antes del primer punto de la clave. Si la clave ya existe, la
    reemplaza. Los ficheros quedan ordenados por clave, para que las diferencias se lean bien.

    Comprueba antes de escribir nada que cada clave trae los seis idiomas, ninguno vacío, y que
    los huecos {0}, {1:N0}... son los mismos en todos. Si algo falla, no escribe nada.

    Se puede lanzar a la vez desde varios sitios: un cerrojo con nombre hace que se escriba de
    uno en uno.

.PARAMETER Json
    Fichero JSON (UTF-8) con los textos.

.PARAMETER Quitar
    Claves que borrar de los seis ficheros (en vez de, o además de, -Json).

.EXAMPLE
    .\herramientas\Idiomas\Agregar-Textos.ps1 -Json .\nuevos.json
#>
[CmdletBinding()]
param(
    [string] $Json,
    [string[]] $Quitar = @()
)

$ErrorActionPreference = 'Stop'
$idiomas = @('es', 'en', 'pt', 'fr', 'it', 'de')
$apartados = @('Comun', 'Principal', 'Cabina', 'Digital', 'Libro', 'Qsl', 'Ajustes', 'Ayuda', 'Dialogos', 'Servicios')
$carpeta = Join-Path $PSScriptRoot '..\..\src\Nodisla.Cuaderno.Idiomas\Recursos'
$carpeta = [System.IO.Path]::GetFullPath($carpeta)

function Huecos([string] $texto) {
    $sinDobles = $texto.Replace('{{', '').Replace('}}', '')
    $m = [regex]::Matches($sinDobles, '\{(\d+)[^}]*\}')
    ($m | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique) -join ','
}

function Apartado([string] $clave) {
    $punto = $clave.IndexOf('.')
    if ($punto -le 0) { throw "La clave «$clave» no lleva apartado delante (Apartado.Nombre)." }
    $a = $clave.Substring(0, $punto)
    if ($apartados -notcontains $a) { throw "La clave «$clave» es de un apartado que no existe: $a. Apartados: $($apartados -join ', ')." }
    if ($clave -notmatch '^[A-Za-z][A-Za-z0-9]*(\.[A-Za-z0-9_]+)+$') { throw "La clave «$clave» solo puede llevar letras, cifras, puntos y guiones bajos." }
    $a
}

$cabecera = @'
<?xml version="1.0" encoding="utf-8"?>
<root>
  <resheader name="resmimetype">
    <value>text/microsoft-resx</value>
  </resheader>
  <resheader name="version">
    <value>2.0</value>
  </resheader>
  <resheader name="reader">
    <value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
  </resheader>
  <resheader name="writer">
    <value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
  </resheader>
</root>
'@

function Fichero([string] $apartado, [string] $idioma) {
    if ($idioma -eq 'es') { Join-Path $carpeta "$apartado.resx" } else { Join-Path $carpeta "$apartado.$idioma.resx" }
}

function Abrir([string] $ruta) {
    $doc = New-Object System.Xml.XmlDocument
    $doc.PreserveWhitespace = $false
    if (Test-Path -LiteralPath $ruta) { $doc.Load($ruta) } else { $doc.LoadXml($cabecera) }
    $doc
}

function Guardar($doc, [string] $ruta) {
    # Ordena los <data> por clave y escribe con sangría, en UTF-8 sin BOM.
    $raiz = $doc.DocumentElement
    $datos = @($raiz.SelectNodes('data'))
    foreach ($d in $datos) { [void]$raiz.RemoveChild($d) }
    foreach ($d in ($datos | Sort-Object { $_.GetAttribute('name') } -CaseSensitive)) { [void]$raiz.AppendChild($d) }

    $ajustes = New-Object System.Xml.XmlWriterSettings
    $ajustes.Indent = $true
    $ajustes.IndentChars = '  '
    $ajustes.Encoding = New-Object System.Text.UTF8Encoding($false)
    $temporal = "$ruta.nuevo"
    $escritor = [System.Xml.XmlWriter]::Create($temporal, $ajustes)
    try { $doc.Save($escritor) } finally { $escritor.Dispose() }
    Move-Item -LiteralPath $temporal -Destination $ruta -Force
}

# ── Leer y validar ────────────────────────────────────────────────────────
$nuevos = [ordered]@{}
if ($Json) {
    $texto = [System.IO.File]::ReadAllText((Resolve-Path -LiteralPath $Json), [System.Text.Encoding]::UTF8)
    $objeto = $texto | ConvertFrom-Json
    $errores = New-Object System.Collections.Generic.List[string]
    foreach ($p in $objeto.PSObject.Properties) {
        $clave = $p.Name
        try { [void](Apartado $clave) } catch { $errores.Add($_.Exception.Message); continue }
        $valores = @{}
        foreach ($i in $idiomas) {
            $v = $p.Value.$i
            if ([string]::IsNullOrWhiteSpace($v)) { $errores.Add("$clave : falta «$i» o está vacío."); continue }
            $valores[$i] = [string]$v
        }
        if ($valores.Count -eq $idiomas.Count) {
            $ref = Huecos $valores['es']
            foreach ($i in $idiomas) {
                $h = Huecos $valores[$i]
                if ($h -ne $ref) { $errores.Add("$clave : los huecos de «$i» ({$h}) no son los del español ({$ref}).") }
            }
        }
        $valores['nota'] = [string]$p.Value.nota
        $nuevos[$clave] = $valores
    }
    if ($errores.Count -gt 0) {
        $errores | ForEach-Object { Write-Host "ERROR $_" -ForegroundColor Red }
        throw "No se ha escrito nada: $($errores.Count) errores."
    }
}

# ── Escribir, de uno en uno ───────────────────────────────────────────────
$cerrojo = New-Object System.Threading.Mutex($false, 'CuadernoNodisla.Idiomas.Resx')
[void]$cerrojo.WaitOne()
try {
    $porApartado = @{}
    foreach ($clave in $nuevos.Keys) { $porApartado[(Apartado $clave)] = $true }
    foreach ($clave in $Quitar) { $porApartado[(Apartado $clave)] = $true }

    foreach ($a in $porApartado.Keys) {
        foreach ($i in $idiomas) {
            $ruta = Fichero $a $i
            $doc = Abrir $ruta
            $raiz = $doc.DocumentElement

            foreach ($clave in $Quitar) {
                if ((Apartado $clave) -ne $a) { continue }
                $viejo = $raiz.SelectSingleNode("data[@name='$clave']")
                if ($viejo) { [void]$raiz.RemoveChild($viejo) }
            }

            foreach ($clave in $nuevos.Keys) {
                if ((Apartado $clave) -ne $a) { continue }
                $viejo = $raiz.SelectSingleNode("data[@name='$clave']")
                if ($viejo) { [void]$raiz.RemoveChild($viejo) }

                $data = $doc.CreateElement('data')
                $data.SetAttribute('name', $clave)
                [void]$data.SetAttribute('space', 'http://www.w3.org/XML/1998/namespace', 'preserve')
                $valor = $doc.CreateElement('value')
                $valor.InnerText = $nuevos[$clave][$i]
                [void]$data.AppendChild($valor)
                if ($i -eq 'es' -and $nuevos[$clave]['nota']) {
                    $nota = $doc.CreateElement('comment')
                    $nota.InnerText = $nuevos[$clave]['nota']
                    [void]$data.AppendChild($nota)
                }
                [void]$raiz.AppendChild($data)
            }

            Guardar $doc $ruta
        }
    }
}
finally {
    $cerrojo.ReleaseMutex()
    $cerrojo.Dispose()
}

Write-Host "Escritas $($nuevos.Count) claves y quitadas $($Quitar.Count) en $($porApartado.Keys -join ', ')."
