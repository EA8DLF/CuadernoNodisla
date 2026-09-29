namespace Nodisla.Cuaderno.Audio.Captura;

/// <summary>
/// Cambia la frecuencia de muestreo interpolando en linea recta.
/// </summary>
/// <remarks>
/// <para>
/// Solo hace falta cuando el dispositivo no esta a 48.000 muestras por segundo. Con el codec
/// del FT-710 no entra en juego —trabaja a 48.000—, pero en esta maquina hay mas de veinte
/// dispositivos de sonido y alguno esta a 44.100: mejor sonar un poco peor que no sonar.
/// </para>
/// <para>
/// La interpolacion recta es honrada pero modesta: al bajar de frecuencia no filtra antes, asi
/// que lo que hubiera por encima de la mitad de la frecuencia nueva se cuela donde no debe. Por
/// eso, cuando hace falta remuestrear, lo suyo es avisar al operador de que ponga el
/// dispositivo a 48.000 y no dejarlo asi.
/// </para>
/// </remarks>
public sealed class Remuestreador
{
    private readonly double _paso;
    private double _posicion;
    private float _ultimaDelBloqueAnterior;
    private bool _hayBloqueAnterior;

    /// <summary>Crea el remuestreador.</summary>
    /// <param name="frecuenciaDeOrigen">Muestras por segundo que entran.</param>
    /// <param name="frecuenciaDeDestino">Muestras por segundo que salen.</param>
    /// <exception cref="ArgumentOutOfRangeException">Si alguna frecuencia no es positiva.</exception>
    public Remuestreador(int frecuenciaDeOrigen, int frecuenciaDeDestino)
    {
        if (frecuenciaDeOrigen <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frecuenciaDeOrigen),
                frecuenciaDeOrigen,
                "La frecuencia de origen tiene que ser positiva.");
        }

        if (frecuenciaDeDestino <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frecuenciaDeDestino),
                frecuenciaDeDestino,
                "La frecuencia de destino tiene que ser positiva.");
        }

        FrecuenciaDeOrigen = frecuenciaDeOrigen;
        FrecuenciaDeDestino = frecuenciaDeDestino;
        _paso = (double)frecuenciaDeOrigen / frecuenciaDeDestino;
    }

    /// <summary>Muestras por segundo que entran.</summary>
    public int FrecuenciaDeOrigen { get; }

    /// <summary>Muestras por segundo que salen.</summary>
    public int FrecuenciaDeDestino { get; }

    /// <summary>Las dos frecuencias coinciden, asi que no hay nada que hacer.</summary>
    public bool EsPasoDirecto => FrecuenciaDeOrigen == FrecuenciaDeDestino;

    /// <summary>Cuantas muestras pueden salir, como mucho, de un bloque de entrada.</summary>
    /// <param name="muestrasDeEntrada">Muestras que entran.</param>
    /// <returns>El tope de muestras de salida, para dimensionar el destino.</returns>
    public int MuestrasMaximas(int muestrasDeEntrada) =>
        EsPasoDirecto ? muestrasDeEntrada : (int)Math.Ceiling((muestrasDeEntrada + 1) / _paso) + 1;

    /// <summary>Olvida lo que quedaba a medias del bloque anterior.</summary>
    public void Reiniciar()
    {
        _posicion = 0;
        _ultimaDelBloqueAnterior = 0f;
        _hayBloqueAnterior = false;
    }

    /// <summary>
    /// Convierte un bloque, sin perder la continuidad con el bloque anterior.
    /// </summary>
    /// <param name="entrada">Muestras que entran.</param>
    /// <param name="salida">Donde se dejan las muestras convertidas.</param>
    /// <returns>Cuantas muestras se escribieron.</returns>
    /// <exception cref="ArgumentException">Si el destino se queda corto.</exception>
    public int Convertir(ReadOnlySpan<float> entrada, Span<float> salida)
    {
        if (EsPasoDirecto)
        {
            if (salida.Length < entrada.Length)
            {
                throw new ArgumentException("El destino no tiene sitio para todas las muestras.", nameof(salida));
            }

            entrada.CopyTo(salida);
            return entrada.Length;
        }

        if (entrada.IsEmpty)
        {
            return 0;
        }

        var escritas = 0;

        // La posicion va en muestras del flujo de entrada, contando desde el principio de este
        // bloque. Puede empezar en negativo: lo que falta se coge del bloque anterior.
        while (true)
        {
            var izquierda = (int)Math.Floor(_posicion);
            if (izquierda > entrada.Length - 2)
            {
                break;
            }

            if (izquierda < -1)
            {
                // Solo puede pasar si se reinicio a media conversion; se salta al principio.
                _posicion = _hayBloqueAnterior ? -1 : 0;
                continue;
            }

            if (izquierda == -1 && !_hayBloqueAnterior)
            {
                _posicion = 0;
                continue;
            }

            if (escritas >= salida.Length)
            {
                throw new ArgumentException("El destino no tiene sitio para todas las muestras.", nameof(salida));
            }

            var anterior = izquierda == -1 ? _ultimaDelBloqueAnterior : entrada[izquierda];
            var siguiente = entrada[izquierda + 1];
            var fraccion = (float)(_posicion - izquierda);

            salida[escritas++] = anterior + ((siguiente - anterior) * fraccion);
            _posicion += _paso;
        }

        _posicion -= entrada.Length;
        _ultimaDelBloqueAnterior = entrada[^1];
        _hayBloqueAnterior = true;

        return escritas;
    }
}
