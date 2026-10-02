namespace Nodisla.Cuaderno.Modos.Cw;

/// <summary>
/// Un decodificador de telegrafía sobre un tono: filtro, envolvente, umbral, tiempos y texto.
/// </summary>
/// <remarks>
/// <para>
/// <b>Tres velocidades a la vez.</b> El suavizado que hace legible una señal débil (una media de
/// casi un punto) es justo el que borra los huecos de una señal más rápida, y con él la
/// velocidad estimada se iría aún más lenta: el decodificador no saldría nunca de ahí. Por eso
/// sobre la misma envolvente trabajan tres lecturas, cada una en su franja de velocidad (lenta,
/// media y rápida, solapadas), con su suavizado, su umbral y su lector. Escribe la que se engancha
/// primero, y se pasa a otra si durante un segundo lee claramente mejor (cambio de velocidad).
/// </para>
/// <para>
/// <b>Enganche.</b> Lo que sale de una lectura no se escribe hasta que se engancha: tres
/// caracteres válidos seguidos, con la confianza de los tiempos por encima de 0,6 y el
/// silenciador abierto. Hasta entonces se guarda y al engancharse se suelta de golpe: no se
/// pierde el principio del mensaje y el ruido, que rara vez arma tres caracteres con tiempos de
/// telegrafía, no escribe nada. Tras seis segundos sin marcas (o veinticinco puntos, si es más)
/// se desengancha y vuelta a empezar.
/// </para>
/// </remarks>
public sealed class CanalCw
{
    private const double Ms = EnvolventeCw.MilisegundosPorCuadro;

    private readonly EnvolventeCw _envolvente;
    private readonly List<LecturaCw> _lecturas = [];
    private LecturaCw? _activa;
    private double _mejorDuranteMs;

    /// <summary>Monta el canal.</summary>
    /// <param name="id">Número del canal (0 = el principal).</param>
    /// <param name="frecuenciaDeMuestreo">Muestras por segundo del audio que llega.</param>
    /// <param name="tonoHz">Tono que sigue.</param>
    /// <param name="opciones">Ancho, sensibilidad y velocidades.</param>
    public CanalCw(int id, double frecuenciaDeMuestreo, double tonoHz, OpcionesCw opciones)
    {
        ArgumentNullException.ThrowIfNull(opciones);
        Id = id;
        _envolvente = new EnvolventeCw(frecuenciaDeMuestreo, tonoHz, opciones.AnchoDelFiltroHz);
        Montar(opciones);
    }

    /// <summary>Salta con cada trozo de texto ya enganchado (en el hilo del audio).</summary>
    public event Action<CanalCw, string>? Texto;

    /// <summary>Número del canal.</summary>
    public int Id { get; }

    /// <summary>Tono que se sigue (Hz).</summary>
    public double TonoHz => _envolvente.TonoHz;

    /// <summary>Velocidad estimada (WPM).</summary>
    public double Wpm => Referencia.Lector.Wpm;

    /// <summary>Lo que destaca la señal sobre el ruido en el filtro (dB).</summary>
    public double SeparacionDb => Referencia.Umbral.SeparacionDb;

    /// <summary>Confianza en los tiempos, de 0 a 1.</summary>
    public double Confianza => Referencia.Lector.Confianza;

    /// <summary>El canal está enganchado a una señal de telegrafía.</summary>
    public bool Enganchado => _activa is not null;

    /// <summary>La llave está abajo ahora mismo.</summary>
    public bool Marcando => Referencia.Lector.EnMarca;

    /// <summary>Milisegundos desde la última marca.</summary>
    public double SilencioMs => _lecturas.Min(l => l.Lector.SilencioMs);

    /// <summary>Milisegundos de vida sin engancharse ni oír nada que se parezca a una señal.</summary>
    public double AbandonoMs { get; private set; }

    /// <summary>Caracteres escritos desde que se creó.</summary>
    public int Escritos { get; private set; }

    /// <summary>El lector de la lectura que manda ahora (para las pruebas).</summary>
    public LectorDeTiempos Lector => Referencia.Lector;

    /// <summary>La lectura que escribe, o la que mejor va si todavía no se ha enganchado ninguna.</summary>
    private LecturaCw Referencia => _activa ?? _lecturas.MaxBy(l => l.Lector.Confianza)!;

    /// <summary>Mueve el canal a otro tono sin perder lo que lleva.</summary>
    public void Afinar(double tonoHz) => _envolvente.Afinar(tonoHz);

