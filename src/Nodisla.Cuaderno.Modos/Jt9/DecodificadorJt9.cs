using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Modos.Convolucional;
using Nodisla.Cuaderno.Modos.Jt65;

namespace Nodisla.Cuaderno.Modos.Jt9;

/// <summary>Como fue la decodificacion de una ventana de JT9, con el detalle para poder medir.</summary>
/// <param name="Decodificaciones">Lo que se saco.</param>
/// <param name="Candidatas">Cuantos sitios se miraron.</param>
/// <param name="Examinadas">Cuantas candidatas pasaron el corte de sincronismo y llegaron a Fano.</param>
/// <param name="CaminosDeFano">Veces que Fano llego al final del arbol.</param>
/// <param name="Rechazadas">
/// Caminos completos que se tiraron, por no tener gramatica o por no parecerse bastante a lo
/// recibido. <b>Es la cifra que importa</b>: cada uno era una decodificacion falsa en potencia.
/// </param>
/// <param name="Duracion">Lo que costo la ventana entera.</param>
public sealed record ResultadoDeVentanaJt9(
    IReadOnlyList<DecodificacionPropia> Decodificaciones,
    int Candidatas,
    int Examinadas,
    int CaminosDeFano,
    int Rechazadas,
    TimeSpan Duracion);

/// <summary>
/// Decodifica una ventana entera de JT9.
/// </summary>
/// <remarks>
/// <para>
/// Espectrograma, busqueda de sincronismo, afinado y medida de cada candidata, valores blandos
/// de los 206 bits, desentrelazado, decodificacion secuencial de Fano del codigo convolucional
/// K=32 (el de WSPR, compartido en <c>Convolucional/</c>) y desempaquetado del mensaje de 72
/// bits (el de JT65).
/// </para>
/// <para>
/// <b>Los filtros que impiden inventar un contacto</b>, por orden:
/// </para>
/// <list type="number">
/// <item>El sincronismo afinado tiene que pasar de <see cref="SincronismoMinimo"/>.</item>
/// <item>Fano tiene que llegar al final del arbol dentro de su esfuerzo, con la cola de 31
/// ceros casando con 62 bits recibidos.</item>
/// <item>Los 72 bits tienen que desempaquetarse a un mensaje con sentido.</item>
/// <item>El mensaje recodificado tiene que parecerse a lo recibido: la correlacion entre sus
/// 206 bits y los valores blandos tiene que pasar de <see cref="CoincidenciaMinima"/>.</item>
/// </list>
/// </remarks>
public sealed class DecodificadorJt9
{
    private readonly ILogger _registro;

    /// <summary>Crea el decodificador.</summary>
    public DecodificadorJt9(ILogger? registro = null)
    {
        _registro = registro ?? NullLogger.Instance;
        Fano = new DecodificadorDeFano(CodigoConvolucional.DeWsprYJt9);
    }

    /// <summary>El decodificador secuencial, para poder ajustarlo desde el banco.</summary>
    public DecodificadorDeFano Fano { get; init; }

    /// <summary>Candidatas que se afinan como mucho por ventana.</summary>
    public int CandidatasPorVentana { get; set; } = 30;

    /// <summary>
    /// Sincronismo afinado minimo (potencia del tono 0 en los intervalos de sincronismo partida
    /// por la de los de datos) para intentar decodificar una candidata. Medido en el banco.
    /// </summary>
    public double SincronismoMinimo { get; set; } = 2.0;

    /// <summary>
    /// Unidades de la escala de 0 a 255 por cada nat de la razon de verosimilitudes de un bit.
    /// Quince es lo que casa con la tabla de metricas por omision del decodificador de Fano.
    /// </summary>
    public double UnidadesPorNat { get; set; } = 15;

    /// <summary>
    /// Parecido minimo entre el mensaje recodificado y lo recibido, de −1 a 1. Medido en el banco
    /// con ruido puro.
    /// </summary>
    public double CoincidenciaMinima { get; set; } = 0.30;

    /// <summary>Ajuste del informe de senal, en decibelios, sacado de medir en el banco.</summary>
    public double CorreccionDelInforme { get; set; } = 0.0;

