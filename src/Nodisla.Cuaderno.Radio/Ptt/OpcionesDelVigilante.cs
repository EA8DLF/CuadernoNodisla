namespace Nodisla.Cuaderno.Radio.Ptt;

/// <summary>Tiempos y manias del vigilante del PTT.</summary>
/// <remarks>
/// Los valores por omision estan pensados para operar de verdad: una transmision de FT8 dura
/// unos 13 segundos y una llamada larga de SSB en un pileup rara vez pasa del minuto, asi que
/// el tope de tres minutos deja sitio de sobra sin dejar el equipo cociendose si algo se cuelga.
/// </remarks>
public sealed class OpcionesDelVigilante
{
    /// <summary>Tope duro del tiempo maximo de transmision: ningun ajuste puede pasar de aqui.</summary>
    public static readonly TimeSpan TiempoMaximoPermitido = TimeSpan.FromMinutes(10);

    private TimeSpan _tiempoMaximo = TimeSpan.FromMinutes(3);
    private TimeSpan _tiempoSinLatido = TimeSpan.FromSeconds(15);
    private TimeSpan _pasoDeVigilancia = TimeSpan.FromMilliseconds(25);
    private TimeSpan _esperaDeSuelta = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Tiempo maximo que se permite estar en antena de una vez. Por omision, tres minutos.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Si no es positivo o pasa de <see cref="TiempoMaximoPermitido"/>.
    /// </exception>
    public TimeSpan TiempoMaximo
    {
        get => _tiempoMaximo;
        set
        {
            if (value <= TimeSpan.Zero || value > TiempoMaximoPermitido)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    $"El tiempo máximo de transmisión debe estar entre cero y {TiempoMaximoPermitido}.");
            }

            _tiempoMaximo = value;
        }
    }

    /// <summary>
    /// Tiempo sin latido tras el cual se baja el PTT. Por omision, quince segundos: cabe
    /// entera una transmision de FT8 sin que quien transmite tenga que latir.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Si no es positivo.</exception>
    public TimeSpan TiempoSinLatido
    {
        get => _tiempoSinLatido;
        set
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "El tiempo sin latido debe ser positivo.");
            }

            _tiempoSinLatido = value;
        }
    }

    /// <summary>Cada cuanto mira el vigilante si hay que soltar. Por omision, 25 ms.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Si no es positivo.</exception>
    public TimeSpan PasoDeVigilancia
    {
        get => _pasoDeVigilancia;
        set
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "El paso de vigilancia debe ser positivo.");
            }

            _pasoDeVigilancia = value;
        }
    }

    /// <summary>
    /// Lo que se espera a que cada via de suelta responda antes de pasar a la siguiente.
    /// Por omision, dos segundos: mas que eso y el cierre del proceso se nos va de las manos.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Si no es positivo.</exception>
    public TimeSpan EsperaDeSuelta
    {
        get => _esperaDeSuelta;
        set
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "La espera de suelta debe ser positiva.");
            }

            _esperaDeSuelta = value;
        }
    }

    /// <summary>
    /// Si el vigilante se engancha al cierre del proceso y a las excepciones no atendidas
    /// para bajar el PTT. Solo se desactiva en pruebas.
    /// </summary>
    public bool EngancharseAlCierreDelProceso { get; set; } = true;

    /// <summary>
    /// Salvaguardas de transmision: plan de banda, ROE y potencia maxima por banda. Se pueden
    /// cambiar en marcha con <see cref="VigilantePtt.CambiarSeguridad"/>.
    /// </summary>
    public OpcionesDeSeguridadDeTx Seguridad { get; set; } = new();

    /// <summary>Copia estos ajustes, para no compartir el objeto con quien lo configuro.</summary>
    /// <returns>Una copia independiente.</returns>
    public OpcionesDelVigilante Copiar() => new()
    {
        _tiempoMaximo = _tiempoMaximo,
        _tiempoSinLatido = _tiempoSinLatido,
        _pasoDeVigilancia = _pasoDeVigilancia,
        _esperaDeSuelta = _esperaDeSuelta,
        EngancharseAlCierreDelProceso = EngancharseAlCierreDelProceso,
        Seguridad = Seguridad,
    };
}
