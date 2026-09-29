using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Ui.Ajustes;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>Una tecla elegible para el PTT de fonia.</summary>
/// <param name="Nombre">Nombre de <c>System.Windows.Input.Key</c>; vacio es ninguna.</param>
/// <param name="Texto">Lo que se lee en pantalla.</param>
public sealed record TeclaDePtt(string Nombre, string Texto);

/// <summary>
/// El apartado de fonia en Ajustes: los cuatro dispositivos, los volumenes y los tiempos.
/// </summary>
/// <remarks>
/// Lo mismo se ve desde el panel de fonia de Operar —los selectores de micro y altavoces son
/// estos mismos—, asi que cambiar en un sitio cambia en el otro. Cada cambio se guarda en el
/// acto: no hay boton de guardar que olvidar.
/// </remarks>
public sealed partial class VistaModeloAjustesFonia : ObservableObject
{
    private readonly AjustesDelPrograma _ajustes;
    private readonly string? _carpeta;
    private readonly Func<IReadOnlyList<DispositivoDeAudio>> _entradas;
    private readonly Func<IReadOnlyList<DispositivoDeAudio>> _salidas;
    private readonly Func<bool, string?> _idPorOmision;
    private bool _cargando;

    /// <summary>Monta el apartado.</summary>
    /// <param name="ajustes">Ajustes del programa.</param>
    /// <param name="carpeta">Donde se guardan; nulo para no guardar (pruebas).</param>
    /// <param name="entradas">Lista los dispositivos de captura.</param>
    /// <param name="salidas">Lista los dispositivos de reproduccion.</param>
    /// <param name="idPorOmision">
    /// Dispositivo de Windows por omision: verdadero para el de captura, falso para el de
    /// reproduccion. Nulo si no se sabe.
    /// </param>
    public VistaModeloAjustesFonia(
        AjustesDelPrograma ajustes,
        string? carpeta,
        Func<IReadOnlyList<DispositivoDeAudio>> entradas,
        Func<IReadOnlyList<DispositivoDeAudio>> salidas,
        Func<bool, string?>? idPorOmision = null)
    {
        ArgumentNullException.ThrowIfNull(ajustes);
        ArgumentNullException.ThrowIfNull(entradas);
        ArgumentNullException.ThrowIfNull(salidas);

        _ajustes = ajustes;
        _ajustes.Fonia ??= new AjustesDeFonia();
        _carpeta = carpeta;
        _entradas = entradas;
        _salidas = salidas;
        _idPorOmision = idPorOmision ?? (_ => null);

        Cargar();
    }

    /// <summary>Lo guardado, tal cual.</summary>
    public AjustesDeFonia Guardado => _ajustes.Fonia;

    /// <summary>Salta cuando cambia algo que la fonia en marcha tiene que aplicar.</summary>
    public event EventHandler<string>? Cambiado;

    /// <summary>Dispositivos de captura: la entrada del equipo y el microfono salen de aqui.</summary>
    public ObservableCollection<DispositivoDeAudio> Entradas { get; } = [];

    /// <summary>Dispositivos de reproduccion: altavoces y salida al equipo.</summary>
    public ObservableCollection<DispositivoDeAudio> Salidas { get; } = [];

    /// <summary>Teclas que se pueden usar para el PTT.</summary>
    public IReadOnlyList<TeclaDePtt> Teclas { get; } =
    [
        new(string.Empty, "Ninguna"),
        new("F10", "F10"),
        new("F11", "F11"),
        new("F12", "F12"),
        new("Pause", "Pausa"),
        new("Scroll", "Bloq Despl"),
        new("Insert", "Insert"),
        new("RightCtrl", "Ctrl derecho"),
    ];

    [ObservableProperty]
    private DispositivoDeAudio? _entradaDelEquipo;

    [ObservableProperty]
    private DispositivoDeAudio? _altavoces;

    [ObservableProperty]
    private DispositivoDeAudio? _microfono;

    [ObservableProperty]
    private DispositivoDeAudio? _salidaAlEquipo;

    [ObservableProperty]
    private int _volumenRx;

    [ObservableProperty]
    private bool _silencioRx;

    [ObservableProperty]
    private bool _escucharAlArrancar;

    [ObservableProperty]
    private double _gananciaTxDb;

    [ObservableProperty]
    private int _tiempoMaximoSegundos;

