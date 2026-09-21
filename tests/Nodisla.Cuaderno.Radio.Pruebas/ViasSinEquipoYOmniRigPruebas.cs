using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Radio.Control;
using Nodisla.Cuaderno.Radio.Control.OmniRig;
using Nodisla.Cuaderno.Radio.Ptt;

namespace Nodisla.Cuaderno.Radio.Pruebas;

/// <summary>OmniRig de mentira, para probar el control sin arrancar nada de COM.</summary>
internal sealed class OmniRigDeMentira : IOmniRigCrudo
{
    private int _aperturas;

    /// <summary>Cuantas veces se ha abierto el objeto.</summary>
    internal int Aperturas => _aperturas;

    /// <summary>Si poner el PTT falla.</summary>
    internal bool FallaAlPonerTx { get; set; }

    /// <inheritdoc />
    public int Estado { get; set; } = EstadoOmniRig.EnLinea;

    /// <inheritdoc />
    public string? TipoDeEquipo { get; set; } = "FT-710.ini";

    /// <inheritdoc />
    public long Frecuencia { get; set; } = 14_074_000;

    /// <inheritdoc />
    public int Modo { get; set; } = ParametrosOmniRig.SsbSuperior;

    private int _tx = ParametrosOmniRig.Rx;

    /// <inheritdoc />
    public int Tx
    {
        get => _tx;
        set
        {
            if (FallaAlPonerTx)
            {
                throw new InvalidOperationException("OmniRig de mentira: el equipo no responde.");
            }

            _tx = value;
        }
    }

    /// <inheritdoc />
    public void Abrir() => Interlocked.Increment(ref _aperturas);

    /// <inheritdoc />
    public void Dispose()
    {
        // Nada que soltar.
    }
}

/// <summary>La via sin equipo, el traductor de modos y la segunda via por OmniRig.</summary>
public class ViasSinEquipoYOmniRigPruebas
{
    [Fact]
    public async Task Sin_equipo_se_puede_operar_apuntando_a_mano()
    {
        await using var control = new ControlNulo();
        await control.ConectarAsync();

        control.Via.Should().Be(ViaDeControl.Ninguna);
        control.ApuntarAMano(Frecuencia.DesdeMegahercios(14.074m), Modo.Parse("FT8"));

        control.Estado.Frecuencia.Should().Be(Frecuencia.DesdeMegahercios(14.074m));
        control.Estado.Modo.NombreUsual.Should().Be("FT8");
        control.Estado.Banda.Nombre.Should().Be("20m");
    }

    [Fact]
    public async Task Sin_equipo_el_vigilante_sigue_mandando()
    {
        await using var control = new ControlNulo();
        await using var vigilante = new VigilantePtt(control, new OpcionesDelVigilante
        {
            EngancharseAlCierreDelProceso = false,
        });

        await using (await vigilante.PedirAntenaAsync("prueba sin equipo"))
        {
            control.Estado.Transmitiendo.Should().BeTrue();
        }

        control.Estado.Transmitiendo.Should().BeFalse();
    }

    [Fact]
    public async Task Sin_equipo_tampoco_se_puede_subir_el_ptt_por_la_puerta_de_atras()
    {
        await using var control = new ControlNulo();

        var subir = () => ((IControlEquipo)control).PonerPttAsync(true);

        await subir.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*IVigilantePtt*");
    }

    [Fact]
    public void La_via_sin_equipo_es_la_de_partida()
    {
        var opciones = new OpcionesDeRadio();

        opciones.Via.Should().Be(ViaDeControl.Ninguna, "el programa no debe tocar ningún puerto serie al arrancar");
        opciones.Via.Should().NotBe(ViaDeControl.CatNativo);
    }

    [Fact]
    public async Task Por_omnirig_se_lee_y_se_escribe_sin_tocar_com()
    {
        var omni = new OmniRigDeMentira();
        await using var control = new ControlOmniRig(omni, new OpcionesOmniRig
        {
            IntervaloDeSondeo = TimeSpan.FromMilliseconds(50),
        });

        await control.ConectarAsync();

        control.Via.Should().Be(ViaDeControl.OmniRig);
        control.Estado.Conectado.Should().BeTrue();
        control.Estado.Frecuencia.Should().Be(Frecuencia.DesdeHercios(14_074_000));
        control.Estado.Modo.NombreUsual.Should().Be("USB");

        await control.PonerFrecuenciaAsync(Frecuencia.DesdeMegahercios(7.074m));
        omni.Frecuencia.Should().Be(7_074_000);
    }

