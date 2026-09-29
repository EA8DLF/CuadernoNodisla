using System.Globalization;
using System.Text;

namespace Nodisla.Cuaderno.Concursos.Telegrafia;

/// <summary>Que se practica.</summary>
public enum EjercicioDeTelegrafia
{
    /// <summary>Grupos de cinco caracteres al azar, que es como se examina.</summary>
    GruposAlAzar,
    /// <summary>Solo letras.</summary>
    Letras,
    /// <summary>Solo numeros.</summary>
    Numeros,
    /// <summary>Indicativos con forma de indicativo de verdad.</summary>
    Indicativos,
    /// <summary>Palabras y abreviaturas del oficio: <c>CQ</c>, <c>QTH</c>, <c>73</c>…</summary>
    Abreviaturas,
    /// <summary>Un intercambio de concurso completo.</summary>
    Concurso,
    /// <summary>Metodo Koch: se empieza con dos caracteres y se va anadiendo de uno en uno.</summary>
    Koch,
}

/// <summary>Como se quiere practicar.</summary>
public sealed record OpcionesDeEntrenamiento
{
    /// <summary>Que se practica.</summary>
    public EjercicioDeTelegrafia Ejercicio { get; init; } = EjercicioDeTelegrafia.GruposAlAzar;

    /// <summary>Cuantos grupos o elementos por tanda.</summary>
    public int Grupos { get; init; } = 10;

    /// <summary>Cuantos caracteres tiene cada grupo.</summary>
    public int Largo { get; init; } = 5;

    /// <summary>Cuantos caracteres del orden de Koch se usan, del segundo en adelante.</summary>
    public int CaracteresDeKoch { get; init; } = 2;

    /// <summary>Velocidad de partida, en palabras por minuto.</summary>
    public int PpmInicial { get; init; } = 15;

    /// <summary>Velocidad maxima a la que se quiere llegar.</summary>
    public int PpmMaximas { get; init; } = 25;

    /// <summary>Cuanto sube la velocidad cada vez que una tanda sale bien.</summary>
    public int PasoDePpm { get; init; } = 1;

    /// <summary>
    /// Que parte hay que acertar para que suba la velocidad, de 0 a 1.
    /// </summary>
    /// <remarks>
    /// Noventa por ciento es lo habitual y tiene sentido: subir con menos deja huecos que
    /// luego cuesta mucho mas tapar, y exigir el cien por cien estanca al alumno en una letra.
    /// </remarks>
    public double AciertoParaSubir { get; init; } = 0.9;

    /// <summary>Semilla del azar. Sirve para repetir una tanda exacta y para las pruebas.</summary>
    public int? Semilla { get; init; }
}

/// <summary>Una tanda de practica.</summary>
/// <param name="Texto">Lo que hay que copiar, ya listo para manipular.</param>
/// <param name="Ppm">Velocidad a la que se manipula.</param>
/// <param name="PpmEfectivas">Velocidad aparente, menor si se usa Farnsworth.</param>
public sealed record TandaDeEntrenamiento(string Texto, int Ppm, int PpmEfectivas)
{
    /// <summary>Los grupos por separado, para corregir uno a uno.</summary>
    public IReadOnlyList<string> Grupos =>
        Texto.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>Como ha ido una tanda.</summary>
/// <param name="Aciertos">Caracteres bien copiados.</param>
/// <param name="Total">Caracteres que habia.</param>
/// <param name="PpmSiguiente">Velocidad de la tanda siguiente.</param>
/// <param name="Sube">La velocidad ha subido.</param>
public sealed record CorreccionDeTanda(int Aciertos, int Total, int PpmSiguiente, bool Sube)
{
    /// <summary>Parte acertada, de 0 a 1.</summary>
    public double Acierto => Total == 0 ? 0 : (double)Aciertos / Total;
}

/// <summary>
/// Genera texto para practicar la recepcion de telegrafia.
/// </summary>
/// <remarks>
/// <para>
/// El entrenador no manipula ni emite: <b>genera texto</b> y corrige lo que copia el alumno.
/// Manipularlo es cosa del manipulador, y sacarlo por el altavoz del ordenador no toca la
/// radio para nada. Practicar no puede poner el equipo en antena por accidente, asi que lo mas
/// sano es que el entrenador ni siquiera sepa que existe.
/// </para>
/// <para>
/// <b>Por que Koch y no el orden alfabetico.</b> Aprender de la A a la Z es el peor orden
/// posible: las letras faciles y las dificiles se mezclan sin criterio. El orden de Koch
/// —<c>K M R S U A P T L O…</c>— empieza por dos sonidos muy distintos entre si y va metiendo
/// el resto de uno en uno, siempre a velocidad final. Es el metodo que usa el original en su
/// entrenador y es el que funciona.
/// </para>
/// </remarks>
public sealed class EntrenadorDeTelegrafia
{
    /// <summary>Orden en que Koch manda aprender los caracteres.</summary>
    public const string OrdenDeKoch = "KMRSUAPTLOWI.NJEF0Y,VG5/Q9ZH38B?427C1D6X";

