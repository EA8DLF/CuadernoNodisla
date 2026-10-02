using System.Globalization;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Satelites.Orbital;

namespace Nodisla.Cuaderno.Satelites.Prediccion;

/// <summary>Un instante del paso con lo que se ve en el.</summary>
/// <param name="Instante">Momento, en UTC.</param>
/// <param name="AzimutGrados">Azimut desde el norte verdadero.</param>
/// <param name="ElevacionGrados">Elevacion sobre el horizonte.</param>
/// <param name="DistanciaKm">Distancia al satelite.</param>
public readonly record struct MomentoDelPaso(
    DateTimeOffset Instante,
    double AzimutGrados,
    double ElevacionGrados,
    double DistanciaKm);

/// <summary>Un paso completo del satelite por encima del horizonte de la estacion.</summary>
/// <param name="Satelite">Nombre del satelite.</param>
/// <param name="Salida">Cuando asoma por el umbral de elevacion, y por donde.</param>
/// <param name="Culminacion">Cuando esta mas alto, y donde.</param>
/// <param name="Puesta">Cuando se pierde por el umbral, y por donde.</param>
/// <param name="EpocaDeLosElementos">Epoca de los elementos con los que se calculo.</param>
public sealed record PasoDeSatelite(
    string Satelite,
    MomentoDelPaso Salida,
    MomentoDelPaso Culminacion,
    MomentoDelPaso Puesta,
    DateTimeOffset EpocaDeLosElementos)
{
    /// <summary>Cuanto dura el paso por encima del umbral.</summary>
    public TimeSpan Duracion => Puesta.Instante - Salida.Instante;

    /// <summary>Elevacion maxima que alcanza, en grados.</summary>
    public double ElevacionMaximaGrados => Culminacion.ElevacionGrados;

    /// <summary>
    /// El paso es rasante: sube poco y se pierde con facilidad detras de cualquier obstaculo.
    /// </summary>
    /// <remarks>
    /// Diez grados es el umbral practico: por debajo de ahi mandan los edificios, los arboles
    /// y el ruido del suelo, y muchos pasos que el programa anuncia no se oyen. Se marca en
    /// vez de esconderlo, porque desde un sitio despejado si valen.
    /// </remarks>
    public bool EsRasante => ElevacionMaximaGrados < 10.0;

    /// <summary>Resumen de una linea para ensenar en la lista de pasos.</summary>
    /// <param name="ahoraUtc">Momento actual, para decir la antiguedad de los elementos.</param>
    /// <param name="frescura">Plazo dentro del cual los elementos se dan por buenos.</param>
    public string Describir(DateTimeOffset ahoraUtc, TimeSpan frescura)
    {
        // Las horas UTC van con su formato tecnico fijo; los grados y minutos, con la cultura.
        var hora = CultureInfo.InvariantCulture;
        var texto = Textos.F(
            "Servicios.Satelites.Paso",
            Satelite,
            Salida.Instante.ToString("dd/MM HH:mm:ss", hora),
            Salida.AzimutGrados,
            Culminacion.Instante.ToString("HH:mm:ss", hora),
            ElevacionMaximaGrados,
            Culminacion.AzimutGrados,
            Puesta.Instante.ToString("HH:mm:ss", hora),
            Puesta.AzimutGrados,
            Duracion.TotalMinutes);

        if (EsRasante)
        {
            texto += Textos.T("Servicios.Satelites.PasoRasante");
        }

        var edad = ahoraUtc - EpocaDeLosElementos;
        if (edad > frescura)
        {
            texto += Textos.F("Servicios.Satelites.OjoElementos", ElementosOrbitales.TextoDeEdad(edad));
        }

        return texto + ".";
    }
}

/// <summary>Ajustes de la busqueda de pasos.</summary>
public sealed class OpcionesDePrediccion
{
    /// <summary>
    /// Elevacion minima que tiene que alcanzar un paso para que se anuncie, en grados.
    /// </summary>
    /// <remarks>
    /// Cero por omision: se ensenan todos y cada uno lleva marcado si es rasante. Subir este
    /// umbral es la forma mas rapida de perder pasos buenos desde un sitio despejado, que es
    /// el caso de quien opera desde la costa.
    /// </remarks>
    public double ElevacionMinimaGrados { get; set; }

