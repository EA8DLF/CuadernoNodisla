using System.Timers;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;
using Temporizador = System.Timers.Timer;

namespace Nodisla.Cuaderno.Ui.Desarrollo;

/// <summary>
/// Dos programas de modos digitales de mentira: uno hablando como WSJT-X y otro como JTDX.
/// </summary>
/// <remarks>
/// Son dos a proposito, y con capacidades distintas a proposito. WSJT-X admite que el cuaderno
/// le diga a quien llamar y que resalte indicativos en su ventana; JTDX no tiene el mensaje de
/// resaltado. La interfaz tiene que ensenar esa diferencia desactivando el boton que no se
/// puede usar, en vez de dejar que el operador lo pulse y no pase nada. Con una sola instancia
/// falsa, eso no se habria podido probar.
///
/// El ritmo tambien es el de verdad: FT8 decodifica en periodos de quince segundos, asi que
/// las decodificaciones llegan a rafagas y no de una en una.
/// </remarks>
public sealed class PuenteDigitalSimulado : IPuenteDigital, IAsyncDisposable
{
    /// <summary>Instancia que habla como WSJT-X y admite todo.</summary>
    public const string InstanciaCompleta = "WSJT-X · principal";

    /// <summary>Instancia que habla como JTDX y no sabe resaltar.</summary>
    public const string InstanciaLimitada = "JTDX · segundo receptor";

    private static readonly string[] Llamantes =
    [
        "JA1NUT", "VK6LC", "ZL3ABC", "K1ABC", "W6XYZ", "PY2NY", "ZS6CCY", "OH2BH",
        "LA5HE", "DL1XYZ", "G3TXF", "F6BEE", "I4UQF", "SM5EDX", "UA3LMR", "VE7CQ",
        "9G5AR", "3B8CF", "HK3TU", "CE3FZ", "VU2PTT", "BY1CQ", "YB0ANN", "TF3IRA",
    ];

    private static readonly string[] Localizadores =
    [
        "PM95", "OF78", "RE66", "FN42", "CM87", "GG66", "KG33", "KP20",
        "JO59", "JO62", "IO91", "JN18", "JN54", "JO89", "KO85", "CN89",
    ];

    private readonly Temporizador _periodo = new(7_500) { AutoReset = true };
    private readonly Random _azar = new(14_074);
    private readonly Dictionary<string, EstadoDigital> _instancias = [];

    private int _vueltas;
    private bool _liberado;

    /// <summary>Monta el puente con las dos instancias apagadas.</summary>
    public PuenteDigitalSimulado() => _periodo.Elapsed += AlTerminarElPeriodo;

    /// <inheritdoc />
    public event EventHandler<DecodificacionDigital>? Decodificado;

    /// <inheritdoc />
    public event EventHandler<EstadoDigital>? EstadoRecibido;

    /// <inheritdoc />
    public event EventHandler<Qso>? QsoRegistrado;

    /// <inheritdoc />
    public event EventHandler<string>? InstanciaPerdida;

    /// <inheritdoc />
    public event EventHandler<string>? AdifRecibido;

    /// <inheritdoc />
    public int Puerto => 2237;

    /// <inheritdoc />
    public IReadOnlyCollection<EstadoDigital> Instancias
    {
        get
        {
            lock (_instancias) return [.. _instancias.Values];
        }
    }

    /// <summary>
    /// Que sabe hacer cada instancia.
    /// </summary>
    /// <remarks>
    /// Las dos instancias simuladas no admiten lo mismo a proposito: JTDX no tiene el mensaje
    /// de resaltado ni el de configuracion. Asi el panel se puede construir viendo de verdad
    /// como se desactivan los botones que no se pueden usar.
    /// </remarks>
    /// <inheritdoc />
    public CapacidadesDigitales Capacidades(string identificador)
    {
        lock (_instancias)
        {
            if (!_instancias.ContainsKey(identificador)) return CapacidadesDigitales.Desconocidas;
        }

        // Las capacidades son las de verdad de cada dialecto, no unas inventadas: si el
        // simulado mintiera, la pantalla se disenaria contra un protocolo que no existe.
        //
        //   · JTDX no tiene el mensaje de resaltado ni el de configuracion.
        //   · WSJT-X no deja mover el tono de TRANSMISION desde fuera: su mensaje de
        //     configuracion fija el de RECEPCION. Eso solo lo permite JTDX.
        return string.Equals(identificador, InstanciaLimitada, StringComparison.Ordinal)
            ? new CapacidadesDigitales(
                PuedeResponder: true,
                PuedeResaltar: false,
                PuedeCambiarTonoTx: true,
                PuedeLlamarCq: true,
                PuedeCambiarConfiguracion: false)
            : new CapacidadesDigitales(
                PuedeResponder: true,
                PuedeResaltar: true,
                PuedeCambiarTonoTx: false,
                PuedeLlamarCq: true,
                PuedeCambiarConfiguracion: true);
    }

