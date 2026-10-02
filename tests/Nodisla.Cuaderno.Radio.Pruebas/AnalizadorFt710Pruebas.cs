using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Radio.Espectro;

namespace Nodisla.Cuaderno.Radio.Pruebas;

/// <summary>
/// El analizador de espectro del propio FT-710: la trama del puente FT4222 y el bucle que la lee.
/// </summary>
/// <remarks>
/// Las tramas reales del equipo de EA8DLF estan en <c>Capturas/espectro-ft710-*.bin</c>; las
/// de mentira se construyen aqui con la misma disposicion (docs/14-espectro-ft710.md). Nada
/// depende del reloj: el bucle se prueba con un puente en memoria.
/// </remarks>
public class AnalizadorFt710Pruebas
{
    /// <summary>Trama de mentira: 21.073.300 Hz, CENTER, span 200 kHz, una señal en el centro.</summary>
    internal static byte[] TramaDeMentira(long vfoHz = 21_073_300, byte span = 7, byte modo = 0, uint inicioFijo = 0)
    {
        var trama = new byte[TramaDelAnalizadorFt710.Largo];
        for (var i = 0; i < TramaDelAnalizadorFt710.Puntos; i++)
        {
            // Nivel 20 de fondo, 200 en el punto central; viajan invertidos.
            trama[i] = (byte)~(i == 425 ? 200 : 20);
        }

        var e = TramaDelAnalizadorFt710.InicioDelEstado;
        trama[e + 32] = span;
        trama[e + 33] = 0x10;
        trama[e + 52] = modo;
        var digitos = vfoHz.ToString("D10", System.Globalization.CultureInfo.InvariantCulture);
        for (var i = 0; i < 5; i++)
        {
            trama[e + 64 + i] = (byte)(((digitos[2 * i] - '0') << 4) | (digitos[(2 * i) + 1] - '0'));
        }

        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(trama.AsSpan(e + 132), (uint)vfoHz);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(trama.AsSpan(e + 144), inicioFijo);
        TramaDelAnalizadorFt710.Cola.CopyTo(trama.AsSpan(TramaDelAnalizadorFt710.Largo - 4));
        return trama;
    }

    [Fact]
    public void La_trama_en_center_da_la_escala_alrededor_del_vfo()
    {
        TramaDelAnalizadorFt710.IntentarDescifrar(TramaDeMentira(), out var traza).Should().BeTrue();

        traza!.VfoHz.Should().Be(21_073_300);
        traza.SpanHz.Should().Be(200_000);
        traza.Modo.Should().Be(ModoDelAnalizador.Centro);
        traza.InicioHz.Should().Be(20_973_300);
        traza.FinHz.Should().Be(21_173_300);
        traza.Velocidad.Should().Be(1);
        traza.Niveles.Should().HaveCount(850);
        traza.Niveles[425].Should().Be(200, "los niveles llegan invertidos y se deshacen");
        traza.Niveles[0].Should().Be(20);
    }

    [Fact]
    public void En_fix_la_escala_empieza_donde_dice_el_equipo()
    {
        TramaDelAnalizadorFt710.IntentarDescifrar(
            TramaDeMentira(span: 9, modo: 2, inicioFijo: 7_000_000), out var traza).Should().BeTrue();

        traza!.Modo.Should().Be(ModoDelAnalizador.Fijo);
        traza.InicioHz.Should().Be(7_000_000);
        traza.FinHz.Should().Be(8_000_000);
    }

    [Fact]
    public void Una_trama_sin_la_marca_final_no_se_descifra()
    {
        var trama = TramaDeMentira();
        trama[^1] = 0;
        TramaDelAnalizadorFt710.IntentarDescifrar(trama, out _).Should().BeFalse();
        TramaDelAnalizadorFt710.IntentarDescifrar(trama.AsSpan(0, 100), out _).Should().BeFalse();
    }

    [Fact]
    public void El_bcd_se_lee_en_hercios_y_rechaza_cifras_imposibles()
    {
        TramaDelAnalizadorFt710.LeerBcd([0x00, 0x21, 0x07, 0x33, 0x00]).Should().Be(21_073_300);
        TramaDelAnalizadorFt710.LeerBcd([0x0A, 0x00]).Should().Be(-1);
    }

