using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Servidores;
using Serilog;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>Un programa conectado, en la lista de Configuracion.</summary>
/// <param name="Id">Para desconectarlo.</param>
/// <param name="Protocolo">rigctld o TCI.</param>
/// <param name="Direccion">IP y puerto.</param>
/// <param name="Desde">Hora local de conexion.</param>
/// <param name="Transmitiendo">Tiene el PTT.</param>
/// <param name="Ordenes">Ordenes atendidas y rechazadas.</param>
public sealed record FilaDeClienteExterno(Guid Id, string Protocolo, string Direccion, string Desde, bool Transmitiendo, string Ordenes);

/// <summary>
/// Configuracion › «Servidor para otros programas»: rigctld y TCI, red, permisos de TX, clientes
/// en vivo y el registro de quien pidio transmitir.
/// </summary>
/// <remarks>
/// Abrir a la red local y dejar transmitir a los programas externos piden confirmacion. Nada
/// arranca solo: de fabrica todo esta apagado.
/// </remarks>
public sealed partial class VistaModeloServidores : ObservableObject
{
    private readonly ServidoresParaOtrosProgramas _servidores;
    private readonly string? _carpeta;

    /// <summary>Monta el apartado con los ajustes guardados.</summary>
    /// <param name="servidores">Los servidores.</param>
    /// <param name="carpeta">Carpeta de datos; nula, no se guarda.</param>
    public VistaModeloServidores(ServidoresParaOtrosProgramas servidores, string? carpeta)
    {
        ArgumentNullException.ThrowIfNull(servidores);
        _servidores = servidores;
        _carpeta = carpeta;
        Cargar(OpcionesDeServidores.Leer(carpeta));
        servidores.Radio.ClientesCambiados += (_, _) => Conversores.Hilo.EnLaVentana(Refrescar);
        servidores.Radio.TxPedida += (_, _) => Conversores.Hilo.EnLaVentana(Refrescar);
        servidores.EstadoCambiado += (_, _) => Conversores.Hilo.EnLaVentana(Refrescar);
        Textos.AlCambiar(this, static vm => vm.Refrescar());
        Refrescar();
    }

    /// <summary>La pregunta antes de abrir a la red local o dejar transmitir. Sin ella, no se hace.</summary>
    public Func<string, string, bool>? Confirmar { get; set; }

    /// <summary>Servidor rigctld encendido.</summary>
    [ObservableProperty]
    private bool _rigctldActivo;

    /// <summary>Puerto del servidor rigctld.</summary>
    [ObservableProperty]
    private string _puertoRigctld = string.Empty;

    /// <summary>Servidor TCI encendido.</summary>
    [ObservableProperty]
    private bool _tciActivo;

    /// <summary>Puerto del servidor TCI.</summary>
    [ObservableProperty]
    private string _puertoTci = string.Empty;

    /// <summary>Escuchar tambien en la red local.</summary>
    [ObservableProperty]
    private bool _abrirALaRedLocal;

    /// <summary>IP o redes permitidas, separadas por comas o espacios.</summary>
    [ObservableProperty]
    private string _ipsPermitidas = string.Empty;

    /// <summary>Los programas pueden cambiar la radio.</summary>
    [ObservableProperty]
    private bool _permitirControl;

    /// <summary>Los programas pueden transmitir.</summary>
    [ObservableProperty]
    private bool _permitirTx;

    /// <summary>Contraseña del TCI.</summary>
    [ObservableProperty]
    private string _tokenTci = string.Empty;

    /// <summary>Mandar spots a los clientes TCI.</summary>
    [ObservableProperty]
    private bool _enviarSpots;

    /// <summary>Pintar los spots de los clientes TCI.</summary>
    [ObservableProperty]
    private bool _recibirSpots;

    /// <summary>Un aviso tras guardar.</summary>
    [ObservableProperty]
    private string _aviso = string.Empty;

    /// <summary>Como esta el servidor rigctld.</summary>
    public string EstadoRigctld => Describir(_servidores.Rigctld);

    /// <summary>Como esta el servidor TCI.</summary>
    public string EstadoTci => Describir(_servidores.Tci);

