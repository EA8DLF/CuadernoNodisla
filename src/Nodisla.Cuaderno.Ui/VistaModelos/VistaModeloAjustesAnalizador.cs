using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Ui.Ajustes;
using Nodisla.Cuaderno.Ui.Recursos;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>
/// Ajustes del analizador de la propia radio, en el apartado Equipo de Configuración. Cada
/// cambio se guarda en el acto y se ve en el analizador al momento.
/// </summary>
public sealed partial class VistaModeloAjustesAnalizador : ObservableObject
{
    private readonly AjustesDelPrograma _ajustes;
    private readonly string? _carpeta;
    private bool _cargando;

    /// <summary>Monta el apartado.</summary>
    /// <param name="ajustes">Ajustes del programa.</param>
    /// <param name="carpeta">Donde se guardan; nulo para no guardar (pruebas y simulado).</param>
    public VistaModeloAjustesAnalizador(AjustesDelPrograma ajustes, string? carpeta)
    {
        ArgumentNullException.ThrowIfNull(ajustes);
        _ajustes = ajustes;
        _ajustes.Analizador ??= new AjustesDelAnalizador();
        _carpeta = carpeta;

        Paletas = Enum.GetValues<PaletaDelAnalizador>()
            .Select(p => new Opcion<PaletaDelAnalizador>(p, () => NombreDePaleta(p)))
            .ToList();
        Pasos = AjustesDelAnalizador.Pasos
            .Select(p => new Opcion<int>(p, () => p == 0 ? Textos.T("Ajustes.Analizador.PasoAutomatico") : TextoDelPaso(p)))
            .ToList();

        Cargar();
    }

    /// <summary>Lo guardado, tal cual.</summary>
    public AjustesDelAnalizador Guardado => _ajustes.Analizador;

    /// <summary>Salta con cada cambio (ya guardado).</summary>
    public event EventHandler? Cambiado;

    /// <summary>Las paletas que se pueden elegir.</summary>
    public IReadOnlyList<Opcion<PaletaDelAnalizador>> Paletas { get; }

    /// <summary>Filas de rotulos que se pueden elegir.</summary>
    public IReadOnlyList<int> CarrilesPosibles { get; } = [1, 2, 3, 4];

    /// <summary>Los pasos del clic y de la rueda.</summary>
    public IReadOnlyList<Opcion<int>> Pasos { get; }

    /// <summary>Spots del cluster encima del analizador.</summary>
    [ObservableProperty]
    private bool _spotsEncima;

    /// <summary>Carriles de rotulos (1 a 4).</summary>
    [ObservableProperty]
    private int _carriles;

    /// <summary>Clic para sintonizar y rueda para mover el VFO.</summary>
    [ObservableProperty]
    private bool _clicParaSintonizar;

    /// <summary>Paso elegido.</summary>
    [ObservableProperty]
    private Opcion<int>? _paso;

    /// <summary>AGC de la cascada: el negro sigue al suelo de ruido.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NivelBajoManual))]
    private bool _sueloAutomatico;

    /// <summary>Sin AGC, el nivel que sale negro.</summary>
    [ObservableProperty]
    private int _nivelBajo;

    /// <summary>Contraste de la cascada.</summary>
    [ObservableProperty]
    private int _contraste;

    /// <summary>Brillo del ruido.</summary>
    [ObservableProperty]
    private int _brillo;

    /// <summary>Paleta elegida.</summary>
    [ObservableProperty]
    private Opcion<PaletaDelAnalizador>? _paleta;

    /// <summary>Marcar los picos.</summary>
    [ObservableProperty]
    private bool _marcarPicos;

    /// <summary>Correr la cascada al resintonizar.</summary>
    [ObservableProperty]
    private bool _desplazarCascada;

    /// <summary>El nivel bajo solo se toca sin AGC.</summary>
    public bool NivelBajoManual => !SueloAutomatico;

