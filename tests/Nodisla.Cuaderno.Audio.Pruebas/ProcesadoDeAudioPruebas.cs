using FluentAssertions;
using Nodisla.Cuaderno.Audio.Procesado;
using Xunit.Abstractions;

namespace Nodisla.Cuaderno.Audio.Pruebas;

/// <summary>
/// El procesado de la fonia medido con senales sinteticas: voz de mentira (armonicos con
/// silabas), ruido blanco gaussiano y una portadora. Nada abre una tarjeta de sonido.
/// </summary>
public sealed class ProcesadoDeAudioPruebas(ITestOutputHelper salida)
{
    private const int Frecuencia = 48000;
    private const int Bloque = 480;

    [Theory]
    [InlineData(TipoDeReductor.Espectral, GananciaEspectral.Gaussiana, 3.0)]
    [InlineData(TipoDeReductor.Espectral, GananciaEspectral.Logaritmica, 3.0)]
    [InlineData(TipoDeReductor.Adaptativo, GananciaEspectral.Gaussiana, 1.0)]
    public void El_reductor_mejora_la_relacion_senal_ruido(TipoDeReductor tipo, GananciaEspectral metodo, double mejoraMinimaDb)
    {
        // El NR1 (LMS) aprende despacio y solo sigue lo que se repite: se mide con una vocal
        // sostenida, que es donde se usa. El espectral, con silabas que cambian de tono.
        var voz = tipo == TipoDeReductor.Adaptativo ? Vocal(segundos: 8) : Voz(segundos: 8);
        var ruido = Ruido(voz.Length, amplitud: 0.12, semilla: 3);
        var entrada = Sumar(voz, ruido);

        var cadena = new CadenaDeEscucha
        {
            ReductorActivo = true,
            Tipo = tipo,
            Metodo = metodo,
            Nivel = 1.0,
            LimitadorActivo = false,
        };
        var procesada = Procesar(cadena, entrada);
        var retraso = (int)Math.Round(cadena.LatenciaAnadida(Frecuencia).TotalSeconds * Frecuencia);

        // Se mide despues de los dos primeros segundos, cuando el estimador ya ha aprendido el ruido.
        var desde = 2 * Frecuencia;
        var antes = SnrInvarianteDb(voz, entrada, desde, 0);
        var despues = SnrInvarianteDb(voz, procesada, desde, retraso);
        var ruidoQuitado = PotenciaEnSilenciosDb(voz, entrada, desde, 0) - PotenciaEnSilenciosDb(voz, procesada, desde, retraso);

        // El NR1 predice: devuelve los armonicos con su fase algo cambiada, y una medida muestra a
        // muestra lo cuenta como error. Para el se mide lo que oye el oido: armonicos frente al
        // ruido que queda entre ellos.
        if (tipo == TipoDeReductor.Adaptativo)
        {
            antes = ArmonicosFrenteARuidoDb(entrada, desde);
            despues = ArmonicosFrenteARuidoDb(procesada, desde);
        }

        salida.WriteLine(
            $"{tipo}/{metodo}: S/R {antes:F1} dB -> {despues:F1} dB (mejora {despues - antes:F1} dB); ruido en pausas -{ruidoQuitado:F1} dB; retraso {retraso} muestras");
        (despues - antes).Should().BeGreaterThan(mejoraMinimaDb);
    }

    [Fact]
    public void El_nivel_del_reductor_espectral_gradua_lo_que_quita()
    {
        var voz = Voz(segundos: 6);
        var entrada = Sumar(voz, Ruido(voz.Length, 0.12, 5));
        double Quitado(double nivel)
        {
            var c = new CadenaDeEscucha { ReductorActivo = true, Nivel = nivel, LimitadorActivo = false };
            var p = Procesar(c, entrada);
            var r = ReductorEspectral.RetrasoEnMuestras(Frecuencia);
            return PotenciaEnSilenciosDb(voz, entrada, 2 * Frecuencia, 0) - PotenciaEnSilenciosDb(voz, p, 2 * Frecuencia, r);
        }

        var poco = Quitado(0.3);
        var mucho = Quitado(1.0);
        salida.WriteLine($"Ruido quitado en pausas: nivel 30 % -{poco:F1} dB, nivel 100 % -{mucho:F1} dB");
        mucho.Should().BeGreaterThan(poco + 3);
    }

