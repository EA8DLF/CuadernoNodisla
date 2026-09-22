using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Propagacion.Geometria;

namespace Nodisla.Cuaderno.Propagacion.Prediccion;

/// <summary>Estado de la ionosfera que hace falta para estimar un circuito.</summary>
/// <param name="ManchasSolares">Numero de manchas suavizado equivalente.</param>
/// <param name="IndiceK">Indice K planetario, de 0 a 9.</param>
/// <param name="Tormenta">Hay tormenta geomagnetica en curso.</param>
public sealed record CondicionesIonosfericas(double ManchasSolares, double IndiceK, bool Tormenta)
{
    /// <summary>
    /// Condiciones de reserva para cuando no hay indices: ciclo medio y campo tranquilo.
    /// </summary>
    /// <remarks>
    /// Se usan solo para no dejar al operador sin nada. Quien las use tiene que decir que los
    /// indices no estaban disponibles.
    /// </remarks>
    public static CondicionesIonosfericas Supuestas { get; } = new(70.0, 2.0, false);
}

/// <summary>Resultado del modelo para un salto y una frecuencia.</summary>
/// <param name="MufMhz">Maxima frecuencia utilizable mediana del circuito.</param>
/// <param name="Saltos">Numero de saltos por la capa F2.</param>
/// <param name="ElevacionGrados">Angulo de salida del rayo sobre el horizonte.</param>
/// <param name="CaminoOblicuoKm">Longitud del rayo, que es mayor que la distancia por el suelo.</param>
public sealed record GeometriaIonosferica(
    double MufMhz,
    int Saltos,
    double ElevacionGrados,
    double CaminoOblicuoKm);

/// <summary>
/// Aproximacion propia de MUF, absorcion y fiabilidad por banda. <b>No es VOACAP.</b>
/// </summary>
/// <remarks>
/// <para>
/// <b>Que es esto y que no es.</b> Es una estimacion construida con formulas publicadas, no un
/// motor de prediccion reconocido. Da ordenes de magnitud correctos y ordena bien las bandas
/// entre si, que es lo que sirve para decidir donde llamar. No da la cifra que daria VOACAP ni
/// pretende darla. Todo lo que sale de aqui va marcado como aproximacion.
/// </para>
/// <para><b>De donde sale cada paso.</b></para>
/// <list type="number">
/// <item>
/// <description>
/// <b>Manchas a partir del flujo solar.</b> Se invierte la relacion de Covington
/// <c>F10,7 = 63,75 + 0,728 R + 0,00089 R^2</c>, que es la que usa el propio NOAA para pasar de
/// numero de manchas a flujo. Error tipico de unas diez manchas.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>Frecuencia critica de la capa F2.</b> Envolvente de Chapman: la ionizacion sigue al coseno
/// del angulo cenital elevado a un cuarto, con un suelo nocturno del 35 % del valor de mediodia,
/// y una referencia de mediodia que crece con las manchas segun
/// <c>foF2 = 8,0 + 0,045 R</c> MHz. Esa recta reproduce los valores tipicos de ionosonda a baja
/// latitud: unos 8 MHz en minimo solar y unos 15 MHz en maximo. La caida hacia los polos se
/// modela lineal a partir de los 20 grados de latitud geomagnetica.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>MUF.</b> Ley de la secante con la geometria del salto: se reparte la distancia en saltos de
/// 4000 km como maximo, se calcula el angulo de salida para una capa a 300 km y se multiplica la
/// frecuencia critica por la secante del angulo de incidencia. Para 3000 km sale un factor de
/// unos 3,3, que es el M(3000)F2 que miden las ionosondas.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>Absorcion.</b> Formula de absorcion no desviativa de la Recomendacion UIT-R P.533:
/// <c>677,2 sec(phi100) (1 + 0,0037 R) cos(0,881 X)^1,3 / ((f + fH)^1,98 + 10,2)</c> decibelios
/// por salto, con la girofrecuencia tomada en 1,4 MHz. De noche el coseno se anula y la capa D
/// desaparece, que es justo lo que pasa.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>Presupuesto de senal.</b> Perdida basica en espacio libre sobre el camino oblicuo, mas la
/// absorcion, mas 3 dB por cada reflexion intermedia en el suelo, mas los 9,9 dB de perdida
/// sistematica que la P.533 anade a los circuitos de menos de 9000 km.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>Ruido.</b> Recomendacion UIT-R P.372: ruido artificial <c>Fam = c - d log10(f)</c> con las
/// constantes de cada ambiente, y ruido galactico por debajo, quedandose con el mayor de los dos.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>Fiabilidad.</b> Producto de dos probabilidades. La de que la MUF del dia supere la
/// frecuencia de trabajo, con la MUF repartida log-normal alrededor de la mediana y desviacion
/// 0,13 en logaritmo, que es lo que hace que la FOT caiga en el 85 % de la MUF con un 90 % de
/// dias. Y la de que la relacion senal-ruido llegue al minimo pedido, con 8 dB de desviacion
/// dia a dia.
/// </description>
/// </item>
/// </list>
/// <para>
/// <b>Que precision cabe esperar.</b> En la MUF, del orden del 20 % en trayectos de latitud media
/// y campo tranquilo. En la senal, no menos de 10 dB de error. La ordenacion de las bandas entre
/// si es bastante mas fiable que el numero de cada una.
/// </para>
/// <para>
/// <b>Donde falla, y falla de verdad.</b>
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>Tormentas geomagneticas.</b> La correccion por indice K es un apano lineal. Durante una
/// tormenta de verdad la capa F2 se desploma de formas que este modelo no reproduce.
/// </description></item>
/// <item><description>
/// <b>Trayectos transpolares.</b> No hay absorcion auroral ni mancha polar. Un camino por encima
/// de los 60 grados geomagneticos saldra mejor de lo que es.
/// </description></item>
/// <item><description>
/// <b>Bandas altas en minimo solar.</b> Sin capa E esporadica ni dispersion transecuatorial, 10 m
/// y 6 m salen cerrados cuando en la realidad abren a ratos. Lo que diga de 6 m es solo capa F2.
/// </description></item>
/// <item><description>
/// <b>Anomalia estacional y anomalia ecuatorial.</b> No estan. En invierno a mediodia la capa F2
/// es mas densa de lo que dice el modelo, y en las crestas ecuatoriales tambien.
/// </description></item>
/// <item><description>
/// <b>Antenas.</b> Se supone la misma ganancia en los dos extremos y nada de diagrama vertical.
/// Un angulo de salida que la antena no radie no se penaliza.
/// </description></item>
/// </list>
/// </remarks>
public static class ModeloMufLuf
{
    /// <summary>Altura virtual de reflexion de la capa F2, en kilometros.</summary>
    public const double AlturaCapaF2Km = 300.0;

