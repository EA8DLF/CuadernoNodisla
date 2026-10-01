using Nodisla.Cuaderno.Modos.Senal;
using Nodisla.Cuaderno.Modos.Tablas;

namespace Nodisla.Cuaderno.Modos.Wspr;

/// <summary>
/// Una candidata a senal de WSPR, tal y como sale de la busqueda gruesa.
/// </summary>
/// <param name="DesplazamientoHz">Frecuencia respecto al centro de la banda (1500 Hz).</param>
/// <param name="ComienzoSegundos">Instante de comienzo respecto a la primera muestra de la ventana.</param>
/// <param name="DerivaHz">Cuanto sube o baja la frecuencia a lo largo de la transmision.</param>
/// <param name="Sincronismo">Parecido con el vector de sincronismo, de -1 a 1.</param>
public readonly record struct CandidataWspr(double DesplazamientoHz, double ComienzoSegundos, double DerivaHz, double Sincronismo);

/// <summary>
/// Busca en la banda base las senales que se parecen a WSPR: la busqueda gruesa.
/// </summary>
/// <remarks>
/// <para>
/// Se calcula un espectrograma de la banda base con bloques de un simbolo, rellenados con
/// ceros al doble para que las casillas caigan a media separacion de tonos, y avanzando medio
/// simbolo por columna. Con eso, los cuatro tonos de una senal caen en casillas separadas de
/// dos en dos, y un simbolo en columnas de dos en dos.
/// </para>
/// <para>
/// Para cada frecuencia, comienzo y deriva posibles se correla el espectrograma con el vector
/// de sincronismo: se suma la potencia de los tonos que deberian llevar un uno de sincronismo
/// (tonos 1 y 3) y se resta la de los que deberian llevar un cero (tonos 0 y 2), con el signo
/// que toque en cada simbolo. Una senal de verdad da una suma grande; el ruido, una suma que
/// se anula. La cuenta se divide por la potencia total de los cuatro tonos para que el
/// resultado no dependa del volumen.
/// </para>
/// <para>
/// Lo que sale son las mejores frecuencias, cada una con su mejor comienzo y su mejor deriva,
/// para que la busqueda fina las afine.
/// </para>
/// </remarks>
public static class SincronizadorWspr
{
    private const int Bloque = ParametrosWspr.MuestrasPorSimboloDeBandaBase;
    private const int Paso = Bloque / 2;
    private const int Transformada = 2 * Bloque;

    /// <summary>Hercios por casilla del espectrograma: media separacion de tonos.</summary>
    public const double HzPorCasilla = ParametrosWspr.FrecuenciaDeBandaBase / Transformada;

    /// <summary>Primera columna del espectrograma en la que puede empezar una senal.</summary>
    public const int PrimeraColumna = -4;

    /// <summary>Ultima columna del espectrograma en la que puede empezar una senal.</summary>
    public const int UltimaColumna = 14;

    /// <summary>Deriva maxima que se busca, en casillas del espectrograma a lo largo de la transmision.</summary>
    public const int DerivaMaximaEnCasillas = 5;

