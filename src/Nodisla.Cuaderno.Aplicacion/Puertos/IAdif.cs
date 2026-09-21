using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Aplicacion.Puertos;

/// <summary>Gravedad de un aviso de lectura de ADIF.</summary>
public enum NivelDeAviso
{
    /// <summary>
    /// Algo digno de mencion que no afecta al dato: una rareza del programa que genero el
    /// fichero, corregida al vuelo. La interfaz puede ocultarlos por omision.
    /// </summary>
    Informativo,

    /// <summary>El dato se ha leido, pero con una interpretacion que conviene revisar.</summary>
    Advertencia,

    /// <summary>El registro no se ha podido leer y se descarta.</summary>
    Error,
}

/// <summary>Un problema encontrado al leer un fichero ADIF.</summary>
/// <param name="NumeroDeRegistro">Registro del fichero, empezando en 1.</param>
/// <param name="Campo">Campo afectado, si se conoce.</param>
/// <param name="Mensaje">Explicacion en espanol, pensada para el operador.</param>
/// <param name="Nivel">Gravedad del aviso.</param>
/// <remarks>
/// El nivel existe porque un respaldo normal de Log4OM genera decenas de avisos rutinarios
/// (rotulos de adorno en el condado, por ejemplo). Sin niveles, la interfaz tendria que
/// elegir entre enterrar al operador en ruido o esconderle los problemas de verdad.
/// </remarks>
public sealed record AvisoAdif(int NumeroDeRegistro, string? Campo, string Mensaje, NivelDeAviso Nivel)
{
    /// <summary>El registro se descarto.</summary>
    public bool EsFatal => Nivel == NivelDeAviso.Error;
}

/// <summary>Resultado de leer un fichero ADIF.</summary>
public sealed record LecturaAdif
{
    public required IReadOnlyList<Qso> Qsos { get; init; }

    public required IReadOnlyList<AvisoAdif> Avisos { get; init; }

    /// <summary>Cabecera del fichero, campo a campo.</summary>
    public IReadOnlyDictionary<string, string> Cabecera { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Programa que genero el fichero, del campo <c>PROGRAMID</c>.</summary>
    public string? ProgramaOrigen =>
        Cabecera.TryGetValue("PROGRAMID", out var v) ? v : null;
}

/// <summary>
/// Lectura de ficheros ADIF.
/// </summary>
/// <remarks>
/// Ha de ser tolerante: los ficheros reales traen campos desconocidos, longitudes declaradas
/// que no coinciden con el contenido, acentos en codificaciones mezcladas y registros a medias.
/// Un fichero sucio nunca debe abortar la importacion entera; se avisa y se sigue.
/// </remarks>
public interface ILectorAdif
{
    /// <summary>Lee un fichero ADI o ADX desde un flujo.</summary>
    Task<LecturaAdif> LeerAsync(Stream origen, CancellationToken ct = default);
}

/// <summary>Opciones de exportacion a ADIF.</summary>
public sealed record OpcionesAdif
{
    /// <summary>Escribir ADX (XML) en lugar de ADI (texto plano).</summary>
    public bool Adx { get; init; }

    /// <summary>Incluir los campos <c>APP_*</c> y los campos extra conservados al importar.</summary>
    public bool IncluirCamposExtra { get; init; } = true;

    /// <summary>Nombre del programa que se escribe en la cabecera.</summary>
    public string ProgramId { get; init; } = "Cuaderno NODISLA";
}

/// <summary>Escritura de ficheros ADIF.</summary>
public interface IEscritorAdif
{
    /// <summary>Escribe los contactos en el flujo de destino.</summary>
    Task EscribirAsync(
        IAsyncEnumerable<Qso> qsos,
        Stream destino,
        OpcionesAdif? opciones = null,
        CancellationToken ct = default);
}
