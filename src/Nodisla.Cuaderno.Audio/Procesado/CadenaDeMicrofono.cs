namespace Nodisla.Cuaderno.Audio.Procesado;

/// <summary>
/// Lo que se le hace a la voz del microfono del PC antes de mandarla al equipo: puerta de
/// ruido, ecualizador sencillo, compresor y un tope final.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nunca sube el nivel.</b> La puerta y el compresor solo bajan (el compresor no tiene
/// ganancia de recuperacion). El ecualizador si puede realzar una banda, pero detras va una
/// guarda: en cada bloque, el pico de lo que sale no pasa del pico de lo que entro ni del techo.
/// La guarda baja la ganancia en el acto y la recupera en rampa, sin pasar nunca del limite
/// del bloque, asi que se cumple muestra a muestra.
/// </para>
/// <para>
/// Lo que entra es el audio con la ganancia del micro ya aplicada: «lo que habia antes» es lo
/// que habria salido sin procesado. Con el procesado apagado, pasa tal cual.
/// </para>
/// </remarks>
public sealed class CadenaDeMicrofono : IProcesadorDeAudio
{
    private readonly FiltroBicuadratico _pasoAlto = new();
    private readonly FiltroBicuadratico _graves = new();
    private readonly FiltroBicuadratico _medios = new();
    private readonly FiltroBicuadratico _agudos = new();

    private int _frecuencia;
    private int _versionAplicada = -1;
    private int _version;

    private double _envolventePuerta;
    private double _gananciaPuerta = 1.0;
    private int _muestrasCerrando;
    private double _envolventeCompresor;
    private double _gananciaGuarda = 1.0;

    private volatile bool _activo;
    private volatile bool _puertaActiva = true;
    private double _umbralPuertaDb = -50;
    private int _corteDeGravesHz = 100;
    private double _gravesDb;
    private double _mediosDb;
    private double _agudosDb;
    private volatile bool _compresorActivo = true;
    private double _umbralCompresorDb = -18;
    private double _relacion = 3;
    private double _techoDb = -1;

    /// <summary>Procesado del micro encendido. Apagado, la voz pasa sin tocar.</summary>
    public bool Activo { get => _activo; set => _activo = value; }

    /// <summary>Puerta de ruido encendida.</summary>
    public bool PuertaActiva { get => _puertaActiva; set => _puertaActiva = value; }

    /// <summary>Por debajo de esto la puerta se cierra (dBFS, de -80 a -20).</summary>
    public double UmbralPuertaDb
    {
        get => Volatile.Read(ref _umbralPuertaDb);
        set => Volatile.Write(ref _umbralPuertaDb, Acotar(value, -80, -20, -50));
    }

    /// <summary>Corte de graves en hercios: 0 lo quita.</summary>
    public int CorteDeGravesHz
    {
        get => Volatile.Read(ref _corteDeGravesHz);
        set
        {
            Volatile.Write(ref _corteDeGravesHz, value <= 0 ? 0 : Math.Clamp(value, 50, 500));
            Interlocked.Increment(ref _version);
        }
    }

    /// <summary>Graves (estanteria a 200 Hz), de -12 a +6 dB.</summary>
    public double GravesDb { get => Volatile.Read(ref _gravesDb); set => PonerBanda(ref _gravesDb, value); }

    /// <summary>Medios (campana a 1.200 Hz), de -12 a +6 dB.</summary>
    public double MediosDb { get => Volatile.Read(ref _mediosDb); set => PonerBanda(ref _mediosDb, value); }

    /// <summary>Agudos (estanteria a 2.500 Hz), de -12 a +6 dB.</summary>
    public double AgudosDb { get => Volatile.Read(ref _agudosDb); set => PonerBanda(ref _agudosDb, value); }

    /// <summary>Compresor encendido.</summary>
    public bool CompresorActivo { get => _compresorActivo; set => _compresorActivo = value; }

    /// <summary>Umbral del compresor en dBFS (de -40 a 0).</summary>
    public double UmbralCompresorDb
    {
        get => Volatile.Read(ref _umbralCompresorDb);
        set => Volatile.Write(ref _umbralCompresorDb, Acotar(value, -40, 0, -18));
    }

