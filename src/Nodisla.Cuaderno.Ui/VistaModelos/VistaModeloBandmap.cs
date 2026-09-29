using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>Un anuncio colocado a la altura que le toca en la escala de frecuencia.</summary>
/// <param name="Fila">Anuncio del cluster.</param>
/// <param name="Altura">Puntos desde arriba de la escala.</param>
public sealed record MarcaDelBandmap(FilaDeSpot Fila, double Altura)
{
    /// <summary>Indicativo anunciado.</summary>
    public string Indicativo => Fila.Indicativo;

    /// <summary>Frecuencia escrita.</summary>
    public string Frecuencia => Fila.Frecuencia;

    /// <summary>Modo anunciado.</summary>
    public string Modo => Fila.Modo;

    /// <summary>Aporta algo: entidad o hueco nuevos.</summary>
    public bool EsInteresante => Fila.EsInteresante;

    /// <summary>Lo anuncia una escucha automatica y no una persona.</summary>
    public bool EsDeEscuchaAutomatica => Fila.EsDeEscuchaAutomatica;

    /// <summary>Lo que se dice al pasar el raton.</summary>
    public string Detalle => Fila.Detalle;
}

/// <summary>Una marca de la regla de frecuencia.</summary>
/// <param name="Texto">Frecuencia escrita, en MHz.</param>
/// <param name="Altura">Puntos desde arriba de la escala.</param>
public sealed record RayaDelBandmap(string Texto, double Altura);

/// <summary>
/// El bandmap: los anuncios del cluster colocados por frecuencia en vez de por lista.
/// </summary>
/// <remarks>
/// <para>
/// Una lista ordenada por hora dice quien ha salido; una escala de frecuencia dice <b>donde
/// esta la banda llena y donde hay hueco</b>, que es lo que decide hacia donde se gira el
/// dial. Son los mismos anuncios mirados de otra manera.
/// </para>
/// <para>
/// La escala cubre la banda entera del VFO que recibe, de su borde inferior al superior. Si el
/// dial esta fuera de las bandas de aficionado —pasa: el del operador estaba en 27.555 MHz— no hay
/// banda que dibujar y se dice, en vez de ensenar una regla vacia que parezca rota.
/// </para>
/// </remarks>
public sealed partial class VistaModeloBandmap : ObservableObject
{
    /// <summary>Alto minimo de la escala. Por debajo no cabe ni la regla.</summary>
    public const double AltoMinimo = 180;

    /// <summary>
    /// Alto de la escala en puntos, que lo pone la vista con el sitio que tiene.
    /// </summary>
    /// <remarks>
    /// Las alturas de los anuncios se calculan aqui, asi que el alto tiene que saberlo el
    /// modelo. Con un alto fijo, la mitad de la banda se quedaba fuera de la pantalla y habia
    /// que desplazarse para ver si habia hueco arriba: justo lo que el bandmap viene a evitar.
    /// </remarks>
    [ObservableProperty]
    private double _altoDeLaEscala = 420;

    /// <summary>Cuantas rayas rotuladas lleva la regla.</summary>
    private const int RayasDeLaRegla = 9;

    /// <summary>Recoloca todo cuando la vista dice cuanto sitio hay.</summary>
    partial void OnAltoDeLaEscalaChanged(double value) => Rehacer();

    private IReadOnlyList<FilaDeSpot> _spots = [];
    private Frecuencia _dial;

    /// <summary>Los anuncios colocados a su altura.</summary>
    public ObservableCollection<MarcaDelBandmap> Marcas { get; } = [];

    /// <summary>Las rayas rotuladas de la regla.</summary>
    public ObservableCollection<RayaDelBandmap> Rayas { get; } = [];

    /// <summary>Banda que se esta dibujando.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Titulo))]
    private string _banda = "—";

    /// <summary>No hay banda que dibujar porque el dial esta fuera de las bandas.</summary>
    [ObservableProperty]
    private bool _sinBanda = true;

    /// <summary>Altura del cursor del dial en la escala. Negativa si no cae dentro.</summary>
    [ObservableProperty]
    private double _alturaDelDial = -1;

    /// <summary>El dial cae dentro de la escala que se dibuja.</summary>
    [ObservableProperty]
    private bool _dialALaVista;

    /// <summary>Solo se ensena lo que aporta algo: entidad o hueco nuevos.</summary>
    [ObservableProperty]
    private bool _soloLoNuevo;

    /// <summary>Solo se ensena lo del modo que tiene puesto el equipo.</summary>
    [ObservableProperty]
    private bool _soloElModo;

    /// <summary>Se esconden los anuncios de las escuchas automaticas.</summary>
    [ObservableProperty]
    private bool _sinEscuchaAutomatica;

    /// <summary>Modo del equipo, para el filtro por modo.</summary>
    [ObservableProperty]
    private string _modoDelEquipo = string.Empty;

    /// <summary>Cuantos anuncios se estan dibujando.</summary>
    [ObservableProperty]
    private string _resumen = string.Empty;

