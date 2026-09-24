using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Modos.Banco;
using Nodisla.Cuaderno.Modos.Ft8;
using Nodisla.Cuaderno.Modos.Ldpc;
using Nodisla.Cuaderno.Modos.Senal;

namespace Nodisla.Cuaderno.Modos.Pruebas;

/// <summary>Pruebas de la sintesis de la senal que se pondria en el aire.</summary>
public class ModuladorPruebas
{
    private static readonly TablasDelProtocolo Tablas = TablasDelProtocolo.DePruebas();

    [Theory]
    [InlineData(ModoDelModem.Ft8, 12.64)]
    [InlineData(ModoDelModem.Ft4, 5.04)]
    public void LaSenalDuraLoQueTieneQueDurar(ModoDelModem modo, double segundos)
    {
        var p = ParametrosDelModo.De(modo);
        var tonos = new byte[p.SimbolosTotales];

        var senal = Modulador.Sintetizar(p, tonos, 1500, 48000, 0.5);

        ((double)senal.Length / 48000).Should().BeApproximately(segundos, 1e-9);
        p.PeriodoSegundos.Should().BeGreaterThan(segundos, "la señal tiene que caber en su ventana con margen");
    }

    [Fact]
    public void NuncaSePasaDeLaAmplitudPedida()
    {
        // Pasarse recortaría la onda, y una onda recortada ensucia la banda entera: es la
        // estación que todo el mundo oye como un chisporroteo a los lados.
        var p = ParametrosDelModo.Ft8;
        var codificador = new Codificador(Tablas);
        codificador.TryCodificar("CQ EA8DLF IL18", ModoDelModem.Ft8, out var tonos, out _).Should().BeTrue();

        var senal = Modulador.Sintetizar(p, tonos, 1500, 48000, 0.5);

        senal.Max(Math.Abs).Should().BeLessThanOrEqualTo(0.5f);
    }

    [Fact]
    public void EmpiezaYAcabaEnSilencioParaNoChasquear()
    {
        var p = ParametrosDelModo.Ft8;
        var senal = Modulador.Sintetizar(p, new byte[p.SimbolosTotales], 1500, 48000, 0.5);

        Math.Abs(senal[0]).Should().BeLessThan(0.01f);
        Math.Abs(senal[^1]).Should().BeLessThan(0.01f);
    }

    [Fact]
    public void LaSenalCabeEnElAnchoDeBandaDelModo()
    {
        // Una señal de FT8 tiene que caber en 50 Hz. Si se saliera, molestaría a los
        // corresponsales de al lado sin que el operador se enterase nunca.
        var p = ParametrosDelModo.Ft8;
        const int Frecuencia = 12800;
        const double Tono = 1500;
        var codificador = new Codificador(Tablas);
        codificador.TryCodificar("CQ EA8DLF IL18", ModoDelModem.Ft8, out var tonos, out _).Should().BeTrue();

        var senal = Modulador.Sintetizar(p, tonos, Tono, Frecuencia, 0.5);
        var longitud = Fft.PotenciaDeDosQueCubre(senal.Length);
        var magnitudes = new float[(longitud / 2) + 1];
        Fft.MagnitudesDeSenalReal(senal, longitud, magnitudes);

        var hzPorCasilla = (double)Frecuencia / longitud;
        double dentro = 0, fuera = 0;
        for (var i = 0; i < magnitudes.Length; i++)
        {
            var hz = i * hzPorCasilla;
            var potencia = (double)magnitudes[i] * magnitudes[i];
            // El hueco del modo son sus ocho tonos mas un margen de un tono a cada lado.
            if (hz >= Tono - p.EspaciadoDeTonosHz && hz <= Tono + p.AnchoDeBandaHz) dentro += potencia;
            else fuera += potencia;
        }

        (10 * Math.Log10(fuera / dentro)).Should().BeLessThan(-30,
            "fuera del hueco del modo no puede quedar ni la milésima parte de la potencia");
    }

    [Theory]
    [InlineData(ModoDelModem.Ft8)]
    [InlineData(ModoDelModem.Ft4)]
    public void LosGruposDeSincronismoSalenEnSuSitio(ModoDelModem modo)
    {
        var p = ParametrosDelModo.De(modo);
        var codificador = new Codificador(Tablas);
        codificador.TryCodificar("CQ EA8DLF IL18", modo, out var tonos, out _).Should().BeTrue();

        for (var i = 0; i < p.SimbolosTotales; i++)
            if (p.EsSimboloDeSincronismo(i, out var esperado))
                tonos[i].Should().Be((byte)esperado, $"el símbolo {i} es de sincronismo y su tono está fijado");
    }
}

