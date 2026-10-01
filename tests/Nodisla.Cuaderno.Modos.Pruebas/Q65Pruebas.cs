using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Modos.Ft8;
using Nodisla.Cuaderno.Modos.Q65;
using Nodisla.Cuaderno.Modos.Senal;

namespace Nodisla.Cuaderno.Modos.Pruebas;

/// <summary>Donde estan las tablas de Q65 mientras el fichero no viaje junto al programa.</summary>
public static class TablasDeQ65DePruebas
{
    /// <summary>Ruta del fichero de tablas dentro del arbol de fuentes.</summary>
    /// <remarks>
    /// Se parte de la ruta de este mismo fichero fuente, que el compilador deja escrita, y no de
    /// donde este el binario: asi da igual con que carpeta de salida se compile.
    /// </remarks>
    public static string Ruta([System.Runtime.CompilerServices.CallerFilePath] string esteFichero = "")
    {
        var carpeta = new DirectoryInfo(Path.GetDirectoryName(esteFichero)!);
        while (carpeta is not null && !File.Exists(Path.Combine(carpeta.FullName, "CuadernoNodisla.sln")))
            carpeta = carpeta.Parent;
        carpeta.Should().NotBeNull("las pruebas viven dentro del arbol de la solucion");
        return Path.Combine(carpeta!.FullName, "src", "Nodisla.Cuaderno.Modos", "Tablas", TablasDeQ65.NombreDelFichero);
    }

    /// <summary>Las tablas de verdad, cargadas del arbol de fuentes.</summary>
    public static TablasDeQ65 Reales() => TablasDeQ65.Cargar(Ruta());
}

/// <summary>Pruebas del cuerpo de 64 elementos.</summary>
public class CampoDeGalois64Pruebas
{
    [Fact]
    public void LasPotenciasDeAlfaSonLasDelPolinomioDelProtocolo()
    {
        // Los primeros valores de la tabla de potencias del protocolo, apuntados a mano al traer
        // las tablas: si el polinomio no fuera x^6+x+1 no saldrian estos.
        int[] esperadas = [1, 2, 4, 8, 16, 32, 3, 6, 12, 24, 48, 35, 5, 10, 20, 40, 19, 38, 15, 30];
        for (var i = 0; i < esperadas.Length; i++) CampoDeGalois64.Potencia(i).Should().Be(esperadas[i]);
        CampoDeGalois64.Potencia(62).Should().Be(33);
        CampoDeGalois64.Potencia(63).Should().Be(1, "alfa tiene orden 63");
    }

    [Fact]
    public void CadaElementoTieneInversoYLogaritmoCoherentes()
    {
        for (var a = 1; a < CampoDeGalois64.Orden; a++)
        {
            CampoDeGalois64.Multiplicar(a, CampoDeGalois64.Inverso(a)).Should().Be(1);
            CampoDeGalois64.Potencia(CampoDeGalois64.Logaritmo(a)).Should().Be(a);
        }
        CampoDeGalois64.Multiplicar(0, 17).Should().Be(0);
    }
}

/// <summary>Pruebas de las tablas y del codigo QRA.</summary>
public class CodigoQraPruebas
{
    private static readonly TablasDeQ65 Tablas = TablasDeQ65DePruebas.Reales();

    [Fact]
    public void LaTablaDeVerdadSeCargaYEsLaDelProtocolo()
    {
        Tablas.EsElCodigoReal.Should().BeTrue();
        Tablas.EntradasDelAcumulador.Should().HaveCount(51);
        Tablas.PosicionesDeSincronismo.Should().HaveCount(22);
        Tablas.PosicionesDeSincronismo[0].Should().Be(0);
        Tablas.PosicionesDeSincronismo[^1].Should().Be(84);
        Tablas.PosicionesDeDatos.Should().HaveCount(63);
        Tablas.PolinomioDelCrc.Should().Be(0xF01);
    }

    [Fact]
    public void LosPesosDeCadaSimboloCierranElAcumulador()
    {
        // Es la propiedad de diseno del codigo: un paso mas devuelve el acumulador a cero. Se
        // comprueba codificando: la suma pesada de los 51 pasos sobre cualquier mensaje es cero.
        var codigo = new CodigoQra(Tablas);
        var azar = new Random(3);
        for (var intento = 0; intento < 50; intento++)
        {
            var info = Enumerable.Range(0, 15).Select(_ => azar.Next(64)).ToArray();
            var palabra = codigo.Codificar(info);
            codigo.CumpleLasEcuaciones(palabra).Should().BeTrue();
            palabra.Take(15).Should().Equal(info, "el codigo es sistematico");
        }
    }

