using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Idiomas;
using Serilog;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>Una tecla de la botonera lateral: rotulo, ayuda y si esta puesta ahora.</summary>
/// <param name="rotulo">Lo que pone la tecla.</param>
/// <param name="ayuda">La ayuda emergente.</param>
/// <param name="valor">Lo que se manda al pulsarla (banda, modo, memoria, canal o plan).</param>
public sealed partial class TeclaDeLaBotonera(string rotulo, string ayuda, object valor) : ObservableObject
{
    /// <summary>Lo que pone la tecla.</summary>
    public string Rotulo { get; } = rotulo;

    /// <summary>Lo que se manda al pulsarla.</summary>
    public object Valor { get; } = valor;

    /// <summary>La ayuda emergente.</summary>
    [ObservableProperty]
    private string _ayuda = ayuda;

    /// <summary>Es la banda, el modo, la memoria o el canal en que esta el VFO activo.</summary>
    [ObservableProperty]
    private bool _activa;
}

/// <summary>
/// La botonera a los lados del frontal: bandas, modos y memorias (HAM) y los canales de 27 MHz
/// (CB). Todo va al VFO activo y nada transmite.
/// </summary>
public sealed partial class VistaModeloEquipo
{
    private PlanCb _planCb = PlanCb.Cept;

    /// <summary>Teclas de banda del equipo (pila de banda, como BAND).</summary>
    public ObservableCollection<TeclaDeLaBotonera> TeclasDeBanda { get; } = [];

    /// <summary>LSB · USB · CW · AM · FM · DATA.</summary>
    public IReadOnlyList<TeclaDeLaBotonera> TeclasDeModo { get; } =
    [
        new("LSB", AyudaDelModo(TeclaDeModo.Lsb), TeclaDeModo.Lsb),
        new("USB", AyudaDelModo(TeclaDeModo.Usb), TeclaDeModo.Usb),
        new("CW", AyudaDelModo(TeclaDeModo.Cw), TeclaDeModo.Cw),
        new("AM", AyudaDelModo(TeclaDeModo.Am), TeclaDeModo.Am),
        new("FM", AyudaDelModo(TeclaDeModo.Fm), TeclaDeModo.Fm),
        new("DATA", AyudaDelModo(TeclaDeModo.Datos), TeclaDeModo.Datos),
    ];

    /// <summary>M1 … M6: los canales de memoria 1 a 6 del equipo.</summary>
    public IReadOnlyList<TeclaDeLaBotonera> TeclasDeMemoria { get; } =
        [.. Enumerable.Range(1, 6).Select(n => new TeclaDeLaBotonera($"M{n}", Textos.F("Cabina.Botonera.MemoriaAyuda", n), n))];

    /// <summary>Canales 1 a 40 de la banda ciudadana, en el plan elegido.</summary>
    public IReadOnlyList<TeclaDeLaBotonera> CanalesDeCb { get; } =
        [.. Enumerable.Range(1, CanalesCb.Canales).Select(n => new TeclaDeLaBotonera(n.ToString(CultureInfo.InvariantCulture), string.Empty, n))];

    /// <summary>11m · UK · CEPT · PL · USA.</summary>
    public IReadOnlyList<TeclaDeLaBotonera> PlanesDeCb { get; } =
        [.. CanalesCb.Planes.Select(p => new TeclaDeLaBotonera(CanalesCb.Rotulo(p), AyudaDelPlan(p), p))];

    /// <summary>El plan de canales de 27 MHz elegido. Se guarda con el estado de los paneles.</summary>
    public PlanCb PlanCb
    {
        get => _planCb;
        set
        {
            if (!SetProperty(ref _planCb, value)) return;
            RecogerLaBotonera();
        }
    }

    /// <summary>El equipo tiene botonera (teclas de banda y modo por CAT).</summary>
    public bool HayBotonera => Real is IEquipoConBotonera;

    /// <summary>Por que la botonera esta apagada, para su ayuda emergente.</summary>
    public string MotivoDeLaBotonera => !HayBotonera
        ? Textos.T("Cabina.Botonera.SinBotonera")
        : !Conectado
            ? Textos.T("Cabina.Botonera.ConecteElEquipo")
            : Textos.T("Cabina.Botonera.TodoAlVfo");

    /// <summary>
    /// Pulsa una tecla de banda: <c>BS</c>, como la tecla BAND de la radio, sobre el VFO activo.
    /// </summary>
    /// <param name="tecla">Tecla pulsada.</param>
    [RelayCommand(CanExecute = nameof(SePuedeUsarLaBotonera))]
    public async Task IrABandaAsync(TeclaDeLaBotonera? tecla)
    {
        if (tecla?.Valor is not TeclaDeBanda banda || Real is not IEquipoConBotonera botonera) return;

        try
        {
            await botonera.IrABandaAsync(banda).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido ir a la banda {Banda}.", banda.Rotulo);
            Aviso = Textos.F("Cabina.Botonera.NoVaABanda", banda.Descripcion, ex.Message);
        }
    }

