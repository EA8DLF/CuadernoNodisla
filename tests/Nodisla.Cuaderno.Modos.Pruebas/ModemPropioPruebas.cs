using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Modos.Ldpc;
using Nodisla.Cuaderno.Modos.Modem;

namespace Nodisla.Cuaderno.Modos.Pruebas;

/// <summary>
/// Pruebas del modem completo.
/// </summary>
/// <remarks>
/// <b>Aqui no se transmite nunca.</b> El modem se construye sin salida de audio y sin vigilante
/// de PTT, que es exactamente la situacion en que tiene que negarse a emitir. La transmision se
/// comprueba guardando en un fichero lo que se habria emitido y volviendoselo a dar al
/// decodificador: cierra el circulo entero sin que el equipo llegue a ponerse en antena.
/// </remarks>
public class ModemPropioPruebas
{
    private static readonly TablasDelProtocolo Tablas = TablasDelProtocolo.DePruebas();

    /// <summary>Un reloj de mentira que no se desvia, para poder probar sin red ni radio.</summary>
    private sealed class RelojDePruebas : IRelojDelModem
    {
        public DateTimeOffset Ahora { get; set; } = DateTimeOffset.UnixEpoch;
        public DesvioDelReloj Desvio { get; } = new(0, "pruebas", DateTimeOffset.UnixEpoch, EsFiable: true);
        public event EventHandler<DesvioDelReloj>? DesvioMedido;
        public Task<DesvioDelReloj> MedirAsync(CancellationToken ct = default)
        {
            DesvioMedido?.Invoke(this, Desvio);
            return Task.FromResult(Desvio);
        }
        public DateTimeOffset ProximaVentana(TimeSpan periodo)
        {
            var ticks = Ahora.UtcTicks;
            return new DateTimeOffset(ticks - (ticks % periodo.Ticks) + periodo.Ticks, TimeSpan.Zero);
        }
    }

    [Fact]
    public async Task SinSalidaDeAudioNiVigilanteSeNiegaAEmitir()
    {
        // Emitir un periodo de FT8 son trece segundos con el equipo en antena. Un modem que se
        // las apanara para emitir sin vigilante acabaria dejando el PTT pegado alguna vez.
        await using var modem = new ModemPropio(Tablas, new RelojDePruebas());

        var emitir = async () => await modem.EmitirAsync("CQ EA8DLF IL18", 1500);

        await emitir.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no puede emitir*");
        modem.EstaEmitiendo.Should().BeFalse();
    }

    [Fact]
    public async Task SinEntradaDeAudioSeNiegaAEscuchar()
    {
        await using var modem = new ModemPropio(Tablas, new RelojDePruebas());
        var escuchar = async () => await modem.EscucharAsync(ModoDelModem.Ft8);
        await escuchar.Should().ThrowAsync<InvalidOperationException>();
        modem.EstaEscuchando.Should().BeFalse();
    }

    [Fact]
    public async Task LoQueSeHabriaEmitidoSeVuelveADecodificarDeUnFichero()
    {
        // La prueba que cierra el circulo entero pasando por disco: se sintetiza la ventana que
        // se habria puesto en el aire, se guarda como WAV y se decodifica ese fichero.
        await using var modem = new ModemPropio(Tablas, new RelojDePruebas());
        const string Texto = "CQ EA8DLF IL18";
        var ruta = Path.Combine(Path.GetTempPath(), $"nodisla-emision-{Guid.NewGuid():N}.wav");

        try
        {
            modem.GuardarEmisionEnFichero(ruta, Texto, 1200, ModoDelModem.Ft8);
            File.Exists(ruta).Should().BeTrue();

            var decodificaciones = await modem.DecodificarFicheroAsync(ruta, ModoDelModem.Ft8);

            decodificaciones.Should().ContainSingle();
            decodificaciones[0].Texto.Should().Be(Texto);
            decodificaciones[0].TonoHz.Should().BeCloseTo(1200, 4);
            decodificaciones[0].EsCq.Should().BeTrue();
            decodificaciones[0].Llamante.Valor.Should().Be("EA8DLF");
            decodificaciones[0].Locator.Valor.Should().Be("IL18");
        }
        finally
        {
            if (File.Exists(ruta)) File.Delete(ruta);
        }
    }

    [Fact]
    public async Task UnFicheroConVariasVentanasSeRecorreEntero()
    {
        await using var modem = new ModemPropio(Tablas, new RelojDePruebas());
        var p = Ft8.ParametrosDelModo.Ft8;
        const int Frecuencia = 48000;
        var muestrasPorVentana = (int)(p.PeriodoSegundos * Frecuencia);

        string[] textos = ["CQ EA8DLF IL18", "EA8DLF EA1ABC IN80"];
        var completo = new float[muestrasPorVentana * textos.Length];
        var ruta = Path.Combine(Path.GetTempPath(), $"nodisla-varias-{Guid.NewGuid():N}.wav");
        var parcial = Path.Combine(Path.GetTempPath(), $"nodisla-parcial-{Guid.NewGuid():N}.wav");

        try
        {
            for (var v = 0; v < textos.Length; v++)
            {
                modem.GuardarEmisionEnFichero(parcial, textos[v], 1400, ModoDelModem.Ft8, Frecuencia);
                var una = Senal.LectorWav.Leer(parcial);
                una.Muestras.AsSpan(0, muestrasPorVentana).CopyTo(completo.AsSpan(v * muestrasPorVentana));
            }
            Senal.LectorWav.Escribir(ruta, completo, Frecuencia);

            var decodificaciones = await modem.DecodificarFicheroAsync(ruta, ModoDelModem.Ft8);

            decodificaciones.Select(d => d.Texto).Should().BeEquivalentTo(textos);
            decodificaciones.Select(d => d.VentanaUtc).Should().OnlyHaveUniqueItems(
                "cada mensaje pertenece a la ventana en que sonó");
        }
        finally
        {
            foreach (var f in new[] { ruta, parcial }) if (File.Exists(f)) File.Delete(f);
        }
    }

    [Fact]
    public async Task ElModemDiceQueTrabajaConElCodigoDePruebas()
    {
        // Mientras no este la tabla de verdad, el modem no puede dar a entender que decodifica
        // a otras estaciones. Que lo diga es parte del encargo, no un detalle.
        await using var modem = new ModemPropio(Tablas, new RelojDePruebas());
        modem.Tablas.EsElCodigoReal.Should().BeFalse();
        modem.Tablas.Procedencia.Should().Contain("prueba");
    }
}