    [Fact]
    public void Con_nivel_cero_el_reductor_espectral_deja_pasar_la_senal_igual_y_solo_la_retrasa()
    {
        var voz = Voz(segundos: 2);
        var cadena = new CadenaDeEscucha { ReductorActivo = true, Nivel = 0, LimitadorActivo = false };
        var procesada = Procesar(cadena, voz);
        var retraso = ReductorEspectral.RetrasoEnMuestras(Frecuencia);

        var maximoError = 0.0;
        for (var i = retraso + Frecuencia / 10; i < voz.Length; i++)
        {
            maximoError = Math.Max(maximoError, Math.Abs(procesada[i] - voz[i - retraso]));
        }

        maximoError.Should().BeLessThan(1e-4, "con mascara uno la ventana y el solape reconstruyen la senal");
    }

    [Fact]
    public void La_latencia_anadida_se_mide_con_un_impulso_y_esta_por_debajo_de_30_ms()
    {
        foreach (var frecuencia in new[] { 44100, 48000 })
        {
            var cadena = new CadenaDeEscucha { ReductorActivo = true, NotchActivo = true, LimitadorActivo = true, Nivel = 0 };
            var senal = new float[frecuencia];
            senal[frecuencia / 2] = 0.5f;
            var procesada = (float[])senal.Clone();
            for (var i = 0; i < procesada.Length; i += Bloque) cadena.Procesar(procesada.AsSpan(i, Math.Min(Bloque, procesada.Length - i)), frecuencia);

            var pico = 0;
            for (var i = 0; i < procesada.Length; i++)
            {
                if (Math.Abs(procesada[i]) > Math.Abs(procesada[pico])) pico = i;
            }

            var medida = TimeSpan.FromSeconds((double)(pico - (frecuencia / 2)) / frecuencia);
            salida.WriteLine($"{frecuencia} Hz: latencia medida {medida.TotalMilliseconds:F2} ms, declarada {cadena.LatenciaAnadida(frecuencia).TotalMilliseconds:F2} ms");
            medida.TotalMilliseconds.Should().BeApproximately(cadena.LatenciaAnadida(frecuencia).TotalMilliseconds, 0.05);
            medida.Should().BeLessThan(TimeSpan.FromMilliseconds(30));
        }

        // El notch, el NR1 y el limitador no retrasan nada.
        new CadenaDeEscucha { ReductorActivo = true, Tipo = TipoDeReductor.Adaptativo, NotchActivo = true }
            .LatenciaAnadida(48000).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void El_notch_automatico_quita_una_portadora_y_deja_la_voz()
    {
        var voz = Voz(segundos: 6);
        var portadora = new float[voz.Length];
        for (var i = 0; i < portadora.Length; i++) portadora[i] = (float)(0.3 * Math.Sin(2 * Math.PI * 1000.0 * i / Frecuencia));
        var entrada = Sumar(voz, portadora);

        var cadena = new CadenaDeEscucha { NotchActivo = true, LimitadorActivo = false };
        var procesada = Procesar(cadena, entrada);

        var desde = 4 * Frecuencia;
        var antes = Goertzel(entrada, desde, 1000);
        var despues = Goertzel(procesada, desde, 1000);
        var vozAntes = Goertzel(entrada, desde, 450);
        var vozDespues = Goertzel(procesada, desde, 450);
        salida.WriteLine($"Portadora de 1 kHz: -{antes - despues:F1} dB; armonico de voz a 450 Hz: {vozDespues - vozAntes:+0.0;-0.0} dB");

        (antes - despues).Should().BeGreaterThan(20, "la portadora tiene que desaparecer");
    }

    [Fact]
    public void El_limitador_no_pasa_nunca_del_techo()
    {
        var azar = new Random(11);
        var entrada = new float[Frecuencia * 3];
        for (var i = 0; i < entrada.Length; i++)
        {
            var golpe = (i / 4800) % 3 == 0 ? 3.0 : 0.4;
            entrada[i] = (float)(golpe * Math.Sin(2 * Math.PI * 300 * i / Frecuencia) + ((azar.NextDouble() - 0.5) * 0.5));
        }

        foreach (var techoDb in new[] { -1.0, -3.0, -10.0 })
        {
            var cadena = new CadenaDeEscucha { LimitadorActivo = true, TechoDb = techoDb };
            var procesada = Procesar(cadena, entrada);
            var techo = Math.Pow(10, techoDb / 20);
            var maximo = procesada.Max(Math.Abs);
            salida.WriteLine($"Techo {techoDb} dBFS ({techo:F4}): pico de salida {maximo:F4}, entrada hasta {entrada.Max(Math.Abs):F2}");
            maximo.Should().BeLessThanOrEqualTo((float)techo);
        }
    }

    [Fact]
    public void El_procesado_del_micro_nunca_sube_el_nivel_ni_pasa_del_techo()
    {
        var voz = Voz(segundos: 4, amplitud: 0.25);
        var entrada = Sumar(voz, Ruido(voz.Length, 0.01, 9));

        var micro = new CadenaDeMicrofono
        {
            Activo = true,
            PuertaActiva = true,
            UmbralPuertaDb = -40,
            CorteDeGravesHz = 150,
            GravesDb = 6,
            MediosDb = 6,
            AgudosDb = 6,
            CompresorActivo = true,
            UmbralCompresorDb = -30,
            Relacion = 4,
            TechoDb = -3,
        };

        var techo = (float)Math.Pow(10, -3 / 20.0);
        var copia = (float[])entrada.Clone();
        var bloquesQueSuben = 0;
        for (var i = 0; i < copia.Length; i += Bloque)
        {
            var trozo = copia.AsSpan(i, Math.Min(Bloque, copia.Length - i));
            var picoAntes = 0f;
            foreach (var m in trozo) picoAntes = Math.Max(picoAntes, Math.Abs(m));
            micro.Procesar(trozo, Frecuencia);
            var picoDespues = 0f;
            foreach (var m in trozo) picoDespues = Math.Max(picoDespues, Math.Abs(m));
            if (picoDespues > picoAntes + 1e-6f || picoDespues > techo + 1e-6f) bloquesQueSuben++;
        }

        bloquesQueSuben.Should().Be(0, "ningun bloque sale mas alto que entro ni por encima del techo");

        // Y apagado, pasa tal cual.
        var apagado = new CadenaDeMicrofono { Activo = false };
        var igual = (float[])entrada.Clone();
        apagado.Procesar(igual, Frecuencia);
        igual.Should().Equal(entrada);
    }

    [Fact]
    public void La_puerta_baja_el_ruido_de_fondo_cuando_no_se_habla()
    {
        var ruido = Ruido(Frecuencia * 2, 0.003, 2); // unos -55 dBFS
        var micro = new CadenaDeMicrofono { Activo = true, PuertaActiva = true, UmbralPuertaDb = -30, CompresorActivo = false, CorteDeGravesHz = 0 };
        var procesado = (float[])ruido.Clone();
        for (var i = 0; i < procesado.Length; i += Bloque) micro.Procesar(procesado.AsSpan(i, Bloque), Frecuencia);

        var antes = PotenciaDb(ruido, Frecuencia, ruido.Length);
        var despues = PotenciaDb(procesado, Frecuencia, procesado.Length);
        salida.WriteLine($"Ruido de fondo del micro: {antes:F1} dBFS -> {despues:F1} dBFS");
        (antes - despues).Should().BeGreaterThan(20);
    }

    [Fact]
    public void El_grabador_circular_guarda_solo_los_ultimos_minutos_y_lo_mas_nuevo_al_final()
    {
        const int f = 8000;
        var grabador = new GrabadorCircular { Minutos = 1 };
        var bloque = new float[800];
        var total = 0;
        for (var n = 0; n < 900; n++)
        {
            for (var i = 0; i < bloque.Length; i++) bloque[i] = ((total + i) % 1000) / 2000f;
            grabador.Procesar(bloque, f);
            total += bloque.Length;
        }

        grabador.Grabado.Should().Be(TimeSpan.FromMinutes(1));
        var todo = grabador.Instantanea()!;
        todo.Frecuencia.Should().Be(f);
        todo.Muestras.Length.Should().Be(60 * f);
        todo.Muestras[^1].Should().BeApproximately(((total - 1) % 1000) / 2000f, 1e-4f);
        todo.Muestras[0].Should().BeApproximately(((total - (60 * f)) % 1000) / 2000f, 1e-4f);

        var ultimo = grabador.Instantanea(TimeSpan.FromSeconds(10))!;
        ultimo.Muestras.Length.Should().Be(10 * f);
        ultimo.Muestras[^1].Should().Be(todo.Muestras[^1]);

        grabador.Activo = false;
        grabador.Instantanea().Should().BeNull("apagado suelta la memoria");
    }

    [Fact]
    public void Un_audio_se_guarda_en_wav_y_se_lee_igual()
    {
        var ruta = Path.Combine(Path.GetTempPath(), "cuaderno-wav-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            var voz = Voz(segundos: 1, amplitud: 0.5);
            ArchivoWav.Guardar(ruta, new AudioEnMemoria(voz, Frecuencia));
            var leido = ArchivoWav.Leer(ruta);

            leido.Frecuencia.Should().Be(Frecuencia);
            leido.Muestras.Length.Should().Be(voz.Length);
            for (var i = 0; i < voz.Length; i += 97) leido.Muestras[i].Should().BeApproximately(voz[i], 1e-4f);
        }
        finally
        {
            File.Delete(ruta);
        }
    }

    // ── Senales y medidas ────────────────────────────────────────────────────

    /// <summary>Voz de mentira: 150 Hz con armonicos hasta 3 kHz, en silabas de 300 ms con 200 ms de pausa.</summary>
    internal static float[] Voz(int segundos, double amplitud = 0.3)
    {
        var n = segundos * Frecuencia;
        var s = new float[n];
        for (var i = 0; i < n; i++)
        {
            var t = (double)i / Frecuencia;
            var enSilaba = (i % (Frecuencia / 2)) < (Frecuencia * 3 / 10);
            if (!enSilaba) continue;
            var fase = (i % (Frecuencia / 2)) / (Frecuencia * 0.3);
            var envolvente = Math.Sin(Math.PI * fase);
            var f0 = 150 + (20 * Math.Sin(2 * Math.PI * 2 * t));
            var v = 0.0;
            for (var h = 1; h * 150 < 3000; h++) v += Math.Sin(2 * Math.PI * f0 * h * t) / h;
            s[i] = (float)(amplitud * envolvente * v / 2.0);
        }

        return s;
    }

    /// <summary>Vocal sostenida: 150 Hz fijos con armonicos hasta 3 kHz.</summary>
    internal static float[] Vocal(int segundos, double amplitud = 0.3)
    {
        var s = new float[segundos * Frecuencia];
        for (var i = 0; i < s.Length; i++)
        {
            var t = (double)i / Frecuencia;
            var v = 0.0;
            for (var h = 1; h * 150 < 3000; h++) v += Math.Sin(2 * Math.PI * 150 * h * t) / h;
            s[i] = (float)(amplitud * v / 2.0);
        }

        return s;
    }

    internal static float[] Ruido(int n, double amplitud, int semilla)
    {
        var azar = new Random(semilla);
        var r = new float[n];
        for (var i = 0; i < n; i++)
        {
            // Gaussiano por Box-Muller.
            var u1 = 1.0 - azar.NextDouble();
            var u2 = azar.NextDouble();
            r[i] = (float)(amplitud * Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2));
        }

        return r;
    }