    [Fact]
    public void UnSimboloCambiadoRompeAlgunaEcuacion()
    {
        var codigo = new CodigoQra(Tablas);
        var azar = new Random(4);
        var info = Enumerable.Range(0, 15).Select(_ => azar.Next(64)).ToArray();
        var palabra = codigo.Codificar(info);
        for (var i = 0; i < palabra.Length; i++)
        {
            var estropeada = (int[])palabra.Clone();
            estropeada[i] ^= 1 + azar.Next(63);
            codigo.CumpleLasEcuaciones(estropeada).Should().BeFalse($"cambiar el simbolo {i} tiene que notarse");
        }
    }

    [Fact]
    public void UnaErrataEnLaTablaSeRechazaEntera()
    {
        var lineas = File.ReadAllLines(TablasDeQ65DePruebas.Ruta());
        var i = Array.FindIndex(lineas, l => l.StartsWith("PESOS", StringComparison.Ordinal));
        var partes = lineas[i].Split(' ');
        partes[5] = ((int.Parse(partes[5], System.Globalization.CultureInfo.InvariantCulture) + 1) % 63).ToString(System.Globalization.CultureInfo.InvariantCulture);
        lineas[i] = string.Join(' ', partes);
        var lectura = () => TablasDeQ65.Leer(lineas);
        lectura.Should().Throw<FormatException>("un peso mal copiado rompe el cierre del acumulador");
    }

    [Fact]
    public void ElCodigoDePruebasTieneLaMismaFormaYNoEsElReal()
    {
        var pruebas = TablasDeQ65.DePruebas();
        pruebas.EsElCodigoReal.Should().BeFalse();
        pruebas.EntradasDelAcumulador.Should().NotEqual(Tablas.EntradasDelAcumulador);
        var codigo = new CodigoQra(pruebas);
        codigo.CumpleLasEcuaciones(codigo.Codificar(Enumerable.Range(0, 15).Select(i => (i * 7) % 64).ToArray())).Should().BeTrue();
    }

    [Fact]
    public void ElSelloDeDoceBitsNotaCualquierSimboloCambiado()
    {
        var azar = new Random(11);
        for (var intento = 0; intento < 100; intento++)
        {
            var simbolos = Enumerable.Range(0, 13).Select(_ => azar.Next(64)).ToArray();
            var (bajo, alto) = Crc12DeQ65.Calcular(simbolos, Tablas.PolinomioDelCrc);
            bajo.Should().BeInRange(0, 63);
            alto.Should().BeInRange(0, 63);
            var estropeados = (int[])simbolos.Clone();
            estropeados[azar.Next(13)] ^= 1 << azar.Next(6);
            Crc12DeQ65.Calcular(estropeados, Tablas.PolinomioDelCrc).Should().NotBe((bajo, alto));
        }
    }

    [Fact]
    public void UnMensajeVaYVuelveIgualPorLosSimbolos()
    {
        MensajeDe77Bits.TryEmpaquetar("CQ EA8DLF IL18", out var bits, out _).Should().BeTrue();
        var info = MensajeDeQ65.InformacionDe(bits, Tablas.PolinomioDelCrc);
        info.Should().HaveCount(15);
        (info[12] & 1).Should().Be(0, "el ultimo simbolo lleva el bit de relleno a cero");
        MensajeDeQ65.TryBitsDeLaInformacion(info, Tablas.PolinomioDelCrc, out var vuelta).Should().BeTrue();
        vuelta.Should().Equal(bits);

        var codigo = new CodigoQra(Tablas);
        var tonos = MensajeDeQ65.TonosDe(codigo.Codificar(info), Tablas);
        tonos.Should().HaveCount(85);
        foreach (var s in Tablas.PosicionesDeSincronismo) tonos[s].Should().Be(0);
        foreach (var d in Tablas.PosicionesDeDatos) tonos[d].Should().BeInRange(1, 64);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(14)]
    public void LaPropagacionDeCreenciasArreglaLosSimbolosMalos(int errores)
    {
        var codigo = new CodigoQra(Tablas);
        var decodificador = new DecodificadorQra(codigo);
        var azar = new Random(100 + errores);
        var info = Enumerable.Range(0, 15).Select(_ => azar.Next(64)).ToArray();
        var palabra = codigo.Codificar(info);

        // Probabilidades: el simbolo correcto con 0,6 y el resto repartido, salvo en los
        // simbolos estropeados, donde el 0,6 se lo lleva otro valor. Los dos del sello, planos.
        var intrinsecas = new double[65 * 64];
        var estropeados = Enumerable.Range(0, 65).Where(i => i is not 13 and not 14).OrderBy(_ => azar.Next()).Take(errores).ToHashSet();
        for (var v = 0; v < 65; v++)
        {
            for (var s = 0; s < 64; s++) intrinsecas[(v * 64) + s] = 0.4 / 63;
            if (v is 13 or 14) { for (var s = 0; s < 64; s++) intrinsecas[(v * 64) + s] = 1.0 / 64; continue; }
            var creido = estropeados.Contains(v) ? (palabra[v] + 1 + azar.Next(63)) % 64 : palabra[v];
            intrinsecas[(v * 64) + creido] = 0.6;
        }

        var salida = new int[65];
        decodificador.TryDecodificar(intrinsecas, salida, out var vueltas).Should().BeTrue();
        salida.Should().Equal(palabra);
        vueltas.Should().BeLessThan(decodificador.VueltasMaximas);
    }

