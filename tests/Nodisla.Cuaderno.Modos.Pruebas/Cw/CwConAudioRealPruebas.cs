using FluentAssertions;
using Nodisla.Cuaderno.Modos.Banco;
using Nodisla.Cuaderno.Modos.Cw;
using Nodisla.Cuaderno.Modos.Pruebas.Banco;
using Nodisla.Cuaderno.Modos.Senal;

namespace Nodisla.Cuaderno.Modos.Pruebas.Cw;

/// <summary>
/// El decodificador de CW con audio de verdad del FT-710 de EA8DLF (grabado el 2-10-2026 en
/// modo compartido, con el equipo en recepción y sin señal de CW en el filtro): el ruido real de
/// la banda, con el AGC del equipo y su filtro de 500 Hz, a cualquier nivel de entrada.
/// </summary>
/// <remarks>
/// <para>
/// <c>cw-ruido-ft710-2026-10-02-a.wav</c> (20 s): empieza con un segundo de ceros (la tarjeta al
/// abrirse) y el nivel sube de golpe; es lo que hacía escribir chorros de «E I T S».
/// <c>-b.wav</c> (60 s): ruido de banda con el AGC trabajando. Los dos, a 8 kHz y 16 bits.
/// </para>
/// <para>
/// Todavía no hay grabación con CW en el aire: la independencia del nivel se comprueba metiendo
/// CW sintética en este ruido real.
/// </para>
/// </remarks>
public class CwConAudioRealPruebas
{
    private static readonly double[] Ganancias = [-40, -30, -20, -10, 0, 10, 20, 30];

    [Theory]
    [InlineData("cw-ruido-ft710-2026-10-02-a.wav")]
    [InlineData("cw-ruido-ft710-2026-10-02-b.wav")]
    [InlineData("ft8-20m-2026-09-27-151745.wav")]
    public void ElRuidoRealNoEscribeANingunNivel(string fichero)
    {
        var wav = LectorWav.Leer(Path.Combine(AppContext.BaseDirectory, "Aire", fichero));
        foreach (var db in Ganancias)
        {
            var k = (float)Math.Pow(10, db / 20);
            var audio = wav.Muestras.Select(v => Math.Clamp(v * k, -1f, 1f)).ToArray();
            var textos = BancoCw.DecodificarTodo(audio, wav.FrecuenciaDeMuestreo, new OpcionesCw { CanalesMaximos = 8 });
            var escrito = string.Concat(textos.Values).Replace(" ", string.Empty, StringComparison.Ordinal);
            escrito.Should().BeEmpty($"{fichero} a {db:+0;-0} dB es solo ruido: «{string.Join(" | ", textos.Values)}»");
        }
    }

    [Fact]
    public void LaCopiaNoDependeDelNivelDeEntrada()
    {
        // CW a 20 WPM metida en el ruido real del FT-710 (6 dB sobre el ruido de todo el filtro),
        // y el conjunto escalado de −40 a +10 dB (50 dB; a +10 dB ya recorta).
        var ruido = LectorWav.Leer(Path.Combine(AppContext.BaseDirectory, "Aire", "cw-ruido-ft710-2026-10-02-b.wav"));
        var fs = ruido.FrecuenciaDeMuestreo;
        var texto = BancoCw.Textos[1];
        var cw = SintetizadorCw.Generar(texto, 720, fs, new ManeraDeManipular(20), 1, new Random(3), 1, 1);
        var potencia = ruido.Muestras.Select(v => (double)v * v).Average();
        var amplitud = Math.Sqrt(potencia * Math.Pow(10, 6 / 10.0) * 2);
        var esperado = BancoCw.Normalizar(texto);

        var cers = new List<double>();
        foreach (var db in new double[] { -40, -30, -20, -10, 0, 10 })
        {
            var k = Math.Pow(10, db / 20);
            var mezcla = new float[Math.Min(cw.Length, ruido.Muestras.Length)];
            for (var i = 0; i < mezcla.Length; i++) mezcla[i] = (float)Math.Clamp(k * (ruido.Muestras[i] + (amplitud * cw[i])), -1, 1);
            var (leido, _) = BancoCw.Decodificar(mezcla, fs, new OpcionesCw { CanalesMaximos = 1 });
            cers.Add(100.0 * BancoCw.Distancia(esperado, BancoCw.Normalizar(leido)) / BancoCw.Longitud(esperado));
        }

        cers.Should().OnlyContain(c => c < 8, $"de −40 a +10 dB se copia igual: {string.Join(" / ", cers.Select(c => c.ToString("0.0")))}");
        (cers.Max() - cers.Min()).Should().BeLessThan(6);
    }

    [Fact]
    public void ElQrnNoEscribe()
    {
        // Chasquidos de 2 a 8 ms, cada 40 a 400 ms, sobre ruido; con la ganancia alta y recorte.
        var azar = new Random(77);
        var audio = new float[8000 * 60];
        GeneradorDeSenal.AnadirRuido(audio, 0.01, 0, 8000, azar);
        var i = 0;
        while (i < audio.Length)
        {
            i += 320 + azar.Next(3200);
            var largo = 16 + azar.Next(48);
            var fuerza = 0.2 + azar.NextDouble();
            for (var j = 0; j < largo && i + j < audio.Length; j++) audio[i + j] += (float)(fuerza * ((azar.NextDouble() * 2) - 1));
        }

        foreach (var db in new double[] { 0, 20 })
        {
            var k = (float)Math.Pow(10, db / 20);
            var escalado = audio.Select(v => Math.Clamp(v * k, -1f, 1f)).ToArray();
            var textos = BancoCw.DecodificarTodo(escalado, 8000, new OpcionesCw { CanalesMaximos = 8 });
            string.Concat(textos.Values).Trim().Should().BeEmpty($"QRN a {db} dB: «{string.Join(" | ", textos.Values)}»");
        }
    }

