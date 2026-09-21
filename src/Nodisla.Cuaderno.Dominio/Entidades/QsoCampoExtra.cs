namespace Nodisla.Cuaderno.Dominio.Entidades;

/// <summary>
/// Campo ADIF que el modelo no representa con una propiedad propia.
/// </summary>
/// <remarks>
/// Existe para cumplir una regla innegociable: <b>importar y volver a exportar un fichero ADIF
/// no puede perder ni un solo dato</b>. Los campos <c>APP_*</c> de otros programas y los campos
/// del estandar que aun no se modelan se guardan aqui tal cual y se vuelven a escribir igual.
/// </remarks>
public sealed class QsoCampoExtra
{
    public long Id { get; set; }

    public long QsoId { get; set; }

    /// <summary>Nombre del campo ADIF en mayusculas, por ejemplo <c>APP_LOG4OM_QSO_ID</c>.</summary>
    public required string Nombre { get; set; }

    /// <summary>Valor tal y como venia en el fichero.</summary>
    public required string Valor { get; set; }

    /// <summary>Indicador de tipo ADIF si el fichero lo traia, por ejemplo <c>D</c> o <c>N</c>.</summary>
    public string? TipoAdif { get; set; }
}
