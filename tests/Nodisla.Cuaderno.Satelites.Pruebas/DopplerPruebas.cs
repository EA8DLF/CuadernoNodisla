using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Satelites.Catalogo;
using Nodisla.Cuaderno.Satelites.Cuaderno;
using Nodisla.Cuaderno.Satelites.Doppler;
using Nodisla.Cuaderno.Satelites.Orbital;
using Nodisla.Cuaderno.Satelites.Seguimiento;

namespace Nodisla.Cuaderno.Satelites.Pruebas;

/// <summary>
/// Correccion Doppler, enlaces de transpondedor y campos ADIF del contacto por satelite.
/// </summary>
/// <remarks>
/// El equipo que se usa aqui es de mentira y solo apunta lo que le mandan. No hay ni una sola
/// prueba que hable con una radio de verdad: el FT-710 puede estar encendido al lado.
/// </remarks>
public sealed class DopplerPruebas
{
    private const string TleSo50 = """
        SAUDISAT 1C (SO-50)
        1 27607U 02058C   26269.22993978  .00000723  00000+0  10290-3 0  9997
        2 27607  64.5524 176.3163 0071304 243.8576 115.5184 14.83229784279446
        """;

    /// <summary>Equipo de mentira: apunta las escrituras y no habla con nada.</summary>
    private sealed class EquipoDePapel : IEquipoConDosVfos
    {
        public List<(NombreDeVfo Vfo, Frecuencia Frecuencia)> Escrituras { get; } = [];

        public EstadoDeLosVfos Vfos => EstadoDeLosVfos.SinDatos;

        public Task<EstadoDeLosVfos> LeerVfosAsync(CancellationToken ct = default) =>
            Task.FromResult(Vfos);

