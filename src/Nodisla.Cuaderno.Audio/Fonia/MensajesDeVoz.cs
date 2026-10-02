using System.Text.Json;
using Nodisla.Cuaderno.Audio.Procesado;

namespace Nodisla.Cuaderno.Audio.Fonia;

/// <summary>Un mensaje del voice keyer.</summary>
/// <param name="Numero">Ranura, de 1 a <see cref="AlmacenDeMensajesDeVoz.Ranuras"/> (la tecla F).</param>
/// <param name="Nombre">Como lo llama el operador: «CQ», «Indicativo»...</param>
/// <param name="Audio">La grabacion; nula si la ranura esta vacia.</param>
public sealed record MensajeDeVoz(int Numero, string Nombre, AudioEnMemoria? Audio)
{
    /// <summary>Hay algo grabado.</summary>
    public bool Grabado => Audio is { Muestras.Length: > 0 };
}

/// <summary>
/// Donde viven los mensajes del voice keyer: un WAV por ranura en la carpeta <c>voz</c> de los
/// datos y los nombres en <c>voz\mensajes.json</c>.
/// </summary>
public sealed class AlmacenDeMensajesDeVoz
{
    /// <summary>Cuantas ranuras hay (F1 a F6).</summary>
    public const int Ranuras = 6;

    /// <summary>Lo mas largo que se deja grabar un mensaje.</summary>
    public static readonly TimeSpan DuracionMaxima = TimeSpan.FromSeconds(60);

    private readonly string? _carpeta;

    /// <summary>Monta el almacen.</summary>
    /// <param name="carpetaDeDatos">Carpeta de datos del programa; nula, nada se guarda (pruebas).</param>
    public AlmacenDeMensajesDeVoz(string? carpetaDeDatos)
    {
        _carpeta = string.IsNullOrWhiteSpace(carpetaDeDatos) ? null : Path.Combine(carpetaDeDatos, "voz");
    }

    /// <summary>Carpeta donde se guardan, o nula.</summary>
    public string? Carpeta => _carpeta;

    /// <summary>Lee las seis ranuras. Lo que falte o este roto sale vacio.</summary>
    /// <param name="nombrePorOmision">Nombre de una ranura sin nombre, por su numero.</param>
    public IReadOnlyList<MensajeDeVoz> Leer(Func<int, string> nombrePorOmision)
    {
        ArgumentNullException.ThrowIfNull(nombrePorOmision);
        var nombres = LeerNombres();
        var lista = new List<MensajeDeVoz>(Ranuras);
        for (var n = 1; n <= Ranuras; n++)
        {
            AudioEnMemoria? audio = null;
            var ruta = RutaDelAudio(n);
            if (ruta is not null && File.Exists(ruta))
            {
                try
                {
                    audio = ArchivoWav.Leer(ruta);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or FormatException or ArgumentException or NotSupportedException)
                {
                    audio = null;
                }
            }

            var nombre = nombres.TryGetValue(n.ToString(System.Globalization.CultureInfo.InvariantCulture), out var guardado) && !string.IsNullOrWhiteSpace(guardado)
                ? guardado
                : nombrePorOmision(n);
            lista.Add(new MensajeDeVoz(n, nombre, audio));
        }

        return lista;
    }

    /// <summary>Guarda la grabacion de una ranura (o la borra con nulo).</summary>
    public void GuardarAudio(int numero, AudioEnMemoria? audio)
    {
        Comprobar(numero);
        var ruta = RutaDelAudio(numero);
        if (ruta is null) return;
        if (audio is null || audio.Muestras.Length == 0)
        {
            if (File.Exists(ruta)) File.Delete(ruta);
            return;
        }

        ArchivoWav.Guardar(ruta, audio);
    }

    /// <summary>Guarda el nombre de una ranura.</summary>
    public void GuardarNombre(int numero, string nombre)
    {
        Comprobar(numero);
        if (_carpeta is null) return;
        var nombres = LeerNombres();
        nombres[numero.ToString(System.Globalization.CultureInfo.InvariantCulture)] = (nombre ?? string.Empty).Trim();
        Directory.CreateDirectory(_carpeta);
        var ruta = Path.Combine(_carpeta, "mensajes.json");
        File.WriteAllText(ruta + ".tmp", JsonSerializer.Serialize(nombres, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(ruta + ".tmp", ruta, overwrite: true);
    }

    private Dictionary<string, string> LeerNombres()
    {
        if (_carpeta is null) return [];
        var ruta = Path.Combine(_carpeta, "mensajes.json");
        try
        {
            return File.Exists(ruta)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(ruta)) ?? []
                : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private string? RutaDelAudio(int numero) =>
        _carpeta is null ? null : Path.Combine(_carpeta, $"mensaje-{numero}.wav");

    private static void Comprobar(int numero)
    {
        if (numero is < 1 or > Ranuras) throw new ArgumentOutOfRangeException(nameof(numero), numero, null);
    }
}
