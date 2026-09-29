using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Pruebas.Dobles;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Aplicacion.Pruebas;

public sealed class BuscarEnCuadernoPruebas
{
    private readonly RepositorioQsoDoble _cuaderno = new();
    private readonly BuscarEnCuaderno _caso;

    public BuscarEnCuadernoPruebas()
    {
        _caso = new BuscarEnCuaderno(_cuaderno);
        for (var i = 0; i < 25; i++)
        {
            var qso = Ayuda.Qso(
                call: $"EA{i % 9 + 1}AA{i}",
                inicioUtc: new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero).AddHours(i));
            qso.Qth = i % 2 == 0 ? "Santa Cruz" : "Las Palmas";
            _cuaderno.Sembrar(qso);
        }
    }

    [Fact]
    public async Task Devuelve_la_pagina_pedida_y_el_total_que_hay_detras()
    {
        var pagina = await _caso.EjecutarAsync(new CriterioQso(), desplazamiento: 10, limite: 5);

        pagina.Elementos.Should().HaveCount(5);
        pagina.TotalFiltrado.Should().Be(25);
        pagina.Desplazamiento.Should().Be(10);
    }

    [Fact]
    public async Task Ordena_por_omision_del_contacto_mas_reciente_al_mas_antiguo()
    {
        var pagina = await _caso.EjecutarAsync(new CriterioQso(), limite: 3);

        pagina.Elementos.Select(q => q.InicioUtc).Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task Ordena_al_reves_cuando_se_pide_ascendente()
    {
        var pagina = await _caso.EjecutarAsync(new CriterioQso { Descendente = false }, limite: 3);

        pagina.Elementos.Select(q => q.InicioUtc).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Filtra_por_texto_libre()
    {
        var pagina = await _caso.EjecutarAsync(new CriterioQso { Texto = "Las Palmas" });

        pagina.TotalFiltrado.Should().Be(12);
        pagina.Elementos.Should().OnlyContain(q => q.Qth == "Las Palmas");
    }

    [Fact]
    public async Task Filtra_por_banda()
    {
        _cuaderno.Sembrar(Ayuda.Qso(call: "EA5ZZZ", banda: "40m", mhz: 7.1m));

        var pagina = await _caso.EjecutarAsync(new CriterioQso { Band = Banda.Parse("40m") });

        pagina.TotalFiltrado.Should().Be(1);
        pagina.Elementos.Should().ContainSingle().Which.Call.Valor.Should().Be("EA5ZZZ");
    }

    [Fact]
    public async Task Acota_el_limite_al_maximo_admitido()
    {
        var pagina = await _caso.EjecutarAsync(new CriterioQso(), limite: 999_999);

        pagina.Elementos.Should().HaveCount(25);
    }

    [Fact]
    public async Task Corrige_el_desplazamiento_negativo()
    {
        var pagina = await _caso.EjecutarAsync(new CriterioQso(), desplazamiento: -40, limite: 2);

        pagina.Desplazamiento.Should().Be(0);
        pagina.Elementos.Should().HaveCount(2);
    }

    [Fact]
    public async Task Cuenta_todo_el_cuaderno()
    {
        (await _caso.ContarTodoAsync()).Should().Be(25);
    }

    [Fact]
    public async Task Ordena_las_bandas_y_los_modos_por_el_campo_que_se_le_pide()
    {
        var pagina = await _caso.EjecutarAsync(
            new CriterioQso { OrdenarPor = CampoDeOrden.Indicativo, Descendente = false });

        pagina.Elementos.Select(q => q.Call.Valor).Should().BeInAscendingOrder(StringComparer.Ordinal);
    }

    /// <summary>
    /// Esta es la prueba del desempate por Id que exige el contrato: con muchos contactos en el
    /// mismo segundo, recorrer el cuaderno pagina a pagina tiene que devolver cada contacto una
    /// vez y ninguno de mas. Sin desempate, el operador veria un cuaderno que miente.
    /// </summary>
    [Fact]
    public async Task Paginar_no_repite_ni_se_salta_contactos_del_mismo_segundo()
    {
        var cuaderno = new RepositorioQsoDoble();
        var caso = new BuscarEnCuaderno(cuaderno);
        var mismoInstante = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

        for (var i = 0; i < 20; i++)
        {
            cuaderno.Sembrar(Ayuda.Qso(call: $"EA{i % 9 + 1}ZZ{i}", inicioUtc: mismoInstante));
        }

        var vistos = new List<long>();
        for (var desplazamiento = 0; desplazamiento < 20; desplazamiento += 3)
        {
            var pagina = await caso.EjecutarAsync(new CriterioQso(), desplazamiento, limite: 3);
            vistos.AddRange(pagina.Elementos.Select(q => q.Id));
        }

        vistos.Should().HaveCount(20);
        vistos.Should().OnlyHaveUniqueItems();
        vistos.Should().BeEquivalentTo(cuaderno.Contenido.Select(q => q.Id));
    }

    [Fact]
    public async Task Anadir_un_lote_salta_los_duplicados_y_dice_cuantos()
    {
        var cuaderno = new RepositorioQsoDoble();
        cuaderno.Sembrar(Ayuda.Qso(call: "EA1ABC"));

        var resultado = await cuaderno.AnadirLoteAsync(
        [
            Ayuda.Qso(call: "EA1ABC"),
            Ayuda.Qso(call: "EA2DEF"),
            Ayuda.Qso(call: "EA3GHI"),
        ]);

        resultado.Anadidos.Should().Be(2);
        resultado.OmitidosPorDuplicado.Should().Be(1);
        resultado.Total.Should().Be(3);
        cuaderno.Contenido.Should().HaveCount(3);
    }
}
