namespace Nodisla.Cuaderno.Dominio.Entidades;

/// <summary>
/// Referencia de un programa de activaciones asociada al contacto: una isla IOTA, una cima
/// SOTA, un parque POTA, un castillo WCA. Puede ser del corresponsal o de mi estacion.
/// </summary>
public sealed class QsoReferencia
{
    public long Id { get; set; }

    public long QsoId { get; set; }

    /// <summary>Programa al que pertenece la referencia.</summary>
    public TipoDeReferencia Tipo { get; set; }

    /// <summary>Nombre del programa cuando el tipo es <see cref="TipoDeReferencia.Otra"/>.</summary>
    public string? NombrePrograma { get; set; }

    /// <summary>Codigo de la referencia, por ejemplo <c>EU-004</c> o <c>EA8/GC-001</c>.</summary>
    public required string Codigo { get; set; }

    /// <summary>Si la referencia es del corresponsal o mia.</summary>
    public LadoDeReferencia Lado { get; set; }

    /// <summary>Descripcion legible, rellenada desde la base de referencia.</summary>
    public string? Descripcion { get; set; }
}
