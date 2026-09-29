using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Nodisla.Cuaderno.Ui.Recursos;

/// <summary>Lo que se averigua al comprobar si la ventana se esta pintando de verdad.</summary>
public enum EstadoDelDibujado
{
    /// <summary>La ventana pinta lo que se le ha pedido.</summary>
    Correcto,
    /// <summary>La ventana existe pero sale en blanco: el dibujado no esta funcionando.</summary>
    EnBlanco,
    /// <summary>No se ha podido comprobar; se da por bueno para no molestar al operador.</summary>
    NoSeHaPodidoComprobar,
}

/// <summary>
/// Comprueba que la ventana se pinta realmente, en lugar de suponerlo.
/// </summary>
/// <remarks>
/// Hay equipos y sesiones donde WPF dibuja por hardware y la ventana sale entera en blanco:
/// el marco lo pinta Windows, pero el contenido no llega nunca. Preguntar por el nivel de
/// dibujado o por si la sesion es remota no lo detecta —puede dar «todo correcto» y salir en
/// blanco igualmente—, asi que aqui se mira el resultado: se le pide a Windows una copia de lo
/// que la ventana ha pintado y se compara con el color de fondo que deberia tener. Si no
/// coincide, quien llama pasa a dibujado por software, que siempre pinta.
/// </remarks>
public static class ComprobacionDeDibujado
{
    /// <summary>Diferencia por canal que se admite entre lo pintado y lo esperado.</summary>
    private const int Tolerancia = 6;

    /// <summary>Distancia al borde donde se mira: ahi se ve el fondo de la ventana, sin tarjetas.</summary>
    private const double MargenDeMuestra = 4.0;

    /// <summary>Comprueba si la ventana esta pintando su color de fondo.</summary>
    public static EstadoDelDibujado Comprobar(Window ventana)
    {
        ArgumentNullException.ThrowIfNull(ventana);

        if (ventana.Background is not SolidColorBrush pincel) return EstadoDelDibujado.NoSeHaPodidoComprobar;
        if (ventana.ActualWidth <= 0 || ventana.ActualHeight <= 0) return EstadoDelDibujado.NoSeHaPodidoComprobar;

        var ventanaNativa = new WindowInteropHelper(ventana).Handle;
        if (ventanaNativa == IntPtr.Zero) return EstadoDelDibujado.NoSeHaPodidoComprobar;
        if (!GetWindowRect(ventanaNativa, out var marco)) return EstadoDelDibujado.NoSeHaPodidoComprobar;

        var ancho = marco.Right - marco.Left;
        var alto = marco.Bottom - marco.Top;
        if (ancho <= 0 || alto <= 0) return EstadoDelDibujado.NoSeHaPodidoComprobar;

        var puntos = PuntosDeMuestra(ventana, marco);
        if (puntos.Count == 0) return EstadoDelDibujado.NoSeHaPodidoComprobar;

        var hdcPantalla = GetDC(IntPtr.Zero);
        if (hdcPantalla == IntPtr.Zero) return EstadoDelDibujado.NoSeHaPodidoComprobar;

        var hdcMemoria = IntPtr.Zero;
        var mapaDeBits = IntPtr.Zero;
        var anterior = IntPtr.Zero;

        try
        {
            hdcMemoria = CreateCompatibleDC(hdcPantalla);
            if (hdcMemoria == IntPtr.Zero) return EstadoDelDibujado.NoSeHaPodidoComprobar;

            mapaDeBits = CreateCompatibleBitmap(hdcPantalla, ancho, alto);
            if (mapaDeBits == IntPtr.Zero) return EstadoDelDibujado.NoSeHaPodidoComprobar;

            anterior = SelectObject(hdcMemoria, mapaDeBits);

            // PW_RENDERFULLCONTENT: pide el contenido tal y como lo compone Windows.
            if (!PrintWindow(ventanaNativa, hdcMemoria, 2)) return EstadoDelDibujado.NoSeHaPodidoComprobar;

            var aciertos = 0;
            var leidos = 0;
            foreach (var (x, y) in puntos)
            {
                if (x < 0 || y < 0 || x >= ancho || y >= alto) continue;

                var color = GetPixel(hdcMemoria, x, y);
                if (color == ColorInvalido) continue;

                leidos++;
                if (SeParece(color, pincel.Color)) aciertos++;
            }

            if (leidos == 0) return EstadoDelDibujado.NoSeHaPodidoComprobar;
            return aciertos > 0 ? EstadoDelDibujado.Correcto : EstadoDelDibujado.EnBlanco;
        }
        finally
        {
            if (hdcMemoria != IntPtr.Zero && anterior != IntPtr.Zero) SelectObject(hdcMemoria, anterior);
            if (mapaDeBits != IntPtr.Zero) DeleteObject(mapaDeBits);
            if (hdcMemoria != IntPtr.Zero) DeleteDC(hdcMemoria);
            ReleaseDC(IntPtr.Zero, hdcPantalla);
        }
    }

    /// <summary>Pasa la aplicacion a dibujado por software y vuelve a crear el destino de dibujo.</summary>
    public static void PasarADibujadoPorSoftware(Window ventana)
    {
        ArgumentNullException.ThrowIfNull(ventana);

        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

        // Esconder y volver a mostrar obliga a WPF a rehacer el destino de dibujo con el modo nuevo.
        var estaba = ventana.WindowState;
        ventana.Hide();
        ventana.Show();
        ventana.WindowState = estaba;
    }

    /// <summary>
    /// Tres puntos del margen exterior de la ventana, donde siempre se ve el color de fondo
    /// y nunca una tarjeta, un campo ni una fila del cuaderno.
    /// </summary>
    private static List<(int X, int Y)> PuntosDeMuestra(Window ventana, Rect marco)
    {
        var puntos = new List<(int, int)>(3);
        double[] alturas = [0.25, 0.5, 0.75];

        foreach (var proporcion in alturas)
        {
            try
            {
                var enPantalla = ventana.PointToScreen(
                    new Point(MargenDeMuestra, ventana.ActualHeight * proporcion));
                puntos.Add(((int)Math.Round(enPantalla.X) - marco.Left, (int)Math.Round(enPantalla.Y) - marco.Top));
            }
            catch (InvalidOperationException)
            {
                // La ventana aun no tiene origen en pantalla; se prueba con el punto siguiente.
            }
        }

        return puntos;
    }

    private static bool SeParece(uint colorNativo, Color esperado)
    {
        // COLORREF viene como 0x00BBGGRR.
        var r = (int)(colorNativo & 0xFF);
        var g = (int)((colorNativo >> 8) & 0xFF);
        var b = (int)((colorNativo >> 16) & 0xFF);

        return Math.Abs(r - esperado.R) <= Tolerancia
            && Math.Abs(g - esperado.G) <= Tolerancia
            && Math.Abs(b - esperado.B) <= Tolerancia;
    }

    private const uint ColorInvalido = 0xFFFFFFFF;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr ventana, out Rect marco);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(IntPtr ventana, IntPtr contexto, uint opciones);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr ventana);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr ventana, IntPtr contexto);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr contexto);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr contexto, int ancho, int alto);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr contexto, IntPtr objeto);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr objeto);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr contexto);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(IntPtr contexto, int x, int y);
}
