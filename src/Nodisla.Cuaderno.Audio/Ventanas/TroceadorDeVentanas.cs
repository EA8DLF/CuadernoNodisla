using Nodisla.Cuaderno.Aplicacion.Puertos;

namespace Nodisla.Cuaderno.Audio.Ventanas;

/// <summary>Una ventana de audio ya completa, lista para el decodificador.</summary>
/// <param name="InicioUtc">Comienzo de la ventana, alineado al reloj universal.</param>
/// <param name="Periodo">Lo que dura: 15 s en FT8, 7,5 s en FT4.</param>
/// <param name="Muestras">Las muestras de la ventana, un canal, coma flotante.</param>
/// <param name="MuestrasFaltantes">
/// Muestras que no llegaron y van a cero. Si no es cero, la ventana tiene agujeros y lo que
/// salga de ella hay que mirarlo con recelo.
/// </param>
public sealed record VentanaDeAudio(
    DateTimeOffset InicioUtc,
    TimeSpan Periodo,
    ReadOnlyMemory<float> Muestras,
    int MuestrasFaltantes)
{
    /// <summary>La ventana llego entera.</summary>
    public bool EstaCompleta => MuestrasFaltantes == 0;
}

/// <summary>
/// Parte el flujo de audio en ventanas alineadas al reloj universal.
/// </summary>
/// <remarks>
/// <para>
/// FT8 y FT4 no trocean el tiempo por donde le venga bien al programa: las ventanas empiezan en
/// los segundos 0, 15, 30 y 45 del minuto —o cada 7,5 s en FT4— para todo el mundo a la vez. Por
/// eso cada muestra se coloca en su sitio dentro de la ventana segun <b>su</b> hora, la que trae
/// el bloque, y no segun el orden en que va llegando.
/// </para>
/// <para>
/// Lo que no llega se queda a cero y se cuenta: una ventana con agujeros se entrega igual —a lo
/// mejor todavia decodifica algo— pero diciendo que los tiene.
/// </para>
/// </remarks>
public sealed class TroceadorDeVentanas
{
    private readonly TimeSpan _periodo;
    private readonly int _frecuenciaDeMuestreo;
    private readonly float[] _acumulado;

    private DateTimeOffset _inicio;
    private bool _hayVentana;
    private int _rellenadas;

    /// <summary>Crea el troceador.</summary>
    /// <param name="periodo">Duracion de la ventana.</param>
    /// <param name="frecuenciaDeMuestreo">Muestras por segundo.</param>
    /// <exception cref="ArgumentOutOfRangeException">Si el periodo o la frecuencia no valen.</exception>
    public TroceadorDeVentanas(TimeSpan periodo, int frecuenciaDeMuestreo)
    {
        if (periodo <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(periodo),
                periodo,
                "El periodo de la ventana tiene que ser positivo.");
        }

