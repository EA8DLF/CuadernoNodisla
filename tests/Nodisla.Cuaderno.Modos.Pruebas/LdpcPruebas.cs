using FluentAssertions;
using Nodisla.Cuaderno.Modos.Ldpc;

namespace Nodisla.Cuaderno.Modos.Pruebas;

/// <summary>
/// Pruebas del codigo corrector de errores.
/// </summary>
/// <remarks>
/// Se prueban con el codigo de pruebas, que tiene las mismas dimensiones que el de FT8. Lo que
/// se comprueba aqui es la maquinaria —despejar la paridad a partir de la matriz, codificar,
/// propagar creencias— y esa maquinaria es la misma sea cual sea la matriz. Cuando llegue la
/// tabla de verdad, estas pruebas valen tal cual.
/// </remarks>
public class CodigoLdpcPruebas
{
    private static readonly CodigoLdpc Codigo = CodigoLdpc.ConstruirDePrueba();

    [Fact]
    public void TieneLasDimensionesDeFt8()
    {
        Codigo.Longitud.Should().Be(174);
        Codigo.BitsDeMensaje.Should().Be(91);
        Codigo.Ecuaciones.Should().Be(83);
    }

    [Fact]
    public void LoCodificadoCumpleTodasLasEcuaciones()
    {
        // El codificador se deduce de la propia matriz de paridad por eliminación gaussiana.
        // Si esa deducción tuviera un fallo, esto no cuadraría.
        var azar = new Random(4);
        for (var intento = 0; intento < 100; intento++)
        {
            var mensaje = new byte[Codigo.BitsDeMensaje];
            for (var i = 0; i < mensaje.Length; i++) mensaje[i] = (byte)azar.Next(2);

            var palabra = Codigo.Codificar(mensaje);
            palabra.Should().HaveCount(Codigo.Longitud);
            palabra.AsSpan(0, Codigo.BitsDeMensaje).ToArray().Should().Equal(mensaje,
                "el código es sistemático: el mensaje viaja tal cual en los primeros bits");
            Codigo.CumpleParidad(palabra).Should().BeTrue();
        }
    }

