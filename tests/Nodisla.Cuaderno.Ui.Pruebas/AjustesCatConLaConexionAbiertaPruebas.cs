using System.IO;
using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Radio.Ptt;
using Nodisla.Cuaderno.Ui.Ajustes;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>Control de mentira que lleva la cuenta de si esta conectado.</summary>
internal sealed class ControlQueSeConecta(ViaDeControl via) : IControlEquipo
{
    /// <inheritdoc />
    public ViaDeControl Via { get; } = via;

    /// <inheritdoc />
    public EstadoDelEquipo Estado { get; private set; } = EstadoDelEquipo.Desconectado;

    /// <summary>Veces que se ha desconectado.</summary>
    internal int Desconexiones { get; private set; }

    /// <inheritdoc />
    public event EventHandler<EstadoDelEquipo>? EstadoCambiado;

    /// <inheritdoc />
    public Task ConectarAsync(CancellationToken ct = default)
    {
        Estado = EstadoDelEquipo.Desconectado with
        {
            Conectado = true,
            Frecuencia = Frecuencia.DesdeMegahercios(27.555m),
            Modo = Modo.Parse("USB"),
        };
        EstadoCambiado?.Invoke(this, Estado);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DesconectarAsync(CancellationToken ct = default)
    {
        Desconexiones++;
        Estado = EstadoDelEquipo.Desconectado;
        EstadoCambiado?.Invoke(this, Estado);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task PonerFrecuenciaAsync(Frecuencia frecuencia, CancellationToken ct = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task PonerModoAsync(Modo modo, CancellationToken ct = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task PonerPttAsync(bool transmitir, CancellationToken ct = default) => Task.CompletedTask;

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// Probar y aplicar los ajustes del equipo <b>con el programa conectado</b>.
/// </summary>
/// <remarks>
/// <para>
/// El fallo (15-11-2026): con el CAT funcionando por COM4 a 38400, se pulso «Probar» con la
/// deteccion puesta. La prueba monta un control aparte, y un puerto serie no se puede abrir dos
/// veces: COM4 estaba cogido por la propia conexion, la busqueda lo dio por perdido, acabo en
/// COM5 (el puerto <i>Standard</i>) a 4800, lo llevo a la pantalla y el «Aplicar» siguiente lo
/// guardo. Mas tarde, con la deteccion quitada, la misma prueba dio «Access to the path 'COM4'
/// is denied».
/// </para>
/// <para>
/// Aqui el montador de mentira hace de puerto ocupado: si se le llama con el control del
/// programa todavia conectado, devuelve lo que devolvio aquel dia.
/// </para>
/// </remarks>
public sealed class AjustesCatConLaConexionAbiertaPruebas : IDisposable
{
    private readonly string _carpeta = Path.Combine(
        Path.GetTempPath(),
        "cuaderno-cat-abierta-" + Guid.NewGuid().ToString("N"));

    private readonly ConmutableDePapel _conmutable = new();
    private readonly ControlQueSeConecta _elDelPrograma = new(ViaDeControl.CatNativo);
    private int _montajes;

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

    private static AjustesDelPrograma Guardado(bool detectar) => new()
    {
        Equipo = new AjustesDeEquipo
        {
            Via = ViaDeControl.CatNativo,
            DetectarElPuerto = detectar,
            Modelo = "yaesu-ft710",
            Puerto = "COM4",
            Baudios = 38400,
            PttPor = ViaDePtt.Cat,
        },
    };

    /// <summary>
    /// El montador de aquel dia: con COM4 cogido por la conexion del programa, la busqueda
    /// «encuentra» el equipo en COM5 a 4800; con COM4 libre, donde esta de verdad.
    /// </summary>
    private Task<MontajeDeEquipo> Montador(AjustesDeEquipo equipo, IProgress<string> aviso, CancellationToken ct)
    {
        _montajes++;
        var ocupado = _elDelPrograma.Estado.Conectado;
        var (puerto, baudios) = ocupado ? ("COM5", 4800) : ("COM4", 38400);
        return Task.FromResult(new MontajeDeEquipo(new ControlQueSeConecta(ViaDeControl.CatNativo), $" en {puerto} a {baudios} baudios")
        {
            Puerto = puerto,
            Baudios = baudios,
        });
    }

    private async Task<VistaModeloAjustesCat> MontarConectadoAsync(AjustesDelPrograma ajustes)
    {
        await _conmutable.SustituirAsync(_elDelPrograma);
        await _elDelPrograma.ConectarAsync();
        return new VistaModeloAjustesCat(
            ajustes,
            _carpeta,
            _conmutable,
            new VigilanteDePapel(),
            registro: null,
            Montador,
            TimeSpan.FromSeconds(30));
    }

    /// <summary>Deja elegido COM4 en el desplegable, lo tenga o no la maquina donde corre la prueba.</summary>
    private static void ElegirCom4(VistaModeloAjustesCat pantalla)
    {
        var com4 = pantalla.Puertos.FirstOrDefault(p => p.Nombre == "COM4");
        if (com4 is null)
        {
            com4 = new Radio.PuertoSerieDeLaMaquina("COM4", null);
            pantalla.Puertos.Add(com4);
        }

        pantalla.Puerto = com4;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Probar_con_el_programa_conectado_cuenta_la_conexion_abierta_y_no_abre_nada(bool detectar)
    {
        var ajustes = Guardado(detectar);
        var pantalla = await MontarConectadoAsync(ajustes);
        ElegirCom4(pantalla);

        await pantalla.ProbarCommand.ExecuteAsync(null);

        _montajes.Should().Be(0, "el puerto lo tiene cogido la propia conexión: montar otro control no puede abrirlo");
        pantalla.Resultado.Should().Be(ResultadoDePrueba.Correcto);
        pantalla.Parte.Should().Contain("COM4").And.Contain("38400").And.Contain("27.555");
        _elDelPrograma.Estado.Conectado.Should().BeTrue("probar no tira la conexión del operador");
        _elDelPrograma.Desconexiones.Should().Be(0);
        ajustes.Equipo.Puerto.Should().Be("COM4");
        ajustes.Equipo.Baudios.Should().Be(38400);
    }

    [Fact]
    public async Task Aplicar_con_deteccion_suelta_antes_el_puerto_y_guarda_donde_esta_de_verdad()
    {
        var ajustes = Guardado(detectar: true);
        var pantalla = await MontarConectadoAsync(ajustes);

        await pantalla.AplicarCommand.ExecuteAsync(null);

        _elDelPrograma.Desconexiones.Should().BeGreaterThan(0, "para buscar hay que soltar antes el puerto");
        _montajes.Should().Be(1);
        ajustes.Equipo.Puerto.Should().Be("COM4");
        ajustes.Equipo.Baudios.Should().Be(38400);
        AjustesDelPrograma.Leer(_carpeta).Equipo.Puerto.Should().Be("COM4");
        pantalla.Parte.Should().Contain("COM4").And.NotContain("COM5");
    }

    [Fact]
    public async Task Probar_otro_puerto_distinto_del_conectado_si_monta_un_control_aparte()
    {
        var ajustes = Guardado(detectar: false);
        var pantalla = await MontarConectadoAsync(ajustes);
        pantalla.Puertos.Add(new Radio.PuertoSerieDeLaMaquina("COM_DE_MENTIRA", null));
        pantalla.Puerto = pantalla.Puertos.First(p => p.Nombre == "COM_DE_MENTIRA");

        await pantalla.ProbarCommand.ExecuteAsync(null);

        _montajes.Should().Be(1);
        _elDelPrograma.Desconexiones.Should().Be(0);
    }
}