    [ObservableProperty]
    private bool _pttConmutado;

    [ObservableProperty]
    private TeclaDePtt? _teclaDelPtt;

    [ObservableProperty]
    private bool _comprobarFuenteDeModulacion;

    [ObservableProperty]
    private string _indiceDeFuenteSsb = string.Empty;

    [ObservableProperty]
    private string _valorDeFuenteUsb = string.Empty;

    /// <summary>Estan elegidos los dos dispositivos de la escucha.</summary>
    public bool EscuchaConfigurada => EntradaDelEquipo is not null && Altavoces is not null;

    /// <summary>Estan elegidos los dos dispositivos de la transmision.</summary>
    public bool TransmisionConfigurada => Microfono is not null && SalidaAlEquipo is not null;

    /// <summary>
    /// Aviso si la salida al equipo no parece la del equipo, o si el micro es la entrada del
    /// equipo: con eso se transmitiria el audio de la radio o por los altavoces del PC.
    /// </summary>
    public string AvisoDeDispositivos
    {
        get
        {
            if (SalidaAlEquipo is { EsDelEquipo: false } && Salidas.Any(s => s.EsDelEquipo))
            {
                return "La salida al equipo no es la del codec del equipo: la voz no llegaría a la radio.";
            }

            if (Microfono is { EsDelEquipo: true })
            {
                return "El micrófono elegido es la entrada del equipo: se retransmitiría lo que se recibe.";
            }

            if (Altavoces is { EsDelEquipo: true })
            {
                return "Los altavoces elegidos son la salida al equipo: la escucha se transmitiría.";
            }

            return string.Empty;
        }
    }

    /// <summary>Vuelve a leer los dispositivos del sistema.</summary>
    [RelayCommand]
    public void RefrescarDispositivos()
    {
        var anterior = _cargando;
        _cargando = true;
        try
        {
            Rellenar(Entradas, Seguro(_entradas));
            Rellenar(Salidas, Seguro(_salidas));

            var f = Guardado;
            EntradaDelEquipo = Buscar(Entradas, f.EntradaDelEquipo) ?? Entradas.FirstOrDefault(d => d.EsDelEquipo);
            SalidaAlEquipo = Buscar(Salidas, f.SalidaAlEquipo) ?? Salidas.FirstOrDefault(d => d.EsDelEquipo);
            Microfono = Buscar(Entradas, f.Microfono)
                ?? (Buscar(Entradas, _idPorOmision(true)) is { EsDelEquipo: false } microPorOmision
                    ? microPorOmision
                    : Entradas.FirstOrDefault(d => !d.EsDelEquipo));
            Altavoces = Buscar(Salidas, f.Altavoces)
                ?? (Buscar(Salidas, _idPorOmision(false)) is { EsDelEquipo: false } salidaPorOmision
                    ? salidaPorOmision
                    : Salidas.FirstOrDefault(d => !d.EsDelEquipo));
        }
        finally
        {
            _cargando = anterior;
        }

        Guardar();
        AvisarDeDispositivos();
    }

    private void Cargar()
    {
        _cargando = true;
        try
        {
            var f = Guardado.Acotar();
            VolumenRx = f.VolumenRx;
            SilencioRx = f.SilencioRx;
            EscucharAlArrancar = f.EscucharAlArrancar;
            GananciaTxDb = f.GananciaTxDb;
            TiempoMaximoSegundos = f.TiempoMaximoSegundos;
            PttConmutado = f.ModoDelPtt == ModoDelPttDeFonia.Conmutado;
            TeclaDelPtt = Teclas.FirstOrDefault(t => string.Equals(t.Nombre, f.TeclaDelPtt, StringComparison.OrdinalIgnoreCase)) ?? Teclas[0];
            ComprobarFuenteDeModulacion = f.ComprobarFuenteDeModulacion;
            IndiceDeFuenteSsb = f.IndiceDeFuenteSsb;
            ValorDeFuenteUsb = f.ValorDeFuenteUsb;
        }
        finally
        {
            _cargando = false;
        }

        RefrescarDispositivos();
    }

    partial void OnEntradaDelEquipoChanged(DispositivoDeAudio? value) => AlCambiarDispositivo(nameof(EntradaDelEquipo));

    partial void OnAltavocesChanged(DispositivoDeAudio? value) => AlCambiarDispositivo(nameof(Altavoces));

