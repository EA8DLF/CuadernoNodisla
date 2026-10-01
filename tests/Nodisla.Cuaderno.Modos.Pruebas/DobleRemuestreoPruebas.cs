using System.Globalization;
using System.Text;
using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Modos.Banco;
using Nodisla.Cuaderno.Modos.Ft8;
using Nodisla.Cuaderno.Modos.Ldpc;

namespace Nodisla.Cuaderno.Modos.Pruebas;

/// <summary>
/// Mide el caso real de Jose: el codec del FT-710 entrega 44100 muestras por segundo, los
/// ajustes tienen «FrecuenciaDeMuestreo» a 96000 y el módem analiza a 12800 (FT8). Eso son DOS
/// remuestreos en cascada —44100→96000 por interpolación lineal en
/// <c>Nodisla.Cuaderno.Audio.Captura.Remuestreador</c>, y 96000→12800 con el remuestreador de
/// calidad de <c>Nodisla.Cuaderno.Modos.Senal.Remuestreador</c>— frente a UN solo remuestreo
/// directo de 44100 a 12800 con el remuestreador bueno.
/// </summary>
/// <remarks>
/// <para>
/// <b>Resultado medido (26-09-2026, 8 franjas de 0 a −21 dB, 30 ventanas por franja, mismas
/// señales y mismo ruido en las dos vías): 211/240 por la vía directa y 211/240 por la vía
/// doble, franja por franja idénticas</b> (30/30 hasta −18 dB, 1/30 a −21 dB). El doble
/// remuestreo NO degrada la decodificación: al subir de frecuencia la interpolación lineal no
/// dobla nada hacia abajo, y el segundo salto lo hace el remuestreador con filtro correcto. Esta
/// prueba queda como guardián de esa igualdad, con menos ventanas para no eternizar la suite.
/// </para>
/// <para>
/// No se puede usar el proyecto de audio real aquí (depende de WASAPI y de Windows en tiempo de
/// ejecución, y este proyecto de pruebas es agnóstico de plataforma), así que la interpolación
/// lineal de <c>Audio.Captura.Remuestreador</c> se replica tal cual —mismo algoritmo, sin
/// filtro previo— para poder medir el camino exacto que recorre el audio real sin abrir ninguna
/// tarjeta de sonido.
/// </para>
/// </remarks>
public class DobleRemuestreoPruebas
{
    private static readonly TablasDelProtocolo Tablas = TablasDelProtocolo.Cargar();

    /// <summary>Lo que de verdad entrega el codec de Jose.</summary>
    public const int FrecuenciaDelCodec = 44100;

    /// <summary>Lo que hay en ajustes.json.</summary>
    public const int FrecuenciaConfigurada = 96000;

    [Fact]
    public void ElDobleRemuestreoDecodificaIgualQueUnoSolo()
    {
        var p = ParametrosDelModo.Ft8;
        var codificador = new Codificador(Tablas);
        var decodificador = new Decodificador(Tablas);

        var informe = new StringBuilder();
        informe.AppendLine("| S/R (dB) | Directo 44100→12800 | Doble 44100→96000→12800 |");
        informe.AppendLine("|---:|---:|---:|");

        int totalDirecto = 0, totalDoble = 0, totalIntentos = 0;

        foreach (var db in new[] { -15.0, -18.0 })
        {
            int aciertosDirecto = 0, aciertosDoble = 0;
            const int VentanasPorFranja = 12;

            for (var v = 0; v < VentanasPorFranja; v++)
            {
                // Misma semilla para las dos vías: la señal y el ruido tienen que ser
                // exactamente los mismos, o la comparación no mediría el remuestreo.
                var semilla = 500_000 + (int)(db * 1000) + (v * 7919);
                var azarSenal = new Random(semilla);
                const string Texto = "CQ EA8DLF IL18";
                var tono = 500 + (azarSenal.NextDouble() * 2000);
                var desfase = (azarSenal.NextDouble() - 0.5) * 0.6;

                codificador.TryCodificar(Texto, ModoDelModem.Ft8, out var tonos, out var motivo)
                    .Should().BeTrue(motivo);

                // Se genera SIEMPRE al ritmo del codec real, con ruido de banda ancha hasta
                // 22050 Hz, que es lo que de verdad entrega una tarjeta de sonido.
                var ventana44100 = GeneradorDeSenal.Ventana(p, tonos, tono, desfase, db, FrecuenciaDelCodec, azarSenal);

                var directa = decodificador.Decodificar(
                    ventana44100, FrecuenciaDelCodec, ModoDelModem.Ft8, DateTimeOffset.UnixEpoch, new CatalogoDeIndicativos());
                if (directa.Decodificaciones.Any(d => d.Texto == Texto)) aciertosDirecto++;

                var ventana96000 = InterpolacionLinealComoAudioCaptura(ventana44100, FrecuenciaDelCodec, FrecuenciaConfigurada);
                var doble = decodificador.Decodificar(
                    ventana96000, FrecuenciaConfigurada, ModoDelModem.Ft8, DateTimeOffset.UnixEpoch, new CatalogoDeIndicativos());
                if (doble.Decodificaciones.Any(d => d.Texto == Texto)) aciertosDoble++;
            }

            informe.AppendLine(string.Format(
                CultureInfo.InvariantCulture, "| {0:0} | {1}/{2} | {3}/{2} |", db, aciertosDirecto, VentanasPorFranja, aciertosDoble));

            totalDirecto += aciertosDirecto;
            totalDoble += aciertosDoble;
            totalIntentos += VentanasPorFranja;
        }

        var resumen = informe.ToString();

        // Medido: las dos vías salen iguales. Se tolera una ventana de diferencia por si algún
        // caso queda justo en el filo del corrector, pero una caída mayor sería una regresión.
        totalDoble.Should().BeGreaterThanOrEqualTo(totalDirecto - 1,
            $"el doble remuestreo (interpolación lineal al subir + filtro bueno al bajar) no degrada la decodificación\n{resumen}");
        ((double)totalDirecto / totalIntentos).Should().BeGreaterThan(0.5,
            $"la vía de un solo remuestreo tiene que seguir siendo utilizable\n{resumen}");
    }