    private static float[] Sumar(float[] a, float[] b)
    {
        var s = new float[a.Length];
        for (var i = 0; i < s.Length; i++) s[i] = a[i] + b[i];
        return s;
    }

    private static float[] Procesar(IProcesadorDeAudio paso, float[] entrada)
    {
        var copia = (float[])entrada.Clone();
        for (var i = 0; i < copia.Length; i += Bloque) paso.Procesar(copia.AsSpan(i, Math.Min(Bloque, copia.Length - i)), Frecuencia);
        return copia;
    }

    /// <summary>S/R invariante a la escala: lo que se parece a la voz limpia frente a todo lo demas.</summary>
    private static double SnrInvarianteDb(float[] limpia, float[] y, int desde, int retraso)
    {
        double sy = 0, ss = 0;
        for (var i = desde; i < limpia.Length - retraso; i++)
        {
            sy += limpia[i] * (double)y[i + retraso];
            ss += limpia[i] * (double)limpia[i];
        }

        var alfa = sy / ss;
        double senal = 0, error = 0;
        for (var i = desde; i < limpia.Length - retraso; i++)
        {
            var objetivo = alfa * limpia[i];
            senal += objetivo * objetivo;
            var e = y[i + retraso] - objetivo;
            error += e * e;
        }

        return 10 * Math.Log10(senal / error);
    }

