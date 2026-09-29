using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Pruebas.Dobles;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Aplicacion.Pruebas;

public sealed class GestionarRondaPruebas
{
    private readonly RepositorioRondasDoble _rondas = new();
    private readonly RepositorioQsoDoble _cuaderno = new();
    private readonly RepositorioEstacionDoble _estaciones = new();
    private readonly GestionarRonda _caso;

    public GestionarRondaPruebas()
    {
        _estaciones.Sembrar(Ayuda.Estacion());
        _caso = new GestionarRonda(_rondas, new RegistrarQso(_cuaderno, _estaciones));
    }

    private static PeticionDeRonda Peticion(string nombre = "Red de los domingos") => new()
    {
        Nombre = nombre,
        Band = Banda.Parse("40m"),
        Mode = Modo.Parse("SSB"),
        Freq = Frecuencia.DesdeMegahercios(7.110m),
    };

    [Fact]
    public async Task Abrir_crea_la_ronda_y_la_deja_como_abierta()
    {
        var ronda = await _caso.AbrirAsync(Peticion());

        ronda.Id.Should().BeGreaterThan(0);
        ronda.EstaAbierta.Should().BeTrue();
        (await _caso.ObtenerAbiertaAsync())!.Id.Should().Be(ronda.Id);
    }

    [Fact]
    public async Task Abrir_dos_veces_no_crea_una_segunda_ronda()
    {
        var primera = await _caso.AbrirAsync(Peticion("Red uno"));
        var segunda = await _caso.AbrirAsync(Peticion("Red dos"));

        segunda.Id.Should().Be(primera.Id);
        segunda.Nombre.Should().Be("Red uno");
    }

    [Fact]
    public async Task Cerrar_hace_que_ya_no_haya_ronda_abierta()
    {
        var ronda = await _caso.AbrirAsync(Peticion());

        await _caso.CerrarAsync(ronda.Id);

        (await _caso.ObtenerAbiertaAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Anadir_participante_con_indicativo_valido_lo_guarda()
    {
        var ronda = await _caso.AbrirAsync(Peticion());

        var resultado = await _caso.AnadirParticipanteAsync(ronda.Id, "EA1ABC");

        resultado.Correcto.Should().BeTrue();
        resultado.Participante!.Call.Valor.Should().Be("EA1ABC");
        resultado.Participante.RondaId.Should().Be(ronda.Id);
    }

    [Fact]
    public async Task Anadir_participante_con_indicativo_invalido_avisa_y_no_lo_guarda()
    {
        var ronda = await _caso.AbrirAsync(Peticion());

        var resultado = await _caso.AnadirParticipanteAsync(ronda.Id, "***");

        resultado.Correcto.Should().BeFalse();
        resultado.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Marcar_trabajado_crea_un_qso_real_con_los_datos_de_la_ronda()
    {
        var ronda = await _caso.AbrirAsync(Peticion());
        var alta = await _caso.AnadirParticipanteAsync(ronda.Id, "EA1ABC");
        var participante = alta.Participante!;
        participante.RstEnviado = Informe.Parse("59");
        participante.RstRecibido = Informe.Parse("57");

        var resultado = await _caso.MarcarTrabajadoAsync(ronda, participante);

        resultado.Correcto.Should().BeTrue();
        _cuaderno.Contenido.Should().ContainSingle();
        var qso = _cuaderno.Contenido[0];
        qso.Call.Valor.Should().Be("EA1ABC");
        qso.Band.Nombre.Should().Be(ronda.Band.Nombre);
        qso.Mode.Principal.Should().Be(ronda.Mode.Principal);
        qso.RstSent.Texto.Should().Be("59");
        qso.RstRcvd.Texto.Should().Be("57");
        qso.Origen.Should().Be("ronda");

        participante.Trabajado.Should().BeTrue();
        participante.QsoId.Should().Be(qso.Id);
        _rondas.ActualizacionesDeParticipante.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Marcar_trabajado_dos_veces_admite_el_duplicado()
    {
        var ronda = await _caso.AbrirAsync(Peticion());
        var alta = await _caso.AnadirParticipanteAsync(ronda.Id, "EA1ABC");

        var primero = await _caso.MarcarTrabajadoAsync(ronda, alta.Participante!);
        var segundo = await _caso.MarcarTrabajadoAsync(ronda, alta.Participante!);

        primero.Correcto.Should().BeTrue();
        segundo.Correcto.Should().BeTrue();
        _cuaderno.Contenido.Should().HaveCount(2);
    }

    [Fact]
    public async Task Eliminar_participante_lo_quita_de_la_ronda()
    {
        var ronda = await _caso.AbrirAsync(Peticion());
        var alta = await _caso.AnadirParticipanteAsync(ronda.Id, "EA1ABC");

        await _caso.EliminarParticipanteAsync(alta.Participante!.Id);

        var recargada = await _caso.ObtenerAbiertaAsync();
        recargada!.Participantes.Should().BeEmpty();
    }
}
