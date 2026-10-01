using System.Diagnostics;
using System.IO;
using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Ui.Ajustes;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>Control de equipo de mentira, que no toca ningun puerto.</summary>
internal sealed class ControlDePapel(ViaDeControl via) : IControlEquipo
{
    /// <inheritdoc />
    public ViaDeControl Via { get; } = via;

    /// <inheritdoc />
    public EstadoDelEquipo Estado { get; private set; } = EstadoDelEquipo.Desconectado;

    /// <inheritdoc />
    public event EventHandler<EstadoDelEquipo>? EstadoCambiado;

    /// <inheritdoc />
    public Task ConectarAsync(CancellationToken ct = default)
    {
        Estado = EstadoDelEquipo.Desconectado with
        {
            Conectado = true,
            Frecuencia = Frecuencia.DesdeMegahercios(14.074m),
            Modo = Modo.Parse("USB"),
        };
        EstadoCambiado?.Invoke(this, Estado);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DesconectarAsync(CancellationToken ct = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task PonerFrecuenciaAsync(Frecuencia frecuencia, CancellationToken ct = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task PonerModoAsync(Modo modo, CancellationToken ct = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task PonerPttAsync(bool transmitir, CancellationToken ct = default) => Task.CompletedTask;

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Intermediario de mentira: apunta con que control se le ha llamado.</summary>
internal sealed class ConmutableDePapel : IControlEquipoConmutable
{
    /// <inheritdoc />
    public IControlEquipo Actual { get; private set; } = new ControlDePapel(ViaDeControl.Ninguna);

    /// <inheritdoc />
    public ViaDeControl Via => Actual.Via;

    /// <inheritdoc />
    public EstadoDelEquipo Estado => Actual.Estado;

    /// <inheritdoc />
    public event EventHandler<EstadoDelEquipo>? EstadoCambiado;

    /// <inheritdoc />
    public event EventHandler<IControlEquipo>? ControlCambiado;

    /// <inheritdoc />
    public Task SustituirAsync(IControlEquipo nuevo, CancellationToken ct = default)
    {
        Actual = nuevo;
        ControlCambiado?.Invoke(this, nuevo);
        EstadoCambiado?.Invoke(this, nuevo.Estado);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ConectarAsync(CancellationToken ct = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task DesconectarAsync(CancellationToken ct = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task PonerFrecuenciaAsync(Frecuencia frecuencia, CancellationToken ct = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task PonerModoAsync(Modo modo, CancellationToken ct = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task PonerPttAsync(bool transmitir, CancellationToken ct = default) => Task.CompletedTask;

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Vigilante de mentira: nunca hay nada en antena.</summary>
internal sealed class VigilanteDePapel : IVigilantePtt
{
    /// <inheritdoc />
    public TimeSpan TiempoMaximo => TimeSpan.FromMinutes(3);

    /// <inheritdoc />
    public TimeSpan TiempoSinLatido => TimeSpan.FromSeconds(15);

    /// <inheritdoc />
    public bool EnAntena => false;

    /// <inheritdoc />
    public event EventHandler<MotivoDeSuelta>? PttSoltado;

    /// <inheritdoc />
    public Task<ITransmisionEnCurso> PedirAntenaAsync(string motivo, CancellationToken ct = default) =>
        throw new NotSupportedException("En estas pruebas no se transmite.");

    /// <inheritdoc />
    public Task SoltarYaAsync(MotivoDeSuelta motivo = MotivoDeSuelta.Panico)
    {
        PttSoltado?.Invoke(this, motivo);
        return Task.CompletedTask;
    }
}

/// <summary>
/// El apartado CAT de los ajustes.
/// </summary>
/// <remarks>
/// Estas pruebas nacen de un fallo concreto: el operador pulsaba <b>Aplicar</b>, la busqueda del
/// equipo se quedaba colgada en un puerto serie que no contestaba, y la pantalla se quedaba
/// <b>muda y con los botones grises para siempre</b>. Ninguna de estas pruebas abre un puerto:
/// el montaje del control se cambia por uno de mentira.
/// </remarks>
public sealed class VistaModeloAjustesCatPruebas : IDisposable
{
    private readonly string _carpeta = Path.Combine(
        Path.GetTempPath(),
        "cuaderno-cat-" + Guid.NewGuid().ToString("N"));

    private readonly ConmutableDePapel _conmutable = new();

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_carpeta)) Directory.Delete(_carpeta, recursive: true);
        }
        catch (IOException)
        {
            // Una carpeta temporal que no se deja borrar no invalida la prueba.
        }
    }

    private VistaModeloAjustesCat Montar(
        MontadorDeEquipo montador,
        AjustesDelPrograma? ajustes = null,
        TimeSpan? tope = null) =>
        new(ajustes ?? new AjustesDelPrograma(),
            _carpeta,
            _conmutable,
            new VigilanteDePapel(),
            registro: null,
            montador,
            tope ?? TimeSpan.FromSeconds(30));

    [Fact]
    public async Task Se_elige_fabricante_y_modelo_y_se_guarda_su_clave()
    {
        var ajustes = new AjustesDelPrograma();
        AjustesDeEquipo? montado = null;
        var pantalla = Montar(
            (equipo, _, _) =>
            {
                montado = equipo;
                return Task.FromResult(new MontajeDeEquipo(new ControlDePapel(ViaDeControl.CatNativo), string.Empty));
            },
            ajustes);

        pantalla.Fabricante!.Fabricante.Should().BeNull("de partida se busca el equipo solo");
        pantalla.SeElijeModelo.Should().BeFalse();

        pantalla.Via = pantalla.Vias.First(v => v.Via == ViaDeControl.CatNativo);
        pantalla.Fabricante = pantalla.Fabricantes.First(f => f.Fabricante == Radio.Modelos.Fabricante.Yaesu);
        pantalla.ModelosDelFabricante.Should().Contain(m => m.Modelo.Clave == "yaesu-ftdx10");
        pantalla.Modelo = pantalla.ModelosDelFabricante.First(m => m.Modelo.Clave == "yaesu-ftdx10");
        pantalla.AvisoDelModelo.Should().Contain("sin probar con la radio");

        await pantalla.AplicarCommand.ExecuteAsync(null);

        montado!.Modelo.Should().Be("yaesu-ftdx10");
        ajustes.Equipo.Modelo.Should().Be("yaesu-ftdx10");
        ajustes.Equipo.AOpcionesDeRadio().Modelo.Should().Be("yaesu-ftdx10");
    }

    [Fact]
    public void Al_abrir_la_pantalla_se_puede_probar_y_aplicar()
    {
        var pantalla = Montar((_, _, _) =>
            Task.FromResult(new MontajeDeEquipo(new ControlDePapel(ViaDeControl.CatNativo), string.Empty)));

        pantalla.ProbarCommand.CanExecute(null).Should().BeTrue();
        pantalla.AplicarCommand.CanExecute(null).Should().BeTrue();
        pantalla.Ocupado.Should().BeFalse();
    }

    [Fact]
    public async Task Una_busqueda_que_no_termina_se_corta_y_los_botones_vuelven()
    {
        // Esto es exactamente lo que pasaba con el puerto del FT-710 cuando la radio no
        // contestaba: la busqueda no terminaba nunca.
        var pantalla = Montar(
            async (_, _, ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                throw new UnreachableException();
            },
            tope: TimeSpan.FromMilliseconds(150));

        pantalla.Via = pantalla.Vias.First(v => v.Via == ViaDeControl.CatNativo);

        await pantalla.AplicarCommand.ExecuteAsync(null);

        pantalla.Ocupado.Should().BeFalse("la orden no puede quedarse muerta");
        pantalla.AplicarCommand.CanExecute(null).Should().BeTrue();
        pantalla.ProbarCommand.CanExecute(null).Should().BeTrue();
        pantalla.Resultado.Should().Be(ResultadoDePrueba.Fallido);
        pantalla.Parte.Should().Contain("agotado el tiempo");
    }

    [Fact]
    public async Task Aplicar_guarda_los_ajustes_aunque_no_aparezca_el_equipo()
    {
        // Antes se guardaba DESPUES de buscar, asi que una busqueda que no terminaba dejaba el
        // fichero con la via en «Ninguna» y al operador sin rastro de lo que habia elegido.
        var ajustes = new AjustesDelPrograma();
        var pantalla = Montar(
            (_, _, _) => Task.FromResult(new MontajeDeEquipo(new ControlDePapel(ViaDeControl.Ninguna), string.Empty)),
            ajustes);

        pantalla.Via = pantalla.Vias.First(v => v.Via == ViaDeControl.CatNativo);
        pantalla.Velocidad = pantalla.Velocidades.First(v => v.Baudios == 38400);

        await pantalla.AplicarCommand.ExecuteAsync(null);

        var guardados = AjustesDelPrograma.Leer(_carpeta);
        guardados.Equipo.Via.Should().Be(ViaDeControl.CatNativo);
        guardados.Equipo.Baudios.Should().Be(38400);
        pantalla.Resultado.Should().Be(ResultadoDePrueba.Fallido);
        pantalla.Parte.Should().Contain("No se ha encontrado");
    }

    [Fact]
    public async Task Aplicar_pone_el_control_nuevo_y_lo_cuenta()
    {
        var control = new ControlDePapel(ViaDeControl.CatNativo);
        var pantalla = Montar((_, _, _) => Task.FromResult(
            new MontajeDeEquipo(control, " en COM15 a 38400 baudios") { Puerto = "COM15", Baudios = 38400 }));

        pantalla.Via = pantalla.Vias.First(v => v.Via == ViaDeControl.CatNativo);

        await pantalla.AplicarCommand.ExecuteAsync(null);

        _conmutable.Actual.Should().BeSameAs(control);
        pantalla.Resultado.Should().Be(ResultadoDePrueba.Correcto);
        pantalla.Parte.Should().Contain("COM15 a 38400");

        // Y lo encontrado se guarda, para acertar a la primera el proximo arranque.
        var guardados = AjustesDelPrograma.Leer(_carpeta);
        guardados.Equipo.Puerto.Should().Be("COM15");
        guardados.Equipo.Baudios.Should().Be(38400);
    }

    [Fact]
    public async Task Probar_no_toca_el_control_que_esta_usando_el_programa()
    {
        var antes = _conmutable.Actual;
        var pantalla = Montar((_, _, _) =>
            Task.FromResult(new MontajeDeEquipo(new ControlDePapel(ViaDeControl.CatNativo), " en COM15 a 38400 baudios")));

        pantalla.Via = pantalla.Vias.First(v => v.Via == ViaDeControl.CatNativo);

        await pantalla.ProbarCommand.ExecuteAsync(null);

        _conmutable.Actual.Should().BeSameAs(antes, "probar no cambia el equipo del programa");
        pantalla.Resultado.Should().Be(ResultadoDePrueba.Correcto);
        pantalla.Parte.Should().Contain("14.074").And.Contain("USB");
    }

    [Fact]
    public async Task La_pantalla_va_contando_lo_que_hace_mientras_busca()
    {
        // Lo que no puede volver a pasar: pulsar y que no se vea nada.
        var partes = new List<string>();
        var pantalla = Montar(async (_, aviso, _) =>
        {
            aviso.Report("Buscando el equipo por los puertos serie…");
            await Task.Yield();
            return new MontajeDeEquipo(new ControlDePapel(ViaDeControl.CatNativo), string.Empty);
        });

        pantalla.Via = pantalla.Vias.First(v => v.Via == ViaDeControl.CatNativo);
        pantalla.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(VistaModeloAjustesCat.Parte)) partes.Add(pantalla.Parte);
        };

        await pantalla.AplicarCommand.ExecuteAsync(null);

        partes.Should().NotBeEmpty();
        partes[0].Should().Be("Aplicando…", "el parte sale desde el primer instante");
        partes.Should().Contain(parte => parte.Contains("Buscando"));
    }

    [Fact]
    public async Task Sin_equipo_no_hay_nada_que_probar_y_se_dice()
    {
        var pantalla = Montar((_, _, _) =>
            throw new UnreachableException("Con la vía en Ninguna no hay que montar nada."));

        pantalla.Via = pantalla.Vias.First(v => v.Via == ViaDeControl.Ninguna);

        await pantalla.ProbarCommand.ExecuteAsync(null);

        pantalla.Resultado.Should().Be(ResultadoDePrueba.Correcto);
        pantalla.Parte.Should().Contain("no hay nada que probar");
    }

    [Fact]
    public async Task Un_fallo_montando_el_equipo_se_cuenta_y_no_deja_la_pantalla_ocupada()
    {
        var pantalla = Montar((_, _, _) => throw new IOException("el puerto está cogido"));

        pantalla.Via = pantalla.Vias.First(v => v.Via == ViaDeControl.CatNativo);

        await pantalla.ProbarCommand.ExecuteAsync(null);

        pantalla.Ocupado.Should().BeFalse();
        pantalla.Resultado.Should().Be(ResultadoDePrueba.Fallido);
        pantalla.Parte.Should().Contain("el puerto está cogido");
    }
}
