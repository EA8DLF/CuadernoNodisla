using FluentAssertions;
using Nodisla.Cuaderno.Modos.ReedSolomon;

namespace Nodisla.Cuaderno.Modos.Pruebas.ReedSolomon;

/// <summary>Pruebas del cuerpo de Galois y del codigo Reed-Solomon de JT65.</summary>
public class ReedSolomonPruebas
{
    private static readonly CodigoReedSolomon Codigo = CodigoReedSolomon.Jt65;

    [Fact]
    public void ElCuerpoDe64TieneLasPropiedadesDeUnCuerpo()
    {
        var gf = CampoDeGalois.Gf64;
        gf.Tamano.Should().Be(64);
        gf.Orden.Should().Be(63);

        for (var a = 1; a < 64; a++)
        {
            gf.Multiplicar(a, gf.Inverso(a)).Should().Be(1, $"{a} por su inverso tiene que dar uno");
            gf.Exp(gf.Log(a)).Should().Be(a);
            gf.Multiplicar(a, 1).Should().Be(a);
            gf.Multiplicar(a, 0).Should().Be(0);
        }

        // Distributiva, con unos cuantos tríos al azar.
        var azar = new Random(1);
        for (var i = 0; i < 500; i++)
        {
            int a = azar.Next(64), b = azar.Next(64), c = azar.Next(64);
            gf.Multiplicar(a, b ^ c).Should().Be(gf.Multiplicar(a, b) ^ gf.Multiplicar(a, c));
            gf.Multiplicar(a, b).Should().Be(gf.Multiplicar(b, a));
        }

        // Alfa^63 = 1 y ninguna potencia menor vuelve a uno: el polinomio es primitivo.
        gf.Exp(63).Should().Be(1);
        for (var e = 1; e < 63; e++) gf.Exp(e).Should().NotBe(1);
    }

