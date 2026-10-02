namespace Nodisla.Cuaderno.Modos.Cw;

/// <summary>
/// Decide, cuadro a cuadro, si la llave está abajo: umbral que sigue a la señal y al ruido, con
/// histéresis y con silenciador.
/// </summary>
/// <remarks>
/// <para>
/// Todo en decibelios, que es como se comporta el QSB. Se lleva un <b>histograma con olvido</b>
/// (constante de 1,5 s) de los niveles de los últimos cuadros:
/// </para>
/// <list type="bullet">
/// <item><b>Ruido</b>: el percentil 20. En telegrafía la llave está arriba bastante más de un 20 %
/// del tiempo, así que eso es ruido aunque haya señal.</item>
/// <item><b>Señal</b>: el percentil 90, que cae en las marcas mientras la llave baje más de un 10 %
/// del tiempo. Además se sigue el nivel de las propias marcas con una constante corta (80 ms), y
/// la referencia es el menor de los dos: así el umbral baja con el QSB sin esperar al
/// histograma.</item>
/// </list>
/// <para>
/// Los percentiles no se dejan arrastrar por el propio umbral (un seguidor «solo en los espacios»
/// se va hundiendo en la cola baja del ruido y acaba con el umbral dentro del ruido).
/// </para>
/// <para>
/// El umbral está entre los dos (55 % para entrar en marca, 40 % para salir: la histéresis que
/// evita que el ruido haga temblar el borde). Es el control automático de ganancia: no importa a
/// qué nivel llegue la señal, solo cuánto destaca.
/// </para>
/// <para>
/// El <b>silenciador</b> no deja pasar marcas mientras la señal no destaque del ruido al menos
/// <see cref="UmbralDb"/>. Con ruido puro la separación se queda por debajo y no sale nada.
/// </para>
/// </remarks>
public sealed class UmbralAdaptativo
{
    private const double Ms = EnvolventeCw.MilisegundosPorCuadro;
    private const double DbMinimo = -220;
    private const double DbPorCasilla = 0.5;
    private const int Casillas = 560;

    /// <summary>Del percentil 20 a la media, para un ruido ya suavizado (dB).</summary>
    private const double CorreccionDelPercentil = 2.5;

    private const double CaidaDelPicoDbPorSegundo = 10;

    private readonly double[] _histograma = new double[Casillas];
    private readonly double _crecimiento = Math.Exp(Ms / 1500);
    private readonly double _enMarca = 1 - Math.Exp(-Ms / 80);
    private double _peso = 1;
    private double _total;
    private double _marcas = double.NaN;
    private int _cuadros;
    private double _bajoMs;
    private double _encimaMs;
    private double _pico = double.NaN;

    // El ruido de corto plazo (los últimos 0,6 s): con él decide el silenciador. El de largo
    // plazo tarda segundo y pico en seguir un salto del nivel (se toca la ganancia de RF, el
    // AGC de la radio se recupera, se abre la tarjeta…) y mientras tanto el ruido nuevo parecía
    // una señal de 60 dB: el silenciador se abría y se escribía ruido.
    private const int CuadrosCortos = 300;
    private readonly double[] _cortos = new double[CuadrosCortos];
    private readonly double[] _ordenados = new double[CuadrosCortos];
    private int _iCortos;
    private int _nCortos;
    private double _ruidoCorto = double.NaN;

    /// <summary>Monta el umbral.</summary>
    /// <param name="umbralDb">Lo que tiene que destacar la señal para abrir el silenciador.</param>
    public UmbralAdaptativo(double umbralDb) => UmbralDb = umbralDb;

    /// <summary>Lo que tiene que destacar la señal sobre el ruido para abrir (dB).</summary>
    public double UmbralDb { get; set; }

    /// <summary>dB que se suman a <see cref="UmbralDb"/> según lo poco suavizada que llega la envolvente.</summary>
    public double Exigencia { get; set; }

    /// <summary>Nivel de ruido (dB relativos).</summary>
    public double RuidoDb { get; private set; } = double.NaN;

    /// <summary>Nivel de las marcas (dB relativos).</summary>
    public double SenalDb { get; private set; } = double.NaN;

    /// <summary>Lo que destaca la señal sobre el ruido (dB), la medida de nivel del canal.</summary>
    public double SeparacionDb => double.IsNaN(RuidoDb) ? 0 : Math.Max(0, SenalDb - RuidoDb);

    /// <summary>La envolvente está por encima del umbral (sin mirar el silenciador).</summary>
    public bool MarcaCruda { get; private set; }

    /// <summary>El silenciador está abierto.</summary>
    public bool Abierto { get; private set; }

