using FluentAssertions;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Satelites.Orbital;
using Nodisla.Cuaderno.Satelites.Prediccion;
using Nodisla.Cuaderno.Satelites.Seguimiento;

namespace Nodisla.Cuaderno.Satelites.Pruebas;

/// <summary>
/// Prediccion de pasos sobre la estacion de EA8DLF en Canarias.
/// </summary>
/// <remarks>
/// Todo esta fijado: los elementos, la estacion y la ventana de tiempo. Ninguna asercion mira
/// el reloj de la maquina, asi que estas pruebas valen igual a las tres de la manana y con el
/// ordenador cargado.
/// </remarks>
public sealed class PasosPruebas(Xunit.Abstractions.ITestOutputHelper salida)
{
    private const string Iss1 = "1 25544U 98067A   24015.54791667  .00016717  00000-0  30177-3 0  9002";
    private const string Iss2 = "2 25544  51.6416 247.4627 0006703 130.5360 325.0288 15.49682159 33601";

    private static readonly Observador Canarias =
        Observador.DesdeLocator(Locator.Parse("IL18"), 50);

    private static Sgp4 Iss() => new(LectorDeElementos.LeerPar("ISS (ZARYA)", Iss1, Iss2));

    private static DateTimeOffset Comienzo => new(2024, 1, 15, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EncuentraPasosCoherentesEnVeinticuatroHoras()
    {
        var predictor = new PredictorDePasos();
        var pasos = predictor.Buscar(Iss(), Canarias, Comienzo, Comienzo.AddHours(24));

        pasos.Should().NotBeEmpty("la ISS pasa varias veces al día sobre Canarias");

        foreach (var paso in pasos)
        {
            paso.Salida.Instante.Should().BeBefore(paso.Culminacion.Instante);
            paso.Culminacion.Instante.Should().BeBefore(paso.Puesta.Instante);
            paso.ElevacionMaximaGrados.Should().BeGreaterThan(paso.Salida.ElevacionGrados);
            paso.ElevacionMaximaGrados.Should().BeGreaterThan(paso.Puesta.ElevacionGrados);
            paso.ElevacionMaximaGrados.Should().BeLessThan(90.0);

            // La ISS está a unos 420 km: un paso no puede durar menos de un minuto ni más de
            // un cuarto de hora. Fuera de ahí, algo está mal en la geometría.
            paso.Duracion.Should().BeGreaterThan(TimeSpan.FromMinutes(1));
            paso.Duracion.Should().BeLessThan(TimeSpan.FromMinutes(15));

            // En la culminación el satélite está más cerca que al salir y al ponerse.
            paso.Culminacion.DistanciaKm.Should().BeLessThan(paso.Salida.DistanciaKm);
            paso.Culminacion.DistanciaKm.Should().BeLessThan(paso.Puesta.DistanciaKm);
        }

        pasos.Should().BeInAscendingOrder(p => p.Salida.Instante);
        salida.WriteLine($"{pasos.Count} pasos; elevación máxima "
                         + $"{pasos.Max(p => p.ElevacionMaximaGrados):F1}°, "
                         + $"mínima {pasos.Min(p => p.ElevacionMaximaGrados):F1}°");
    }

    [Fact]
    public void UnPasoDeExploracionCuatroVecesMasGruesoNoPierdeNingunPaso()
    {
        // Esta es la prueba que importa de verdad. La búsqueda es de máximos de elevación
        // justamente para que los pasos rasantes no se escapen entre dos muestras; si alguien
        // la cambia por una búsqueda de cruces del umbral, esto se cae.
        var fino = new PredictorDePasos(new OpcionesDePrediccion
        {
            PasoDeExploracion = TimeSpan.FromSeconds(30),
        });
        var grueso = new PredictorDePasos(new OpcionesDePrediccion
        {
            PasoDeExploracion = TimeSpan.FromSeconds(120),
        });

        var conFino = fino.Buscar(Iss(), Canarias, Comienzo, Comienzo.AddHours(48));
        var conGrueso = grueso.Buscar(Iss(), Canarias, Comienzo, Comienzo.AddHours(48));

        conGrueso.Should().HaveCount(conFino.Count);

        for (var i = 0; i < conFino.Count; i++)
        {
            (conGrueso[i].Culminacion.Instante - conFino[i].Culminacion.Instante)
                .Duration().Should().BeLessThan(TimeSpan.FromSeconds(5));
            conGrueso[i].ElevacionMaximaGrados
                .Should().BeApproximately(conFino[i].ElevacionMaximaGrados, 0.2);
        }
    }

    [Fact]
    public void SubirElUmbralDeElevacionDescartaLosPasosRasantes()
    {
        var todos = new PredictorDePasos(new OpcionesDePrediccion { ElevacionMinimaGrados = 0 })
            .Buscar(Iss(), Canarias, Comienzo, Comienzo.AddHours(48));
        var altos = new PredictorDePasos(new OpcionesDePrediccion { ElevacionMinimaGrados = 20 })
            .Buscar(Iss(), Canarias, Comienzo, Comienzo.AddHours(48));

        altos.Count.Should().BeLessThan(todos.Count);
        altos.Should().OnlyContain(p => p.ElevacionMaximaGrados > 20);
        todos.Should().Contain(p => p.EsRasante, "en dos días siempre sale algún paso rasante");
    }

    [Fact]
    public void ElResumenDelPasoAvisaDeLosElementosViejos()
    {
        var paso = new PredictorDePasos()
            .Buscar(Iss(), Canarias, Comienzo, Comienzo.AddHours(24))
            .First();

        var frescura = TimeSpan.FromDays(3);
        paso.Describir(paso.Salida.Instante, frescura).Should().NotContain("Ojo");
        paso.Describir(paso.Salida.Instante.AddDays(30), frescura).Should().Contain("Ojo");
    }

    [Fact]
    public void ElSeguimientoEnVivoCuadraConElPasoPredicho()
    {
        var seguidor = new SeguidorDeSatelites(new OpcionesDeSatelites { Observador = Canarias });
        seguidor.Cargar(LectorDeElementos.LeerPar("ISS (ZARYA)", Iss1, Iss2)).Should().BeTrue();

        var paso = seguidor.Pasos("ISS (ZARYA)", Comienzo, TimeSpan.FromHours(24)).First();
        var enCulminacion = seguidor.Donde("ISS (ZARYA)", paso.Culminacion.Instante);

        enCulminacion.Should().NotBeNull();
        enCulminacion!.Vista.ElevacionGrados
            .Should().BeApproximately(paso.ElevacionMaximaGrados, 1e-6);
        enCulminacion.Vista.SobreElHorizonte.Should().BeTrue();

        // La altura de la ISS sobre el elipsoide, y el subpunto dentro de la inclinación.
        enCulminacion.AlturaKm.Should().BeInRange(380, 460);
        Math.Abs(enCulminacion.Subpunto.Latitud).Should().BeLessThan(51.65);

        // A la mitad del periodo orbital ya no puede seguir a la vista.
        var media = seguidor.Donde("ISS (ZARYA)", paso.Culminacion.Instante.AddMinutes(46));
        media!.Vista.SobreElHorizonte.Should().BeFalse();
    }

    [Fact]
    public void ElSeguidorNoSeTraganUnSateliteDeEspacioProfundo()
    {
        var seguidor = new SeguidorDeSatelites(new OpcionesDeSatelites { Observador = Canarias });

        var cargado = seguidor.Cargar(LectorDeElementos.LeerPar(
            "GEOESTACIONARIO",
            "1 43700U 18090A   24015.50000000 -.00000100  00000-0  00000-0 0  9990",
            "2 43700   0.0200  95.0000 0002000 200.0000 160.0000  1.00270000 18000"));

        cargado.Should().BeFalse("hace falta SDP4 y no está implementado");
        seguidor.Cargados.Should().BeEmpty();
        seguidor.Donde("GEOESTACIONARIO", Comienzo).Should().BeNull();
    }
}