    /// <summary>Decodifica una ventana de audio a 12000 muestras por segundo.</summary>
    public ResultadoDeVentanaJt9 Decodificar(ReadOnlySpan<float> audio, DateTimeOffset ventanaUtc, CancellationToken ct = default)
    {
        var reloj = Stopwatch.StartNew();
        if (audio.Length < ParametrosJt9.MuestrasDeLaSenal / 2)
            return new ResultadoDeVentanaJt9([], 0, 0, 0, 0, reloj.Elapsed);

        var muestras = audio.ToArray();
        var espectrograma = EspectrogramaJt9.Calcular(muestras);
        var candidatas = SincronizadorJt9.Buscar(espectrograma, CandidatasPorVentana);
        var demodulador = new DemoduladorJt9(muestras);

        var salida = new List<DecodificacionPropia>();
        var decodificadas = new List<double>();
        var porBit = new byte[TablasJt9.BitsCodificados];
        int examinadas = 0, caminos = 0, rechazadas = 0;

        foreach (var candidata in candidatas)
        {
            if (ct.IsCancellationRequested) break;
            var frecuenciaGruesa = candidata.Casilla * EspectrogramaJt9.CasillaHz;
            if (decodificadas.Any(f => Math.Abs(frecuenciaGruesa - f) < ParametrosJt9.AnchoDeBandaHz)) continue;

            var medida = demodulador.Medir(candidata);
            if (medida.Sincronismo < SincronismoMinimo) continue;
            examinadas++;

            var blandos = medida.ValoresBlandos(UnidadesPorNat);
            CodificadorJt9.Desentrelazar(blandos, porBit);
            var resultado = Fano.Decodificar(porBit, MensajeDe72Bits.Bits, CodificadorJt9.BitsConCola);
            if (!resultado.Decodificado) continue;
            caminos++;

            if (!MensajeDe72Bits.TryDesempaquetar(resultado.Bits.AsSpan(0, MensajeDe72Bits.Bits), out var mensaje))
            {
                rechazadas++;
                continue;
            }

            var coincidencia = Coincidencia(resultado.Bits.AsSpan(0, MensajeDe72Bits.Bits), porBit);
            if (coincidencia < CoincidenciaMinima)
            {
                rechazadas++;
                _registro.LogDebug("JT9: «{Mensaje}» descartado por coincidencia {Coincidencia:0.00}.", mensaje.Texto, coincidencia);
                continue;
            }

            var tono = medida.TonoBaseHz;
            if (salida.Any(d => d.Texto == mensaje.Texto && Math.Abs(d.TonoHz - tono) < 10)) continue;
            decodificadas.Add(tono);
            salida.Add(new DecodificacionPropia(
                mensaje.Texto,
                Informe(medida.SenalSobreRuido),
                Math.Round(medida.ComienzoSegundos - ParametrosJt9.ComienzoNominalSegundos, 2),
                (int)Math.Round(tono),
                ModoDelModem.Jt9,
                ventanaUtc)
            {
                Llamante = mensaje.Llamante,
                Llamado = mensaje.Llamado,
                Locator = mensaje.Locator,
                EsCq = mensaje.EsCq,
            });
        }

        salida.Sort(static (x, y) => x.TonoHz.CompareTo(y.TonoHz));
        _registro.LogDebug(
            "Ventana JT9 {Ventana}: {Candidatas} candidatas, {Examinadas} examinadas, {Caminos} caminos de Fano, {Rechazadas} rechazados, {Salieron} decodificaciones en {Milisegundos} ms.",
            ventanaUtc, candidatas.Count, examinadas, caminos, rechazadas, salida.Count, reloj.ElapsedMilliseconds);
        return new ResultadoDeVentanaJt9(salida, candidatas.Count, examinadas, caminos, rechazadas, reloj.Elapsed);
    }

    /// <summary>
    /// Parecido entre los 206 bits que saldrian de recodificar el mensaje y los valores blandos
    /// recibidos: correlacion normalizada, de −1 a 1.
    /// </summary>
    public static double Coincidencia(ReadOnlySpan<byte> bits72, ReadOnlySpan<byte> blandosPorBit)
    {
        var codificados = CodificadorJt9.BitsCodificadosDe(bits72);
        double producto = 0, norma = 0;
        for (var p = 0; p < TablasJt9.BitsCodificados; p++)
        {
            var y = blandosPorBit[p] - 128.0;
            producto += (codificados[p] == 1 ? 1 : -1) * y;
            norma += Math.Abs(y);
        }
        return norma <= 0 ? 0 : producto / norma;
    }

    /// <summary>Informe de senal referido a 2500 Hz.</summary>
    private int Informe(double senalSobreRuidoEnCasilla)
    {
        if (senalSobreRuidoEnCasilla <= 0) return -30;
        var db = (10 * Math.Log10(senalSobreRuidoEnCasilla * ParametrosJt9.EspaciadoDeTonosHz / 2500.0)) + CorreccionDelInforme;
        return (int)Math.Round(Math.Clamp(db, -30, 50));
    }
}
