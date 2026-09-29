using System.Diagnostics;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Datos.Repositorios;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;
using Xunit.Abstractions;

namespace Nodisla.Cuaderno.Datos.Pruebas;

/// <summary>El cuaderno: alta, edicion, borrado, busqueda y deteccion de duplicados.</summary>
/// <param name="salida">Salida de la prueba, para publicar las mediciones.</param>
public sealed class RepositorioQsoPruebas(ITestOutputHelper salida) : IAsyncLifetime
{
    private CuadernoDePrueba cuaderno = null!;
    private ContextoCuaderno contexto = null!;
    private RepositorioQso repositorio = null!;

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        cuaderno = await CuadernoDePrueba.CrearAsync();
        contexto = cuaderno.CrearContexto();
        repositorio = new RepositorioQso(contexto);
    }

    /// <inheritdoc/>
    public async Task DisposeAsync() => await cuaderno.DisposeAsync();

    [Fact]
    public async Task AnadirGuardaElContactoConSusFilasHijas()
    {
        var qso = FabricaDeContactos.Crear();
        qso.Confirmaciones.Add(new QsoConfirmacion
        {
            Medio = MedioDeConfirmacion.Lotw,
            Recibido = EstadoDeConfirmacion.Confirmado,
        });
        qso.Referencias.Add(new QsoReferencia { Tipo = TipoDeReferencia.Sota, Codigo = "EA8/GC-001" });

        var id = await repositorio.AnadirAsync(qso);
        id.Should().BeGreaterThan(0);

        await using var otro = cuaderno.CrearContexto();
        var leido = await new RepositorioQso(otro).ObtenerAsync(id);

        leido.Should().NotBeNull();
        leido!.Call.Valor.Should().Be("DL1ABC");
        leido.Mode.NombreUsual.Should().Be("FT8");
        leido.Confirmaciones.Should().HaveCount(1);
        leido.Referencias.Should().HaveCount(1);
        leido.CreadoUtc.Should().NotBe(default);
    }

    [Fact]
    public async Task ObtenerPorUuidEncuentraElContacto()
    {
        var qso = FabricaDeContactos.Crear();
        await repositorio.AnadirAsync(qso);

        var leido = await repositorio.ObtenerPorUuidAsync(qso.Uuid);

        leido.Should().NotBeNull();
        leido!.Id.Should().Be(qso.Id);
    }

    [Fact]
    public async Task ActualizarGuardaLosCambiosDeUnContactoDesconectado()
    {
        var qso = FabricaDeContactos.Crear();
        qso.Confirmaciones.Add(new QsoConfirmacion { Medio = MedioDeConfirmacion.Papel });
        var id = await repositorio.AnadirAsync(qso);

        await using (var edicion = cuaderno.CrearContexto())
        {
            var repoEdicion = new RepositorioQso(edicion);
            var cargado = await repoEdicion.ObtenerAsync(id);
            cargado!.Name = "Hansi";
            cargado.Qth = "Bonn";
            cargado.Mode = Modo.Parse("MFSK", "FT4");
            cargado.Confirmaciones.Clear();
            cargado.Confirmaciones.Add(new QsoConfirmacion
            {
                Medio = MedioDeConfirmacion.Lotw,
                Recibido = EstadoDeConfirmacion.Confirmado,
            });

            // Se edita una copia desconectada, como hace la rejilla del cuaderno.
            await using var otro = cuaderno.CrearContexto();
            await new RepositorioQso(otro).ActualizarAsync(cargado);
        }

        await using var lectura = cuaderno.CrearContexto();
        var leido = await new RepositorioQso(lectura).ObtenerAsync(id);

        leido!.Name.Should().Be("Hansi");
        leido.Qth.Should().Be("Bonn");
        leido.Mode.Submodo.Should().Be("FT4");
        leido.Confirmaciones.Should().HaveCount(1);
        leido.Confirmaciones[0].Medio.Should().Be(MedioDeConfirmacion.Lotw);
        leido.ModificadoUtc.Should().BeAfter(leido.CreadoUtc.AddSeconds(-1));
    }

    [Fact]
    public async Task EliminarBorraElContactoYSusFilasHijas()
    {
        var qso = FabricaDeContactos.Crear();
        qso.Confirmaciones.Add(new QsoConfirmacion { Medio = MedioDeConfirmacion.Papel });
        qso.Referencias.Add(new QsoReferencia { Tipo = TipoDeReferencia.Iota, Codigo = "AF-004" });
        qso.CamposExtra.Add(new QsoCampoExtra { Nombre = "APP_PRUEBA", Valor = "1" });
        var id = await repositorio.AnadirAsync(qso);

        await repositorio.EliminarAsync(id);

        await using var lectura = cuaderno.CrearContexto();
        (await lectura.Qsos.CountAsync()).Should().Be(0);
        (await lectura.Confirmaciones.CountAsync()).Should().Be(0);
        (await lectura.Referencias.CountAsync()).Should().Be(0);
        (await lectura.CamposExtra.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task BuscarDuplicadoUsaLaClaveNatural()
    {
        await repositorio.AnadirAsync(FabricaDeContactos.Crear());

        var candidato = FabricaDeContactos.Crear(submodo: "FT4");
        var duplicado = await repositorio.BuscarDuplicadoAsync(candidato);

        duplicado.Should().NotBeNull();

        var otroSegundo = FabricaDeContactos.Crear(inicio: FabricaDeContactos.Instante.AddSeconds(1));
        (await repositorio.BuscarDuplicadoAsync(otroSegundo)).Should().BeNull();

        var otraBanda = FabricaDeContactos.Crear(banda: "40m");
        (await repositorio.BuscarDuplicadoAsync(otraBanda)).Should().BeNull();
    }

    [Fact]
    public async Task TrabajadoAntesDevuelveLosContactosDelIndicativoDelMasNuevoAlMasViejo()
    {
        await repositorio.AnadirAsync(FabricaDeContactos.Crear(banda: "40m"));
        await repositorio.AnadirAsync(
            FabricaDeContactos.Crear(banda: "20m", inicio: FabricaDeContactos.Instante.AddDays(1)));
        await repositorio.AnadirAsync(FabricaDeContactos.Crear(call: "F5XYZ", banda: "20m"));

        var anteriores = await repositorio.TrabajadoAntesAsync(Indicativo.Parse("dl1abc"));

        anteriores.Should().HaveCount(2);
        anteriores[0].InicioUtc.Should().BeAfter(anteriores[1].InicioUtc);
    }

    [Fact]
    public async Task TrabajadoAntesConIndicativoVacioNoDevuelveNada()
    {
        await repositorio.AnadirAsync(FabricaDeContactos.Crear());

        (await repositorio.TrabajadoAntesAsync(Indicativo.Vacio)).Should().BeEmpty();
    }

    [Fact]
    public async Task BuscarFiltraPorTextoLibre()
    {
        await repositorio.AnadirAsync(FabricaDeContactos.Crear());
        var otro = FabricaDeContactos.Crear(call: "F5XYZ", banda: "40m");
        otro.Qth = "Toulouse";
        otro.Name = "Pierre";
        await repositorio.AnadirAsync(otro);

        var porQth = await repositorio.BuscarAsync(new CriterioQso { Texto = "toulou" }, 0, 20);
        porQth.TotalFiltrado.Should().Be(1);
        porQth.Elementos[0].Call.Valor.Should().Be("F5XYZ");

        var porNombre = await repositorio.BuscarAsync(new CriterioQso { Texto = "Hans" }, 0, 20);
        porNombre.TotalFiltrado.Should().Be(1);
        porNombre.Elementos[0].Call.Valor.Should().Be("DL1ABC");

        var porNotas = await repositorio.BuscarAsync(new CriterioQso { Texto = "novedad" }, 0, 20);
        porNotas.TotalFiltrado.Should().Be(2);
    }

    [Fact]
    public async Task ElIndiceDeTextoSigueLosCambiosYLosBorrados()
    {
        var id = await repositorio.AnadirAsync(FabricaDeContactos.Crear());

        await using (var edicion = cuaderno.CrearContexto())
        {
            var repoEdicion = new RepositorioQso(edicion);
            var cargado = await repoEdicion.ObtenerAsync(id);
            cargado!.Qth = "Hamburgo";
            await repoEdicion.ActualizarAsync(cargado);
        }

        (await repositorio.BuscarAsync(new CriterioQso { Texto = "Hamburgo" }, 0, 20))
            .TotalFiltrado.Should().Be(1);
        (await repositorio.BuscarAsync(new CriterioQso { Texto = "Koln" }, 0, 20))
            .TotalFiltrado.Should().Be(0);

        await repositorio.EliminarAsync(id);

        (await repositorio.BuscarAsync(new CriterioQso { Texto = "Hamburgo" }, 0, 20))
            .TotalFiltrado.Should().Be(0);
    }

    [Fact]
    public async Task BuscarFiltraPorIndicativoExactoYPorComodin()
    {
        await repositorio.AnadirAsync(FabricaDeContactos.Crear(call: "EA8DLF"));
        await repositorio.AnadirAsync(FabricaDeContactos.Crear(call: "EA8ABC", banda: "40m"));
        await repositorio.AnadirAsync(FabricaDeContactos.Crear(call: "F5XYZ", banda: "15m"));

        var exacto = await repositorio.BuscarAsync(new CriterioQso { Call = "ea8dlf" }, 0, 20);
        exacto.TotalFiltrado.Should().Be(1);

        var comodin = await repositorio.BuscarAsync(new CriterioQso { Call = "EA8*" }, 0, 20);
        comodin.TotalFiltrado.Should().Be(2);
    }

    [Fact]
    public async Task BuscarFiltraPorBandaModoEntidadYFechas()
    {
        await repositorio.AnadirAsync(FabricaDeContactos.Crear(banda: "20m", modo: "MFSK", submodo: "FT8"));
        await repositorio.AnadirAsync(
            FabricaDeContactos.Crear(call: "F5XYZ", banda: "40m", modo: "CW", submodo: null, dxcc: 227));
        await repositorio.AnadirAsync(
            FabricaDeContactos.Crear(call: "G0ABC", banda: "20m", modo: "SSB", submodo: null, dxcc: 223,
                inicio: FabricaDeContactos.Instante.AddDays(10)));

        var porBanda = await repositorio.BuscarAsync(new CriterioQso { Band = Banda.Parse("20m") }, 0, 20);
        porBanda.TotalFiltrado.Should().Be(2);

        // La busqueda no rastrea entidades: el submodo tiene que llegar igual.
        porBanda.Elementos.Single(q => q.Call.Valor == "DL1ABC").Mode.Submodo.Should().Be("FT8");

        (await repositorio.BuscarAsync(new CriterioQso { Mode = "cw" }, 0, 20))
            .TotalFiltrado.Should().Be(1);
        (await repositorio.BuscarAsync(new CriterioQso { Dxcc = 223 }, 0, 20))
            .TotalFiltrado.Should().Be(1);

        var porFecha = await repositorio.BuscarAsync(
            new CriterioQso { DesdeUtc = FabricaDeContactos.Instante.AddDays(5) }, 0, 20);
        porFecha.TotalFiltrado.Should().Be(1);
        porFecha.Elementos[0].Call.Valor.Should().Be("G0ABC");

        var hasta = await repositorio.BuscarAsync(
            new CriterioQso { HastaUtc = FabricaDeContactos.Instante.AddDays(5) }, 0, 20);
        hasta.TotalFiltrado.Should().Be(2);
    }

    [Fact]
    public async Task BuscarFiltraPorEstacionYPorConfirmacion()
    {
        var estacion = new Estacion
        {
            NombrePerfil = "Casa",
            StationCallsign = Indicativo.Parse("EA8DLF"),
        };
        contexto.Estaciones.Add(estacion);
        await contexto.SaveChangesAsync();

        var confirmado = FabricaDeContactos.Crear();
        confirmado.EstacionId = estacion.Id;
        confirmado.Confirmaciones.Add(new QsoConfirmacion
        {
            Medio = MedioDeConfirmacion.Lotw,
            Recibido = EstadoDeConfirmacion.Confirmado,
        });
        await repositorio.AnadirAsync(confirmado);

        var pendiente = FabricaDeContactos.Crear(call: "F5XYZ", banda: "40m");
        pendiente.Confirmaciones.Add(new QsoConfirmacion
        {
            Medio = MedioDeConfirmacion.Lotw,
            Recibido = EstadoDeConfirmacion.Pendiente,
        });
        await repositorio.AnadirAsync(pendiente);

        (await repositorio.BuscarAsync(new CriterioQso { EstacionId = estacion.Id }, 0, 20))
            .TotalFiltrado.Should().Be(1);

        var porConfirmacion = await repositorio.BuscarAsync(
            new CriterioQso { ConfirmadoPor = MedioDeConfirmacion.Lotw }, 0, 20);
        porConfirmacion.TotalFiltrado.Should().Be(1);
        porConfirmacion.Elementos[0].Call.Valor.Should().Be("DL1ABC");
    }

    [Fact]
    public async Task BuscarPaginaYOrdenaEnSql()
    {
        await repositorio.AnadirLoteAsync(FabricaDeContactos.Generar(25));

        var primera = await repositorio.BuscarAsync(new CriterioQso(), 0, 10);
        primera.TotalFiltrado.Should().Be(25);
        primera.Elementos.Should().HaveCount(10);
        primera.Desplazamiento.Should().Be(0);
        primera.Elementos[0].InicioUtc.Should().BeAfter(primera.Elementos[9].InicioUtc);

        var tercera = await repositorio.BuscarAsync(new CriterioQso(), 20, 10);
        tercera.Elementos.Should().HaveCount(5);
        tercera.Elementos.Should().NotContain(q => primera.Elementos.Any(p => p.Id == q.Id));

        var ascendente = await repositorio.BuscarAsync(
            new CriterioQso { OrdenarPor = CampoDeOrden.Fecha, Descendente = false }, 0, 3);
        ascendente.Elementos[0].InicioUtc.Should().Be(FabricaDeContactos.Instante);

        var porIndicativo = await repositorio.BuscarAsync(
            new CriterioQso { OrdenarPor = CampoDeOrden.Indicativo, Descendente = false }, 0, 25);
        porIndicativo.Elementos.Select(q => q.Call.Valor).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task ContarDevuelveLosContactosDelCuaderno()
    {
        await repositorio.AnadirLoteAsync(FabricaDeContactos.Generar(7));

        (await repositorio.ContarAsync()).Should().Be(7);
    }

    [Fact]
    public async Task AnadirLoteImportaElCuadernoRealEnUnaTransaccion()
    {
        // 1.838 es el tamano del cuaderno real de EA8DLF, que es el caso que hay que aguantar.
        const int cuantos = 1838;
        var contactos = FabricaDeContactos.Generar(cuantos).ToList();

        var reloj = Stopwatch.StartNew();
        var resultado = await repositorio.AnadirLoteAsync(contactos);
        reloj.Stop();

        salida.WriteLine(
            $"Alta en lote de {cuantos} contactos: {reloj.ElapsedMilliseconds} ms " +
            $"({cuantos * 1000.0 / Math.Max(1, reloj.ElapsedMilliseconds):F0} contactos/s)");

        resultado.Anadidos.Should().Be(cuantos);
        resultado.OmitidosPorDuplicado.Should().Be(0);
        resultado.Total.Should().Be(cuantos);
        resultado.Duracion.Should().BeGreaterThan(TimeSpan.Zero);
        (await repositorio.ContarAsync()).Should().Be(cuantos);
        reloj.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));

        var relojBusqueda = Stopwatch.StartNew();
        var trabajado = await repositorio.TrabajadoAntesAsync(contactos[1000].Call);
        relojBusqueda.Stop();
        salida.WriteLine($"«Trabajado antes» sobre {cuantos} contactos: {relojBusqueda.Elapsed.TotalMilliseconds:F1} ms");

        trabajado.Should().NotBeEmpty();
    }

    [Fact]
    public async Task AnadirLoteSinOmitirDuplicadosNoDejaNadaSiUnoRepiteLaClaveNatural()
    {
        var contactos = FabricaDeContactos.Generar(10).ToList();
        contactos.Add(Copiar(contactos[0]));

        var importar = async () => await repositorio.AnadirLoteAsync(contactos, omitirDuplicados: false);

        await importar.Should().ThrowAsync<DbUpdateException>();
        (await repositorio.ContarAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AnadirLoteSaltaLosQueYaEstabanEnElCuaderno()
    {
        var primeros = FabricaDeContactos.Generar(10).ToList();
        (await repositorio.AnadirLoteAsync(primeros)).Anadidos.Should().Be(10);

        // Reimportar el mismo respaldo con cinco contactos nuevos al final.
        var reimportado = FabricaDeContactos.Generar(15).ToList();

        var resultado = await repositorio.AnadirLoteAsync(reimportado);

        resultado.Anadidos.Should().Be(5);
        resultado.OmitidosPorDuplicado.Should().Be(10);
        resultado.Total.Should().Be(15);
        (await repositorio.ContarAsync()).Should().Be(15);
    }

    [Fact]
    public async Task AnadirLoteSaltaTambienLosRepetidosDentroDelPropioFichero()
    {
        var contactos = FabricaDeContactos.Generar(5).ToList();
        contactos.Add(Copiar(contactos[0]));
        contactos.Add(Copiar(contactos[3]));

        var resultado = await repositorio.AnadirLoteAsync(contactos);

        resultado.Anadidos.Should().Be(5);
        resultado.OmitidosPorDuplicado.Should().Be(2);
        (await repositorio.ContarAsync()).Should().Be(5);
    }

    [Fact]
    public async Task LaPaginacionNoRepiteNiSaltaContactosDelMismoSegundo()
    {
        // Cien contactos con el mismo instante: sin desempate por Id, la paginacion miente.
        var mismoSegundo = Enumerable.Range(0, 100)
            .Select(i => FabricaDeContactos.Crear(
                call: $"EA8T{i:00}", banda: "20m", inicio: FabricaDeContactos.Instante))
            .ToList();
        (await repositorio.AnadirLoteAsync(mismoSegundo)).Anadidos.Should().Be(100);

        foreach (var descendente in new[] { true, false })
        {
            var vistos = new List<long>();
            for (var desplazamiento = 0; desplazamiento < 100; desplazamiento += 10)
            {
                var pagina = await repositorio.BuscarAsync(
                    new CriterioQso { OrdenarPor = CampoDeOrden.Fecha, Descendente = descendente },
                    desplazamiento,
                    10);
                vistos.AddRange(pagina.Elementos.Select(q => q.Id));
            }

            vistos.Should().HaveCount(100);
            vistos.Should().OnlyHaveUniqueItems();
        }
    }

    [Fact]
    public async Task TrabajadoAntesRespetaElTopePedido()
    {
        var contactos = Enumerable.Range(0, 8)
            .Select(i => FabricaDeContactos.Crear(
                banda: "20m", inicio: FabricaDeContactos.Instante.AddMinutes(i)))
            .ToList();
        await repositorio.AnadirLoteAsync(contactos);

        (await repositorio.TrabajadoAntesAsync(Indicativo.Parse("DL1ABC"), maximo: 3))
            .Should().HaveCount(3);
        (await repositorio.TrabajadoAntesAsync(Indicativo.Parse("DL1ABC")))
            .Should().HaveCount(8);
    }

    private static Qso Copiar(Qso qso) => FabricaDeContactos.Crear(
        call: qso.Call.Valor,
        banda: qso.Band.Nombre,
        modo: qso.Mode.Principal,
        submodo: qso.Mode.Submodo,
        inicio: qso.InicioUtc);

    [Fact]
    public async Task TodosLosMetodosQueDevuelvenContactosTraenSusColeccionesHijas()
    {
        var qso = ConHijas();
        var id = await repositorio.AnadirAsync(qso);

        await using var otro = cuaderno.CrearContexto();
        var repoLectura = new RepositorioQso(otro);

        Comprobar(await repoLectura.ObtenerAsync(id), "ObtenerAsync");
        Comprobar(await repoLectura.ObtenerPorUuidAsync(qso.Uuid), "ObtenerPorUuidAsync");
        Comprobar((await repoLectura.BuscarAsync(new CriterioQso(), 0, 10)).Elementos.Single(), "BuscarAsync");
        Comprobar(
            (await repoLectura.BuscarAsync(new CriterioQso { Texto = "Koln" }, 0, 10)).Elementos.Single(),
            "BuscarAsync con texto libre");
        Comprobar(await repoLectura.BuscarDuplicadoAsync(ConHijas()), "BuscarDuplicadoAsync");
        Comprobar(
            (await repoLectura.TrabajadoAntesAsync(Indicativo.Parse("DL1ABC"))).Single(),
            "TrabajadoAntesAsync");

        static void Comprobar(Qso? leido, string metodo)
        {
            leido.Should().NotBeNull($"{metodo} tiene que devolver el contacto");
            leido!.Confirmaciones.Should().HaveCount(2, $"{metodo} debe traer las confirmaciones");
            leido.Confirmaciones.Should().ContainSingle(c => c.Medio == MedioDeConfirmacion.Lotw
                                                            && c.EstaConfirmada);
            leido.Referencias.Should().HaveCount(2, $"{metodo} debe traer las referencias");
            leido.CamposExtra.Should().HaveCount(3, $"{metodo} debe traer los campos extra");
            leido.CamposExtra.Should().ContainSingle(c => c.Nombre == "APP_LOG4OM_QSO_ID"
                                                         && c.Valor == "4711");
        }
    }

    [Fact]
    public async Task ActualizarModificaUnaConfirmacionYBorraLaQueYaNoEsta()
    {
        var id = await repositorio.AnadirAsync(ConHijas());

        await using (var edicion = cuaderno.CrearContexto())
        {
            var repoEdicion = new RepositorioQso(edicion);
            var cargado = await repoEdicion.ObtenerAsync(id);

            var lotw = cargado!.Confirmaciones.Single(c => c.Medio == MedioDeConfirmacion.Lotw);
            lotw.Recibido = EstadoDeConfirmacion.Verificado;
            lotw.Nota = "Verificada por el servicio";

            var papel = cargado.Confirmaciones.Single(c => c.Medio == MedioDeConfirmacion.Papel);
            cargado.Confirmaciones.Remove(papel);

            cargado.Referencias.Remove(cargado.Referencias.Last());
            cargado.CamposExtra.Single(c => c.Nombre == "APP_LOG4OM_QSO_ID").Valor = "9999";
            cargado.CamposExtra.Add(new QsoCampoExtra { Nombre = "APP_NODISLA_ORIGEN", Valor = "prueba" });

            // Se guarda desde otro contexto: el grafo llega desconectado, como en la rejilla.
            await using var otro = cuaderno.CrearContexto();
            await new RepositorioQso(otro).ActualizarAsync(cargado);
        }

        await using var lectura = cuaderno.CrearContexto();
        var leido = await new RepositorioQso(lectura).ObtenerAsync(id);

        leido!.Confirmaciones.Should().HaveCount(1);
        leido.Confirmaciones[0].Medio.Should().Be(MedioDeConfirmacion.Lotw);
        leido.Confirmaciones[0].Recibido.Should().Be(EstadoDeConfirmacion.Verificado);
        leido.Confirmaciones[0].Nota.Should().Be("Verificada por el servicio");
        leido.Referencias.Should().HaveCount(1);
        leido.CamposExtra.Should().HaveCount(4);
        leido.CamposExtra.Single(c => c.Nombre == "APP_LOG4OM_QSO_ID").Valor.Should().Be("9999");
        leido.CamposExtra.Should().ContainSingle(c => c.Nombre == "APP_NODISLA_ORIGEN");

        // Y no se han quedado filas huerfanas en las tablas hijas.
        (await lectura.Confirmaciones.CountAsync()).Should().Be(1);
        (await lectura.Referencias.CountAsync()).Should().Be(1);
        (await lectura.CamposExtra.CountAsync()).Should().Be(4);
    }

    [Fact]
    public async Task ActualizarNoDuplicaLasHijasDeUnGrafoSinIdentificadores()
    {
        var id = await repositorio.AnadirAsync(ConHijas());

        // Asi queda un contacto recien fundido con un ADIF: las hijas no traen identificador.
        var fundido = ConHijas();
        fundido.Id = id;
        fundido.CamposExtra.Add(new QsoCampoExtra { Nombre = "APP_NUEVO", Valor = "1" });

        await using (var otro = cuaderno.CrearContexto())
        {
            await new RepositorioQso(otro).ActualizarAsync(fundido);
        }

        await using var lectura = cuaderno.CrearContexto();
        var leido = await new RepositorioQso(lectura).ObtenerAsync(id);

        leido!.Confirmaciones.Should().HaveCount(2);
        leido.Referencias.Should().HaveCount(2);
        leido.CamposExtra.Should().HaveCount(4);
    }

    [Fact]
    public async Task UnaPaginaDeDoscientosContactosConHijasNoMultiplicaLasFilas()
    {
        const int cuantos = 200;
        var contactos = FabricaDeContactos.Generar(cuantos).ToList();
        foreach (var qso in contactos)
        {
            qso.Confirmaciones.Add(new QsoConfirmacion
            {
                Medio = MedioDeConfirmacion.Lotw,
                Recibido = EstadoDeConfirmacion.Confirmado,
            });
            qso.Confirmaciones.Add(new QsoConfirmacion { Medio = MedioDeConfirmacion.Papel });
            qso.Confirmaciones.Add(new QsoConfirmacion { Medio = MedioDeConfirmacion.Eqsl });
            qso.Referencias.Add(new QsoReferencia { Tipo = TipoDeReferencia.Pota, Codigo = "EA-0001" });
            for (var i = 0; i < 6; i++)
            {
                qso.CamposExtra.Add(new QsoCampoExtra { Nombre = $"APP_X{i}", Valor = i.ToString() });
            }
        }

        await repositorio.AnadirLoteAsync(contactos);

        await using var otro = cuaderno.CrearContexto();
        var reloj = Stopwatch.StartNew();
        var pagina = await new RepositorioQso(otro).BuscarAsync(new CriterioQso(), 0, cuantos);
        reloj.Stop();

        salida.WriteLine($"Pagina de {cuantos} contactos con 3 confirmaciones, 1 referencia y "
            + $"6 campos extra: {reloj.Elapsed.TotalMilliseconds:F0} ms");

        pagina.Elementos.Should().HaveCount(cuantos);
        pagina.Elementos.Select(q => q.Id).Should().OnlyHaveUniqueItems();
        pagina.Elementos.Should().OnlyContain(q => q.Confirmaciones.Count == 3);
        pagina.Elementos.Should().OnlyContain(q => q.Referencias.Count == 1);
        pagina.Elementos.Should().OnlyContain(q => q.CamposExtra.Count == 6);
        reloj.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task BuscarDuplicadoAguantaElRitmoDeUnaImportacion()
    {
        const int cuantos = 1838;
        var contactos = FabricaDeContactos.Generar(cuantos).ToList();
        foreach (var qso in contactos)
        {
            qso.Confirmaciones.Add(new QsoConfirmacion
            {
                Medio = MedioDeConfirmacion.Lotw,
                Recibido = EstadoDeConfirmacion.Confirmado,
            });
            qso.CamposExtra.Add(new QsoCampoExtra { Nombre = "APP_LOG4OM_QSO_ID", Valor = "1" });
        }

        await repositorio.AnadirLoteAsync(contactos);

        await using var otro = cuaderno.CrearContexto();
        var repoLectura = new RepositorioQso(otro);

        // Los candidatos son contactos recien leidos de un ADIF: sin identificador todavia.
        var candidatos = contactos.Select(Copiar).ToList();

        // Se calienta la consulta antes de medir: el primer viaje paga el plan de EF Core.
        await repoLectura.BuscarDuplicadoAsync(candidatos[0]);

        var reloj = Stopwatch.StartNew();
        var encontrados = 0;
        foreach (var candidato in candidatos)
        {
            if (await repoLectura.BuscarDuplicadoAsync(candidato) is not null)
            {
                encontrados++;
            }
        }

        reloj.Stop();
        salida.WriteLine($"BuscarDuplicadoAsync sobre {cuantos} contactos con hijas: "
            + $"{reloj.ElapsedMilliseconds} ms en total, "
            + $"{reloj.Elapsed.TotalMilliseconds / cuantos:F2} ms por contacto");

        encontrados.Should().Be(cuantos);
        (reloj.Elapsed.TotalMilliseconds / cuantos).Should().BeLessThan(10);
    }

    private static Qso ConHijas()
    {
        var qso = FabricaDeContactos.Crear();
        qso.Confirmaciones.Add(new QsoConfirmacion
        {
            Medio = MedioDeConfirmacion.Lotw,
            Enviado = EstadoDeConfirmacion.Confirmado,
            Recibido = EstadoDeConfirmacion.Confirmado,
            RecibidoUtc = FabricaDeContactos.Instante.AddDays(3),
        });
        qso.Confirmaciones.Add(new QsoConfirmacion
        {
            Medio = MedioDeConfirmacion.Papel,
            Enviado = EstadoDeConfirmacion.Solicitado,
            Via = ViaDeEnvio.Buro,
        });
        qso.Referencias.Add(new QsoReferencia { Tipo = TipoDeReferencia.Pota, Codigo = "EA-0001" });
        qso.Referencias.Add(new QsoReferencia
        {
            Tipo = TipoDeReferencia.Sota,
            Codigo = "EA8/GC-001",
            Lado = LadoDeReferencia.Propia,
        });
        qso.CamposExtra.Add(new QsoCampoExtra { Nombre = "APP_LOG4OM_QSO_ID", Valor = "4711" });
        qso.CamposExtra.Add(new QsoCampoExtra { Nombre = "APP_LOG4OM_EQSL", Valor = "N" });
        qso.CamposExtra.Add(new QsoCampoExtra { Nombre = "MY_ANTENNA_AZ", Valor = "180" });
        return qso;
    }
}