    /// <summary>Los programas conectados.</summary>
    public ObservableCollection<FilaDeClienteExterno> Clientes { get; } = [];

    /// <summary>Las ultimas peticiones de transmitir, ya en texto.</summary>
    public ObservableCollection<string> Peticiones { get; } = [];

    /// <summary>Hay peticiones de transmitir en el registro.</summary>
    public bool HayPeticiones => Peticiones.Count > 0;

    /// <summary>Hay algun programa conectado.</summary>
    public bool HayClientes => Clientes.Count > 0;

    /// <summary>Algun servidor esta escuchando (para el indicador de la barra de estado).</summary>
    public bool HayAlgunoActivo => _servidores.Rigctld.Encendido || _servidores.Tci.Encendido;

    /// <summary>El indicador de la barra de estado: «Programas externos: 2».</summary>
    public string IndicadorTexto => Textos.F("Ajustes.Servidor.Indicador", Clientes.Count);

    /// <summary>El consejo del indicador: quien esta conectado.</summary>
    public string IndicadorConsejo => Clientes.Count == 0
        ? Textos.T("Ajustes.Servidor.Indicador.Nadie")
        : string.Join(Environment.NewLine, Clientes.Select(c => $"{c.Protocolo} {c.Direccion}{(c.Transmitiendo ? " · TX" : string.Empty)}"));

    /// <summary>Alguien transmite ahora por un servidor.</summary>
    public bool AlguienTransmite => Clientes.Any(c => c.Transmitiendo);

    partial void OnAbrirALaRedLocalChanged(bool value)
    {
        if (!value || _cargando) return;
        if (Confirmar is not { } preguntar || !preguntar(Textos.T("Ajustes.Servidor.ConfirmarRed.Titulo"), Textos.T("Ajustes.Servidor.ConfirmarRed")))
        {
            _cargando = true;
            AbrirALaRedLocal = false;
            _cargando = false;
        }
    }

    partial void OnPermitirTxChanged(bool value)
    {
        if (!value || _cargando) return;
        if (Confirmar is not { } preguntar || !preguntar(Textos.T("Ajustes.Servidor.ConfirmarTx.Titulo"), Textos.T("Ajustes.Servidor.ConfirmarTx")))
        {
            _cargando = true;
            PermitirTx = false;
            _cargando = false;
        }
    }

    private bool _cargando;

