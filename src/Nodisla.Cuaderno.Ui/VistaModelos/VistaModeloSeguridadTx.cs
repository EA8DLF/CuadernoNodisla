using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Radio.Ptt;
using Nodisla.Cuaderno.Ui.Ajustes;
using Serilog;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>Una banda en la tabla de salvaguardas: si se libera del plan y su potencia maxima.</summary>
public sealed partial class FilaDeSeguridadTx : ObservableObject
{
    private readonly Func<FilaDeSeguridadTx, bool> _confirmarLiberar;
    private bool _volviendo;

    /// <summary>Crea la fila.</summary>
    /// <param name="banda">La clave de la banda («40m», «27 MHz»…).</param>
    /// <param name="liberada">Liberada del plan.</param>
    /// <param name="potencia">Potencia maxima, o vacio.</param>
    /// <param name="confirmarLiberar">La pregunta antes de liberarla.</param>
    public FilaDeSeguridadTx(string banda, bool liberada, string potencia, Func<FilaDeSeguridadTx, bool> confirmarLiberar)
    {
        Banda = banda;
        _liberada = liberada;
        _potenciaMaxima = potencia;
        _confirmarLiberar = confirmarLiberar;
    }

    /// <summary>La banda.</summary>
    public string Banda { get; }

    /// <summary>Se deja transmitir fuera del plan en esta banda (solo con confirmacion).</summary>
    [ObservableProperty]
    private bool _liberada;

    /// <summary>Potencia maxima en vatios; vacio, sin limite.</summary>
    [ObservableProperty]
    private string _potenciaMaxima;

    partial void OnLiberadaChanged(bool value)
    {
        if (!value || _volviendo) return;
        if (_confirmarLiberar(this)) return;
        _volviendo = true;
        try
        {
            Liberada = false;
        }
        finally
        {
            _volviendo = false;
        }
    }
}

/// <summary>
/// Las salvaguardas de transmision: plan de banda, ROE y potencia maxima por banda.
/// </summary>
/// <remarks>
/// Se guardan en <c>seguridad-tx.json</c> y se le dan al vigilante del PTT en el acto. Liberar
/// una banda del plan pide una confirmacion explicita: es para licencias especiales.
/// </remarks>
public sealed partial class VistaModeloSeguridadTx : ObservableObject
{
    /// <summary>Las bandas de la tabla.</summary>
    public static IReadOnlyList<string> Bandas { get; } =
        ["160m", "80m", "60m", "40m", "30m", "20m", "17m", "15m", "12m", "10m", "6m", "4m", "2m", "70cm", "27 MHz"];

    private readonly IVigilantePtt _vigilante;
    private readonly string? _carpeta;

    /// <summary>Monta el apartado.</summary>
    /// <param name="vigilante">El vigilante del PTT.</param>
    /// <param name="carpeta">Carpeta de datos; nula, no se guarda.</param>
    public VistaModeloSeguridadTx(IVigilantePtt vigilante, string? carpeta)
    {
        ArgumentNullException.ThrowIfNull(vigilante);
        _vigilante = vigilante;
        _carpeta = carpeta;
        var guardado = AjustesDeSeguridadTx.Leer(carpeta).Acotar();
        _bloquearFueraDeBanda = guardado.BloquearFueraDeBanda;
        _vigilarRoe = guardado.VigilarRoe;
        _roeMaxima = guardado.RoeMaxima;
        _lecturasSeguidas = guardado.LecturasSeguidas;
        foreach (var banda in Bandas)
        {
            Filas.Add(new FilaDeSeguridadTx(
                banda,
                guardado.BandasLiberadas.Contains(banda),
                guardado.PotenciaMaximaPorBanda.TryGetValue(banda, out var w) ? w.ToString(CultureInfo.InvariantCulture) : string.Empty,
                Confirmar));
        }

        if (vigilante is VigilantePtt de) de.BloqueoCambiado += (_, _) => Conversores.Hilo.EnLaVentana(() => OnPropertyChanged(nameof(MotivoDeBloqueo)));
    }

    /// <summary>La pregunta antes de liberar una banda. Sin ella, no se libera.</summary>
    public Func<string, bool>? ConfirmarLiberarBanda { get; set; }

    /// <summary>Las bandas.</summary>
    public ObservableCollection<FilaDeSeguridadTx> Filas { get; } = [];

    /// <summary>No transmitir fuera del plan de banda.</summary>
    [ObservableProperty]
    private bool _bloquearFueraDeBanda;

    /// <summary>Cortar por ROE.</summary>
    [ObservableProperty]
    private bool _vigilarRoe;

    /// <summary>ROE maxima.</summary>
    [ObservableProperty]
    private double _roeMaxima;

    /// <summary>Lecturas seguidas.</summary>
    [ObservableProperty]
    private int _lecturasSeguidas;

    /// <summary>Un aviso.</summary>
    [ObservableProperty]
    private string _aviso = string.Empty;

    /// <summary>Por que no se puede transmitir ahora (tras un corte), o vacio.</summary>
    public string MotivoDeBloqueo => _vigilante.MotivoDeBloqueo ?? string.Empty;

    /// <summary>Guarda y se lo da al vigilante.</summary>
    [RelayCommand]
    public void Guardar()
    {
        var ajustes = new AjustesDeSeguridadTx
        {
            BloquearFueraDeBanda = BloquearFueraDeBanda,
            VigilarRoe = VigilarRoe,
            RoeMaxima = RoeMaxima,
            LecturasSeguidas = LecturasSeguidas,
            BandasLiberadas = Filas.Where(f => f.Liberada).Select(f => f.Banda).ToList(),
            PotenciaMaximaPorBanda = Filas
                .Where(f => int.TryParse(f.PotenciaMaxima, NumberStyles.Integer, CultureInfo.InvariantCulture, out var w) && w > 0)
                .ToDictionary(f => f.Banda, f => int.Parse(f.PotenciaMaxima, CultureInfo.InvariantCulture)),
        }.Acotar();

        RoeMaxima = ajustes.RoeMaxima;
        LecturasSeguidas = ajustes.LecturasSeguidas;
        if (_vigilante is VigilantePtt vigilante) vigilante.CambiarSeguridad(ajustes.AOpciones());

        if (string.IsNullOrEmpty(_carpeta))
        {
            Aviso = Textos.T("Ajustes.SeguridadTx.Guardado");
            return;
        }

        try
        {
            ajustes.Guardar(_carpeta);
            Aviso = Textos.T("Ajustes.SeguridadTx.Guardado");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error(ex, "No se han podido guardar las salvaguardas de transmisión.");
            Aviso = Textos.F("Ajustes.SeguridadTx.NoGuarda", ex.Message);
        }
    }

    private bool Confirmar(FilaDeSeguridadTx fila)
    {
        var si = ConfirmarLiberarBanda is { } preguntar && preguntar(Textos.F("Ajustes.SeguridadTx.ConfirmarLiberar", fila.Banda));
        if (si) Log.Warning("El operador ha liberado la banda {Banda} del plan de banda (transmitir fuera del plan).", fila.Banda);
        return si;
    }
}
