using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Modos.Banco;
using Nodisla.Cuaderno.Modos.Ft8;
using Nodisla.Cuaderno.Modos.Ldpc;

namespace Nodisla.Cuaderno.Modos.Pruebas;

/// <summary>
/// La mezcla de varias senales FT8 en una sola, para el lado fox de fox/hound: que la
/// normalizacion no sature nunca y que el propio decodificador recupere los mensajes mezclados.
/// </summary>
/// <remarks>
/// Igual que <see cref="CadenaCompletaPruebas"/>, esto no depende de nada de fuera: se sintetiza
/// con <see cref="Modulador.SintetizarMezcla"/> (o <see cref="ModoFt8.GenerarMezcla"/>, que lo
/// envuelve) y se decodifica con el propio decodificador. Es la unica prueba de que lo que se
/// mezcla se puede separar de vuelta; no sustituye a probarlo con un receptor de verdad en el
/// aire, que no se ha hecho.
/// </remarks>
public class MezclaDeSenalesPruebas
{
    private static readonly TablasDelProtocolo Tablas = TablasDelProtocolo.Cargar();
    private const int Frecuencia = 48000;

    [Fact]
    public void LaMezclaDeUnaSolaSenalEsLaMismaQueSintetizarla()
    {
        var p = ParametrosDelModo.Ft8;
        var codificador = new Codificador(Tablas);
        codificador.TryCodificar("CQ EA8DLF IL18", ModoDelModem.Ft8, out var tonos, out _).Should().BeTrue();

        var sola = Modulador.Sintetizar(p, tonos, 1500, Frecuencia, amplitud: 0.4);
        var mezcla = Modulador.SintetizarMezcla(p, [(tonos, 1500.0)], Frecuencia, amplitudDePico: 0.4);

        mezcla.Should().HaveCount(sola.Length);
        // El pico de una señal sola sintetizada a amplitud 0.4 es, por construccion, 0.4: la
        // mezcla de una sola tiene que coincidir, no solo en pico sino muestra a muestra (misma
        // forma de onda, mismo factor de escala).
        for (var i = 0; i < sola.Length; i++)
            mezcla[i].Should().BeApproximately(sola[i], 1e-5f);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(10)]
    public void LaMezclaNuncaSuperaLaAmplitudDePicoPedida(int cuantas)
    {
        var p = ParametrosDelModo.Ft8;
        var codificador = new Codificador(Tablas);
        var senales = new List<(byte[] Tonos, double TonoBaseHz)>();
        for (var i = 0; i < cuantas; i++)
        {
            codificador.TryCodificar($"EA{i}ABC EA8DLF -{i:00}", ModoDelModem.Ft8, out var tonos, out var motivo).Should().BeTrue(motivo);
            senales.Add((tonos, 1000 + (i * p.SeparacionMinimaDeTonosHz)));
        }

        const double AmplitudDePico = 0.4;
        var mezcla = Modulador.SintetizarMezcla(p, senales, Frecuencia, AmplitudDePico);

        var picoReal = 0f;
        foreach (var m in mezcla) picoReal = Math.Max(picoReal, Math.Abs(m));

        // Nunca por encima (no satura) y, salvo que las fases se cancelen del todo (practicamente
        // imposible con varias señales de tonos distintos), bastante cerca del pico pedido: la
        // normalizacion aprovecha el margen en vez de dejarlo tirado.
        picoReal.Should().BeLessThanOrEqualTo((float)AmplitudDePico + 1e-5f);
        picoReal.Should().BeGreaterThan(0, "con señales de verdad dentro, el pico no puede ser cero");
    }

