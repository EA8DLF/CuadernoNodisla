using System.Globalization;

namespace Nodisla.Cuaderno.Radio.Control.Rigctld;

/// <summary>
/// Lo que contesta <c>rigctld</c> a una orden en modo extendido.
/// </summary>
/// <remarks>
/// En modo extendido —el que se consigue anteponiendo <c>+</c> a la orden— <c>rigctld</c>
/// devuelve primero el eco de la orden, luego una linea <c>Clave: valor</c> por dato y al final
/// <c>RPRT n</c>, que es lo que hace inequivoco saber donde termina la respuesta. Sin ese
/// terminador habria que adivinar cuantas lineas trae cada orden.
/// </remarks>
/// <param name="Codigo">Codigo de <c>RPRT</c>: cero si todo fue bien, negativo si hubo error.</param>
/// <param name="Lineas">Lineas de la respuesta, sin el eco ni el <c>RPRT</c>.</param>
public sealed record RespuestaRigctld(int Codigo, IReadOnlyList<string> Lineas)
{
    /// <summary>La orden se ejecuto sin error.</summary>
    public bool Bien => Codigo == 0;

    /// <summary>Busca el valor de una clave, por ejemplo <c>Frequency</c>.</summary>
    /// <param name="clave">Clave a buscar, sin los dos puntos y sin distinguir mayusculas.</param>
    /// <returns>El valor si esta, o nulo.</returns>
    public string? Valor(string clave)
    {
        foreach (var linea in Lineas)
        {
            var separador = linea.IndexOf(':', StringComparison.Ordinal);
            if (separador <= 0)
            {
                continue;
            }

            if (linea.AsSpan(0, separador).Trim().Equals(clave, StringComparison.OrdinalIgnoreCase))
            {
                return linea[(separador + 1)..].Trim();
            }
        }

        return null;
    }

    /// <summary>Valor de una clave leido como numero entero.</summary>
    /// <param name="clave">Clave a buscar.</param>
    /// <returns>El numero si se pudo leer, o nulo.</returns>
    public long? Entero(string clave) =>
        long.TryParse(Valor(clave), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n
            : null;

    /// <summary>Valor de una clave leido como numero con decimales.</summary>
    /// <param name="clave">Clave a buscar.</param>
    /// <returns>El numero si se pudo leer, o nulo.</returns>
    public double? Decimal(string clave) =>
        double.TryParse(Valor(clave), NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
            ? n
            : null;

    /// <summary>
    /// Primera linea suelta de la respuesta, para las ordenes que contestan un valor pelado.
    /// </summary>
    /// <returns>La linea, o nulo si no hay ninguna.</returns>
    public string? ValorSuelto() => Lineas.Count > 0 ? Lineas[0].Trim() : null;
}
