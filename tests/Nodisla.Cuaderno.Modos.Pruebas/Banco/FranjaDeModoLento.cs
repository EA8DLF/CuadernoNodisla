namespace Nodisla.Cuaderno.Modos.Pruebas.Banco;

/// <summary>Como fue una franja de relacion senal-ruido de un modo lento (JT65 o JT9).</summary>
/// <param name="Decibelios">Relacion senal-ruido de las senales de la franja, referida a 2500 Hz.</param>
/// <param name="Intentos">Ventanas probadas.</param>
/// <param name="Aciertos">Ventanas en que salio el mensaje que se habia emitido.</param>
/// <param name="Falsos">Mensajes que salieron y no eran el emitido. <b>Tiene que ser cero.</b></param>
/// <param name="Rechazadas">
/// Palabras de codigo que el decodificador tuvo entre manos y tiro por no pasar el criterio de
/// aceptacion. Es la medida del riesgo latente: cuantas veces se tento a la suerte.
/// </param>
/// <param name="ErrorDelInforme">Diferencia media entre el informe medido y el de verdad, en decibelios.</param>
/// <param name="MilisegundosPorVentana">Coste medio por ventana, en milisegundos de <b>procesador</b>.</param>
public sealed record FranjaDeModoLento(
    double Decibelios,
    int Intentos,
    int Aciertos,
    int Falsos,
    int Rechazadas,
    double ErrorDelInforme,
    double MilisegundosPorVentana)
{
    /// <summary>Porcentaje de mensajes recuperados.</summary>
    public double Porcentaje => Intentos == 0 ? 0 : 100.0 * Aciertos / Intentos;
}

/// <summary>Como fue una tanda de ventanas de ruido puro.</summary>
/// <param name="Ventanas">Ventanas probadas.</param>
/// <param name="Falsos">Mensajes que salieron. <b>Tiene que ser cero.</b></param>
/// <param name="Examinadas">Candidatas que pasaron el corte de sincronismo y llegaron al decodificador.</param>
/// <param name="Rechazadas">Palabras de codigo que salieron de algun intento y se tiraron.</param>
/// <param name="MilisegundosPorVentana">Coste medio por ventana, en milisegundos de procesador.</param>
public sealed record TandaDeRuidoDeModoLento(int Ventanas, int Falsos, int Examinadas, int Rechazadas, double MilisegundosPorVentana);
