using Nodisla.Cuaderno.Impresion.Plantillas;

namespace Nodisla.Cuaderno.Impresion.Diplomas;

/// <summary>
/// Las plantillas de diploma guardadas, en <c>&lt;carpeta de datos&gt;\qsl\diplomas\</c>. El
/// motor es el mismo de las tarjetas QSL (<see cref="AlmacenDePlantillas{T}"/>).
/// </summary>
public sealed class AlmacenDeDisenosDeDiploma : AlmacenDePlantillas<DisenoDeDiploma>
{
    /// <summary>Extension de las plantillas exportadas.</summary>
    public const string ExtensionExportada = ".diploma-nodisla";

    /// <summary>Crea el almacen sobre la carpeta de datos del programa.</summary>
    /// <param name="carpetaDeDatos">La carpeta de datos.</param>
    public AlmacenDeDisenosDeDiploma(string carpetaDeDatos)
        : base(Path.Combine(Requerida(carpetaDeDatos), "qsl", "diplomas"))
    {
    }

    /// <summary>Copia una imagen como fondo del diploma, soltando la anterior.</summary>
    /// <param name="diseno">La plantilla.</param>
    /// <param name="ruta">La imagen elegida.</param>
    public void PonerFondo(DisenoDeDiploma diseno, string ruta)
    {
        ArgumentNullException.ThrowIfNull(diseno);
        var anterior = diseno.ImagenDeFondo;
        diseno.ImagenDeFondo = CopiarImagen(diseno, ruta, "fondo");
        if (!string.IsNullOrEmpty(anterior)) OlvidarImagen(anterior);
    }

    /// <summary>Copia una imagen para un hueco (logo, firma…), soltando la anterior.</summary>
    /// <param name="diseno">La plantilla.</param>
    /// <param name="imagen">El hueco.</param>
    /// <param name="ruta">La imagen elegida.</param>
    public void PonerImagen(DisenoDeDiploma diseno, ImagenDeDiploma imagen, string ruta)
    {
        ArgumentNullException.ThrowIfNull(diseno);
        ArgumentNullException.ThrowIfNull(imagen);
        var anterior = imagen.Fichero;
        imagen.Fichero = CopiarImagen(diseno, ruta, imagen.Uso.ToString());
        if (!string.IsNullOrEmpty(anterior)) OlvidarImagen(anterior);
    }

    /// <inheritdoc />
    protected override IReadOnlyList<DisenoDeDiploma> DeFabrica() => PlantillasDeDiplomaDeFabrica.Todas();

    /// <inheritdoc />
    protected override DisenoDeDiploma Clonar(DisenoDeDiploma plantilla) => plantilla.Copiar();

    /// <inheritdoc />
    protected override void Normalizar(DisenoDeDiploma plantilla)
    {
        plantilla.Campos ??= [];
        plantilla.ImagenesColocadas ??= [];
        plantilla.Marco ??= new MarcoDeDiploma();
        plantilla.Tabla ??= new TablaDeDiploma();
        plantilla.Tabla.Columnas ??= [];
        plantilla.Tabla.Bloques = Math.Clamp(plantilla.Tabla.Bloques, 1, 4);
        if (string.IsNullOrWhiteSpace(plantilla.Serie)) plantilla.Serie = "NODISLA";
    }

    private static string Requerida(string carpeta)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(carpeta);
        return carpeta;
    }
}