    /// <summary>
    /// Paso de la exploracion inicial. Cuanto mas fino, mas cuesta y menos se escapa.
    /// </summary>
    /// <remarks>
    /// Treinta segundos. La busqueda no mira cruces de umbral sino maximos de elevacion, asi
    /// que un paso rasante de dos minutos sobre el horizonte se detecta igual; con deteccion
    /// de cruces y este mismo paso se perderia.
    /// </remarks>
    public TimeSpan PasoDeExploracion { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Precision con la que se afinan la salida, la culminacion y la puesta.</summary>
    public TimeSpan Precision { get; set; } = TimeSpan.FromSeconds(0.5);

    /// <summary>Cuantos pasos como mucho se devuelven de una vez.</summary>
    public int MaximoDePasos { get; set; } = 50;
}

/// <summary>
/// Busca los pasos de un satelite sobre una estacion dentro de una ventana de tiempo.
/// </summary>
/// <remarks>
/// La busqueda es de maximos de elevacion, no de cruces del umbral. Es deliberado: un paso
/// rasante puede estar sobre el horizonte menos tiempo que el paso de exploracion entre dos
/// muestras y, buscando cruces, desaparece sin dejar rastro. Buscando maximos se detecta la
/// culminacion —que siempre existe, este o no sobre el horizonte— y solo despues se mira si
/// supera el umbral.
/// </remarks>
public sealed class PredictorDePasos(OpcionesDePrediccion? opciones = null)
{
    private readonly OpcionesDePrediccion _opciones = opciones ?? new OpcionesDePrediccion();

    /// <summary>Busca los pasos de un satelite en una ventana de tiempo.</summary>
    /// <param name="propagador">Propagador ya construido con los elementos del satelite.</param>
    /// <param name="observador">La estacion desde la que se mira.</param>
    /// <param name="desdeUtc">Principio de la ventana.</param>
    /// <param name="hastaUtc">Final de la ventana.</param>
    /// <returns>Los pasos encontrados, en orden.</returns>
    public IReadOnlyList<PasoDeSatelite> Buscar(
        Sgp4 propagador,
        Observador observador,
        DateTimeOffset desdeUtc,
        DateTimeOffset hastaUtc)
    {
        ArgumentNullException.ThrowIfNull(propagador);

        if (hastaUtc <= desdeUtc)
        {
            return [];
        }

        var pasos = new List<PasoDeSatelite>();
        var paso = _opciones.PasoDeExploracion;
        if (paso <= TimeSpan.Zero)
        {
            paso = TimeSpan.FromSeconds(30);
        }

        var anterior = desdeUtc;
        var actual = desdeUtc + paso;
        var elevAnterior = Elevacion(propagador, observador, anterior);
        var elevActual = Elevacion(propagador, observador, actual);

        while (actual < hastaUtc && pasos.Count < _opciones.MaximoDePasos)
        {
            var siguiente = actual + paso;
            var elevSiguiente = Elevacion(propagador, observador, siguiente);

            // Maximo local de la elevacion: aqui esta la culminacion de un paso, suba lo que suba.
            if (elevActual >= elevAnterior && elevActual >= elevSiguiente
                && elevActual > _opciones.ElevacionMinimaGrados)
            {
                var culminacion = AfinarMaximo(propagador, observador, anterior, siguiente);
                if (culminacion.ElevacionGrados > _opciones.ElevacionMinimaGrados)
                {
                    var salida = Cruce(propagador, observador, culminacion.Instante, desdeUtc - paso, -paso);
                    var puesta = Cruce(propagador, observador, culminacion.Instante, hastaUtc + paso, paso);

                    pasos.Add(new PasoDeSatelite(
                        propagador.Elementos.Nombre,
                        salida,
                        culminacion,
                        puesta,
                        propagador.Elementos.Epoca));

                    // Se salta al final del paso para no volver a encontrarlo.
                    var reanudar = puesta.Instante + paso;
                    if (reanudar > siguiente)
                    {
                        anterior = reanudar;
                        actual = anterior + paso;
                        elevAnterior = Elevacion(propagador, observador, anterior);
                        elevActual = Elevacion(propagador, observador, actual);
                        continue;
                    }
                }
            }

            anterior = actual;
            actual = siguiente;
            elevAnterior = elevActual;
            elevActual = elevSiguiente;
        }

        return pasos;
    }