    [Fact]
    public void ElTextoDeRuidoSeReconoce()
    {
        static List<IReadOnlyList<string>> P(string t) =>
            t.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => (IReadOnlyList<string>)BancoCw.Simbolos(w)).ToList();

        // Lo que Jose vio con la ganancia de RF alta.
        CanalCw.PareceRuido(P("N IT SNAIEEDIET E E ET CKTM G E I ETAEEET EERT IEI TTEA")).Should().BeTrue();
        CanalCw.PareceRuido(P("E T N HN E T")).Should().BeTrue();
        CanalCw.PareceRuido(P("EA IE E T")).Should().BeTrue();

        // Y contactos de verdad, con muchas E y T, no.
        CanalCw.PareceRuido(P("TEST DE OH2BH OH2BH TEST")).Should().BeFalse();
        CanalCw.PareceRuido(P("CQ CQ DE EA8DLF EA8DLF K")).Should().BeFalse();
        CanalCw.PareceRuido(P("R TU 5NN 73 E E")).Should().BeFalse();
    }

    // ── AUTO con enganche ────────────────────────────────────────────────

    [Fact]
    public void EnganchadoNoSaltaAUnaSenalMasFuerteNiConPausasNiConQsb()
    {
        var azar = new Random(8);
        // A: 650 Hz, 20 WPM, con QSB de 10 dB y pausas largas entre palabras.
        var a = SintetizadorCw.Generar("CQ CQ DE EA8DLF EA8DLF K   CQ CQ DE EA8DLF K   CQ DE EA8DLF K", 650, 8000,
            new ManeraDeManipular(20, EspacioEntrePalabras: 12), 0.2, azar, 1, 0, SintetizadorCw.Qsb(10, 0.3));
        // B: 900 Hz, 6 dB más fuerte, empieza a los 8 s.
        var b = SintetizadorCw.Generar("TEST DE OH2BH OH2BH TEST DE OH2BH OH2BH TEST DE OH2BH", 900, 8000, new ManeraDeManipular(26), 0.4, azar, 8, 0);
        var audio = new float[Math.Max(a.Length, b.Length)];
        for (var i = 0; i < audio.Length; i++) audio[i] = (i < a.Length ? a[i] : 0) + (i < b.Length ? b[i] : 0);
        GeneradorDeSenal.AnadirRuido(audio, 0.02, 6, 8000, azar);

        var d = new DecodificadorCw(new OpcionesCw { CanalesMaximos = 1 });
        var principal = new System.Text.StringBuilder();
        var tonos = new List<double>();
        d.TextoDecodificado += (_, t) =>
        {
            if (t.EsPrincipal) principal.Append(t.Texto);
        };
        for (var i = 0; i < a.Length; i += 160)
        {
            d.Alimentar(audio.AsSpan(i, 160), 8000);
            if (i > 8000 * 9 && d.Estado.Anclado) tonos.Add(d.Estado.Principal!.TonoHz);
        }

        tonos.Should().NotBeEmpty();
        tonos.Should().OnlyContain(t => Math.Abs(t - 650) < 40, "anclada a la primera señal, no salta a la más fuerte");
        principal.ToString().Should().Contain("EA8DLF").And.NotContain("OH2BH");
    }

    [Fact]
    public void SinSenalVuelveABuscarYBuscarLaSuelta()
    {
        var azar = new Random(9);
        var a = SintetizadorCw.Generar("CQ CQ DE EA8DLF EA8DLF K", 650, 8000, new ManeraDeManipular(22), 0.3, azar, 1, 0);
        var b = SintetizadorCw.Generar("TEST DE OH2BH OH2BH TEST DE OH2BH OH2BH TEST DE OH2BH OH2BH", 900, 8000, new ManeraDeManipular(22), 0.3, azar, 1, 0);
        var audio = new float[a.Length + (8000 * 25)];
        for (var i = 0; i < audio.Length; i++)
        {
            if (i < a.Length) audio[i] += a[i];
            var j = i - (a.Length + 8000);
            if (j >= 0 && j < b.Length) audio[i] += b[j];
        }

        GeneradorDeSenal.AnadirRuido(audio, 0.045, 10, 8000, azar);
        var d = new DecodificadorCw(new OpcionesCw { CanalesMaximos = 1, SegundosSinSenalParaBuscar = 5 });
        var texto = new System.Text.StringBuilder();
        d.TextoDecodificado += (_, t) => texto.Append(t.Texto);
        double? tonoAlAcabarA = null;
        for (var i = 0; i < audio.Length; i += 160)
        {
            d.Alimentar(audio.AsSpan(i, Math.Min(160, audio.Length - i)), 8000);
            if (tonoAlAcabarA is null && i > a.Length) tonoAlAcabarA = d.Estado.Principal!.TonoHz;
        }

        tonoAlAcabarA.Should().BeApproximately(650, 40);
        d.Estado.Principal!.TonoHz.Should().BeApproximately(900, 40, "tras 5 s sin la primera, busca y se va a la otra");
        texto.ToString().Should().Contain("EA8DLF").And.Contain("OH2BH");

        // «Buscar» suelta el ancla en el acto.
        d.Buscar();
        d.Alimentar(new float[1600], 8000);
        d.Estado.Anclado.Should().BeFalse();
    }
}
