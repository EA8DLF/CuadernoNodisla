using System.Reflection;

namespace Nodisla.Cuaderno.Radio.Modelos;

/// <summary>
/// Todos los modelos que el cuaderno sabe manejar, de todos los protocolos.
/// </summary>
/// <remarks>
/// Los modelos no se escriben aqui: los aporta cada <see cref="IProveedorDeModelos"/> del
/// ensamblado, que se encuentran solos por reflexion. Asi el de ICOM (carpeta
/// <c>Control/Icom</c>) se engancha sin tocar este fichero.
/// </remarks>
public static class CatalogoDeModelos
{
    /// <summary>Valor de <see cref="OpcionesDeRadio.Modelo"/> que pide la deteccion automatica.</summary>
    public const string Automatico = "auto";

    private static readonly Lazy<IReadOnlyList<IProveedorDeModelos>> LosProveedores = new(Descubrir);

    /// <summary>Los proveedores, en orden de deteccion.</summary>
    public static IReadOnlyList<IProveedorDeModelos> Proveedores => LosProveedores.Value;

    /// <summary>Todos los modelos, por fabricante y nombre.</summary>
    public static IReadOnlyList<ModeloDeEquipo> Todos =>
        Proveedores.SelectMany(p => p.Modelos)
            .OrderBy(m => m.Fabricante)
            .ThenBy(m => m.Nombre, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Fabricantes que tienen algun modelo.</summary>
    public static IReadOnlyList<Fabricante> Fabricantes => Todos.Select(m => m.Fabricante).Distinct().ToList();

    /// <summary>Modelos de un fabricante.</summary>
    /// <param name="fabricante">Fabricante.</param>
    /// <returns>Sus modelos, por nombre.</returns>
    public static IReadOnlyList<ModeloDeEquipo> De(Fabricante fabricante) =>
        Todos.Where(m => m.Fabricante == fabricante).ToList();

    /// <summary>Busca un modelo por su clave. Nulo si no existe o si es <see cref="Automatico"/>.</summary>
    /// <param name="clave">Clave guardada en los ajustes.</param>
    /// <returns>El modelo, o nulo.</returns>
    public static ModeloDeEquipo? Buscar(string? clave) =>
        string.IsNullOrWhiteSpace(clave) || clave == Automatico
            ? null
            : Todos.FirstOrDefault(m => string.Equals(m.Clave, clave, StringComparison.OrdinalIgnoreCase));

    /// <summary>El modelo Yaesu que contesta ese <c>ID;</c>.</summary>
    /// <param name="identificador">Las cuatro cifras (<c>0800</c>).</param>
    /// <returns>El modelo, o nulo si no se conoce.</returns>
    public static ModeloDeEquipo? PorIdentificadorYaesu(string? identificador) =>
        identificador is null
            ? null
            : Todos.FirstOrDefault(m => m.Protocolo == ProtocoloCat.YaesuAscii && m.IdentificadorYaesu == identificador.Trim());

    /// <summary>El modelo ICOM con esa direccion CI-V de fabrica.</summary>
    /// <param name="direccion">Direccion CI-V.</param>
    /// <returns>El modelo, o nulo si no se conoce.</returns>
    public static ModeloDeEquipo? PorDireccionCiv(byte direccion) =>
        Todos.FirstOrDefault(m => m.Protocolo == ProtocoloCat.IcomCiv && m.DireccionCiv == direccion);

    /// <summary>El proveedor que maneja un modelo.</summary>
    /// <param name="modelo">Modelo.</param>
    /// <returns>El proveedor, o nulo si no hay ninguno para su protocolo.</returns>
    public static IProveedorDeModelos? ProveedorDe(ModeloDeEquipo modelo)
    {
        ArgumentNullException.ThrowIfNull(modelo);
        return Proveedores.FirstOrDefault(p => p.Modelos.Contains(modelo))
            ?? Proveedores.FirstOrDefault(p => p.Protocolo == modelo.Protocolo);
    }

    private static IReadOnlyList<IProveedorDeModelos> Descubrir() =>
        typeof(CatalogoDeModelos).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && typeof(IProveedorDeModelos).IsAssignableFrom(t)
                        && t.GetConstructor(BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes) is not null)
            .Select(t => (IProveedorDeModelos)Activator.CreateInstance(t)!)
            .OrderBy(p => p.OrdenDeDeteccion)
            .ThenBy(p => p.GetType().FullName, StringComparer.Ordinal)
            .ToList();
}