    private const string Letras = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Numeros = "0123456789";

    private static readonly string[] AbreviaturasDelOficio =
    [
        "CQ", "DE", "QTH", "QSL", "QRZ", "QRM", "QRN", "QSB", "QRP", "QRO",
        "RST", "TU", "73", "88", "OM", "YL", "AGN", "PSE", "TNX", "ES",
        "BK", "K", "KN", "SK", "AR", "UR", "HR", "WX", "RIG", "ANT", "PWR",
        "NR", "GM", "GA", "GE", "DX", "RPT", "FB", "NW", "CFM",
    ];

    private static readonly string[] PrefijosDeEjemplo =
    [
        "EA", "EA8", "EB", "EC", "CT", "CT3", "F", "G", "GM", "DL", "PA", "ON", "I", "IT9",
        "SM", "LA", "OH", "OZ", "SP", "OK", "HA", "YO", "LZ", "S5", "9A", "UR", "R", "UA9",
        "K", "W", "N", "VE", "PY", "LU", "CE", "JA", "VK", "ZL", "ZS", "CN", "4X", "5B",
    ];

    private readonly Random _azar;

    /// <summary>Crea un entrenador.</summary>
    /// <param name="opciones">Como se quiere practicar.</param>
    public EntrenadorDeTelegrafia(OpcionesDeEntrenamiento? opciones = null)
    {
        Opciones = opciones ?? new OpcionesDeEntrenamiento();
        _azar = Opciones.Semilla is { } semilla ? new Random(semilla) : new Random();
        Ppm = Math.Clamp(Opciones.PpmInicial, Manipulador.PpmMinimas, Manipulador.PpmMaximas);
    }

    /// <summary>Como se esta practicando.</summary>
    public OpcionesDeEntrenamiento Opciones { get; }

    /// <summary>Velocidad de la proxima tanda.</summary>
    public int Ppm { get; private set; }

    /// <summary>Cuantos caracteres de Koch se estan practicando.</summary>
    public int CaracteresDeKoch { get; private set; } = 2;

    /// <summary>Genera la siguiente tanda.</summary>
    /// <returns>El texto a copiar y a que velocidad va.</returns>
    public TandaDeEntrenamiento Siguiente()
    {
        if (Opciones.Ejercicio == EjercicioDeTelegrafia.Koch)
        {
            CaracteresDeKoch = Math.Clamp(
                Math.Max(CaracteresDeKoch, Opciones.CaracteresDeKoch), 2, OrdenDeKoch.Length);
        }

        var texto = Opciones.Ejercicio switch
        {
            EjercicioDeTelegrafia.Letras => Grupos(Letras),
            EjercicioDeTelegrafia.Numeros => Grupos(Numeros),
            EjercicioDeTelegrafia.Indicativos => Unir(Indicativo),
            EjercicioDeTelegrafia.Abreviaturas => Unir(() => Elegir(AbreviaturasDelOficio)),
            EjercicioDeTelegrafia.Concurso => Intercambio(),
            EjercicioDeTelegrafia.Koch => Grupos(OrdenDeKoch[..CaracteresDeKoch]),
            _ => Grupos(Letras + Numeros + "/?.,"),
        };

        // En Koch las letras van siempre a velocidad final y lo que se estira son los huecos:
        // ese es el metodo. En los demas ejercicios, velocidad plana.
        var efectivas = Opciones.Ejercicio == EjercicioDeTelegrafia.Koch
            ? Math.Min(Ppm, Math.Max(Manipulador.PpmMinimas, Opciones.PpmInicial))
            : Ppm;
        var caracter = Opciones.Ejercicio == EjercicioDeTelegrafia.Koch
            ? Math.Clamp(Opciones.PpmMaximas, Manipulador.PpmMinimas, Manipulador.PpmMaximas)
            : Ppm;

        return new TandaDeEntrenamiento(texto, caracter, Math.Min(efectivas, caracter));
    }