    /// <summary>Titulo del bloque.</summary>
    public string Titulo => SinBanda ? "Bandmap" : $"Bandmap · {Banda}";

    /// <summary>Salta cuando se elige un anuncio, para llevar el equipo a su frecuencia.</summary>
    public event EventHandler<FilaDeSpot>? SpotElegido;

    /// <summary>Dice donde esta el dial y en que modo, para centrar la escala.</summary>
    /// <param name="frecuencia">Frecuencia del VFO que recibe.</param>
    /// <param name="modo">Modo del equipo.</param>
    public void PonerElDial(Frecuencia frecuencia, string? modo)
    {
        _dial = frecuencia;
        ModoDelEquipo = modo ?? string.Empty;
        Rehacer();
    }

    /// <summary>Dice que anuncios hay ahora mismo.</summary>
    /// <param name="spots">Anuncios que pasan el filtro del panel de cluster.</param>
    public void PonerSpots(IEnumerable<FilaDeSpot> spots)
    {
        ArgumentNullException.ThrowIfNull(spots);

        _spots = spots as IReadOnlyList<FilaDeSpot> ?? [.. spots];
        Rehacer();
    }

    /// <summary>Lleva el equipo al anuncio elegido.</summary>
    /// <param name="marca">Anuncio elegido.</param>
    [RelayCommand]
    public void IrAlSpot(MarcaDelBandmap? marca)
    {
        if (marca is not null) SpotElegido?.Invoke(this, marca.Fila);
    }

    partial void OnSoloLoNuevoChanged(bool value) => Rehacer();

    partial void OnSoloElModoChanged(bool value) => Rehacer();

    partial void OnSinEscuchaAutomaticaChanged(bool value) => Rehacer();

    /// <summary>Recoloca todo: la regla, el cursor del dial y los anuncios.</summary>
    private void Rehacer()
    {
        // Ojo: la propiedad Banda de esta clase tapa al tipo Banda del dominio, asi que hay
        // que cualificarlo. Mismo tropiezo que en el filtro de spots.
        var banda = Dominio.Valores.Banda.DesdeFrecuencia(_dial);

        Rayas.Clear();
        Marcas.Clear();

        if (banda.EsVacia)
        {
            SinBanda = true;
            DialALaVista = false;
            Banda = "—";
            Resumen = _dial.EsCero
                ? "Sin frecuencia del equipo."
                : "El dial está fuera de las bandas de aficionado; no hay banda que dibujar.";
            OnPropertyChanged(nameof(Titulo));
            return;
        }

        SinBanda = false;
        Banda = banda.Nombre;

        var (inferior, superior) = banda.Limite;
        var recorrido = superior - inferior;
        if (recorrido <= 0) return;

        for (var i = 0; i < RayasDeLaRegla; i++)
        {
            var parte = (decimal)i / (RayasDeLaRegla - 1);
            var frecuencia = inferior + (recorrido * parte);

            // Punto decimal SIEMPRE, nunca la coma de la cultura espanola: 14.200 son catorce
            // megahercios y pico. Ver la regla en TextoDeFrecuencia.
            Rayas.Add(new RayaDelBandmap(
                frecuencia.ToString("0.000", CultureInfo.InvariantCulture),
                (double)parte * AltoDeLaEscala));
        }

        AlturaDelDial = Altura(_dial.Megahercios, inferior, recorrido);
        DialALaVista = AlturaDelDial >= 0;

        var dibujados = 0;
        foreach (var fila in _spots)
        {
            if (!Pasa(fila, banda)) continue;

            var altura = Altura(fila.Spot.Frecuencia.Megahercios, inferior, recorrido);
            if (altura < 0) continue;

            Marcas.Add(new MarcaDelBandmap(fila, altura));
            dibujados++;
        }

        Resumen = dibujados == 0
            ? $"Nadie anunciado en {banda.Nombre} ahora mismo."
            : $"{dibujados.ToString("N0", CultureInfo.CurrentCulture)} en {banda.Nombre}";

        OnPropertyChanged(nameof(Titulo));
    }

    /// <summary>El anuncio pasa los filtros y cae en la banda que se dibuja.</summary>
    private bool Pasa(FilaDeSpot fila, Dominio.Valores.Banda banda)
    {
        if (!string.Equals(fila.Banda, banda.Nombre, StringComparison.OrdinalIgnoreCase)) return false;
        if (SoloLoNuevo && !fila.EsInteresante) return false;
        if (SinEscuchaAutomatica && fila.EsDeEscuchaAutomatica) return false;

        return !SoloElModo
            || ModoDelEquipo.Length == 0
            || string.Equals(fila.Modo, ModoDelEquipo, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Altura en puntos de una frecuencia. Negativa si cae fuera de la escala.</summary>
    private double Altura(decimal megahercios, decimal inferior, decimal recorrido)
    {
        var parte = (megahercios - inferior) / recorrido;
        return parte is < 0 or > 1 ? -1 : (double)parte * AltoDeLaEscala;
    }
}
