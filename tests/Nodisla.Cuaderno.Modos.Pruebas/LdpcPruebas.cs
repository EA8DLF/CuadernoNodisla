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
    public void LasDosTablasDelCodigoDicenLoMismo()
    {
        // El fichero trae la matriz de paridad y la generadora, que describen el mismo código por
        // caminos distintos. Cargar() las cruza; que la carga salga adelante ya significa que
        // coinciden en los 91 mensajes de la base, y por linealidad en todos los demás.
        var ruta = Path.Combine(AppContext.BaseDirectory, TablasDelProtocolo.NombreDelFichero);
        File.Exists(ruta).Should().BeTrue("la tabla del protocolo tiene que ir junto al programa");

        var lineas = File.ReadAllLines(ruta);
        lineas.Count(l => l.TrimStart().StartsWith("GENERADORA", StringComparison.OrdinalIgnoreCase))
              .Should().Be(83, "hace falta una fila de generadora por cada bit de paridad");

        TablasDelProtocolo.Cargar(ruta).EsElCodigoReal.Should().BeTrue();
    }

    [Fact]
    public void UnaErrataDeUnSoloBitEnLaGeneradoraTiraElFicheroEntero()
    {
        // Es la razón de ser de la segunda tabla. Una errata al traer 522 números no da un error
        // visible: da un módem que decodifica basura de vez en cuando con el CRC cuadrando. Aquí
        // se cambia un bit a propósito y se comprueba que el módem no se lo traga.
        var original = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, TablasDelProtocolo.NombreDelFichero));
        var estropeadas = original.ToArray();
        var primera = Array.FindIndex(estropeadas, l => l.TrimStart().StartsWith("GENERADORA", StringComparison.OrdinalIgnoreCase));
        primera.Should().BeGreaterThanOrEqualTo(0);

        // Se le da la vuelta a un solo bit, el primero de la fila: el cambio más pequeño posible.
        // Tiene que ser de los 91 que cuentan y no de los cinco de relleno del final, que no
        // describen nada y por eso no se comprueban.
        var fila = estropeadas[primera];
        var digito = fila.IndexOf("GENERADORA", StringComparison.OrdinalIgnoreCase) + "GENERADORA".Length;
        while (digito < fila.Length && fila[digito] == ' ') digito++;
        var valor = Convert.ToInt32(fila[digito..(digito + 1)], 16);
        estropeadas[primera] = string.Concat(
            fila.AsSpan(0, digito),
            (valor ^ 1).ToString("X", System.Globalization.CultureInfo.InvariantCulture),
            fila.AsSpan(digito + 1));

        var ruta = Path.Combine(Path.GetTempPath(), $"nodisla-errata-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllLines(ruta, estropeadas);
            TablasDelProtocolo.Cargar(ruta).EsElCodigoReal.Should().BeFalse(
                "si las dos tablas no cuadran no se sabe cuál está mal, así que no vale ninguna");
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

/// <summary>
/// Pruebas del freno de la recuperacion profunda.
/// </summary>
/// <remarks>
/// <para>
/// Esta clase no mide sensibilidad: mide <b>honradez</b>. La recuperacion profunda tiene una
/// propiedad incomoda que no comparte con la propagacion de creencias: no se rinde nunca.
/// Dandole ruido puro devuelve igualmente una palabra valida del codigo, porque 91 bits
/// cualesquiera determinan una. Si esa palabra llegara al CRC, y el CRC se tirara doscientas
/// veces por ventana, saldrian indicativos inventados cada pocos minutos.
/// </para>
/// <para>
/// Lo que se comprueba aqui es que el freno esta puesto y funciona. Si alguien sube
/// <see cref="RecuperacionProfunda.ErroresDurosMaximos"/> «para sacar unas pocas mas», estas
/// pruebas se lo dicen antes de que llegue al aire.
/// </para>
/// </remarks>
public class RecuperacionProfundaPruebas
{
    private static readonly TablasDelProtocolo Tablas = TablasDelProtocolo.Cargar();

    [Fact]
    public void ConRuidoPuroCasiNuncaDevuelveNada()
    {
        var con = new RecuperacionProfunda(Tablas.Ldpc);
        var sin = new RecuperacionProfunda(Tablas.Ldpc) { ErroresDurosMaximos = int.MaxValue };
        var palabra = new byte[Tablas.Ldpc.Longitud];
        var confianzas = new float[Tablas.Ldpc.Longitud];

        // Sesenta y no trescientos: con el orden que lleva puesto, cada reconstrucción explora
        // un par de cientos de miles de combinaciones, y esta prueba la llama dos veces por
        // intento. Sesenta bastan de sobra para distinguir «casi siempre» de «casi nunca», que
        // es lo único que se está comprobando aquí.
        const int Intentos = 60;
        int conFreno = 0, sinFreno = 0;
        var azar = new Random(31);
        for (var i = 0; i < Intentos; i++)
        {
            for (var j = 0; j < confianzas.Length; j++)
            {
                // Ruido gaussiano por Box y Muller: ni un bit lleva información.
                var u1 = 1.0 - azar.NextDouble();
                var u2 = azar.NextDouble();
                confianzas[j] = (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
            }
            if (sin.TryRecuperar(confianzas, palabra)) sinFreno++;
            if (con.TryRecuperar(confianzas, palabra)) conFreno++;
        }

        // Sin freno reconstruye prácticamente siempre: esa es justo la propiedad peligrosa.
        sinFreno.Should().BeGreaterThan((int)(Intentos * 0.9),
            "sin freno esta clase siempre encuentra una palabra válida, también en el ruido");

        // Con el freno puesto, el ruido no pasa.
        conFreno.Should().BeLessThan((int)(Intentos * 0.05),
            "si el ruido puro pasa el freno, el CRC es lo único que separa el cuaderno de un contacto inventado");
    }

    [Fact]
    public void ConLaSenalCasiLimpiaSiRecupera()
    {
        // El freno no puede estar tan apretado que se cargue lo que sí es bueno.
        var profunda = new RecuperacionProfunda(Tablas.Ldpc);
        var palabra = new byte[Tablas.Ldpc.Longitud];
        var recuperada = new byte[Tablas.Ldpc.Longitud];
        var confianzas = new float[Tablas.Ldpc.Longitud];
        var azar = new Random(55);
        var aciertos = 0;

        const int Intentos = 40;
        for (var intento = 0; intento < Intentos; intento++)
        {
            var mensaje = new byte[Tablas.Ldpc.BitsDeMensaje];
            for (var i = 0; i < mensaje.Length; i++) mensaje[i] = (byte)azar.Next(2);
            var emitida = Tablas.Ldpc.Codificar(mensaje);

            // Confianzas con magnitudes distintas, que es lo que hace falta: esta clase se apoya
            // en poder ordenar los bits del más fiable al menos fiable, y si todos llegaran con
            // la misma magnitud ese orden sería arbitrario.
            for (var i = 0; i < confianzas.Length; i++)
            {
                var magnitud = 1f + ((float)azar.NextDouble() * 3f);
                confianzas[i] = emitida[i] == 0 ? magnitud : -magnitud;
            }

            // Y diez bits en los que el demodulador se equivoca. Se les pone magnitud pequeña
            // porque es lo que hace el ruido de verdad: un bit que sale mal suele salir dudoso,
            // no seguro del revés.
            var estropeados = new HashSet<int>();
            while (estropeados.Count < 10) estropeados.Add(azar.Next(confianzas.Length));
            foreach (var bit in estropeados)
                confianzas[bit] = emitida[bit] == 0 ? -0.3f : 0.3f;

            if (profunda.TryRecuperar(confianzas, recuperada) && recuperada.SequenceEqual(emitida)) aciertos++;
            emitida.CopyTo(palabra, 0);
        }

        aciertos.Should().BeGreaterThan(Intentos / 2,
            "con diez bits mal de ciento setenta y cuatro la reconstrucción tiene que salir la mayoría de las veces");
    }

    [Fact]
    public void SubirElOrdenNuncaEmpeoraYNuncaSeSaltaElFreno()
    {
        // Lo que se comprueba aquí es el mecanismo, no cuánto rinde: que la búsqueda por órdenes
        // es acumulativa —el orden cuatro prueba todo lo que prueba el uno, y más, así que no
        // puede salir peor— y que nada de lo que devuelve se salta el freno.
        //
        // Cuánto rinde cada orden NO se comprueba aquí y es deliberado: depende del reparto de
        // errores, y un caso sintético se puede amañar para que dé cualquier resultado. Esa
        // cifra sale del banco, con señal y ruido de verdad, y está apuntada en la página de
        // RecuperacionProfunda. Un intento anterior de medirlo aquí daba 40 de 40 con los dos
        // órdenes, que no demostraba nada.
        // La búsqueda va abierta a toda la base a propósito: de serie va estrechada a la cola,
        // que es un freno más, y con ella estrechada los órdenes altos ni siquiera llegan a los
        // bits que aquí se estropean. Lo que se comprueba es el mecanismo, no los valores de
        // serie; las cifras de cuánto rinde cada orden están en el banco.
        var bajo = new RecuperacionProfunda(Tablas.Ldpc) { Orden = 1, ProfundidadDeLaBusqueda = 91 };
        var alto = new RecuperacionProfunda(Tablas.Ldpc) { Orden = 4, ProfundidadDeLaBusqueda = 91 };
        var recuperada = new byte[Tablas.Ldpc.Longitud];
        var confianzas = new float[Tablas.Ldpc.Longitud];
        var azar = new Random(91);
        int conBajo = 0, conAlto = 0;

        const int Intentos = 40;
        for (var intento = 0; intento < Intentos; intento++)
        {
            var mensaje = new byte[Tablas.Ldpc.BitsDeMensaje];
            for (var i = 0; i < mensaje.Length; i++) mensaje[i] = (byte)azar.Next(2);
            var emitida = Tablas.Ldpc.Codificar(mensaje);

            // Casi todo llega con mucha confianza y bien.
            for (var i = 0; i < confianzas.Length; i++)
                confianzas[i] = emitida[i] == 0 ? 6f : -6f;

            // Y hay dos bits dudosos que además salen del revés. Uno solo lo arregla el orden
            // uno dándole la vuelta; dos a la vez, no: hay que probarlos en pareja, y para eso
            // hace falta orden dos o más. Es el caso más limpio para ver la diferencia.
            var estropeados = new HashSet<int>();
            while (estropeados.Count < 2) estropeados.Add(azar.Next(confianzas.Length));
            foreach (var bit in estropeados)
                confianzas[bit] = emitida[bit] == 0 ? -0.2f : 0.2f;

            if (bajo.TryRecuperar(confianzas, recuperada) && recuperada.SequenceEqual(emitida)) conBajo++;
            if (alto.TryRecuperar(confianzas, recuperada))
            {
                alto.UltimosErroresDuros.Should().BeLessThanOrEqualTo(alto.ErroresDurosMaximos,
                    "nada que pase el freno puede contradecir al demodulador más de la cuenta");
                if (recuperada.SequenceEqual(emitida)) conAlto++;
            }
        }

        conBajo.Should().BeGreaterThan(0, "el caso de prueba tiene que ser resoluble, o no mide nada");
        conAlto.Should().BeGreaterThanOrEqualTo(conBajo,
            "la búsqueda es acumulativa: el orden cuatro prueba todo lo del uno y más, " +
            "así que no puede encontrar menos");
    }
}
