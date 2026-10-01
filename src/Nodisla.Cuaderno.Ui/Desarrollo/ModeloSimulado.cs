using Nodisla.Cuaderno.Radio.Modelos;

namespace Nodisla.Cuaderno.Ui.Desarrollo;

/// <summary>
/// El modelo con el que se presenta el <see cref="EquipoSimulado"/>: el FT-710 de siempre, u
/// otro con <c>CUADERNO_MODELO=&lt;modelo&gt;</c> para ver su frontal sin radio
/// (docs/18-frontales.md).
/// </summary>
/// <remarks>
/// El valor se busca en <see cref="CatalogoDeModelos"/> por clave (<c>icom-ic7300</c>) o por
/// nombre, sin fijarse en mayúsculas, guiones ni espacios (<c>IC-7300</c>, <c>ic7300</c>,
/// <c>FT-991A</c>). Si el catálogo todavía no lo tiene, sirve la tabla de aquí, que solo lleva
/// lo que hace falta para elegir y dibujar el frontal. <c>generico</c> da un modelo sin dibujo
/// propio: sale el frontal genérico.
/// </remarks>
public static class ModeloSimulado
{
    private const long Hf = 30_000;
    private const long Seis = 54_000_000;

    private static readonly ModeloDeEquipo[] Propios =
    [
        Crear("yaesu-ft710", Fabricante.Yaesu, "FT-710", "FrontalFt710", analizador: true, sintonizador: true, dosReceptores: false),
        Crear("yaesu-ftdx10", Fabricante.Yaesu, "FTDX10", "FrontalFtdx10", analizador: true, sintonizador: true, dosReceptores: false),
        Crear("yaesu-ftdx101", Fabricante.Yaesu, "FTDX101D", "FrontalFtdx101", analizador: true, sintonizador: true, dosReceptores: true),
        Crear("yaesu-ft991", Fabricante.Yaesu, "FT-991A", "FrontalFt991", analizador: true, sintonizador: true, dosReceptores: false, maximo: 470_000_000),
        Crear("yaesu-ft891", Fabricante.Yaesu, "FT-891", "FrontalFt891", analizador: false, sintonizador: false, dosReceptores: false),
        Crear("icom-ic7300", Fabricante.Icom, "IC-7300", "FrontalIc7300", analizador: true, sintonizador: true, dosReceptores: false, maximo: 74_800_000),
        Crear("icom-ic705", Fabricante.Icom, "IC-705", "FrontalIc705", analizador: true, sintonizador: false, dosReceptores: false, maximo: 450_000_000),
        Crear("icom-ic7610", Fabricante.Icom, "IC-7610", "FrontalIc7610", analizador: true, sintonizador: true, dosReceptores: true),
        Crear("generico", Fabricante.Yaesu, "Genérico", null, analizador: true, sintonizador: true, dosReceptores: false),
    ];

    /// <summary>El modelo pedido con <c>CUADERNO_MODELO</c>, o el FT-710.</summary>
    /// <returns>El modelo.</returns>
    public static ModeloDeEquipo DelEntorno() => Buscar(Environment.GetEnvironmentVariable("CUADERNO_MODELO"));

    /// <summary>Busca un modelo por clave o nombre; el FT-710 si no se encuentra.</summary>
    /// <param name="pedido">Clave o nombre.</param>
    /// <returns>El modelo.</returns>
    public static ModeloDeEquipo Buscar(string? pedido)
    {
        if (string.IsNullOrWhiteSpace(pedido)) pedido = "yaesu-ft710";
        var buscado = Limpiar(pedido);

        IEnumerable<ModeloDeEquipo> delCatalogo;
        try
        {
            delCatalogo = CatalogoDeModelos.Todos;
        }
        catch (Exception)
        {
            delCatalogo = [];
        }

        // Primero el catálogo (si ya tiene el modelo, manda él); luego la tabla de aquí.
        foreach (var lista in new[] { delCatalogo, Propios })
        {
            foreach (var modelo in lista)
            {
                if (Coincide(modelo, buscado)) return modelo;
            }
        }

        // «FT-991A» cuando el catálogo solo tiene «FT-991», «FTDX101MP» y parecidos: el nombre
        // más largo que empiece igual (así «FTDX101MP» no se queda en el FTDX10).
        return delCatalogo.Concat(Propios)
            .Select(m => (Modelo: m, Comun: ComunAlPrincipio(Limpiar(m.Nombre), buscado)))
            .Where(p => p.Comun >= 5)
            .OrderByDescending(p => p.Comun)
            .Select(p => p.Modelo)
            .FirstOrDefault() ?? Propios[0];
    }

    private static int ComunAlPrincipio(string a, string b)
    {
        var n = 0;
        while (n < a.Length && n < b.Length && a[n] == b[n]) n++;
        return n;
    }

    private static bool Coincide(ModeloDeEquipo modelo, string buscado) =>
        Limpiar(modelo.Clave) == buscado
        || Limpiar(modelo.Nombre) == buscado
        || Limpiar(modelo.NombreCompleto) == buscado;

    private static string Limpiar(string texto) =>
        new(texto.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static ModeloDeEquipo Crear(
        string clave, Fabricante fabricante, string nombre, string? frontal,
        bool analizador, bool sintonizador, bool dosReceptores, long maximo = Seis) =>
        new(
            clave,
            fabricante,
            nombre,
            fabricante == Fabricante.Icom ? ProtocoloCat.IcomCiv : ProtocoloCat.YaesuAscii,
            IdentificadorYaesu: null,
            DireccionCiv: null,
            Velocidades: [38400],
            new CapacidadesDelModelo(
                DosVfos: true, DosReceptores: dosReceptores, Analizador: analizador, Sintonizador: sintonizador,
                Memorias: 99, Mandos: true, Encendido: false, HerciosMinimo: Hf, HerciosMaximo: maximo),
            frontal,
            ProbadoConRadio: clave == "yaesu-ft710",
            Notas: "Modelo del equipo simulado (CUADERNO_MODELO).");
}