    /// <summary>Relacion del compresor (de 1 a 10).</summary>
    public double Relacion
    {
        get => Volatile.Read(ref _relacion);
        set => Volatile.Write(ref _relacion, Acotar(value, 1, 10, 3));
    }

    /// <summary>Techo final en dBFS (de -12 a -1). No se puede subir por encima de -1.</summary>
    public double TechoDb
    {
        get => Volatile.Read(ref _techoDb);
        set => Volatile.Write(ref _techoDb, Acotar(value, -12, -1, -1));
    }

    /// <summary>La puerta esta cerrada ahora (para el panel).</summary>
    public bool PuertaCerrada => _activo && _puertaActiva && _gananciaPuerta < 0.5;

    /// <summary>Reduccion del compresor y la guarda en curso, en dB.</summary>
    public double ReduccionDb { get; private set; }

    /// <inheritdoc />
    public void Procesar(Span<float> muestras, int frecuencia)
    {
        if (!_activo || frecuencia <= 0 || muestras.IsEmpty) return;

        if (frecuencia != _frecuencia)
        {
            _frecuencia = frecuencia;
            _versionAplicada = -1;
            _envolventePuerta = 0;
            _gananciaPuerta = 1;
            _envolventeCompresor = 0;
            _gananciaGuarda = 1;
        }

        var version = Volatile.Read(ref _version);
        if (version != _versionAplicada)
        {
            Recalcular(frecuencia);
            _versionAplicada = version;
        }

        // Lo que habia antes: el pico de referencia de este bloque.
        var picoDeEntrada = 0f;
        for (var i = 0; i < muestras.Length; i++)
        {
            if (!float.IsFinite(muestras[i])) muestras[i] = 0f;
            picoDeEntrada = Math.Max(picoDeEntrada, Math.Abs(muestras[i]));
        }

        if (_puertaActiva) Puerta(muestras, frecuencia);

        _pasoAlto.Procesar(muestras);
        _graves.Procesar(muestras);
        _medios.Procesar(muestras);
        _agudos.Procesar(muestras);

        var reduccionCompresor = _compresorActivo ? Comprimir(muestras, frecuencia) : 1.0;

        Guardar(muestras, Math.Min(picoDeEntrada, Limitador.TechoLineal(TechoDb)), frecuencia);
        ReduccionDb = 20.0 * Math.Log10(Math.Max(1e-6, Math.Min(reduccionCompresor, _gananciaGuarda)));
    }

    private void Puerta(Span<float> muestras, int frecuencia)
    {
        var umbral = Math.Pow(10, UmbralPuertaDb / 20);
        var cierre = umbral * 0.7;
        var caidaEnvolvente = Math.Exp(-1.0 / (0.010 * frecuencia));
        var abrir = 1.0 - Math.Exp(-1.0 / (0.002 * frecuencia));
        var cerrar = 1.0 - Math.Exp(-1.0 / (0.120 * frecuencia));
        var espera = (int)(0.150 * frecuencia);
        const double Cerrada = 0.03; // unos -30 dB: no corta en seco, deja el fondo muy bajo

        for (var i = 0; i < muestras.Length; i++)
        {
            var a = Math.Abs(muestras[i]);
            _envolventePuerta = a > _envolventePuerta ? a : _envolventePuerta * caidaEnvolvente;

            double objetivo;
            if (_envolventePuerta >= umbral)
            {
                _muestrasCerrando = espera;
                objetivo = 1.0;
            }
            else if (_envolventePuerta >= cierre || _muestrasCerrando > 0)
            {
                if (_muestrasCerrando > 0) _muestrasCerrando--;
                objetivo = _gananciaPuerta > 0.5 ? 1.0 : Cerrada;
            }
            else
            {
                objetivo = Cerrada;
            }

            _gananciaPuerta += (objetivo - _gananciaPuerta) * (objetivo > _gananciaPuerta ? abrir : cerrar);
            if (_gananciaPuerta > 1.0) _gananciaPuerta = 1.0;
            muestras[i] = (float)(muestras[i] * _gananciaPuerta);
        }
    }