    /// <summary>Vuelve a los de fábrica.</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    public void DeFabrica()
    {
        _ajustes.Analizador = new AjustesDelAnalizador();
        Cargar();
        Guardar();
    }

    /// <summary>Nombre de una paleta en el idioma en uso.</summary>
    /// <param name="paleta">La paleta.</param>
    /// <returns>Su nombre.</returns>
    public static string NombreDePaleta(PaletaDelAnalizador paleta) => paleta switch
    {
        PaletaDelAnalizador.Nodisla => Textos.T("Ajustes.Analizador.PaletaNodisla"),
        PaletaDelAnalizador.Arcoiris => Textos.T("Ajustes.Analizador.PaletaArcoiris"),
        PaletaDelAnalizador.Fuego => Textos.T("Ajustes.Analizador.PaletaFuego"),
        PaletaDelAnalizador.Hielo => Textos.T("Ajustes.Analizador.PaletaHielo"),
        PaletaDelAnalizador.Gris => Textos.T("Ajustes.Analizador.PaletaGris"),
        _ => Textos.T("Ajustes.Analizador.PaletaRadio"),
    };

    private static string TextoDelPaso(int hz) => hz >= 1000
        ? string.Create(CultureInfo.InvariantCulture, $"{hz / 1000.0:0.#} kHz")
        : string.Create(CultureInfo.InvariantCulture, $"{hz} Hz");

    private void Cargar()
    {
        _cargando = true;
        try
        {
            var a = Guardado.Acotar();
            SpotsEncima = a.SpotsEncima;
            Carriles = a.CarrilesDeSpots;
            ClicParaSintonizar = a.ClicParaSintonizar;
            Paso = Pasos.FirstOrDefault(p => p.Valor == a.PasoHz) ?? Pasos[0];
            SueloAutomatico = a.SueloAutomatico;
            NivelBajo = a.NivelBajo;
            Contraste = a.Contraste;
            Brillo = a.Brillo;
            var paleta = PaletasDelAnalizador.DesdeNombre(a.Paleta);
            Paleta = Paletas.First(p => p.Valor == paleta);
            MarcarPicos = a.MarcarPicos;
            DesplazarCascada = a.DesplazarCascada;
        }
        finally
        {
            _cargando = false;
        }
    }

    partial void OnSpotsEncimaChanged(bool value) => Guardar();

    partial void OnCarrilesChanged(int value) => Guardar();

    partial void OnClicParaSintonizarChanged(bool value) => Guardar();

    partial void OnPasoChanged(Opcion<int>? value) => Guardar();

    partial void OnSueloAutomaticoChanged(bool value) => Guardar();

    partial void OnNivelBajoChanged(int value) => Guardar();

    partial void OnContrasteChanged(int value) => Guardar();

    partial void OnBrilloChanged(int value) => Guardar();

    partial void OnPaletaChanged(Opcion<PaletaDelAnalizador>? value) => Guardar();

    partial void OnMarcarPicosChanged(bool value) => Guardar();

    partial void OnDesplazarCascadaChanged(bool value) => Guardar();

    private void Guardar()
    {
        if (_cargando) return;
        var a = Guardado;
        a.SpotsEncima = SpotsEncima;
        a.CarrilesDeSpots = Carriles;
        a.ClicParaSintonizar = ClicParaSintonizar;
        a.PasoHz = Paso?.Valor ?? 0;
        a.SueloAutomatico = SueloAutomatico;
        a.NivelBajo = NivelBajo;
        a.Contraste = Contraste;
        a.Brillo = Brillo;
        a.Paleta = (Paleta?.Valor ?? PaletaDelAnalizador.Radio).ToString();
        a.MarcarPicos = MarcarPicos;
        a.DesplazarCascada = DesplazarCascada;
        a.Acotar();

        if (_carpeta is not null) _ajustes.Guardar(_carpeta);
        Cambiado?.Invoke(this, EventArgs.Empty);
    }
}
