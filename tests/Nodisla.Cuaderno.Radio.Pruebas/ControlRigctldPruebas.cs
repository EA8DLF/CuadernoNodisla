using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Radio.Control.Rigctld;
using Nodisla.Cuaderno.Radio.Ptt;
using Nodisla.Cuaderno.Radio.Pruebas.Dobles;

namespace Nodisla.Cuaderno.Radio.Pruebas;

/// <summary>
/// El control generico por <c>rigctld</c>, probado contra un demonio de mentira que habla el
/// mismo protocolo de texto que el de Hamlib.
/// </summary>
public class ControlRigctldPruebas
{
    private static OpcionesRigctld Opciones(int puerto) => new()
    {
        Maquina = "127.0.0.1",
        Puerto = puerto,
        LanzarElDemonio = false,
        IntervaloDeSondeo = TimeSpan.FromMilliseconds(50),
        EsperaDeOrden = TimeSpan.FromMilliseconds(500),
        EsperaDeReconexion = TimeSpan.FromMilliseconds(50),
    };

    [Fact]
    public async Task Se_lee_el_estado_del_equipo()
    {
        await using var demonio = new RigctldDeMentira();
        await using var control = new ControlRigctld(Opciones(demonio.Puerto));

        await control.ConectarAsync();

        control.Via.Should().Be(ViaDeControl.Rigctld);
        control.Estado.Conectado.Should().BeTrue();
        control.Estado.Frecuencia.Should().Be(Frecuencia.DesdeHercios(14_074_000));
        control.Estado.Modo.NombreUsual.Should().Be("USB");
        control.Estado.Vfo.Should().Be("VFOA");
    }

    [Fact]
    public async Task Se_pone_la_frecuencia_y_se_vuelve_a_leer_del_equipo()
    {
        await using var demonio = new RigctldDeMentira();
        await using var control = new ControlRigctld(Opciones(demonio.Puerto));
        await control.ConectarAsync();

        await control.PonerFrecuenciaAsync(Frecuencia.DesdeMegahercios(7.074m));

        demonio.Hercios.Should().Be(7_074_000);
        control.Estado.Frecuencia.Should().Be(Frecuencia.DesdeMegahercios(7.074m));
    }

    [Fact]
    public async Task El_dial_fisico_manda_y_el_sondeo_se_entera()
    {
        await using var demonio = new RigctldDeMentira();
        await using var control = new ControlRigctld(Opciones(demonio.Puerto));
        await control.ConectarAsync();

        var senales = new EsperaDeSenales(control);

        // El operador mueve el dial a mano, sin pasar por el programa.
        demonio.Hercios = 21_074_000;

        var estado = await senales.EstadoAsync(
            e => e.Frecuencia == Frecuencia.DesdeHercios(21_074_000),
            control.Estado);
        estado.Frecuencia.Should().Be(Frecuencia.DesdeHercios(21_074_000));
    }

    [Fact]
    public async Task Una_frecuencia_fuera_de_banda_no_rompe_el_sondeo()
    {
        await using var demonio = new RigctldDeMentira();
        await using var control = new ControlRigctld(Opciones(demonio.Puerto));
        await control.ConectarAsync();

        var senales = new EsperaDeSenales(control);
        demonio.Hercios = 27_555_000;

        var estado = await senales.EstadoAsync(
            e => e.Frecuencia == Frecuencia.DesdeHercios(27_555_000),
            control.Estado);
        estado.Banda.EsVacia.Should().BeTrue();
        estado.Conectado.Should().BeTrue();
    }

    [Fact]
    public async Task Si_el_equipo_no_sabe_decir_si_esta_en_antena_no_se_rompe_nada()
    {
        await using var demonio = new RigctldDeMentira();
        demonio.NoSabeDelPtt();
        await using var control = new ControlRigctld(Opciones(demonio.Puerto));

        await control.ConectarAsync();

        // El equipo ficticio de Hamlib contesta «RPRT -11» a get_ptt: se deja de preguntar y se
        // usa lo que haya pedido el vigilante, pero conectarse tiene que funcionar igual.
        control.Estado.Conectado.Should().BeTrue();
        control.Estado.Transmitiendo.Should().BeFalse();
    }