    [Fact]
    public void Las_tramas_reales_guardadas_se_descifran()
    {
        var carpeta = Path.Combine(AppContext.BaseDirectory, "Capturas");
        var ficheros = Directory.Exists(carpeta)
            ? Directory.GetFiles(carpeta, "espectro-ft710-*.bin")
            : [];

        foreach (var fichero in ficheros)
        {
            var bytes = File.ReadAllBytes(fichero);
            TramaDelAnalizadorFt710.IntentarDescifrar(bytes, out var traza)
                .Should().BeTrue($"{Path.GetFileName(fichero)} es una trama real del equipo");
            traza!.VfoHz.Should().BeInRange(30_000, 75_000_000);
            traza.SpanHz.Should().BeGreaterThan(0);
            (traza.FinHz - traza.InicioHz).Should().Be(traza.SpanHz);
        }
    }

    [Theory]
    [InlineData("span50k", 50_000, ModoDelAnalizador.Centro, 1, false, false)]
    [InlineData("slow1", 50_000, ModoDelAnalizador.Centro, 0, false, false)]
    [InlineData("cursor", 50_000, ModoDelAnalizador.Cursor, 0, false, false)]
    [InlineData("fix", 100_000, ModoDelAnalizador.Fijo, 0, false, false)]
    [InlineData("expand", 50_000, ModoDelAnalizador.Centro, 0, false, true)]
    [InlineData("3dss", 50_000, ModoDelAnalizador.Centro, 0, true, false)]
    public void Las_tramas_de_la_radio_corridas_un_bit_se_enderezan_y_traen_todo_el_ajuste(
        string modo, int span, ModoDelAnalizador posicion, int velocidad, bool tresD, bool ampliado)
    {
        // Leidas del FT-710 real el 29-09-2026 tras cambiar SPAN/SPEED/SS06 por CAT: llegan
        // retrasadas un bit y sin enderezar ninguna trama valia (el analizador se congelaba).
        var cruda = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Capturas", $"analizador-ft710-{modo}.bin"));
        TramaDelAnalizadorFt710.EsValida(cruda).Should().BeFalse();
        var enderezada = new byte[TramaDelAnalizadorFt710.Largo];
        TramaDelAnalizadorFt710.IntentarEnderezar(cruda, enderezada, out var bits).Should().BeTrue();
        bits.Should().Be(1);

        TramaDelAnalizadorFt710.IntentarDescifrar(cruda, out var traza).Should().BeTrue();
        traza!.SpanHz.Should().Be(span);
        traza.Modo.Should().Be(posicion);
        traza.Velocidad.Should().Be(velocidad);
        traza.TresD.Should().Be(tresD);
        traza.Ampliado.Should().Be(ampliado);
        traza.VfoHz.Should().Be(18_128_100);
        (traza.FinHz - traza.InicioHz).Should().Be(span);
        if (posicion == ModoDelAnalizador.Fijo) traza.InicioHz.Should().Be(18_068_000);
    }

    [Fact]
    public void Enderezar_deja_igual_una_trama_que_ya_viene_bien_y_rechaza_la_basura()
    {
        var buena = TramaDeMentira();
        var destino = new byte[TramaDelAnalizadorFt710.Largo];
        TramaDelAnalizadorFt710.IntentarEnderezar(buena, destino, out var bits).Should().BeTrue();
        bits.Should().Be(0);
        destino.Should().Equal(buena);

        TramaDelAnalizadorFt710.IntentarEnderezar(new byte[TramaDelAnalizadorFt710.Largo], destino, out _).Should().BeFalse();
    }

    [Fact]
    public async Task El_bucle_se_sincroniza_y_entrega_trazas()
    {
        var basura = new byte[37];
        var sincronia = Enumerable.Repeat(TramaDelAnalizadorFt710.Cola.ToArray(), 4).SelectMany(b => b).ToArray();
        var puente = new PuenteEnMemoria([.. basura, .. sincronia, .. TramaDeMentira(), .. TramaDeMentira(7_074_000)]);

        var recibidas = new List<TrazaDeEspectro>();
        var dos = new TaskCompletionSource();
        await using var analizador = new AnalizadorFt710(
            () => (AperturaDelPuente.Abierto, puente, "en memoria"), TimeSpan.FromMilliseconds(1));
        analizador.TrazaRecibida += (_, t) =>
        {
            lock (recibidas)
            {
                recibidas.Add(t);
                if (recibidas.Count == 2) dos.TrySetResult();
            }
        };

        analizador.Iniciar();
        await dos.Task.WaitAsync(TimeSpan.FromSeconds(10));

        recibidas[0].VfoHz.Should().Be(21_073_300);
        recibidas[1].VfoHz.Should().Be(7_074_000);
    }

