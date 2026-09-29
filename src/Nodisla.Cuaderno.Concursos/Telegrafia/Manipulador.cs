namespace Nodisla.Cuaderno.Concursos.Telegrafia;

/// <summary>Cada pieza de lo que se manipula.</summary>
public enum TipoDeElemento
{
    /// <summary>Punto: una unidad con tono.</summary>
    Punto,
    /// <summary>Raya: tres unidades con tono.</summary>
    Raya,
    /// <summary>Silencio entre el punto y la raya de una misma letra: una unidad.</summary>
    EntreElementos,
    /// <summary>Silencio entre letras: tres unidades, o mas si se manipula a la Farnsworth.</summary>
    EntreCaracteres,
    /// <summary>Silencio entre palabras: siete unidades, o mas a la Farnsworth.</summary>
    EntrePalabras,
}

/// <summary>Una pieza de la manipulacion, con lo que dura.</summary>
/// <param name="Tipo">Que es.</param>
/// <param name="Duracion">Cuanto dura.</param>
public readonly record struct ElementoMorse(TipoDeElemento Tipo, TimeSpan Duracion)
{
    /// <summary>Durante esta pieza el equipo esta en antena.</summary>
    public bool ConTono => Tipo is TipoDeElemento.Punto or TipoDeElemento.Raya;
}

/// <summary>
/// Convierte texto en puntos, rayas y silencios con su duracion.
/// </summary>
/// <remarks>
/// <para>
/// <b>La unidad.</b> Toda la temporizacion del Morse se mide en «puntos». La velocidad en
/// palabras por minuto se define con la palabra patron <c>PARIS</c>, que dura exactamente 50
/// unidades contando sus silencios; de ahi sale que un punto dure <c>1200 / ppm</c>
/// milisegundos. A 25 palabras por minuto, 48 ms.
/// </para>
/// <para>
/// <b>Por que hay dos velocidades.</b> Quien esta aprendiendo no necesita letras lentas: las
/// letras lentas se aprenden mal, porque el oido acaba contando puntos en vez de reconocer el
/// sonido entero. Lo que necesita es letras <b>rapidas</b> con <b>mucho hueco</b> entre ellas.
/// Eso es el metodo Farnsworth, y por eso aqui hay una velocidad de caracter y otra efectiva.
/// El reparto del hueco sobrante —tres partes entre letras y siete entre palabras, de
/// diecinueve— es el de la formula clasica de la ARRL, no una invencion.
/// </para>
/// <para>
/// Esta clase <b>no emite</b>. Devuelve una lista de piezas con su duracion, y quien quiera las
/// convierte en audio o en ordenes al manipulador del equipo. Asi se puede probar la
/// temporizacion entera sin tocar la radio ni la tarjeta de sonido.
/// </para>
/// </remarks>
public sealed class Manipulador
{
    /// <summary>Velocidad minima que admite.</summary>
    public const int PpmMinimas = 5;

    /// <summary>Velocidad maxima que admite.</summary>
    public const int PpmMaximas = 60;

    private readonly TimeSpan _punto;
    private readonly TimeSpan _entreCaracteres;
    private readonly TimeSpan _entrePalabras;

    /// <summary>Crea un manipulador.</summary>
    /// <param name="ppm">Velocidad a la que se dicen las letras, en palabras por minuto.</param>
    /// <param name="ppmEfectivas">
    /// Velocidad aparente del conjunto. Si es menor que <paramref name="ppm"/>, se alargan los
    /// silencios entre letras y entre palabras sin tocar las letras: eso es Farnsworth. Si no
    /// se pone, o es mayor o igual, la manipulacion es la corriente.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">La velocidad esta fuera de lo admitido.</exception>
    public Manipulador(int ppm = 25, int? ppmEfectivas = null)
    {
        if (ppm is < PpmMinimas or > PpmMaximas)
            throw new ArgumentOutOfRangeException(nameof(ppm), ppm, $"La velocidad va de {PpmMinimas} a {PpmMaximas} ppm.");

        Ppm = ppm;
        PpmEfectivas = ppmEfectivas is { } e && e >= PpmMinimas && e < ppm ? e : ppm;
        _punto = TimeSpan.FromMilliseconds(1200.0 / ppm);

        if (PpmEfectivas == ppm)
        {
            _entreCaracteres = _punto * 3;
            _entrePalabras = _punto * 7;
        }
        else
        {
            // Formula de Farnsworth: el retardo que hay que repartir para que una palabra
            // patron dure lo que dura a la velocidad efectiva, sin tocar las letras.
            var retardo = (60.0 * ppm - 37.2 * PpmEfectivas) / (ppm * (double)PpmEfectivas);
            _entreCaracteres = TimeSpan.FromSeconds(retardo * 3.0 / 19.0);
            _entrePalabras = TimeSpan.FromSeconds(retardo * 7.0 / 19.0);
        }
    }

