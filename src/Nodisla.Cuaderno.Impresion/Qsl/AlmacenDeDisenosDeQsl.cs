using Nodisla.Cuaderno.Impresion.Plantillas;

namespace Nodisla.Cuaderno.Impresion.Qsl;

/// <summary>
/// Las plantillas de tarjeta guardadas: un JSON y, si la tiene, su imagen de fondo.
/// </summary>
/// <remarks>
/// <para>
/// Viven en <c>&lt;carpeta de datos&gt;\qsl\disenos\</c>: <c>&lt;id&gt;.json</c> con la
/// plantilla y <c>&lt;id&gt;-fondo-&lt;hora&gt;.&lt;ext&gt;</c> con la foto. El guardado, la
/// copia de imagenes, duplicar, exportar e importar son los de <see cref="AlmacenDePlantillas{T}"/>,
/// el mismo motor que usan los diplomas.
/// </para>
/// <para>
/// Si no hay ninguna guardada se ofrece la del programa (<see cref="DisenoDeQsl.PorOmision"/>)
/// sin escribirla: el disco no se toca hasta que el operador guarda algo suyo.
/// </para>
/// </remarks>
public sealed class AlmacenDeDisenosDeQsl : AlmacenDePlantillas<DisenoDeQsl>
{
    /// <summary>Crea el almacen sobre la carpeta de datos del programa.</summary>
    /// <param name="carpetaDeDatos">La carpeta de datos (la de <c>CUADERNO_CARPETA</c> en pruebas).</param>
    public AlmacenDeDisenosDeQsl(string carpetaDeDatos)
        : base(Path.Combine(Requerida(carpetaDeDatos), "qsl", "disenos"))
    {
    }

    /// <summary>Copia una imagen como fondo de la plantilla y la apunta en ella.</summary>
    /// <param name="diseno">La plantilla (se modifica su <see cref="DisenoDeQsl.ImagenDeFondo"/>).</param>
    /// <param name="rutaDeLaImagen">La foto elegida por el operador.</param>
    public void PonerFondo(DisenoDeQsl diseno, string rutaDeLaImagen)
    {
        ArgumentNullException.ThrowIfNull(diseno);
        var nombre = CopiarImagen(diseno, rutaDeLaImagen, "fondo");
        var anterior = diseno.ImagenDeFondo;
        diseno.ImagenDeFondo = nombre;
        if (!string.IsNullOrEmpty(anterior) && anterior != nombre && RutaDeImagen(anterior) is { } vieja) BorrarSinFallar(vieja);
    }

    /// <summary>Ruta completa de la imagen de fondo, si la tiene y existe.</summary>
    /// <param name="diseno">La plantilla.</param>
    /// <returns>La ruta o nulo.</returns>
    public string? RutaDelFondo(DisenoDeQsl diseno)
    {
        ArgumentNullException.ThrowIfNull(diseno);
        return RutaDeImagen(diseno.ImagenDeFondo);
    }

    /// <inheritdoc />
    protected override IReadOnlyList<DisenoDeQsl> DeFabrica() => [DisenoDeQsl.PorOmision()];

    /// <inheritdoc />
    protected override DisenoDeQsl Clonar(DisenoDeQsl plantilla) => plantilla.Copiar();

    /// <inheritdoc />
    protected override void Normalizar(DisenoDeQsl plantilla) => plantilla.Campos ??= [];

    private static string Requerida(string carpeta)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(carpeta);
        return carpeta;
    }
}
