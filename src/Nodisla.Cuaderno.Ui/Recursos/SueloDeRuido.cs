namespace Nodisla.Cuaderno.Ui.Recursos;

/// <summary>
/// Sigue el suelo de ruido de la traza del analizador, en la escala de la radio (0-255).
/// </summary>
/// <remarks>
/// <para>
/// Idea de Thetis (<c>display.cs:processNoiseFloor</c>, MW0LGE), reimplementada: cada pasada se
/// promedian los puntos que quedan por debajo del suelo más un margen; si son pocos (el suelo
/// se ha quedado bajo, p. ej. tras subir el preamplificador), el suelo sube un punto por pasada.
/// Lo medido no se pone de golpe: el suelo se acerca a ello en unas
/// <see cref="TiempoDeAtaqueMs"/> (unos dos segundos), para que una ráfaga no haga parpadear la
/// cascada. Al cambiar de banda o de span hay <see cref="AtaqueRapido"/>: durante un segundo el
/// suelo va directo a lo medido (y, si se ha quedado bajo, se vuelve a medir de cero en vez de
/// subir de punto en punto).
/// </para>
/// <para>
/// Por qué los puntos de abajo y no la mediana: en una banda llena (un concurso) la mitad de
/// la traza son señales y la mediana se va hacia arriba; los puntos que quedan por debajo del
/// suelo son ruido aunque haya pocos.
/// </para>
/// </remarks>
public sealed class SueloDeRuido
{
    /// <summary>
    /// Lo que se mira por encima del suelo para contar un punto como ruido. La traza del FT-710
    /// es muy dentada (el ruido de una pasada va de ~45 a ~83, medido en las tramas reales): con
    /// un margen menor el suelo se iria a la parte de abajo de los dientes. Con 14, en las tramas
    /// reales el suelo cae en 64 (media de la traza 66,5; con 10 caia en 56).
    /// </summary>
    public const double Margen = 14;

    /// <summary>Parte de los puntos que tiene que quedar bajo suelo+margen para fiarse de la media.</summary>
    public const double ParteNecesaria = 0.2;

    /// <summary>Lo que se tarda en llegar al suelo medido, en milisegundos.</summary>
    public const double TiempoDeAtaqueMs = 2000;

    /// <summary>Lo que dura el ataque rápido tras cambiar de banda o de span.</summary>
    public const double DuracionDelAtaqueRapidoMs = 1000;

    private double _medido = double.NaN;
    private double _suelo = double.NaN;
    private double _rapidoHastaMs = double.NegativeInfinity;

    /// <summary>Pasadas por segundo que llegan (el FT-710 manda unas once).</summary>
    public double PasadasPorSegundo { get; set; } = 11;

    /// <summary>El suelo que se esta usando; 0 hasta la primera pasada.</summary>
    public double Valor => double.IsNaN(_suelo) ? 0 : _suelo;

    /// <summary>Ya ha visto alguna pasada.</summary>
    public bool Listo => !double.IsNaN(_suelo);

    /// <summary>Esta en ataque rapido.</summary>
    public bool EnAtaqueRapido { get; private set; }

    /// <summary>
    /// Ha cambiado la banda o el span: el suelo de antes ya no vale y se va a por el nuevo deprisa.
    /// </summary>
    /// <param name="ahoraMs">Reloj en milisegundos.</param>
    /// <param name="sugerido">Un suelo ya conocido para esta banda (caché); nulo si no hay.</param>
    public void AtaqueRapido(double ahoraMs, double? sugerido = null)
    {
        _rapidoHastaMs = ahoraMs + DuracionDelAtaqueRapidoMs;
        EnAtaqueRapido = true;
        if (sugerido is { } s && double.IsFinite(s))
        {
            _suelo = s;
            _medido = s;
        }
    }

    /// <summary>Mira una pasada y mueve el suelo.</summary>
    /// <param name="niveles">Los niveles de la traza (0-255).</param>
    /// <param name="ahoraMs">Reloj en milisegundos.</param>
    /// <returns>El suelo nuevo.</returns>
    public double Seguir(ReadOnlySpan<byte> niveles, double ahoraMs)
    {
        if (niveles.IsEmpty) return Valor;
        if (EnAtaqueRapido && ahoraMs > _rapidoHastaMs) EnAtaqueRapido = false;

        if (double.IsNaN(_medido))
        {
            // La primera vez se parte de la quinta parte más baja: no se espera dos segundos
            // a que el suelo baje desde cero o desde arriba.
            _medido = Percentil(niveles, ParteNecesaria);
            _suelo = _medido;
            return _suelo;
        }

        var umbral = _medido + Margen;
        long suma = 0;
        var cuantos = 0;
        foreach (var n in niveles)
        {
            if (n >= umbral) continue;
            suma += n;
            cuantos++;
        }

        if (cuantos >= niveles.Length * ParteNecesaria)
        {
            var media = (double)suma / cuantos;
            _medido = EnAtaqueRapido ? media : (_medido + media) * 0.5;
        }
        else if (EnAtaqueRapido)
        {
            // Otra banda con mas ruido: no se sube de punto en punto, se mide de nuevo.
            _medido = Percentil(niveles, ParteNecesaria);
        }
        else
        {
            _medido += 1;
        }

        _medido = Math.Clamp(_medido, 0, 255);

        var pasadasDeAtaque = EnAtaqueRapido ? 1 : 1 + (int)(PasadasPorSegundo * TiempoDeAtaqueMs / 1000);
        _suelo -= (_suelo - _medido) / pasadasDeAtaque;
        return _suelo;
    }

    private static double Percentil(ReadOnlySpan<byte> niveles, double parte)
    {
        Span<int> cuenta = stackalloc int[256];
        foreach (var n in niveles) cuenta[n]++;
        var objetivo = (int)(niveles.Length * parte);
        var acumulado = 0;
        for (var i = 0; i < 256; i++)
        {
            acumulado += cuenta[i];
            if (acumulado > objetivo) return i;
        }

        return 255;
    }
}
