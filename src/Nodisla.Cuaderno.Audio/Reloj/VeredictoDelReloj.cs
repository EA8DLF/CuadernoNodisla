using System.Globalization;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Audio.Reloj;

/// <summary>
/// Pone en palabras lo que dice el numero del desvio.
/// </summary>
/// <remarks>
/// <para>
/// Esta es la regla de cuantos milisegundos son demasiados, y vive aqui a proposito: es del
/// reloj, no de la pantalla. La ventana se limita a pintar lo que salga de aqui.
/// </para>
/// <para>
/// Los tres escalones no son gustos. Por debajo del margen bueno FT8 va fino; entre ahi y el
/// margen de fuera de ventana se decodifica peor, que es un problema de uno mismo; pasado ese,
/// se <b>transmite fuera de ventana</b>, y eso ya es un problema de los demas.
/// </para>
/// </remarks>
public static class VeredictoDelReloj
{
    /// <summary>
    /// Compone el estado del reloj a partir de una medida y de los umbrales configurados.
    /// </summary>
    /// <param name="desvio">La medida.</param>
    /// <param name="opciones">De donde salen los umbrales.</param>
    /// <returns>El estado, listo para pintar sin calcular nada.</returns>
    /// <exception cref="ArgumentNullException">Si falta la medida o las opciones.</exception>
    public static EstadoDelReloj Componer(DesvioDelReloj desvio, OpcionesDelReloj opciones)
    {
        ArgumentNullException.ThrowIfNull(desvio);
        ArgumentNullException.ThrowIfNull(opciones);

        if (!desvio.EsFiable)
        {
            return new EstadoDelReloj(
                desvio,
                CalidadDelReloj.SinMedir,
                Textos.T("Servicios.Audio.Reloj.SinComprobar"),
                Textos.T("Servicios.Audio.Reloj.SinComprobarTexto"),
                Textos.T("Servicios.Audio.Reloj.SinMedir"));
        }

        var cuanto = Math.Abs(desvio.DesvioMs);
        var sentido = desvio.DesvioMs >= 0 ? Textos.T("Servicios.Audio.Reloj.Adelantado") : Textos.T("Servicios.Audio.Reloj.Atrasado");
        var cantidad = Escribir(cuanto);
        var paraMostrar = ParaMostrar(desvio.DesvioMs);

        if (cuanto <= opciones.MargenBuenoMs)
        {
            return new EstadoDelReloj(
                desvio,
                CalidadDelReloj.Bien,
                Textos.F("Servicios.Audio.Reloj.EnHora", cantidad, sentido),

                // Vacio: no hay nada que hacer, y decirlo con una frase solo invita a buscarle
                // tres pies al gato.
                string.Empty,
                paraMostrar);
        }

        if (cuanto < opciones.MargenFueraDeVentanaMs)
        {
            return new EstadoDelReloj(
                desvio,
                CalidadDelReloj.Regular,
                Textos.F("Servicios.Audio.Reloj.Desviado", cantidad, sentido),
                Textos.T("Servicios.Audio.Reloj.Regular"),
                paraMostrar);
        }

        return new EstadoDelReloj(
            desvio,
            CalidadDelReloj.FueraDeVentana,
            Textos.F("Servicios.Audio.Reloj.Desviado", cantidad, sentido),
            Textos.T("Servicios.Audio.Reloj.FueraDeVentana"),
            paraMostrar);
    }

    /// <summary>El desvio con su signo, para ensenarlo tal cual.</summary>
    /// <param name="desvioMs">Desvio en milisegundos.</param>
    /// <returns>Algo como «-1,29 s» o «+350 ms».</returns>
    public static string ParaMostrar(double desvioMs) => Math.Abs(desvioMs) >= 1000
        ? string.Create(CultureInfo.CurrentCulture, $"{desvioMs / 1000.0:+0.00;-0.00} s")
        : string.Create(CultureInfo.CurrentCulture, $"{desvioMs:+0;-0} ms");

    /// <summary>Una cantidad de tiempo sin signo, en la unidad que se lea mejor.</summary>
    private static string Escribir(double cuantoMs) => cuantoMs >= 1000
        ? string.Create(CultureInfo.CurrentCulture, $"{cuantoMs / 1000.0:0.00} s")
        : string.Create(CultureInfo.CurrentCulture, $"{cuantoMs:0} ms");
}
