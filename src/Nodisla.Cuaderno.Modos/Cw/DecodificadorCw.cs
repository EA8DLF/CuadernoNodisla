namespace Nodisla.Cuaderno.Modos.Cw;

/// <summary>Un trozo de texto decodificado.</summary>
/// <param name="Canal">Número del canal que lo leyó.</param>
/// <param name="Texto">Un carácter, un prosigno («&lt;AR&gt;») o un espacio.</param>
/// <param name="TonoHz">Tono del canal.</param>
/// <param name="EsPrincipal">Lo leyó el canal principal.</param>
public sealed record TextoCw(int Canal, string Texto, double TonoHz, bool EsPrincipal);

/// <summary>Cómo está un canal, para enseñarlo.</summary>
/// <param name="Id">Número del canal.</param>
/// <param name="TonoHz">Tono que sigue.</param>
/// <param name="Wpm">Velocidad estimada.</param>
/// <param name="SeparacionDb">Lo que destaca la señal sobre el ruido en su filtro.</param>
/// <param name="Confianza">Confianza en los tiempos, de 0 a 1.</param>
/// <param name="Enganchado">Está escribiendo.</param>
/// <param name="Marcando">La llave está abajo ahora mismo.</param>
/// <param name="EsPrincipal">Es el canal principal.</param>
public sealed record CanalEnVivo(
    int Id,
    double TonoHz,
    double Wpm,
    double SeparacionDb,
    double Confianza,
    bool Enganchado,
    bool Marcando,
    bool EsPrincipal);

/// <summary>Una foto del decodificador: canales y espectro.</summary>
/// <param name="Canales">Los canales vivos; el principal, el primero.</param>
/// <param name="EspectroDb">Espectro medio, en dB sobre el ruido, desde <paramref name="PrimeraHz"/>.</param>
/// <param name="PrimeraHz">Frecuencia de la primera casilla del espectro.</param>
/// <param name="HzPorCasilla">Ancho de cada casilla.</param>
/// <param name="TonoFijoHz">Tono fijado a mano, o nulo en automático.</param>
public sealed record EstadoCw(
    IReadOnlyList<CanalEnVivo> Canales,
    float[] EspectroDb,
    double PrimeraHz,
    double HzPorCasilla,
    double? TonoFijoHz)
{
    /// <summary>En automático, la principal está anclada a una señal (no busca otra).</summary>
    public bool Anclado { get; init; }

    /// <summary>Segundos que lleva la principal sin una marca.</summary>
    public double SegundosSinSenal { get; init; }

    /// <summary>Sin audio todavía.</summary>
    public static EstadoCw Vacio { get; } = new([], [], 0, 1, null);

    /// <summary>El canal principal, si hay.</summary>
    public CanalEnVivo? Principal => Canales.Count > 0 ? Canales[0] : null;
}

/// <summary>
/// El decodificador de telegrafía: en continuo, sobre el audio de recepción, con uno o varios
/// canales a la vez.
/// </summary>
/// <remarks>
/// <para>
/// <b>Todo propio.</b> Ni fldigi ni CW Skimmer ni bibliotecas de fuera: diezmado, oscilador,
/// filtro, envolvente, umbral, tiempos y tabla están en esta carpeta.
/// </para>
/// <para>
/// <b>Canal principal.</b> En automático se va al tono más fuerte de la ventana de búsqueda
/// (300–1200 Hz por omisión) y lo sigue si deriva; solo salta a otro cuando lleva un rato sin
/// escribir. Fijado, se queda en el tono que se le diga (el pitch del equipo o un clic en el
/// espectro).
/// </para>
/// <para>
/// <b>Varias señales.</b> Con <see cref="OpcionesCw.CanalesMaximos"/> mayor que uno, cada pico de
/// la banda que no tenga canal recibe uno propio, cada cual con su texto: un «skimmer» sencillo.
/// Un canal que se queda sin pico y sin escribir se retira.
/// </para>
/// <para>
/// <b>Hilos.</b> <see cref="Alimentar"/> se llama desde el hilo del audio y ahí salta
/// <see cref="TextoDecodificado"/>. Las órdenes (<see cref="FijarTono"/>, <see cref="Configurar"/>,
/// <see cref="Reiniciar"/>) pueden venir de cualquier hilo: se apuntan y se aplican en el siguiente
/// bloque. <see cref="Estado"/> se lee desde cualquier hilo.
/// </para>
/// <para>
/// No transmite nada: solo escucha.
/// </para>
/// </remarks>
public sealed class DecodificadorCw
{
    private const double SegundosParaRetirar = 10;