    private static double Elevacion(Sgp4 propagador, Observador observador, DateTimeOffset instante) =>
        propagador.TryPropagar(instante, out var estado, out _)
            ? Topocentrico.Mirar(estado, observador).ElevacionGrados
            : double.NegativeInfinity;

    private static MomentoDelPaso Momento(Sgp4 propagador, Observador observador, DateTimeOffset instante)
    {
        if (!propagador.TryPropagar(instante, out var estado, out var fallo))
        {
            throw new InvalidOperationException(
                $"No se puede propagar «{propagador.Elementos.Nombre}»: {Sgp4.Descripcion(fallo)}");
        }

        var vista = Topocentrico.Mirar(estado, observador);
        return new MomentoDelPaso(instante, vista.AzimutGrados, vista.ElevacionGrados, vista.DistanciaKm);
    }

    /// <summary>
    /// Afina la culminacion por seccion aurea dentro del intervalo donde se detecto el maximo.
    /// </summary>
    /// <remarks>
    /// La elevacion es suave y con un unico maximo dentro del paso, asi que la seccion aurea
    /// converge sin derivadas y sin sorpresas. Con una parabola por tres puntos tambien se
    /// podria, pero se porta mal cuando el maximo cae justo en el borde del intervalo.
    /// </remarks>
    private MomentoDelPaso AfinarMaximo(
        Sgp4 propagador,
        Observador observador,
        DateTimeOffset izquierda,
        DateTimeOffset derecha)
    {
        const double razon = 0.6180339887498949;
        var a = izquierda;
        var b = derecha;

        var c = a + TimeSpan.FromTicks((long)((b - a).Ticks * (1 - razon)));
        var d = a + TimeSpan.FromTicks((long)((b - a).Ticks * razon));
        var fc = Elevacion(propagador, observador, c);
        var fd = Elevacion(propagador, observador, d);

        while (b - a > _opciones.Precision)
        {
            if (fc > fd)
            {
                b = d;
                d = c;
                fd = fc;
                c = a + TimeSpan.FromTicks((long)((b - a).Ticks * (1 - razon)));
                fc = Elevacion(propagador, observador, c);
            }
            else
            {
                a = c;
                c = d;
                fc = fd;
                d = a + TimeSpan.FromTicks((long)((b - a).Ticks * razon));
                fd = Elevacion(propagador, observador, d);
            }
        }

        return Momento(propagador, observador, a + TimeSpan.FromTicks((b - a).Ticks / 2));
    }

    /// <summary>
    /// Busca hacia atras o hacia delante el instante en que la elevacion cruza el umbral.
    /// </summary>
    /// <remarks>
    /// Se sale de la culminacion, donde seguro que estamos por encima del umbral, y se camina
    /// en la direccion pedida hasta bajar de el; despues se parte el intervalo por la mitad.
    /// Si se llega al limite de la ventana sin cruzar, se devuelve el limite: el paso esta
    /// cortado por el borde de la busqueda y decirlo asi es mas honesto que inventar una hora.
    /// </remarks>
    private MomentoDelPaso Cruce(
        Sgp4 propagador,
        Observador observador,
        DateTimeOffset culminacion,
        DateTimeOffset limite,
        TimeSpan paso)
    {
        var adelante = paso > TimeSpan.Zero;
        var dentro = culminacion;
        var fuera = culminacion + paso;

        while ((adelante ? fuera <= limite : fuera >= limite)
               && Elevacion(propagador, observador, fuera) > _opciones.ElevacionMinimaGrados)
        {
            dentro = fuera;
            fuera += paso;
        }

        // Se ha llegado al borde de la ventana sin cruzar: el paso esta cortado por la busqueda.
        if (adelante ? fuera > limite : fuera < limite)
        {
            return Momento(propagador, observador, limite);
        }

        while ((adelante ? fuera - dentro : dentro - fuera) > _opciones.Precision)
        {
            var medio = dentro + TimeSpan.FromTicks((fuera - dentro).Ticks / 2);
            if (Elevacion(propagador, observador, medio) > _opciones.ElevacionMinimaGrados)
            {
                dentro = medio;
            }
            else
            {
                fuera = medio;
            }
        }

        return Momento(propagador, observador, fuera);
    }
}
