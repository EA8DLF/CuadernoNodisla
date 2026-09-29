using Nodisla.Cuaderno.Concursos.Catalogo;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Concursos.Sesion;

/// <summary>
/// Sabe al instante si una estacion ya esta trabajada.
/// </summary>
/// <remarks>
/// <para>
/// <b>Esto es lo que mas se mira de todo el programa.</b> El aviso de duplicado se recalcula
/// con cada tecla que pulsa el operador mientras escribe el indicativo, en mitad de un
/// pileup, con la estacion llamando encima. Si tarda, el operador deja de fiarse y mira la
/// lista a mano, que es justo lo que se queria evitar.
/// </para>
/// <para>
/// Por eso el indice no guarda una clave compuesta en texto —construirla obligaria a crear
/// una cadena nueva en cada pulsacion— sino un diccionario por indicativo que no distingue
/// mayusculas, de modo que se puede preguntar con lo que hay escrito en la casilla tal cual,
/// sin pasarlo a mayusculas ni normalizarlo. Dentro de cada indicativo hay una lista de las
/// bandas y modos en que se trabajo; en un concurso de verdad esa lista tiene seis entradas
/// como mucho, asi que recorrerla es mas barato que cualquier cosa mas lista.
/// </para>
/// </remarks>
public sealed class IndiceDeDuplicados
{
    private readonly record struct Contacto(Banda Banda, ModoCabrillo Modo);

    private readonly Dictionary<string, List<Contacto>> _porIndicativo =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly List<string> _ordenados = [];
    private bool _ordenadosAlDia = true;

    /// <summary>Hasta donde llega la memoria del indice: por banda, por banda y modo, o total.</summary>
    public AmbitoDeDuplicado Ambito { get; }

    /// <summary>Cuantas estaciones distintas se llevan trabajadas.</summary>
    public int Estaciones => _porIndicativo.Count;

    /// <summary>Crea un indice con el ambito que dicten las bases.</summary>
    /// <param name="ambito">Cuando deja de ser duplicado repetir a una estacion.</param>
    public IndiceDeDuplicados(AmbitoDeDuplicado ambito) => Ambito = ambito;

    /// <summary>Apunta un contacto ya hecho.</summary>
    /// <param name="indicativo">Indicativo del corresponsal.</param>
    /// <param name="banda">Banda del contacto.</param>
    /// <param name="modo">Modo del contacto.</param>
    public void Anadir(string indicativo, Banda banda, Modo modo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indicativo);
        var clave = Indicativo.Normalizar(indicativo);
        if (!_porIndicativo.TryGetValue(clave, out var lista))
        {
            lista = new List<Contacto>(2);
            _porIndicativo[clave] = lista;
            _ordenados.Add(clave);
            _ordenadosAlDia = false;
        }
        lista.Add(new Contacto(banda, ClaseDeModo.De(modo)));
    }

    /// <summary>Dice si repetir esta estacion seria duplicado.</summary>
    /// <param name="indicativo">
    /// Lo que hay escrito en la casilla, tal cual. Puede venir a medias y en minusculas: se
    /// compara sin distinguir mayusculas y sin crear cadenas nuevas.
    /// </param>
    /// <param name="banda">Banda en la que se esta trabajando.</param>
    /// <param name="modo">Modo en el que se esta trabajando.</param>
    /// <returns>Si la estacion es nueva, duplicada o conocida de otra banda.</returns>
    public EstadoDeDuplicado Comprobar(string? indicativo, Banda banda, Modo modo)
    {
        if (string.IsNullOrWhiteSpace(indicativo)) return EstadoDeDuplicado.Nuevo;
        if (!_porIndicativo.TryGetValue(indicativo, out var lista)) return EstadoDeDuplicado.Nuevo;
        if (Ambito == AmbitoDeDuplicado.Unico) return EstadoDeDuplicado.Duplicado;

        var clase = ClaseDeModo.De(modo);
        foreach (var contacto in lista)
        {
            if (contacto.Banda != banda) continue;
            if (Ambito == AmbitoDeDuplicado.Banda || contacto.Modo == clase) return EstadoDeDuplicado.Duplicado;
        }
        return EstadoDeDuplicado.TrabajadaEnOtraBanda;
    }

    /// <summary>Indicativos ya trabajados que empiezan por lo que se lleva escrito.</summary>
    /// <remarks>
    /// Sirve para el aviso mientras se teclea: con dos letras el operador ya ve si esa
    /// estacion suena de algo. Se resuelve con una busqueda binaria sobre la lista ordenada,
    /// que solo se reordena cuando ha entrado alguien nuevo.
    /// </remarks>
    /// <param name="prefijo">Lo escrito hasta ahora.</param>
    /// <param name="maximo">Cuantos devolver como mucho.</param>
    /// <returns>Los indicativos que casan, en orden alfabetico.</returns>
    public IReadOnlyList<string> PorPrefijo(string? prefijo, int maximo = 10)
    {
        if (string.IsNullOrWhiteSpace(prefijo) || maximo <= 0) return [];
        Ordenar();

        var aguja = Indicativo.Normalizar(prefijo);
        var desde = _ordenados.BinarySearch(aguja, StringComparer.Ordinal);
        if (desde < 0) desde = ~desde;

        var salida = new List<string>(Math.Min(maximo, 8));
        for (var i = desde; i < _ordenados.Count && salida.Count < maximo; i++)
        {
            if (!_ordenados[i].StartsWith(aguja, StringComparison.Ordinal)) break;
            salida.Add(_ordenados[i]);
        }
        return salida;
    }

    /// <summary>Olvida todo lo trabajado.</summary>
    public void Vaciar()
    {
        _porIndicativo.Clear();
        _ordenados.Clear();
        _ordenadosAlDia = true;
    }

    private void Ordenar()
    {
        if (_ordenadosAlDia) return;
        _ordenados.Sort(StringComparer.Ordinal);
        _ordenadosAlDia = true;
    }
}
