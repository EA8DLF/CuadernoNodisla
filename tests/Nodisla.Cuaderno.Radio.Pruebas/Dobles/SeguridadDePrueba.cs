using Nodisla.Cuaderno.Radio.Ptt;

namespace Nodisla.Cuaderno.Radio.Pruebas.Dobles;

/// <summary>
/// Salvaguardas para las pruebas de la mecanica del PTT con la captura del FT-710.
/// </summary>
/// <remarks>
/// La captura real esta en 27,555 MHz, fuera del plan de banda: con las salvaguardas puestas el
/// vigilante no dejaria subir el PTT. Esas pruebas miden otra cosa (sueltas, cierres, sintonia),
/// asi que aqui se quitan el plan y la ROE. Las salvaguardas se prueban en SeguridadDeTxPruebas.
/// </remarks>
internal static class SeguridadDePrueba
{
    /// <summary>Sin plan de banda ni vigilancia de ROE.</summary>
    public static OpcionesDeSeguridadDeTx SinPlanNiRoe { get; } = new() { BloquearFueraDeBanda = false, VigilarRoe = false };
}