    /// <summary>Guarda los ajustes y los aplica: arranca, para o reinicia lo que haga falta.</summary>
    [RelayCommand]
    public async Task GuardarAsync()
    {
        if (!Puerto(PuertoRigctld, out var rigctld) || !Puerto(PuertoTci, out var tci) || rigctld == tci)
        {
            Aviso = Textos.T("Ajustes.Servidor.PuertoMalo");
            return;
        }

        var ips = IpsPermitidas.Split([',', ';', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries);
        var malas = ips.Where(i => !ControlDeAcceso.EntradaValida(i)).ToList();
        if (malas.Count > 0)
        {
            Aviso = Textos.F("Ajustes.Servidor.IpMala", string.Join(", ", malas));
            return;
        }

        var opciones = new OpcionesDeServidores
        {
            RigctldActivo = RigctldActivo,
            PuertoRigctld = rigctld,
            TciActivo = TciActivo,
            PuertoTci = tci,
            AbrirALaRedLocal = AbrirALaRedLocal,
            IpsPermitidas = [.. ips],
            PermitirControl = PermitirControl,
            PermitirTx = PermitirTx,
            TokenTci = TokenTci,
            EnviarSpots = EnviarSpots,
            RecibirSpots = RecibirSpots,
        }.Acotar();

        try
        {
            if (!string.IsNullOrEmpty(_carpeta)) opciones.Guardar(_carpeta);
            Log.Information(
                "Servidor para otros programas: rigctld {Rigctld} ({PuertoRigctld}), TCI {Tci} ({PuertoTci}), red local {Red}, TX {Tx}.",
                opciones.RigctldActivo,
                opciones.PuertoRigctld,
                opciones.TciActivo,
                opciones.PuertoTci,
                opciones.AbrirALaRedLocal,
                opciones.PermitirTx);
            await _servidores.AplicarAsync(opciones).ConfigureAwait(true);
            Aviso = Textos.T("Ajustes.Servidor.Guardado");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error(ex, "No se han podido guardar los ajustes del servidor para otros programas.");
            Aviso = Textos.F("Ajustes.Servidor.NoGuarda", ex.Message);
        }

        Refrescar();
    }

    /// <summary>Desconecta un programa (primero se le baja el PTT).</summary>
    /// <param name="fila">El programa.</param>
    [RelayCommand]
    public async Task DesconectarAsync(FilaDeClienteExterno? fila)
    {
        if (fila is null) return;
        Log.Warning("El operador desconecta al programa externo {Protocolo} {Direccion}.", fila.Protocolo, fila.Direccion);
        await _servidores.Radio.DesconectarAsync(fila.Id).ConfigureAwait(true);
        Refrescar();
    }

    /// <summary>Vuelve a leer clientes, peticiones y estado.</summary>
    public void Refrescar()
    {
        Clientes.Clear();
        foreach (var c in _servidores.Radio.Clientes)
        {
            Clientes.Add(new FilaDeClienteExterno(
                c.Id,
                c.Protocolo == ProtocoloExterno.Rigctld ? "rigctld" : "TCI",
                c.Direccion,
                c.DesdeUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture),
                c.Transmitiendo,
                Textos.F("Ajustes.Servidor.Ordenes", c.Ordenes, c.Rechazadas)));
        }

        Peticiones.Clear();
        foreach (var p in _servidores.Radio.Peticiones.Take(12))
        {
            Peticiones.Add(Textos.F(
                p.Concedida ? "Ajustes.Servidor.Peticion.Concedida" : "Ajustes.Servidor.Peticion.Denegada",
                p.Utc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                p.Cliente,
                p.Frecuencia.Kilohercios.ToString("0.0", CultureInfo.CurrentCulture),
                p.Modo.NombreUsual ?? string.Empty,
                p.Detalle));
        }

        OnPropertyChanged(nameof(EstadoRigctld));
        OnPropertyChanged(nameof(EstadoTci));
        OnPropertyChanged(nameof(HayClientes));
        OnPropertyChanged(nameof(HayPeticiones));
        OnPropertyChanged(nameof(HayAlgunoActivo));
        OnPropertyChanged(nameof(IndicadorTexto));
        OnPropertyChanged(nameof(IndicadorConsejo));
        OnPropertyChanged(nameof(AlguienTransmite));
    }

    private void Cargar(OpcionesDeServidores o)
    {
        _cargando = true;
        RigctldActivo = o.RigctldActivo;
        PuertoRigctld = o.PuertoRigctld.ToString(CultureInfo.InvariantCulture);
        TciActivo = o.TciActivo;
        PuertoTci = o.PuertoTci.ToString(CultureInfo.InvariantCulture);
        AbrirALaRedLocal = o.AbrirALaRedLocal;
        IpsPermitidas = string.Join(", ", o.IpsPermitidas);
        PermitirControl = o.PermitirControl;
        PermitirTx = o.PermitirTx;
        TokenTci = o.TokenTci ?? string.Empty;
        EnviarSpots = o.EnviarSpots;
        RecibirSpots = o.RecibirSpots;
        _cargando = false;
    }

    /// <summary>Lo guardado, tal cual (para arrancar los servidores al abrir el programa).</summary>
    /// <returns>Los ajustes.</returns>
    public OpcionesDeServidores Guardadas() => OpcionesDeServidores.Leer(_carpeta);

    private static bool Puerto(string texto, out int puerto) =>
        int.TryParse(texto, NumberStyles.Integer, CultureInfo.InvariantCulture, out puerto) && puerto is >= 1024 and <= 65535;

    private static string Describir(EstadoDeServidor estado) => estado switch
    {
        { Error: { Length: > 0 } error } => error,
        { Encendido: true, Punto: { } punto } => Textos.F("Ajustes.Servidor.Escuchando", punto),
        _ => Textos.T("Ajustes.Servidor.Apagado"),
    };
}