    private static double PotenciaEnSilenciosDb(float[] limpia, float[] y, int desde, int retraso)
    {
        double suma = 0;
        var n = 0;
        for (var i = desde; i < limpia.Length - retraso; i++)
        {
            if (limpia[i] != 0) continue;
            suma += y[i + retraso] * (double)y[i + retraso];
            n++;
        }

        return 10 * Math.Log10((suma / Math.Max(1, n)) + 1e-20);
    }

    private static double PotenciaDb(float[] y, int desde, int hasta)
    {
        double suma = 0;
        for (var i = desde; i < hasta; i++) suma += y[i] * (double)y[i];
        return 10 * Math.Log10((suma / Math.Max(1, hasta - desde)) + 1e-20);
    }

    /// <summary>Armonicos de 150 Hz frente al ruido a medio camino entre ellos, en dB.</summary>
    private static double ArmonicosFrenteARuidoDb(float[] y, int desde)
    {
        double senal = 0, ruido = 0;
        foreach (var h in new[] { 2, 3, 4, 6, 8, 10, 14 })
        {
            senal += Math.Pow(10, Goertzel(y, desde, 150.0 * h) / 10);
            ruido += Math.Pow(10, Goertzel(y, desde, (150.0 * h) + 75) / 10);
        }

        return 10 * Math.Log10(senal / ruido);
    }

    /// <summary>Potencia en dB de una frecuencia, por Goertzel.</summary>
    private static double Goertzel(float[] y, int desde, double hercios)
    {
        var coef = 2 * Math.Cos(2 * Math.PI * hercios / Frecuencia);
        double s1 = 0, s2 = 0;
        for (var i = desde; i < y.Length; i++)
        {
            var s0 = y[i] + (coef * s1) - s2;
            s2 = s1;
            s1 = s0;
        }

        var potencia = (s1 * s1) + (s2 * s2) - (coef * s1 * s2);
        return 10 * Math.Log10(potencia + 1e-20);
    }
}
