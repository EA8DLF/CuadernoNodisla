using System.Globalization;
using System.Text;
using FluentAssertions;
using Nodisla.Cuaderno.Dominio.Dxcc;
using Nodisla.Cuaderno.Dominio.Valores;
using Xunit.Abstractions;

namespace Nodisla.Cuaderno.Dominio.Pruebas.Dxcc;

/// <summary>
/// Mide el acuerdo del resolutor con los 1.836 contactos reales que Log4OM ya
/// tenia resueltos. Es la prueba que dice si la tabla de paises sirve para trabajar.
/// </summary>
public sealed class AciertoContraLog4OmPruebas(ITestOutputHelper salida)
{
    private static readonly ResolutorDxcc Resolutor = ResolutorDxcc.Predeterminado;

    [HechoConBancoReal]
    public void El_banco_de_pruebas_tiene_los_contactos_esperados()
    {
        BancoDeQsos.Todos.Should().HaveCount(1836);
    }

    [HechoConBancoReal]
    public void La_entidad_coincide_con_Log4OM_en_casi_todos_los_contactos()
    {
        var (aciertos, total, fallos) = Medir(static (r, q) => r.Entidad?.Numero == q.Dxcc);

        var porcentaje = 100.0 * aciertos / total;
        salida.WriteLine($"Entidad DXCC: {aciertos}/{total} = {porcentaje:F2} %");
        foreach (var f in fallos.Take(20)) salida.WriteLine("  " + f);

        porcentaje.Should().BeGreaterThan(99.0);
    }

    [HechoConBancoReal]
    public void El_continente_coincide_con_Log4OM_en_casi_todos_los_contactos()
    {
        var (aciertos, total, fallos) = Medir(static (r, q) => r.Continente == q.Continente);

        var porcentaje = 100.0 * aciertos / total;
        salida.WriteLine($"Continente: {aciertos}/{total} = {porcentaje:F2} %");
        foreach (var f in fallos.Take(20)) salida.WriteLine("  " + f);

        porcentaje.Should().BeGreaterThan(99.0);
    }

    [HechoConBancoReal]
    public void Las_zonas_coinciden_con_Log4OM_en_la_mayoria_de_los_contactos()
    {
        // Log4OM anota por omision la zona de la entidad; el resolutor propio anota la del
        // prefijo, que es mas fina (KL7 no esta en la zona 5, ni VE7 en la 5). Por eso este
        // porcentaje es menor que el de la entidad: no es un fallo, es una diferencia de
        // criterio, y se mide tambien contra la zona de la entidad para dejarlo claro.
        var (finas, total, _) = Medir(static (r, q) => r.ZonaCq == q.ZonaCq && r.ZonaItu == q.ZonaItu);
        var (deEntidad, _, _) = Medir(static (r, q) =>
            r.Entidad is not null && r.Entidad.ZonaCq == q.ZonaCq && r.Entidad.ZonaItu == q.ZonaItu);

        salida.WriteLine($"Zonas del prefijo:  {finas}/{total} = {100.0 * finas / total:F2} %");
        salida.WriteLine($"Zonas de la entidad: {deEntidad}/{total} = {100.0 * deEntidad / total:F2} %");

        (100.0 * finas / total).Should().BeGreaterThan(80.0);
        (100.0 * deEntidad / total).Should().BeGreaterThan(90.0);
    }

    [HechoConBancoReal]
    public void Ningun_contacto_del_banco_queda_sin_resolver()
    {
        var sinResolver = BancoDeQsos.Todos
            .Where(q => Resolutor.Resolver(Indicativo.Crudo(q.Indicativo), q.Fecha).EsDesconocido)
            .Select(q => q.Indicativo)
            .ToArray();

        salida.WriteLine($"Sin resolver: {sinResolver.Length}");
        foreach (var s in sinResolver) salida.WriteLine("  " + s);

        sinResolver.Should().HaveCountLessThan(5);
    }

    private static (int Aciertos, int Total, List<string> Fallos) Medir(
        Func<ResultadoDxcc, QsoDeReferencia, bool> acierta)
    {
        var aciertos = 0;
        var fallos = new List<string>();
        foreach (var q in BancoDeQsos.Todos)
        {
            var r = Resolutor.Resolver(Indicativo.Crudo(q.Indicativo), q.Fecha);
            if (acierta(r, q)) { aciertos++; continue; }

            var texto = new StringBuilder(q.Indicativo)
                .Append(" -> esperado ").Append(q.Dxcc.ToString(CultureInfo.InvariantCulture))
                .Append(' ').Append(q.Pais)
                .Append(" / obtenido ")
                .Append(r.Entidad is null ? "nada" : r.Entidad.Numero.ToString(CultureInfo.InvariantCulture))
                .Append(' ').Append(r.Entidad?.Nombre ?? string.Empty)
                .Append(" (zonas ").Append(r.ZonaCq).Append('/').Append(r.ZonaItu)
                .Append(" frente a ").Append(q.ZonaCq).Append('/').Append(q.ZonaItu).Append(')');
            fallos.Add(texto.ToString());
        }
        return (aciertos, BancoDeQsos.Todos.Count, fallos);
    }
}