    [Fact]
    public void UnBitCambiadoRompeAlgunaEcuacion()
    {
        var mensaje = new byte[Codigo.BitsDeMensaje];
        new Random(6).NextBytes(mensaje);
        for (var i = 0; i < mensaje.Length; i++) mensaje[i] &= 1;
        var palabra = Codigo.Codificar(mensaje);

        for (var bit = 0; bit < Codigo.Longitud; bit++)
        {
            var estropeada = palabra.ToArray();
            estropeada[bit] ^= 1;
            Codigo.EcuacionesQueFallan(estropeada).Should().BeGreaterThan(0);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(10)]
    [InlineData(16)]
    public void LaPropagacionDeCreenciasArreglaLosBitsMalos(int errores)
    {
        var corrector = new DecodificadorDeCreencia(Codigo);
        var azar = new Random(100 + errores);
        var arreglados = 0;
        const int Intentos = 40;

        for (var intento = 0; intento < Intentos; intento++)
        {
            var mensaje = new byte[Codigo.BitsDeMensaje];
            for (var i = 0; i < mensaje.Length; i++) mensaje[i] = (byte)azar.Next(2);
            var palabra = Codigo.Codificar(mensaje);

            // Confianzas fuertes y correctas, salvo en los bits estropeados. Positivo es cero.
            var confianzas = new float[Codigo.Longitud];
            for (var i = 0; i < confianzas.Length; i++) confianzas[i] = palabra[i] == 0 ? 4f : -4f;
            foreach (var bit in DistintosAlAzar(azar, Codigo.Longitud, errores)) confianzas[bit] = -confianzas[bit];

            var recuperada = new byte[Codigo.Longitud];
            if (corrector.TryDecodificar(confianzas, recuperada) && recuperada.SequenceEqual(palabra)) arreglados++;
        }

        // Los topes salen de medir, no de desear. Este es el caso peor posible: los bits malos
        // no llegan dudosos sino con toda la confianza puesta del reves, que es lo contrario de
        // lo que hace el ruido de verdad —ahi un bit estropeado suele llegar dudoso y se arregla
        // mucho mejor—. Un codigo de 174 bits con 91 de mensaje tiene una distancia minima de
        // unas dos decenas de bits, asi que diez errores tan tercos ya rozan su limite teorico.
        var minimo = errores switch
        {
            <= 4 => Intentos,          // hasta cuatro, siempre
            <= 10 => Intentos * 6 / 10, // a diez, la mayoria
            _ => 1,                     // mas alla, lo que se pueda
        };
        arreglados.Should().BeGreaterThanOrEqualTo(minimo,
            $"con {errores} bits invertidos con plena confianza el corrector recupera al menos {minimo} de {Intentos}");
    }

    [Fact]
    public void ElCodigoDePruebasNoTieneCiclosCortos()
    {
        // Dos ecuaciones que comparten dos variables forman un ciclo de cuatro, y la propagacion
        // de creencias se atasca en ellos. Un codigo con muchos rendiría por debajo de lo que
        // rendiría el de FT8, y las cifras del banco engañarían por lo bajo.
        Codigo.ParejasDeEcuacionesQueSeSolapan().Should().Be(0);
    }

    [Fact]
    public void ElCodigoSeConstruyeIgualConLaMismaSemilla()
    {
        // Sin esto, el banco de medida no sería repetible y sus cifras no valdrían para comparar.
        var uno = CodigoLdpc.ConstruirDePrueba(8);
        var otro = CodigoLdpc.ConstruirDePrueba(8);
        for (var e = 0; e < uno.Ecuaciones; e++)
            uno.VariablesDeCadaEcuacion[e].Should().Equal(otro.VariablesDeCadaEcuacion[e]);
    }

    [Fact]
    public void UnaMatrizQueNoSePuedeDespejarSeRechaza()
    {
        // Dos ecuaciones iguales no permiten despejar dos bits de paridad distintos. Vale mas
        // enterarse al construir el codigo que decodificar basura durante meses.
        var h = new bool[2][];
        h[0] = [true, false, true, false];
        h[1] = [true, false, true, false];
        var accion = () => CodigoLdpc.DesdeMatrizDeParidad(h, 2);
        accion.Should().Throw<ArgumentException>();
    }

    private static IEnumerable<int> DistintosAlAzar(Random azar, int tope, int cuantos)
    {
        var elegidos = new HashSet<int>();
        while (elegidos.Count < cuantos) elegidos.Add(azar.Next(tope));
        return elegidos;
    }
}

/// <summary>Pruebas de la carga de las tablas del protocolo.</summary>
public class TablasDelProtocoloPruebas
{
    [Fact]
    public void SinFicheroSeAvisaYSeUsaElCodigoDePruebas()
    {
        var tablas = TablasDelProtocolo.Cargar(Path.Combine(Path.GetTempPath(), "no-existe-nada.txt"));

        tablas.EsElCodigoReal.Should().BeFalse("el módem tiene que decir que no tiene la tabla de verdad");
        tablas.Ldpc.Longitud.Should().Be(174);
        tablas.MezclaDeFt4.Should().OnlyContain(b => b == 0);
    }

