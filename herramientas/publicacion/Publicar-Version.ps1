<#
.SYNOPSIS
    Saca una version de Cuaderno NODISLA: publica, compila el instalador, calcula su SHA-256 y,
    con -Subir, crea la release en GitHub con los dos ficheros adjuntos.

.DESCRIPTION
    El aviso de versiones del programa consulta releases/latest de EA8DLF/CuadernoNodisla y
    solo ofrece «Descargar e instalar» si la release trae:
      - CuadernoNodisla-Instalador-<version>.exe
      - CuadernoNodisla-Instalador-<version>.exe.sha256   (formato sha256sum: "suma  nombre")
    Sin el .sha256 el programa NO instala nada. Este script deja los dos juntos.

    La version sale de <Version> en Directory.Build.props; la etiqueta es v<Version>.
    Sin -Subir solo genera los ficheros en instalador\salida y no toca GitHub.

.PARAMETER Subir
    Crea la release v<Version> en GitHub (o adjunta a la existente) con gh. Requiere
    'gh auth login' hecho. La ficha de acceso es la de gh en este PC: nunca va en el programa.

.PARAMETER Notas
    Fichero Markdown con las notas de la version (lo que el programa ensena en el aviso).

.PARAMETER SinCompilar
    Reutiliza el instalador ya compilado en instalador\salida.

.EXAMPLE
    .\herramientas\publicacion\Publicar-Version.ps1
    .\herramientas\publicacion\Publicar-Version.ps1 -Subir -Notas .\notas-0.2.0.md
#>
[CmdletBinding()]
param(
    [switch] $Subir,
    [string] $Notas,
    [switch] $SinCompilar,
    [string] $Repositorio = 'EA8DLF/CuadernoNodisla',
    [string] $Iscc = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
)

$ErrorActionPreference = 'Stop'
$raiz = Resolve-Path (Join-Path $PSScriptRoot '..\..')
Set-Location $raiz

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

# ── Publicar y compilar el instalador ─────────────────────────────────
if (-not $SinCompilar) {
    $publicar = Join-Path $raiz 'instalador\publicar\win-x64'
    if (Test-Path $publicar) { Remove-Item $publicar -Recurse -Force }
    dotnet publish 'src\Nodisla.Cuaderno.Ui\Nodisla.Cuaderno.Ui.csproj' -c Release -r win-x64 --self-contained true -o $publicar
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish ha fallado.' }

    if (-not (Test-Path $Iscc)) { throw "No se encuentra Inno Setup en $Iscc." }
    & $Iscc 'instalador\CuadernoNodisla.iss'
    if ($LASTEXITCODE -ne 0) { throw 'ISCC ha fallado.' }
}
if (-not (Test-Path $instalador)) { throw "No esta $instalador. ¿Coincide la version del .exe publicado con Directory.Build.props?" }

# ── Suma SHA-256 (formato sha256sum, sin BOM) ──────────────────────────
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
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'Falta GitHub CLI (gh).' }

# "gh" escribe en stderr cuando la release no existe (caso normal la primera vez). Con
# $ErrorActionPreference = 'Stop' a nivel de script, redirigir ese stderr lo convierte en un
# NativeCommandError que para el script en vez de dejar mirar $LASTEXITCODE: se baja la
# severidad solo para esta llamada.
$anterior = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
gh release view $etiqueta --repo $Repositorio *> $null
$ErrorActionPreference = $anterior
if ($LASTEXITCODE -eq 0) {
    Write-Host "La release $etiqueta ya existe: se reemplazan los adjuntos."
    gh release upload $etiqueta $instalador $ficheroSuma --repo $Repositorio --clobber
}
else {
    $argumentos = @('release', 'create', $etiqueta, $instalador, $ficheroSuma, '--repo', $Repositorio, '--title', "Cuaderno NODISLA $version")
    if ($Notas) { $argumentos += @('--notes-file', $Notas) } else { $argumentos += @('--generate-notes') }
    if ($version.Contains('-')) { $argumentos += '--prerelease' }
    gh @argumentos
}
if ($LASTEXITCODE -ne 0) { throw 'gh ha fallado.' }
Write-Host "Publicada $etiqueta en https://github.com/$Repositorio/releases/tag/$etiqueta"
