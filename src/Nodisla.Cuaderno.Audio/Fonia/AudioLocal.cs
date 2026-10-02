using Nodisla.Cuaderno.Audio.Procesado;

namespace Nodisla.Cuaderno.Audio.Fonia;

/// <summary>
/// Graba del microfono del PC <b>solo en local</b>: para los mensajes del voice keyer. No va
/// hacia el equipo ni toca el PTT.
/// </summary>
public interface IGrabadorDeMicrofono
{
    /// <summary>Esta grabando.</summary>
    bool Grabando { get; }

    /// <summary>Pico reciente de lo que entra, de 0 a 1.</summary>
    double Nivel { get; }

    /// <summary>Empieza a grabar del microfono.</summary>
    Task EmpezarAsync(string idMicrofono, CancellationToken ct = default);

    /// <summary>Para y devuelve lo grabado (nulo si no habia nada).</summary>
    Task<AudioEnMemoria?> PararAsync();
}

/// <summary>Suena audio por los altavoces del PC, para revisar un mensaje o un audio de un QSO.</summary>
public interface IReproductorLocal
{
    /// <summary>Esta sonando.</summary>
    bool Sonando { get; }

    /// <summary>Suena el audio entero y termina; se corta con el testigo o con <see cref="Parar"/>.</summary>
    Task ReproducirAsync(AudioEnMemoria audio, string idAltavoces, CancellationToken ct = default);

    /// <summary>Corta lo que suene.</summary>
    void Parar();
}