    /// <summary>Un cuadro nuevo.</summary>
    /// <param name="potencia">Potencia del cuadro (lineal).</param>
    /// <returns>Si la llave está abajo y el silenciador abierto.</returns>
    public bool Paso(double potencia)
    {
        // Silencio digital (la tarjeta entrega ceros al abrirse, o la radio calla del todo): no
        // es ruido ni señal y no se apunta. Si se apuntara, el percentil del ruido caería a
        // −220 dB y cualquier ruido de verdad parecería una señal enorme al llegar.
        if (potencia < 1e-15)
        {
            MarcaCruda = false;
            return false;
        }

        var db = 10 * Math.Log10(potencia);
        Apuntar(db);
        _cuadros++;

        // Hacen falta unos cuadros para que los percentiles digan algo.
        if (_cuadros < 250)
        {
            Percentiles(out var r, out var s);
            RuidoDb = r;
            SenalDb = s;
            MarcaCruda = false;
            Abierto = false;
            return false;
        }

        Percentiles(out var ruido, out var p90);

        // Ruido de corto plazo: percentil 10 de los últimos 0,6 s (cada 20 ms). En régimen queda
        // por debajo del de largo plazo y no cuenta; tras un salto de nivel manda él.
        _cortos[_iCortos] = db;
        _iCortos = (_iCortos + 1) % CuadrosCortos;
        if (_nCortos < CuadrosCortos) _nCortos++;
        if (_cuadros % 10 == 0 || double.IsNaN(_ruidoCorto))
        {
            Array.Copy(_cortos, _ordenados, _nCortos);
            Array.Sort(_ordenados, 0, _nCortos);
            _ruidoCorto = _ordenados[(int)(0.1 * (_nCortos - 1))];
        }

        // Solo cuenta si de verdad ha habido un salto (más de 6 dB por encima): en régimen, con una
        // señal débil, subir el suelo unos dB le quitaba la sensibilidad.
        if (_ruidoCorto > ruido + 6) ruido = _ruidoCorto;

        // Pico con caída de 10 dB/s: en un desvanecimiento la media de las marcas va por detrás,
        // y el pico, que se rehace con cada marca, la frena.
        _pico = double.IsNaN(_pico) ? db : Math.Max(db, _pico - (CaidaDelPicoDbPorSegundo * Ms / 1000));
        var senal = double.IsNaN(_marcas) ? p90 : Math.Clamp(Math.Max(Math.Min(_marcas, _pico), _marcas - Math.Max(0, (_marcas - ruido - 12) / 2)), ruido, Math.Max(ruido, p90 + 6));
        RuidoDb = ruido;
        SenalDb = senal;
        var separacion = Math.Max(0, senal - ruido);

        // Umbral en potencia lineal, a mitad entre la media del ruido y la de las marcas: con la
        // envolvente suavizada por una media móvil, cruzar a la mitad conserva la duración de
        // cada marca, y la histéresis simétrica (60 % y 40 %) también.
        var ln = Math.Pow(10, (ruido + CorreccionDelPercentil) / 10);
        var ls = Math.Max(ln, Math.Pow(10, senal / 10));
        var entrar = ln + (0.65 * (ls - ln));
        var salir = ln + (0.35 * (ls - ln));
        MarcaCruda = MarcaCruda ? potencia >= salir : potencia > entrar;

        if (MarcaCruda)
        {
            // Media (en dB) del nivel de las marcas, simétrica para no irse a los picos del ruido
            // que lleva encima; solo un salto grande (una señal nueva y fuerte) se sigue de golpe.
            if (double.IsNaN(_marcas) || db > _marcas + 6) _marcas = db - 3;
            else _marcas += _enMarca * (db - _marcas);
        }

        // El silenciador no puede temblar con cada cuadro (partiría las marcas de una señal que
        // anda justo en el umbral): abre tras pasar el umbral 150 ms seguidos (un pico suelto de
        // ruido no abre) y cierra tras 0,8 s seguidos más de 1,5 dB por debajo. Antes la
        // histéresis era de 3 dB y bastaba con que el ruido rondara el umbral para dejarlo
        // abierto para siempre: es lo que escribía chorros de E, I, T con la ganancia de RF alta.
        var exigido = UmbralDb + Exigencia;
        _encimaMs = separacion >= exigido ? _encimaMs + Ms : 0;
        if (_encimaMs >= 150) Abierto = true;
        _bajoMs = separacion < exigido - 3 ? _bajoMs + Ms : 0;
        if (_bajoMs > 1500) Abierto = false;

        return MarcaCruda && Abierto;
    }

    /// <summary>Olvida los niveles.</summary>
    public void Reiniciar()
    {
        Array.Clear(_histograma);
        _peso = 1;
        _total = 0;
        _cuadros = 0;
        _marcas = double.NaN;
        _pico = double.NaN;
        _iCortos = _nCortos = 0;
        _ruidoCorto = double.NaN;
        RuidoDb = SenalDb = double.NaN;
        MarcaCruda = false;
        Abierto = false;
    }

    /// <summary>
    /// Suma el cuadro al histograma. El olvido se hace al revés: en vez de encoger todo lo viejo
    /// en cada cuadro, lo nuevo pesa cada vez más; de cuando en cuando se renormaliza.
    /// </summary>
    private void Apuntar(double db)
    {
        var casilla = (int)Math.Clamp((db - DbMinimo) / DbPorCasilla, 0, Casillas - 1);
        _peso *= _crecimiento;
        _histograma[casilla] += _peso;
        _total += _peso;
        if (_peso > 1e150)
        {
            for (var i = 0; i < Casillas; i++) _histograma[i] /= _peso;
            _total /= _peso;
            _peso = 1;
        }
    }

    /// <summary>Los percentiles 20 y 90 en una sola pasada.</summary>
    private void Percentiles(out double p20, out double p90)
    {
        var o20 = 0.2 * _total;
        var o90 = 0.9 * _total;
        double acumulado = 0;
        p20 = double.NaN;
        for (var i = 0; i < Casillas; i++)
        {
            acumulado += _histograma[i];
            if (double.IsNaN(p20) && acumulado >= o20) p20 = DbMinimo + ((i + 0.5) * DbPorCasilla);
            if (acumulado >= o90)
            {
                p90 = DbMinimo + ((i + 0.5) * DbPorCasilla);
                if (double.IsNaN(p20)) p20 = p90;
                return;
            }
        }

        p90 = DbMinimo + (Casillas * DbPorCasilla);
        if (double.IsNaN(p20)) p20 = p90;
    }
}