    /// <inheritdoc />
    public Task ArrancarAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        Anunciar(InstanciaCompleta, DialectoDigital.WsjtX, 14.074m, "FT8");
        Anunciar(InstanciaLimitada, DialectoDigital.Jtdx, 7.074m, "FT8");

        _periodo.Start();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task PararAsync(CancellationToken ct = default)
    {
        _periodo.Stop();

        string[] perdidas;
        lock (_instancias)
        {
            perdidas = [.. _instancias.Keys];
            _instancias.Clear();
        }

        foreach (var identificador in perdidas) InstanciaPerdida?.Invoke(this, identificador);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> ResponderAAsync(DecodificacionDigital decodificacion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(decodificacion);
        ct.ThrowIfCancellationRequested();

        var identificador = decodificacion.Identificador;
        if (!Capacidades(identificador).PuedeResponder) return Task.FromResult(false);

        lock (_instancias)
        {
            if (_instancias.TryGetValue(identificador, out var estado))
            {
                _instancias[identificador] = estado with
                {
                    Llamado = decodificacion.Llamante,
                    Transmitiendo = true,
                    RecibidoUtc = DateTimeOffset.UtcNow,
                    MensajeEnTransmision = $"{decodificacion.Llamante.Valor} EA8DLF IL18",
                };
            }
        }

        AvisarDelEstado(identificador);
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> ResaltarAsync(
        string identificador,
        Indicativo indicativo,
        bool esNuevo,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Capacidades(identificador).PuedeResaltar);
    }

    /// <inheritdoc />
    public Task<bool> PonerTonoTxAsync(string identificador, int tonoHz, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!Capacidades(identificador).PuedeCambiarTonoTx) return Task.FromResult(false);

        Cambiar(identificador, estado => estado with
        {
            TonoTxHz = Math.Clamp(tonoHz, 200, 3_000),
            RecibidoUtc = DateTimeOffset.UtcNow,
        });

        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> LlamarCqAsync(string identificador, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!Capacidades(identificador).PuedeLlamarCq) return Task.FromResult(false);

        Cambiar(identificador, estado => estado with
        {
            Transmitiendo = true,
            Llamado = Indicativo.Vacio,
            MensajeEnTransmision = "CQ EA8DLF IL18",
            RecibidoUtc = DateTimeOffset.UtcNow,
        });

        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> CambiarConfiguracionAsync(
        string identificador,
        string configuracion,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!Capacidades(identificador).PuedeCambiarConfiguracion) return Task.FromResult(false);

        Cambiar(identificador, estado => estado with
        {
            Configuracion = configuracion,
            RecibidoUtc = DateTimeOffset.UtcNow,
        });

        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (_liberado) return ValueTask.CompletedTask;
        _liberado = true;

        _periodo.Elapsed -= AlTerminarElPeriodo;
        _periodo.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Termina un periodo de decodificacion y suelta de golpe todo lo que se ha oido, que es
    /// como funciona FT8 de verdad.
    /// </summary>
    private void AlTerminarElPeriodo(object? origen, ElapsedEventArgs e)
    {
        if (_liberado) return;

        _vueltas++;
        var instante = DateTimeOffset.UtcNow;

        foreach (var estado in Instancias)
        {
            var cuantas = _azar.Next(3, 9);
            for (var i = 0; i < cuantas; i++)
            {
                Decodificado?.Invoke(this, Inventar(estado, instante));
            }
        }

        // De vez en cuando el programa da un contacto por cerrado y lo manda al cuaderno, y
        // ademas manda su ADIF, que trae campos que el aviso binario no lleva.
        if (_vueltas % 4 != 0) return;

        var qso = InventarQso(instante);
        QsoRegistrado?.Invoke(this, qso);
        AdifRecibido?.Invoke(this, AdifDe(qso));
    }

    /// <summary>Escribe el contacto en ADIF, como hace WSJT-X al cerrarlo.</summary>
    private static string AdifDe(Qso qso)
    {
        static string Campo(string nombre, string valor) =>
            valor.Length == 0
                ? string.Empty
                : $"<{nombre}:{valor.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)}>{valor}";

        var inicio = qso.InicioUtc.UtcDateTime;

        return string.Concat(
            Campo("call", qso.Call.Valor),
            Campo("gridsquare", qso.Gridsquare.Valor),
            Campo("mode", qso.Mode.NombreUsual),
            Campo("band", qso.Band.Nombre ?? string.Empty),
            Campo("freq", qso.Freq.AAdif()),
            Campo("qso_date", inicio.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture)),
            Campo("time_on", inicio.ToString("HHmmss", System.Globalization.CultureInfo.InvariantCulture)),
            Campo("rst_sent", qso.RstSent.Texto),
            Campo("rst_rcvd", qso.RstRcvd.Texto),
            Campo("station_callsign", qso.StationCallsign.Valor),
            "<eor>");
    }

