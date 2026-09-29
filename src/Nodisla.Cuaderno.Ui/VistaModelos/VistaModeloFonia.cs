using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Audio.Fonia;
using Nodisla.Cuaderno.Radio.Control.Ft710;
using Nodisla.Cuaderno.Ui.Conversores;
using Serilog;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>
/// El panel «Fonía (SSB/FM/AM)» de Operar: escuchar la radio por los altavoces del PC y
/// hablar por el micro del PC con un PTT en pantalla.
/// </summary>
/// <remarks>
/// <para>
/// El PTT no se toca aqui: se le pide a <see cref="ControlDeFonia"/>, que va siempre por el
/// vigilante. Esta clase decide <b>cuando se puede</b>: con el equipo conectado, en un modo de
/// voz y con los dispositivos elegidos. En DATA, CW o RTTY el PTT de fonia se apaga para no
/// mezclarse con el modem.
/// </para>
/// <para>
/// Se suelta tambien al perder el foco del programa: un PTT mantenido con el raton que se
/// queda sin su «soltar» porque otra ventana se llevo el foco es la forma mas tonta de dejar
/// una radio en el aire.
/// </para>
/// </remarks>
public sealed partial class VistaModeloFonia : ObservableObject
{
    private static readonly string[] ModosDeVoz = ["USB", "LSB", "SSB", "AM", "FM", "AMN", "FMN"];

    private readonly ControlDeFonia _control;
    private readonly VistaModeloEquipo _equipo;
    private readonly IControlEquipo? _controlDelEquipo;
    private string _modoComprobado = string.Empty;