    [Fact]
    public async Task El_ptt_no_se_puede_subir_sin_el_vigilante()
    {
        await using var demonio = new RigctldDeMentira();
        await using var control = new ControlRigctld(Opciones(demonio.Puerto));
        await control.ConectarAsync();

        var subir = () => ((IControlEquipo)control).PonerPttAsync(true);

        await subir.Should().ThrowAsync<InvalidOperationException>();
        demonio.EnAntena.Should().BeFalse();
    }

    [Fact]
    public async Task Con_el_vigilante_se_sube_y_se_baja_el_ptt()
    {
        await using var demonio = new RigctldDeMentira();
        await using var control = new ControlRigctld(Opciones(demonio.Puerto));
        await control.ConectarAsync();
        await using var vigilante = new VigilantePtt(control, new OpcionesDelVigilante
        {
            TiempoMaximo = TimeSpan.FromSeconds(5),
            TiempoSinLatido = TimeSpan.FromSeconds(5),
            EngancharseAlCierreDelProceso = false,
        });

        await using (await vigilante.PedirAntenaAsync("prueba"))
        {
            demonio.EnAntena.Should().BeTrue();
        }

        demonio.EnAntena.Should().BeFalse();
    }

    [Fact]
    public async Task Al_desconectar_se_baja_el_ptt_antes_de_cerrar()
    {
        await using var demonio = new RigctldDeMentira();
        var control = new ControlRigctld(Opciones(demonio.Puerto));
        await control.ConectarAsync();
        var vigilante = new VigilantePtt(control, new OpcionesDelVigilante
        {
            EngancharseAlCierreDelProceso = false,
        });

        var transmision = await vigilante.PedirAntenaAsync("prueba");
        demonio.EnAntena.Should().BeTrue();

        await control.DesconectarAsync();

        demonio.EnAntena.Should().BeFalse();
        demonio.Recibidas.Should().Contain(orden => orden.Contains("set_ptt 0", StringComparison.Ordinal));

        await transmision.DisposeAsync();
        await vigilante.DisposeAsync();
        await control.DisposeAsync();
    }

    [Fact]
    public async Task Si_se_cae_el_demonio_el_control_se_da_cuenta()
    {
        var demonio = new RigctldDeMentira();
        await using var control = new ControlRigctld(Opciones(demonio.Puerto));
        await control.ConectarAsync();
        control.Estado.Conectado.Should().BeTrue();

        var senales = new EsperaDeSenales(control);

        await demonio.DisposeAsync();

        var estado = await senales.EstadoAsync(e => !e.Conectado, control.Estado);
        estado.Conectado.Should().BeFalse();
    }

    [Fact]
    public async Task Hay_una_via_de_emergencia_por_socket_nuevo()
    {
        await using var demonio = new RigctldDeMentira();
        await using var control = new ControlRigctld(Opciones(demonio.Puerto));
        await control.ConectarAsync();

        control.ViasDeSuelta.Should().NotBeEmpty();
        var porSocketNuevo = control.ViasDeSuelta[0];
        porSocketNuevo.Nombre.Should().Contain("socket nuevo");
        porSocketNuevo.SoltarSincrono.Should().NotBeNull("en el cierre del proceso no hay tiempo de esperar tareas");

        // Se sube el PTT por dentro para comprobar que la vía de emergencia lo baja de verdad.
        await using (var vigilante = new VigilantePtt(control, new OpcionesDelVigilante
        {
            EngancharseAlCierreDelProceso = false,
        }))
        {
            var transmision = await vigilante.PedirAntenaAsync("prueba");
            demonio.EnAntena.Should().BeTrue();

            await porSocketNuevo.SoltarAsync(CancellationToken.None);

            demonio.EnAntena.Should().BeFalse();
            await transmision.DisposeAsync();
        }
    }

    [Fact]
    public void El_lanzador_encuentra_el_rigctld_de_hamlib_si_esta_instalado()
    {
        var ruta = LanzadorDeRigctld.BuscarRigctld();

        // En la máquina de Jose está el de Log4OM; en otra puede no estar, y entonces el
        // operador tiene que indicar la ruta a mano. Las dos cosas son correctas.
        if (ruta is not null)
        {
            File.Exists(ruta).Should().BeTrue();
            Path.GetFileName(ruta).Should().Be("rigctld.exe");
        }
    }
}
