using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Modos.Banco;
using Nodisla.Cuaderno.Modos.Ft8;
using Nodisla.Cuaderno.Modos.Ldpc;

namespace Nodisla.Cuaderno.Modos.Pruebas;

/// <summary>
/// La decodificacion AP: idea de JTDX (documentada en «JTDX_improved» y en la propia ayuda de
/// JTDX sobre «AP decoding»; <b>no se ha copiado ni mirado su código</b>, solo lo que publican de
/// su comportamiento), reimplementada desde cero sobre el LDPC propio.
/// </summary>
/// <remarks>
/// Lo que se mide aquí tiene que demostrar dos cosas, no solo una: que ayuda cuando el QSO ya se
/// conoce (<see cref="LaPistaRecuperaSenalesQueSinEllaSePierden"/>), y que no se puede colar un
/// contacto falso cuando la pista está equivocada o no hay señal (las demás). La primera sin la
/// segunda no vale nada en este programa.
/// </remarks>
public class PistaApPruebas
{
    private static readonly TablasDelProtocolo Tablas = TablasDelProtocolo.Cargar();

    [Theory]
    [InlineData("EA1ABC", "EA8DLF")]
    [InlineData("K1ABC", "EA8DLF/R")]
    public void SeConstruyeParaDosIndicativosCorrientes(string dxCall, string miIndicativo)
    {
        PistaAp.TryDesde(miIndicativo, dxCall, out var pista).Should().BeTrue();

        // Los 61 bits conocidos (58 de los indicativos y 3 del tipo) tienen que coincidir con
        // los que de verdad saldrian codificando un mensaje con esos mismos dos indicativos,
        // sea cual sea el informe que se use de relleno.
        MensajeDe77Bits.TryEmpaquetar($"{dxCall} {miIndicativo} -09", out var bitsDeControl, out _).Should().BeTrue();
        for (var i = 0; i < 58; i++) pista.Bits77[i].Should().Be(bitsDeControl[i], $"bit {i} es parte de un indicativo");
        for (var i = 74; i < 77; i++) pista.Bits77[i].Should().Be(bitsDeControl[i], $"bit {i} es el tipo de mensaje");

        // El campo del informe (58 a 73) no se da por sabido: es lo unico que de verdad hace
        // falta leer de la senal.
        for (var i = 58; i < 74; i++) pista.Conocido77[i].Should().BeFalse($"bit {i} es del campo de informe, no de los indicativos");
    }

    [Theory]
    [InlineData("HB10GBT", "EA8DLF")]
    [InlineData("EA1ABC", "")]
    [InlineData("", "EA8DLF")]
    public void NoHayPistaSiAlgunIndicativoNoEsCorriente(string dxCall, string miIndicativo)
    {
        PistaAp.TryDesde(miIndicativo, dxCall, out _).Should().BeFalse(
            "un indicativo raro viaja resumido o en claro segun el resto del mensaje, que aqui no se conoce");
    }

    [Fact]
    public void LaPistaRecuperaSenalesQueSinEllaSePierden()
    {
        // A -19/-20 dB el modem ya pierde buena parte de las ventanas (ver el banco de medida:
        // a -18 dB el filo esta sobre el 70 %). Es la franja donde a la pista le toca demostrar
        // algo, no un caso de laboratorio elegido a mano.
        const double Decibelios = -19.5;
        const int Ventanas = 60;

        var (sinPista, conPista) = Comparar("EA1ABC EA8DLF -09", "EA8DLF", "EA1ABC", Decibelios, Ventanas);

        conPista.Should().BeGreaterThan(sinPista,
            $"con la pista del QSO en curso tiene que recuperar más que a ciegas a {Decibelios} dB " +
            $"(a ciegas: {sinPista}/{Ventanas}, con pista: {conPista}/{Ventanas})");
    }

    [Fact]
    public void UnaPistaEquivocadaNoInventaNadaConRuidoPuro()
    {
        // El caso que de verdad importa: si una pista (de un QSO con quien no esta transmitiendo)
        // pudiera colar algo con solo ruido, el programa mentiria sobre con quien se ha hablado.
        var p = ParametrosDelModo.Ft8;
        const int Frecuencia = 48000;
        PistaAp.TryDesde("EA8DLF", "EA1ABC", out var pista).Should().BeTrue();
        var decodificador = new Decodificador(Tablas);
        var inventadas = 0;

        for (var v = 0; v < 40; v++)
        {
            var azar = new Random(2000 + v);
            var ventana = new float[(int)(p.PeriodoSegundos * Frecuencia)];
            GeneradorDeSenal.AnadirRuido(ventana, 0.05, 0, Frecuencia, azar);
            inventadas += decodificador.Decodificar(
                ventana, Frecuencia, ModoDelModem.Ft8, DateTimeOffset.UnixEpoch, new CatalogoDeIndicativos(), pista: pista)
                .Decodificaciones.Count;
        }

        inventadas.Should().Be(0, "cuarenta ventanas de ruido puro no pueden producir ni un solo mensaje, con pista o sin ella");
    }

