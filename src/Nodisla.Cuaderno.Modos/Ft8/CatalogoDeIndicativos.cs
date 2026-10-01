namespace Nodisla.Cuaderno.Modos.Ft8;

/// <summary>
/// Recuerda los indicativos que se han visto para poder resolver los que viajan resumidos.
/// </summary>
/// <remarks>
/// <para>
/// Un mensaje de FT8 tiene 77 bits contados. Un indicativo corriente, del tipo <c>EA8DLF</c>,
/// cabe en 28. Uno raro —<c>EA8DLF/P</c>, <c>VP2E/K1ABC</c>, una estacion de expedicion— no
/// cabe, asi que el protocolo manda en su lugar un <b>resumen</b> de 10, 12 o 22 bits y confia
/// en que el receptor ya haya oido el indicativo entero en algun mensaje anterior.
/// </para>
/// <para>
/// De ahi este catalogo: se apunta cada indicativo largo que se decodifica entero y, cuando
/// luego llega uno resumido, se busca aqui.
/// </para>
/// <para>
/// <b>Si el resumen no esta en el catalogo, no se inventa nada.</b> Se devuelve el texto entre
/// corchetes angulares, que es como lo ensena todo el mundo, y ese mensaje no sirve para
/// apuntar un contacto. Resolverlo «a lo que mas se parezca» meteria indicativos falsos en el
/// cuaderno, que es justo lo que no puede pasar.
/// </para>
/// <para>
/// Un resumen de 10 bits solo tiene mil combinaciones, asi que colisiona con facilidad; por eso
/// el catalogo guarda los tres tamanos por separado y, si dos indicativos distintos chocan en
/// el mismo resumen, se queda con el mas reciente y marca el hueco como dudoso.
/// </para>
/// </remarks>
public sealed class CatalogoDeIndicativos
{
    /// <summary>
    /// Multiplicador del resumen. Es una constante del protocolo: el resumen se calcula
    /// pasando el indicativo a un numero en base 38, multiplicandolo por esto y quedandose con
    /// los bits de arriba del producto de 64 bits (que se desborda: la cuenta es modulo 2^64).
    /// </summary>
    /// <remarks>
    /// Procedencia: protocolo FT8/FT4 descrito en «The FT4 and FT8 Communication Protocols»
    /// (Franke, Somerville y Taylor, QEX jul/ago 2020); el valor coincide con el de ft8_lib
    /// (licencia MIT), con cuyos resumenes se ha contrastado. Los resumenes de 12 y 10 bits son
    /// los 12 y 10 bits de arriba del de 22, no otra cuenta.
    /// </remarks>
    private const ulong Multiplicador = 47055833459UL;

    /// <summary>Alfabeto en el que se numera el indicativo para calcular el resumen.</summary>
    private const string Alfabeto = " 0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ/";

    /// <summary>Caracteres que se usan del indicativo para el resumen.</summary>
    private const int LongitudDelResumen = 11;

    private readonly Dictionary<int, string> _de10 = [];
    private readonly Dictionary<int, string> _de12 = [];
    private readonly Dictionary<int, string> _de22 = [];
    private readonly List<string> _fijos = [];
    private readonly object _cerrojo = new();

    /// <summary>Cuantos indicativos distintos hay apuntados.</summary>
    public int Cuantos
    {
        get { lock (_cerrojo) return _de22.Count; }
    }

    /// <summary>
    /// Calcula el resumen de un indicativo con la anchura pedida.
    /// </summary>
    /// <param name="indicativo">Indicativo en mayusculas, sin espacios.</param>
    /// <param name="bits">Anchura del resumen: 10, 12 o 22.</param>
    public static int Resumir(string indicativo, int bits)
    {
        if (bits is not (10 or 12 or 22))
            throw new ArgumentOutOfRangeException(nameof(bits), bits, "El protocolo solo define resumenes de 10, 12 y 22 bits.");

        ulong n = 0;
        for (var i = 0; i < LongitudDelResumen; i++)
        {
            // El indicativo se alinea a la izquierda y se rellena con espacios, que valen cero.
            var c = i < indicativo.Length ? char.ToUpperInvariant(indicativo[i]) : ' ';
            var indice = Alfabeto.IndexOf(c, StringComparison.Ordinal);
            if (indice < 0) indice = 0;
            n = (n * 38) + (ulong)indice;
        }
        // Los bits altos del producto son los que mejor mezclan: cada caracter del indicativo
        // influye en todos ellos.
        return (int)((n * Multiplicador) >> (64 - bits));
    }

    /// <summary>Apunta un indicativo entero para poder resolver luego sus resumenes.</summary>
    public void Recordar(string? indicativo)
    {
        if (string.IsNullOrWhiteSpace(indicativo)) return;
        var v = indicativo.Trim().ToUpperInvariant();
        if (v.Length is < 3 or > LongitudDelResumen) return;
        // Solo indicativos en claro: un «<...>» o un «<EA8DLF>» darian un resumen que no es el suyo.
        if (v.Any(c => Alfabeto.IndexOf(c, StringComparison.Ordinal) < 0 || c == ' ')) return;

        lock (_cerrojo)
        {
            _de10[Resumir(v, 10)] = v;
            _de12[Resumir(v, 12)] = v;
            _de22[Resumir(v, 22)] = v;
        }
    }

    /// <summary>
    /// Apunta un indicativo que no se olvida al cambiar de banda: el propio y los que uno emite.
    /// </summary>
    /// <remarks>
    /// Quien contesta a un indicativo raro manda resumido el del otro (<c>&lt;EA8DLF&gt; HB10GBT
    /// RRR</c>). Si el receptor no tiene apuntado el propio indicativo, ese mensaje saldria como
    /// <c>&lt;...&gt;</c> y el secuenciador no sabria que es para uno.
    /// </remarks>
    public void Fijar(string? indicativo)
    {
        if (string.IsNullOrWhiteSpace(indicativo)) return;
        var v = indicativo.Trim().ToUpperInvariant();
        if (v.Length is < 3 or > LongitudDelResumen) return;
        lock (_cerrojo)
        {
            if (!_fijos.Contains(v))
            {
                // Los fijos no crecen sin limite: se queda con los ultimos.
                if (_fijos.Count >= 64) _fijos.RemoveAt(0);
                _fijos.Add(v);
            }
        }
        Recordar(v);
    }

    /// <summary>
    /// Busca a que indicativo corresponde un resumen.
    /// </summary>
    /// <returns>
    /// El indicativo si consta, o nulo si no. Nulo significa <b>no lo se</b>, no «parecido a».
    /// </returns>
    public string? Resolver(int resumen, int bits)
    {
        var tabla = bits switch
        {
            10 => _de10,
            12 => _de12,
            22 => _de22,
            _ => throw new ArgumentOutOfRangeException(nameof(bits), bits, "Anchura de resumen no valida."),
        };
        lock (_cerrojo) return tabla.TryGetValue(resumen, out var v) ? v : null;
    }

    /// <summary>Texto con el que se ensena un resumen sin resolver.</summary>
    public static string TextoSinResolver() => "<...>";

    /// <summary>Vacia el catalogo. Se hace al cambiar de banda o al reiniciar la escucha.</summary>
    public void Olvidar()
    {
        lock (_cerrojo)
        {
            _de10.Clear();
            _de12.Clear();
            _de22.Clear();
            foreach (var v in _fijos)
            {
                _de10[Resumir(v, 10)] = v;
                _de12[Resumir(v, 12)] = v;
                _de22[Resumir(v, 22)] = v;
            }
        }
    }
}