    [Fact]
    public void ConProbabilidadesPlanasNoSeInventaNada()
    {
        var decodificador = new DecodificadorQra(new CodigoQra(Tablas));
        var intrinsecas = new double[65 * 64];
        Array.Fill(intrinsecas, 1.0 / 64);
        var salida = new int[65];
        // Con todo plano las decisiones son todo ceros, que si cumple las ecuaciones: es la
        // palabra nula, y la rechaza el sello y el desempaquetado, no el codigo. Aqui se
        // comprueba que con ruido al azar no cierra ninguna palabra.
        var azar = new Random(7);
        var cerradas = 0;
        for (var intento = 0; intento < 20; intento++)
        {
            for (var i = 0; i < intrinsecas.Length; i++) intrinsecas[i] = azar.NextDouble();
            if (decodificador.TryDecodificar(intrinsecas, salida, out _)) cerradas++;
        }
        cerradas.Should().Be(0);
    }
}

/// <summary>Pruebas de la transformada de raices mixtas.</summary>
public class TransformadaMixtaPruebas
{
    [Theory]
    [InlineData(1800)]
    [InlineData(7200)]
    [InlineData(16000)]
    [InlineData(41472)]
    [InlineData(60)]
    public void CoincideConLaTransformadaDirecta(int n)
    {
        var azar = new Random(n);
        var re = new double[n];
        var im = new double[n];
        for (var i = 0; i < n; i++) { re[i] = azar.NextDouble() - 0.5; im[i] = azar.NextDouble() - 0.5; }
        var esperadaRe = new double[n];
        var esperadaIm = new double[n];
        var casillas = new[] { 0, 1, 7, n / 3, n / 2, n - 1 };
        foreach (var k in casillas)
        {
            double sr = 0, si = 0;
            for (var t = 0; t < n; t++)
            {
                var a = -2 * Math.PI * k * t / n;
                sr += (re[t] * Math.Cos(a)) - (im[t] * Math.Sin(a));
                si += (re[t] * Math.Sin(a)) + (im[t] * Math.Cos(a));
            }
            esperadaRe[k] = sr;
            esperadaIm[k] = si;
        }

        new TransformadaMixta(n).Transformar(re, im);
        foreach (var k in casillas)
        {
            re[k].Should().BeApproximately(esperadaRe[k], 1e-6 * n);
            im[k].Should().BeApproximately(esperadaIm[k], 1e-6 * n);
        }
    }

    [Fact]
    public void UnaLongitudConFactorSieteSeRechaza()
    {
        var construir = () => new TransformadaMixta(14);
        construir.Should().Throw<ArgumentException>();
    }
}

/// <summary>Pruebas de la cadena entera de Q65: generar, meter ruido, decodificar.</summary>
public class CadenaDeQ65Pruebas
{
    private static readonly TablasDeQ65 Tablas = TablasDeQ65DePruebas.Reales();

    [Theory]
    [InlineData(30, SubmodoDeQ65.A)]
    [InlineData(60, SubmodoDeQ65.A)]
    [InlineData(120, SubmodoDeQ65.A)]
    [InlineData(60, SubmodoDeQ65.C)]
    public void UnMensajeConSenalHolgadaVuelveIgual(int periodo, SubmodoDeQ65 submodo)
    {
        var modo = new ModoQ65(ParametrosDeQ65.De(periodo, submodo), Tablas) { UsarPromediado = false };
        modo.TryTonos("CQ EA8DLF IL18", out var tonos, out _).Should().BeTrue();
        var azar = new Random(periodo);
        var ventana = GeneradorDeSenalDeQ65.Ventana(modo.Parametros, tonos, 1200, 0.2, -15, ParametrosDeQ65.FrecuenciaDeAnalisis, azar);

        var salida = modo.Decodificar(ventana, DateTimeOffset.UnixEpoch, CancellationToken.None);
        salida.Should().ContainSingle();
        salida[0].Texto.Should().Be("CQ EA8DLF IL18");
        salida[0].EsCq.Should().BeTrue();
        salida[0].TonoHz.Should().BeCloseTo(1200, 2);
        salida[0].DesfaseSegundos.Should().BeApproximately(0.2, modo.Parametros.DuracionDeSimboloSegundos / 2);
        salida[0].Decibelios.Should().BeInRange(-18, -12);
    }