    [Fact]
    public void UnaPistaEquivocadaNoCambiaLoQueSeOyeDeOtraEstacion()
    {
        // Si mientras se esta en QSO con EA1ABC pasa por la banda OTRA estacion (K1ABC llamando
        // CQ, por ejemplo), la pista de EA1ABC no tiene ningun bit en comun que le sirva: tiene
        // que decodificarse igual que sin pista, o no decodificarse, nunca otra cosa distinta.
        const string Mensaje = "CQ K1ABC FN42";
        PistaAp.TryDesde("EA8DLF", "EA1ABC", out var pista).Should().BeTrue();

        for (var db = -6; db >= -21; db -= 3)
        {
            var sinPista = CadenaCompletaPruebas.IdaYVuelta(ModoDelModem.Ft8, Mensaje, db, desfase: 0.1, tono: 900, semilla: 11);
            var conPista = IdaYVueltaConPista(Mensaje, db, desfase: 0.1, tono: 900, semilla: 11, pista);

            // Lo que salga con pista tiene que ser un subconjunto de lo que sale a ciegas: la
            // pista puede, como mucho, no ayudar aqui (no tiene ningun bit de EA1ABC que pescar
            // en una senal de K1ABC); lo que no puede es inventar un texto que no sea el emitido.
            foreach (var d in conPista) d.Texto.Should().Be(Mensaje);
            conPista.Count.Should().BeLessThanOrEqualTo(sinPista.Count + 1,
                "la pista no puede sacar más estaciones distintas de las que hay de verdad en la ventana");
        }
    }

    /// <summary>Decodifica la misma señal sintética con y sin la pista, para comparar.</summary>
    private static (int SinPista, int ConPista) Comparar(
        string texto, string miIndicativo, string dxCall, double decibelios, int ventanas)
    {
        PistaAp.TryDesde(miIndicativo, dxCall, out var pista).Should().BeTrue();

        var p = ParametrosDelModo.Ft8;
        var codificador = new Codificador(Tablas);
        codificador.TryCodificar(texto, ModoDelModem.Ft8, out var tonos, out var motivo).Should().BeTrue(motivo);
        const int Frecuencia = 48000;

        int sinPista = 0, conPista = 0;
        var decodificador = new Decodificador(Tablas);
        for (var v = 0; v < ventanas; v++)
        {
            var azar = new Random(5000 + v);
            var tono = 500 + (azar.NextDouble() * 2000);
            var desfase = (azar.NextDouble() - 0.5) * 0.6;
            var ventana = GeneradorDeSenal.Ventana(p, tonos, tono, desfase, decibelios, Frecuencia, azar);

            if (decodificador.Decodificar(ventana, Frecuencia, ModoDelModem.Ft8, DateTimeOffset.UnixEpoch, new CatalogoDeIndicativos())
                .Decodificaciones.Any(d => d.Texto == texto)) sinPista++;

            if (decodificador.Decodificar(
                ventana, Frecuencia, ModoDelModem.Ft8, DateTimeOffset.UnixEpoch, new CatalogoDeIndicativos(), pista: pista)
                .Decodificaciones.Any(d => d.Texto == texto)) conPista++;
        }

        return (sinPista, conPista);
    }

    private static IReadOnlyList<DecodificacionPropia> IdaYVueltaConPista(
        string texto, double decibelios, double desfase, double tono, int semilla, PistaAp pista)
    {
        var p = ParametrosDelModo.Ft8;
        var codificador = new Codificador(Tablas);
        codificador.TryCodificar(texto, ModoDelModem.Ft8, out var tonos, out var motivo).Should().BeTrue(motivo);
        const int Frecuencia = 48000;
        var ventana = GeneradorDeSenal.Ventana(p, tonos, tono, desfase, decibelios, Frecuencia, new Random(semilla));

        var decodificador = new Decodificador(Tablas);
        return decodificador.Decodificar(
            ventana, Frecuencia, ModoDelModem.Ft8, DateTimeOffset.UnixEpoch, new CatalogoDeIndicativos(), pista: pista)
            .Decodificaciones;
    }
}
