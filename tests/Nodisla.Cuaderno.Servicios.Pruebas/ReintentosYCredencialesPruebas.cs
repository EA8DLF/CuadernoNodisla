using System.Net;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using FluentAssertions;
using Nodisla.Cuaderno.Servicios.Credenciales;
using Nodisla.Cuaderno.Servicios.Red;

namespace Nodisla.Cuaderno.Servicios.Pruebas;

/// <summary>Reintentos con retardo creciente y custodia de los secretos.</summary>
public class ReintentosYCredencialesPruebas
{
    [Fact]
    public async Task Reintenta_los_fallos_pasajeros_y_acaba_devolviendo_el_resultado()
    {
        var esperas = new List<TimeSpan>();
        var politica = new PoliticaDeReintentos(
            esperar: (retardo, _) => { esperas.Add(retardo); return Task.CompletedTask; })
        {
            Intentos = 3,
            RetardoInicial = TimeSpan.FromSeconds(2),
        };

        var intentos = 0;
        var resultado = await politica.EjecutarAsync("prueba", _ =>
        {
            intentos++;
            if (intentos < 3) throw new HttpRequestException("caido", null, HttpStatusCode.ServiceUnavailable);
            return Task.FromResult("bien");
        });

        resultado.Should().Be("bien");
        intentos.Should().Be(3);
        esperas.Should().HaveCount(2);
    }

    [Fact]
    public void El_retardo_crece_y_no_pasa_del_tope()
    {
        var politica = new PoliticaDeReintentos(esperar: (_, _) => Task.CompletedTask)
        {
            RetardoInicial = TimeSpan.FromSeconds(2),
            Factor = 2,
            RetardoMaximo = TimeSpan.FromSeconds(10),
        };

        politica.RetardoDe(1).Should().Be(TimeSpan.Zero);
        politica.RetardoDe(2).Should().Be(TimeSpan.FromSeconds(2));
        politica.RetardoDe(3).Should().Be(TimeSpan.FromSeconds(4));
        politica.RetardoDe(4).Should().Be(TimeSpan.FromSeconds(8));
        politica.RetardoDe(5).Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task No_insiste_con_lo_que_no_va_a_mejorar()
    {
        var politica = new PoliticaDeReintentos(esperar: (_, _) => Task.CompletedTask) { Intentos = 3 };

        var intentos = 0;
        var accion = async () => await politica.EjecutarAsync<string>("prueba", _ =>
        {
            intentos++;
            throw new RespuestaDelServicioException("no autorizado", HttpStatusCode.Unauthorized);
        });

        await accion.Should().ThrowAsync<RespuestaDelServicioException>();
        intentos.Should().Be(1);
    }

    [Fact]
    public async Task Agotados_los_intentos_avisa_de_que_el_servicio_no_esta()
    {
        var politica = new PoliticaDeReintentos(esperar: (_, _) => Task.CompletedTask) { Intentos = 2 };

        var accion = async () => await politica.EjecutarAsync<string>(
            "descargar el informe de LoTW",
            _ => throw new HttpRequestException("sin red"));

        (await accion.Should().ThrowAsync<ServicioNoDisponibleException>())
            .WithMessage("*descargar el informe de LoTW*");
    }

    [Fact]
    public void Un_codigo_429_se_reintenta_y_un_404_no()
    {
        PoliticaDeReintentos.EsRecuperable(HttpStatusCode.TooManyRequests).Should().BeTrue();
        PoliticaDeReintentos.EsRecuperable(HttpStatusCode.InternalServerError).Should().BeTrue();
        PoliticaDeReintentos.EsRecuperable(HttpStatusCode.NotFound).Should().BeFalse();
        PoliticaDeReintentos.EsRecuperable(HttpStatusCode.Unauthorized).Should().BeFalse();
    }

    [Fact]
    public void El_almacen_en_memoria_guarda_lee_y_borra()
    {
        var almacen = new AlmacenDeCredencialesEnMemoria();

        almacen.Existe(ClavesDeCredencial.LotwContrasena).Should().BeFalse();
        almacen.Guardar(ClavesDeCredencial.LotwContrasena, "secreta");
        almacen.Leer(ClavesDeCredencial.LotwContrasena).Should().Be("secreta");
        almacen.Borrar(ClavesDeCredencial.LotwContrasena);
        almacen.Leer(ClavesDeCredencial.LotwContrasena).Should().BeNull();
    }

    [Fact]
    public void El_almacen_cifrado_no_deja_el_secreto_en_claro_en_el_fichero()
    {
        if (!OperatingSystem.IsWindows()) return; // DPAPI solo existe en Windows.

        var ruta = Path.Combine(Path.GetTempPath(), $"nodisla-cred-{Guid.NewGuid():N}.dat");
        try
        {
            var almacen = new AlmacenDeCredencialesDpapi(ruta);
            almacen.Guardar(ClavesDeCredencial.ClubLogApi, "clave-en-claro-que-no-debe-aparecer");

            var contenido = File.ReadAllText(ruta);
            contenido.Should().NotContain("clave-en-claro-que-no-debe-aparecer");

            // Y se recupera entera al volver a abrirlo.
            new AlmacenDeCredencialesDpapi(ruta).Leer(ClavesDeCredencial.ClubLogApi)
                .Should().Be("clave-en-claro-que-no-debe-aparecer");
        }
        finally
        {
            if (File.Exists(ruta)) File.Delete(ruta);
        }
    }

    [Fact]
    public void Dos_secretos_distintos_no_comparten_cifrado()
    {
        if (!OperatingSystem.IsWindows()) return;

        var ruta = Path.Combine(Path.GetTempPath(), $"nodisla-cred-{Guid.NewGuid():N}.dat");
        try
        {
            var almacen = new AlmacenDeCredencialesDpapi(ruta);
            almacen.Guardar(ClavesDeCredencial.LotwContrasena, "lamisma");
            almacen.Guardar(ClavesDeCredencial.EqslContrasena, "lamisma");

            var lineas = File.ReadAllLines(ruta).Where(l => l.Contains('=')).ToList();
            lineas.Should().HaveCount(2);
            lineas[0].Split('=')[1].Should().NotBe(lineas[1].Split('=')[1]);
        }
        finally
        {
            if (File.Exists(ruta)) File.Delete(ruta);
        }
    }
}