    /// <summary>Altura de la capa D, donde se produce la absorcion, en kilometros.</summary>
    public const double AlturaCapaDKm = 100.0;

    /// <summary>Distancia maxima de un salto por la capa F2, en kilometros.</summary>
    public const double SaltoMaximoKm = 4000.0;

    /// <summary>Girofrecuencia del electron que se usa en la absorcion, en megahercios.</summary>
    public const double GirofrecuenciaMhz = 1.4;

    /// <summary>Perdida por cada reflexion intermedia en el suelo, en decibelios.</summary>
    public const double PerdidaPorReflexionDb = 3.0;

    /// <summary>
    /// Perdida sistematica que la P.533 anade a los circuitos de menos de 9000 km, en decibelios.
    /// </summary>
    public const double PerdidaSistematicaDb = 9.9;

    /// <summary>Desviacion logaritmica de la MUF de un dia para otro.</summary>
    public const double DispersionDeLaMuf = 0.13;

    /// <summary>Desviacion de la senal de un dia para otro, en decibelios.</summary>
    public const double DispersionDeLaSenalDb = 8.0;

    /// <summary>
    /// Tope de la absorcion acumulada, en decibelios. Por encima de eso la onda ya no llega, y
    /// seguir sumando decibelios solo produce cifras absurdas en pantalla.
    /// </summary>
    public const double AbsorcionMaximaDb = 150.0;

    /// <summary>
    /// Relacion senal-ruido por debajo de la cual no se da ninguna cifra: no significa nada.
    /// </summary>
    public const double RelacionSinSentidoDb = -40.0;

    private const double RadioTerrestreKm = Geodesia.RadioTerrestreKm;
    private const double Rad = Math.PI / 180.0;
    private const double Deg = 180.0 / Math.PI;

    /// <summary>
    /// Numero de manchas equivalente a un flujo solar dado, invirtiendo la relacion de Covington.
    /// </summary>
    /// <param name="flujoSolar">Flujo solar a 10,7 cm en unidades de flujo solar.</param>
    public static double ManchasDesdeFlujo(double flujoSolar)
    {
        const double a = 0.00089;
        const double b = 0.728;
        const double c = 63.75;

        var discriminante = (b * b) - (4.0 * a * (c - flujoSolar));
        if (discriminante <= 0)
        {
            return 0.0;
        }

        var manchas = (-b + Math.Sqrt(discriminante)) / (2.0 * a);
        return Math.Max(0.0, manchas);
    }

