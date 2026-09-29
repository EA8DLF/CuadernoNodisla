namespace Nodisla.Cuaderno.Audio.Cascada;

/// <summary>Lo que se llama con cada tramo completo.</summary>
/// <param name="indiceDeInicio">Numero de la primera muestra del tramo dentro del flujo.</param>
/// <param name="tramo">Las muestras del tramo.</param>
public delegate void TramoCompleto(long indiceDeInicio, ReadOnlySpan<float> tramo);

/// <summary>
/// Trocea un flujo continuo de muestras en tramos que se solapan.
/// </summary>
/// <remarks>
/// El solape no es un adorno: con tramos pegados, una senal que cae justo en la costura se
/// parte entre dos y se ve mas debil de lo que es. Avanzando medio tramo cada vez, cualquier
/// instante del audio cae entero dentro de algun tramo. Lo innegociable es que el troceado
/// <b>no pierda ni repita</b> muestras: el flujo entra una sola vez y sale entero.
/// </remarks>
public sealed class TroceadorConSolape
{
    private readonly float[] _acumulado;
    private readonly int _salto;
    private int _llenado;
    private long _indiceDelAcumulado;

    /// <summary>Crea el troceador.</summary>
    /// <param name="tamano">Muestras de cada tramo.</param>
    /// <param name="salto">Muestras que se avanza de un tramo al siguiente.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Si el tamano no es positivo, o el salto no esta entre uno y el tamano.
    /// </exception>
    public TroceadorConSolape(int tamano, int salto)
    {
        if (tamano <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tamano),
                tamano,
                "El tamaño del tramo tiene que ser positivo.");
        }

        if (salto <= 0 || salto > tamano)
        {
            throw new ArgumentOutOfRangeException(
                nameof(salto),
                salto,
                "El salto tiene que estar entre una muestra y el tamaño del tramo.");
        }

        _acumulado = new float[tamano];
        _salto = salto;
    }

    /// <summary>Muestras de cada tramo.</summary>
    public int Tamano => _acumulado.Length;

    /// <summary>Muestras que se avanza de un tramo al siguiente.</summary>
    public int Salto => _salto;

    /// <summary>Muestras que han entrado desde que se creo o se reinicio.</summary>
    public long MuestrasRecibidas { get; private set; }

    /// <summary>Olvida lo acumulado y vuelve a empezar a contar.</summary>
    public void Reiniciar()
    {
        _llenado = 0;
        _indiceDelAcumulado = 0;
        MuestrasRecibidas = 0;
    }

    /// <summary>
    /// Mete muestras y llama a <paramref name="alCompletar"/> por cada tramo que se complete.
    /// </summary>
    /// <param name="muestras">Muestras nuevas.</param>
    /// <param name="alCompletar">Que hacer con cada tramo.</param>
    /// <returns>Cuantos tramos se completaron.</returns>
    /// <exception cref="ArgumentNullException">Si no se dice que hacer con los tramos.</exception>
    public int Anadir(ReadOnlySpan<float> muestras, TramoCompleto alCompletar)
    {
        ArgumentNullException.ThrowIfNull(alCompletar);

        var tramos = 0;
        var restantes = muestras;

        while (!restantes.IsEmpty)
        {
            var hueco = _acumulado.Length - _llenado;
            var cuantas = Math.Min(hueco, restantes.Length);
            restantes[..cuantas].CopyTo(_acumulado.AsSpan(_llenado));
            _llenado += cuantas;
            MuestrasRecibidas += cuantas;
            restantes = restantes[cuantas..];

            if (_llenado < _acumulado.Length)
            {
                continue;
            }

            alCompletar(_indiceDelAcumulado, _acumulado);
            tramos++;

            // Se corre lo que se queda para el siguiente tramo y se avanza el indice: asi el
            // solape no repite muestras del flujo, solo las vuelve a mirar en otra posicion.
            var conservadas = _acumulado.Length - _salto;
            if (conservadas > 0)
            {
                _acumulado.AsSpan(_salto, conservadas).CopyTo(_acumulado);
            }

            _llenado = conservadas;
            _indiceDelAcumulado += _salto;
        }

        return tramos;
    }
}
