using Nodisla.Cuaderno.Modos.Tablas;

namespace Nodisla.Cuaderno.Modos.Wspr;

/// <summary>
/// Lo que se saca de mirar una candidata de cerca.
/// </summary>
/// <param name="DesplazamientoHz">Frecuencia afinada respecto al centro de la senal estrecha.</param>
/// <param name="ComienzoMuestras">Muestra de la senal estrecha en la que empieza el primer simbolo.</param>
/// <param name="DerivaHz">Deriva afinada a lo largo de la transmision.</param>
/// <param name="Sincronismo">Parecido con el vector de sincronismo, de -1 a 1.</param>
/// <param name="PotenciaDeCadaTono">Potencia de los cuatro tonos en cada simbolo, 162 por 4.</param>
/// <param name="SimbolosPresentes">Cuantos de los 162 simbolos cayeron dentro del audio.</param>
public sealed record MedidaWspr(
    double DesplazamientoHz,
    int ComienzoMuestras,
    double DerivaHz,
    double Sincronismo,
    float[,] PotenciaDeCadaTono,
    int SimbolosPresentes)
{
    /// <summary>
    /// Valores blandos de los 162 bits de datos, en el orden de los simbolos emitidos.
    /// </summary>
    /// <remarks>
    /// Para cada simbolo se sabe cual es su bit de sincronismo, asi que solo compiten los dos
    /// tonos que lo llevan: el del dato uno menos el del dato cero. Se dividen por su valor
    /// eficaz para que no dependan del volumen, se multiplican por la ganancia y se centran en
    /// 128, que es la escala en la que trabaja el decodificador de Fano.
    /// </remarks>
    /// <param name="ganancia">Cuanto vale, en la escala de 0 a 255, una desviacion tipica.</param>
    public byte[] ValoresBlandos(double ganancia)
    {
        var y = ValoresCrudos();
        double suma = 0;
        foreach (var v in y) suma += v * v;
        var eficaz = Math.Sqrt(suma / y.Length);
        var blandos = new byte[y.Length];
        for (var k = 0; k < y.Length; k++)
        {
            var z = eficaz > 0 ? y[k] / eficaz : 0;
            var v = 128 + (int)Math.Round(z * ganancia);
            blandos[k] = (byte)Math.Clamp(v, 0, 255);
        }
        return blandos;
    }

    /// <summary>Diferencia de potencia entre el tono del dato uno y el del dato cero, por simbolo.</summary>
    public double[] ValoresCrudos()
    {
        var sincronismo = TablasWspr.VectorDeSincronismo;
        var y = new double[ParametrosWspr.Simbolos];
        for (var k = 0; k < y.Length; k++)
        {
            var s = sincronismo[k];
            y[k] = PotenciaDeCadaTono[k, s + 2] - PotenciaDeCadaTono[k, s];
        }
        return y;
    }
}

/// <summary>
/// Afina una candidata y mide la potencia de sus cuatro tonos simbolo a simbolo.
/// </summary>
/// <remarks>
/// <para>
/// Trabaja sobre la senal estrecha de la candidata, a 23,4375 muestras por segundo, donde un
/// simbolo son dieciseis muestras y cada tono se mide correlando esas dieciseis muestras con
/// una exponencial compleja a su frecuencia exacta. Como los tonos estan separados justo la
/// inversa de la duracion del simbolo, son ortogonales: la potencia de uno no se cuela en los
/// otros mientras la frecuencia este bien afinada.
/// </para>
/// <para>
/// Afinar es buscar la frecuencia, el comienzo y la deriva que maximizan el parecido con el
/// vector de sincronismo. Se hace por coordenadas —primero comienzo y frecuencia juntos, luego
/// deriva, luego frecuencia fina, luego comienzo fino— porque barrer las tres a la vez costaria
/// cien veces mas y no encuentra nada mejor: la funcion es suave y tiene un solo pico alrededor
/// de la candidata gruesa.
/// </para>
/// </remarks>
public sealed class DemoduladorWspr
{
    private const int Muestras = ParametrosWspr.MuestrasPorSimboloEstrecho;
    private const double Fs = ParametrosWspr.FrecuenciaEstrecha;

    private readonly SenalCompleja _senal;
    private readonly int _muestrasUtiles;
    private readonly int[] _signo;

    /// <summary>Crea el demodulador sobre la senal estrecha de una candidata.</summary>
    /// <param name="senal">Senal estrecha a 23,4375 muestras por segundo.</param>
    /// <param name="muestrasUtiles">Cuantas muestras son de la ventana de verdad.</param>
    public DemoduladorWspr(SenalCompleja senal, int muestrasUtiles)
    {
        ArgumentNullException.ThrowIfNull(senal);
        _senal = senal;
        _muestrasUtiles = Math.Min(muestrasUtiles, senal.Longitud);
        _signo = new int[ParametrosWspr.Simbolos];
        var sincronismo = TablasWspr.VectorDeSincronismo;
        for (var k = 0; k < _signo.Length; k++) _signo[k] = sincronismo[k] == 1 ? 1 : -1;
    }

