using System.Diagnostics;
using System.Globalization;
using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Modos.Banco;
using Nodisla.Cuaderno.Modos.Ft8;
using Nodisla.Cuaderno.Modos.Ldpc;
using Nodisla.Cuaderno.Modos.Senal;
using Xunit.Abstractions;

namespace Nodisla.Cuaderno.Modos.Pruebas;

/// <summary>
/// Mide cuanto cuesta decodificar una ventana y en que se va el tiempo.
/// </summary>
/// <remarks>
/// <para>
/// FT8 da quince segundos entre ventana y ventana, y en ese hueco hay que decodificar la
/// anterior. Si se pasa, la siguiente empieza tarde y se pierden decodificaciones; eso es lo que
/// el puerto del modem llama <c>LlegoTarde</c>. El ordenador donde esto va a correr tiene
/// ademas el cuaderno abierto, el mapa, el cluster y lo que el operador este haciendo, asi que
/// <b>gastar la mitad del hueco ya es demasiado</b>.
/// </para>
/// <para>
/// Esta prueba no solo pone un tope: tambien reparte el tiempo entre las cuatro fases, que es lo
/// unico que permite saber donde merece la pena tocar. Sin ese reparto, optimizar es adivinar.
/// </para>
/// </remarks>
public class RendimientoPruebas(ITestOutputHelper salida)
{
    private static readonly TablasDelProtocolo Tablas = TablasDelProtocolo.DePruebas();

    /// <summary>
    /// Presupuesto de tiempo por ventana de FT8, en milisegundos.
    /// </summary>
    /// <remarks>
    /// Un tercio del hueco de quince segundos. Deja margen de sobra para que el ordenador este
    /// ocupado con otras cosas y aun asi no se pierda ninguna ventana.
    /// </remarks>
    private const int PresupuestoPorVentanaMs = 5000;

    [Fact]
    public void UnaVentanaDeFt8CabeEnSuPresupuesto()
    {
        var p = ParametrosDelModo.Ft8;
        const int Frecuencia = 48000;
        var codificador = new Codificador(Tablas);
        var decodificador = new Decodificador(Tablas);

        // Una ventana con cuatro estaciones y ruido, que es lo que hay en una banda de verdad.
        var azar = new Random(2026);
        var ventana = new float[(int)(p.PeriodoSegundos * Frecuencia)];
        double potencia = 0;
        for (var i = 0; i < 4; i++)
        {
            codificador.TryCodificar("CQ EA8DLF IL18", ModoDelModem.Ft8, out var tonos, out _).Should().BeTrue();
            var senal = Modulador.Sintetizar(p, tonos, 700 + (i * 500), Frecuencia, 0.2);
            potencia = GeneradorDeSenal.PotenciaMedia(senal);
            var comienzo = (int)(p.ComienzoNominalSegundos * Frecuencia);
            for (var k = 0; k < senal.Length; k++) ventana[comienzo + k] += senal[k];
        }
        GeneradorDeSenal.AnadirRuido(ventana, potencia, -10, Frecuencia, azar);

        // Una pasada en vacio para que el compilador de tiempo de ejecucion haga su trabajo:
        // si no, la primera medida incluye la compilacion y no mide el modem.
        decodificador.Decodificar(ventana, Frecuencia, ModoDelModem.Ft8, DateTimeOffset.UnixEpoch, new CatalogoDeIndicativos());

        var reparto = Repartir(ventana, Frecuencia, p);
        var reloj = Stopwatch.StartNew();
        const int Vueltas = 3;
        for (var i = 0; i < Vueltas; i++)
            decodificador.Decodificar(ventana, Frecuencia, ModoDelModem.Ft8, DateTimeOffset.UnixEpoch, new CatalogoDeIndicativos());
        reloj.Stop();
        var porVentana = reloj.ElapsedMilliseconds / (double)Vueltas;

        var c = CultureInfo.GetCultureInfo("es-ES");
        salida.WriteLine(string.Format(c, "Ventana completa: {0:0} ms (presupuesto {1} ms)", porVentana, PresupuestoPorVentanaMs));
        foreach (var (fase, ms) in reparto)
            salida.WriteLine(string.Format(c, "  {0,-22} {1,6:0.0} ms", fase, ms));

        porVentana.Should().BeLessThan(PresupuestoPorVentanaMs,
            "decodificar una ventana tiene que caber holgadamente en los quince segundos del modo");
    }

    /// <summary>Cronometra cada fase por separado, para saber donde se va el tiempo.</summary>
    private static List<(string Fase, double Milisegundos)> Repartir(float[] ventana, int frecuencia, ParametrosDelModo p)
    {
        var reparto = new List<(string, double)>();
        var reloj = Stopwatch.StartNew();

        var muestras = Remuestreador.Remuestrear(ventana, frecuencia, p.FrecuenciaDeAnalisis);
        reparto.Add(("remuestrear", reloj.Elapsed.TotalMilliseconds));

        reloj.Restart();
        var analisis = AnalisisDeVentana.Calcular(muestras, p);
        reparto.Add(("analizar la ventana", reloj.Elapsed.TotalMilliseconds));

        reloj.Restart();
        var candidatas = Sincronizador.Buscar(analisis, 200);
        reparto.Add(($"buscar ({candidatas.Count} candidatas)", reloj.Elapsed.TotalMilliseconds));

        reloj.Restart();
        var demodulador = new Demodulador(analisis);
        foreach (var candidata in candidatas) demodulador.Medir(candidata);
        reparto.Add(("demodular las candidatas", reloj.Elapsed.TotalMilliseconds));

        reloj.Restart();
        var corrector = new DecodificadorDeCreencia(Tablas.Ldpc);
        var palabra = new byte[Tablas.Ldpc.Longitud];
        foreach (var _ in candidatas) corrector.TryDecodificar(demodulador.Confianzas, palabra, 60);
        reparto.Add(("corregir errores", reloj.Elapsed.TotalMilliseconds));

        return reparto;
    }
}
