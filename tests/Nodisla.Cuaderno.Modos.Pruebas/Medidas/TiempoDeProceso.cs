using System.Diagnostics;

namespace Nodisla.Cuaderno.Modos.Pruebas.Medidas;

/// <summary>
/// Mide lo que cuesta un trozo de codigo en <b>tiempo de procesador</b>, no en tiempo de reloj.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que no vale el reloj de pared.</b> Cronometrar con un reloj normal no mide lo que
/// cuesta el codigo: mide lo que cuesta el codigo <i>mas todo lo que la maquina estuviera
/// haciendo a la vez</i>. Cuando las pruebas de once proyectos corren en paralelo, una misma
/// ventana puede tardar cuatrocientos milisegundos o cinco segundos sin que el modem haya
/// cambiado en nada. Una prueba asi no falla cuando hay una regresion: falla cuando la maquina
/// esta ocupada, que es otra cosa y ademas pasa a destiempo.
/// </para>
/// <para>
/// <b>Que mide el tiempo de procesador.</b> Los ciclos que el sistema operativo apunta de
/// verdad a nuestro proceso. Si otro programa nos quita el turno, el reloj de pared se estira
/// pero aqui no se suma ni un ciclo: lo que se cuenta es trabajo hecho, no espera. Y como sale
/// en milisegundos, el presupuesto sigue significando algo frente a los quince segundos de
/// hueco que da FT8.
/// </para>
/// <para>
/// <b>Por que el mejor de varios pases y no la media.</b> Dentro del propio proceso de pruebas
/// puede haber otras clases trabajando, y su tiempo si cuenta aqui. Esa contaminacion solo
/// puede hacer que un pase salga mas caro, nunca mas barato, asi que el pase mas barato de
/// todos es la estimacion honesta de lo que cuesta el codigo a solas.
/// </para>
/// </remarks>
public static class TiempoDeProceso
{
    /// <summary>
    /// Ejecuta algo varias veces y devuelve lo que costo el pase mas barato.
    /// </summary>
    /// <param name="pases">Cuantas veces repetirlo.</param>
    /// <param name="trabajo">Lo que hay que medir.</param>
    /// <returns>Milisegundos de procesador del mejor pase.</returns>
    public static double MejorDe(int pases, Action trabajo)
    {
        ArgumentNullException.ThrowIfNull(trabajo);
        if (pases < 1) throw new ArgumentOutOfRangeException(nameof(pases));

        var mejor = double.MaxValue;
        for (var i = 0; i < pases; i++)
        {
            var antes = Ahora();
            trabajo();
            var coste = Ahora() - antes;
            if (coste < mejor) mejor = coste;
        }
        return mejor;
    }

    /// <summary>Mide lo que cuesta una sola ejecucion, en milisegundos de procesador.</summary>
    /// <param name="trabajo">Lo que hay que medir.</param>
    public static double De(Action trabajo)
    {
        ArgumentNullException.ThrowIfNull(trabajo);
        var antes = Ahora();
        trabajo();
        return Ahora() - antes;
    }

    /// <summary>Milisegundos de procesador gastados por este proceso desde que arranco.</summary>
    private static double Ahora()
    {
        // Se relee el proceso cada vez a proposito: el objeto guarda en cache los contadores y
        // reutilizarlo devolveria siempre la misma cifra.
        using var proceso = Process.GetCurrentProcess();
        return proceso.TotalProcessorTime.TotalMilliseconds;
    }
}