    [Fact]
    public void UnaTablaBienEscritaSeCarga()
    {
        // Se fabrica un fichero con el propio codigo de pruebas para comprobar el formato y el
        // camino de carga completos. Cuando llegue la tabla de verdad entrará por aquí mismo.
        var codigo = CodigoLdpc.ConstruirDePrueba();
        var lineas = new List<string> { "# tabla de prueba", $"{codigo.Longitud} {codigo.BitsDeMensaje}" };
        for (var e = 0; e < codigo.Ecuaciones; e++) lineas.Add(string.Join(' ', codigo.VariablesDeCadaEcuacion[e]));
        lineas.Add("MEZCLA-FT4 0102030405060708090A");

        var ruta = Path.Combine(Path.GetTempPath(), $"nodisla-tablas-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllLines(ruta, lineas);
            var tablas = TablasDelProtocolo.Cargar(ruta);

            tablas.EsElCodigoReal.Should().BeTrue();
            tablas.Ldpc.Ecuaciones.Should().Be(83);
            tablas.MezclaDeFt4.Should().Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 0x0A]);
        }
        finally
        {
            if (File.Exists(ruta)) File.Delete(ruta);
        }
    }

    [Fact]
    public void LaTablaDeVerdadEstaYEsLaQueDiceSerLLDPC()
    {
        // Se carga como la carga el módem, del fichero que va junto al programa.
        var tablas = TablasDelProtocolo.Cargar();
        tablas.EsElCodigoReal.Should().BeTrue(
            "sin la tabla real el módem funciona consigo mismo pero no decodifica a nadie más");

        var codigo = tablas.Ldpc;
        codigo.Longitud.Should().Be(174);
        codigo.BitsDeMensaje.Should().Be(91);
        codigo.Ecuaciones.Should().Be(83);

        // El protocolo declara 24 ecuaciones de siete bits y 59 de seis.
        var pesos = codigo.VariablesDeCadaEcuacion.GroupBy(f => f.Length).ToDictionary(g => g.Key, g => g.Count());
        pesos.Should().BeEquivalentTo(new Dictionary<int, int> { [7] = 24, [6] = 59 });

        // Y cada uno de los 174 bits tiene que aparecer en exactamente tres ecuaciones.
        codigo.EcuacionesDeCadaBit.Should().AllSatisfy(e => e.Should().HaveCount(3));

        // Las dos cuentas del número de unos tienen que dar lo mismo por los dos caminos: es la
        // comprobación que caza una fila mal transcrita sin tener que mirarlas una a una.
        var porFilas = codigo.VariablesDeCadaEcuacion.Sum(f => f.Length);
        var porColumnas = codigo.EcuacionesDeCadaBit.Sum(e => e.Length);
        porFilas.Should().Be(522);
        porColumnas.Should().Be(522);
    }

    [Fact]
    public void LaTablaDeVerdadNoTieneCiclosCortos()
    {
        // Un código bueno no tiene dos ecuaciones que compartan más de un bit. Si la tabla real
        // los tuviera, no sería que el protocolo esté mal: sería que la hemos leído mal.
        TablasDelProtocolo.Cargar().Ldpc.ParejasDeEcuacionesQueSeSolapan().Should().Be(0);
    }

    [Fact]
    public void LaMezclaDeFt4EsSuPropiaInversa()
    {
        var tablas = TablasDelProtocolo.Cargar();
        tablas.MezclaDeFt4.Should().HaveCount(10);
        tablas.MezclaDeFt4.Should().NotBeEquivalentTo(new byte[10], "sin la secuencia, FT4 no revuelve nada");

        // Aplicarla dos veces tiene que dejar el mensaje como estaba: si no, emitir y recibir no
        // serían la misma operación al revés y FT4 no hablaría ni consigo mismo.
        var codificador = new Ft8.Codificador(tablas);
        var azar = new Random(3);
        var original = new byte[Ft8.MensajeDe77Bits.Bits];
        for (var i = 0; i < original.Length; i++) original[i] = (byte)azar.Next(2);

        var revuelto = original.ToArray();
        codificador.AplicarMezclaDeFt4(revuelto);
        revuelto.Should().NotEqual(original);
        codificador.AplicarMezclaDeFt4(revuelto);
        revuelto.Should().Equal(original);
    }

    [Fact]
    public void ConLaTablaDeVerdadUnMensajeVaYVuelveIgual()
    {
        // La comprobación que de verdad importa: los 91 bits entran, salen 174, se corrigen y
        // vuelven idénticos. Si la matriz estuviera mal transcrita en una sola fila, el
        // codificador la usaría igual pero el corrector no convergería.
        var tablas = TablasDelProtocolo.Cargar();
        var corrector = new DecodificadorDeCreencia(tablas.Ldpc);
        var azar = new Random(17);

        for (var intento = 0; intento < 100; intento++)
        {
            var mensaje = new byte[tablas.Ldpc.BitsDeMensaje];
            for (var i = 0; i < mensaje.Length; i++) mensaje[i] = (byte)azar.Next(2);

            var palabra = tablas.Ldpc.Codificar(mensaje);
            tablas.Ldpc.CumpleParidad(palabra).Should().BeTrue();

            var confianzas = new float[tablas.Ldpc.Longitud];
            for (var i = 0; i < confianzas.Length; i++) confianzas[i] = palabra[i] == 0 ? 4f : -4f;

            var recuperada = new byte[tablas.Ldpc.Longitud];
            corrector.TryDecodificar(confianzas, recuperada).Should().BeTrue();
            recuperada.Should().Equal(palabra);
        }
    }

    [Fact]
    public void UnaTablaAMediasSeDescartaEntera()
    {
        // Una tabla incompleta decodificaría basura con el sello cuadrando de vez en cuando.
        var ruta = Path.Combine(Path.GetTempPath(), $"nodisla-rota-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllLines(ruta, ["174 91", "0 1 2", "3 4 5"]);
            TablasDelProtocolo.Cargar(ruta).EsElCodigoReal.Should().BeFalse();
        }
        finally
        {
            if (File.Exists(ruta)) File.Delete(ruta);
        }
    }
}