    /// <summary>Frecuencia critica de la capa F2 en un punto y un momento, en megahercios.</summary>
    /// <param name="punto">Punto de control, normalmente el centro de un salto.</param>
    /// <param name="momentoUtc">Momento para el que se calcula.</param>
    /// <param name="condiciones">Manchas y actividad geomagnetica.</param>
    public static double FrecuenciaCriticaF2Mhz(
        Coordenada punto,
        DateTimeOffset momentoUtc,
        CondicionesIonosfericas condiciones)
    {
        var referenciaMediodia = 8.0 + (0.045 * Math.Max(0.0, condiciones.ManchasSolares));

        var altura = CalculadoraSolar.AlturaSolarGrados(punto, momentoUtc);
        var cosenoCenital = Math.Max(0.0, Math.Sin(altura * Rad));
        var envolvente = 0.35 + (0.65 * Math.Pow(cosenoCenital, 0.25));

        var latitudGeomagnetica = Math.Abs(GeometriaDeTrayecto.LatitudGeomagneticaGrados(punto));
        var factorLatitud = Math.Clamp(1.0 - (0.005 * Math.Max(0.0, latitudGeomagnetica - 20.0)), 0.55, 1.0);

        var factorTormenta = FactorDeTormenta(latitudGeomagnetica, condiciones.IndiceK);

        return referenciaMediodia * envolvente * factorLatitud * factorTormenta;
    }

    /// <summary>
    /// Cuanto hunde la tormenta geomagnetica la capa F2 en una latitud geomagnetica dada.
    /// </summary>
    /// <param name="latitudGeomagneticaGrados">Latitud geomagnetica absoluta del punto.</param>
    /// <param name="indiceK">Indice K planetario.</param>
    /// <returns>Factor de 0,5 a 1 por el que se multiplica la frecuencia critica.</returns>
    /// <remarks>
    /// Correccion tosca y reconocida como tal: lineal en K a partir de 3 y creciente con la
    /// latitud a partir de los 30 grados geomagneticos. Sirve para que el modelo no ensene
    /// bandas altas abiertas en plena tormenta, no para predecir una tormenta.
    /// </remarks>
    public static double FactorDeTormenta(double latitudGeomagneticaGrados, double indiceK)
    {
        var exceso = Math.Max(0.0, indiceK - 3.0);
        var peso = Math.Clamp((Math.Abs(latitudGeomagneticaGrados) - 30.0) / 40.0, 0.0, 1.0);
        return Math.Clamp(1.0 - (0.06 * exceso * peso), 0.5, 1.0);
    }

    /// <summary>Numero de saltos por la capa F2 que hacen falta para cubrir una distancia.</summary>
    /// <param name="distanciaKm">Distancia por el suelo.</param>
    public static int SaltosNecesarios(double distanciaKm) =>
        Math.Max(1, (int)Math.Ceiling(distanciaKm / SaltoMaximoKm));

    /// <summary>
    /// Angulo de salida del rayo sobre el horizonte para un salto, en grados.
    /// </summary>
    /// <param name="distanciaSaltoKm">Distancia por el suelo de un solo salto.</param>
    /// <param name="alturaCapaKm">Altura virtual de la capa que refleja.</param>
    public static double ElevacionGrados(double distanciaSaltoKm, double alturaCapaKm)
    {
        var mitad = Math.Max(1e-6, distanciaSaltoKm / 2.0) / RadioTerrestreKm;
        var elevacion = Math.Atan2(
            Math.Cos(mitad) - (RadioTerrestreKm / (RadioTerrestreKm + alturaCapaKm)),
            Math.Sin(mitad));
        return Math.Max(0.0, elevacion * Deg);
    }

    /// <summary>
    /// Secante del angulo de incidencia del rayo en una capa, que es el factor por el que sube la
    /// frecuencia utilizable respecto a la critica.
    /// </summary>
    /// <param name="elevacionGrados">Angulo de salida del rayo.</param>
    /// <param name="alturaCapaKm">Altura de la capa.</param>
    public static double FactorOblicuidad(double elevacionGrados, double alturaCapaKm)
    {
        var seno = Math.Cos(elevacionGrados * Rad) * RadioTerrestreKm / (RadioTerrestreKm + alturaCapaKm);
        seno = Math.Clamp(seno, -1.0, 1.0);
        var coseno = Math.Sqrt(Math.Max(1e-6, 1.0 - (seno * seno)));
        return 1.0 / coseno;
    }

