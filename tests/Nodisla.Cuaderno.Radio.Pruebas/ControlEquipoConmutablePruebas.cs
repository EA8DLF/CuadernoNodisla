using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Radio.Control;
using Nodisla.Cuaderno.Radio.Ptt;

namespace Nodisla.Cuaderno.Radio.Pruebas;

/// <summary>
/// Control de mentira que apunta todo lo que le hacen al cerrarlo.
/// </summary>
/// <remarks>
/// Lo que se prueba aqui no es que hable con una radio —no hay radio—, sino que el
/// intermediario lo cierra <b>en el orden correcto</b>: bajar el PTT, desconectar y soltar.
/// Ese orden es lo unico que separa un cambio de via de dejar un equipo en antena.
/// </remarks>
internal sealed class ControlApuntador : IControlEquipo, IPttDirecto, ISueltaDeEmergenciaPtt, IAvisaDePerdidaDeComunicacion
{
    private readonly List<string> _pasos = [];

    internal ControlApuntador(ViaDeControl via, string nombreDeLaVia)
    {
        Via = via;
        ViasDeSuelta = [new ViaDeSuelta(nombreDeLaVia, _ => Task.CompletedTask, () => { })];
    }

    /// <summary>Lo que le han hecho, en orden.</summary>
    internal IReadOnlyList<string> Pasos => _pasos;

    /// <inheritdoc />
    public ViaDeControl Via { get; }

    /// <inheritdoc />
    public EstadoDelEquipo Estado { get; private set; } = EstadoDelEquipo.Desconectado;

    /// <inheritdoc />
    public IReadOnlyList<ViaDeSuelta> ViasDeSuelta { get; }

    /// <inheritdoc />
    public event EventHandler<EstadoDelEquipo>? EstadoCambiado;

    /// <inheritdoc />
    public event EventHandler<string>? ComunicacionPerdida;

    /// <summary>Hace como que el equipo dice algo nuevo.</summary>
    internal void Anunciar(EstadoDelEquipo estado)
    {
        Estado = estado;
        EstadoCambiado?.Invoke(this, estado);
    }

    /// <summary>Hace como que se ha perdido la comunicacion.</summary>
    internal void Perder(string porque) => ComunicacionPerdida?.Invoke(this, porque);

