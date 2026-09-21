using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Datos.Pruebas;

/// <summary>Ida y vuelta de cada objeto de valor entre el dominio y la base de datos.</summary>
public sealed class ConversoresPruebas : IAsyncLifetime
{
    private CuadernoDePrueba cuaderno = null!;

    /// <inheritdoc/>
    public async Task InitializeAsync() => cuaderno = await CuadernoDePrueba.CrearAsync();

    /// <inheritdoc/>
    public async Task DisposeAsync() => await cuaderno.DisposeAsync();

    [Fact]
    public async Task LosObjetosDeValorVuelvenIgualQueSeGuardaron()
    {
        var original = FabricaDeContactos.Crear();
        original.FreqRx = Frecuencia.DesdeMegahercios(14.076m);
        original.BandRx = Banda.Parse("20m");
        original.FinUtc = original.InicioUtc.AddMinutes(3);

        long id;
        await using (var escritura = cuaderno.CrearContexto())
        {
            escritura.Qsos.Add(original);
            await escritura.SaveChangesAsync();
            id = original.Id;
        }

        await using var lectura = cuaderno.CrearContexto();
        var leido = await lectura.Qsos.SingleAsync(q => q.Id == id);

        leido.Call.Should().Be(original.Call);
        leido.Call.Valor.Should().Be("DL1ABC");
        leido.Band.Should().Be(original.Band);
        leido.BandRx.Should().Be(original.BandRx);
        leido.Mode.Principal.Should().Be("MFSK");
        leido.Mode.Submodo.Should().Be("FT8");
        leido.Mode.Should().Be(original.Mode);
        leido.Freq.Should().Be(original.Freq);
        leido.FreqRx.Should().Be(original.FreqRx);
        leido.RstSent.Should().Be(original.RstSent);
        leido.RstSent.Forma.Should().Be(FormaDeInforme.Decibelios);
        leido.RstRcvd.Should().Be(original.RstRcvd);
        leido.Gridsquare.Should().Be(original.Gridsquare);
        leido.MyGridsquare.Should().Be(original.MyGridsquare);
        leido.StationCallsign.Should().Be(original.StationCallsign);
        leido.InicioUtc.Should().Be(original.InicioUtc);
        leido.FinUtc.Should().Be(original.FinUtc);
        leido.Uuid.Should().Be(original.Uuid);
    }

    [Fact]
    public async Task LosValoresVaciosVuelvenVacios()
    {
        var original = FabricaDeContactos.Crear();
        original.Gridsquare = Locator.Vacio;
        original.BandRx = Banda.Vacia;
        original.RstSent = Informe.Ninguno;
        original.FreqRx = null;
        original.Mode = Modo.Parse("CW");

        await using (var escritura = cuaderno.CrearContexto())
        {
            escritura.Qsos.Add(original);
            await escritura.SaveChangesAsync();
        }

        await using var lectura = cuaderno.CrearContexto();
        var leido = await lectura.Qsos.SingleAsync();

        leido.Gridsquare.EsVacio.Should().BeTrue();
        leido.BandRx.EsVacia.Should().BeTrue();
        leido.RstSent.EsVacio.Should().BeTrue();
        leido.FreqRx.Should().BeNull();
        leido.Mode.Principal.Should().Be("CW");
        leido.Mode.Submodo.Should().BeNull();
    }

    [Fact]
    public async Task LasFechasSeGuardanEnTextoUtcAlSegundo()
    {
        var original = FabricaDeContactos.Crear();
        original.InicioUtc = new DateTimeOffset(2026, 5, 23, 11, 56, 31, TimeSpan.FromHours(2));

        await using (var escritura = cuaderno.CrearContexto())
        {
            escritura.Qsos.Add(original);
            await escritura.SaveChangesAsync();
        }

        await using var conexion = await cuaderno.AbrirConexionAsync();
        await using var orden = conexion.CreateCommand();
        orden.CommandText = "SELECT qso_inicio_utc, freq, mode, submode FROM qso";
        await using var lector = await orden.ExecuteReaderAsync();
        (await lector.ReadAsync()).Should().BeTrue();

        // La hora local (11:56 en +02:00) tiene que haberse guardado como 09:56 UTC.
        lector.GetString(0).Should().Be("2026-05-23 09:56:31");
        lector.GetDouble(1).Should().BeApproximately(14.074, 1e-9);
        lector.GetString(2).Should().Be("MFSK");
        lector.GetString(3).Should().Be("FT8");
    }

    [Fact]
    public async Task LasConfirmacionesYReferenciasGuardanSusCodigos()
    {
        var qso = FabricaDeContactos.Crear();
        qso.Confirmaciones.Add(new QsoConfirmacion
        {
            Medio = MedioDeConfirmacion.Lotw,
            Enviado = EstadoDeConfirmacion.Confirmado,
            Recibido = EstadoDeConfirmacion.Confirmado,
            EnviadoUtc = FabricaDeContactos.Instante,
            RecibidoUtc = FabricaDeContactos.Instante.AddDays(2),
            Via = ViaDeEnvio.Electronico,
            Nota = "Verificado",
        });
        qso.Referencias.Add(new QsoReferencia
        {
            Tipo = TipoDeReferencia.Pota,
            Codigo = "EA-0001",
            Lado = LadoDeReferencia.Propia,
            Descripcion = "Parque de prueba",
        });
        qso.CamposExtra.Add(new QsoCampoExtra
        {
            Nombre = "APP_LOG4OM_QSO_ID",
            Valor = "1234",
            TipoAdif = "S",
        });

        await using (var escritura = cuaderno.CrearContexto())
        {
            escritura.Qsos.Add(qso);
            await escritura.SaveChangesAsync();
        }

        await using var conexion = await cuaderno.AbrirConexionAsync();
        await using var orden = conexion.CreateCommand();
        orden.CommandText = """
            SELECT (SELECT servicio FROM qso_confirmacion),
                   (SELECT recibida FROM qso_confirmacion),
                   (SELECT via_envio FROM qso_confirmacion),
                   (SELECT award_code FROM qso_referencia),
                   (SELECT propia FROM qso_referencia),
                   (SELECT campo FROM qso_campo_extra)
            """;
        await using var lector = await orden.ExecuteReaderAsync();
        (await lector.ReadAsync()).Should().BeTrue();

        lector.GetString(0).Should().Be("LOTW");
        lector.GetString(1).Should().Be("Y");
        lector.GetString(2).Should().Be("E");
        lector.GetString(3).Should().Be("POTA");
        lector.GetInt32(4).Should().Be(1);
        lector.GetString(5).Should().Be("APP_LOG4OM_QSO_ID");

        await using var lectura = cuaderno.CrearContexto();
        var leido = await lectura.Qsos
            .Include(q => q.Confirmaciones)
            .Include(q => q.Referencias)
            .Include(q => q.CamposExtra)
            .SingleAsync();

        leido.Confirmaciones[0].Medio.Should().Be(MedioDeConfirmacion.Lotw);
        leido.Confirmaciones[0].Recibido.Should().Be(EstadoDeConfirmacion.Confirmado);
        leido.Confirmaciones[0].Via.Should().Be(ViaDeEnvio.Electronico);
        leido.Confirmaciones[0].RecibidoUtc.Should().Be(FabricaDeContactos.Instante.AddDays(2));
        leido.Referencias[0].Tipo.Should().Be(TipoDeReferencia.Pota);
        leido.Referencias[0].Lado.Should().Be(LadoDeReferencia.Propia);
        leido.CamposExtra[0].Valor.Should().Be("1234");
    }
}
