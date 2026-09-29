namespace Nodisla.Cuaderno.Audio.Reloj;

/// <summary>Como se mide la hora del modem.</summary>
public sealed class OpcionesDelReloj
{
    private TimeSpan _validezDeLaMedida = TimeSpan.FromMinutes(10);
    private TimeSpan _periodoDeSeguimiento = TimeSpan.FromMinutes(5);
    private TimeSpan _espera = TimeSpan.FromSeconds(2);
    private double _margenBuenoMs = 200.0;
    private double _margenFueraDeVentanaMs = 1000.0;

    /// <summary>
    /// Servidores de hora a los que se pregunta, en orden de preferencia.
    /// </summary>
    /// <remarks>
    /// El primero es el del Real Instituto y Observatorio de la Armada, que es la hora oficial
    /// en Espana y esta cerca; los demas estan para cuando ese no conteste. Se pregunta a
    /// todos a la vez y se toma la medida que menos haya tardado en ir y volver, que es la que
    /// menos incertidumbre arrastra.
    /// </remarks>
    public IList<string> Servidores { get; } = new List<string>
    {
        "hora.roa.es",
        "pool.ntp.org",
        "time.cloudflare.com",
        "time.windows.com",
    };

    /// <summary>
    /// Cuanto vale una medida antes de volver a preguntar. Por omision, diez minutos.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Si no es positivo.</exception>
    public TimeSpan ValidezDeLaMedida
    {
        get => _validezDeLaMedida;
        set
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "La validez de la medida tiene que ser positiva.");
            }

            _validezDeLaMedida = value;
        }
    }

    /// <summary>Lo que se espera a que un servidor conteste. Por omision, dos segundos.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Si no es positivo.</exception>
    public TimeSpan EsperaPorServidor
    {
        get => _espera;
        set
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "La espera por servidor tiene que ser positiva.");
            }

            _espera = value;
        }
    }

    /// <summary>
    /// Cada cuanto se remide el desvio cuando el seguimiento esta en marcha. Por omision,
    /// cinco minutos.
    /// </summary>
    /// <remarks>
    /// Medir solo al arrancar no basta: un reloj se va durante una sesion larga, y una tarde
    /// de concurso son muchas horas. Cinco minutos es barato —un paquete de 48 bytes— y coge
    /// la deriva mucho antes de que estropee un periodo.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Si no es positivo.</exception>
    public TimeSpan PeriodoDeSeguimiento
    {
        get => _periodoDeSeguimiento;
        set
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "El periodo de seguimiento tiene que ser positivo.");
            }

            _periodoDeSeguimiento = value;
        }
    }

    /// <summary>
    /// El seguimiento se pone en marcha solo al crear el reloj. Por omision, si.
    /// </summary>
    /// <remarks>
    /// Se apaga en las pruebas y en cualquier sitio donde no se quiera que el programa hable
    /// con la red por su cuenta.
    /// </remarks>
    public bool SeguimientoAutomatico { get; set; } = true;

    /// <summary>
    /// Hasta que desvio se considera que el reloj esta bien, en milisegundos. Por omision, 200.
    /// </summary>
    /// <remarks>
    /// Por debajo de aqui no hay nada que hacer: FT8 tiene de sobra con ese margen dentro de
    /// una ventana de quince segundos. El numero esta aqui y no suelto en medio del codigo
    /// precisamente para que se pueda discutir y cambiar sin tocar nada mas.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Si no es positivo.</exception>
    public double MargenBuenoMs
    {
        get => _margenBuenoMs;
        set
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "El margen de reloj bueno tiene que ser positivo.");
            }

            _margenBuenoMs = value;
        }
    }

    /// <summary>
    /// A partir de que desvio se empieza a <b>transmitir fuera de ventana</b>, en milisegundos.
    /// Por omision, 1000.
    /// </summary>
    /// <remarks>
    /// Este es el umbral que de verdad importa. Entre el margen bueno y este, lo que pasa es
    /// que se decodifica peor, y eso solo perjudica a quien lo tiene. Pasado este, la
    /// transmision se sale de la ventana y <b>molesta a los demas</b> sin que el operador se
    /// entere: por eso se dice con esas palabras y no con un numero.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Si no es mayor que el margen bueno.</exception>
    public double MargenFueraDeVentanaMs
    {
        get => _margenFueraDeVentanaMs;
        set
        {
            if (value <= _margenBuenoMs)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "El margen de transmisión fuera de ventana tiene que ser mayor que el margen bueno.");
            }

            _margenFueraDeVentanaMs = value;
        }
    }

    /// <summary>
    /// Servidor de hora que se le propone al operador para poner Windows en hora.
    /// </summary>
    /// <remarks>
    /// El del Real Instituto y Observatorio de la Armada: es la hora oficial en Espana, esta
    /// cerca y contesta. Comprobado en esta maquina.
    /// </remarks>
    public string ServidorRecomendado { get; set; } = "hora.roa.es";

    /// <summary>Copia estos ajustes, para no compartir el objeto con quien lo configuro.</summary>
    /// <returns>Una copia independiente.</returns>
    public OpcionesDelReloj Copiar()
    {
        var copia = new OpcionesDelReloj
        {
            _validezDeLaMedida = _validezDeLaMedida,
            _periodoDeSeguimiento = _periodoDeSeguimiento,
            _espera = _espera,
            _margenBuenoMs = _margenBuenoMs,
            _margenFueraDeVentanaMs = _margenFueraDeVentanaMs,
            SeguimientoAutomatico = SeguimientoAutomatico,
            ServidorRecomendado = ServidorRecomendado,
        };

        copia.Servidores.Clear();
        foreach (var servidor in Servidores)
        {
            copia.Servidores.Add(servidor);
        }

        return copia;
    }
}
