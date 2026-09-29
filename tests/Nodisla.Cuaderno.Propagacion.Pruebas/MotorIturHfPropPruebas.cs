using System.Globalization;
using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Propagacion;
using Nodisla.Cuaderno.Propagacion.Prediccion;

namespace Nodisla.Cuaderno.Propagacion.Pruebas;

/// <summary>
/// El motor de verdad, lanzando el proceso. Estas pruebas se saltan solas cuando ITURHFProp no
/// esta compilado, para que un clon recien hecho del repositorio no falle por algo que no lo es;
/// como compilarlo lo cuenta <c>herramientas\ITURHFProp\LEEME.md</c>.
/// </summary>
public class MotorIturHfPropPruebas
{
    private static readonly Coordenada Tenerife = new(28.4636, -16.2518);
    private static readonly Coordenada Madrid = new(40.4168, -3.7038);
    private static readonly DateTimeOffset Mediodia = Momento("2026-09-22T13:00:00Z");

    private static readonly IndicesSolares Indices =
        new(104, 3, 1, 52, 400, -2, false, Mediodia);

    [Fact]
    public async Task El_modelo_de_la_uit_predice_canarias_madrid()
    {
        var motor = MotorIturHfProp.Localizar();
        if (motor is null)
        {
            return;
        }

        var prediccion = await motor.PredecirAsync(Solicitud(Madrid), CancellationToken.None);

        prediccion.Should().HaveCount(11);
        var porBanda = prediccion.ToDictionary(p => p.Banda.Nombre);

        // A mediodia en septiembre con el Sol flojo, veinte metros manda y ochenta esta cerrado.
        porBanda["20m"].Fiabilidad.Should().BeGreaterThan(0.8);
        porBanda["20m"].EsAproximacion.Should().BeFalse();
        porBanda["20m"].Motor.Should().Be(MotorIturHfProp.NombreDelMotor);
        porBanda["20m"].RelacionSenalRuido.Should().BeGreaterThan(10.0);
        porBanda["20m"].Saltos.Should().Be(1);
        porBanda["80m"].Fiabilidad.Should().BeLessThan(0.1);
    }

    [Fact]
    public async Task Seis_metros_se_le_sale_del_rango_y_cae_a_la_aproximacion()
    {
        // Es el caso que justifica que la bandera viva en cada prediccion: el motor es real, pero
        // esta banda no la ha calculado el.
        var motor = MotorIturHfProp.Localizar();
        if (motor is null)
        {
            return;
        }

        var prediccion = await motor.PredecirAsync(Solicitud(Madrid), CancellationToken.None);
        var seisMetros = prediccion.Single(p => p.Banda.Nombre == "6m");

        seisMetros.EsAproximacion.Should().BeTrue();
        seisMetros.Motor.Should().Be(MotorAproximacionNodisla.NombreDelMotor);

        // Y todas las demas, que si estan entre 1,6 y 30 MHz, las ha calculado el modelo.
        prediccion.Where(p => p.Banda.Nombre != "6m")
            .Should().OnlyContain(p => !p.EsAproximacion);
    }

    [Fact]
    public async Task Tampoco_el_motor_de_la_uit_publica_cifras_sin_sentido()
    {
        // La P.533 devuelve relaciones senal-ruido de ciento ochenta decibelios negativos para
        // bandas que no existen a esa hora. Es su numero, pero no significa nada: se recorta con
        // la misma regla que se aplica a la aproximacion propia.
        var motor = MotorIturHfProp.Localizar();
        if (motor is null)
        {
            return;
        }

        var prediccion = await motor.PredecirAsync(Solicitud(Madrid), CancellationToken.None);

        prediccion.Should().OnlyContain(p =>
            p.RelacionSenalRuido == null || p.RelacionSenalRuido > ModeloMufLuf.RelacionSinSentidoDb);

        // Y ciento sesenta metros a mediodia es justo uno de esos casos.
        var ciento = prediccion.Single(p => p.Banda.Nombre == "160m");
        ciento.Fiabilidad.Should().BeApproximately(0.0, 0.01);
        ciento.RelacionSenalRuido.Should().BeNull();
        ciento.SenalDbw.Should().BeNull();
        // Sigue siendo una prediccion del modelo de la UIT, no del respaldo.
        ciento.EsAproximacion.Should().BeFalse();
    }

    [Fact]
    public async Task A_medianoche_no_se_cae_el_proceso()
    {
        // Con Path.hour 0 el ejecutable muere con una violacion de segmento. Si alguien quita la
        // conversion a la hora 24, esta prueba lo caza.
        var motor = MotorIturHfProp.Localizar();
        if (motor is null)
        {
            return;
        }

        var prediccion = await motor.PredecirAsync(
            Solicitud(Madrid, Momento("2026-09-22T00:00:00Z")),
            CancellationToken.None);

        prediccion.Where(p => p.Banda.Nombre != "6m").Should().OnlyContain(p => !p.EsAproximacion);
    }

    [Fact]
    public async Task Un_trayecto_muy_largo_tambien_sale()
    {
        var motor = MotorIturHfProp.Localizar();
        if (motor is null)
        {
            return;
        }

        var tokio = new Coordenada(35.6895, 139.6917);
        var prediccion = await motor.PredecirAsync(Solicitud(tokio), CancellationToken.None);

        prediccion.Should().HaveCount(11);
        prediccion.Where(p => p.Banda.Nombre != "6m").Should().OnlyContain(p => !p.EsAproximacion);
        prediccion.Should().OnlyContain(p => p.Fiabilidad >= 0.0 && p.Fiabilidad <= 1.0);
    }

    [Fact]
    public async Task Si_el_ejecutable_no_esta_donde_dice_todo_cae_a_la_aproximacion()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "nodisla-sin-motor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(carpeta, "datos"));
        try
        {
            var motor = new MotorIturHfProp(
                Path.Combine(carpeta, "no-existe.exe"),
                Path.Combine(carpeta, "datos"));

            var prediccion = await motor.PredecirAsync(Solicitud(Madrid), CancellationToken.None);

            prediccion.Should().HaveCount(11);
            prediccion.Should().OnlyContain(p => p.EsAproximacion);
            prediccion.Should().OnlyContain(p => p.Motor == MotorAproximacionNodisla.NombreDelMotor);

            // El motor sigue diciendo lo que es; lo que cambia es lo que dice cada prediccion.
            motor.EsAproximacion.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(carpeta, recursive: true);
        }
    }

    [Fact]
    public void Sin_carpeta_de_datos_el_motor_no_se_da_por_instalado()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "nodisla-sin-datos-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(carpeta, "bin"));
        var falso = Path.Combine(carpeta, "bin", "ITURHFProp.exe");
        try
        {
            File.WriteAllText(falso, "no soy un ejecutable");

            MotorIturHfProp.Localizar(new OpcionesPropagacion { RutaDelMotorExterno = falso })
                .Should().BeNull();
        }
        finally
        {
            Directory.Delete(carpeta, recursive: true);
        }
    }

    [Fact]
    public void Se_puede_obligar_a_usar_solo_la_aproximacion()
    {
        MotorIturHfProp.Localizar(new OpcionesPropagacion { UsarMotorExterno = false })
            .Should().BeNull();
    }

    private static SolicitudDePrediccion Solicitud(Coordenada destino, DateTimeOffset? momento = null) =>
        new(Tenerife, destino, 100, momento ?? Mediodia, Indices);

    private static DateTimeOffset Momento(string texto) =>
        DateTimeOffset.Parse(texto, CultureInfo.InvariantCulture);
}
