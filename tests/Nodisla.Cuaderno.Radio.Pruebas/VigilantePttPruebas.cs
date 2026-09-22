using Microsoft.Extensions.Logging;
using System.Diagnostics;
using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Radio.Ptt;
using Nodisla.Cuaderno.Radio.Pruebas.Dobles;

namespace Nodisla.Cuaderno.Radio.Pruebas;

/// <summary>
/// El PTT no se puede quedar pegado. Estas son las pruebas que lo demuestran.
/// </summary>
/// <remarks>
/// Aqui no se transmite nunca de verdad: todo va contra <see cref="ControlDeMentira"/>. Cada
/// prueba termina comprobando que el PTT ha quedado abajo, que es lo unico que de verdad
/// importa; el motivo de la suelta es informacion para el operador.
/// </remarks>
public class VigilantePttPruebas
{
    private static OpcionesDelVigilante OpcionesRapidas(
        int tiempoMaximoMs = 5000,
        int tiempoSinLatidoMs = 5000) => new()
        {
            TiempoMaximo = TimeSpan.FromMilliseconds(tiempoMaximoMs),
            TiempoSinLatido = TimeSpan.FromMilliseconds(tiempoSinLatidoMs),
            PasoDeVigilancia = TimeSpan.FromMilliseconds(5),
            EsperaDeSuelta = TimeSpan.FromMilliseconds(500),
            EngancharseAlCierreDelProceso = false,
        };

    /// <summary>
    /// Lo que se le da al vigilante para que suelte: lo que el mismo declara que tarda —el
    /// tiempo que vigila mas su plazo de suelta— y un respiro para el planificador. Nada de
    /// plazos a ojo: si esto se queda corto, es que el vigilante llega tarde de verdad.
    /// </summary>
    private static TimeSpan PlazoRazonable(VigilantePtt vigilante, OpcionesDelVigilante opciones)
    {
        var loQueVigila = opciones.TiempoMaximo < opciones.TiempoSinLatido
            ? opciones.TiempoMaximo
            : opciones.TiempoSinLatido;

        return loQueVigila + opciones.PasoDeVigilancia + vigilante.PlazoDeSuelta + TimeSpan.FromSeconds(5);
    }

    private static async Task EsperarAQue(Func<bool> condicion, int milisegundos = 3000)
    {
        var reloj = Stopwatch.StartNew();
        while (reloj.ElapsedMilliseconds < milisegundos)
        {
            if (condicion())
            {
                return;
            }

            await Task.Delay(5);
        }

        condicion().Should().BeTrue("la condición debía cumplirse antes de agotarse la espera");
    }

    [Fact]
    public async Task El_uso_normal_sube_y_baja_el_ptt()
    {
        var equipo = new ControlDeMentira();
        await using var vigilante = new VigilantePtt(equipo, OpcionesRapidas());
        var sueltas = new EsperaDeSueltas(vigilante);

        await using (await vigilante.PedirAntenaAsync("prueba"))
        {
            equipo.PttArriba.Should().BeTrue();
            vigilante.EnAntena.Should().BeTrue();
        }

        equipo.PttArriba.Should().BeFalse();
        equipo.Subidas.Should().Be(1);
        equipo.Bajadas.Should().Be(1);
        vigilante.EnAntena.Should().BeFalse();
        (await sueltas.PrimeraAsync(TimeSpan.FromSeconds(10))).Should().Be(MotivoDeSuelta.Normal);
        sueltas.Motivos.Should().ContainSingle();
    }

    [Fact]
    public async Task Una_excepcion_dentro_del_bloque_baja_el_ptt()
    {
        var equipo = new ControlDeMentira();
        await using var vigilante = new VigilantePtt(equipo, OpcionesRapidas());

        var accion = async () =>
        {
            await using (await vigilante.PedirAntenaAsync("prueba"))
            {
                throw new InvalidOperationException("algo se rompió mientras transmitíamos");
            }
        };

        await accion.Should().ThrowAsync<InvalidOperationException>();
        equipo.PttArriba.Should().BeFalse();
        vigilante.EnAntena.Should().BeFalse();
    }

