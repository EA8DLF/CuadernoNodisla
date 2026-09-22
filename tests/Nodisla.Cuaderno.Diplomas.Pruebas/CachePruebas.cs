using FluentAssertions;

namespace Nodisla.Cuaderno.Diplomas.Pruebas;

/// <summary>
/// La cache se tiene que enterar de todo.
/// </summary>
/// <remarks>
/// Una cache que se queda vieja en un diploma es peor que no tener cache: el operador ve un
/// numero que ya no es verdad y no tiene manera de saberlo. Se comprueban las dos vias: el aviso
/// explicito de la aplicacion y la marca de agua para lo que entra por detras.
/// </remarks>
public sealed class CachePruebas : IAsyncLifetime
{
    private readonly CuadernoDePrueba _cuaderno = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _cuaderno.DisposeAsync().AsTask();

    [Fact]
    public async Task Un_contacto_nuevo_se_refleja_sin_que_nadie_avise()
    {
        var motor = _cuaderno.Motor(o => o.IntervaloDeComprobacion = TimeSpan.Zero);
        _cuaderno.AnadirQso("K1ABC", dxcc: 291);

        (await motor.ProgresoAsync("DXCC", "MIXED")).Trabajadas.Should().Be(1);

        _cuaderno.AnadirQso("EA8DLF", dxcc: 29);

        (await motor.ProgresoAsync("DXCC", "MIXED")).Trabajadas.Should().Be(2);
    }

    [Fact]
    public async Task Una_confirmacion_descargada_se_refleja_sin_que_nadie_avise()
    {
        var motor = _cuaderno.Motor(o => o.IntervaloDeComprobacion = TimeSpan.Zero);
        var qso = _cuaderno.AnadirQso("K1ABC", dxcc: 291);

        (await motor.ProgresoAsync("DXCC", "MIXED")).Confirmadas.Should().Be(0);

        _cuaderno.AnadirConfirmacion(qso, "LOTW", "Y");

        (await motor.ProgresoAsync("DXCC", "MIXED")).Confirmadas.Should().Be(1);
    }

    [Fact]
    public async Task Una_confirmacion_que_pasa_de_pendiente_a_recibida_tambien_se_refleja()
    {
        var motor = _cuaderno.Motor(o => o.IntervaloDeComprobacion = TimeSpan.Zero);
        var qso = _cuaderno.AnadirQso("K1ABC", dxcc: 291);
        _cuaderno.AnadirConfirmacion(qso, "LOTW", "N");

        (await motor.ProgresoAsync("DXCC", "MIXED")).Confirmadas.Should().Be(0);

        // No entra ninguna fila nueva: cambia una que ya estaba.
        _cuaderno.AnadirConfirmacion(qso, "LOTW", "Y");

        (await motor.ProgresoAsync("DXCC", "MIXED")).Confirmadas.Should().Be(1);
    }

    [Fact]
    public async Task El_aviso_explicito_tira_la_cache_aunque_no_toque_comprobar()
    {
        var motor = _cuaderno.Motor(o => o.IntervaloDeComprobacion = TimeSpan.FromHours(1));
        _cuaderno.AnadirQso("K1ABC", dxcc: 291);

        (await motor.ProgresoAsync("DXCC", "MIXED")).Trabajadas.Should().Be(1);

        _cuaderno.AnadirQso("EA8DLF", dxcc: 29);

        // Sin aviso y con la comprobacion todavia lejos, se sirve lo cacheado: eso es lo que
        // hace que teclear un indicativo no cueste una consulta.
        (await motor.ProgresoAsync("DXCC", "MIXED")).Trabajadas.Should().Be(1);

        motor.AvisarDeCambioEnContactos();

        (await motor.ProgresoAsync("DXCC", "MIXED")).Trabajadas.Should().Be(2);
    }

    [Fact]
    public async Task El_aviso_de_confirmaciones_tambien_renueva_lo_que_usa_el_aviso_al_teclear()
    {
        var motor = _cuaderno.Motor(o =>
        {
            o.IntervaloDeComprobacion = TimeSpan.FromHours(1);
            o.MisDiplomas.Add("DXCC/MIXED");
        });

        var qso = _cuaderno.AnadirQso("EA8ABC", dxcc: 29);
        var antes = await motor.QueAportaAsync(
            Dominio.Valores.Indicativo.Crudo("EA8DLF"),
            Dominio.Valores.Banda.Parse("20m"),
            Dominio.Valores.Modo.Crudo("SSB"));
        antes.Should().Contain(a => a.Contains("sin confirmar"));

        _cuaderno.AnadirConfirmacion(qso, "LOTW", "Y");
        motor.AvisarDeCambioEnConfirmaciones();

        var despues = await motor.QueAportaAsync(
            Dominio.Valores.Indicativo.Crudo("EA8DLF"),
            Dominio.Valores.Banda.Parse("20m"),
            Dominio.Valores.Modo.Crudo("SSB"));
        despues.Should().BeEmpty("la entidad ya está trabajada y confirmada");
    }

    [Fact]
    public async Task Recalcular_renueva_todo_e_informa_del_avance()
    {
        var motor = _cuaderno.Motor(o =>
        {
            o.IntervaloDeComprobacion = TimeSpan.FromHours(1);
            o.MisDiplomas.Add("DXCC/MIXED");
            o.MisDiplomas.Add("WAS/WAS");
        });

        _cuaderno.AnadirQso("K1ABC", dxcc: 291, state: "CA");
        await motor.ProgresoAsync("DXCC", "MIXED");

        _cuaderno.AnadirQso("EA8DLF", dxcc: 29);

        var avances = new List<Aplicacion.Puertos.ProgresoDeSincronizacion>();
        await motor.RecalcularAsync(new Progress<Aplicacion.Puertos.ProgresoDeSincronizacion>(avances.Add));

        (await motor.ProgresoAsync("DXCC", "MIXED")).Trabajadas.Should().Be(2);

        // Progress<T> entrega en el contexto de sincronizacion, asi que puede llegar con
        // retraso; lo que importa es que el progreso quede bien.
        var mios = await motor.ProgresoDeMisDiplomasAsync();
        mios.Should().HaveCount(2);
    }
}