        public Task PonerVfoActivoAsync(NombreDeVfo vfo, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task IntercambiarVfosAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task IgualarVfosAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task PonerFrecuenciaDeAsync(NombreDeVfo vfo, Frecuencia frecuencia, CancellationToken ct = default)
        {
            Escrituras.Add((vfo, frecuencia));
            return Task.CompletedTask;
        }

        public Task PonerModoDeAsync(NombreDeVfo vfo, Modo modo, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    [Fact]
    public void LaSubidaYLaBajadaSeCorrigenEnSentidosContrarios()
    {
        var nominal = Frecuencia.DesdeMegahercios(435.000m);

        // Se acerca a cinco kilometros por segundo: la velocidad radial es negativa.
        var bajada = CorreccionDoppler.Bajada(nominal, -5.0);
        var subida = CorreccionDoppler.Subida(nominal, -5.0);

        bajada.Hercios.Should().BeGreaterThan(nominal.Hercios, "acercándose se oye más arriba");
        subida.Hercios.Should().BeLessThan(nominal.Hercios, "para que llegue en su sitio hay que bajar");

        // Los dos desplazamientos tienen el mismo tamaño salvo el segundo orden, despreciable.
        var arriba = bajada.Hercios - nominal.Hercios;
        var abajo = nominal.Hercios - subida.Hercios;
        Math.Abs(arriba - abajo).Should().BeLessThan(1, "a 5 km/s el segundo orden no llega al hercio");

        // 435 MHz a 5 km/s son 7,25 kHz.
        arriba.Should().BeCloseTo(7255, 5);
    }

    [Fact]
    public void ElTranspondedorInvertidoMueveLaSubidaAlReves()
    {
        var rs44 = CatalogoDeSatelites.Instancia.PorAbreviatura("RS-44")!;
        var lineal = rs44.Transpondedores.First(t => t.Clase == ClaseDeTranspondedor.Lineal);
        lineal.Invertido.Should().BeTrue("el lineal de RS-44 es invertido");

        var abajo = EnlaceDeSatelite.ConBajada(lineal, lineal.BajadaInicio!.Value);
        var arriba = EnlaceDeSatelite.ConBajada(lineal, lineal.BajadaFin!.Value);

        // Al extremo bajo de la bajada le toca el extremo alto de la subida, y al revés.
        abajo.SubidaNominal.Should().Be(lineal.SubidaFin!.Value);
        arriba.SubidaNominal.Should().Be(lineal.SubidaInicio!.Value);
    }

    [Fact]
    public void ElTranspondedorNoInvertidoMueveLaSubidaEnElMismoSentido()
    {
        var ao7 = CatalogoDeSatelites.Instancia.PorAbreviatura("AO-7")!;
        var modoA = ao7.Transpondedores.First(t => t.NombreDelTranspondedor == "Modo A");
        modoA.Invertido.Should().BeFalse();

        var abajo = EnlaceDeSatelite.ConBajada(modoA, modoA.BajadaInicio!.Value);
        var arriba = EnlaceDeSatelite.ConBajada(modoA, modoA.BajadaFin!.Value);

        abajo.SubidaNominal.Should().Be(modoA.SubidaInicio!.Value);
        arriba.SubidaNominal.Should().Be(modoA.SubidaFin!.Value);
    }

    [Fact]
    public void EnUnRepetidorDeFmLaSubidaNoSeMueve()
    {
        var so50 = CatalogoDeSatelites.Instancia.PorAbreviatura("SO-50")!;
        var fm = so50.Transpondedores.First(t => t.Clase == ClaseDeTranspondedor.Fm);

        var enlace = EnlaceDeSatelite.ConBajada(fm, Frecuencia.DesdeMegahercios(436.800m));

        enlace.SubidaNominal.Should().Be(fm.SubidaInicio!.Value);
    }

    [Fact]
    public async Task ElSeguimientoEscribeLosDosVfosYRespetaLaBandaMuerta()
    {
        var seguidor = Preparado();
        var equipo = new EquipoDePapel();
        var doppler = new SeguimientoDoppler(
            seguidor, equipo, new OpcionesDeDoppler { BandaMuertaHz = 20 });

        var so50 = CatalogoDeSatelites.Instancia.PorAbreviatura("SO-50")!;
        doppler.Seguir(EnlaceDeSatelite.Centrado(
            so50.Transpondedores.First(t => t.Clase == ClaseDeTranspondedor.Fm)));

        // En mitad de un paso comprobado contra Heavens-Above (26-09-2026, culmina a 13:45:47).
        var t = new DateTimeOffset(2026, 9, 26, 13, 45, 0, TimeSpan.Zero);

        var primero = await doppler.AjustarAsync(t);
        primero.Escrita.Should().BeTrue();
        equipo.Escrituras.Should().HaveCount(2, "se escriben la bajada y la subida");
        equipo.Escrituras[0].Vfo.Should().Be(NombreDeVfo.B, "la bajada va primero: es lo que se oye");
        equipo.Escrituras[1].Vfo.Should().Be(NombreDeVfo.A);

        // Un centenar de milisegundos despues el satelite casi no se ha movido.
        var apenas = await doppler.AjustarAsync(t.AddMilliseconds(100));
        apenas.Escrita.Should().BeFalse();
        apenas.Motivo.Should().Contain("banda muerta");
        equipo.Escrituras.Should().HaveCount(2, "no se escribe por escribir");

        // Cinco segundos despues si se ha movido lo suficiente.
        var despues = await doppler.AjustarAsync(t.AddSeconds(5));
        despues.Escrita.Should().BeTrue();
        equipo.Escrituras.Count.Should().BeGreaterThan(2);
    }

    [Fact]
    public async Task NoSeTocaElEquipoConElSateliteBajoElHorizonte()
    {
        var seguidor = Preparado();
        var equipo = new EquipoDePapel();
        var doppler = new SeguimientoDoppler(seguidor, equipo);

        var so50 = CatalogoDeSatelites.Instancia.PorAbreviatura("SO-50")!;
        doppler.Seguir(EnlaceDeSatelite.Centrado(
            so50.Transpondedores.First(t => t.Clase == ClaseDeTranspondedor.Fm)));

        // Entre dos pasos: el 26-09-2026 a las 17:00 UTC SO-50 está muy por debajo.
        var fuera = await doppler.AjustarAsync(new DateTimeOffset(2026, 9, 26, 17, 0, 0, TimeSpan.Zero));

        fuera.Escrita.Should().BeFalse();
        fuera.Motivo.Should().Contain("horizonte");
        equipo.Escrituras.Should().BeEmpty();
    }

    [Fact]
    public async Task SoltarDejaDeEscribirYNoDevuelveElDialASuSitio()
    {
        var seguidor = Preparado();
        var equipo = new EquipoDePapel();
        var doppler = new SeguimientoDoppler(seguidor, equipo);

        var so50 = CatalogoDeSatelites.Instancia.PorAbreviatura("SO-50")!;
        doppler.Seguir(EnlaceDeSatelite.Centrado(
            so50.Transpondedores.First(t => t.Clase == ClaseDeTranspondedor.Fm)));
        await doppler.AjustarAsync(new DateTimeOffset(2026, 9, 26, 13, 45, 0, TimeSpan.Zero));
        var escritas = equipo.Escrituras.Count;

        doppler.Soltar();
        var despues = await doppler.AjustarAsync(new DateTimeOffset(2026, 9, 26, 13, 46, 0, TimeSpan.Zero));

        despues.Escrita.Should().BeFalse();
        equipo.Escrituras.Should().HaveCount(escritas, "soltar no mueve el dial del operador");
    }

    [Fact]
    public void ElContactoPorSateliteSaleConSusCincoCampos()
    {
        var so50 = CatalogoDeSatelites.Instancia.PorAbreviatura("SO-50")!;
        var enlace = EnlaceDeSatelite.Centrado(
            so50.Transpondedores.First(t => t.Clase == ClaseDeTranspondedor.Fm));

        var qso = new Qso { Call = Indicativo.Parse("EA8DLF") };
        ContactoPorSatelite.Aplicar(qso, enlace);

        qso.PropMode.Should().Be("SAT");
        qso.SatName.Should().Be("SO-50");
        qso.SatMode.Should().Be("VU");
        qso.Freq.Should().Be(Frecuencia.DesdeMegahercios(145.850m), "FREQ es la subida");
        qso.FreqRx.Should().Be(Frecuencia.DesdeMegahercios(436.795m), "FREQ_RX es la bajada");
        qso.Band.Nombre.Should().Be("2m");
        qso.BandRx.Nombre.Should().Be("70cm");
    }

    [Fact]
    public void SeAnotanLasFrecuenciasNominalesYNoLasDelDialCorrido()
    {
        var so50 = CatalogoDeSatelites.Instancia.PorAbreviatura("SO-50")!;
        var enlace = EnlaceDeSatelite.Centrado(
            so50.Transpondedores.First(t => t.Clase == ClaseDeTranspondedor.Fm));

        // El dial estaria aqui a mitad de paso; el cuaderno no debe llevar esto.
        var corrida = CorreccionDoppler.Bajada(enlace.BajadaNominal, -6.0);
        corrida.Should().NotBe(enlace.BajadaNominal);

        var qso = new Qso { Call = Indicativo.Parse("EA8DLF") };
        ContactoPorSatelite.Aplicar(qso, enlace);

        qso.FreqRx.Should().Be(enlace.BajadaNominal);
    }

    [Fact]
    public void SeDeduceElEnlaceDeUnContactoYaAnotado()
    {
        var qso = new Qso
        {
            Call = Indicativo.Parse("EA8DLF"),
            PropMode = "SAT",
            SatName = "RS-44",
            SatMode = "VU",
            Freq = Frecuencia.DesdeMegahercios(145.965m),
            FreqRx = Frecuencia.DesdeMegahercios(435.640m),
        };

        var enlace = ContactoPorSatelite.Deducir(qso);

        enlace.Should().NotBeNull();
        enlace!.Satelite.Should().Be("RS-44");
        enlace.Transpondedor.Clase.Should().Be(ClaseDeTranspondedor.Lineal);
    }

    [Fact]
    public void UnContactoQueNoEsPorSateliteNoDeduceNada()
    {
        var qso = new Qso
        {
            Call = Indicativo.Parse("EA8DLF"),
            Freq = Frecuencia.DesdeMegahercios(14.074m),
        };

        ContactoPorSatelite.Deducir(qso).Should().BeNull();
    }

    private static SeguidorDeSatelites Preparado()
    {
        var seguidor = new SeguidorDeSatelites(new OpcionesDeSatelites
        {
            Observador = Observador.DesdeLocator(Locator.Parse("JN00AA")),
        });
        seguidor.CargarFichero(TleSo50).Should().Be(1);
        return seguidor;
    }
}
