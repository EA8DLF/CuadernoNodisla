using FluentAssertions;
using Nodisla.Cuaderno.Modos.Ldpc;
using Nodisla.Cuaderno.Modos.Pruebas.Tablas;

namespace Nodisla.Cuaderno.Modos.Pruebas;

/// <summary>
/// Pruebas de la generalizacion del codigo LDPC a cualquier matriz y a mensajes que no van
/// delante, y de las tablas de MSK144, FST4 y FST4W.
/// </summary>
/// <remarks>
/// Lo primero que se comprueba es que la generalizacion <b>no cambia nada</b> para FT8: el
/// mismo codigo construido por el camino nuevo, con las posiciones 0, 1, 2..., da bit a bit las
/// mismas palabras que por el camino de siempre.
/// </remarks>
public class LdpcGeneralizadoPruebas
{
    [Fact]
    public void ConElMensajeDelanteElCaminoNuevoDaLoMismoQueElDeSiempre()
    {
        var clasico = CodigoLdpc.ConstruirDePrueba();
        var h = MatrizDe(clasico);
        var posiciones = Enumerable.Range(0, clasico.BitsDeMensaje).ToArray();
        var nuevo = CodigoLdpc.DesdeMatrizDeParidad(h, posiciones);

        nuevo.PosicionesDeMensaje.Should().Equal(posiciones);
        nuevo.PosicionesDeParidad.Should().Equal(Enumerable.Range(clasico.BitsDeMensaje, clasico.Ecuaciones));

        var azar = new Random(11);
        for (var intento = 0; intento < 200; intento++)
        {
            var mensaje = MensajeAlAzar(clasico.BitsDeMensaje, azar);
            nuevo.Codificar(mensaje).Should().Equal(clasico.Codificar(mensaje));
        }
    }

    [Fact]
    public void UnCodigoConElMensajeRepartidoCodificaYDecodifica()
    {
        // Se permutan las columnas del codigo de pruebas: el mensaje acaba disperso por la
        // palabra, como en el codigo corto de MSK144, y todo tiene que seguir cuadrando.
        var origen = CodigoLdpc.ConstruirDePrueba(semilla: 3, longitud: 64, bitsDeMensaje: 32);
        var azar = new Random(5);
        var permutacion = Enumerable.Range(0, origen.Longitud).OrderBy(_ => azar.Next()).ToArray();
        var h = MatrizDe(origen);
        var hPermutada = new bool[origen.Ecuaciones][];
        for (var e = 0; e < origen.Ecuaciones; e++)
        {
            hPermutada[e] = new bool[origen.Longitud];
            for (var v = 0; v < origen.Longitud; v++) hPermutada[e][permutacion[v]] = h[e][v];
        }
        var posiciones = Enumerable.Range(0, origen.BitsDeMensaje).Select(i => permutacion[i]).ToArray();
        var codigo = CodigoLdpc.DesdeMatrizDeParidad(hPermutada, posiciones);
        var corrector = new DecodificadorDeCreencia(codigo);

        for (var intento = 0; intento < 50; intento++)
        {
            var mensaje = MensajeAlAzar(origen.BitsDeMensaje, azar);
            var palabra = codigo.Codificar(mensaje);
            codigo.CumpleParidad(palabra).Should().BeTrue();

            // La palabra permutada es la del codigo de origen recolocada.
            var deOrigen = origen.Codificar(mensaje);
            for (var v = 0; v < origen.Longitud; v++) palabra[permutacion[v]].Should().Be(deOrigen[v]);

            var extraido = new byte[origen.BitsDeMensaje];
            codigo.ExtraerMensaje(palabra, extraido);
            extraido.Should().Equal(mensaje);

            // Con dos bits estropeados y confianzas flojas en ellos, el corrector los arregla.
            var confianzas = new float[codigo.Longitud];
            for (var v = 0; v < codigo.Longitud; v++) confianzas[v] = palabra[v] == 0 ? 6f : -6f;
            confianzas[azar.Next(codigo.Longitud)] *= -0.3f;
            confianzas[azar.Next(codigo.Longitud)] *= -0.3f;
            var recuperada = new byte[codigo.Longitud];
            corrector.TryDecodificar(confianzas, recuperada).Should().BeTrue();
            recuperada.Should().Equal(palabra);
        }
    }

