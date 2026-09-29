using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Ui.Desarrollo;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// Los mandos de la pestaña Cuaderno: buscar, filtrar, paginar, modificar y borrar, y lo que
/// pinta cada fila. Contra el repositorio en memoria, sin base de datos ni reloj de pared.
/// </summary>
public class PestanaCuadernoPruebas
{
    private static readonly DateTimeOffset Origen = new(2026, 5, 23, 9, 56, 0, TimeSpan.Zero);

    private static Qso Contacto(string indicativo, string banda, int minutos, string? nombre = null) => new()
    {
        Call = Indicativo.Crudo(indicativo),
        Band = Banda.Parse(banda),
        Mode = Modo.Crudo("SSB", "USB"),
        InicioUtc = Origen.AddMinutes(minutos),
        Name = nombre,
    };

    private static (VistaModeloCuaderno Modelo, RepositorioQsoEnMemoria Repositorio) Montar(int cuantos)
    {
        var qsos = Enumerable.Range(0, cuantos)
            .Select(i => Contacto($"EA{i % 9}AB{(char)('A' + (i % 26))}", i % 2 == 0 ? "20m" : "40m", i, $"Nombre {i}"));
        var repositorio = new RepositorioQsoEnMemoria(qsos);
        var modelo = new VistaModeloCuaderno(new BuscarEnCuaderno(repositorio), new EliminarQso(repositorio));
        return (modelo, repositorio);
    }

