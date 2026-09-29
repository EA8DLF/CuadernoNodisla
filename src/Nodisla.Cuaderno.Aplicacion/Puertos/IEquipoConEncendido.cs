namespace Nodisla.Cuaderno.Aplicacion.Puertos;

/// <summary>
/// Un equipo que se puede apagar y encender desde el programa (la pulsación larga de LOCK del
/// frontal dibujado del FT-710).
/// </summary>
/// <remarks>
/// Pedido y autorizado expresamente por el operador el 29-09-2026. Apagar solo se hace tras la
/// confirmación del operador; quien llama es responsable de preguntar.
/// </remarks>
public interface IEquipoConEncendido
{
    /// <summary>Baja el PTT, apaga el equipo y cierra la comunicación.</summary>
    Task ApagarAsync(CancellationToken ct = default);

    /// <summary>
    /// Enciende el equipo por CAT y vuelve a conectar cuando conteste.
    /// </summary>
    /// <returns>
    /// Nulo si se ha encendido y conectado; si no, el motivo en palabras del operador (por
    /// ejemplo, que con la radio apagada el puerto USB desaparece y no se la puede despertar).
    /// </returns>
    Task<string?> EncenderAsync(CancellationToken ct = default);
}
