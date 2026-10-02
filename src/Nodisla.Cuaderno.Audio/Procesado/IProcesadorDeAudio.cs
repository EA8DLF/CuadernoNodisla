namespace Nodisla.Cuaderno.Audio.Procesado;

/// <summary>
/// Un paso de procesado que trabaja en el sitio sobre un bloque de muestras de un canal.
/// </summary>
/// <remarks>
/// <para>
/// Lo llama el hilo de audio de Windows con cada bloque que llega (unos 10 ms): no puede
/// bloquear, ni pedir memoria en cada vuelta, ni lanzar. Los ajustes que cambia la pantalla se
/// leen al principio de cada bloque, asi que todo se activa y desactiva en vivo.
/// </para>
/// <para>
/// La frecuencia de muestreo viene con cada bloque: si cambia (otro dispositivo, otro formato),
/// el paso se reinicia solo.
/// </para>
/// </remarks>
public interface IProcesadorDeAudio
{
    /// <summary>Procesa el bloque en el sitio.</summary>
    /// <param name="muestras">Muestras de un canal, de -1 a 1.</param>
    /// <param name="frecuencia">Muestras por segundo.</param>
    void Procesar(Span<float> muestras, int frecuencia);
}
