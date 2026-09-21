using System.Diagnostics;
using FluentAssertions;
using Nodisla.Cuaderno.Dominio.Dxcc;
using Nodisla.Cuaderno.Dominio.Valores;
using Xunit.Abstractions;

namespace Nodisla.Cuaderno.Dominio.Pruebas.Dxcc;

/// <summary>
/// La resolucion se dispara con cada tecla que escribe el operador, asi que tiene que
/// costar microsegundos. Aqui se mide, no se supone.
/// </summary>
public sealed class RendimientoDxccPruebas(ITestOutputHelper salida)
{
    [Fact]
    public void Resolver_un_indicativo_cuesta_menos_de_diez_microsegundos()
    {
        var resolutor = ResolutorDxcc.Predeterminado;
        var fecha = new DateOnly(2026, 1, 1);
        var indicativos = BancoDeQsos.Todos
            .Select(q => Indicativo.Crudo(q.Indicativo))
            .ToArray();

        // Calentamiento: la primera llamada carga el catalogo y compila el codigo.
        foreach (var i in indicativos) resolutor.Resolver(i, fecha);

        const int Vueltas = 20;
        var reloj = Stopwatch.StartNew();
        for (var v = 0; v < Vueltas; v++)
        {
            foreach (var i in indicativos) resolutor.Resolver(i, fecha);
        }
        reloj.Stop();

        var resoluciones = (long)Vueltas * indicativos.Length;
        var microsegundos = reloj.Elapsed.TotalMilliseconds * 1000.0 / resoluciones;
        salida.WriteLine($"{resoluciones} resoluciones en {reloj.ElapsedMilliseconds} ms");
        salida.WriteLine($"media: {microsegundos:F3} us por indicativo");

        microsegundos.Should().BeLessThan(10.0);
    }

    [Fact]
    public void Cargar_el_catalogo_entero_cuesta_menos_de_dos_segundos()
    {
        var reloj = Stopwatch.StartNew();
        var catalogo = CatalogoDxcc.Cargar(LeerRecursoIncrustado());
        reloj.Stop();

        salida.WriteLine($"catalogo cargado en {reloj.ElapsedMilliseconds} ms: "
                         + $"{catalogo.Todas.Count} entidades, {catalogo.NodosDelArbol} nodos");

        reloj.Elapsed.TotalSeconds.Should().BeLessThan(2.0);
        catalogo.Todas.Should().NotBeEmpty();
    }

    /// <summary>Vuelve a leer el texto del recurso a traves del catalogo ya cargado.</summary>
    private static string LeerRecursoIncrustado()
    {
        // El texto vive en el dominio como constante interna; se llega a el reconstruyendo
        // el catalogo por omision, que es lo que se quiere cronometrar de verdad.
        var tipo = typeof(CatalogoDxcc).Assembly.GetType("Nodisla.Cuaderno.Dominio.Dxcc.DatosPaises")!;
        var campo = tipo.GetField("Texto",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
        return (string)campo.GetRawConstantValue()!;
    }
}
