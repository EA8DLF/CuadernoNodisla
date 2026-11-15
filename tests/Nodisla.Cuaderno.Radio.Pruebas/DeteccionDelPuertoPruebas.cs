using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Radio.Control.Ft710;
using Nodisla.Cuaderno.Radio.Pruebas.Dobles;

namespace Nodisla.Cuaderno.Radio.Pruebas;

/// <summary>
/// La busqueda del equipo no puede acabar en el puerto <i>Standard</i> ni abrir puertos ajenos.
/// </summary>
/// <remarks>
/// <para>
/// El fallo (15-11-2026, registro de la maquina de EA8DLF): el CAT funcionaba por COM4 a 38400
/// (51 mandos). Se pulso «Probar» con la deteccion puesta y <b>con el programa conectado</b>: el
/// puerto COM4 lo tenia cogido la propia conexion, la comprobacion no pudo abrirlo y lo anoto
/// como «Por COM4 a 38400 ya no contesta el equipo; se buscan todos los puertos». El barrido
/// abrio COM13 (que no es la radio) a las cinco velocidades, se salto COM4 —ocupado— y encontro
/// «Yaesu FT-710 en COM5 a 4800 baudios»: el puerto <i>Standard</i>, el del control de
/// transmision. Y eso fue lo que se guardo.
/// </para>
/// <para>
/// Los puertos de estas pruebas son los de esa maquina, con sus descripciones tal cual, y un
/// sondeador de mentira: aqui no se abre ningun puerto serie.
/// </para>
/// </remarks>
public class DeteccionDelPuertoPruebas
{
    private static readonly PuertoSerieDeLaMaquina Com4 =
        new("COM4", "Silicon Labs Dual CP2105 USB to UART Bridge: Enhanced COM Port");

    private static readonly PuertoSerieDeLaMaquina Com5 =
        new("COM5", "Silicon Labs Dual CP2105 USB to UART Bridge: Standard COM Port");

    private static readonly PuertoSerieDeLaMaquina Com13 = new("COM13", "Dispositivo serie USB");

    /// <summary>Los tres puertos, en el orden en que los daba la busqueda (COM13 antes que COM4).</summary>
    private static readonly IReadOnlyList<PuertoSerieDeLaMaquina> LosDeLaMaquina = [Com13, Com4, Com5];

    /// <summary>La radio de mentira: contesta por COM4 a 38400 y por COM5 a 4800, como la de verdad.</summary>
    private sealed class Radio
    {
        private readonly ConcurrentQueue<(string Puerto, int Baudios)> _abiertos = new();

        /// <summary>El COM4 esta cogido (por la propia conexion del programa, o por otro).</summary>
        internal bool Com4Ocupado { get; init; }

        /// <summary>Todo lo que la busqueda ha intentado abrir.</summary>
        internal IReadOnlyCollection<(string Puerto, int Baudios)> Abiertos => _abiertos;

        internal Task<ResultadoDeSonda> Sondear(string puerto, int baudios, CancellationToken ct)
        {
            _abiertos.Enqueue((puerto, baudios));
            return Task.FromResult((puerto, baudios) switch
            {
                ("COM4", _) when Com4Ocupado => ResultadoDeSonda.NoSeAbre,
                ("COM4", 38400) => new ResultadoDeSonda(true, "0800"),
                ("COM5", 4800) => new ResultadoDeSonda(true, "0800"),
                _ => ResultadoDeSonda.NoContesta,
            });
        }
    }

    private static OpcionesDeRadio Ajustes(string? puerto, int baudios) => new()
    {
        Via = ViaDeControl.CatNativo,
        Ft710 = { Puerto = puerto, Baudios = baudios },
    };

