using System.Globalization;
using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Propagacion.Solar;

namespace Nodisla.Cuaderno.Propagacion.Pruebas;

/// <summary>
/// Composicion de los indices, su edad y el cache en disco. Nada de esto toca la red.
/// </summary>
public class IndicesSolaresPruebas
{
    [Fact]
    public void La_fecha_del_conjunto_es_la_de_la_medida_mas_antigua()
    {
        // El viento solar es de hace un minuto y el flujo solar es de ayer. El conjunto no puede
        // presentarse como si fuera de hace un minuto.
        var wwv = new LecturaWwv(
            104,
            Momento("2026-09-21T20:00:00Z"),
            3,
            Momento("2026-09-22T00:00:00Z"),
            0.67,
            Momento("2026-09-22T06:00:00Z"),
            false,
            Momento("2026-09-22T06:15:00Z"));
        var diario = new LecturaDiariaSolar(Momento("2026-09-21T00:00:00Z"), 104, 85);
        var viento = new LecturaInstantanea(309, Momento("2026-09-22T07:17:00Z"));
        var campo = new LecturaInstantanea(-3.2, Momento("2026-09-22T07:17:00Z"));

        var indices = ProveedorDeIndicesSolaresSwpc.Componer(wwv, diario, viento, campo);

        indices.Should().NotBeNull();
        indices!.MedidoUtc.Should().Be(Momento("2026-09-21T20:00:00Z"));
        indices.FlujoSolar.Should().Be(104);
        indices.ManchasSolares.Should().Be(85);
        indices.IndiceA.Should().Be(3);
        indices.IndiceK.Should().Be(0.67);
        indices.VientoSolarKmS.Should().Be(309);
        indices.CampoBz.Should().Be(-3.2);
        indices.Tormenta.Should().BeFalse();
    }

    [Fact]
    public void Sin_ningun_boletin_no_se_inventa_nada()
    {
        ProveedorDeIndicesSolaresSwpc.Componer(null, null, null, null).Should().BeNull();
    }

    [Fact]
    public void Con_solo_el_viento_solar_se_devuelve_lo_que_hay_y_lo_demas_va_nulo()
    {
        var viento = new LecturaInstantanea(420, Momento("2026-09-22T07:17:00Z"));

        var indices = ProveedorDeIndicesSolaresSwpc.Componer(null, null, viento, null);

        indices.Should().NotBeNull();
        indices!.VientoSolarKmS.Should().Be(420);
        indices.FlujoSolar.Should().BeNull();
        indices.IndiceK.Should().BeNull();
        indices.ManchasSolares.Should().BeNull();
    }

    [Fact]
    public void Si_falla_el_boletin_el_indice_a_sale_de_los_datos_diarios()
    {
        var diario = new LecturaDiariaSolar(Momento("2026-09-21T00:00:00Z"), 104, 85);
        var respaldo = new LecturaInstantanea(7, Momento("2026-09-22T00:00:00Z"));

        var indices = ProveedorDeIndicesSolaresSwpc.Componer(null, diario, null, null, respaldo);

        indices!.IndiceA.Should().Be(7);
        indices.IndiceK.Should().BeNull();
    }

    [Fact]
    public void Un_indice_k_alto_se_cuenta_como_tormenta()
    {
        var wwv = new LecturaWwv(
            187, Momento("2026-09-21T20:00:00Z"),
            48, Momento("2026-09-22T00:00:00Z"),
            6.33, Momento("2026-09-22T06:00:00Z"),
            false,
            Momento("2026-09-22T06:15:00Z"));

        ProveedorDeIndicesSolaresSwpc.Componer(wwv, null, null, null)!.Tormenta.Should().BeTrue();
    }

    [Fact]
    public void La_edad_de_la_medida_se_dice_siempre_aunque_se_acabe_de_descargar()
    {
        // El flujo solar se publica una vez al dia: recien traido ya tiene doce horas. Se dice.
        var lectura = new LecturaDeIndices(
            Indices(Momento("2026-09-21T20:00:00Z")),
            Momento("2026-09-22T08:00:00Z"),
            OrigenDeLosIndices.Red,
            "NOAA SWPC");
        var ahora = Momento("2026-09-22T08:00:00Z");

        lectura.Antiguedad(ahora).Should().Be(TimeSpan.FromHours(12));
        lectura.EstanFrescos(ahora, TimeSpan.FromHours(3)).Should().BeTrue();
        lectura.Describir(ahora, TimeSpan.FromHours(3))
            .Should().Be("Índices del 21/09/2026 20:00 UTC, hace 12 h (NOAA SWPC).");
    }