    private readonly object _cerrojo = new();
    private readonly List<CanalCw> _canales = [];
    private OpcionesCw _opciones;
    private OpcionesCw? _opcionesPendientes;
    private double? _tonoFijo;
    private bool _tonoPendiente;
    private bool _reinicioPendiente;
    private DiezmadorCw? _diezmador;
    private BuscadorDeTonos? _buscador;
    private CanalCw? _principal;
    private float[] _intermedio = [];
    private int _siguienteId;
    private double _silencioDelPrincipalParaSaltar;
    private EstadoCw _estado = EstadoCw.Vacio;
    private double? _anclado;
    private bool _buscarPendiente;

    /// <summary>Monta el decodificador.</summary>
    public DecodificadorCw(OpcionesCw? opciones = null) => _opciones = (opciones ?? new OpcionesCw()).Acotada();

    /// <summary>Salta con cada carácter, prosigno o espacio escrito, en el hilo del audio.</summary>
    public event EventHandler<TextoCw>? TextoDecodificado;

    /// <summary>Las opciones en uso.</summary>
    public OpcionesCw Opciones
    {
        get
        {
            lock (_cerrojo) return _opcionesPendientes ?? _opciones;
        }
    }

    /// <summary>Tono fijado, o nulo si está en automático.</summary>
    public double? TonoFijoHz
    {
        get
        {
            lock (_cerrojo) return _tonoFijo;
        }
    }

    /// <summary>La última foto, para pintar.</summary>
    public EstadoCw Estado
    {
        get
        {
            lock (_cerrojo) return _estado;
        }
    }

    /// <summary>Fija el canal principal a un tono, o lo vuelve a automático con nulo.</summary>
    public void FijarTono(double? hz)
    {
        lock (_cerrojo)
        {
            _tonoFijo = hz is { } h ? Math.Clamp(h, 100, 3000) : null;
            _tonoPendiente = true;
        }
    }

    /// <summary>En automático: suelta el tono anclado y vuelve a buscar la señal más fuerte.</summary>
    public void Buscar()
    {
        lock (_cerrojo) _buscarPendiente = true;
    }

    /// <summary>Cambia las opciones (en el siguiente bloque).</summary>
    public void Configurar(OpcionesCw opciones)
    {
        ArgumentNullException.ThrowIfNull(opciones);
        lock (_cerrojo) _opcionesPendientes = opciones.Acotada();
    }

    /// <summary>Olvida canales, niveles y velocidades (en el siguiente bloque).</summary>
    public void Reiniciar()
    {
        lock (_cerrojo) _reinicioPendiente = true;
    }

    /// <summary>Un bloque de audio de recepción.</summary>
    /// <param name="muestras">Muestras mono, de −1 a 1.</param>
    /// <param name="frecuenciaDeMuestreo">Muestras por segundo.</param>
    public void Alimentar(ReadOnlySpan<float> muestras, int frecuenciaDeMuestreo)
    {
        if (frecuenciaDeMuestreo <= 0 || muestras.IsEmpty) return;
        AplicarLoPendiente(frecuenciaDeMuestreo);

        var diezmador = _diezmador!;
        var cabe = diezmador.SalidaMaxima(muestras.Length);
        if (_intermedio.Length < cabe) _intermedio = new float[cabe];
        var n = diezmador.Procesar(muestras, _intermedio);

        for (var i = 0; i < n; i++)
        {
            var x = _intermedio[i];
            if (_buscador!.Anadir(x)) Gestionar();
            foreach (var canal in _canales) canal.Anadir(x);
        }
    }