    /// <summary>Aplica opciones nuevas (el ancho vacía el filtro; las velocidades rehacen las lecturas).</summary>
    public void Configurar(OpcionesCw opciones)
    {
        ArgumentNullException.ThrowIfNull(opciones);
        if (Math.Abs(opciones.AnchoDelFiltroHz - _envolvente.AnchoHz) > 0.5) _envolvente.CambiarAncho(opciones.AnchoDelFiltroHz);
        var franjas = Franjas(opciones.WpmMinima, opciones.WpmMaxima);
        if (franjas.Count != _lecturas.Count || franjas.Where((f, i) => f != _lecturas[i].Franja).Any())
        {
            Montar(opciones);
            return;
        }

        foreach (var l in _lecturas) l.Umbral.UmbralDb = opciones.UmbralDb;
    }

    /// <summary>Una muestra de audio (ya a la frecuencia del canal).</summary>
    public void Anadir(float x)
    {
        if (!_envolvente.Anadir(x, out var potencia)) return;

        var algunaAbierta = false;
        foreach (var l in _lecturas)
        {
            l.Paso(potencia);
            algunaAbierta |= l.Umbral.Abierto;
        }

        AbandonoMs = Enganchado || algunaAbierta ? 0 : AbandonoMs + Ms;
        if (_activa is not { } activa) return;

        if (activa.Lector.SilencioMs > Math.Max(6000, 25 * activa.Lector.PuntoMs) || activa.CerradoMs > 3000 || activa.Lector.Confianza < 0.5)
        {
            activa.Lector.Vaciar();
            _activa = null;
            foreach (var l in _lecturas) l.Guardados.Clear();
            return;
        }

        // ¿Otra lectura va claramente mejor (la velocidad ha cambiado de franja)?
        var mejor = _lecturas.MaxBy(l => l.Lector.Confianza)!;
        var activaFuera = !activa.EnSuFranja;
        if (mejor != activa && mejor.EnSuFranja && mejor.Lector.ValidosSeguidos >= 3
            && (mejor.Lector.Confianza > activa.Lector.Confianza + 0.2 || (activaFuera && mejor.Lector.Confianza >= activa.Lector.Confianza - 0.05)))
        {
            _mejorDuranteMs += Ms;
            if (_mejorDuranteMs > 1000 && !activa.Lector.CaracterAMedias)
            {
                _activa = mejor;
                _mejorDuranteMs = 0;
            }
        }
        else
        {
            _mejorDuranteMs = 0;
        }
    }

    /// <summary>Cierra lo que haya a medias.</summary>
    public void Vaciar()
    {
        foreach (var l in _lecturas) l.Lector.Vaciar();
    }

    /// <summary>
    /// Las franjas de velocidad de las lecturas: lenta (hasta 16), media (13 a 32) y rápida
    /// (desde 26), recortadas a lo que se pida.
    /// </summary>
    internal static IReadOnlyList<(double Minima, double Maxima)> Franjas(double minima, double maxima)
    {
        var franjas = new List<(double, double)>();
        foreach (var (a, b) in new[] { (5.0, 16.0), (13.0, 32.0), (26.0, 60.0) })
        {
            var lo = Math.Max(a, minima);
            var hi = Math.Min(b, maxima);
            if (hi - lo >= 3) franjas.Add((lo, hi));
        }

        if (franjas.Count == 0) franjas.Add((minima, maxima));
        return franjas;
    }

    private void Montar(OpcionesCw opciones)
    {
        _activa = null;
        _lecturas.Clear();
        foreach (var franja in Franjas(opciones.WpmMinima, opciones.WpmMaxima))
        {
            var lectura = new LecturaCw(franja, opciones.UmbralDb)
            {
                AbiertaPorAbajo = franja.Minima <= opciones.WpmMinima,
                AbiertaPorArriba = franja.Maxima >= opciones.WpmMaxima,
            };
            lectura.Simbolo += AlLeer;
            _lecturas.Add(lectura);
        }
    }

    private void AlLeer(LecturaCw lectura, string simbolo)
    {
        if (lectura == _activa)
        {
            Escribir(simbolo);
            return;
        }

        if (simbolo == " " && lectura.Guardados.Count == 0) return;
        lectura.Guardar(simbolo);

        if (_activa is null && lectura.ListaParaEngancharse)
        {
            _activa = lectura;
            foreach (var s in lectura.Guardados) Escribir(s);
            foreach (var l in _lecturas) l.Guardados.Clear();
        }
    }

    private void Escribir(string s)
    {
        if (s != " ") Escritos++;
        Texto?.Invoke(this, s);
    }