    /// <summary>
    /// Afina la candidata partiendo de los valores gruesos.
    /// </summary>
    /// <param name="desplazamientoHz">Frecuencia gruesa respecto al centro de la senal estrecha.</param>
    /// <param name="comienzoMuestras">Muestra gruesa de comienzo.</param>
    /// <param name="derivaHz">Deriva gruesa.</param>
    public MedidaWspr Afinar(double desplazamientoHz, int comienzoMuestras, double derivaHz)
    {
        var mejor = Medir(desplazamientoHz, comienzoMuestras, derivaHz);

        // Comienzo y frecuencia a la vez, en pasos de una muestra (43 ms) y un sexto de tono.
        for (var dt = -8; dt <= 8; dt++)
            for (var df = -3; df <= 3; df++)
                mejor = Mejor(mejor, desplazamientoHz + (df * 0.25), comienzoMuestras + dt, derivaHz);

        // Deriva.
        var f1 = mejor.DesplazamientoHz;
        var t1 = mejor.ComienzoMuestras;
        for (var dd = -4; dd <= 4; dd++)
            mejor = Mejor(mejor, f1, t1, derivaHz + (dd * 0.25));

        // Frecuencia fina, en pasos de 0,05 Hz.
        var d2 = mejor.DerivaHz;
        for (var df = -6; df <= 6; df++)
            mejor = Mejor(mejor, f1 + (df * 0.05), t1, d2);

        // Comienzo fino y una ultima pasada de deriva fina.
        var f3 = mejor.DesplazamientoHz;
        for (var dt = -2; dt <= 2; dt++)
            mejor = Mejor(mejor, f3, t1 + dt, d2);
        var t3 = mejor.ComienzoMuestras;
        for (var dd = -3; dd <= 3; dd++)
            mejor = Mejor(mejor, f3, t3, d2 + (dd * 0.08));

        return mejor;
    }

    private MedidaWspr Mejor(MedidaWspr actual, double f, int t, double d)
    {
        var otra = Medir(f, t, d);
        return otra.Sincronismo > actual.Sincronismo ? otra : actual;
    }

    /// <summary>
    /// Mide los cuatro tonos de cada simbolo para unos parametros dados.
    /// </summary>
    /// <param name="desplazamientoHz">Frecuencia del centro de los tonos respecto al centro de la senal.</param>
    /// <param name="comienzoMuestras">Muestra en la que empieza el primer simbolo.</param>
    /// <param name="derivaHz">Deriva a lo largo de la transmision.</param>
    public MedidaWspr Medir(double desplazamientoHz, int comienzoMuestras, double derivaHz)
    {
        var potencia = new float[ParametrosWspr.Simbolos, 4];
        var real = _senal.Real;
        var imaginaria = _senal.Imaginaria;
        var mitad = (ParametrosWspr.Simbolos - 1) / 2.0;
        var presentes = 0;
        double suma = 0, total = 0;

        for (var k = 0; k < ParametrosWspr.Simbolos; k++)
        {
            var n0 = comienzoMuestras + (k * Muestras);
            if (n0 < 0 || n0 + Muestras > _muestrasUtiles) continue;
            presentes++;

            var frecuenciaDelSimbolo = desplazamientoHz + (derivaHz * (k - mitad) / (ParametrosWspr.Simbolos - 1));
            for (var tono = 0; tono < 4; tono++)
            {
                var frecuencia = frecuenciaDelSimbolo + ((tono - 1.5) * ParametrosWspr.EspaciadoDeTonosHz);
                var angulo = -2 * Math.PI * frecuencia / Fs;
                var pasoReal = Math.Cos(angulo);
                var pasoImaginaria = Math.Sin(angulo);
                double giroReal = 1, giroImaginaria = 0;
                double sumaReal = 0, sumaImaginaria = 0;
                for (var i = 0; i < Muestras; i++)
                {
                    var xr = real[n0 + i];
                    var xi = imaginaria[n0 + i];
                    sumaReal += (xr * giroReal) - (xi * giroImaginaria);
                    sumaImaginaria += (xr * giroImaginaria) + (xi * giroReal);
                    var siguiente = (giroReal * pasoReal) - (giroImaginaria * pasoImaginaria);
                    giroImaginaria = (giroReal * pasoImaginaria) + (giroImaginaria * pasoReal);
                    giroReal = siguiente;
                }
                potencia[k, tono] = (float)((sumaReal * sumaReal) + (sumaImaginaria * sumaImaginaria));
            }

            var p0 = potencia[k, 0];
            var p1 = potencia[k, 1];
            var p2 = potencia[k, 2];
            var p3 = potencia[k, 3];
            suma += _signo[k] * (p1 + p3 - p0 - p2);
            total += p0 + p1 + p2 + p3;
        }

        var sincronismo = total > 0 ? suma / total : 0;
        return new MedidaWspr(desplazamientoHz, comienzoMuestras, derivaHz, sincronismo, potencia, presentes);
    }
}