    [Fact]
    public async Task Una_excepcion_con_transmitir_se_apunta_como_excepcion()
    {
        var equipo = new ControlDeMentira();
        await using var vigilante = new VigilantePtt(equipo, OpcionesRapidas());
        var sueltas = new EsperaDeSueltas(vigilante);

        var accion = () => vigilante.TransmitirAsync(
            "prueba",
            _ => throw new InvalidOperationException("se rompió"));

        await accion.Should().ThrowAsync<InvalidOperationException>();
        equipo.PttArriba.Should().BeFalse();
        (await sueltas.PrimeraAsync(TimeSpan.FromSeconds(10))).Should().Be(MotivoDeSuelta.Excepcion);
    }

    [Fact]
    public async Task Al_cancelarse_el_testigo_se_baja_el_ptt()
    {
        var equipo = new ControlDeMentira();
        await using var vigilante = new VigilantePtt(equipo, OpcionesRapidas());
        using var cts = new CancellationTokenSource();

        var transmision = await vigilante.PedirAntenaAsync("prueba", cts.Token);
        equipo.PttArriba.Should().BeTrue();

        var sueltas = new EsperaDeSueltas(vigilante);

        await cts.CancelAsync();

        var motivo = await sueltas.PrimeraAsync(PlazoRazonable(vigilante, OpcionesRapidas()));
        motivo.Should().Be(MotivoDeSuelta.Cancelado);
        equipo.PttArriba.Should().BeFalse();
        vigilante.EnAntena.Should().BeFalse();
        transmision.EnAntena.Should().BeFalse();

        // Liberar despues de la suelta no manda otra orden.
        await transmision.DisposeAsync();
        equipo.Bajadas.Should().Be(1);
    }

    [Fact]
    public async Task Al_agotarse_el_tiempo_maximo_se_baja_el_ptt()
    {
        var opciones = OpcionesRapidas(tiempoMaximoMs: 120);
        var equipo = new ControlDeMentira();
        await using var vigilante = new VigilantePtt(equipo, opciones);
        var sueltas = new EsperaDeSueltas(vigilante);

        var transmision = await vigilante.PedirAntenaAsync("una transmisión que se eterniza");

        var motivo = await sueltas.PrimeraAsync(PlazoRazonable(vigilante, opciones));
        motivo.Should().Be(MotivoDeSuelta.TiempoAgotado);
        equipo.PttArriba.Should().BeFalse();
        transmision.EnAntena.Should().BeFalse();
        await transmision.DisposeAsync();
    }

    [Fact]
    public async Task Si_deja_de_latir_se_baja_el_ptt()
    {
        var opciones = OpcionesRapidas(tiempoMaximoMs: 5000, tiempoSinLatidoMs: 100);
        var equipo = new ControlDeMentira();
        await using var vigilante = new VigilantePtt(equipo, opciones);
        var sueltas = new EsperaDeSueltas(vigilante);

        var transmision = await vigilante.PedirAntenaAsync("un módem que se cuelga");

        var motivo = await sueltas.PrimeraAsync(PlazoRazonable(vigilante, opciones));
        motivo.Should().Be(MotivoDeSuelta.SinLatido);
        equipo.PttArriba.Should().BeFalse();
        await transmision.DisposeAsync();
    }

    [Fact]
    public async Task El_tope_de_tiempo_suelta_aunque_el_repartidor_de_tareas_este_ahogado()
    {
        // La red de seguridad final es el tiempo máximo de transmisión. Tiene que cumplirse
        // aunque la aplicación haya dejado el repartidor de tareas sin un hilo libre: por eso
        // el vigilante tiene hilo propio y no espera a nadie.
        //
        // El repartidor se ocupa con tareas dormidas, no quemando procesador: lo que se prueba
        // es que la suelta no depende de que haya un hilo libre en el pool, no que aguante una
        // máquina sin procesador —eso no lo puede prometer nadie— y así esta prueba tampoco
        // estorba a las demás.
        var opciones = OpcionesRapidas(tiempoMaximoMs: 200);
        var equipo = new ControlDeMentira();
        await using var vigilante = new VigilantePtt(equipo, opciones);
        var sueltas = new EsperaDeSueltas(vigilante);

        using var ahogo = new CancellationTokenSource();
        var quemadores = new List<Task>();
        for (var i = 0; i < Environment.ProcessorCount * 4; i++)
        {
            quemadores.Add(Task.Run(
                () => ahogo.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(30)),
                CancellationToken.None));
        }