    /// <summary>Una lectura: suavizado, umbral y lector para una franja de velocidad.</summary>
    private sealed class LecturaCw
    {
        private const int GuardadosMaximos = 16;

        private readonly double[] _suavizado = new double[30];
        private int _i;

        public LecturaCw((double Minima, double Maxima) franja, double umbralDb)
        {
            Franja = franja;
            Umbral = new UmbralAdaptativo(umbralDb);
            Lector = new LectorDeTiempos(franja.Minima, franja.Maxima, Math.Sqrt(franja.Minima * franja.Maxima));
            Lector.Simbolo += s => Simbolo?.Invoke(this, s);
            Lector.CodigoDesconocido += _ => Guardados.Clear();
        }

        public event Action<LecturaCw, string>? Simbolo;

        public (double Minima, double Maxima) Franja { get; }

        public UmbralAdaptativo Umbral { get; }

        public LectorDeTiempos Lector { get; }

        public List<string> Guardados { get; } = [];

        /// <summary>Tiempo con el silenciador cerrado; arranca «infinito» (nunca se abrió).</summary>
        public double CerradoMs { get; private set; } = 1e9;

        public bool ListaParaEngancharse =>
            Lector.ValidosSeguidos >= 5 && Lector.Confianza >= 0.75 && (Umbral.Abierto || CerradoMs < 500)
            && Lector.PuntoMedidoMs is not null && EnSuFranja && Mezclados() >= 3;

        /// <summary>
        /// Caracteres guardados que mezclan puntos y rayas. El ruido arma sobre todo E, I, S, H, 5
        /// y T (solo puntos o solo rayas); el texto de verdad trae enseguida C, Q, D, A...
        /// </summary>
        private int Mezclados()
        {
            var n = 0;
            foreach (var s in Guardados)
            {
                var codigo = s == " " ? null : TablaMorse.Codificar(s);
                if (codigo is not null && codigo.Contains('.') && codigo.Contains('-')) n++;
            }

            return n;
        }

        /// <summary>Lo que va más lento que el mínimo de su franja queda fuera, salvo que ese mínimo sea el de las opciones.</summary>
        public bool AbiertaPorAbajo { get; init; }

        /// <summary>Lo mismo por arriba.</summary>
        public bool AbiertaPorArriba { get; init; }

        /// <summary>
        /// La velocidad estimada cae dentro de la franja y no pegada a un borde: una lectura que
        /// tiene la velocidad aplastada contra su límite está leyendo una señal que no es la suya
        /// y no debe engancharse (a veces lo hace bien, pero la que tiene la velocidad dentro lo
        /// hace mejor).
        /// </summary>
        public bool EnSuFranja
        {
            get
            {
                var punto = Lector.PuntoMedidoMs ?? Lector.PuntoMs;
                return (AbiertaPorAbajo || (Lector.PuntoMs < 0.97 * Lector.PuntoMaximoMs && punto < 1.1 * Lector.PuntoMaximoMs))
                    && (AbiertaPorArriba || (Lector.PuntoMs > 1.03 * Lector.PuntoMinimoMs && punto > 0.9 * Lector.PuntoMinimoMs));
            }
        }

        public void Guardar(string simbolo)
        {
            Guardados.Add(simbolo);
            if (Guardados.Count > GuardadosMaximos) Guardados.RemoveAt(0);
        }

        public void Paso(double potencia)
        {
            // Media móvil de la potencia: el filtro adaptado a la velocidad que cabe después de la
            // envolvente. Con el umbral a mitad de altura no cambia la duración de las marcas. Con
            // señal fuerte se suaviza poco (un cuarto de punto) y los tiempos salen finos; cuanto
            // más débil, más largo, hasta 0,8 puntos, que gana varios dB.
            _suavizado[_i] = potencia;
            _i = (_i + 1) % _suavizado.Length;
            var fraccion = 0.25 + (0.55 * Math.Clamp((22 - Umbral.SeparacionDb) / 8, 0, 1));
            var k = Math.Clamp((int)Math.Round(fraccion * Lector.PuntoMs / Ms), 1, _suavizado.Length);
            double suma = 0;
            for (var j = 1; j <= k; j++) suma += _suavizado[(_i - j + _suavizado.Length) % _suavizado.Length];

            var marca = Umbral.Paso(suma / k);
            Lector.SenalFuerte = Umbral.SeparacionDb >= 20;
            Lector.Paso(marca);
            CerradoMs = Umbral.Abierto ? 0 : CerradoMs + Ms;
        }
    }
}