    /// <summary>Cierra los caracteres a medias (al acabar un fichero o una prueba).</summary>
    public void Vaciar()
    {
        foreach (var canal in _canales) canal.Vaciar();
    }

    private void AplicarLoPendiente(int frecuenciaDeMuestreo)
    {
        bool reiniciar, retonar;
        OpcionesCw? nuevas;
        double? fijo;
        lock (_cerrojo)
        {
            reiniciar = _reinicioPendiente || _diezmador is null || _diezmador.FrecuenciaDeEntrada != frecuenciaDeMuestreo;
            retonar = _tonoPendiente;
            nuevas = _opcionesPendientes;
            fijo = _tonoFijo;
            _reinicioPendiente = _tonoPendiente = false;
            _opcionesPendientes = null;
            if (nuevas is not null) _opciones = nuevas;
        }

        if (reiniciar)
        {
            _diezmador = new DiezmadorCw(frecuenciaDeMuestreo);
            _buscador = new BuscadorDeTonos(_diezmador.FrecuenciaDeSalida);
            _canales.Clear();
            _principal = Nuevo(fijo ?? _opciones.TonoPorOmisionHz);
            _anclado = null;
            _silencioDelPrincipalParaSaltar = 0;
            return;
        }

        if (nuevas is not null)
        {
            foreach (var c in _canales) c.Configurar(nuevas);
            while (_canales.Count > nuevas.CanalesMaximos) Retirar(_canales.Last(c => c != _principal));
        }

        if (retonar && fijo is { } hz) _principal!.Afinar(hz);
    }

    private CanalCw Nuevo(double tonoHz)
    {
        var canal = new CanalCw(_siguienteId++, _diezmador!.FrecuenciaDeSalida, tonoHz, _opciones);
        canal.Texto += AlEscribir;
        _canales.Add(canal);
        return canal;
    }

    private void Retirar(CanalCw canal)
    {
        canal.Texto -= AlEscribir;
        _canales.Remove(canal);
    }

    private void AlEscribir(CanalCw canal, string texto) =>
        TextoDecodificado?.Invoke(this, new TextoCw(canal.Id, texto, canal.TonoHz, canal == _principal));

