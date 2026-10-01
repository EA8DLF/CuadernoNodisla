namespace Nodisla.Cuaderno.Dominio.Entidades;

/// <summary>De donde sale un diploma impreso.</summary>
public enum OrigenDeDiplomaEmitido
{
    /// <summary>Lo emite esta estacion a otra (diploma propio o de club).</summary>
    Emitido = 0,

    /// <summary>Certificado de un diploma conseguido por esta estacion.</summary>
    Conseguido,
}

/// <summary>
/// Un diploma o certificado emitido: quien lo recibe, cuando, con que numero y lo que se imprimio.
/// </summary>
/// <remarks>
/// <para>
/// El <see cref="Numero"/> es correlativo dentro de su <see cref="Serie"/> y no se repite nunca
/// (indice unico en la base de datos): es lo que da valor a un diploma emitido.
/// </para>
/// <para>
/// <see cref="Datos"/> guarda en JSON lo que se imprimio (nombre, categoria, tabla…), para que
/// volver a imprimirlo saque exactamente lo mismo aunque el cuaderno cambie despues.
/// </para>
/// </remarks>
public sealed class DiplomaEmitido
{
    /// <summary>Identificador en la base de datos.</summary>
    public long Id { get; set; }

    /// <summary>Serie de la numeracion.</summary>
    public required string Serie { get; set; }

    /// <summary>Numero correlativo dentro de la serie, desde 1.</summary>
    public int Numero { get; set; }

    /// <summary>Emitido a otra estacion o certificado propio.</summary>
    public OrigenDeDiplomaEmitido Origen { get; set; }

    /// <summary>Indicativo del que lo recibe.</summary>
    public required string Indicativo { get; set; }

    /// <summary>Su nombre.</summary>
    public string? Nombre { get; set; }

    /// <summary>Nombre del diploma.</summary>
    public required string NombreDelDiploma { get; set; }

    /// <summary>Categoria o nivel.</summary>
    public string? Categoria { get; set; }

    /// <summary>Cuando se emitio.</summary>
    public DateTimeOffset EmitidoUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Plantilla con la que se imprimio.</summary>
    public string? PlantillaId { get; set; }

    /// <summary>Nombre de la plantilla en ese momento.</summary>
    public string? PlantillaNombre { get; set; }

    /// <summary>Referencias que lo justifican.</summary>
    public int Referencias { get; set; }

    /// <summary>Contactos que lo justifican.</summary>
    public int Qsos { get; set; }

    /// <summary>Direccion a la que se mando, si se mando.</summary>
    public string? Correo { get; set; }

    /// <summary>Cuando se mando por correo; nulo si no se ha mandado.</summary>
    public DateTimeOffset? EnviadoUtc { get; set; }

    /// <summary>Lo impreso, en JSON.</summary>
    public string? Datos { get; set; }
}