    [Theory]
    [InlineData("tablas-msk144.txt", 128, 90, 38, 10, 11, 384)]
    [InlineData("tablas-msk40.txt", 32, 16, 16, 5, 7, 96)]
    [InlineData("tablas-fst4.txt", 240, 101, 139, 5, 6, 720)]
    [InlineData("tablas-fst4w.txt", 240, 74, 166, 4, 5, 720)]
    public void LasTablasNuevasCargadasSonLasDelProtocolo(string fichero, int longitud, int bits, int ecuaciones, int pesoMinimo, int pesoMaximo, int unos)
    {
        var tabla = TablaLdpc.Cargar(fichero, longitud, bits, TablasDelRepositorio.Ruta(fichero));
        tabla.EsElCodigoReal.Should().BeTrue(tabla.Procedencia);
        var codigo = tabla.Codigo;
        codigo.Longitud.Should().Be(longitud);
        codigo.BitsDeMensaje.Should().Be(bits);
        codigo.Ecuaciones.Should().Be(ecuaciones);

        var pesos = codigo.VariablesDeCadaEcuacion.Select(e => e.Length).ToList();
        pesos.Min().Should().Be(pesoMinimo);
        pesos.Max().Should().Be(pesoMaximo);
        pesos.Sum().Should().Be(unos);
        codigo.EcuacionesDeCadaBit.Should().OnlyContain(c => c.Length == 3, "todos los codigos de la familia tienen tres unos por columna");
        codigo.ParejasDeEcuacionesQueSeSolapan().Should().Be(0, "no hay ciclos de longitud cuatro");

        // Ida y vuelta sin ruido, con el corrector de creencias de siempre.
        var corrector = new DecodificadorDeCreencia(codigo);
        var azar = new Random(7);
        for (var intento = 0; intento < 20; intento++)
        {
            var mensaje = MensajeAlAzar(bits, azar);
            var palabra = codigo.Codificar(mensaje);
            codigo.CumpleParidad(palabra).Should().BeTrue();
            var confianzas = palabra.Select(b => b == 0 ? 8f : -8f).ToArray();
            var recuperada = new byte[longitud];
            corrector.TryDecodificar(confianzas, recuperada).Should().BeTrue();
            recuperada.Should().Equal(palabra);
            var extraido = new byte[bits];
            codigo.ExtraerMensaje(recuperada, extraido);
            extraido.Should().Equal(mensaje);
        }
    }

    [Fact]
    public void ElCodigoCortoLlevaElMensajeRepartidoComoDiceElFichero()
    {
        var codigo = TablasDelRepositorio.Msk40.Codigo;
        codigo.PosicionesDeMensaje.Should().Equal([16, 12, 18, 19, 7, 21, 22, 11, 24, 5, 26, 14, 9, 29, 30, 31]);
        codigo.PosicionesDeParidad.Should().Equal([0, 1, 2, 3, 4, 6, 8, 10, 13, 15, 17, 20, 23, 25, 27, 28]);
    }

    [Fact]
    public void UnaErrataEnLaGeneradoraRechazaElFicheroEntero()
    {
        var lineas = File.ReadAllLines(TablasDelRepositorio.Ruta("tablas-msk144.txt"));
        var indice = Array.FindIndex(lineas, l => l.StartsWith("GENERADORA", StringComparison.Ordinal));
        // Se cambia el primer digito hexadecimal, que es un bit de verdad y no relleno.
        var linea = lineas[indice];
        var cabecera = "GENERADORA ";
        var primero = linea[cabecera.Length];
        lineas[indice] = cabecera + (primero == '0' ? '8' : '0') + linea[(cabecera.Length + 1)..];
        var acto = () => TablaLdpc.Leer(lineas, 128, 90);
        acto.Should().Throw<FormatException>().WithMessage("*no dicen lo mismo*");
    }

    [Fact]
    public void UnFicheroDeOtrasDimensionesSeRechaza()
    {
        var lineas = File.ReadAllLines(TablasDelRepositorio.Ruta("tablas-msk144.txt"));
        var acto = () => TablaLdpc.Leer(lineas, 240, 101);
        acto.Should().Throw<FormatException>();

        var tabla = TablaLdpc.Cargar("no-existe.txt", 128, 90, Path.Combine(Path.GetTempPath(), "no-existe-nodisla.txt"));
        tabla.EsElCodigoReal.Should().BeFalse();
        tabla.Codigo.Longitud.Should().Be(128);
        tabla.Codigo.BitsDeMensaje.Should().Be(90);
    }

    private static bool[][] MatrizDe(CodigoLdpc codigo)
    {
        var h = new bool[codigo.Ecuaciones][];
        for (var e = 0; e < codigo.Ecuaciones; e++)
        {
            h[e] = new bool[codigo.Longitud];
            foreach (var v in codigo.VariablesDeCadaEcuacion[e]) h[e][v] = true;
        }
        return h;
    }

    private static byte[] MensajeAlAzar(int bits, Random azar)
    {
        var mensaje = new byte[bits];
        for (var i = 0; i < bits; i++) mensaje[i] = (byte)azar.Next(2);
        return mensaje;
    }
}