    /// <summary>Con cada espectro nuevo: seguir los tonos, abrir y cerrar canales y hacer la foto.</summary>
    private void Gestionar()
    {
        var o = _opciones;
        var buscador = _buscador!;
        var periodo = buscador.SegundosPorEspectro;
        var tolerancia = Math.Max(0.6 * o.AnchoDelFiltroHz, 25);
        var separacion = Math.Max(o.AnchoDelFiltroHz, 40);
        // Un pico cuenta si es el máximo en ±1,2 anchos de filtro: los hombros de una señal
        // fuerte (la manipulación ensancha su espectro) no abren canales propios.
        var picos = buscador.Picos(o.BandaDesdeHz, o.BandaHastaHz, 2 * Math.Max(1.2 * o.AnchoDelFiltroHz, 60));
        double? fijo;
        lock (_cerrojo) fijo = _tonoFijo;

        var principal = _principal!;

        // ── El principal ──
        if (fijo is { } f)
        {
            if (Math.Abs(principal.TonoHz - f) > 0.5) principal.Afinar(f);
        }
        else
        {
            bool buscar;
            lock (_cerrojo)
            {
                buscar = _buscarPendiente;
                _buscarPendiente = false;
            }

            // AUTO con enganche: en cuanto la principal escribe, se ancla a ese tono. Anclada no
            // salta a otro pico aunque suba más, ni en las pausas ni en los bajones de QSB; solo
            // vuelve a buscar tras SegundosSinSenalParaBuscar sin una marca, o si se pide.
            if (buscar) principal.Soltar();
            if (principal.Enganchado) _anclado = principal.TonoHz;
            if (buscar || (_anclado is not null && !principal.Enganchado && principal.SinSenalMs > o.SegundosSinSenalParaBuscar * 1000))
            {
                _anclado = null;
                _silencioDelPrincipalParaSaltar = 1;
            }

            var cerca = MasCercano(picos, principal.TonoHz, tolerancia);
            if (cerca is { } p)
            {
                // Anclada solo se deja derivar un poco (±tolerancia del tono de enganche).
                var nuevo = principal.TonoHz + (0.25 * (p.Hz - principal.TonoHz));
                if (_anclado is not { } a || Math.Abs(nuevo - a) <= tolerancia) principal.Afinar(nuevo);
                _silencioDelPrincipalParaSaltar = 0;
            }
            else if (!principal.Enganchado)
            {
                _silencioDelPrincipalParaSaltar += periodo;
            }

            // Sin anclar, sin señal en su tono y sin escribir: al pico más fuerte.
            if (_anclado is null && !principal.Enganchado && picos.Count > 0
                && (cerca is null ? _silencioDelPrincipalParaSaltar > 0.5 : Math.Abs(picos[0].Hz - principal.TonoHz) > separacion && principal.SilencioMs > 1500))
            {
                var mejor = picos[0];
                var dueno = _canales.FirstOrDefault(c => c != principal && Math.Abs(c.TonoHz - mejor.Hz) < tolerancia);
                if (dueno is not null)
                {
                    // Otro canal ya lo lleva: pasa a ser el principal y el viejo se retira.
                    Retirar(principal);
                    _canales.Remove(dueno);
                    _canales.Insert(0, dueno);
                    _principal = principal = dueno;
                }
                else
                {
                    principal.Afinar(mejor.Hz);
                }

                _silencioDelPrincipalParaSaltar = 0;
            }
        }

        // ── Los demás (skimmer) ──
        foreach (var canal in _canales.ToList())
        {
            if (canal == principal) continue;
            var cerca = MasCercano(picos, canal.TonoHz, tolerancia);
            if (cerca is { } p) canal.Afinar(canal.TonoHz + (0.25 * (p.Hz - canal.TonoHz)));

            var choca = Math.Abs(canal.TonoHz - principal.TonoHz) < separacion / 2;
            var abandonado = !canal.Enganchado && (cerca is null ? canal.AbandonoMs > 3000 : canal.AbandonoMs > SegundosParaRetirar * 1000);
            var mudo = cerca is null && canal.SilencioMs > SegundosParaRetirar * 1000;

            // Sin pico propio y con otro canal al lado: está leyendo lo que se le cuela de la
            // señal del vecino por el flanco del filtro. Sobra.
            var huerfano = cerca is null && _canales.Any(c => c != canal && Math.Abs(c.TonoHz - canal.TonoHz) < 2 * o.AnchoDelFiltroHz);
            if (choca || abandonado || mudo || huerfano) Retirar(canal);
        }

        if (o.CanalesMaximos > 1)
        {
            foreach (var pico in picos)
            {
                if (_canales.Count >= o.CanalesMaximos) break;
                if (_canales.Any(c => Math.Abs(c.TonoHz - pico.Hz) < separacion)) continue;
                Nuevo(pico.Hz);
            }
        }

        // ── La foto ──
        var vivos = _canales
            .OrderBy(c => c == principal ? 0 : 1)
            .ThenBy(c => c.TonoHz)
            .Select(c => new CanalEnVivo(c.Id, c.TonoHz, c.Wpm, c.SeparacionDb, c.Confianza, c.Enganchado, c.Marcando, c == principal))
            .ToArray();
        var espectro = buscador.EspectroDb(Math.Max(100, o.BandaDesdeHz - 100), o.BandaHastaHz + 100, out var primera);
        var estado = new EstadoCw(vivos, espectro, primera, buscador.HzPorCasilla, fijo)
        {
            Anclado = fijo is null && _anclado is not null,
            SegundosSinSenal = principal.SinSenalMs / 1000,
        };
        lock (_cerrojo) _estado = estado;
    }

    private static PicoCw? MasCercano(IReadOnlyList<PicoCw> picos, double hz, double tolerancia)
    {
        PicoCw? mejor = null;
        foreach (var p in picos)
        {
            var d = Math.Abs(p.Hz - hz);
            if (d <= tolerancia && (mejor is null || d < Math.Abs(mejor.Value.Hz - hz))) mejor = p;
        }

        return mejor;
    }
}