    /// <summary>
    /// Corrige lo que ha copiado el alumno y decide la velocidad siguiente.
    /// </summary>
    /// <param name="tanda">La tanda que se mando.</param>
    /// <param name="copiado">Lo que el alumno escribio.</param>
    /// <returns>Cuanto acerto y a que velocidad va la siguiente.</returns>
    public CorreccionDeTanda Corregir(TandaDeEntrenamiento tanda, string? copiado)
    {
        ArgumentNullException.ThrowIfNull(tanda);
        var esperado = Normalizar(tanda.Texto);
        var recibido = Normalizar(copiado);

        var aciertos = 0;
        for (var i = 0; i < Math.Min(esperado.Length, recibido.Length); i++)
        {
            if (esperado[i] == recibido[i]) aciertos++;
        }

        var acierto = esperado.Length == 0 ? 0 : (double)aciertos / esperado.Length;
        var sube = acierto >= Opciones.AciertoParaSubir;
        if (sube)
        {
            if (Opciones.Ejercicio == EjercicioDeTelegrafia.Koch && CaracteresDeKoch < OrdenDeKoch.Length)
            {
                // En Koch no se sube la velocidad: se anade un caracter nuevo.
                CaracteresDeKoch++;
            }
            else
            {
                Ppm = Math.Min(Ppm + Math.Max(1, Opciones.PasoDePpm),
                    Math.Clamp(Opciones.PpmMaximas, Manipulador.PpmMinimas, Manipulador.PpmMaximas));
            }
        }

        return new CorreccionDeTanda(aciertos, esperado.Length, Ppm, sube);
    }

    private static string Normalizar(string? texto) =>
        string.IsNullOrWhiteSpace(texto)
            ? string.Empty
            : new string(texto.Where(c => !char.IsWhiteSpace(c)).Select(char.ToUpperInvariant).ToArray());

    private string Grupos(string alfabeto)
    {
        var largo = Math.Clamp(Opciones.Largo, 1, 10);
        var cuantos = Math.Clamp(Opciones.Grupos, 1, 100);
        var salida = new StringBuilder(cuantos * (largo + 1));
        for (var g = 0; g < cuantos; g++)
        {
            if (g > 0) salida.Append(' ');
            for (var i = 0; i < largo; i++) salida.Append(alfabeto[_azar.Next(alfabeto.Length)]);
        }
        return salida.ToString();
    }

    private string Unir(Func<string> generador)
    {
        var cuantos = Math.Clamp(Opciones.Grupos, 1, 100);
        var salida = new StringBuilder(cuantos * 8);
        for (var g = 0; g < cuantos; g++)
        {
            if (g > 0) salida.Append(' ');
            salida.Append(generador());
        }
        return salida.ToString();
    }

    private string Indicativo()
    {
        var prefijo = Elegir(PrefijosDeEjemplo);
        var salida = new StringBuilder(prefijo);
        if (!char.IsAsciiDigit(prefijo[^1])) salida.Append(_azar.Next(0, 10));
        var sufijo = _azar.Next(1, 4);
        for (var i = 0; i < sufijo; i++) salida.Append(Letras[_azar.Next(Letras.Length)]);
        return salida.ToString();
    }

    private string Intercambio()
    {
        var cuantos = Math.Clamp(Opciones.Grupos, 1, 100);
        var salida = new StringBuilder(cuantos * 16);
        for (var g = 0; g < cuantos; g++)
        {
            if (g > 0) salida.Append(' ');
            salida.Append(Indicativo())
                  .Append(" 599 ")
                  .Append(_azar.Next(1, 1000).ToString("000", CultureInfo.InvariantCulture));
        }
        return salida.ToString();
    }

    private string Elegir(string[] opciones) => opciones[_azar.Next(opciones.Length)];
}
