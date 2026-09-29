namespace Nodisla.Cuaderno.Audio.Reloj;

/// <summary>Lo que se saca de preguntarle la hora a un servidor.</summary>
/// <param name="DesvioMs">
/// Cuanto adelanta el reloj del sistema frente al servidor, en milisegundos. Positivo es
/// adelantado.
/// </param>
/// <param name="IdaYVueltaMs">
/// Lo que tardo la consulta en ir y volver. Cuanto menos, mas fina es la medida: la mitad de
/// este tiempo es la incertidumbre que arrastra el desvio.
/// </param>
/// <param name="Fuente">Nombre del servidor que contesto.</param>
public sealed record MedidaDeHora(double DesvioMs, double IdaYVueltaMs, string Fuente);

/// <summary>Alguien a quien se le puede preguntar la hora de verdad.</summary>
/// <remarks>
/// Existe para que el reloj del modem se pueda probar sin red: en las pruebas se le da una
/// fuente de mentira que contesta lo que convenga, incluso que no contesta.
/// </remarks>
public interface IFuenteDeHora
{
    /// <summary>Nombre de la fuente, para ensenarselo al operador.</summary>
    string Nombre { get; }

    /// <summary>Pregunta la hora.</summary>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>La medida, o nulo si la fuente no contesto o contesto algo que no vale.</returns>
    Task<MedidaDeHora?> ConsultarAsync(CancellationToken ct = default);
}