    [Fact]
    public void UnPolinomioNoPrimitivoSeRechaza()
    {
        // x^6 + x^3 + 1 no es primitivo en GF(64) (su orden es 9).
        var construir = () => new CampoDeGalois(6, 0x49);
        construir.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ElCodigoTieneLasDimensionesDeJt65()
    {
        Codigo.Longitud.Should().Be(63);
        Codigo.SimbolosDeMensaje.Should().Be(12);
        Codigo.Raices.Should().Be(51);
        Codigo.ErroresCorregibles.Should().Be(25);
    }

    [Fact]
    public void CodificarEsSistematicoYLaPalabraSeAnulaEnLasRaices()
    {
        var azar = new Random(7);
        var mensaje = new byte[12];
        var palabra = new byte[63];
        for (var vez = 0; vez < 20; vez++)
        {
            for (var i = 0; i < 12; i++) mensaje[i] = (byte)azar.Next(64);
            Codigo.Codificar(mensaje, palabra);

            palabra[51..].Should().Equal(mensaje, "el mensaje va tal cual en las posiciones altas");
            var extraido = new byte[12];
            Codigo.ExtraerMensaje(palabra, extraido);
            extraido.Should().Equal(mensaje);

            var sindromes = new int[51];
            Codigo.Sindromes(palabra, sindromes).Should().BeTrue("una palabra de codigo tiene todos los sindromes a cero");
        }
    }

    [Fact]
    public void ElCodigoEsLineal()
    {
        var azar = new Random(11);
        var a = new byte[12];
        var b = new byte[12];
        var pa = new byte[63];
        var pb = new byte[63];
        var suma = new byte[63];
        for (var i = 0; i < 12; i++) { a[i] = (byte)azar.Next(64); b[i] = (byte)azar.Next(64); }
        Codigo.Codificar(a, pa);
        Codigo.Codificar(b, pb);
        for (var i = 0; i < 63; i++) suma[i] = (byte)(pa[i] ^ pb[i]);
        Codigo.Sindromes(suma, new int[51]).Should().BeTrue("la suma de dos palabras de codigo es otra palabra de codigo");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(25)]
    public void CorrigeHastaVeinticincoErrores(int errores)
    {
        var azar = new Random(100 + errores);
        for (var vez = 0; vez < 50; vez++)
        {
            var (mensaje, palabra) = PalabraAlAzar(azar);
            var recibida = (byte[])palabra.Clone();
            Estropear(recibida, errores, azar);

            Codigo.TryCorregir(recibida, [], out var corregidas).Should().BeTrue($"{errores} errores caben en la capacidad del codigo");
            corregidas.Should().Be(errores);
            recibida.Should().Equal(palabra);
            var extraido = new byte[12];
            Codigo.ExtraerMensaje(recibida, extraido);
            extraido.Should().Equal(mensaje);
        }
    }

    [Theory]
    [InlineData(51, 0)]
    [InlineData(41, 5)]
    [InlineData(21, 15)]
    [InlineData(1, 25)]
    public void CorrigeBorradosYErroresMientrasQuepan(int borrados, int errores)
    {
        var azar = new Random(200 + borrados);
        for (var vez = 0; vez < 50; vez++)
        {
            var (_, palabra) = PalabraAlAzar(azar);
            var recibida = (byte[])palabra.Clone();

            // Se eligen posiciones distintas para borrados y errores.
            var posiciones = Enumerable.Range(0, 63).OrderBy(_ => azar.Next()).ToArray();
            var borradas = posiciones[..borrados];
            var erradas = posiciones[borrados..(borrados + errores)];
            foreach (var p in borradas) recibida[p] = (byte)azar.Next(64);   // puede o no coincidir
            foreach (var p in erradas) recibida[p] = (byte)((recibida[p] + 1 + azar.Next(63)) % 64);

            Codigo.TryCorregir(recibida, borradas, out _).Should().BeTrue($"{borrados} borrados y {errores} errores cumplen e + 2t <= 51");
            recibida.Should().Equal(palabra);
        }
    }

    [Fact]
    public void ConDemasiadosErroresNoInventaOtraPalabraCasiNunca()
    {
        // Con 26 o mas errores el decodificador no puede garantizar nada; lo que se comprueba es
        // que, cuando dice que corrigio, la palabra que devuelve es de codigo de verdad, y que
        // casi siempre se rinde en vez de inventar. (Con 26 errores en RS(63,12) la
        // probabilidad de caer dentro del radio de otra palabra es del orden de 1e-4.)
        var azar = new Random(300);
        var inventadas = 0;
        var pruebas = 400;
        for (var vez = 0; vez < pruebas; vez++)
        {
            var (_, palabra) = PalabraAlAzar(azar);
            var recibida = (byte[])palabra.Clone();
            Estropear(recibida, 30, azar);
            if (Codigo.TryCorregir(recibida, [], out _))
            {
                Codigo.Sindromes(recibida, new int[51]).Should().BeTrue("si corrige, lo que devuelve tiene que ser palabra de codigo");
                if (!recibida.AsSpan().SequenceEqual(palabra)) inventadas++;
            }
        }
        inventadas.Should().BeLessThan(pruebas / 20);
    }

    [Fact]
    public void ElDecodificadorBlandoNoAceptaNadaConRuidoPuro()
    {
        // Miles de palabras de ruido puro: 63 posiciones con 64 potencias exponenciales cada
        // una. Ni una sola puede salir aceptada. Esta es la prueba que protege el cuaderno.
        var blando = new DecodificadorBlando(Codigo) { Intentos = 300 };
        var azar = new Random(4000);
        var potencias = new float[63 * 64];
        var palabra = new byte[63];
        var aceptadas = 0;
        var mejorPuntuacion = double.NegativeInfinity;
        const int Palabras = 300;
        for (var vez = 0; vez < Palabras; vez++)
        {
            RuidoExponencial(potencias, azar);
            // Se le hace creer que hay una senal de −24 dB (s = 3,7), que es el caso peligroso:
            // con s pequena todo puntua poco y con s grande el ruido no llega.
            var r = blando.Decodificar(potencias, palabra, azar, 3.7);
            if (r.Decodificada) aceptadas++;
            if (r.Puntuacion > mejorPuntuacion) mejorPuntuacion = r.Puntuacion;
        }
        aceptadas.Should().Be(0, $"con ruido puro no hay nada que decodificar (mejor puntuación vista: {mejorPuntuacion:0.0})");
        mejorPuntuacion.Should().BeLessThan(blando.PuntuacionMinima - 20, "el umbral tiene que quedar con margen por encima de lo que produce el ruido");
    }

    [Fact]
    public void ElDecodificadorBlandoSacaPalabrasQueElDuroNoPuede()
    {
        // Simbolos con senal: la potencia del tono bueno es ruido mas una senal de amplitud
        // conocida, con estadistica de Rice (no coherente). A relacion senal-ruido por simbolo
        // baja, mas de 25 simbolos llegan mal y el decodificador duro se rinde; el blando no.
        var blando = new DecodificadorBlando(Codigo) { Intentos = 3000 };
        var azar = new Random(5000);
        var potencias = new float[63 * 64];
        var salida = new byte[63];
        int durasBien = 0, blandasBien = 0, falsas = 0;
        const int Palabras = 40;
        const double RelacionPorSimbolo = 4.0; // ~6 dB por simbolo, el filo de JT65
        for (var vez = 0; vez < Palabras; vez++)
        {
            var (_, palabra) = PalabraAlAzar(azar);
            RuidoExponencial(potencias, azar);
            for (var i = 0; i < 63; i++)
                potencias[(i * 64) + palabra[i]] = (float)PotenciaConSenal(RelacionPorSimbolo, azar);

            var dura = new byte[63];
            for (var i = 0; i < 63; i++)
            {
                var fila = potencias.AsSpan(i * 64, 64);
                var h = 0;
                for (var v = 1; v < 64; v++) if (fila[v] > fila[h]) h = v;
                dura[i] = (byte)h;
            }
            var copia = (byte[])dura.Clone();
            if (Codigo.TryCorregir(copia, [], out _) && copia.AsSpan().SequenceEqual(palabra)) durasBien++;

            var r = blando.Decodificar(potencias, salida, azar, RelacionPorSimbolo);
            if (!r.Decodificada) continue;
            if (salida.AsSpan().SequenceEqual(palabra)) blandasBien++;
            else falsas++;
        }

        falsas.Should().Be(0);
        blandasBien.Should().BeGreaterThan(durasBien, "el blando tiene que sacar mas que el duro");
        blandasBien.Should().BeGreaterThan(Palabras / 2);
    }

    [Fact]
    public void LaProbabilidadDeErrorDeSimboloTieneSentido()
    {
        // Sin senal se acierta uno de cada 64; con senal holgada casi siempre.
        DecodificadorBlando.ProbabilidadDeErrorDeSimbolo(0, 64).Should().BeApproximately(63.0 / 64, 1e-3);
        DecodificadorBlando.ProbabilidadDeErrorDeSimbolo(20, 64).Should().BeLessThan(0.01);
        var anterior = 1.0;
        foreach (var s in new[] { 0.5, 1, 2, 3, 4, 6, 10 })
        {
            var pe = DecodificadorBlando.ProbabilidadDeErrorDeSimbolo(s, 64);
            pe.Should().BeLessThan(anterior, "con mas senal hay menos errores");
            anterior = pe;
        }
    }

    [Fact]
    public void ConSenalFuerteNoAceptaUnaPalabraQueCorrigeDemasiado()
    {
        // Una palabra de codigo cualquiera, y unas potencias que apoyan con mucha fuerza solo
        // 20 de sus simbolos: el resto de posiciones tienen un tono fuerte en otro sitio. La
        // verosimilitud de esa palabra es enorme (los 20 simbolos fuertes pesan mucho), pero
        // con una relacion de 1000 por simbolo no se espera ni un error duro: corregir 43 no
        // es creible y se tiene que rechazar.
        var azar = new Random(77);
        var (_, palabra) = PalabraAlAzar(azar);
        var potencias = new float[63 * 64];
        RuidoExponencial(potencias, azar);
        for (var i = 0; i < 63; i++)
        {
            var tono = i < 20 ? palabra[i] : (palabra[i] + 1 + azar.Next(63)) % 64;
            potencias[(i * 64) + tono] = 1000;
        }
        var blando = new DecodificadorBlando(Codigo) { Intentos = 500 };
        var r = blando.Decodificar(potencias, new byte[63], azar, 1000);
        r.Decodificada.Should().BeFalse();
    }

    private static (byte[] Mensaje, byte[] Palabra) PalabraAlAzar(Random azar)
    {
        var mensaje = new byte[12];
        for (var i = 0; i < 12; i++) mensaje[i] = (byte)azar.Next(64);
        var palabra = new byte[63];
        Codigo.Codificar(mensaje, palabra);
        return (mensaje, palabra);
    }

    private static void Estropear(byte[] palabra, int errores, Random azar)
    {
        var posiciones = Enumerable.Range(0, 63).OrderBy(_ => azar.Next()).Take(errores);
        foreach (var p in posiciones) palabra[p] = (byte)((palabra[p] + 1 + azar.Next(63)) % 64);
    }

    private static void RuidoExponencial(float[] potencias, Random azar)
    {
        for (var i = 0; i < potencias.Length; i++)
            potencias[i] = (float)(-Math.Log(1.0 - azar.NextDouble()));
    }

    /// <summary>Potencia de una casilla con senal de relacion dada sobre ruido complejo gaussiano de potencia uno.</summary>
    private static double PotenciaConSenal(double relacion, Random azar)
    {
        var amplitud = Math.Sqrt(relacion);
        var re = amplitud + (Gaussiana(azar) / Math.Sqrt(2));
        var im = Gaussiana(azar) / Math.Sqrt(2);
        return (re * re) + (im * im);
    }

    private static double Gaussiana(Random azar) =>
        Math.Sqrt(-2.0 * Math.Log(1.0 - azar.NextDouble())) * Math.Cos(2.0 * Math.PI * azar.NextDouble());
}
