using FluentAssertions;
using Nodisla.Cuaderno.Modos.Banco;
using Nodisla.Cuaderno.Modos.Cw;
using Nodisla.Cuaderno.Modos.Pruebas.Banco;
using Nodisla.Cuaderno.Modos.Senal;

namespace Nodisla.Cuaderno.Modos.Pruebas.Cw;

/// <summary>El decodificador de telegrafía, pieza a pieza y entero.</summary>
public class DecodificadorCwPruebas
{
    // ── La tabla ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(".-", "A")]
    [InlineData("--..", "Z")]
    [InlineData("-----", "0")]
    [InlineData("..--..", "?")]
    [InlineData("-..-.", "/")]
    [InlineData(".-.-.", "<AR>")]
    [InlineData("...-.-", "<SK>")]
    [InlineData("-...-", "<BT>")]
    [InlineData("-.--.", "<KN>")]
    [InlineData(".-...", "<AS>")]
    [InlineData("--.--", "Ñ")]
    [InlineData(".--.-", "À")]
    [InlineData(".-.-", "Ä")]
    [InlineData("..-..", "É")]
    [InlineData("---.", "Ö")]
    [InlineData("..--", "Ü")]
    [InlineData("----", "CH")]
    public void LaTablaTraduceLetrasNumerosPuntuacionProsignosYEuropeos(string codigo, string texto)
    {
        TablaMorse.TryDecodificar(codigo, out var t).Should().BeTrue();
        t.Should().Be(texto);
    }

    [Fact]
    public void TodoLoQueSeDecodificaSeVuelveACodificarIgual()
    {
        foreach (var (codigo, texto) in TablaMorse.Codigos)
            TablaMorse.Codificar(texto).Should().Be(codigo, texto);
    }

    [Fact]
    public void UnCodigoQueNoExisteNoSeInventa()
    {
        TablaMorse.TryDecodificar("......-", out var t).Should().BeFalse();
        t.Should().BeEmpty();
    }

    [Fact]
    public void LosProsignosEntreAngulosSonUnSoloSimbolo()
    {
        TablaMorse.Simbolos("cq  de ea8dlf <AR>").Should().Equal("C", "Q", " ", "D", "E", " ", "E", "A", "8", "D", "L", "F", " ", "<AR>");
        TablaMorse.ATexto("SOS").Should().Be("... --- ...");
        TablaMorse.Codificar("=").Should().Be("-...-", "el signo igual y <BT> son el mismo código");
    }

    // ── El sintetizador ───────────────────────────────────────────────────

    [Fact]
    public void ElSintetizadorRespetaLosTiemposDeParis()
    {
        // PARIS + espacio de palabra = 50 puntos: a 20 WPM son 3 s exactos.
        var tramos = SintetizadorCw.Tramos("PARIS ", new ManeraDeManipular(20));
        tramos.Sum(t => t.Segundos).Should().BeApproximately(3.0 - (4 * 0.06), 1e-9, "sin el último espacio de palabra, que se recorta");
        tramos.First().Marca.Should().BeTrue();
        tramos.Where(t => t.Marca).Select(t => Math.Round(t.Segundos, 6)).Distinct().Should().BeEquivalentTo([0.06, 0.18]);
    }

    // ── El lector de tiempos, sin audio ──────────────────────────────────

    [Fact]
    public void ElLectorLeeLlaveLimpiaYSigueLaVelocidad()
    {
        var lector = new LectorDeTiempos(5, 60, 20);
        var texto = string.Empty;
        lector.Simbolo += s => texto += s;
        Manipular(lector, "CQ CQ DE EA8DLF EA8DLF K", 35);
        lector.Vaciar();

        BancoCw.Normalizar(texto).Should().EndWith("DE EA8DLF EA8DLF K");
        lector.Wpm.Should().BeApproximately(35, 2.5);
    }

    [Fact]
    public void LasRachasDeRuidoCortoNoCuentan()
    {
        var lector = new LectorDeTiempos();
        var texto = string.Empty;
        lector.Simbolo += s => texto += s;
        // 4 ms de llave abajo cada 100 ms: chasquidos, no telegrafía.
        for (var i = 0; i < 500; i++)
        {
            lector.Paso(i % 50 < 2);
        }

        lector.Vaciar();
        texto.Trim().Should().BeEmpty();
    }

    // ── La envolvente y el buscador de tonos ─────────────────────────────

    [Fact]
    public void ElFiltroDejaPasarElTonoYApartaOtroA300Hz()
    {
        static double Media(double hz)
        {
            var env = new EnvolventeCw(8000, 700, 100);
            double suma = 0;
            var n = 0;
            for (var i = 0; i < 8000; i++)
            {
                if (env.Anadir((float)Math.Sin(2 * Math.PI * hz * i / 8000), out var p) && i > 2000)
                {
                    suma += p;
                    n++;
                }
            }

            return suma / n;
        }

        var dentro = Media(700);
        var fuera = Media(1000);
        dentro.Should().BeApproximately(0.25, 0.02, "un tono de amplitud 1 en el centro da 1/4");
        (10 * Math.Log10(dentro / fuera)).Should().BeGreaterThan(30);
    }

    [Fact]
    public void ElBuscadorEncuentraDosTonosEnLaBanda()
    {
        var buscador = new BuscadorDeTonos(8000);
        var azar = new Random(5);
        var audio = new float[8000 * 3];
        for (var i = 0; i < audio.Length; i++)
            audio[i] = (float)((0.2 * Math.Sin(2 * Math.PI * 650 * i / 8000)) + (0.1 * Math.Sin(2 * Math.PI * 912 * i / 8000)));
        GeneradorDeSenal.AnadirRuido(audio, 0.02, 0, 8000, azar);
        foreach (var x in audio) buscador.Anadir(x);

        var picos = buscador.Picos(300, 1200, 60);
        picos.Should().HaveCountGreaterThanOrEqualTo(2);
        picos[0].Hz.Should().BeApproximately(650, 5);
        picos[1].Hz.Should().BeApproximately(912, 5);
    }