    /// <summary>Pulsa una tecla de modo sobre el VFO activo.</summary>
    /// <param name="tecla">Tecla pulsada.</param>
    [RelayCommand(CanExecute = nameof(SePuedeUsarLaBotonera))]
    public async Task PonerModoDeTeclaAsync(TeclaDeLaBotonera? tecla)
    {
        if (tecla?.Valor is not TeclaDeModo modo || Real is not IEquipoConBotonera botonera) return;

        try
        {
            await botonera.PonerModoDeTeclaAsync(modo).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido poner el modo {Modo}.", modo);
            Aviso = Textos.F("Cabina.Botonera.NoPoneModo", tecla.Rotulo, ex.Message);
        }
    }

    /// <summary>Recupera un canal de memoria del equipo (M1 … M6).</summary>
    /// <param name="tecla">Tecla pulsada.</param>
    [RelayCommand(CanExecute = nameof(SePuedeUsarLaBotonera))]
    public async Task RecuperarMemoriaAsync(TeclaDeLaBotonera? tecla)
    {
        if (tecla?.Valor is not int numero || Real is not IEquipoAvanzado avanzado) return;

        try
        {
            await avanzado.IrAMemoriaAsync(numero).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido recuperar la memoria {Numero}.", numero);
            Aviso = Textos.F("Cabina.Botonera.NoRecuperaMemoria", numero, ex.Message);
        }
    }