    /// <summary>Longitud del rayo de un salto, en kilometros.</summary>
    /// <param name="distanciaSaltoKm">Distancia por el suelo de un salto.</param>
    /// <param name="alturaCapaKm">Altura virtual de la capa.</param>
    public static double CaminoOblicuoSaltoKm(double distanciaSaltoKm, double alturaCapaKm)
    {
        var mitad = Math.Max(1e-6, distanciaSaltoKm / 2.0) / RadioTerrestreKm;
        var cima = RadioTerrestreKm + alturaCapaKm;
        var lado = Math.Sqrt(
            (RadioTerrestreKm * RadioTerrestreKm) + (cima * cima)
            - (2.0 * RadioTerrestreKm * cima * Math.Cos(mitad)));
        return 2.0 * lado;
    }

    /// <summary>
    /// Geometria y MUF de un trayecto entero: reparte los saltos y se queda con el salto peor.
    /// </summary>
    /// <param name="origen">Punto de partida.</param>
    /// <param name="destino">Punto de llegada.</param>
    /// <param name="momentoUtc">Momento para el que se calcula.</param>
    /// <param name="condiciones">Manchas y actividad geomagnetica.</param>
    public static GeometriaIonosferica Calcular(
        Coordenada origen,
        Coordenada destino,
        DateTimeOffset momentoUtc,
        CondicionesIonosfericas condiciones)
    {
        var distancia = Geodesia.DistanciaKm(origen, destino);
        var saltos = SaltosNecesarios(distancia);
        var distanciaSalto = distancia / saltos;
        var elevacion = ElevacionGrados(distanciaSalto, AlturaCapaF2Km);
        var oblicuidad = FactorOblicuidad(elevacion, AlturaCapaF2Km);

        // La MUF del trayecto la manda el salto peor: basta con que uno no aguante la frecuencia
        // para que el circuito no cierre.
        var muf = double.MaxValue;
        foreach (var control in GeometriaDeTrayecto.PuntosDeControl(origen, destino, saltos))
        {
            var critica = FrecuenciaCriticaF2Mhz(control, momentoUtc, condiciones);
            muf = Math.Min(muf, critica * oblicuidad);
        }

        var camino = saltos * CaminoOblicuoSaltoKm(distanciaSalto, AlturaCapaF2Km);
        return new GeometriaIonosferica(muf, saltos, elevacion, camino);
    }

    /// <summary>Absorcion de la capa D en todo el trayecto, en decibelios.</summary>
    /// <param name="origen">Punto de partida.</param>
    /// <param name="destino">Punto de llegada.</param>
    /// <param name="momentoUtc">Momento para el que se calcula.</param>
    /// <param name="frecuenciaMhz">Frecuencia de trabajo.</param>
    /// <param name="condiciones">Manchas y actividad geomagnetica.</param>
    /// <param name="geometria">Geometria ya calculada del trayecto.</param>
    public static double AbsorcionDb(
        Coordenada origen,
        Coordenada destino,
        DateTimeOffset momentoUtc,
        double frecuenciaMhz,
        CondicionesIonosfericas condiciones,
        GeometriaIonosferica geometria)
    {
        var secante = FactorOblicuidad(geometria.ElevacionGrados, AlturaCapaDKm);
        var denominador = Math.Pow(frecuenciaMhz + GirofrecuenciaMhz, 1.98) + 10.2;
        var factorSolar = 1.0 + (0.0037 * Math.Max(0.0, condiciones.ManchasSolares));

        var total = 0.0;
        foreach (var control in GeometriaDeTrayecto.PuntosDeControl(origen, destino, geometria.Saltos))
        {
            var altura = CalculadoraSolar.AlturaSolarGrados(control, momentoUtc);
            var cenital = 90.0 - altura;
            var coseno = Math.Cos(0.881 * cenital * Rad);
            if (coseno <= 0)
            {
                // De noche no hay capa D: la absorcion no desviativa se va a cero.
                continue;
            }

            total += 677.2 * secante * factorSolar * Math.Pow(coseno, 1.3) / denominador;
        }

        return Math.Min(total, AbsorcionMaximaDb);
    }