    [Fact]
    public void ElRelojAtrasado300MsNoExplicaPorSiSoloElCeroPorCientoDeDecodificaciones()
    {
        // Jose tenía el reloj 304 ms atrasado. Un desfase de ese orden tiene que seguir
        // decodificando: el sincronizador busca la señal a lo largo de toda la ventana.
        var p = ParametrosDelModo.Ft8;
        var codificador = new Codificador(Tablas);
        var decodificador = new Decodificador(Tablas);
        const string Texto = "CQ EA8DLF IL18";
        const double Db = -12;

        codificador.TryCodificar(Texto, ModoDelModem.Ft8, out var tonos, out var motivo).Should().BeTrue(motivo);

        int aciertosEnHora = 0, aciertosAtrasado = 0;
        const int Intentos = 8;
        for (var v = 0; v < Intentos; v++)
        {
            var ventanaEnHora = GeneradorDeSenal.Ventana(p, tonos, 1500, desfaseSegundos: 0.0, Db, FrecuenciaDelCodec, new Random(900_000 + v));
            if (decodificador.Decodificar(ventanaEnHora, FrecuenciaDelCodec, ModoDelModem.Ft8, DateTimeOffset.UnixEpoch, new CatalogoDeIndicativos())
                .Decodificaciones.Any(d => d.Texto == Texto)) aciertosEnHora++;

            var ventanaAtrasada = GeneradorDeSenal.Ventana(p, tonos, 1500, desfaseSegundos: -0.304, Db, FrecuenciaDelCodec, new Random(900_000 + v));
            if (decodificador.Decodificar(ventanaAtrasada, FrecuenciaDelCodec, ModoDelModem.Ft8, DateTimeOffset.UnixEpoch, new CatalogoDeIndicativos())
                .Decodificaciones.Any(d => d.Texto == Texto)) aciertosAtrasado++;
        }

        aciertosAtrasado.Should().Be(aciertosEnHora,
            $"con 304 ms de desfase tiene que decodificar lo mismo que en hora ({aciertosAtrasado}/{Intentos} frente a {aciertosEnHora}/{Intentos})");
        aciertosEnHora.Should().Be(Intentos);
    }

    /// <summary>
    /// Réplica EXACTA del algoritmo de <c>Nodisla.Cuaderno.Audio.Captura.Remuestreador</c>:
    /// interpolación lineal sin filtro antialiasing previo, aplicada a un bloque completo.
    /// </summary>
    internal static float[] InterpolacionLinealComoAudioCaptura(float[] entrada, int frecuenciaDeOrigen, int frecuenciaDeDestino)
    {
        if (frecuenciaDeOrigen == frecuenciaDeDestino) return entrada;

        var paso = (double)frecuenciaDeOrigen / frecuenciaDeDestino;
        var salida = new List<float>((int)Math.Ceiling((entrada.Length + 1) / paso) + 1);
        var posicion = 0.0;

        while (true)
        {
            var izquierda = (int)Math.Floor(posicion);
            if (izquierda > entrada.Length - 2) break;
            if (izquierda < 0) { posicion = 0; continue; }

            var anterior = entrada[izquierda];
            var siguiente = entrada[izquierda + 1];
            var fraccion = (float)(posicion - izquierda);

            salida.Add(anterior + ((siguiente - anterior) * fraccion));
            posicion += paso;
        }

        return [.. salida];
    }
}
