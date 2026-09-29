namespace Nodisla.Cuaderno.Dominio.Dxcc;

/// <summary>
/// Arbol de prefijos (trie) sobre el alfabeto <c>0-9A-Z</c>. Sirve para encontrar el
/// prefijo mas largo de la tabla que encaja con un indicativo sin recorrer la tabla
/// entera: la busqueda cuesta lo que mide el indicativo, no lo que mide el catalogo.
/// </summary>
/// <remarks>
/// Los nodos se guardan en vectores planos y los hijos de un nodo ocupan posiciones
/// consecutivas, ordenadas por simbolo. Asi la busqueda no reserva memoria: se puede
/// disparar con cada tecla que escribe el operador.
/// </remarks>
internal sealed class ArbolPrefijos
{
    private const int Alfabeto = 36;

    private readonly byte[] _simbolo;
    private readonly int[] _primerHijo;
    private readonly int[] _finHijos;
    private readonly ReglaDxcc[]?[] _reglas;

    private ArbolPrefijos(byte[] simbolo, int[] primerHijo, int[] finHijos, ReglaDxcc[]?[] reglas)
    {
        _simbolo = simbolo;
        _primerHijo = primerHijo;
        _finHijos = finHijos;
        _reglas = reglas;
    }

    /// <summary>Numero de nodos del arbol, util para las pruebas y para medir memoria.</summary>
    public int Nodos => _simbolo.Length;

    /// <summary>Convierte un caracter en su simbolo del alfabeto, o -1 si no pertenece.</summary>
    private static int Simbolo(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'A' and <= 'Z' => c - 'A' + 10,
        >= 'a' and <= 'z' => c - 'a' + 10,
        _ => -1,
    };

    /// <summary>Construye el arbol a partir de las reglas agrupadas por clave.</summary>
    public static ArbolPrefijos Construir(IEnumerable<KeyValuePair<string, List<ReglaDxcc>>> entradas)
    {
        var claves = new List<string>();
        var porClave = new Dictionary<string, List<ReglaDxcc>>(StringComparer.Ordinal);
        foreach (var (clave, reglas) in entradas)
        {
            if (clave.Length == 0 || reglas.Count == 0) continue;
            porClave[clave] = reglas;
            claves.Add(clave);
        }
        claves.Sort(StringComparer.Ordinal);

        var simbolo = new List<byte> { 0 };
        var primerHijo = new List<int> { 0 };
        var finHijos = new List<int> { 0 };
        var reglasNodo = new List<ReglaDxcc[]?> { null };

        // Recorrido por niveles: cada nodo pendiente abarca un tramo contiguo de claves.
        var cola = new Queue<(int Nodo, int Inicio, int Fin, int Profundidad)>();
        cola.Enqueue((0, 0, claves.Count, 0));

        while (cola.Count > 0)
        {
            var (nodo, inicio, fin, profundidad) = cola.Dequeue();
            var i = inicio;

            // Si el tramo empieza por una clave de esta misma longitud, es la del nodo.
            if (profundidad > 0 && i < fin && claves[i].Length == profundidad)
            {
                var lista = porClave[claves[i]];
                lista.Sort(static (a, b) => a.Preferencia.CompareTo(b.Preferencia));
                reglasNodo[nodo] = lista.ToArray();
                i++;
            }

            primerHijo[nodo] = simbolo.Count;
            var pendientes = new List<(int Nodo, int Inicio, int Fin, int Profundidad)>();
            while (i < fin)
            {
                var c = claves[i][profundidad];
                var j = i;
                while (j < fin && claves[j][profundidad] == c) j++;

                var hijo = simbolo.Count;
                simbolo.Add((byte)Simbolo(c));
                primerHijo.Add(0);
                finHijos.Add(0);
                reglasNodo.Add(null);
                pendientes.Add((hijo, i, j, profundidad + 1));
                i = j;
            }
            finHijos[nodo] = simbolo.Count;
            foreach (var p in pendientes) cola.Enqueue(p);
        }

        return new ArbolPrefijos(
            simbolo.ToArray(), primerHijo.ToArray(), finHijos.ToArray(), reglasNodo.ToArray());
    }

    /// <summary>
    /// Anota en <paramref name="nodos"/> los nodos con reglas que atraviesa la clave, del
    /// prefijo mas corto al mas largo, y devuelve cuantos hay.
    /// </summary>
    public int Camino(ReadOnlySpan<char> clave, Span<int> nodos)
    {
        var nodo = 0;
        var n = 0;
        foreach (var c in clave)
        {
            var s = Simbolo(c);
            if (s < 0) break;
            var hijo = BuscarHijo(nodo, s);
            if (hijo < 0) break;
            nodo = hijo;
            if (_reglas[nodo] is not null)
            {
                if (n == nodos.Length) break;
                nodos[n++] = nodo;
            }
        }
        return n;
    }

    /// <summary>Reglas guardadas en un nodo, ya ordenadas por preferencia.</summary>
    public ReglaDxcc[] ReglasDe(int nodo) => _reglas[nodo] ?? [];

    /// <summary>Busqueda binaria del hijo con el simbolo dado; -1 si no existe.</summary>
    private int BuscarHijo(int nodo, int simbolo)
    {
        var lo = _primerHijo[nodo];
        var hi = _finHijos[nodo] - 1;
        while (lo <= hi)
        {
            var medio = (int)(((uint)lo + (uint)hi) >> 1);
            var s = _simbolo[medio];
            if (s == simbolo) return medio;
            if (s < simbolo) lo = medio + 1;
            else hi = medio - 1;
        }
        return -1;
    }
}