    [Fact]
    public async Task Por_omnirig_el_ptt_tambien_pasa_por_el_vigilante()
    {
        var omni = new OmniRigDeMentira();
        await using var control = new ControlOmniRig(omni, new OpcionesOmniRig
        {
            IntervaloDeSondeo = TimeSpan.FromMilliseconds(50),
        });
        await control.ConectarAsync();
        await using var vigilante = new VigilantePtt(control, new OpcionesDelVigilante
        {
            EngancharseAlCierreDelProceso = false,
        });

        var subir = () => ((IControlEquipo)control).PonerPttAsync(true);
        await subir.Should().ThrowAsync<InvalidOperationException>();
        omni.Tx.Should().Be(ParametrosOmniRig.Rx);

        await using (await vigilante.PedirAntenaAsync("prueba"))
        {
            omni.Tx.Should().Be(ParametrosOmniRig.Tx);
        }

        omni.Tx.Should().Be(ParametrosOmniRig.Rx);
    }

    [Fact]
    public async Task Si_omnirig_falla_al_soltar_se_reabre_el_objeto_y_se_insiste()
    {
        var omni = new OmniRigDeMentira();
        await using var control = new ControlOmniRig(omni, new OpcionesOmniRig
        {
            IntervaloDeSondeo = TimeSpan.FromSeconds(30),
        });
        await control.ConectarAsync();
        var aperturasAlConectar = omni.Aperturas;

        control.ViasDeSuelta.Should().HaveCount(2);
        omni.FallaAlPonerTx = true;

        var primera = () => control.ViasDeSuelta[0].SoltarAsync(CancellationToken.None);
        await primera.Should().ThrowAsync<InvalidOperationException>();

        // La segunda vía vuelve a abrir el objeto COM antes de insistir.
        omni.FallaAlPonerTx = false;
        await control.ViasDeSuelta[1].SoltarAsync(CancellationToken.None);

        omni.Aperturas.Should().BeGreaterThan(aperturasAlConectar);
        omni.Tx.Should().Be(ParametrosOmniRig.Rx);
    }

    [Theory]
    [InlineData("USB", "SSB", "USB")]
    [InlineData("LSB", "SSB", "LSB")]
    [InlineData("CW", "CW", null)]
    [InlineData("PKTUSB", "FT8", null)]
    [InlineData("RTTY", "RTTY", null)]
    [InlineData("FM", "FM", null)]
    public void Los_modos_del_equipo_se_pasan_a_adif(string delEquipo, string principal, string? submodo)
    {
        var modo = TraductorDeModos.PorOmision.DesdeElEquipo(delEquipo);

        modo.Principal.Should().Be(principal);
        modo.Submodo.Should().Be(submodo);
    }

    [Fact]
    public void La_banda_lateral_se_elige_por_la_frecuencia_cuando_no_se_dice()
    {
        var ssb = Modo.Parse("SSB");

        TraductorDeModos.PorOmision.AlEquipo(ssb, Frecuencia.DesdeMegahercios(7.1m)).Should().Be("LSB");
        TraductorDeModos.PorOmision.AlEquipo(ssb, Frecuencia.DesdeMegahercios(14.2m)).Should().Be("USB");
    }

    [Fact]
    public void Los_modos_digitales_salen_por_el_modo_de_datos_del_equipo()
    {
        var traductor = TraductorDeModos.PorOmision;

        traductor.AlEquipo(Modo.Parse("FT8"), Frecuencia.DesdeMegahercios(14.074m)).Should().Be("PKTUSB");
        traductor.AlEquipo(Modo.Parse("MFSK", "FT4"), Frecuencia.DesdeMegahercios(14.08m)).Should().Be("PKTUSB");
        traductor.AlEquipo(Modo.Parse("CW"), Frecuencia.DesdeMegahercios(7.03m)).Should().Be("CW");
    }

    [Fact]
    public void Un_modo_desconocido_del_equipo_no_se_pierde()
    {
        var modo = TraductorDeModos.PorOmision.DesdeElEquipo("MANDANGA");

        modo.EsVacio.Should().BeFalse("perder el modo sería peor que apuntarlo tal cual");
        modo.Principal.Should().Be("MANDANGA");
    }
}
