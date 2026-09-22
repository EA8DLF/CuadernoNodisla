using Nodisla.Cuaderno.Aplicacion.Puertos;

namespace Nodisla.Cuaderno.Audio.Captura;

/// <summary>
/// Junta las muestras que salen del colchon en bloques del mismo tamano y les pone la hora.
/// </summary>
/// <remarks>
/// La hora de cada bloque <b>no</b> se lee del reloj cuando el bloque sale: se cuenta a partir
/// de la hora del primer bloque mas las muestras que han pasado. Leer el reloj en cada bloque
/// meteria el retraso del colchon y los saltos del sistema dentro de la marca de tiempo, y FT8
/// se sincroniza a ventanas de quince segundos: la hora tiene que ir pegada a las muestras, no
/// a cuando el programa se entera de ellas.
/// </remarks>
public sealed class EnsambladorDeBloques
{
    private readonly int _frecuenciaDeMuestreo;
    private readonly float[] _acumulado;
    private int _llenado;

    /// <summary>Crea el ensamblador.</summary>
    /// <param name="frecuenciaDeMuestreo">Muestras por segundo.</param>
    /// <param name="muestrasPorBloque">Muestras de cada bloque.</param>
    /// <param name="inicioUtc">Hora corregida de la primera muestra.</param>
    /// <exception cref="ArgumentOutOfRangeException">Si algun tamano no es positivo.</exception>
    public EnsambladorDeBloques(int frecuenciaDeMuestreo, int muestrasPorBloque, DateTimeOffset inicioUtc)
    {
        if (frecuenciaDeMuestreo <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frecuenciaDeMuestreo),
                frecuenciaDeMuestreo,
                "La frecuencia de muestreo tiene que ser positiva.");
        }

        if (muestrasPorBloque <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(muestrasPorBloque),
                muestrasPorBloque,
                "Los bloques tienen que tener al menos una muestra.");
        }

        _frecuenciaDeMuestreo = frecuenciaDeMuestreo;
        _acumulado = new float[muestrasPorBloque];
        InicioUtc = inicioUtc;
    }

    /// <summary>Hora corregida de la primera muestra del flujo.</summary>
    public DateTimeOffset InicioUtc { get; private set; }

    /// <summary>Muestras del flujo contadas hasta ahora, incluidas las perdidas.</summary>
    public long MuestrasDelFlujo { get; private set; }

    /// <summary>Muestras de cada bloque.</summary>
    public int MuestrasPorBloque => _acumulado.Length;

    /// <summary>Hora corregida que le tocaria a la siguiente muestra que entre.</summary>
    public DateTimeOffset InstanteDeLaSiguienteMuestra => InstanteDe(MuestrasDelFlujo);

    /// <summary>Hora corregida de una muestra del flujo por su numero.</summary>
    /// <param name="numeroDeMuestra">Numero de la muestra dentro del flujo.</param>
    /// <returns>Cuando se capturo esa muestra, segun el reloj corregido.</returns>
    public DateTimeOffset InstanteDe(long numeroDeMuestra) =>
        InicioUtc + TimeSpan.FromSeconds((double)numeroDeMuestra / _frecuenciaDeMuestreo);

    /// <summary>
    /// Da por pasadas unas muestras que se perdieron, sin entregarlas.
    /// </summary>
    /// <param name="muestras">Cuantas se perdieron.</param>
    /// <remarks>
    /// Es lo que mantiene la hora en su sitio despues de un hueco: el periodo del hueco se
    /// pierde —eso no tiene arreglo—, pero los siguientes siguen fechados como toca.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Si el numero es negativo.</exception>
    public void Saltar(long muestras)
    {
        if (muestras < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(muestras),
                muestras,
                "No se pueden saltar muestras negativas.");
        }

        if (muestras == 0)
        {
            return;
        }

        // Lo que estuviera a medias ya no es continuo con lo que viene: se tira.
        _llenado = 0;
        MuestrasDelFlujo += muestras;
    }

    /// <summary>Vuelve a anclar el flujo a una hora, por ejemplo al reabrir el dispositivo.</summary>
    /// <param name="inicioUtc">Hora corregida de la siguiente muestra que entre.</param>
    public void Reanclar(DateTimeOffset inicioUtc)
    {
        InicioUtc = inicioUtc;
        MuestrasDelFlujo = 0;
        _llenado = 0;
    }

    /// <summary>
    /// Mete muestras y deja en <paramref name="destino"/> los bloques que se completen.
    /// </summary>
    /// <param name="muestras">Muestras nuevas.</param>
    /// <param name="destino">Lista donde se anaden los bloques completos.</param>
    /// <returns>Cuantos bloques se completaron.</returns>
    /// <exception cref="ArgumentNullException">Si no se da lista de destino.</exception>
    public int Anadir(ReadOnlySpan<float> muestras, IList<BloqueDeAudio> destino)
    {
        ArgumentNullException.ThrowIfNull(destino);

        var bloques = 0;
        var restantes = muestras;

        while (!restantes.IsEmpty)
        {
            var hueco = _acumulado.Length - _llenado;
            var cuantas = Math.Min(hueco, restantes.Length);
            restantes[..cuantas].CopyTo(_acumulado.AsSpan(_llenado));
            _llenado += cuantas;
            MuestrasDelFlujo += cuantas;
            restantes = restantes[cuantas..];

            if (_llenado < _acumulado.Length)
            {
                continue;
            }

            var primera = MuestrasDelFlujo - _acumulado.Length;
            destino.Add(new BloqueDeAudio(
                _acumulado.AsSpan().ToArray(),
                _frecuenciaDeMuestreo,
                InstanteDe(primera)));

            bloques++;
            _llenado = 0;
        }

        return bloques;
    }
}