    /// <summary>Ruido en el receptor, en decibelios sobre un vatio.</summary>
    /// <param name="frecuenciaMhz">Frecuencia de trabajo.</param>
    /// <param name="anchoDeBandaHz">Ancho de banda del receptor.</param>
    /// <param name="ambiente">Ambiente de ruido del emplazamiento.</param>
    /// <remarks>
    /// Constantes de la Recomendacion UIT-R P.372. Se toma el mayor entre el ruido artificial del
    /// emplazamiento y el ruido galactico, que es el suelo por debajo del cual no se baja.
    /// </remarks>
    public static double RuidoDbw(double frecuenciaMhz, double anchoDeBandaHz, AmbienteDeRuido ambiente)
    {
        var (c, d) = ambiente switch
        {
            AmbienteDeRuido.Industrial => (76.8, 27.7),
            AmbienteDeRuido.Residencial => (72.5, 27.7),
            AmbienteDeRuido.Rural => (67.2, 27.7),
            _ => (53.6, 28.6),
        };

        var logaritmo = Math.Log10(Math.Max(0.05, frecuenciaMhz));
        var artificial = c - (d * logaritmo);
        var galactico = 52.0 - (23.0 * logaritmo);
        var figuraDeRuido = Math.Max(artificial, galactico);

        return -204.0 + figuraDeRuido + (10.0 * Math.Log10(Math.Max(1.0, anchoDeBandaHz)));
    }

    /// <summary>Perdida basica del circuito, en decibelios.</summary>
    /// <param name="frecuenciaMhz">Frecuencia de trabajo.</param>
    /// <param name="geometria">Geometria del trayecto.</param>
    /// <param name="absorcionDb">Absorcion de la capa D ya calculada.</param>
    public static double PerdidaDb(double frecuenciaMhz, GeometriaIonosferica geometria, double absorcionDb)
    {
        var espacioLibre = 32.45
                           + (20.0 * Math.Log10(Math.Max(0.05, frecuenciaMhz)))
                           + (20.0 * Math.Log10(Math.Max(1.0, geometria.CaminoOblicuoKm)));
        var reflexiones = PerdidaPorReflexionDb * (geometria.Saltos - 1);
        return espacioLibre + absorcionDb + reflexiones + PerdidaSistematicaDb;
    }

    /// <summary>
    /// Probabilidad de que la MUF del dia supere la frecuencia de trabajo.
    /// </summary>
    /// <param name="mufMhz">MUF mediana del circuito.</param>
    /// <param name="frecuenciaMhz">Frecuencia de trabajo.</param>
    public static double ProbabilidadDeMuf(double mufMhz, double frecuenciaMhz)
    {
        if (mufMhz <= 0 || frecuenciaMhz <= 0)
        {
            return 0.0;
        }

        var z = Math.Log(mufMhz / frecuenciaMhz) / DispersionDeLaMuf;
        return NormalAcumulada(z);
    }

    /// <summary>Probabilidad de que la senal llegue con la relacion senal-ruido pedida.</summary>
    /// <param name="relacionDb">Relacion senal-ruido mediana prevista.</param>
    /// <param name="requeridaDb">Relacion senal-ruido que se considera suficiente.</param>
    public static double ProbabilidadDeSenal(double relacionDb, double requeridaDb) =>
        NormalAcumulada((relacionDb - requeridaDb) / DispersionDeLaSenalDb);

    /// <summary>Funcion de distribucion normal tipificada.</summary>
    /// <param name="z">Valor tipificado.</param>
    /// <remarks>
    /// Aproximacion de Abramowitz y Stegun 7.1.26 sobre la funcion de error: cinco cifras buenas,
    /// de sobra para una probabilidad que despues se ensena redondeada al uno por ciento.
    /// </remarks>
    public static double NormalAcumulada(double z) => 0.5 * (1.0 + FuncionDeError(z / Math.Sqrt(2.0)));

    private static double FuncionDeError(double x)
    {
        const double a1 = 0.254829592;
        const double a2 = -0.284496736;
        const double a3 = 1.421413741;
        const double a4 = -1.453152027;
        const double a5 = 1.061405429;
        const double p = 0.3275911;

        var signo = x < 0 ? -1.0 : 1.0;
        var absoluto = Math.Abs(x);
        var t = 1.0 / (1.0 + (p * absoluto));
        var y = 1.0 - ((((((((a5 * t) + a4) * t) + a3) * t) + a2) * t + a1) * t * Math.Exp(-absoluto * absoluto));
        return signo * y;
    }
}