    /// <summary>Lleva el VFO activo al canal de 27 MHz del plan elegido.</summary>
    /// <param name="tecla">Tecla pulsada.</param>
    [RelayCommand(CanExecute = nameof(SePuedeUsarLaBotonera))]
    public async Task IrAlCanalAsync(TeclaDeLaBotonera? tecla)
    {
        if (tecla?.Valor is not int canal) return;

        try
        {
            // PonerFrecuenciaAsync va al VFO activo (en el FT-710, FA o FB segun VS).
            var hercios = CanalesCb.Hercios(PlanCb, canal);
            await _equipo.PonerFrecuenciaAsync(Dominio.Valores.Frecuencia.DesdeHercios(hercios)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido ir al canal {Canal}.", canal);
            Aviso = Textos.F("Cabina.Botonera.NoVaACanal", canal, ex.Message);
        }
    }

    /// <summary>Elige el plan de canales de 27 MHz.</summary>
    /// <param name="tecla">Tecla del plan.</param>
    [RelayCommand]
    public void ElegirPlanCb(TeclaDeLaBotonera? tecla)
    {
        if (tecla?.Valor is PlanCb plan) PlanCb = plan;
    }

    private bool SePuedeUsarLaBotonera(TeclaDeLaBotonera? tecla) => Conectado && HayBotonera;

    private static string AyudaDelPlan(PlanCb plan) => plan switch
    {
        PlanCb.Once => Textos.T("Cabina.Botonera.Plan11m"),
        PlanCb.Uk => Textos.T("Cabina.Botonera.PlanUk"),
        PlanCb.Cept => Textos.T("Cabina.Botonera.PlanCept"),
        PlanCb.Pl => Textos.T("Cabina.Botonera.PlanPl"),
        _ => Textos.T("Cabina.Botonera.PlanUsa"),
    };

    private static string AyudaDelModo(TeclaDeModo modo) => modo switch
    {
        TeclaDeModo.Lsb => Textos.T("Cabina.Botonera.ModoLsb"),
        TeclaDeModo.Usb => Textos.T("Cabina.Botonera.ModoUsb"),
        TeclaDeModo.Cw => Textos.T("Cabina.Botonera.ModoCw"),
        TeclaDeModo.Am => Textos.T("Cabina.Botonera.ModoAm"),
        TeclaDeModo.Fm => Textos.T("Cabina.Botonera.ModoFm"),
        _ => Textos.T("Cabina.Botonera.ModoDatos"),
    };

    /// <summary>Monta las teclas de banda del equipo que haya y avisa de lo que cambia.</summary>
    private void RehacerLaBotonera()
    {
        TeclasDeBanda.Clear();
        if (Real is IEquipoConBotonera botonera)
        {
            foreach (var tecla in botonera.TeclasDeBanda)
            {
                TeclasDeBanda.Add(new TeclaDeLaBotonera(tecla.Rotulo, Textos.F("Cabina.Botonera.BandaAyuda", tecla.Descripcion), tecla));
            }
        }

        OnPropertyChanged(nameof(HayBotonera));
        AvisarDeLaBotonera();
    }

    /// <summary>Al cambiar de idioma: los textos calculados y las ayudas de la botonera, que se guardan hechas.</summary>
    private void AlCambiarElIdioma()
    {
        OnPropertyChanged(string.Empty);
        foreach (var tecla in TeclasDeModo) tecla.Ayuda = AyudaDelModo((TeclaDeModo)tecla.Valor);
        foreach (var tecla in PlanesDeCb) tecla.Ayuda = AyudaDelPlan((PlanCb)tecla.Valor);
        // Las teclas de banda se retocan en su sitio y no se vuelven a montar: vaciar y rellenar
        // la colección obliga a hacerlo desde el hilo de su vista, y el cambio de idioma puede
        // llegar de otro. Cambiar una propiedad de una tecla, en cambio, WPF lo admite.
        foreach (var tecla in TeclasDeBanda) tecla.Ayuda = Textos.F("Cabina.Botonera.BandaAyuda", ((TeclaDeBanda)tecla.Valor).Descripcion);
        RecogerLaBotonera();
    }

    private void AvisarDeLaBotonera()
    {
        OnPropertyChanged(nameof(MotivoDeLaBotonera));
        IrABandaCommand.NotifyCanExecuteChanged();
        PonerModoDeTeclaCommand.NotifyCanExecuteChanged();
        RecuperarMemoriaCommand.NotifyCanExecuteChanged();
        IrAlCanalCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Resalta la banda, el modo, la memoria y el canal en que esta el VFO activo.</summary>
    private void RecogerLaBotonera()
    {
        var estado = _equipo.Estado;
        var conectado = estado.Conectado && !estado.Frecuencia.EsCero;
        var hercios = conectado ? estado.Frecuencia.Hercios : 0;

        foreach (var tecla in TeclasDeBanda)
        {
            var banda = (TeclaDeBanda)tecla.Valor;
            tecla.Activa = conectado && (banda.EsCoberturaGeneral ? estado.Banda.EsVacia : banda.Banda == estado.Banda);
        }

        var modo = conectado ? ModoDeLaTecla(estado.Modo, hercios) : null;
        foreach (var tecla in TeclasDeModo)
        {
            tecla.Activa = modo is { } m && (TeclaDeModo)tecla.Valor == m;
        }

        foreach (var tecla in TeclasDeMemoria)
        {
            var numero = (int)tecla.Valor;
            var memoria = Memorias.FirstOrDefault(x => x.Numero == numero);
            tecla.Ayuda = memoria is { Ocupada: true }
                ? $"M{numero}: {Aplicacion.CasosDeUso.TextoDeFrecuencia.Escribir(memoria.Frecuencia)} MHz {memoria.Modo.NombreUsual}{(memoria.Etiqueta is { } e ? $" «{e}»" : string.Empty)} (MC{numero:D3})."
                : Textos.F("Cabina.Botonera.MemoriaVacia", numero);
        }

        var canal = conectado ? CanalesCb.CanalEn(PlanCb, hercios) : null;
        foreach (var tecla in CanalesDeCb)
        {
            var n = (int)tecla.Valor;
            tecla.Activa = canal == n;
            tecla.Ayuda = Textos.F("Cabina.Botonera.CanalAyuda", n, CanalesCb.Rotulo(PlanCb), (CanalesCb.Hercios(PlanCb, n) / 1e6).ToString("0.00000", CultureInfo.CurrentCulture));
        }

        foreach (var tecla in PlanesDeCb)
        {
            tecla.Activa = (PlanCb)tecla.Valor == PlanCb;
        }
    }

    /// <summary>La tecla de modo que corresponde al modo del equipo, o nula.</summary>
    /// <param name="modo">Modo que informa el equipo.</param>
    /// <param name="hercios">Frecuencia del dial: un «SSB» sin banda lateral es LSB por debajo de 10 MHz.</param>
    /// <returns>La tecla, o nula si no hay ninguna (RTTY, PSK…).</returns>
    public static TeclaDeModo? ModoDeLaTecla(Dominio.Valores.Modo modo, long hercios) => modo.NombreUsual.ToUpperInvariant() switch
    {
        "SSB" => hercios < 10_000_000 ? TeclaDeModo.Lsb : TeclaDeModo.Usb,
        "LSB" => TeclaDeModo.Lsb,
        "USB" => TeclaDeModo.Usb,
        "CW" => TeclaDeModo.Cw,
        "AM" => TeclaDeModo.Am,
        "FM" => TeclaDeModo.Fm,
        "FT8" or "FT4" or "PKT" or "PKTUSB" or "PKTLSB" or "DATA" or "DIGITALVOICE" => TeclaDeModo.Datos,
        _ => null,
    };
}