        try
        {
            var transmision = await vigilante.PedirAntenaAsync("una transmisión con la máquina ahogada");
            var reloj = Stopwatch.StartNew();

            // Se espera bloqueando: con el repartidor de tareas lleno, un «await» no despertaría
            // aunque el vigilante hubiera soltado, y estaríamos midiendo el pool, no el PTT.
            var motivo = sueltas.EsperarBloqueando(PlazoRazonable(vigilante, opciones));
            reloj.Stop();

            motivo.Should().Be(MotivoDeSuelta.TiempoAgotado);
            equipo.PttArriba.Should().BeFalse();
            reloj.Elapsed.Should().BeLessThan(
                opciones.TiempoMaximo + vigilante.PlazoDeSuelta + TimeSpan.FromSeconds(2),
                "el tope de transmisión no puede quedarse esperando a que haya un hilo libre");

            await transmision.DisposeAsync();
        }
        finally
        {
            await ahogo.CancelAsync();
            try
            {
                await Task.WhenAll(quemadores).WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (Exception)
            {
                // La carga es de mentira: da igual cómo termine.
            }
        }
    }

    [Fact]
    public async Task Mientras_late_se_mantiene_en_antena()
    {
        var equipo = new ControlDeMentira();
        await using var vigilante = new VigilantePtt(
            equipo,
            OpcionesRapidas(tiempoMaximoMs: 5000, tiempoSinLatidoMs: 500));

        await using var transmision = await vigilante.PedirAntenaAsync("un módem que late");
        var reloj = Stopwatch.StartNew();
        while (reloj.ElapsedMilliseconds < 1000)
        {
            transmision.Latir();
            await Task.Delay(25);
        }

        equipo.PttArriba.Should().BeTrue("mientras se late el vigilante no debe soltar");
        transmision.EnAntena.Should().BeTrue();
    }

    [Fact]
    public async Task La_doble_liberacion_solo_baja_el_ptt_una_vez()
    {
        var equipo = new ControlDeMentira();
        await using var vigilante = new VigilantePtt(equipo, OpcionesRapidas());

        var transmision = await vigilante.PedirAntenaAsync("prueba");
        await transmision.DisposeAsync();
        await transmision.DisposeAsync();
        await transmision.DisposeAsync();

        equipo.Bajadas.Should().Be(1);
        equipo.PttArriba.Should().BeFalse();
    }

    [Fact]
    public async Task Se_puede_liberar_desde_otro_hilo()
    {
        var equipo = new ControlDeMentira();
        await using var vigilante = new VigilantePtt(equipo, OpcionesRapidas());

        var transmision = await vigilante.PedirAntenaAsync("prueba");
        await Task.Run(async () => await transmision.DisposeAsync());

        equipo.PttArriba.Should().BeFalse();
        equipo.Bajadas.Should().Be(1);
    }

    [Fact]
    public async Task Dos_sueltas_a_la_vez_no_dejan_el_ptt_puesto()
    {
        var equipo = new ControlDeMentira { Tardanza = TimeSpan.FromMilliseconds(20) };
        await using var vigilante = new VigilantePtt(equipo, OpcionesRapidas());

        var transmision = await vigilante.PedirAntenaAsync("prueba");

        var sueltas = new List<Task>
        {
            Task.Run(async () => await transmision.DisposeAsync()),
            Task.Run(() => vigilante.SoltarYaAsync()),
            Task.Run(() => vigilante.SoltarYaAsync(MotivoDeSuelta.Panico)),
            Task.Run(async () => await transmision.DisposeAsync()),
        };

        await Task.WhenAll(sueltas);

        equipo.PttArriba.Should().BeFalse();
        vigilante.EnAntena.Should().BeFalse();
    }

