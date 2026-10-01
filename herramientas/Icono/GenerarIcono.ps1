# Genera src\Nodisla.Cuaderno.Ui\Recursos\CuadernoNodisla.ico a partir del dibujo vectorial de
# src\Nodisla.Cuaderno.Ui\Recursos\Marca.xaml, para que el icono del programa, el del
# instalador y el de la cabecera de la ventana sean exactamente el mismo dibujo.
#
# Tamaños: 16, 24, 32, 48, 64 y 256. Del 16 al 64 van como mapa de bits de 32 bits (lo que
# entiende cualquier Windows y el compilador de Inno Setup); el de 256 va en PNG, como manda
# Windows desde Vista. El 16 y el 24 salen de la versión pequeña de la marca.
#
# Uso (Windows PowerShell 5.1, sin nada que instalar):
#   powershell -NoProfile -ExecutionPolicy Bypass -File herramientas\Icono\GenerarIcono.ps1
# Deja además las PNG de cada tamaño en herramientas\Icono\png\ para revisarlas a ojo.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$raiz = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$marca = Join-Path $raiz 'src\Nodisla.Cuaderno.Ui\Recursos\Marca.xaml'
$destino = Join-Path $raiz 'src\Nodisla.Cuaderno.Ui\Recursos\CuadernoNodisla.ico'
$carpetaPng = Join-Path $PSScriptRoot 'png'
New-Item -ItemType Directory -Force $carpetaPng | Out-Null

$lector = [IO.File]::OpenRead($marca)
try { $diccionario = [Windows.Markup.XamlReader]::Load($lector) } finally { $lector.Close() }

function Pintar([int]$lado) {
    $clave = if ($lado -le 24) { 'MarcaDelCuadernoPequena' } else { 'MarcaDelCuaderno' }
    $dibujo = $diccionario[$clave].Drawing
    $visual = New-Object Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()
    $dc.PushTransform((New-Object Windows.Media.ScaleTransform ($lado / 256.0), ($lado / 256.0)))
    $dc.DrawDrawing($dibujo)
    $dc.Pop()
    $dc.Close()
    $mapa = New-Object Windows.Media.Imaging.RenderTargetBitmap $lado, $lado, 96, 96, ([Windows.Media.PixelFormats]::Pbgra32)
    $mapa.Render($visual)
    # Sin premultiplicar: el .ico guarda BGRA normal.
    return New-Object Windows.Media.Imaging.FormatConvertedBitmap $mapa, ([Windows.Media.PixelFormats]::Bgra32), $null, 0
}

function Png($mapa) {
    $enc = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($mapa))
    $ms = New-Object IO.MemoryStream
    $enc.Save($ms)
    return $ms.ToArray()
}

function Dib($mapa, [int]$lado) {
    $paso = $lado * 4
    $pixeles = New-Object byte[] ($paso * $lado)
    $mapa.CopyPixels($pixeles, $paso, 0)
    $pasoMascara = [int]([Math]::Ceiling($lado / 32.0) * 4)
    $ms = New-Object IO.MemoryStream
    $w = New-Object IO.BinaryWriter $ms
    # BITMAPINFOHEADER: alto doble (imagen + máscara), 32 bits, sin comprimir.
    $w.Write([int]40); $w.Write([int]$lado); $w.Write([int]($lado * 2))
    $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]0)
    $w.Write([int]($paso * $lado + $pasoMascara * $lado))
    $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0)
    # Filas de abajo arriba.
    for ($y = $lado - 1; $y -ge 0; $y--) { $w.Write($pixeles, $y * $paso, $paso) }
    # Máscara AND: 1 donde es transparente del todo.
    for ($y = $lado - 1; $y -ge 0; $y--) {
        $fila = New-Object byte[] $pasoMascara
        for ($x = 0; $x -lt $lado; $x++) {
            if ($pixeles[$y * $paso + $x * 4 + 3] -eq 0) { $fila[[int][Math]::Floor($x / 8)] = $fila[[int][Math]::Floor($x / 8)] -bor (0x80 -shr ($x % 8)) }
        }
        $w.Write($fila)
    }
    $w.Flush()
    return $ms.ToArray()
}

$lados = 16, 24, 32, 48, 64, 256
$imagenes = @()
foreach ($lado in $lados) {
    $mapa = Pintar $lado
    [IO.File]::WriteAllBytes((Join-Path $carpetaPng "cuaderno-$lado.png"), (Png $mapa))
    $datos = if ($lado -eq 256) { Png $mapa } else { Dib $mapa $lado }
    $imagenes += , @($lado, $datos)
}

$ms = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $ms
$w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$imagenes.Count)
$desplazamiento = 6 + 16 * $imagenes.Count
foreach ($i in $imagenes) {
    $lado = $i[0]; $datos = $i[1]
    $b = if ($lado -ge 256) { 0 } else { $lado }
    $w.Write([byte]$b); $w.Write([byte]$b); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([int16]1); $w.Write([int16]32)
    $w.Write([int]$datos.Length); $w.Write([int]$desplazamiento)
    $desplazamiento += $datos.Length
}
foreach ($i in $imagenes) { $w.Write([byte[]]$i[1]) }
$w.Flush()
[IO.File]::WriteAllBytes($destino, $ms.ToArray())
Write-Output "Icono escrito en $destino ($($ms.Length) bytes, lados $($lados -join ', '))."