    [Fact]
    public async Task Sin_fila_elegida_modificar_y_borrar_estan_apagados_y_con_fila_se_encienden()
    {
        var (modelo, _) = Montar(3);
        await modelo.RefrescarAsync();

        modelo.EditarSeleccionadoCommand.CanExecute(null).Should().BeFalse();
        modelo.EliminarSeleccionadoCommand.CanExecute(null).Should().BeFalse();

        modelo.FilaSeleccionada = modelo.Filas[0];

        modelo.EditarSeleccionadoCommand.CanExecute(null).Should().BeTrue();
        modelo.EliminarSeleccionadoCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task Modificar_entrega_el_contacto_elegido()
    {
        var (modelo, _) = Montar(3);
        await modelo.RefrescarAsync();
        Qso? pedido = null;
        modelo.SolicitaEditar += (_, qso) => pedido = qso;

        modelo.FilaSeleccionada = modelo.Filas[1];
        modelo.EditarSeleccionadoCommand.Execute(null);

        pedido.Should().BeSameAs(modelo.Filas[1].Qso);
    }

    [Fact]
    public async Task Borrar_confirmado_quita_el_contacto_y_borrar_cancelado_no()
    {
        var (modelo, repositorio) = Montar(3);
        await modelo.RefrescarAsync();

        modelo.ConfirmarBorrado = _ => false;
        modelo.FilaSeleccionada = modelo.Filas[0];
        await modelo.EliminarSeleccionadoAsync();
        (await repositorio.ContarAsync()).Should().Be(3);

        modelo.ConfirmarBorrado = _ => true;
        modelo.FilaSeleccionada = modelo.Filas[0];
        var borrado = modelo.Filas[0].Id;
        await modelo.EliminarSeleccionadoAsync();

        (await repositorio.ContarAsync()).Should().Be(2);
        modelo.Filas.Should().NotContain(f => f.Id == borrado);
    }

    [Fact]
    public async Task La_paginacion_solo_deja_ir_donde_hay_pagina()
    {
        var (modelo, _) = Montar(120);
        modelo.TamanoDePagina = 50;
        await modelo.BuscarAsync();

        modelo.TotalDePaginas.Should().Be(3);
        modelo.PrimeraPaginaCommand.CanExecute(null).Should().BeFalse();
        modelo.PaginaAnteriorCommand.CanExecute(null).Should().BeFalse();
        modelo.PaginaSiguienteCommand.CanExecute(null).Should().BeTrue();

        await modelo.UltimaPaginaCommand.ExecuteAsync(null);

        modelo.Pagina.Should().Be(3);
        modelo.Filas.Should().HaveCount(20);
        modelo.PaginaSiguienteCommand.CanExecute(null).Should().BeFalse();
        modelo.UltimaPaginaCommand.CanExecute(null).Should().BeFalse();
        modelo.PaginaAnteriorCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task El_filtro_de_banda_deja_solo_esa_banda_y_quitar_filtro_lo_devuelve_todo()
    {
        var (modelo, _) = Montar(10);
        modelo.BandaFiltro = "40m";
        await modelo.BuscarAsync();

        modelo.Filas.Should().HaveCount(5).And.OnlyContain(f => f.Banda == "40m");

        await modelo.QuitarFiltroCommand.ExecuteAsync(null);

        modelo.BandaFiltro.Should().Be(modelo.Bandas[0]);
        modelo.Filas.Should().HaveCount(10);
    }

    [Fact]
    public async Task Buscar_por_texto_encuentra_el_indicativo()
    {
        var (modelo, _) = Montar(10);
        modelo.TextoBuscado = "EA3ABD";
        await modelo.BuscarAsync();

        modelo.Filas.Should().ContainSingle().Which.Indicativo.Should().Be("EA3ABD");
    }

    [Fact]
    public async Task La_rejilla_vacia_dice_si_es_el_filtro_o_el_cuaderno()
    {
        var (vacio, _) = Montar(0);
        await vacio.RefrescarAsync();
        vacio.PorQueNoHayFilas.Should().Contain("vacío");

        var (lleno, _) = Montar(4);
        lleno.TextoBuscado = "NADIE";
        await lleno.BuscarAsync();
        lleno.PorQueNoHayFilas.Should().Contain("filtro");

        await lleno.QuitarFiltroCommand.ExecuteAsync(null);
        lleno.PorQueNoHayFilas.Should().BeEmpty();
    }

    [Fact]
    public void Las_columnas_elegidas_se_guardan_y_se_recuperan()
    {
        var (modelo, _) = Montar(0);
        modelo.VerLocalizador = true;
        modelo.VerComentario = false;

        var guardadas = modelo.ColumnasVisibles();
        var (otro, _) = Montar(0);
        otro.PonerColumnasVisibles(guardadas);

        otro.VerLocalizador.Should().BeTrue();
        otro.VerComentario.Should().BeFalse();
        otro.ColumnasVisibles().Should().BeEquivalentTo(guardadas);
    }

    [Theory]
    [InlineData(EstadoDeConfirmacion.Confirmado, EstadoDeConfirmacion.Ninguno, EstadoDePastilla.Enviada)]
    [InlineData(EstadoDeConfirmacion.Confirmado, EstadoDeConfirmacion.Confirmado, EstadoDePastilla.Confirmada)]
    [InlineData(EstadoDeConfirmacion.Ninguno, EstadoDeConfirmacion.Verificado, EstadoDePastilla.Verificada)]
    [InlineData(EstadoDeConfirmacion.Ninguno, EstadoDeConfirmacion.Ninguno, EstadoDePastilla.Nada)]
    public void La_pastilla_distingue_enviada_confirmada_y_verificada(
        EstadoDeConfirmacion enviado, EstadoDeConfirmacion recibido, EstadoDePastilla esperado)
    {
        var qso = Contacto("EA8DLF", "20m", 0);
        qso.Confirmaciones.Add(new QsoConfirmacion { Medio = MedioDeConfirmacion.Lotw, Enviado = enviado, Recibido = recibido });

        var fila = new FilaDeQso(qso);

        fila.Lotw.Should().Be(esperado);
        fila.Eqsl.Should().Be(EstadoDePastilla.Nada);
        fila.ResumenQsl.Should().NotBeNullOrWhiteSpace();
    }
}
