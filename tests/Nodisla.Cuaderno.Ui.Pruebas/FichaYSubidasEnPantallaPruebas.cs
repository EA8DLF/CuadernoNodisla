using System.IO;
using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Servicios.Subidas;
using Nodisla.Cuaderno.Ui.Ajustes;
using Nodisla.Cuaderno.Ui.Desarrollo;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// La ficha de QRZ en el formulario de Operar, F2 sin perder datos y las pastillas de subida.
/// Todo con dobles y sin reloj de pared.
/// </summary>
public sealed class FichaYSubidasEnPantallaPruebas
{
    private readonly RepositorioQsoEnMemoria _cuaderno = new([]);
    private readonly RepositorioEstacionEnMemoria _estaciones = new();
    private readonly AvisosDeQsos _avisos = new();

    private sealed class ConsultaFija : IConsultaIndicativo
    {
        public string Nombre => "QRZ.com";
        public bool EstaDisponible => true;

        public Task<FichaIndicativo?> ConsultarAsync(Indicativo indicativo, CancellationToken ct = default) =>
            Task.FromResult<FichaIndicativo?>(new FichaIndicativo
            {
                Indicativo = indicativo,
                Nombre = "Nombre de " + indicativo.Valor,
                Localidad = "QTH de " + indicativo.Valor,
                Localizador = Locator.Parse("JO62qm"),
                UsaLotw = true,
                Fuente = "QRZ.com",
            });
    }

    private VistaModeloEntradaQso Formulario()
    {
        var completador = new CompletadorDeQso(new ConsultaFija(), esperar: (_, _) => Task.CompletedTask);
        return new VistaModeloEntradaQso(
            new RegistrarQso(_cuaderno, _estaciones, completador, _avisos),
            new EditarQso(_cuaderno, _estaciones, _avisos),
            new ConsultarTrabajadoAntes(_cuaderno),
            completador)
        {
            Esperar = (_, _) => Task.CompletedTask,
        };
    }

    [Fact]
    public async Task Al_teclear_el_indicativo_rellena_lo_vacio_y_respeta_lo_escrito()
    {
        var formulario = Formulario();
        formulario.Nombre = "Hans";

        formulario.Indicativo = "DL1ABC";
        await formulario.ConsultaEnCurso;

        formulario.Nombre.Should().Be("Hans");
        formulario.Qth.Should().Be("QTH de DL1ABC");
        formulario.Localizador.Should().BeEquivalentTo("JO62qm");
        formulario.ResumenDeFicha.Should().Contain("usa LoTW");
    }

    [Fact]
    public async Task Si_cambia_el_indicativo_se_quita_lo_que_puso_la_ficha_del_anterior()
    {
        var formulario = Formulario();
        formulario.Indicativo = "DL1ABC";
        await formulario.ConsultaEnCurso;
        formulario.Qth = "Escrito a mano";

        formulario.Indicativo = "F4XYZ";
        await formulario.ConsultaEnCurso;

        formulario.Nombre.Should().Be("Nombre de F4XYZ");
        formulario.Qth.Should().Be("Escrito a mano");
    }

    [Fact]
    public async Task Modificar_con_f2_conserva_lo_que_el_formulario_no_ensena()
    {
        var formulario = Formulario();
        formulario.Indicativo = "DL1ABC";
        await formulario.ConsultaEnCurso;
        await formulario.GuardarCommand.ExecuteAsync(null);

        var guardado = (await _cuaderno.BuscarAsync(new CriterioQso(), 0, 10)).Elementos.Single();
        guardado.Country = "Germany";
        guardado.Confirmaciones.Add(new QsoConfirmacion
        {
            Medio = MedioDeConfirmacion.Lotw,
            Enviado = EstadoDeConfirmacion.Confirmado,
        });

        formulario.CargarParaEditar(guardado);
        await formulario.ConsultaEnCurso;
        formulario.Comentario = "corregido";
        await formulario.GuardarCommand.ExecuteAsync(null);

        var despues = await _cuaderno.ObtenerAsync(guardado.Id);
        despues!.Comentario.Should().Be("corregido");
        despues.Country.Should().Be("Germany");
        despues.Confirmaciones.Should().ContainSingle(c => c.Medio == MedioDeConfirmacion.Lotw);
        formulario.Mensaje.Should().Contain("LoTW no admite modificar");
    }

    [Fact]
    public async Task Las_pastillas_dicen_los_pendientes_y_subir_ahora_los_sube()
    {
        var ajustes = new AjustesDelPrograma();
        var cola = new ColaDeSubidas(
            _cuaderno,
            () => ServicioQslSimulado.Todos,
            medio => CuentasDeServicios.Activado(ajustes.Servicios, medio),
            ruta: null,
            esperar: (_, _) => Task.CompletedTask);
        var completador = new CompletadorDeQso(new ConsultaFija());
        var carpeta = Path.Combine(Path.GetTempPath(), "cuaderno-subidas-" + Guid.NewGuid().ToString("N"));
        var subidas = new VistaModeloSubidas(cola, completador, ajustes, carpeta);

        try
        {
            var qso = new Qso
            {
                Call = Indicativo.Parse("DL1ABC"),
                Band = Banda.Parse("20m"),
                Mode = Modo.Parse("FT8"),
                InicioUtc = new DateTimeOffset(2026, 9, 27, 11, 0, 0, TimeSpan.Zero),
                StationCallsign = Indicativo.Parse("EA8DLF"),
            };
            await _cuaderno.AnadirAsync(qso);
            await cola.EncolarAsync(qso, modificado: false);
            subidas.Refrescar();

            subidas.Pastillas[0].Etiqueta.Should().Be("LoTW 1");
            subidas.Pastillas[0].Semaforo.Should().Be(SemaforoDeSubida.Rojo, "el LoTW simulado no tiene TQSL");
            subidas.Pastillas[1].Semaforo.Should().Be(SemaforoDeSubida.Ambar);
            subidas.Cola.Should().HaveCount(4);

            await subidas.SubirAhoraAsync();

            subidas.Pastillas[1].Semaforo.Should().Be(SemaforoDeSubida.Verde);
            subidas.Pastillas[1].Etiqueta.Should().Be("eQSL");
            subidas.Cola.Should().ContainSingle().Which.Servicio.Should().Be("LoTW");

            // Desmarcar Club Log se guarda en los ajustes.
            subidas.Servicios[2].Activado = false;
            ajustes.Servicios.SubirAClubLog.Should().BeFalse();
            subidas.Pastillas[2].Semaforo.Should().Be(SemaforoDeSubida.Apagado);
        }
        finally
        {
            if (Directory.Exists(carpeta)) Directory.Delete(carpeta, recursive: true);
        }
    }
}
