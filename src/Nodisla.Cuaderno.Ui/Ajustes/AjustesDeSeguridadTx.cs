using System.IO;
using System.Text;
using System.Text.Json;
using Nodisla.Cuaderno.Radio.Ptt;

namespace Nodisla.Cuaderno.Ui.Ajustes;

/// <summary>
/// Las salvaguardas de transmision tal y como se guardan: plan de banda, ROE y potencia maxima
/// por banda.
/// </summary>
/// <remarks>
/// En su propio fichero, <c>seguridad-tx.json</c> de la carpeta de datos. Las bandas liberadas
/// del plan solo entran aqui despues de una confirmacion explicita del operador.
/// </remarks>
public sealed class AjustesDeSeguridadTx
{
    /// <summary>Nombre del fichero en la carpeta de datos.</summary>
    public const string Fichero = "seguridad-tx.json";

    private static readonly JsonSerializerOptions Formato = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>No se transmite fuera del plan de banda.</summary>
    public bool BloquearFueraDeBanda { get; set; } = true;

    /// <summary>Bandas en las que se deja transmitir fuera del plan (licencias especiales).</summary>
    public List<string> BandasLiberadas { get; set; } = [];

    /// <summary>Vigilar la ROE durante la transmision.</summary>
    public bool VigilarRoe { get; set; } = true;

    /// <summary>ROE maxima.</summary>
    public double RoeMaxima { get; set; } = 2.5;

    /// <summary>Lecturas seguidas por encima de la maxima para cortar.</summary>
    public int LecturasSeguidas { get; set; } = 3;

    /// <summary>Potencia maxima por banda (W).</summary>
    public Dictionary<string, int> PotenciaMaximaPorBanda { get; set; } = [];

    /// <summary>Deja todo dentro de limites con sentido.</summary>
    /// <returns>Los mismos ajustes.</returns>
    public AjustesDeSeguridadTx Acotar()
    {
        RoeMaxima = Math.Clamp(double.IsFinite(RoeMaxima) ? RoeMaxima : 2.5, 1.2, 3.0);
        LecturasSeguidas = Math.Clamp(LecturasSeguidas, 1, 20);
        BandasLiberadas = (BandasLiberadas ?? []).Where(b => !string.IsNullOrWhiteSpace(b)).Distinct(StringComparer.Ordinal).ToList();
        PotenciaMaximaPorBanda = (PotenciaMaximaPorBanda ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p.Key) && p.Value > 0)
            .ToDictionary(p => p.Key, p => Math.Clamp(p.Value, 1, 2000), StringComparer.Ordinal);
        return this;
    }

    /// <summary>Las opciones del vigilante que salen de aqui.</summary>
    /// <returns>Las opciones.</returns>
    public OpcionesDeSeguridadDeTx AOpciones() => new()
    {
        BloquearFueraDeBanda = BloquearFueraDeBanda,
        BandasLiberadas = new HashSet<string>(BandasLiberadas, StringComparer.Ordinal),
        VigilarRoe = VigilarRoe,
        RoeMaxima = RoeMaxima,
        LecturasSeguidas = LecturasSeguidas,
        PotenciaMaximaPorBanda = new Dictionary<string, int>(PotenciaMaximaPorBanda, StringComparer.Ordinal),
    };

    /// <summary>Lee de la carpeta de datos; sin fichero o con uno roto, los de fabrica (todo puesto).</summary>
    /// <param name="carpeta">Carpeta de datos.</param>
    /// <returns>Los ajustes.</returns>
    public static AjustesDeSeguridadTx Leer(string? carpeta)
    {
        if (string.IsNullOrEmpty(carpeta)) return new AjustesDeSeguridadTx();
        var ruta = Path.Combine(carpeta, Fichero);
        try
        {
            return File.Exists(ruta)
                ? (JsonSerializer.Deserialize<AjustesDeSeguridadTx>(File.ReadAllText(ruta, Encoding.UTF8), Formato) ?? new()).Acotar()
                : new AjustesDeSeguridadTx();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Serilog.Log.Warning(ex, "No se han podido leer las salvaguardas de transmisión; se usan las de fábrica.");
            return new AjustesDeSeguridadTx();
        }
    }

    /// <summary>Guarda en la carpeta de datos.</summary>
    /// <param name="carpeta">Carpeta de datos.</param>
    public void Guardar(string carpeta)
    {
        Directory.CreateDirectory(carpeta);
        var ruta = Path.Combine(carpeta, Fichero);
        File.WriteAllText(ruta + ".tmp", JsonSerializer.Serialize(Acotar(), Formato), new UTF8Encoding(false));
        File.Move(ruta + ".tmp", ruta, overwrite: true);
    }
}