    [Fact]
    public void LaSenalGeneradaA48000YRemuestreadaSeDecodifica()
    {
        var modo = new ModoQ65(ParametrosDeQ65.De(30, SubmodoDeQ65.A), Tablas) { UsarPromediado = false };
        var senal = modo.Generar("EA8DLF K1ABC FN42", 1500, 48000);
        senal.Length.Should().Be((int)Math.Round(85 * 0.3 * 48000));
        ((double)senal.Max()).Should().BeApproximately(0.5, 0.01);

        var ventana = new float[30 * 48000];
        var comienzo = (int)Math.Round(modo.ComienzoNominalSegundos * 48000);
        for (var i = 0; i < senal.Length; i++) ventana[comienzo + i] = senal[i];
        GeneradorDeSenalDeQ65.AnadirRuido(ventana, 0.125, -10, 48000, new Random(5));

        var a6000 = Remuestreador.Remuestrear(ventana, 48000, ParametrosDeQ65.FrecuenciaDeAnalisis);
        var salida = modo.Decodificar(a6000, DateTimeOffset.UnixEpoch, CancellationToken.None);
        salida.Should().ContainSingle().Which.Texto.Should().Be("EA8DLF K1ABC FN42");
        salida[0].DesfaseSegundos.Should().BeApproximately(0, 0.1);
    }

    [Fact]
    public void DosSenalesEnLaMismaVentanaSalenLasDos()
    {
        var modo = new ModoQ65(ParametrosDeQ65.De(60, SubmodoDeQ65.A), Tablas) { UsarPromediado = false };
        modo.TryTonos("CQ EA8DLF IL18", out var t1, out _).Should().BeTrue();
        modo.TryTonos("EA1ABC EA8DLF -07", out var t2, out _).Should().BeTrue();
        var azar = new Random(60);
        var ventana = GeneradorDeSenalDeQ65.Ventana(modo.Parametros, t1, 900, 0.0, -20, 6000, azar);
        var otra = GeneradorDeSenalDeQ65.Sintetizar(modo.Parametros, t2, 1700, 6000, 0.35);
        var comienzo = (int)Math.Round((modo.ComienzoNominalSegundos + 1.5) * 6000);
        for (var i = 0; i < otra.Length; i++) ventana[comienzo + i] += otra[i];

        var salida = modo.Decodificar(ventana, DateTimeOffset.UnixEpoch, CancellationToken.None);
        salida.Select(d => d.Texto).Should().BeEquivalentTo(["CQ EA8DLF IL18", "EA1ABC EA8DLF -07"]);
    }

    [Fact]
    public void ConRuidoPuroNoSaleNadaConNiSinAp()
    {
        var modo = new ModoQ65(ParametrosDeQ65.De(30, SubmodoDeQ65.A), Tablas) { UsarPromediado = false };
        for (var i = 0; i < 4; i++)
        {
            var ruido = GeneradorDeSenalDeQ65.SoloRuido(modo.Parametros, 6000, new Random(900 + i));
            modo.MiIndicativo = null;
            modo.Decodificar(ruido, DateTimeOffset.UnixEpoch, CancellationToken.None).Should().BeEmpty();
            modo.MiIndicativo = "EA8DLF";
            modo.IndicativoDx = "K1ABC";
            modo.Decodificar(ruido, DateTimeOffset.UnixEpoch, CancellationToken.None).Should().BeEmpty();
        }
    }

