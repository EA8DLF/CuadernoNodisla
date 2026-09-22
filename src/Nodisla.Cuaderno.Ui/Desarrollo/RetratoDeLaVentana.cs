using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Serilog;

namespace Nodisla.Cuaderno.Ui.Desarrollo;

/// <summary>
/// Saca una foto de la ventana desde dentro de la propia aplicacion.
/// </summary>
/// <remarks>
/// <para>
/// Existe por una razon muy concreta: <b>verificar no puede costarle al operador que le roben
/// el ordenador</b>. Capturar con las herramientas de Windows obliga a que la ventana este
/// visible —y las que sirven de verdad, encima, la traen al frente—, asi que cada comprobacion
/// le quitaba el teclado de las manos a quien estuviera trabajando. Apartar la ventana fuera
/// de la pantalla tampoco vale: Windows no compone lo que no se ve, y la foto sale en blanco.
/// </para>
/// <para>
/// Esto pinta el arbol visual de la ventana en un mapa de bits y lo guarda, sin pasar por el
/// escritorio. Da igual donde este la ventana, si esta tapada o si esta fuera de la pantalla:
/// lo que se guarda es lo que se veria.
/// </para>
/// <para>
/// Se activa con <c>CUADERNO_CAPTURA</c> —la ruta del fichero— y espera los segundos que diga
/// <c>CUADERNO_ESPERA</c> antes de disparar, para dar tiempo a que lleguen los datos que
/// tardan: los mosaicos del mapa, los indices solares o los primeros anuncios del cluster.
/// Cuando termina, cierra la aplicacion sola.
/// </para>
/// </remarks>
public static class RetratoDeLaVentana
{
    /// <summary>Puntos por pulgada con los que se pinta. Los mismos de la pantalla.</summary>
    private const double PuntosPorPulgada = 96.0;

    /// <summary>Lo que se espera por omision antes de disparar.</summary>
    private static readonly TimeSpan EsperaPorOmision = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Si se ha pedido una captura, la programa y cierra la aplicacion al terminar.
    /// </summary>
    /// <param name="ventana">Ventana que se retrata.</param>
    /// <returns>Cierto si se ha programado una captura.</returns>
    public static bool ProgramarSiSePide(Window ventana)
    {
        ArgumentNullException.ThrowIfNull(ventana);

        if (Environment.GetEnvironmentVariable("CUADERNO_CAPTURA") is not { Length: > 0 } ruta)
        {
            return false;
        }

        var espera = EsperaPorOmision;
        if (Environment.GetEnvironmentVariable("CUADERNO_ESPERA") is { Length: > 0 } segundos
            && double.TryParse(segundos, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var valor)
            && valor > 0)
        {
            espera = TimeSpan.FromSeconds(valor);
        }

        var reloj = new System.Windows.Threading.DispatcherTimer { Interval = espera };
        reloj.Tick += (_, _) =>
        {
            reloj.Stop();

            try
            {
                Guardar(ventana, ruta);
                Log.Information("Retrato de la ventana guardado en {Ruta}.", ruta);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "No se ha podido guardar el retrato de la ventana.");
            }
            finally
            {
                Application.Current?.Shutdown();
            }
        };

        reloj.Start();
        return true;
    }

    /// <summary>Pinta la ventana en un fichero PNG.</summary>
    /// <param name="ventana">Ventana que se retrata.</param>
    /// <param name="ruta">Fichero donde se guarda.</param>
    public static void Guardar(Window ventana, string ruta)
    {
        ArgumentNullException.ThrowIfNull(ventana);

        var ancho = (int)Math.Ceiling(ventana.ActualWidth);
        var alto = (int)Math.Ceiling(ventana.ActualHeight);
        if (ancho < 1 || alto < 1) return;

        var mapa = new RenderTargetBitmap(ancho, alto, PuntosPorPulgada, PuntosPorPulgada, PixelFormats.Pbgra32);
        mapa.Render(ventana);

        var codificador = new PngBitmapEncoder();
        codificador.Frames.Add(BitmapFrame.Create(mapa));

        var carpeta = Path.GetDirectoryName(Path.GetFullPath(ruta));
        if (!string.IsNullOrEmpty(carpeta)) Directory.CreateDirectory(carpeta);

        using var fichero = File.Create(ruta);
        codificador.Save(fichero);
    }
}
