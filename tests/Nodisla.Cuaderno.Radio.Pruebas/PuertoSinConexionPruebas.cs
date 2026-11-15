using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Radio.Control;
using Nodisla.Cuaderno.Radio.Control.Ft710;
using Nodisla.Cuaderno.Radio.Pruebas.Dobles;

namespace Nodisla.Cuaderno.Radio.Pruebas;

/// <summary>
/// El puerto del equipo solo esta abierto mientras hay conexion: ni una conexion fallida ni una
/// consulta suelta lo dejan cogido.
/// </summary>
/// <remarks>
/// El fallo (15-11-2026): con el puerto equivocado guardado, «Conectar» abria el puerto, el
/// equipo no contestaba a <c>ID;</c> y el control lanzaba el fallo <b>dejando el puerto
/// abierto</b>. En el registro se ve: tras el primer «Abierto el canal CAT por COM5 a 38400»,
/// los dos intentos siguientes ya no lo abren, porque seguia abierto. Con las lineas del puerto
/// levantadas, eso era la radio en antena hasta cerrar el programa, y el boton de panico no
/// podia hacer nada: mandaba <c>TX0;</c> por un puerto que no era el del CAT.
/// </remarks>
public class PuertoSinConexionPruebas
{
    private static OpcionesFt710 Opciones() => new()
    {
        IntervaloDeSondeo = TimeSpan.FromHours(1),
        EsperaDeOrden = TimeSpan.FromMilliseconds(200),
        ComprobarElCodecDeAudio = false,
    };

    [Fact]
    public async Task Una_conexion_que_no_contesta_no_deja_el_puerto_abierto()
    {
        var canal = new CanalCatDeCaptura();
        canal.Responder("ID;", null);
        await using var control = new ControlFt710(canal, Opciones());

        var conectar = () => control.ConectarAsync();

        await conectar.Should().ThrowAsync<EquipoNoContestaException>();
        canal.Abierto.Should().BeFalse("un puerto que no es el del equipo no puede quedarse abierto");
        control.Estado.Conectado.Should().BeFalse();
    }

    [Fact]
    public async Task Una_conexion_con_algo_que_no_es_un_Yaesu_tampoco_deja_el_puerto_abierto()
    {
        var canal = new CanalCatDeCaptura();
        canal.Responder("ID;", "registro de otro aparato;");
        await using var control = new ControlFt710(canal, Opciones());

        var conectar = () => control.ConectarAsync();

        await conectar.Should().ThrowAsync<InvalidOperationException>();
        canal.Abierto.Should().BeFalse();
    }

    [Fact]
    public async Task Tras_fallar_se_puede_volver_a_intentar_y_conecta()
    {
        var canal = new CanalCatDeCaptura();
        canal.Responder("ID;", null);
        await using var control = new ControlFt710(canal, Opciones());
        await ((Func<Task>)(() => control.ConectarAsync())).Should().ThrowAsync<EquipoNoContestaException>();

        canal.Responder("ID;", "ID0800;");
        await control.ConectarAsync();

        control.Estado.Conectado.Should().BeTrue();
        canal.Abierto.Should().BeTrue();
        canal.Aperturas.Should().Be(2);
    }

    [Fact]
    public async Task Un_control_sin_conectar_no_abre_el_puerto_por_una_consulta()
    {
        // Lo que pasaba al aplicar los ajustes: la pantalla preguntaba algo al control recien
        // puesto y el puerto se abria sin que nadie hubiera pulsado Conectar.
        var canal = new CanalCatDeCaptura();
        await using var control = new ControlFt710(canal, Opciones());

        var lectura = await control.LeerDelMenuAsync("030105");

        lectura.Should().BeNull();
        canal.Aperturas.Should().Be(0);
        canal.Abierto.Should().BeFalse();
    }

    [Fact]
    public async Task Tras_desconectar_una_consulta_no_vuelve_a_abrir_el_puerto()
    {
        var canal = new CanalCatDeCaptura();
        await using var control = new ControlFt710(canal, Opciones());
        await control.ConectarAsync();
        await control.DesconectarAsync();

        var lectura = await control.LeerDelMenuAsync("030105");

        lectura.Should().BeNull();
        canal.Aperturas.Should().Be(1);
        canal.Abierto.Should().BeFalse();
    }
}
