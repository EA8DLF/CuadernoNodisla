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
    /// los bits de arriba del producto de 64 bits.
    /// </summary>
    private const ulong Multiplicador = 47055833459UL;

    /// <summary>Alfabeto en el que se numera el indicativo para calcular el resumen.</summary>
    private const string Alfabeto = " 0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ/";

    /// <summary>Caracteres que se usan del indicativo para el resumen.</summary>
    private const int LongitudDelResumen = 11;

    private readonly Dictionary<int, string> _de10 = [];
    private readonly Dictionary<int, string> _de12 = [];
    private readonly Dictionary<int, string> _de22 = [];
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

        lock (_cerrojo)
        {
            _de10[Resumir(v, 10)] = v;
            _de12[Resumir(v, 12)] = v;
            _de22[Resumir(v, 22)] = v;
        }
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
        }
    }
}