    /// <summary>Velocidad de las letras, en palabras por minuto.</summary>
    public int Ppm { get; }

    /// <summary>Velocidad aparente del conjunto, en palabras por minuto.</summary>
    public int PpmEfectivas { get; }

    /// <summary>Se esta manipulando a la Farnsworth: letras rapidas con huecos largos.</summary>
    public bool EsFarnsworth => PpmEfectivas < Ppm;

    /// <summary>Lo que dura un punto.</summary>
    public TimeSpan Punto => _punto;

    /// <summary>Lo que dura el silencio entre letras.</summary>
    public TimeSpan EntreCaracteres => _entreCaracteres;

    /// <summary>Lo que dura el silencio entre palabras.</summary>
    public TimeSpan EntrePalabras => _entrePalabras;

    /// <summary>Convierte un texto en la lista de piezas que hay que manipular.</summary>
    /// <param name="texto">
    /// Texto a manipular. Las senales de procedimiento van entre angulos: <c>&lt;AR&gt;</c>.
    /// Lo que no se sepa manipular se descarta en silencio; ningun caracter raro debe dejar
    /// muda una llamada entera.
    /// </param>
    /// <returns>Las piezas, en orden, con su duracion.</returns>
    public IReadOnlyList<ElementoMorse> Manipular(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return [];

        var piezas = new List<ElementoMorse>(texto.Length * 6);
        var hayAlgoAntes = false;
        var pendienteDePalabra = false;

        for (var i = 0; i < texto.Length; i++)
        {
            var letra = texto[i];
            if (char.IsWhiteSpace(letra))
            {
                if (hayAlgoAntes) pendienteDePalabra = true;
                continue;
            }

            string codigo;
            if (letra == '<')
            {
                var cierre = texto.IndexOf('>', i + 1);
                if (cierre < 0) continue;
                if (!AlfabetoMorse.TryProcedimiento(texto[(i + 1)..cierre], out codigo)) { i = cierre; continue; }
                i = cierre;
            }
            else if (!AlfabetoMorse.TryCodigo(letra, out codigo))
            {
                continue;
            }

            if (hayAlgoAntes)
            {
                piezas.Add(new ElementoMorse(
                    pendienteDePalabra ? TipoDeElemento.EntrePalabras : TipoDeElemento.EntreCaracteres,
                    pendienteDePalabra ? _entrePalabras : _entreCaracteres));
            }
            pendienteDePalabra = false;

            for (var j = 0; j < codigo.Length; j++)
            {
                if (j > 0) piezas.Add(new ElementoMorse(TipoDeElemento.EntreElementos, _punto));
                piezas.Add(codigo[j] == '.'
                    ? new ElementoMorse(TipoDeElemento.Punto, _punto)
                    : new ElementoMorse(TipoDeElemento.Raya, _punto * 3));
            }
            hayAlgoAntes = true;
        }

        return piezas;
    }

    /// <summary>Lo que tardaria en manipularse un texto.</summary>
    /// <param name="texto">Texto a manipular.</param>
    /// <returns>La duracion total.</returns>
    public TimeSpan Duracion(string? texto)
    {
        var total = TimeSpan.Zero;
        foreach (var pieza in Manipular(texto)) total += pieza.Duracion;
        return total;
    }
}
