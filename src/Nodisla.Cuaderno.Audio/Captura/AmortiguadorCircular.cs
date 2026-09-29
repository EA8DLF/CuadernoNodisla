namespace Nodisla.Cuaderno.Audio.Captura;

/// <summary>
/// El colchon entre el hilo de audio y el que trabaja.
/// </summary>
/// <remarks>
/// <para>
/// Windows entrega el audio en un hilo suyo, de mucha prioridad, y lo que ahi se haga no puede
/// tardar: si se tarda, se pierden muestras y con ellas se va un periodo entero de FT8. Por eso
/// ese hilo solo copia aqui, y todo el trabajo —transformadas, decodificacion, avisos— pasa en
/// otro hilo que lee de aqui.
/// </para>
/// <para>
/// Cuando el colchon se llena se tira lo <b>mas viejo</b>, no lo mas nuevo. Parece al reves de
/// lo que uno haria, y tiene un motivo: asi el hueco queda justo donde el lector va a leer a
/// continuacion, y el lector puede correr su cuenta de muestras exactamente lo que se perdio.
/// Si se tirara lo nuevo, el hueco quedaria un colchon entero mas tarde y todo lo que viniera
/// despues iria fechado con retraso, que es la clase de fallo que deja al modem sin decodificar
/// sin que nadie sepa por que.
/// </para>
/// </remarks>
public sealed class AmortiguadorCircular
{
    private readonly float[] _datos;
    private readonly object _candado = new();
    private int _lectura;
    private int _disponibles;
    private long _perdidas;

    /// <summary>Crea el colchon.</summary>
    /// <param name="capacidad">Muestras que caben.</param>
    /// <exception cref="ArgumentOutOfRangeException">Si la capacidad no es positiva.</exception>
    public AmortiguadorCircular(int capacidad)
    {
        if (capacidad <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacidad),
                capacidad,
                "La capacidad tiene que ser positiva.");
        }

        _datos = new float[capacidad];
    }

    /// <summary>Muestras que caben.</summary>
    public int Capacidad => _datos.Length;

    /// <summary>Muestras sin leer.</summary>
    public int Disponibles
    {
        get
        {
            lock (_candado)
            {
                return _disponibles;
            }
        }
    }

    /// <summary>
    /// Muestras perdidas desde que se creo o se vacio. Es el dato que hay que ensenarle al
    /// operador: explica por que un periodo no decodifico nada.
    /// </summary>
    public long MuestrasPerdidas
    {
        get
        {
            lock (_candado)
            {
                return _perdidas;
            }
        }
    }

    /// <summary>Tira todo lo que haya y pone a cero la cuenta de perdidas.</summary>
    public void Vaciar()
    {
        lock (_candado)
        {
            _lectura = 0;
            _disponibles = 0;
            _perdidas = 0;
        }
    }

    /// <summary>
    /// Mete muestras. Lo llama el hilo de audio, asi que no hace nada mas que copiar.
    /// </summary>
    /// <param name="muestras">Muestras nuevas.</param>
    /// <returns>Cuantas muestras viejas hubo que tirar para que cupieran.</returns>
    public int Escribir(ReadOnlySpan<float> muestras)
    {
        if (muestras.IsEmpty)
        {
            return 0;
        }

        lock (_candado)
        {
            var entrantes = muestras;
            var tiradas = 0;

            // Si llega mas de lo que cabe entero, solo tiene sentido quedarse con el final.
            if (entrantes.Length > _datos.Length)
            {
                tiradas = entrantes.Length - _datos.Length;
                entrantes = entrantes[^_datos.Length..];
            }

            var hueco = _datos.Length - _disponibles;
            if (entrantes.Length > hueco)
            {
                var sobran = entrantes.Length - hueco;
                _lectura = (_lectura + sobran) % _datos.Length;
                _disponibles -= sobran;
                tiradas += sobran;
            }

            var escritura = (_lectura + _disponibles) % _datos.Length;
            var hastaElFinal = Math.Min(entrantes.Length, _datos.Length - escritura);
            entrantes[..hastaElFinal].CopyTo(_datos.AsSpan(escritura));

            if (hastaElFinal < entrantes.Length)
            {
                entrantes[hastaElFinal..].CopyTo(_datos.AsSpan(0));
            }

            _disponibles += entrantes.Length;
            _perdidas += tiradas;
            return tiradas;
        }
    }

    /// <summary>Saca muestras.</summary>
    /// <param name="destino">Donde se dejan.</param>
    /// <returns>Cuantas se sacaron; puede ser menos de las que caben en el destino.</returns>
    public int Leer(Span<float> destino)
    {
        if (destino.IsEmpty)
        {
            return 0;
        }

        lock (_candado)
        {
            var cuantas = Math.Min(destino.Length, _disponibles);
            if (cuantas == 0)
            {
                return 0;
            }

            var hastaElFinal = Math.Min(cuantas, _datos.Length - _lectura);
            _datos.AsSpan(_lectura, hastaElFinal).CopyTo(destino);

            if (hastaElFinal < cuantas)
            {
                _datos.AsSpan(0, cuantas - hastaElFinal).CopyTo(destino[hastaElFinal..]);
            }

            _lectura = (_lectura + cuantas) % _datos.Length;
            _disponibles -= cuantas;
            return cuantas;
        }
    }
}
