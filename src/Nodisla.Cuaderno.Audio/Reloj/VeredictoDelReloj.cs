using System.Globalization;
using Nodisla.Cuaderno.Aplicacion.Puertos;

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
                "Reloj sin comprobar.",
                "No se ha podido medir el desvío contra ningún servidor de hora, así que no se sabe si "
                    + "el módem está en hora. Comprueba la conexión a internet y vuelve a medir.",
                "sin medir");
        }

        var cuanto = Math.Abs(desvio.DesvioMs);
        var sentido = desvio.DesvioMs >= 0 ? "adelantado" : "atrasado";
        var cantidad = Escribir(cuanto);
        var paraMostrar = ParaMostrar(desvio.DesvioMs);

        if (cuanto <= opciones.MargenBuenoMs)
        {
            return new EstadoDelReloj(
                desvio,
                CalidadDelReloj.Bien,
                string.Create(CultureInfo.CurrentCulture, $"El reloj está en hora ({cantidad} {sentido})."),

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
                string.Create(CultureInfo.CurrentCulture, $"El reloj está {cantidad} {sentido}."),
                "Con este desvío FT8 empieza a decodificar peor: conviene sincronizar el reloj.",
                paraMostrar);
        }

        return new EstadoDelReloj(
            desvio,
            CalidadDelReloj.FueraDeVentana,
            string.Create(CultureInfo.CurrentCulture, $"El reloj está {cantidad} {sentido}."),
            "Con este desvío no se decodifica casi nada y, sobre todo, se transmite fuera de ventana, "
                + "molestando a los demás sin enterarse. Hay que sincronizar el reloj antes de transmitir.",
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
