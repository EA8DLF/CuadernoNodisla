using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Datos.Repositorios;
using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Datos.Pruebas;

/// <summary>Un notificador de pilas que solo cuenta los avisos.</summary>
internal sealed class NotificadorDePrueba : INotificadorDeDiplomas
{
    public int Avisos { get; private set; }

    public void CuadernoCambiado() => Avisos++;
}

/// <summary>La fabrica de conexiones y los avisos al motor de diplomas.</summary>
public sealed class ConexionYAvisosPruebas : IAsyncLifetime
{
    private CuadernoDePrueba cuaderno = null!;
    private ContextoCuaderno contexto = null!;
    private NotificadorDePrueba notificador = null!;
    private RepositorioQso repositorio = null!;

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        cuaderno = await CuadernoDePrueba.CrearAsync();
        contexto = cuaderno.CrearContexto();
        notificador = new NotificadorDePrueba();
        repositorio = new RepositorioQso(contexto, notificador);
    }

    /// <inheritdoc/>
    public async Task DisposeAsync() => await cuaderno.DisposeAsync();

    [Fact]
    public async Task LaFabricaAbreConexionesConLosAjustesPuestos()
    {
        IFabricaDeConexion fabrica = new FabricaDeConexion(cuaderno.Opciones);

        fabrica.RutaDelCuaderno.Should().Be(cuaderno.Opciones.Ruta);

        await using var conexion = await fabrica.AbrirAsync();

        conexion.State.Should().Be(System.Data.ConnectionState.Open);
        (await Escalar(conexion, "PRAGMA journal_mode;")).Should().Be("wal");
        (await Escalar(conexion, "PRAGMA foreign_keys;")).Should().Be("1");
        (await Escalar(conexion, "PRAGMA busy_timeout;")).Should().Be("5000");
    }

    [Fact]
    public async Task LaConexionDeLaFabricaVeElCuadernoDeVerdad()
    {
        await repositorio.AnadirAsync(FabricaDeContactos.Crear());

        var fabrica = new FabricaDeConexion(cuaderno.Opciones);
        await using var conexion = await fabrica.AbrirAsync();

        (await Escalar(conexion, "SELECT COUNT(*) FROM qso")).Should().Be("1");

        // Y el borrado en cascada funciona por esa conexion, que es lo que da foreign_keys.
        await using var orden = conexion.CreateCommand();
        orden.CommandText = "PRAGMA foreign_key_check;";
        await using var lector = await orden.ExecuteReaderAsync();
        (await lector.ReadAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task SeAvisaAlMotorDeDiplomasCuandoElCuadernoCambia()
    {
        var id = await repositorio.AnadirAsync(FabricaDeContactos.Crear());
        notificador.Avisos.Should().Be(1);

        await repositorio.AnadirLoteAsync(FabricaDeContactos.Generar(5));
        notificador.Avisos.Should().Be(2);

        await using (var otro = cuaderno.CrearContexto())
        {
            var cargado = await new RepositorioQso(otro).ObtenerAsync(id);
            cargado!.Comentario = "Cambiado";
            await new RepositorioQso(contexto, notificador).ActualizarAsync(cargado);
        }

        notificador.Avisos.Should().Be(3);

        await repositorio.EliminarAsync(id);
        notificador.Avisos.Should().Be(4);
    }

    [Fact]
    public async Task NoSeAvisaSiNoHaCambiadoNada()
    {
        var contactos = FabricaDeContactos.Generar(4).ToList();
        await repositorio.AnadirLoteAsync(contactos);
        var avisosTrasElAlta = notificador.Avisos;

        // Reimportar lo mismo no escribe nada: no hay por que invalidar los diplomas.
        var repetido = await repositorio.AnadirLoteAsync(FabricaDeContactos.Generar(4));
        repetido.Anadidos.Should().Be(0);
        notificador.Avisos.Should().Be(avisosTrasElAlta);

        // Borrar un contacto que no existe tampoco cambia nada.
        await repositorio.EliminarAsync(99999);
        notificador.Avisos.Should().Be(avisosTrasElAlta);
    }

    [Fact]
    public async Task ElRepositorioFuncionaSinNotificador()
    {
        var sinAvisos = new RepositorioQso(contexto);

        var id = await sinAvisos.AnadirAsync(FabricaDeContactos.Crear());

        (await sinAvisos.ContarAsync()).Should().Be(1);
        await sinAvisos.EliminarAsync(id);
        (await sinAvisos.ContarAsync()).Should().Be(0);
    }

    [Fact]
    public async Task LosCamposSigYSigInfoVuelvenIgualQueSeGuardaron()
    {
        var qso = FabricaDeContactos.Crear();
        qso.Sig = "SIOTA";
        qso.SigInfo = "EA8-0123";
        await repositorio.AnadirAsync(qso);

        await using var lectura = cuaderno.CrearContexto();
        var leido = await lectura.Qsos.SingleAsync();

        leido.Sig.Should().Be("SIOTA");
        leido.SigInfo.Should().Be("EA8-0123");

        // La columna no distingue mayusculas: los diplomas comparan referencias sin normalizar.
        var encontrados = await lectura.Qsos.CountAsync(q => q.SigInfo == "ea8-0123");
        encontrados.Should().Be(1);
    }

    private static async Task<string?> Escalar(System.Data.Common.DbConnection conexion, string sql)
    {
        await using var orden = conexion.CreateCommand();
        orden.CommandText = sql;
        var valor = await orden.ExecuteScalarAsync();
        return valor?.ToString();
    }
}