    /// <summary>Monta el panel. No abre ningun dispositivo.</summary>
    /// <param name="ajustes">El apartado de ajustes de fonia.</param>
    /// <param name="control">La fonia, con su vigilante.</param>
    /// <param name="equipo">El equipo en pantalla: se mira si esta conectado y en que modo.</param>
    /// <param name="controlDelEquipo">Para leer por CAT la fuente de modulacion; puede ser nulo.</param>
    public VistaModeloFonia(
        VistaModeloAjustesFonia ajustes,
        ControlDeFonia control,
        VistaModeloEquipo equipo,
        IControlEquipo? controlDelEquipo = null)
    {
        ArgumentNullException.ThrowIfNull(ajustes);
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(equipo);

        Ajustes = ajustes;
        _control = control;
        _equipo = equipo;
        _controlDelEquipo = controlDelEquipo;

        AplicarAjustes();
        Ajustes.Cambiado += (_, que) => AlCambiarLosAjustes(que);
        Ajustes.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(VistaModeloAjustesFonia.EscuchaConfigurada)
                or nameof(VistaModeloAjustesFonia.TransmisionConfigurada))
            {
                RecalcularSiSePuede();
            }
        };

        _equipo.PropertyChanged += AlCambiarElEquipo;
        _control.TransmisionTerminada += (_, fin) => Hilo.EnLaVentana(() => AlTerminar(fin));
        _control.EscuchaPerdida += (_, fallo) => Hilo.EnLaVentana(() =>
        {
            Escuchando = false;
            Aviso = "La escucha por el PC se ha cortado: " + fallo.Message;
        });

        RecalcularSiSePuede();
    }

    /// <summary>Dispositivos, volumenes y tiempos.</summary>
    public VistaModeloAjustesFonia Ajustes { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoDelPtt))]
    private bool _transmitiendo;

    [ObservableProperty]
    private bool _escuchando;

    [ObservableProperty]
    private bool _cambiandoLaEscucha;

    [ObservableProperty]
    private string _tiempoEnElAire = string.Empty;

    [ObservableProperty]
    private double _nivelMicro;

    [ObservableProperty]
    private bool _microSatura;

    [ObservableProperty]
    private double _nivelRx;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoDelPtt))]
    private bool _puedeTransmitir;

    [ObservableProperty]
    private string _motivoDeNoPoder = string.Empty;

    [ObservableProperty]
    private string _avisoDeFuente = string.Empty;

    [ObservableProperty]
    private string _aviso = string.Empty;

    [ObservableProperty]
    private string _avisoDeEscucha = string.Empty;

    private DateTimeOffset _escuchaAbiertaUtc;

    /// <summary>Rotulo del boton grande.</summary>
    public string TextoDelPtt => Transmitiendo
        ? (Ajustes.PttConmutado ? "EN EL AIRE · clic para acabar" : "EN EL AIRE · suelte para acabar")
        : (Ajustes.PttConmutado ? "PTT · clic para hablar" : "PTT · mantenga pulsado");

    /// <summary>Lo que dice el tope de la pasada.</summary>
    public string TextoDelTope => "máx. " + Formatear(_control.TiempoMaximoEfectivo);

    /// <summary>
    /// Boton o tecla del PTT, hacia abajo. En conmutado alterna; en mantener, empieza.
    /// </summary>
    public async Task PttAbajoAsync()
    {
        if (Ajustes.PttConmutado && (Transmitiendo || _control.Transmitiendo))
        {
            await TerminarAsync(MotivoDeSuelta.Normal).ConfigureAwait(true);
            return;
        }

        await EmpezarAsync().ConfigureAwait(true);
    }

    /// <summary>Boton o tecla del PTT, hacia arriba. Solo cuenta en mantener.</summary>
    public async Task PttArribaAsync()
    {
        if (Ajustes.PttConmutado) return;
        await TerminarAsync(MotivoDeSuelta.Normal).ConfigureAwait(true);
    }

    /// <summary>El programa ha perdido el foco, o el boton ha perdido el raton: se suelta.</summary>
    public async Task AlPerderElFocoAsync()
    {
        if (!_control.Transmitiendo && !Transmitiendo) return;
        await TerminarAsync(MotivoDeSuelta.Cancelado).ConfigureAwait(true);
        Aviso = "PTT soltado: el programa ha perdido el foco.";
    }

    /// <summary>Abre o cierra la escucha por los altavoces del PC.</summary>
    [RelayCommand]
    public async Task AlternarEscuchaAsync()
    {
        if (CambiandoLaEscucha) return;
        CambiandoLaEscucha = true;
        try
        {
            if (_control.Escuchando)
            {
                await _control.PararEscuchaAsync().ConfigureAwait(true);
                Aviso = string.Empty;
            }
            else
            {
                await AbrirEscuchaAsync().ConfigureAwait(true);
            }
        }
        finally
        {
            Escuchando = _control.Escuchando;
            CambiandoLaEscucha = false;
        }
    }

    /// <summary>Abre la escucha si el operador lo dejo pedido y hay con que.</summary>
    public async Task EscucharSiSePidioAsync()
    {
        if (!Ajustes.EscucharAlArrancar || _control.Escuchando || !Ajustes.EscuchaConfigurada) return;
        await AbrirEscuchaAsync().ConfigureAwait(true);
        Escuchando = _control.Escuchando;
    }

    /// <summary>Lee por CAT la fuente de modulacion y avisa si no es USB.</summary>
    [RelayCommand]
    public async Task ComprobarFuenteAsync()
    {
        _modoComprobado = _equipo.Modo;
        AvisoDeFuente = await LeerFuenteAsync().ConfigureAwait(true);
    }

    /// <summary>Medidores y reloj de la pasada; lo llama un temporizador de la pantalla.</summary>
    public void Refrescar()
    {
        NivelRx = _control.Escuchando ? _control.Recepcion.Nivel : 0;
        NivelMicro = _control.Transmitiendo ? _control.Transmision.Nivel : 0;
        MicroSatura = _control.Transmitiendo && _control.Transmision.Saturando;
        TiempoEnElAire = _control.Transmitiendo
            ? $"{Formatear(_control.TiempoEnElAire)} / {Formatear(_control.TiempoMaximoEfectivo)}"
            : string.Empty;

        if (Transmitiendo != _control.Transmitiendo) Transmitiendo = _control.Transmitiendo;
        if (Escuchando != _control.Escuchando && !CambiandoLaEscucha) Escuchando = _control.Escuchando;

        AvisoDeEscucha = AvisoSiNoLlegaAudio(DateTimeOffset.UtcNow);

        // Red de seguridad: si algun aviso de cambio del equipo se perdiera, el boton no se
        // queda bloqueado ni habilitado de mas; se recalcula con cada vuelta del medidor.
        RecalcularSiSePuede();
    }

    /// <summary>
    /// Si la escucha esta abierta pero no llega audio —radio apagada, USB desenchufado—, lo dice.
    /// </summary>
    public string AvisoSiNoLlegaAudio(DateTimeOffset ahora)
    {
        if (!_control.Escuchando || _control.Transmitiendo) return string.Empty;

        var ultimo = _control.Recepcion.UltimoAvanceUtc ?? _escuchaAbiertaUtc;
        var parado = ahora - ultimo;
        return parado > TimeSpan.FromSeconds(2)
            ? $"No llega audio del equipo desde hace {(int)parado.TotalSeconds} s: ¿está apagada la radio o desenchufado el USB?"
            : string.Empty;
    }

    /// <summary>Suelta todo al cerrar el programa.</summary>
    public void Detener()
    {
        _equipo.PropertyChanged -= AlCambiarElEquipo;
        try
        {
            Task.Run(async () => await _control.DisposeAsync().ConfigureAwait(false)).Wait(TimeSpan.FromSeconds(3));
        }
        catch (Exception fallo)
        {
            Log.Warning(fallo, "Fallo al cerrar la fonía.");
        }
    }

    private async Task EmpezarAsync()
    {
        RecalcularSiSePuede();
        if (!PuedeTransmitir)
        {
            Aviso = MotivoDeNoPoder;
            return;
        }

        try
        {
            Aviso = string.Empty;
            await _control.EmpezarTransmisionAsync(Ajustes.Microfono!.Id, Ajustes.SalidaAlEquipo!.Id).ConfigureAwait(true);
        }
        catch (Exception fallo)
        {
            Log.Warning(fallo, "No se ha podido salir al aire en fonía.");
            Aviso = "No se ha podido transmitir: " + fallo.Message;
        }
        finally
        {
            Transmitiendo = _control.Transmitiendo;
        }
    }

    private async Task TerminarAsync(MotivoDeSuelta motivo)
    {
        try
        {
            await _control.TerminarTransmisionAsync(motivo).ConfigureAwait(true);
        }
        catch (Exception fallo)
        {
            Log.Error(fallo, "Fallo al terminar la fonía.");
        }
        finally
        {
            Transmitiendo = _control.Transmitiendo;
        }
    }

    private async Task AbrirEscuchaAsync()
    {
        if (!Ajustes.EscuchaConfigurada)
        {
            Aviso = "Elija la entrada del equipo y los altavoces del PC.";
            return;
        }

        try
        {
            _escuchaAbiertaUtc = DateTimeOffset.UtcNow;
            await _control.EmpezarEscuchaAsync(Ajustes.EntradaDelEquipo!.Id, Ajustes.Altavoces!.Id).ConfigureAwait(true);
            Aviso = string.Empty;
        }
        catch (Exception fallo)
        {
            Log.Warning(fallo, "No se ha podido abrir la escucha por el PC.");
            Aviso = "No se ha podido abrir la escucha: " + fallo.Message;
        }
    }

    private void AlTerminar(FinDeFonia fin)
    {
        Transmitiendo = false;
        TiempoEnElAire = string.Empty;
        NivelMicro = 0;
        MicroSatura = false;

        Aviso = fin.Motivo switch
        {
            MotivoDeSuelta.Normal => string.Empty,
            MotivoDeSuelta.Cancelado => Aviso,
            MotivoDeSuelta.TiempoAgotado => $"PTT soltado: se agotó el tiempo máximo ({Formatear(_control.TiempoMaximoEfectivo)}).",
            MotivoDeSuelta.SinLatido => "PTT soltado: el audio del micrófono o del equipo se paró.",
            MotivoDeSuelta.Panico => "PTT soltado con «SOLTAR PTT».",
            MotivoDeSuelta.EquipoPerdido => "PTT soltado: se perdió la comunicación con el equipo.",
            MotivoDeSuelta.Cierre => "PTT soltado: el programa se cierra.",
            _ => "PTT soltado por un fallo; mire el registro.",
        };
    }

    private void AplicarAjustes()
    {
        _control.Recepcion.Ganancia = (float)(Ajustes.VolumenRx / 100.0);
        _control.SilenciarEscucha(Ajustes.SilencioRx);
        _control.Transmision.Ganancia = (float)Math.Pow(10, Ajustes.GananciaTxDb / 20.0);
        _control.Opciones.TiempoMaximo = TimeSpan.FromSeconds(Math.Clamp(Ajustes.TiempoMaximoSegundos, 10, 600));
        OnPropertyChanged(nameof(TextoDelTope));
        OnPropertyChanged(nameof(TextoDelPtt));
    }

    private void AlCambiarLosAjustes(string que)
    {
        AplicarAjustes();

        if (que is nameof(VistaModeloAjustesFonia.EntradaDelEquipo) or nameof(VistaModeloAjustesFonia.Altavoces)
            && _control.Escuchando && Ajustes.EscuchaConfigurada)
        {
            _ = AbrirEscuchaAsync();
        }

        if (que is nameof(VistaModeloAjustesFonia.IndiceDeFuenteSsb) or nameof(VistaModeloAjustesFonia.ValorDeFuenteUsb)
            or nameof(VistaModeloAjustesFonia.ComprobarFuenteDeModulacion))
        {
            _modoComprobado = string.Empty;
            ComprobarFuenteSiToca();
        }

        RecalcularSiSePuede();
    }

    private void AlCambiarElEquipo(object? origen, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(VistaModeloEquipo.Modo) or nameof(VistaModeloEquipo.Conectado))) return;

        Hilo.EnLaVentana(() =>
        {
            RecalcularSiSePuede();

            // Si el equipo cambia a un modo que no es de voz a media pasada, se corta.
            if (_control.Transmitiendo && !EsModoDeVoz(_equipo.Modo))
            {
                _ = TerminarAsync(MotivoDeSuelta.Cancelado);
                Aviso = $"PTT soltado: el equipo ha pasado a {_equipo.Modo}.";
            }

            ComprobarFuenteSiToca();
        });
    }

    private void ComprobarFuenteSiToca()
    {
        if (!_equipo.Conectado || !EsModoSsb(_equipo.Modo))
        {
            AvisoDeFuente = string.Empty;
            _modoComprobado = string.Empty;
            return;
        }

        if (string.Equals(_modoComprobado, _equipo.Modo, StringComparison.OrdinalIgnoreCase)) return;
        _ = ComprobarFuenteAsync();
    }

    private async Task<string> LeerFuenteAsync()
    {
        if (!Ajustes.ComprobarFuenteDeModulacion) return string.Empty;

        var real = _controlDelEquipo is IControlEquipoConmutable conmutable ? conmutable.Actual : _controlDelEquipo;
        if (real is not IEquipoAvanzado avanzado || !_equipo.Conectado) return string.Empty;
        if (!avanzado.NombreDelEquipo.Contains("710", StringComparison.Ordinal)) return string.Empty;

        string orden;
        try
        {
            // Solo lectura: seis cifras y punto y coma. Con una cifra de mas seria una escritura.
            orden = MenuFt710.OrdenDeLectura(Ajustes.IndiceDeFuenteSsb);
        }
        catch (ArgumentException)
        {
            return "El índice del menú de la fuente de modulación no es válido (seis cifras).";
        }

        try
        {
            var respuesta = await avanzado.OrdenEnCrudoAsync(orden).ConfigureAwait(true);
            if (!MenuFt710.TryAnalizar(respuesta, out var lectura) || lectura is null)
            {
                return string.Empty;
            }

            return string.Equals(lectura.Texto, Ajustes.ValorDeFuenteUsb, StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : $"La radio no parece tener la fuente de modulación de SSB en USB (menú EX{lectura.Indice} = {lectura.Texto}, se esperaba {Ajustes.ValorDeFuenteUsb}). Con el micro de la radio como fuente, el audio del PC no sale al aire.";
        }
        catch (Exception fallo)
        {
            Log.Debug(fallo, "No se ha podido leer la fuente de modulación.");
            return string.Empty;
        }
    }

    private void RecalcularSiSePuede()
    {
        string motivo;
        if (!_equipo.Conectado)
        {
            motivo = "Conecte el equipo para usar el PTT de fonía.";
        }
        else if (!EsModoDeVoz(_equipo.Modo))
        {
            motivo = $"El equipo está en {_equipo.Modo}: el PTT de fonía solo va en SSB, AM y FM, para no mezclarse con el módem.";
        }
        else if (!Ajustes.TransmisionConfigurada)
        {
            motivo = "Elija el micrófono del PC y la salida hacia el equipo.";
        }
        else
        {
            motivo = string.Empty;
        }

        MotivoDeNoPoder = motivo;
        PuedeTransmitir = motivo.Length == 0;
    }

    public static bool EsModoDeVoz(string? modo) =>
        modo is not null && ModosDeVoz.Contains(modo.Trim(), StringComparer.OrdinalIgnoreCase);

    private static bool EsModoSsb(string? modo) =>
        modo is not null && (modo.Trim().Equals("USB", StringComparison.OrdinalIgnoreCase)
            || modo.Trim().Equals("LSB", StringComparison.OrdinalIgnoreCase)
            || modo.Trim().Equals("SSB", StringComparison.OrdinalIgnoreCase));

    private static string Formatear(TimeSpan tiempo) =>
        string.Create(CultureInfo.InvariantCulture, $"{(int)tiempo.TotalMinutes}:{tiempo.Seconds:00}");
}