    /// <summary>
    /// Busca candidatas.
    /// </summary>
    /// <param name="bandaBase">Banda base a 375 muestras por segundo.</param>
    /// <param name="muestrasUtiles">Cuantas muestras de la banda base son de la ventana de verdad.</param>
    /// <param name="cuantas">Maximo de candidatas que se devuelven.</param>
    /// <param name="sincronismoMinimo">Por debajo de este parecido no se devuelve nada.</param>
    /// <returns>Las candidatas, de mejor a peor sincronismo.</returns>
    public static List<CandidataWspr> Buscar(SenalCompleja bandaBase, int muestrasUtiles, int cuantas, double sincronismoMinimo)
    {
        ArgumentNullException.ThrowIfNull(bandaBase);
        var n = Math.Min(muestrasUtiles, bandaBase.Longitud);
        var columnas = ((n - Bloque) / Paso) + 1;
        if (columnas < ParametrosWspr.Simbolos) return [];

        // Espectrograma de potencia, con las casillas giradas para que la de en medio sea la
        // frecuencia cero.
        var potencia = new float[columnas][];
        var real = new float[Transformada];
        var imaginaria = new float[Transformada];
        for (var c = 0; c < columnas; c++)
        {
            Array.Clear(real);
            Array.Clear(imaginaria);
            bandaBase.Real.AsSpan(c * Paso, Bloque).CopyTo(real);
            bandaBase.Imaginaria.AsSpan(c * Paso, Bloque).CopyTo(imaginaria);
            Fft.Transformar(real, imaginaria);
            var fila = new float[Transformada];
            for (var k = 0; k < Transformada; k++)
            {
                var m = (k + (Transformada / 2)) % Transformada;
                fila[m] = (real[k] * real[k]) + (imaginaria[k] * imaginaria[k]);
            }
            potencia[c] = fila;
        }

        // Para cada columna y casilla, lo que aportaria al sincronismo (tonos 1 y 3 menos 0 y 2)
        // y la potencia de los cuatro tonos, que es lo que normaliza.
        var diferencia = new float[columnas][];
        var total = new float[columnas][];
        for (var c = 0; c < columnas; c++)
        {
            var d = new float[Transformada];
            var t = new float[Transformada];
            var fila = potencia[c];
            for (var m = 3; m < Transformada - 3; m++)
            {
                d[m] = fila[m - 1] + fila[m + 3] - fila[m - 3] - fila[m + 1];
                t[m] = fila[m - 3] + fila[m - 1] + fila[m + 1] + fila[m + 3];
            }
            diferencia[c] = d;
            total[c] = t;
        }

        var sincronismo = TablasWspr.VectorDeSincronismo;
        var signo = new int[ParametrosWspr.Simbolos];
        for (var k = 0; k < signo.Length; k++) signo[k] = sincronismo[k] == 1 ? 1 : -1;

        var casillasDeBusqueda = (int)Math.Round(ParametrosWspr.MediaAnchuraDeBusquedaHz / HzPorCasilla);
        var centro = Transformada / 2;

        // Desplazamiento en casillas de cada simbolo para cada deriva, calculado una vez.
        var derivas = (2 * DerivaMaximaEnCasillas) + 1;
        var desplazamientoPorDeriva = new int[derivas][];
        for (var i = 0; i < derivas; i++)
        {
            var deriva = i - DerivaMaximaEnCasillas;
            var v = new int[ParametrosWspr.Simbolos];
            for (var k = 0; k < v.Length; k++)
                v[k] = (int)Math.Round(deriva * (k - ((ParametrosWspr.Simbolos - 1) / 2.0)) / (ParametrosWspr.Simbolos - 1));
            desplazamientoPorDeriva[i] = v;
        }

        var mejores = new List<CandidataWspr>();
        var mejorPorCasilla = new (double Puntuacion, int Columna, int Deriva)[Transformada];
        for (var m = centro - casillasDeBusqueda; m <= centro + casillasDeBusqueda; m++)
        {
            var mejor = (Puntuacion: double.NegativeInfinity, Columna: 0, Deriva: 0);
            for (var columna0 = PrimeraColumna; columna0 <= UltimaColumna; columna0++)
            {
                for (var i = 0; i < derivas; i++)
                {
                    var desplazamiento = desplazamientoPorDeriva[i];
                    double suma = 0, potenciaTotal = 0;
                    for (var k = 0; k < ParametrosWspr.Simbolos; k++)
                    {
                        var c = columna0 + (2 * k);
                        if (c < 0 || c >= columnas) continue;
                        var casilla = m + desplazamiento[k];
                        suma += signo[k] * diferencia[c][casilla];
                        potenciaTotal += total[c][casilla];
                    }
                    if (potenciaTotal <= 0) continue;
                    var puntuacion = suma / potenciaTotal;
                    if (puntuacion > mejor.Puntuacion) mejor = (puntuacion, columna0, i - DerivaMaximaEnCasillas);
                }
            }
            mejorPorCasilla[m] = mejor;
        }

        // De mejor a peor, sin repetir la misma senal desde una casilla vecina.
        var orden = Enumerable.Range(centro - casillasDeBusqueda, (2 * casillasDeBusqueda) + 1)
            .OrderByDescending(m => mejorPorCasilla[m].Puntuacion)
            .ToList();
        var tomadas = new List<int>();
        foreach (var m in orden)
        {
            var (puntuacion, columna, deriva) = mejorPorCasilla[m];
            if (puntuacion < sincronismoMinimo || double.IsNegativeInfinity(puntuacion)) break;
            if (tomadas.Any(t => Math.Abs(t - m) <= 2)) continue;
            tomadas.Add(m);
            mejores.Add(new CandidataWspr(
                DesplazamientoHz: (m - centro) * HzPorCasilla,
                ComienzoSegundos: columna * Paso / ParametrosWspr.FrecuenciaDeBandaBase,
                DerivaHz: deriva * HzPorCasilla,
                Sincronismo: puntuacion));
            if (mejores.Count >= cuantas) break;
        }
        return mejores;
    }
}