    [Fact]
    public async Task Con_el_Enhanced_ocupado_no_se_da_por_bueno_el_Standard()
    {
        // Tal cual el 15-11-2026: guardado COM4 a 38400, y COM4 cogido por la propia conexion.
        var radio = new Radio { Com4Ocupado = true };
        var registro = new RegistroDeMentira();

        var encontrado = await FabricaDeControlEquipo.BuscarEquipoAsync(
            Ajustes("COM4", 38400), null, registro, LosDeLaMaquina, radio.Sondear, CancellationToken.None);

        encontrado.Should().BeNull("el equipo no se ha movido: es que su puerto no se puede abrir");
        radio.Abiertos.Should().NotContain(abierto => abierto.Puerto == "COM5", "el Standard es el del control de transmisión");
        radio.Abiertos.Should().NotContain(abierto => abierto.Puerto == "COM13", "no es la radio");
        registro.De(LogLevel.Warning).Should().Contain(mensaje => mensaje.Contains("COM4") && mensaje.Contains("no se puede abrir"));
        registro.Apuntes.Should().NotContain(apunte => apunte.Mensaje.Contains("ya no contesta"));
    }

    [Fact]
    public async Task Con_el_Standard_guardado_se_encuentra_el_Enhanced_sin_abrir_el_Standard()
    {
        // Lo que quedo guardado aquel dia: COM5. La busqueda siguiente tiene que salir de ahi.
        var radio = new Radio();

        var encontrado = await FabricaDeControlEquipo.BuscarEquipoAsync(
            Ajustes("COM5", 4800), null, null, LosDeLaMaquina, radio.Sondear, CancellationToken.None);

        encontrado.Should().NotBeNull();
        encontrado!.Puerto.Should().Be("COM4");
        encontrado.Baudios.Should().Be(38400);
        radio.Abiertos.Should().OnlyContain(abierto => abierto.Puerto == "COM4");
    }

    [Fact]
    public async Task Sin_nada_guardado_solo_se_abre_el_Enhanced()
    {
        var radio = new Radio();

        var encontrado = await FabricaDeControlEquipo.BuscarEquipoAsync(
            Ajustes(null, 38400), null, null, LosDeLaMaquina, radio.Sondear, CancellationToken.None);

        encontrado.Should().NotBeNull();
        encontrado!.Puerto.Should().Be("COM4");
        encontrado.Baudios.Should().Be(38400);
        radio.Abiertos.Should().OnlyContain(abierto => abierto.Puerto == "COM4");
    }

    [Fact]
    public async Task Lo_guardado_sigue_siendo_lo_primero_que_se_prueba()
    {
        var radio = new Radio();

        var encontrado = await FabricaDeControlEquipo.BuscarEquipoAsync(
            Ajustes("COM4", 38400), null, null, LosDeLaMaquina, radio.Sondear, CancellationToken.None);

        encontrado.Should().NotBeNull();
        encontrado!.Puerto.Should().Be("COM4");
        radio.Abiertos.Should().Equal(("COM4", 38400));
    }

    [Fact]
    public async Task Un_puerto_que_no_se_abre_no_se_reintenta_a_las_demas_velocidades()
    {
        var radio = new Radio { Com4Ocupado = true };

        var encontrados = await AutodeteccionFt710.BuscarEnAsync(LosDeLaMaquina, radio.Sondear);

        encontrados.Should().BeEmpty();
        radio.Abiertos.Should().HaveCount(1);
    }

    [Fact]
    public void Los_candidatos_de_la_maquina_de_Jose_son_solo_el_Enhanced()
    {
        AutodeteccionFt710.PuertosCandidatos(LosDeLaMaquina).Should().Equal(Com4);
    }

    [Fact]
    public void Sin_ningun_Enhanced_se_miran_todos_menos_el_Standard()
    {
        // Otro cable, u otro equipo: se busca como antes, pero el Standard no se abre nunca.
        AutodeteccionFt710.PuertosCandidatos([Com13, Com5]).Should().Equal(Com13);
    }

    [Fact]
    public void Sin_descripciones_se_miran_todos_como_antes()
    {
        // Si Windows no deja leer las descripciones no hay forma de distinguirlos.
        IReadOnlyList<PuertoSerieDeLaMaquina> aPelo = [new("COM13", null), new("COM4", null), new("COM5", null)];

        AutodeteccionFt710.PuertosCandidatos(aPelo).Should().Equal(aPelo);
    }
}