    [Fact]
    public void SinSenalesLanza()
    {
        var p = ParametrosDelModo.Ft8;
        var accion = () => Modulador.SintetizarMezcla(p, [], Frecuencia, 0.4);
        accion.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// El corazon de la verificacion pedida: varios mensajes de fox, cada uno a su cazador y a su
    /// tono (separados lo que dice <see cref="ParametrosDelModo.SeparacionMinimaDeTonosHz"/>),
    /// mezclados con <see cref="ModoFt8.GenerarMezcla"/> y devueltos por el propio decodificador.
    /// </summary>
    [Fact]
    public void UnPileupDeCincoCazadoresSeDecodificaEnteroConBuenaSenal()
    {
        var modo = new ModoFt8(ModoDelModem.Ft8, Tablas, new CatalogoDeIndicativos()) { AmplitudDeSalida = 0.4 };
        var p = ParametrosDelModo.Ft8;

        string[] mensajes =
        [
            "EA1AAA EA8DLF -10",
            "EA2BBB EA8DLF R-05",
            "EA3CCC EA8DLF RR73",
            "EA4DDD EA8DLF -15",
            "EA5EEE EA8DLF R-02",
        ];
        var paso = p.SeparacionMinimaDeTonosHz;
        var tonos = Enumerable.Range(0, mensajes.Length).Select(i => 1000 + (int)Math.Round(i * paso)).ToArray();

        var mezcla = modo.GenerarMezcla(
            mensajes.Zip(tonos, (texto, tono) => (texto, tono)).ToList(), Frecuencia);

        var ventana = new float[(int)Math.Round(p.PeriodoSegundos * Frecuencia)];
        var comienzo = (int)Math.Round(p.ComienzoNominalSegundos * Frecuencia);
        for (var i = 0; i < mezcla.Length && comienzo + i < ventana.Length; i++) ventana[comienzo + i] = mezcla[i];

        // Señal fuerte y limpia a proposito (30 dB): esto comprueba que la mezcla SE PUEDE
        // separar de vuelta entera, no cuanto aguanta de ruido un pileup (eso es otra pregunta,
        // sin banco propio todavia).
        GeneradorDeSenal.AnadirRuido(ventana, GeneradorDeSenal.PotenciaMedia(mezcla), 30, Frecuencia, new Random(99));

        var decodificador = new Decodificador(Tablas);
        var resultado = decodificador.Decodificar(ventana, Frecuencia, ModoDelModem.Ft8, DateTimeOffset.UnixEpoch, new CatalogoDeIndicativos());

        resultado.Decodificaciones.Select(d => d.Texto).Should().BeEquivalentTo(mensajes,
            "los cinco mensajes del pileup, cada uno en su tono, tienen que volver todos con buena señal");
    }

    /// <summary>
    /// Lo mismo pero con una señal mas modesta (10 dB, ya floja para FT8) y contando cuantos
    /// vuelven: no se exige el 100%, se deja constancia de la cifra real para no fingir una tasa
    /// que no se ha medido.
    /// </summary>
    [Fact]
    public void UnPileupDeCincoCazadoresConSenalFlojaRecuperaLaMayoria()
    {
        var modo = new ModoFt8(ModoDelModem.Ft8, Tablas, new CatalogoDeIndicativos()) { AmplitudDeSalida = 0.4 };
        var p = ParametrosDelModo.Ft8;

        string[] mensajes =
        [
            "EA1AAA EA8DLF -10",
            "EA2BBB EA8DLF R-05",
            "EA3CCC EA8DLF RR73",
            "EA4DDD EA8DLF -15",
            "EA5EEE EA8DLF R-02",
        ];
        var paso = p.SeparacionMinimaDeTonosHz;
        var tonos = Enumerable.Range(0, mensajes.Length).Select(i => 1000 + (int)Math.Round(i * paso)).ToArray();

        var recuperados = 0;
        const int Repeticiones = 10;
        for (var r = 0; r < Repeticiones; r++)
        {
            var mezcla = modo.GenerarMezcla(mensajes.Zip(tonos, (texto, tono) => (texto, tono)).ToList(), Frecuencia);

            var ventana = new float[(int)Math.Round(p.PeriodoSegundos * Frecuencia)];
            var comienzo = (int)Math.Round(p.ComienzoNominalSegundos * Frecuencia);
            for (var i = 0; i < mezcla.Length && comienzo + i < ventana.Length; i++) ventana[comienzo + i] = mezcla[i];

            GeneradorDeSenal.AnadirRuido(ventana, GeneradorDeSenal.PotenciaMedia(mezcla), 10, Frecuencia, new Random(1000 + r));

            var decodificador = new Decodificador(Tablas);
            var resultado = decodificador.Decodificar(ventana, Frecuencia, ModoDelModem.Ft8, DateTimeOffset.UnixEpoch, new CatalogoDeIndicativos());
            recuperados += resultado.Decodificaciones.Select(d => d.Texto).Intersect(mensajes).Count();
        }

        var totalPosibles = mensajes.Length * Repeticiones;
        // No es una cifra de protocolo ni esta validada con hardware: es el suelo que este banco
        // mide hoy para cinco señales mezcladas a 10 dB. Si baja de esto en el futuro, algo se
        // habra roto en la mezcla o en el decodificador.
        recuperados.Should().BeGreaterThanOrEqualTo((int)(totalPosibles * 0.5),
            $"se esperaban recuperar al menos la mitad de {totalPosibles} mensajes con señal floja; salieron {recuperados}");
    }

    [Fact]
    public void GenerarMezclaSinMensajesLanza()
    {
        var modo = new ModoFt8(ModoDelModem.Ft8, Tablas, new CatalogoDeIndicativos());
        var accion = () => modo.GenerarMezcla([], Frecuencia);
        accion.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GenerarMezclaConMensajeInvalidoLanzaFormatException()
    {
        var modo = new ModoFt8(ModoDelModem.Ft8, Tablas, new CatalogoDeIndicativos());
        var accion = () => modo.GenerarMezcla([("esto no es un mensaje valido de verdad y es demasiado largo", 1500)], Frecuencia);
        accion.Should().Throw<FormatException>();
    }

    [Fact]
    public void UnModoSinMezclaLanzaNotSupported()
    {
        Nodisla.Cuaderno.Modos.Marco.IModoDigital modo = new global::Nodisla.Cuaderno.Modos.Wspr.ModoWspr();
        var accion = () => modo.GenerarMezcla([("K1ABC FN42 23", 1500)], Frecuencia);
        accion.Should().Throw<NotSupportedException>();
    }
}