    // ── Entero ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(12)]
    [InlineData(20)]
    [InlineData(30)]
    [InlineData(40)]
    public void DecodificaUnContactoLimpioEnAutomatico(double wpm)
    {
        var azar = new Random((int)wpm);
        var texto = "CQ CQ DE EA8DLF EA8DLF K GM UR RST 599 <BT> 73 <SK>";
        var audio = BancoCw.Senal(BancoCw.Escenario.Limpia, texto, wpm, 10, 730, azar);
        var (leido, medida) = BancoCw.Decodificar(audio, BancoCw.Frecuencia);

        var errores = BancoCw.Distancia(BancoCw.Normalizar(texto), BancoCw.Normalizar(leido));
        errores.Should().BeLessThanOrEqualTo(3, $"leído: «{leido}»");
        medida.Should().BeApproximately(wpm, wpm * 0.15);
    }

    [Fact]
    public void A48kHzSeDiezmaYSeLeeIgual()
    {
        var texto = "TEST DE EA8DLF <KN>";
        var audio = SintetizadorCw.Generar(texto, 680, 48000, new ManeraDeManipular(22), 0.3, null, 1.5, 1.5);
        GeneradorDeSenal.AnadirRuido(audio, 0.045, 10, 48000, new Random(9));
        var (leido, _) = BancoCw.Decodificar(audio, 48000);
        BancoCw.Normalizar(leido).Should().EndWith("DE EA8DLF <KN>");
    }

    [Fact]
    public void ElTonoFijoSeRespetaYElSkimmerLeeLasDosSenales()
    {
        var azar = new Random(17);
        var una = SintetizadorCw.Generar("CQ TEST DE EA8DLF EA8DLF TEST", 600, 8000, new ManeraDeManipular(24), 0.3, azar, 1, 1);
        var otra = SintetizadorCw.Generar("CQ CQ DE DL1ABC DL1ABC K", 950, 8000, new ManeraDeManipular(16), 0.3, azar, 1, 1);
        var audio = new float[Math.Max(una.Length, otra.Length)];
        for (var i = 0; i < audio.Length; i++) audio[i] = (i < una.Length ? una[i] : 0) + (i < otra.Length ? otra[i] : 0);
        GeneradorDeSenal.AnadirRuido(audio, 0.045, 6, 8000, azar);

        var (fijo, _) = BancoCw.Decodificar(audio, 8000, new OpcionesCw { CanalesMaximos = 1 }, 950);
        BancoCw.Normalizar(fijo).Should().Contain("DL1ABC");

        var todos = BancoCw.DecodificarTodo(audio, 8000, new OpcionesCw { CanalesMaximos = 4 });
        var juntos = string.Join(" | ", todos.Values);
        juntos.Should().Contain("EA8DLF").And.Contain("DL1ABC");
    }

    [Fact]
    public void ElRuidoPuroNoEscribeNada() => BancoCwPruebas.RuidoPuro(120).Should().Be(0);

    [Fact]
    public void ElAudioRealDeFt8NoEscribeTextoBasura()
    {
        // No hay grabaciones de telegrafía en el repositorio: la de FT8 sirve para comprobar que
        // el audio real de otro modo (tonos continuos que saltan) no se lee como telegrafía.
        var ruta = Path.Combine(AppContext.BaseDirectory, "Aire", "ft8-20m-2026-09-27-151745.wav");
        var wav = LectorWav.Leer(ruta);
        var textos = BancoCw.DecodificarTodo(wav.Muestras, wav.FrecuenciaDeMuestreo, new OpcionesCw { CanalesMaximos = 8 });
        var escritos = textos.Values.Sum(t => t.Replace(" ", string.Empty, StringComparison.Ordinal).Length);
        escritos.Should().BeLessThanOrEqualTo(2, string.Join(" | ", textos.Values));
    }

    [Fact]
    public void LasOrdenesDesdeOtroHiloSeAplicanEnElSiguienteBloque()
    {
        var decodificador = new DecodificadorCw(new OpcionesCw { CanalesMaximos = 1 });
        decodificador.Alimentar(new float[800], 8000);
        decodificador.FijarTono(812);
        decodificador.Configurar(new OpcionesCw { AnchoDelFiltroHz = 60, CanalesMaximos = 1 });
        decodificador.Alimentar(new float[8000], 8000);

        decodificador.TonoFijoHz.Should().Be(812);
        decodificador.Opciones.AnchoDelFiltroHz.Should().Be(60);
        decodificador.Estado.Principal!.TonoHz.Should().BeApproximately(812, 0.5);

        decodificador.FijarTono(null);
        decodificador.TonoFijoHz.Should().BeNull();
    }

    private static void Manipular(LectorDeTiempos lector, string texto, double wpm)
    {
        foreach (var tramo in SintetizadorCw.Tramos(texto, new ManeraDeManipular(wpm)))
        {
            var cuadros = (int)Math.Round(tramo.Segundos * 1000 / EnvolventeCw.MilisegundosPorCuadro);
            for (var i = 0; i < cuadros; i++) lector.Paso(tramo.Marca);
        }

        for (var i = 0; i < 1000; i++) lector.Paso(false);
    }
}