    [Fact]
    public void Si_hace_horas_que_no_se_refrescan_se_dice_cuantas()
    {
        var lectura = new LecturaDeIndices(
            Indices(Momento("2026-09-21T20:00:00Z")),
            Momento("2026-09-22T08:00:00Z"),
            OrigenDeLosIndices.Red,
            "NOAA SWPC");
        var ahora = Momento("2026-09-22T15:00:00Z");

        lectura.EstanFrescos(ahora, TimeSpan.FromHours(3)).Should().BeFalse();
        lectura.Describir(ahora, TimeSpan.FromHours(3))
            .Should().Contain("sin refrescar desde hace 7 h")
            .And.Contain("del 21/09/2026 20:00 UTC, hace 19 h");
    }

    [Fact]
    public void Lo_que_viene_del_fichero_se_dice_que_viene_del_fichero()
    {
        var lectura = new LecturaDeIndices(
            Indices(Momento("2026-09-20T20:00:00Z")),
            Momento("2026-09-20T21:00:00Z"),
            OrigenDeLosIndices.Cache,
            "NOAA SWPC");

        lectura.Describir(Momento("2026-09-22T08:00:00Z"), TimeSpan.FromHours(3))
            .Should().StartWith("Sin red:").And.Contain("1 día");
    }

    [Theory]
    [InlineData(30, "menos de un minuto")]
    [InlineData(2100, "35 min")]
    [InlineData(7800, "2 h 10 min")]
    [InlineData(7200, "2 h")]
    [InlineData(259200, "3 días")]
    public void Las_edades_se_escriben_en_palabras_cortas(int segundos, string esperado)
    {
        LecturaDeIndices.TextoDeEdad(TimeSpan.FromSeconds(segundos)).Should().Be(esperado);
    }

    [Fact]
    public void El_cache_conserva_los_valores_y_los_marca_como_guardados()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "nodisla-propagacion-" + Guid.NewGuid().ToString("N"));
        var ruta = Path.Combine(carpeta, "indices.json");
        try
        {
            var cache = new CacheDeIndicesEnDisco(ruta);
            var original = new LecturaDeIndices(
                Indices(Momento("2026-09-21T20:00:00Z")),
                Momento("2026-09-22T08:00:00Z"),
                OrigenDeLosIndices.Red,
                "NOAA SWPC");

            cache.Guardar(original);
            var leido = cache.Leer();

            leido.Should().NotBeNull();
            leido!.Indices.Should().Be(original.Indices);
            leido.ObtenidoUtc.Should().Be(original.ObtenidoUtc);
            leido.Origen.Should().Be(OrigenDeLosIndices.Cache);
        }
        finally
        {
            if (Directory.Exists(carpeta))
            {
                Directory.Delete(carpeta, recursive: true);
            }
        }
    }

    [Fact]
    public void Un_cache_que_no_existe_o_esta_roto_devuelve_nulo()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "nodisla-propagacion-" + Guid.NewGuid().ToString("N"));
        var ruta = Path.Combine(carpeta, "indices.json");
        try
        {
            new CacheDeIndicesEnDisco(ruta).Leer().Should().BeNull();

            Directory.CreateDirectory(carpeta);
            File.WriteAllText(ruta, "esto no es json");
            new CacheDeIndicesEnDisco(ruta).Leer().Should().BeNull();
        }
        finally
        {
            if (Directory.Exists(carpeta))
            {
                Directory.Delete(carpeta, recursive: true);
            }
        }
    }

    private static IndicesSolares Indices(DateTimeOffset medido) =>
        new(104, 3, 0.67, 85, 309, -3.2, false, medido);

    private static DateTimeOffset Momento(string texto) =>
        DateTimeOffset.Parse(texto, CultureInfo.InvariantCulture);
}