    private double Comprimir(Span<float> muestras, int frecuencia)
    {
        var umbralDb = UmbralCompresorDb;
        var pendiente = 1.0 - (1.0 / Relacion);
        var ataque = 1.0 - Math.Exp(-1.0 / (0.003 * frecuencia));
        var suelta = 1.0 - Math.Exp(-1.0 / (0.150 * frecuencia));
        var minima = 1.0;

        for (var i = 0; i < muestras.Length; i++)
        {
            var a = Math.Abs(muestras[i]);
            _envolventeCompresor += (a - _envolventeCompresor) * (a > _envolventeCompresor ? ataque : suelta);
            var nivelDb = 20.0 * Math.Log10(Math.Max(_envolventeCompresor, 1e-9));
            var g = 1.0;
            if (nivelDb > umbralDb) g = Math.Pow(10, -(nivelDb - umbralDb) * pendiente / 20.0);
            if (g > 1.0) g = 1.0;
            if (g < minima) minima = g;
            muestras[i] = (float)(muestras[i] * g);
        }

        return minima;
    }

    /// <summary>
    /// La guarda: ninguna muestra del bloque pasa de <paramref name="limite"/>. Baja en el acto,
    /// sube en rampa hasta la ganancia permitida, nunca por encima.
    /// </summary>
    private void Guardar(Span<float> muestras, float limite, int frecuencia)
    {
        var pico = 0f;
        for (var i = 0; i < muestras.Length; i++) pico = Math.Max(pico, Math.Abs(muestras[i]));

        var permitida = pico > limite && pico > 0 ? limite / pico : 1.0;
        var anterior = _gananciaGuarda;

        if (permitida <= anterior)
        {
            for (var i = 0; i < muestras.Length; i++) muestras[i] = (float)(muestras[i] * permitida);
            _gananciaGuarda = permitida;
        }
        else
        {
            // Recuperar en unos 50 ms, pero sin pasar de lo permitido en este bloque.
            var paso = muestras.Length / (0.050 * frecuencia);
            var destino = Math.Min(permitida, anterior + ((1.0 - anterior) * Math.Min(1.0, paso)));
            for (var i = 0; i < muestras.Length; i++)
            {
                var g = anterior + ((destino - anterior) * (i + 1) / muestras.Length);
                muestras[i] = (float)(muestras[i] * g);
            }

            _gananciaGuarda = destino;
        }

        // Por el redondeo de la coma flotante.
        for (var i = 0; i < muestras.Length; i++) muestras[i] = Math.Clamp(muestras[i], -limite, limite);
    }

    private void Recalcular(int frecuencia)
    {
        var corte = CorteDeGravesHz;
        if (corte > 0 && corte < frecuencia / 2) _pasoAlto.PasoAlto(frecuencia, corte);
        else _pasoAlto.Neutro();

        Banda(_graves, GravesDb, db => _graves.EstanteriaBaja(frecuencia, 200, db));
        Banda(_medios, MediosDb, db => _medios.Campana(frecuencia, 1200, 0.9, db));
        Banda(_agudos, AgudosDb, db => _agudos.EstanteriaAlta(frecuencia, Math.Min(2500, frecuencia * 0.4), db));

        _pasoAlto.Reiniciar();
        _graves.Reiniciar();
        _medios.Reiniciar();
        _agudos.Reiniciar();
    }

    private static void Banda(FiltroBicuadratico filtro, double db, Action<double> poner)
    {
        if (Math.Abs(db) < 0.05) filtro.Neutro();
        else poner(db);
    }

    private void PonerBanda(ref double campo, double valor)
    {
        Volatile.Write(ref campo, Acotar(valor, -12, 6, 0));
        Interlocked.Increment(ref _version);
    }

    private static double Acotar(double valor, double minimo, double maximo, double siNoEsNumero) =>
        double.IsFinite(valor) ? Math.Clamp(valor, minimo, maximo) : siNoEsNumero;
}
