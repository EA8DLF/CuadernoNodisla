using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Pruebas.Dobles;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Aplicacion.Pruebas;

/// <summary>
/// Completar los contactos con QRZ al guardarlos, venga de donde venga el contacto. Sin reloj de
/// pared: la espera la controla la prueba.
/// </summary>
public sealed class CompletarConQrzPruebas
{
    private readonly RepositorioQsoDoble _cuaderno = new();
    private readonly RepositorioEstacionDoble _estaciones = new();
    private readonly AvisosDeQsos _avisos = new();
    private readonly List<(string Call, TipoDeGuardado Tipo)> _oidos = [];
    private readonly TaskCompletionSource _plazo = new();

    public CompletarConQrzPruebas()
    {
        _estaciones.Sembrar(Ayuda.Estacion());
        _avisos.QsoGuardado += (_, e) => _oidos.Add((e.Qso.Call.Valor, e.Tipo));
    }

    private static FichaIndicativo Ficha(string call = "EA1ABC") => new()
    {
        Indicativo = Indicativo.Parse(call),
        Nombre = "Pepe Pérez",
        Localidad = "León",
        Localizador = Locator.Parse("IN72ao"),
        Pais = "Spain",
        Dxcc = 281,
        ZonaCq = 14,
        ZonaItu = 37,
        DivisionPrimaria = "LE",
        CorreoElectronico = "ea1abc@ejemplo.invalido",
        GestorQsl = "BURO",
        Fuente = "QRZ.com",
    };

    private RegistrarQso Caso(ConsultaDoble consulta) => new(
        _cuaderno,
        _estaciones,
        new CompletadorDeQso(consulta, esperar: (_, _) => _plazo.Task),
        _avisos);

    [Fact]
    public async Task Rellena_lo_vacio_antes_de_guardar_sin_pisar_lo_escrito()
    {
        var caso = Caso(new ConsultaDoble { Ficha = Ficha() });
        var qso = Ayuda.Qso();
        qso.Name = "Pepe (escrito a mano)";

        var resultado = await caso.EjecutarAsync(new PeticionDeRegistro { Qso = qso });

        resultado.Correcto.Should().BeTrue();
        qso.Name.Should().Be("Pepe (escrito a mano)");
        qso.Qth.Should().Be("León");
        qso.Gridsquare.Valor.Should().BeEquivalentTo("IN72ao");
        qso.Dxcc.Should().Be(281);
        qso.Cqz.Should().Be(14);
        qso.Ituz.Should().Be(37);
        qso.State.Should().Be("LE");
        qso.Email.Should().Be("ea1abc@ejemplo.invalido");
        qso.QslVia.Should().Be("BURO");
        _cuaderno.Actualizaciones.Should().Be(0, "llegó a tiempo: se guarda una sola vez");
        _oidos.Should().Equal(("EA1ABC", TipoDeGuardado.Nuevo));
    }

    [Fact]
    public async Task Si_qrz_tarda_guarda_ya_y_completa_el_contacto_despues()
    {
        var lenta = new TaskCompletionSource<FichaIndicativo?>();
        var caso = Caso(new ConsultaDoble { Pendiente = lenta.Task });
        var qso = Ayuda.Qso();

        var registro = caso.EjecutarAsync(new PeticionDeRegistro { Qso = qso });
        _plazo.SetResult(); // se agota la espera
        var resultado = await registro;

        resultado.Correcto.Should().BeTrue();
        _cuaderno.Contenido.Should().ContainSingle();
        qso.Name.Should().BeNull();
        _oidos.Should().BeEmpty("se anuncia cuando termine de completarse");

        lenta.SetResult(Ficha());
        await caso.CompletadoPendiente;

        qso.Name.Should().Be("Pepe Pérez");
        _cuaderno.Actualizaciones.Should().Be(1);
        _oidos.Should().Equal(("EA1ABC", TipoDeGuardado.Completado), ("EA1ABC", TipoDeGuardado.Nuevo));
    }

    [Fact]
    public async Task Sin_red_el_contacto_se_guarda_igual_y_queda_el_motivo_para_la_barra()
    {
        var consulta = new ConsultaDoble { Fallo = new HttpRequestException("sin red") };
        var completador = new CompletadorDeQso(consulta, esperar: (_, _) => _plazo.Task);
        var caso = new RegistrarQso(_cuaderno, _estaciones, completador, _avisos);

        var resultado = await caso.EjecutarAsync(new PeticionDeRegistro { Qso = Ayuda.Qso() });

        resultado.Correcto.Should().BeTrue();
        _cuaderno.Contenido.Should().ContainSingle();
        completador.Problema.Should().Contain("sin red");
        _oidos.Should().ContainSingle();
    }

    [Fact]
    public async Task Sin_credenciales_no_se_consulta_y_se_guarda_igual()
    {
        var consulta = new ConsultaDoble { Disponible = false, Ficha = Ficha() };
        var caso = Caso(consulta);
        var qso = Ayuda.Qso();

        (await caso.EjecutarAsync(new PeticionDeRegistro { Qso = qso })).Correcto.Should().BeTrue();

        consulta.Consultas.Should().Be(0);
        qso.Name.Should().BeNull();
    }

    [Fact]
    public void La_ficha_del_indicativo_base_vale_para_el_portable_pero_no_la_de_otro()
    {
        var qso = Ayuda.Qso(call: "EA8/EA1ABC");
        CompletadorDeQso.Completar(qso, Ficha("EA1ABC")).Should().BeTrue();
        qso.Name.Should().Be("Pepe Pérez");

        var otro = Ayuda.Qso(call: "EA2XYZ");
        CompletadorDeQso.Completar(otro, Ficha("EA1ABC")).Should().BeFalse();
    }

    [Fact]
    public async Task Modificar_con_f2_se_anuncia_como_modificado()
    {
        var registrar = new RegistrarQso(_cuaderno, _estaciones, avisos: _avisos);
        var qso = Ayuda.Qso();
        await registrar.EjecutarAsync(new PeticionDeRegistro { Qso = qso });

        var editar = new EditarQso(_cuaderno, _estaciones, _avisos);
        qso.Comentario = "corregido";
        (await editar.EjecutarAsync(new PeticionDeEdicion { Qso = qso })).Correcto.Should().BeTrue();

        _oidos.Should().Equal(("EA1ABC", TipoDeGuardado.Nuevo), ("EA1ABC", TipoDeGuardado.Modificado));
    }

    [Fact]
    public async Task Un_oyente_que_falla_no_impide_guardar()
    {
        _avisos.QsoGuardado += (_, _) => throw new InvalidOperationException("roto");
        var caso = new RegistrarQso(_cuaderno, _estaciones, avisos: _avisos);

        (await caso.EjecutarAsync(new PeticionDeRegistro { Qso = Ayuda.Qso() })).Correcto.Should().BeTrue();
        _oidos.Should().ContainSingle();
    }

    private sealed class ConsultaDoble : IConsultaIndicativo
    {
        public string Nombre => "QRZ.com";
        public bool Disponible { get; set; } = true;
        public bool EstaDisponible => Disponible;
        public FichaIndicativo? Ficha { get; set; }
        public Task<FichaIndicativo?>? Pendiente { get; set; }
        public Exception? Fallo { get; set; }
        public int Consultas { get; private set; }

        public Task<FichaIndicativo?> ConsultarAsync(Indicativo indicativo, CancellationToken ct = default)
        {
            Consultas++;
            if (Fallo is not null) return Task.FromException<FichaIndicativo?>(Fallo);
            return Pendiente ?? Task.FromResult(Ficha);
        }
    }
}