    [Fact]
    public void UnaSenalConElSelloMalNoSeDecodifica()
    {
        // Una palabra de codigo perfectamente valida cuyo sello no cuadra: la emitio alguien
        // que no habla Q65, o es basura con estructura. No puede salir nada.
        var modo = new ModoQ65(ParametrosDeQ65.De(30, SubmodoDeQ65.A), Tablas) { UsarPromediado = false };
        MensajeDe77Bits.TryEmpaquetar("CQ EA8DLF IL18", out var bits, out _).Should().BeTrue();
        var info = MensajeDeQ65.InformacionDe(bits, Tablas.PolinomioDelCrc);
        info[13] ^= 0x15;
        var codigo = new CodigoQra(Tablas);
        var tonos = MensajeDeQ65.TonosDe(codigo.Codificar(info), Tablas);
        var ventana = GeneradorDeSenalDeQ65.Ventana(modo.Parametros, tonos, 1000, 0, -10, 6000, new Random(1));

        modo.Decodificar(ventana, DateTimeOffset.UnixEpoch, CancellationToken.None).Should().BeEmpty();
        modo.UltimasCuentas.Candidatas.Should().BeGreaterThan(0, "el sincronismo si esta");
        modo.UltimasCuentas.RechazadasPorElCrc.Should().BeGreaterThan(0);
    }

    [Fact]
    public void UnaSenalConSimbolosEstropeadosSoloPuedeDarElMensajeOriginal()
    {
        var modo = new ModoQ65(ParametrosDeQ65.De(30, SubmodoDeQ65.A), Tablas) { UsarPromediado = false, MiIndicativo = "EA8DLF", IndicativoDx = "K1ABC" };
        modo.TryTonos("EA8DLF K1ABC R-12", out var tonos, out _).Should().BeTrue();
        var azar = new Random(77);
        for (var intento = 0; intento < 5; intento++)
        {
            var estropeados = (byte[])tonos.Clone();
            foreach (var d in Tablas.PosicionesDeDatos.OrderBy(_ => azar.Next()).Take(12))
                estropeados[d] = (byte)(1 + azar.Next(64));
            var ventana = GeneradorDeSenalDeQ65.Ventana(modo.Parametros, estropeados, 1400, 0.1, -12, 6000, azar);
            var salida = modo.Decodificar(ventana, DateTimeOffset.UnixEpoch, CancellationToken.None);
            salida.Select(d => d.Texto).Should().OnlyContain(t => t == "EA8DLF K1ABC R-12");
        }
    }

    [Fact]
    public void ElPromediadoSacaLoQueUnPeriodoSoloNoSaca()
    {
        var modo = new ModoQ65(ParametrosDeQ65.De(60, SubmodoDeQ65.A), Tablas) { UsarPromediado = true };
        modo.TryTonos("CQ EA8DLF IL18", out var tonos, out _).Should().BeTrue();
        var azar = new Random(2026);
        const double Db = -31.5;
        var decodificados = 0;
        var promediados = 0;
        for (var periodo = 0; periodo < 6; periodo++)
        {
            var ventana = GeneradorDeSenalDeQ65.Ventana(modo.Parametros, tonos, 1100, 0.0, Db, 6000, azar);
            var salida = modo.Decodificar(ventana, DateTimeOffset.UnixEpoch.AddMinutes(periodo), CancellationToken.None);
            salida.Should().OnlyContain(d => d.Texto == "CQ EA8DLF IL18");
            decodificados += salida.Count;
            promediados += salida.Count(d => d.EsRecuperacionProfunda);
        }
        decodificados.Should().BeGreaterThan(0, "sumando periodos tiene que salir");
        promediados.Should().BeGreaterThan(0, "y tiene que haber salido de la suma, no de un periodo suelto");
    }

    [Fact]
    public void ElModoDeclaraLoQueElMarcoNecesita()
    {
        var modo = new ModoQ65(ParametrosDeQ65.De(120, SubmodoDeQ65.A), Tablas);
        modo.Modo.Should().Be(ModoDelModem.Q65);
        modo.Periodo.Should().Be(TimeSpan.FromSeconds(120));
        modo.FrecuenciaDeAnalisis.Should().Be(6000);
        modo.ComienzoNominalSegundos.Should().Be(1.0);
        modo.Parametros.Nombre.Should().Be("Q65-120A");
        modo.Parametros.EspaciadoDeTonosHz.Should().BeApproximately(0.75, 1e-9);
        ParametrosDeQ65.De(60, SubmodoDeQ65.A).EspaciadoDeTonosHz.Should().BeApproximately(1.6667, 1e-3);
        ParametrosDeQ65.De(30, SubmodoDeQ65.A).ComienzoNominalSegundos.Should().Be(0.5);
        var generar = () => modo.Generar("ESTO NO CABE EN NINGUN FORMATO DE Q65", 1000, 48000);
        generar.Should().Throw<FormatException>();
        var noCabe = () => ParametrosDeQ65.De(15, SubmodoDeQ65.E);
        noCabe.Should().Throw<ArgumentException>();
    }
}