    [Fact]
    public async Task Sin_marca_dice_que_la_radio_no_manda_su_espectro()
    {
        var puente = new PuenteEnMemoria(new byte[AnalizadorFt710.TopeDeSincronia + 10]);
        var sinTramas = new TaskCompletionSource();
        await using var analizador = new AnalizadorFt710(
            () => (AperturaDelPuente.Abierto, puente, "en memoria"), TimeSpan.FromHours(1));
        analizador.EstadoCambiado += (_, _) =>
        {
            if (analizador.Estado == EstadoDelAnalizador.SinTramas) sinTramas.TrySetResult();
        };

        analizador.Iniciar();
        await sinTramas.Task.WaitAsync(TimeSpan.FromSeconds(10));

        analizador.Motivo.Should().Contain("SCU-LAN10");
    }

    [Fact]
    public async Task Sin_biblioteca_lo_dice_y_no_revienta()
    {
        var sinBiblioteca = new TaskCompletionSource();
        await using var analizador = new AnalizadorFt710(
            () => (AperturaDelPuente.SinBiblioteca, null, "No está LibFT4222-64.dll"), TimeSpan.FromHours(1));
        analizador.EstadoCambiado += (_, _) =>
        {
            if (analizador.Estado == EstadoDelAnalizador.SinBiblioteca) sinBiblioteca.TrySetResult();
        };

        analizador.Iniciar();
        await sinBiblioteca.Task.WaitAsync(TimeSpan.FromSeconds(10));

        analizador.Motivo.Should().Contain("LibFT4222");
        await analizador.DetenerAsync();
        analizador.Estado.Should().Be(EstadoDelAnalizador.Parado);
    }

    /// <summary>
    /// 01-10-2026: con el puente FT4222 presente, la apertura se quedo colgada dentro del
    /// controlador de FTDI y el analizador no dijo nada nunca: ni en el registro ni en pantalla
    /// (Parado → Parado no se apunta). Ahora lo dice, no apila otra apertura colgada encima al
    /// ocultar y volver a mostrar, y si la apertura termina tarde se aprovecha.
    /// </summary>
    [Fact]
    public async Task Si_la_apertura_se_cuelga_lo_dice_y_no_apila_otra()
    {
        using var suelta = new ManualResetEventSlim(false);
        var aperturas = 0;
        var sincronia = Enumerable.Repeat(TramaDelAnalizadorFt710.Cola.ToArray(), 4).SelectMany(b => b).ToArray();
        var puente = new PuenteEnMemoria([.. sincronia, .. TramaDeMentira()]);
        var colgada = new TaskCompletionSource();
        var traza = new TaskCompletionSource();

        await using var analizador = new AnalizadorFt710(
            () =>
            {
                Interlocked.Increment(ref aperturas);
                suelta.Wait();
                return (AperturaDelPuente.Abierto, puente, "en memoria");
            },
            TimeSpan.FromHours(1),
            plazoDeApertura: TimeSpan.FromMilliseconds(50));
        analizador.EstadoCambiado += (_, _) =>
        {
            if (analizador.Estado == EstadoDelAnalizador.Fallo) colgada.TrySetResult();
        };
        analizador.TrazaRecibida += (_, _) => traza.TrySetResult();

        analizador.Iniciar();
        await colgada.Task.WaitAsync(TimeSpan.FromSeconds(10));
        analizador.Motivo.Should().Contain("no contesta");

        // Ocultar no se queda esperando a la apertura colgada.
        await analizador.DetenerAsync().WaitAsync(TimeSpan.FromSeconds(3));
        analizador.Estado.Should().Be(EstadoDelAnalizador.Parado);

        // Volver a mostrar espera a la misma apertura, no abre otra encima.
        colgada = new TaskCompletionSource();
        analizador.Iniciar();
        await colgada.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Volatile.Read(ref aperturas).Should().Be(1);

        // Si al final abre, se lee.
        suelta.Set();
        await traza.Task.WaitAsync(TimeSpan.FromSeconds(10));
        analizador.Estado.Should().Be(EstadoDelAnalizador.Recibiendo);
        Volatile.Read(ref aperturas).Should().Be(1);
    }

    private sealed class PuenteEnMemoria(byte[] datos) : IPuenteDelAnalizador
    {
        private int _posicion;

        public bool Leer(Span<byte> destino)
        {
            if (_posicion + destino.Length > datos.Length) return false;
            datos.AsSpan(_posicion, destino.Length).CopyTo(destino);
            _posicion += destino.Length;
            return true;
        }

        public void Dispose()
        {
        }
    }
}
