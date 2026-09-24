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

        // Con hasta unos diez bits mal tiene que salir siempre; a partir de ahi empieza a costar,
        // que es exactamente la forma de la curva de un codigo de este tamano.
        if (errores <= 10) arreglados.Should().Be(Intentos, $"con {errores} bits mal el corrector no debería fallar");
        else arreglados.Should().BeGreaterThan(0, "aun con muchos bits mal se recupera algo");
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
