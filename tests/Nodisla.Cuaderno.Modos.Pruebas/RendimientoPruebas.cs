using System.Globalization;
using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Modos.Banco;
using Nodisla.Cuaderno.Modos.Ft8;
using Nodisla.Cuaderno.Modos.Ldpc;
using Nodisla.Cuaderno.Modos.Pruebas.Medidas;
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
/// <b>Se mide en tiempo de procesador y no con un reloj de pared</b>, porque lo que se quiere
/// saber es cuanto trabajo cuesta una ventana, no cuanto tardo el ordenador en hacerlo mientras
/// atendia a otros diez. Ver <see cref="TiempoDeProceso"/>.
/// </para>
/// <para>
/// <b>El presupuesto se exige en las dos compilaciones</b>, y con el mismo numero. Se penso en
/// exigirlo solo en publicacion —que es lo que se hace con el informe del banco, porque ahi lo
/// que se publica son cifras comparables— pero aqui no hace falta y ademas seria peor: la serie
/// de pruebas se pasa en depuracion, asi que un presupuesto que solo valiera en publicacion no
/// lo comprobaria nadie casi nunca. Midiendo trabajo en vez de reloj, el mismo numero vale para
/// las dos: hoy son unos 1200 ms en depuracion y unos 400 en publicacion, las dos holgadamente
/// por debajo del tope.
/// </para>
/// </remarks>
public class RendimientoPruebas(ITestOutputHelper salida)
{
    private static readonly TablasDelProtocolo Tablas = TablasDelProtocolo.Cargar();

    // Se carga la tabla de verdad, no la de pruebas: estas comprobaciones tienen que
    // recorrer el mismo camino que recorrerá el módem cuando esté escuchando la banda.

    /// <summary>
    /// Presupuesto de trabajo por ventana de FT8, en milisegundos de procesador.
    /// </summary>
    /// <remarks>
    /// Cinco segundos son un tercio del hueco de quince que da el modo. Hoy se gastan unos 1200
    /// milisegundos compilando en depuracion y unos 400 en publicacion, asi que hay un margen de
    /// cuatro veces en el peor caso: el presupuesto no esta para afinar decimas sino para que
    /// salte si alguien deja el decodificador varias veces mas lento sin darse cuenta.
    /// </remarks>
    private const int PresupuestoPorVentanaMs = 5000;

    /// <summary>Pases que se miden; vale el mas barato de todos.</summary>
    private const int Pases = 3;

    [Fact]
    public void UnaVentanaDeFt8CabeEnSuPresupuesto()
    {
        var p = ParametrosDelModo.Ft8;
        const int Frecuencia = 48000;
        var decodificador = new Decodificador(Tablas);
        var ventana = VentanaConCuatroEstaciones(p, Frecuencia);

        // Una pasada en vacio para que el compilador de tiempo de ejecucion haga su trabajo:
        // si no, la primera medida incluye la compilacion y no mide el modem.
        var primera = Decodificar(decodificador, ventana, Frecuencia);
        primera.Decodificaciones.Should().HaveCount(4, "la ventana de prueba lleva cuatro estaciones");

        var reparto = Repartir(ventana, Frecuencia, p);
        var porVentana = TiempoDeProceso.MejorDe(Pases, () => Decodificar(decodificador, ventana, Frecuencia));

        var c = CultureInfo.GetCultureInfo("es-ES");
        salida.WriteLine(string.Format(c,
            "Ventana completa: {0:0} ms de procesador (presupuesto {1} ms, {2})",
            porVentana, PresupuestoPorVentanaMs, Configuracion));
        foreach (var (fase, ms) in reparto)
            salida.WriteLine(string.Format(c, "  {0,-24} {1,6:0.0} ms", fase, ms));

        porVentana.Should().BeLessThan(PresupuestoPorVentanaMs,
            "decodificar una ventana tiene que caber holgadamente en los quince segundos del modo");
    }

#if DEBUG
    private const string Configuracion = "compilación de depuración";
#else
    private const string Configuracion = "compilación de publicación";
#endif

    private static ResultadoDeVentana Decodificar(Decodificador decodificador, float[] ventana, int frecuencia) =>
        decodificador.Decodificar(ventana, frecuencia, ModoDelModem.Ft8, DateTimeOffset.UnixEpoch, new CatalogoDeIndicativos());

    /// <summary>Una ventana con cuatro estaciones y ruido, que es lo que hay en una banda de verdad.</summary>
    private static float[] VentanaConCuatroEstaciones(ParametrosDelModo p, int frecuencia)
    {
        var codificador = new Codificador(Tablas);
        var azar = new Random(2026);
        var ventana = new float[(int)(p.PeriodoSegundos * frecuencia)];
        double potencia = 0;

        string[] mensajes = ["CQ EA8DLF IL18", "EA8DLF EA1ABC IN80", "EA1ABC EA8DLF -07", "CQ K1ABC FN42"];
        for (var i = 0; i < mensajes.Length; i++)
        {
            codificador.TryCodificar(mensajes[i], ModoDelModem.Ft8, out var tonos, out _).Should().BeTrue();
            var senal = Modulador.Sintetizar(p, tonos, 700 + (i * 500), frecuencia, 0.2);
            potencia = GeneradorDeSenal.PotenciaMedia(senal);
            var comienzo = (int)(p.ComienzoNominalSegundos * frecuencia);
            for (var k = 0; k < senal.Length; k++) ventana[comienzo + k] += senal[k];
        }
        GeneradorDeSenal.AnadirRuido(ventana, potencia, -10, frecuencia, azar);
        return ventana;
    }

    /// <summary>Cronometra cada fase por separado, para saber donde se va el tiempo.</summary>
    /// <remarks>
    /// El reparto es informativo y no se exige nada sobre el: esta para que quien vaya a
    /// optimizar sepa donde merece la pena tocar en vez de adivinar.
    /// </remarks>
    private static List<(string Fase, double Milisegundos)> Repartir(float[] ventana, int frecuencia, ParametrosDelModo p)
    {
        var reparto = new List<(string, double)>();
        float[] muestras = [];
        AnalisisDeVentana? analisis = null;
        List<Candidata> candidatas = [];
        Demodulador? demodulador = null;

        reparto.Add(("remuestrear", TiempoDeProceso.De(() =>
            muestras = Remuestreador.Remuestrear(ventana, frecuencia, p.FrecuenciaDeAnalisis))));

        reparto.Add(("analizar la ventana", TiempoDeProceso.De(() =>
            analisis = AnalisisDeVentana.Calcular(muestras, p))));

        reparto.Add(("buscar candidatas", TiempoDeProceso.De(() =>
            candidatas = Sincronizador.Buscar(analisis!, 200))));

        reparto.Add(($"demodular ({candidatas.Count} candidatas)", TiempoDeProceso.De(() =>
        {
            demodulador = new Demodulador(analisis!);
            foreach (var candidata in candidatas) demodulador.Medir(candidata);
        })));

        reparto.Add(("corregir errores", TiempoDeProceso.De(() =>
        {
            var corrector = new DecodificadorDeCreencia(Tablas.Ldpc);
            var palabra = new byte[Tablas.Ldpc.Longitud];
            foreach (var _ in candidatas) corrector.TryDecodificar(demodulador!.Confianzas, palabra, 60);
        })));

        return reparto;
    }
}