    private DecodificacionDigital Inventar(EstadoDigital estado, DateTimeOffset instante)
    {
        var llamante = Indicativo.Crudo(Llamantes[_azar.Next(Llamantes.Length)]);
        var esCq = _azar.Next(3) != 0;
        var locator = Locator.Parse(Localizadores[_azar.Next(Localizadores.Length)]);
        var decibelios = _azar.Next(-24, 6);

        var texto = esCq
            ? $"CQ {llamante.Valor} {locator.Valor}"
            : $"EA8DLF {llamante.Valor} {(_azar.Next(2) == 0 ? locator.Valor : decibelios.ToString("+00;-00", System.Globalization.CultureInfo.InvariantCulture))}";

        return new DecodificacionDigital(
            estado.Identificador,
            texto,
            decibelios,
            Math.Round((_azar.NextDouble() - 0.5) * 1.4, 1),
            _azar.Next(300, 2600),
            estado.Modo,
            instante,
            estado.Dialecto)
        {
            Llamante = llamante,
            Llamado = esCq ? Indicativo.Vacio : Indicativo.Parse("EA8DLF"),
            Locator = locator,
            EsCq = esCq,
        };
    }

    private Qso InventarQso(DateTimeOffset instante)
    {
        var llamante = Indicativo.Crudo(Llamantes[_azar.Next(Llamantes.Length)]);
        var locator = Locator.Parse(Localizadores[_azar.Next(Localizadores.Length)]);

        return new Qso
        {
            Call = llamante,
            Band = Banda.Parse("20m"),
            Mode = Modo.Parse("FT8"),
            Freq = Frecuencia.DesdeMegahercios(14.074m),
            InicioUtc = instante.AddMinutes(-2),
            FinUtc = instante,
            RstSent = Informe.DesdeDecibelios(_azar.Next(-22, 6)),
            RstRcvd = Informe.DesdeDecibelios(_azar.Next(-22, 6)),
            Gridsquare = locator,
            StationCallsign = Indicativo.Parse("EA8DLF"),
            Operator = "EA8DLF",
            MyGridsquare = Locator.Parse("IL18SN"),
            TxPwr = 30,
            Origen = "WSJT-X simulado",
        };
    }

    private void Anunciar(string identificador, DialectoDigital dialecto, decimal mhz, string modo)
    {
        var estado = new EstadoDigital(
            dialecto,
            identificador,
            Frecuencia.DesdeMegahercios(mhz),
            Modo.Parse(modo),
            Transmitiendo: false,
            Decodificando: false,
            Llamado: Indicativo.Vacio,
            RecibidoUtc: DateTimeOffset.UtcNow)
        {
            // Lo que el programa no informa se queda nulo, y eso tambien es informacion: la
            // pantalla escribe un guion, no un cero que el operador leeria como una medida.
            TonoRxHz = 1_500,
            TonoTxHz = 1_500,
            TransmisionHabilitada = true,
            TransmiteElPrimero = dialecto == DialectoDigital.WsjtX,
            Configuracion = dialecto == DialectoDigital.WsjtX ? "Por omisión" : null,
        };

        lock (_instancias) _instancias[identificador] = estado;
        EstadoRecibido?.Invoke(this, estado);
    }

    /// <summary>Cambia el estado de una instancia y avisa del cambio.</summary>
    /// <param name="identificador">Instancia que cambia.</param>
    /// <param name="cambio">Como queda su estado.</param>
    private void Cambiar(string identificador, Func<EstadoDigital, EstadoDigital> cambio)
    {
        lock (_instancias)
        {
            if (!_instancias.TryGetValue(identificador, out var estado)) return;
            _instancias[identificador] = cambio(estado);
        }

        AvisarDelEstado(identificador);
    }

    private void AvisarDelEstado(string identificador)
    {
        EstadoDigital? estado;
        lock (_instancias) _instancias.TryGetValue(identificador, out estado);
        if (estado is not null) EstadoRecibido?.Invoke(this, estado);
    }
}