        if (frecuenciaDeMuestreo <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frecuenciaDeMuestreo),
                frecuenciaDeMuestreo,
                "La frecuencia de muestreo tiene que ser positiva.");
        }

        _periodo = periodo;
        _frecuenciaDeMuestreo = frecuenciaDeMuestreo;

        var muestras = (long)Math.Round(periodo.TotalSeconds * frecuenciaDeMuestreo);
        if (muestras <= 0 || muestras > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(periodo),
                periodo,
                "La ventana no cabe en memoria con esa frecuencia de muestreo.");
        }

        _acumulado = new float[muestras];
    }

    /// <summary>Muestras que tiene una ventana entera.</summary>
    public int MuestrasPorVentana => _acumulado.Length;

    /// <summary>Duracion de la ventana.</summary>
    public TimeSpan Periodo => _periodo;

    /// <summary>Comienzo de la ventana que se esta llenando, o nulo si no hay ninguna.</summary>
    public DateTimeOffset? VentanaEnCurso => _hayVentana ? _inicio : null;

    /// <summary>Comienzo de la ventana a la que pertenece un instante.</summary>
    /// <param name="instante">Un momento cualquiera, en hora corregida.</param>
    /// <param name="periodo">Duracion de la ventana.</param>
    /// <returns>El comienzo de su ventana.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Si el periodo no es positivo.</exception>
    public static DateTimeOffset InicioDeLaVentana(DateTimeOffset instante, TimeSpan periodo)
    {
        if (periodo <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(periodo),
                periodo,
                "El periodo de la ventana tiene que ser positivo.");
        }

        var pulsos = instante.UtcTicks;
        return new DateTimeOffset(pulsos - (pulsos % periodo.Ticks), TimeSpan.Zero);
    }

    /// <summary>Tira lo que hubiera a medias.</summary>
    public void Reiniciar()
    {
        _hayVentana = false;
        _rellenadas = 0;
        Array.Clear(_acumulado);
    }

    /// <summary>
    /// Entrega la ventana a medias que hubiera, por ejemplo al parar la escucha.
    /// </summary>
    /// <returns>La ventana incompleta, o nulo si no habia nada.</returns>
    public VentanaDeAudio? Cerrar()
    {
        if (!_hayVentana || _rellenadas == 0)
        {
            Reiniciar();
            return null;
        }

        var ventana = Crear();
        Reiniciar();
        return ventana;
    }

    /// <summary>
    /// Mete un bloque y devuelve las ventanas que se hayan completado.
    /// </summary>
    /// <param name="bloque">Bloque recien capturado, con su hora corregida.</param>
    /// <returns>Las ventanas terminadas; puede venir vacia.</returns>
    /// <exception cref="ArgumentNullException">Si el bloque es nulo.</exception>
    /// <exception cref="ArgumentException">Si el bloque no viene a la frecuencia esperada.</exception>
    public IReadOnlyList<VentanaDeAudio> Anadir(BloqueDeAudio bloque)
    {
        ArgumentNullException.ThrowIfNull(bloque);

        if (bloque.FrecuenciaDeMuestreo != _frecuenciaDeMuestreo)
        {
            throw new ArgumentException(
                "El bloque viene a otra frecuencia de muestreo que la que se dijo al crear el troceador.",
                nameof(bloque));
        }

        var terminadas = new List<VentanaDeAudio>();
        var muestras = bloque.Muestras.Span;

        if (muestras.IsEmpty)
        {
            return terminadas;
        }

        if (!_hayVentana)
        {
            _inicio = InicioDeLaVentana(bloque.InstanteUtc, _periodo);
            _hayVentana = true;
            _rellenadas = 0;
            Array.Clear(_acumulado);
        }

        // Donde cae la primera muestra del bloque dentro de la ventana que se esta llenando.
        var desplazamiento = (long)Math.Round(
            (bloque.InstanteUtc - _inicio).TotalSeconds * _frecuenciaDeMuestreo);

        var i = 0;
        while (i < muestras.Length)
        {
            var destino = desplazamiento + i;

            if (destino < 0)
            {
                // Llega tarde: esas muestras eran de una ventana ya entregada.
                i += (int)Math.Min(muestras.Length - i, -destino);
                continue;
            }

            if (destino >= _acumulado.Length)
            {
                if (_rellenadas > 0)
                {
                    terminadas.Add(Crear());
                    _inicio += _periodo;
                    desplazamiento -= _acumulado.Length;
                    _rellenadas = 0;
                    Array.Clear(_acumulado);
                }
                else
                {
                    // Ventana vacia: no se entrega nada, se salta de golpe hasta donde toque.
                    var saltos = destino / _acumulado.Length;
                    _inicio += _periodo * saltos;
                    desplazamiento -= saltos * _acumulado.Length;
                }

                continue;
            }

            var cuantas = (int)Math.Min(muestras.Length - i, _acumulado.Length - destino);
            muestras.Slice(i, cuantas).CopyTo(_acumulado.AsSpan((int)destino));
            _rellenadas += cuantas;
            i += cuantas;
        }

        return terminadas;
    }

    /// <summary>Crea la ventana con lo que haya acumulado.</summary>
    private VentanaDeAudio Crear() => new(
        _inicio,
        _periodo,
        _acumulado.AsSpan().ToArray(),
        Math.Max(0, _acumulado.Length - _rellenadas));
}