/// <summary>Pruebas de la busqueda del sincronismo dentro de la ventana.</summary>
public class SincronizadorPruebas
{
    private static readonly TablasDelProtocolo Tablas = TablasDelProtocolo.DePruebas();

    [Theory]
    [InlineData(ModoDelModem.Ft8, 800)]
    [InlineData(ModoDelModem.Ft8, 2400)]
    [InlineData(ModoDelModem.Ft4, 1500)]
    public void EncuentraLaSenalDondeEsta(ModoDelModem modo, double tono)
    {
        var p = ParametrosDelModo.De(modo);
        var codificador = new Codificador(Tablas);
        codificador.TryCodificar("CQ EA8DLF IL18", modo, out var tonos, out _).Should().BeTrue();

        var ventana = GeneradorDeSenal.Ventana(p, tonos, tono, 0, 0, (int)p.FrecuenciaDeAnalisis, new Random(13));
        var analisis = AnalisisDeVentana.Calcular(ventana, p);
        var candidatas = Sincronizador.Buscar(analisis, 50);

        candidatas.Should().NotBeEmpty();
        var mejor = candidatas[0];
        analisis.FrecuenciaDe(mejor.Casilla).Should().BeApproximately(tono, p.EspaciadoDeTonosHz,
            "la mejor candidata tiene que caer sobre la señal, con medio tono de tolerancia");

        // El instante: la senal empieza en el segundo 0,5 de la ventana y cada bloque es medio simbolo.
        var segundosDeLaMejor = mejor.Bloque * p.DuracionDeSimboloSegundos / 2;
        segundosDeLaMejor.Should().BeApproximately(p.ComienzoNominalSegundos, p.DuracionDeSimboloSegundos);
    }

    [Fact]
    public void EnUnaVentanaVaciaNoDestacaNada()
    {
        var p = ParametrosDelModo.Ft8;
        var ventana = new float[p.MuestrasDeLaVentana];
        GeneradorDeSenal.AnadirRuido(ventana, 0.05, 0, (int)p.FrecuenciaDeAnalisis, new Random(77));

        var analisis = AnalisisDeVentana.Calcular(ventana, p);
        var candidatas = Sincronizador.Buscar(analisis, 200);

        // Con ruido puro siempre hay algo que se parece un poco al patrón; lo que no puede haber
        // es una candidata que destaque como destaca una señal de verdad.
        candidatas.Should().AllSatisfy(c => c.Puntuacion.Should().BeLessThan(2.2));
    }

    [Fact]
    public void LaBandaBaseDejaCadaTonoEnSuCasilla()
    {
        // Es la pieza que hace rápido al decodificador: recortar los 50 Hz de la candidata en el
        // dominio de la frecuencia deja la señal filtrada, trasladada y remuestreada de una vez.
        var p = ParametrosDelModo.Ft8;
        const double Tono = 1500;
        const int TonoDePrueba = 5;
        var tonos = new byte[p.SimbolosTotales];
        Array.Fill(tonos, (byte)TonoDePrueba);

        var senal = Modulador.Sintetizar(p, tonos, Tono, (int)p.FrecuenciaDeAnalisis, 0.5);
        var ventana = new float[p.MuestrasDeLaVentana];
        senal.AsSpan(0, Math.Min(senal.Length, ventana.Length)).CopyTo(ventana);

        var analisis = AnalisisDeVentana.Calcular(ventana, p);
        var real = new float[analisis.MuestrasDeLaBandaBase];
        var imaginaria = new float[analisis.MuestrasDeLaBandaBase];
        analisis.ExtraerBandaBase(Tono, real, imaginaria);

        // Se analiza un simbolo del medio, lejos de las rampas.
        var n = analisis.MuestrasPorSimboloEnBase;
        var desde = 10 * n;
        var trozoReal = real.AsSpan(desde, n).ToArray();
        var trozoImaginaria = imaginaria.AsSpan(desde, n).ToArray();
        Fft.Transformar(trozoReal, trozoImaginaria);

        var mayor = 0;
        for (var i = 1; i < n; i++)
        {
            var potencia = (trozoReal[i] * trozoReal[i]) + (trozoImaginaria[i] * trozoImaginaria[i]);
            var mejor = (trozoReal[mayor] * trozoReal[mayor]) + (trozoImaginaria[mayor] * trozoImaginaria[mayor]);
            if (potencia > mejor) mayor = i;
        }
        mayor.Should().Be(TonoDePrueba + AnalisisDeVentana.MargenEnTonos,
            "el tono cero cae en la casilla del margen y cada tono siguiente en la de al lado");
    }
}