    partial void OnMicrofonoChanged(DispositivoDeAudio? value) => AlCambiarDispositivo(nameof(Microfono));

    partial void OnSalidaAlEquipoChanged(DispositivoDeAudio? value) => AlCambiarDispositivo(nameof(SalidaAlEquipo));

    partial void OnVolumenRxChanged(int value) => AlCambiar(nameof(VolumenRx));

    partial void OnSilencioRxChanged(bool value) => AlCambiar(nameof(SilencioRx));

    partial void OnEscucharAlArrancarChanged(bool value) => AlCambiar(nameof(EscucharAlArrancar));

    partial void OnGananciaTxDbChanged(double value) => AlCambiar(nameof(GananciaTxDb));

    partial void OnTiempoMaximoSegundosChanged(int value) => AlCambiar(nameof(TiempoMaximoSegundos));

    partial void OnPttConmutadoChanged(bool value) => AlCambiar(nameof(PttConmutado));

    partial void OnTeclaDelPttChanged(TeclaDePtt? value) => AlCambiar(nameof(TeclaDelPtt));

    partial void OnComprobarFuenteDeModulacionChanged(bool value) => AlCambiar(nameof(ComprobarFuenteDeModulacion));

    partial void OnIndiceDeFuenteSsbChanged(string value) => AlCambiar(nameof(IndiceDeFuenteSsb));

    partial void OnValorDeFuenteUsbChanged(string value) => AlCambiar(nameof(ValorDeFuenteUsb));

    private void AlCambiarDispositivo(string que)
    {
        if (_cargando) return;
        AvisarDeDispositivos();
        AlCambiar(que);
    }

    private void AvisarDeDispositivos()
    {
        OnPropertyChanged(nameof(EscuchaConfigurada));
        OnPropertyChanged(nameof(TransmisionConfigurada));
        OnPropertyChanged(nameof(AvisoDeDispositivos));
    }

    private void AlCambiar(string que)
    {
        if (_cargando) return;
        Guardar();
        Cambiado?.Invoke(this, que);
    }

    private void Guardar()
    {
        var f = Guardado;

        // Un dispositivo que no esta ahora (la radio apagada) no borra lo guardado.
        f.EntradaDelEquipo = EntradaDelEquipo?.Id ?? f.EntradaDelEquipo;
        f.Altavoces = Altavoces?.Id ?? f.Altavoces;
        f.Microfono = Microfono?.Id ?? f.Microfono;
        f.SalidaAlEquipo = SalidaAlEquipo?.Id ?? f.SalidaAlEquipo;
        f.VolumenRx = VolumenRx;
        f.SilencioRx = SilencioRx;
        f.EscucharAlArrancar = EscucharAlArrancar;
        f.GananciaTxDb = GananciaTxDb;
        f.TiempoMaximoSegundos = TiempoMaximoSegundos;
        f.ModoDelPtt = PttConmutado ? ModoDelPttDeFonia.Conmutado : ModoDelPttDeFonia.Mantener;
        f.TeclaDelPtt = TeclaDelPtt?.Nombre ?? string.Empty;
        f.ComprobarFuenteDeModulacion = ComprobarFuenteDeModulacion;
        f.IndiceDeFuenteSsb = (IndiceDeFuenteSsb ?? string.Empty).Trim();
        f.ValorDeFuenteUsb = (ValorDeFuenteUsb ?? string.Empty).Trim();
        f.Acotar();

        if (_carpeta is not null) _ajustes.Guardar(_carpeta);
    }

    private static IReadOnlyList<DispositivoDeAudio> Seguro(Func<IReadOnlyList<DispositivoDeAudio>> listar)
    {
        try
        {
            return listar();
        }
        catch (Exception fallo)
        {
            Serilog.Log.Warning(fallo, "No se han podido listar los dispositivos de sonido para la fonía.");
            return [];
        }
    }

    private static void Rellenar(ObservableCollection<DispositivoDeAudio> destino, IEnumerable<DispositivoDeAudio> origen)
    {
        destino.Clear();
        foreach (var d in origen) destino.Add(d);
    }

    private static DispositivoDeAudio? Buscar(IEnumerable<DispositivoDeAudio> lista, string? id) =>
        string.IsNullOrEmpty(id)
            ? null
            : lista.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
}