    /// <inheritdoc />
    public Task ConectarAsync(CancellationToken ct = default)
    {
        _pasos.Add("conectar");
        Anunciar(Estado with { Conectado = true });
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DesconectarAsync(CancellationToken ct = default)
    {
        _pasos.Add("desconectar");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task PonerFrecuenciaAsync(Frecuencia frecuencia, CancellationToken ct = default)
    {
        _pasos.Add("frecuencia");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task PonerModoAsync(Modo modo, CancellationToken ct = default)
    {
        _pasos.Add("modo");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    Task IControlEquipo.PonerPttAsync(bool transmitir, CancellationToken ct) =>
        GuardiaDelPtt.Rechazar(transmitir, () => ((IPttDirecto)this).PonerPttDirectoAsync(false, ct));

    /// <inheritdoc />
    Task IPttDirecto.PonerPttDirectoAsync(bool transmitir, CancellationToken ct)
    {
        _pasos.Add(transmitir ? "ptt=1" : "ptt=0");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _pasos.Add("soltar");
        return ValueTask.CompletedTask;
    }
}

/// <summary>El intermediario que deja cambiar de vía sin cerrar el programa.</summary>
public sealed class ControlEquipoConmutablePruebas
{
    [Fact]
    public void Delega_la_via_y_el_estado_en_el_control_que_hay_detras()
    {
        var dentro = new ControlApuntador(ViaDeControl.Rigctld, "de mentira");
        var conmutable = new ControlEquipoConmutable(dentro);

        conmutable.Via.Should().Be(ViaDeControl.Rigctld);
        conmutable.Actual.Should().BeSameAs(dentro);

        dentro.Anunciar(EstadoDelEquipo.Desconectado with { Conectado = true });
        conmutable.Estado.Conectado.Should().BeTrue();
    }

    [Fact]
    public void Sin_control_de_partida_arranca_sin_equipo()
    {
        var conmutable = new ControlEquipoConmutable();

        conmutable.Via.Should().Be(ViaDeControl.Ninguna);
        conmutable.Actual.Should().BeOfType<ControlNulo>();
    }

    [Fact]
    public async Task Al_sustituir_baja_el_ptt_desconecta_y_suelta_el_anterior_en_ese_orden()
    {
        var viejo = new ControlApuntador(ViaDeControl.Rigctld, "vieja");
        var nuevo = new ControlApuntador(ViaDeControl.CatNativo, "nueva");
        var conmutable = new ControlEquipoConmutable(viejo);

        await conmutable.SustituirAsync(nuevo);

        // El orden importa: soltar el objeto antes de bajar el PTT seria bajarlo sobre un
        // canal ya cerrado, que es lo mismo que no bajarlo.
        viejo.Pasos.Should().Equal("ptt=0", "desconectar", "soltar");
        nuevo.Pasos.Should().BeEmpty("el control nuevo no se conecta solo: eso lo pide el operador");
        conmutable.Actual.Should().BeSameAs(nuevo);
        conmutable.Via.Should().Be(ViaDeControl.CatNativo);
    }

    [Fact]
    public async Task Deja_de_repetir_lo_que_dice_el_control_viejo_y_repite_lo_del_nuevo()
    {
        var viejo = new ControlApuntador(ViaDeControl.Rigctld, "vieja");
        var nuevo = new ControlApuntador(ViaDeControl.CatNativo, "nueva");
        var conmutable = new ControlEquipoConmutable(viejo);

        var recibidos = new List<EstadoDelEquipo>();
        var origenes = new List<object?>();
        conmutable.EstadoCambiado += (origen, estado) =>
        {
            origenes.Add(origen);
            recibidos.Add(estado);
        };

        await conmutable.SustituirAsync(nuevo);

        // Al sustituir se avisa del estado del nuevo, para que la pantalla se entere.
        recibidos.Should().HaveCount(1);
        recibidos.Clear();
        origenes.Clear();

        viejo.Anunciar(EstadoDelEquipo.Desconectado with { Conectado = true });
        recibidos.Should().BeEmpty("el control viejo ya no manda");

        nuevo.Anunciar(EstadoDelEquipo.Desconectado with { Conectado = true, Vfo = "A" });
        recibidos.Should().ContainSingle().Which.Vfo.Should().Be("A");

        // El origen que se reparte es el intermediario, no el control de dentro: quien escucha
        // se engancho a este objeto y no sabe que hay nadie detras.
        origenes.Should().AllBeEquivalentTo(conmutable);
    }

    [Fact]
    public async Task Repite_el_aviso_de_que_se_ha_perdido_el_equipo_del_control_de_ahora()
    {
        var viejo = new ControlApuntador(ViaDeControl.Rigctld, "vieja");
        var nuevo = new ControlApuntador(ViaDeControl.CatNativo, "nueva");
        var conmutable = new ControlEquipoConmutable(viejo);

        var perdidas = new List<string>();
        ((IAvisaDePerdidaDeComunicacion)conmutable).ComunicacionPerdida += (_, porque) => perdidas.Add(porque);

        await conmutable.SustituirAsync(nuevo);

        viejo.Perder("el viejo");
        nuevo.Perder("el nuevo");

        perdidas.Should().Equal("el nuevo");
    }

    [Fact]
    public async Task Las_vias_de_suelta_son_siempre_las_del_control_de_ahora()
    {
        var viejo = new ControlApuntador(ViaDeControl.Rigctld, "vieja");
        var nuevo = new ControlApuntador(ViaDeControl.CatNativo, "nueva");
        var conmutable = new ControlEquipoConmutable(viejo);

        ((ISueltaDeEmergenciaPtt)conmutable).ViasDeSuelta.Should().ContainSingle()
            .Which.Nombre.Should().Be("vieja");

        await conmutable.SustituirAsync(nuevo);

        // Esto es lo que hace que el vigilante del PTT siga valiendo tras cambiar de via: las
        // pregunta en el momento de soltar, no las guarda al arrancar.
        ((ISueltaDeEmergenciaPtt)conmutable).ViasDeSuelta.Should().ContainSingle()
            .Which.Nombre.Should().Be("nueva");
    }

    [Fact]
    public async Task Avisa_de_que_ha_cambiado_el_control()
    {
        var nuevo = new ControlApuntador(ViaDeControl.CatNativo, "nueva");
        var conmutable = new ControlEquipoConmutable(new ControlApuntador(ViaDeControl.Rigctld, "vieja"));

        IControlEquipo? avisado = null;
        conmutable.ControlCambiado += (_, control) => avisado = control;

        await conmutable.SustituirAsync(nuevo);

        avisado.Should().BeSameAs(nuevo);
    }

    [Fact]
    public async Task Subir_el_ptt_por_el_puerto_publico_sigue_rechazandose()
    {
        var dentro = new ControlApuntador(ViaDeControl.CatNativo, "nueva");
        var conmutable = new ControlEquipoConmutable(dentro);

        var subir = async () => await ((IControlEquipo)conmutable).PonerPttAsync(true);
        await subir.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage(GuardiaDelPtt.MensajeDeRechazo);

        // Bajarlo, en cambio, se deja siempre: bajar el PTT nunca hace daño.
        await ((IControlEquipo)conmutable).PonerPttAsync(false);
        dentro.Pasos.Should().Equal("ptt=0");
    }

    [Fact]
    public async Task Sustituir_por_el_mismo_control_no_lo_cierra()
    {
        var dentro = new ControlApuntador(ViaDeControl.CatNativo, "nueva");
        var conmutable = new ControlEquipoConmutable(dentro);

        await conmutable.SustituirAsync(dentro);

        dentro.Pasos.Should().BeEmpty();
        conmutable.Actual.Should().BeSameAs(dentro);
    }

    [Fact]
    public async Task Soltar_el_intermediario_cierra_el_control_que_lleva_dentro()
    {
        var dentro = new ControlApuntador(ViaDeControl.CatNativo, "nueva");
        var conmutable = new ControlEquipoConmutable(dentro);

        await conmutable.DisposeAsync();

        dentro.Pasos.Should().Equal("ptt=0", "desconectar", "soltar");
    }

    [Fact]
    public async Task El_vigilante_suelta_el_ptt_por_el_control_que_hay_puesto()
    {
        var viejo = new ControlApuntador(ViaDeControl.Rigctld, "vieja");
        var nuevo = new ControlApuntador(ViaDeControl.CatNativo, "nueva");
        var conmutable = new ControlEquipoConmutable(viejo);

        await using var vigilante = new VigilantePtt(
            conmutable,
            new OpcionesDelVigilante { EngancharseAlCierreDelProceso = false });

        await conmutable.SustituirAsync(nuevo);

        await using (await vigilante.PedirAntenaAsync("prueba"))
        {
            // Nada: se suelta al salir del bloque.
        }

        // El PTT ha subido y ha bajado por el control NUEVO, que es el que hay puesto. Si el
        // vigilante se hubiera quedado con el viejo, esto habria transmitido por una via que
        // ya no existe, que es la forma tonta de dejar un equipo en antena.
        nuevo.Pasos.Should().Contain("ptt=1").And.Contain("ptt=0");
        viejo.Pasos.Should().NotContain("ptt=1");
    }
}
