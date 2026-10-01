using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Modos.Banco;
using Nodisla.Cuaderno.Modos.Ft8;
using Nodisla.Cuaderno.Modos.Ldpc;
using Nodisla.Cuaderno.Modos.Modem;

namespace Nodisla.Cuaderno.Modos.Pruebas;

/// <summary>
/// El módem escuchando «en vivo»: bloques de audio que llegan por el evento de la entrada, con
/// su instante, se acumulan, se cortan por ventanas y se decodifican. Es el camino que recorre
/// el audio del FT-710 en producción y que las pruebas por fichero no tocan.
/// </summary>
/// <remarks>
/// Se reproduce el caso real de Jose sin abrir ninguna tarjeta: el flujo nace a 44100 Hz (lo que
/// entrega su codec), se sube a 96000 Hz con la misma interpolación lineal que usa la captura
/// (la frecuencia de sus ajustes), llega en bloques de 50 ms y la escucha empieza en mitad de
/// una ventana, que es lo normal al pulsar «Escuchar».
/// </remarks>
public class ModemPropioEnVivoPruebas
{
    private static readonly TablasDelProtocolo Tablas = TablasDelProtocolo.Cargar();

    /// <summary>Lo que se espera como mucho a que lleguen las ventanas. No es una medida de tiempo.</summary>
    private static readonly TimeSpan Paciencia = TimeSpan.FromMinutes(3);

    [Fact]
    public async Task ElFlujoEnVivoA96000SubidoDesde44100DecodificaCadaVentana()
    {
        var p = ParametrosDelModo.Ft8;
        var codificador = new Codificador(Tablas);
        const int Codec = DobleRemuestreoPruebas.FrecuenciaDelCodec;
        const int Configurada = DobleRemuestreoPruebas.FrecuenciaConfigurada;

        string[] textos = ["CQ EA8DLF IL18", "EA8DLF EA1ABC IN80"];
        double[] tonos = [1100, 1900];
        const double Db = -8;

        // Seis segundos de ruido antes (se empieza a escuchar a mitad de ventana), dos ventanas
        // con señal y tres segundos de la siguiente para que la segunda se cierre y decodifique.
        var azar = new Random(31);
        var muestrasPorVentana = (int)Math.Round(p.PeriodoSegundos * Codec);
        var flujo = new List<float>();

        var previo = new float[6 * Codec];
        GeneradorDeSenal.AnadirRuido(previo, 0.35 * 0.35 / 2, Db, Codec, azar);
        flujo.AddRange(previo);

        for (var v = 0; v < textos.Length; v++)
        {
            codificador.TryCodificar(textos[v], ModoDelModem.Ft8, out var simbolos, out var motivo).Should().BeTrue(motivo);
            flujo.AddRange(GeneradorDeSenal.Ventana(p, simbolos, tonos[v], 0.1, Db, Codec, azar));
        }

        var cola = new float[3 * Codec];
        GeneradorDeSenal.AnadirRuido(cola, 0.35 * 0.35 / 2, Db, Codec, azar);
        flujo.AddRange(cola);

        var a96000 = DobleRemuestreoPruebas.InterpolacionLinealComoAudioCaptura([.. flujo], Codec, Configurada);

        // El primer bloque empieza 6 s antes del comienzo de la primera ventana con señal.
        var primeraVentana = DateTimeOffset.UnixEpoch.AddSeconds(15 * 4);
        var entrada = new EntradaDeMentira();
        var reloj = new RelojParado(primeraVentana.AddSeconds(-6));

        await using var modem = new ModemPropio(Tablas, reloj, entrada);

        var ventanas = new List<VentanaDecodificada>();
        var segundaLista = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        modem.VentanaLista += (_, v) =>
        {
            lock (ventanas)
            {
                ventanas.Add(v);
                if (ventanas.Count(x => x.Decodificaciones.Count > 0) >= 2) segundaLista.TrySetResult();
            }
        };

        await modem.EscucharAsync(ModoDelModem.Ft8);
        entrada.Emitir(a96000, Configurada, primeraVentana.AddSeconds(-6), TimeSpan.FromMilliseconds(50));

        try { await segundaLista.Task.WaitAsync(Paciencia); }
        catch (TimeoutException) { /* Se juzga abajo con lo que haya llegado. */ }

        await modem.PararAsync();

        IReadOnlyList<VentanaDecodificada> vistas;
        lock (ventanas) vistas = [.. ventanas];

        var textosVistos = vistas.SelectMany(v => v.Decodificaciones).Select(d => d.Texto).ToList();
        textosVistos.Should().BeEquivalentTo(textos,
            "las dos ventanas con señal tienen que decodificar por el camino en vivo (ventanas recibidas: " +
            string.Join("; ", vistas.Select(v => $"{v.VentanaUtc:HH:mm:ss}→{v.Decodificaciones.Count}")) + ")");

        // Y cada mensaje se atribuye a SU ventana, no a la de al lado.
        vistas.Single(v => v.Decodificaciones.Any(d => d.Texto == textos[0])).VentanaUtc.Should().Be(primeraVentana);
        vistas.Single(v => v.Decodificaciones.Any(d => d.Texto == textos[1])).VentanaUtc.Should().Be(primeraVentana.AddSeconds(15));
    }

    /// <summary>Una entrada que suelta por el evento los bloques que se le den, con su instante.</summary>
    private sealed class EntradaDeMentira : IEntradaDeAudio
    {
        public IReadOnlyList<DispositivoDeAudio> Dispositivos { get; } = [];

        public DispositivoDeAudio? Abierto => null;

        public double Nivel => 0;

        public event EventHandler<BloqueDeAudio>? BloqueCapturado;

        public event EventHandler<HuecoDeAudio>? MuestrasPerdidas;

        public void Emitir(float[] muestras, int frecuencia, DateTimeOffset inicio, TimeSpan porBloque)
        {
            var porBloqueMuestras = (int)Math.Round(porBloque.TotalSeconds * frecuencia);
            for (var desde = 0; desde + porBloqueMuestras <= muestras.Length; desde += porBloqueMuestras)
            {
                var instante = inicio.AddSeconds((double)desde / frecuencia);
                BloqueCapturado?.Invoke(this, new BloqueDeAudio(
                    muestras.AsMemory(desde, porBloqueMuestras).ToArray(), frecuencia, instante));
            }

            _ = MuestrasPerdidas;
        }

        public Task AbrirAsync(string idDispositivo, int frecuenciaDeMuestreo = 48000, CancellationToken ct = default) => Task.CompletedTask;

        public Task CerrarAsync(CancellationToken ct = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RelojParado(DateTimeOffset ahora) : IRelojDelModem
    {
        public DateTimeOffset Ahora { get; } = ahora;

        public DesvioDelReloj Desvio { get; } = new(0, "pruebas", DateTimeOffset.UnixEpoch, EsFiable: true);

        public EstadoDelReloj Estado => new(Desvio, CalidadDelReloj.Bien, "En hora.", string.Empty, "0 ms");

        public event EventHandler<EstadoDelReloj>? DesvioMedido;

        public Task<DesvioDelReloj> MedirAsync(bool forzar = false, CancellationToken ct = default)
        {
            DesvioMedido?.Invoke(this, Estado);
            return Task.FromResult(Desvio);
        }

        public DateTimeOffset ProximaVentana(TimeSpan periodo) => Ahora + periodo;
    }
}