    [Fact]
    public async Task El_boton_de_panico_baja_el_ptt()
    {
        var equipo = new ControlDeMentira();
        await using var vigilante = new VigilantePtt(equipo, OpcionesRapidas());
        var sueltas = new EsperaDeSueltas(vigilante);

        var transmision = await vigilante.PedirAntenaAsync("prueba");
        await vigilante.SoltarYaAsync();

        equipo.PttArriba.Should().BeFalse();
        transmision.EnAntena.Should().BeFalse();
        (await sueltas.PrimeraAsync(TimeSpan.FromSeconds(10))).Should().Be(MotivoDeSuelta.Panico);
        await transmision.DisposeAsync();
    }

    [Fact]
    public async Task El_panico_manda_bajar_el_ptt_aunque_no_creamos_estar_en_antena()
    {
        // Si el PTT lo subio otro programa, o un camino que no controlamos, el boton de
        // panico tiene que mandar la orden igualmente.
        var equipo = new ControlDeMentira();
        await using var vigilante = new VigilantePtt(equipo, OpcionesRapidas());

        await vigilante.SoltarYaAsync();

        equipo.Ordenes.Should().Contain("ptt=0");
    }

    [Fact]
    public async Task Si_la_via_normal_falla_se_baja_por_una_via_de_emergencia()
    {
        var equipo = new ControlDeMentira();
        var registro = new RegistroDeMentira();
        await using var vigilante = new VigilantePtt(equipo, OpcionesRapidas(), registro);

        var transmision = await vigilante.PedirAntenaAsync("prueba");
        equipo.FallaLaViaNormal = true;
        await transmision.DisposeAsync();

        equipo.PttArriba.Should().BeFalse();
        equipo.BajadasPorEmergencia.Should().Be(1);
        registro.De(LogLevel.Warning).Should()
            .Contain(mensaje => mensaje.Contains("vía normal", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Si_fallan_todas_las_vias_se_avisa_a_gritos()
    {
        var equipo = new ControlDeMentira();
        var registro = new RegistroDeMentira();
        await using var vigilante = new VigilantePtt(equipo, OpcionesRapidas(), registro);
        var sueltas = new EsperaDeSueltas(vigilante);

        var transmision = await vigilante.PedirAntenaAsync("prueba");
        equipo.FallaLaViaNormal = true;
        equipo.FallanTodasLasVias = true;

        var accion = async () => await transmision.DisposeAsync();

        await accion.Should().ThrowAsync<PttPegadoException>();
        var avisado = await sueltas.PegadoAsync(TimeSpan.FromSeconds(10));
        avisado.Fallos.Should().HaveCountGreaterThan(1, "hay que dejar constancia de cada vía probada");
        registro.De(LogLevel.Error).Should().NotBeEmpty();

        // Aunque haya fallado, el vigilante queda libre para que el operador pueda reintentar.
        vigilante.EnAntena.Should().BeFalse();
        equipo.FallaLaViaNormal = false;
        equipo.FallanTodasLasVias = false;
        await vigilante.SoltarYaAsync();
        equipo.PttArriba.Should().BeFalse();
    }

    [Fact]
    public async Task No_se_puede_pedir_antena_dos_veces()
    {
        var equipo = new ControlDeMentira();
        await using var vigilante = new VigilantePtt(equipo, OpcionesRapidas());

        await using var transmision = await vigilante.PedirAntenaAsync("la primera");
        var accion = () => vigilante.PedirAntenaAsync("la segunda");

        await accion.Should().ThrowAsync<InvalidOperationException>();
        equipo.Subidas.Should().Be(1);
    }

    [Fact]
    public async Task Si_falla_al_subir_el_ptt_no_queda_en_antena()
    {
        var equipo = new ControlDeMentira { FallaAlSubir = true };
        await using var vigilante = new VigilantePtt(equipo, OpcionesRapidas());

        var accion = () => vigilante.PedirAntenaAsync("prueba");

        await accion.Should().ThrowAsync<IOException>();
        vigilante.EnAntena.Should().BeFalse();
        equipo.PttArriba.Should().BeFalse();

        // Y se ha mandado bajar por si el equipo se hubiera quedado arriba.
        equipo.Ordenes.Should().Contain("ptt=0");
    }

    [Fact]
    public async Task Tras_soltar_el_latido_no_revive_la_transmision()
    {
        var equipo = new ControlDeMentira();
        await using var vigilante = new VigilantePtt(equipo, OpcionesRapidas());

        var transmision = await vigilante.PedirAntenaAsync("prueba");
        await transmision.DisposeAsync();

        transmision.Latir();

        transmision.EnAntena.Should().BeFalse();
        equipo.PttArriba.Should().BeFalse();
        equipo.Subidas.Should().Be(1);
    }

    [Fact]
    public async Task Cien_ciclos_seguidos_dejan_la_cuenta_cuadrada()
    {
        var equipo = new ControlDeMentira();
        await using var vigilante = new VigilantePtt(equipo, OpcionesRapidas());

        for (var vuelta = 0; vuelta < 100; vuelta++)
        {
            await using var transmision = await vigilante.PedirAntenaAsync($"vuelta {vuelta}");
            transmision.Latir();
        }

        equipo.Subidas.Should().Be(100);
        equipo.Bajadas.Should().Be(100);
        equipo.PttArriba.Should().BeFalse();
    }

    [Fact]
    public async Task Muchos_hilos_peleandose_no_dejan_el_ptt_puesto()
    {
        var equipo = new ControlDeMentira();
        await using var vigilante = new VigilantePtt(equipo, OpcionesRapidas(tiempoMaximoMs: 300));

        for (var vuelta = 0; vuelta < 20; vuelta++)
        {
            var tareas = new List<Task>();
            for (var hilo = 0; hilo < 8; hilo++)
            {
                tareas.Add(Task.Run(async () =>
                {
                    try
                    {
                        var transmision = await vigilante.PedirAntenaAsync("pelea");
                        transmision.Latir();
                        await Task.Delay(Random.Shared.Next(0, 15));
                        await transmision.DisposeAsync();
                    }
                    catch (InvalidOperationException)
                    {
                        // Otro hilo se llevó la antena: es lo correcto, solo puede haber una.
                    }
                }));
            }

            tareas.Add(Task.Run(() => vigilante.SoltarYaAsync()));
            await Task.WhenAll(tareas);
        }

        await EsperarAQue(() => !equipo.PttArriba);
        equipo.PttArriba.Should().BeFalse("después de la tormenta el PTT tiene que estar abajo");
        vigilante.EnAntena.Should().BeFalse();
    }

    [Fact]
    public async Task Liberar_el_vigilante_baja_el_ptt()
    {
        var equipo = new ControlDeMentira();
        var vigilante = new VigilantePtt(equipo, OpcionesRapidas());
        var transmision = await vigilante.PedirAntenaAsync("prueba");

        await vigilante.DisposeAsync();

        equipo.PttArriba.Should().BeFalse();
        transmision.EnAntena.Should().BeFalse();
        await transmision.DisposeAsync();
    }

    [Fact]
    public async Task El_vigilante_liberado_no_deja_pedir_antena()
    {
        var equipo = new ControlDeMentira();
        var vigilante = new VigilantePtt(equipo, OpcionesRapidas());
        await vigilante.DisposeAsync();

        var accion = () => vigilante.PedirAntenaAsync("prueba");

        await accion.Should().ThrowAsync<ObjectDisposedException>();
        equipo.Subidas.Should().Be(0);
    }

    [Fact]
    public void No_se_admiten_tiempos_absurdos()
    {
        var opciones = new OpcionesDelVigilante();

        var pasarse = () => opciones.TiempoMaximo = TimeSpan.FromHours(1);
        var negativo = () => opciones.TiempoSinLatido = TimeSpan.FromSeconds(-1);

        pasarse.Should().Throw<ArgumentOutOfRangeException>();
        negativo.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Los_tiempos_de_partida_dan_para_operar()
    {
        var opciones = new OpcionesDelVigilante();

        // Una transmision de FT8 dura unos 13 segundos: tiene que caber sin latir.
        opciones.TiempoSinLatido.Should().BeGreaterThan(TimeSpan.FromSeconds(13));

        // Y una llamada larga de SSB tiene que caber de sobra, pero no eternizarse.
        opciones.TiempoMaximo.Should().BeGreaterThan(TimeSpan.FromMinutes(1));
        opciones.TiempoMaximo.Should().BeLessThanOrEqualTo(OpcionesDelVigilante.TiempoMaximoPermitido);
    }
}
