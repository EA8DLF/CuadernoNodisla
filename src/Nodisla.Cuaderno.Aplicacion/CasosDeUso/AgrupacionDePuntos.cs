using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Aplicacion.CasosDeUso;

/// <summary>Un monton de elementos que caen tan juntos que se pintan como una sola marca.</summary>
/// <typeparam name="T">Lo que se esta agrupando: contactos, spots o lo que sea.</typeparam>
/// <param name="Centro">Punto medio del grupo, que es donde se pinta la marca.</param>
/// <param name="Elementos">Los que han caido en la misma casilla.</param>
public sealed record Grupo<T>(Coordenada Centro, IReadOnlyList<T> Elementos)
{
    /// <summary>Cuantos elementos representa la marca.</summary>
    public int Cuantos => Elementos.Count;

    /// <summary>El grupo es un elemento suelto y se puede pintar con su etiqueta propia.</summary>
    public bool EsSuelto => Elementos.Count == 1;

    /// <summary>El primero del grupo, que es el que da nombre a la marca cuando esta sola.</summary>
    public T Primero => Elementos[0];
}

/// <summary>
/// Junta puntos que caen casi encima para no pintar veinte mil marcas de una en una.
/// </summary>
/// <remarks>
/// Un cuaderno de treinta anos tiene decenas de miles de contactos y la mitad estan en el
/// mismo puno de Europa. Pintarlos todos no solo va lento: tampoco se ve nada, porque las
/// marcas se tapan unas a otras. Se reparte el mundo en casillas y cada casilla se pinta una
/// vez, con el numero de contactos que lleva dentro.
///
/// El tamano de casilla se calcula a partir de cuantas marcas se quieren ver en pantalla, no
/// a ojo: asi el mapa va igual de fluido con mil contactos que con cincuenta mil.
/// </remarks>
public static class AgrupacionDePuntos
{
    /// <summary>Marcas que se pintan de una en una sin agrupar nada.</summary>
    public const int MarcasComodas = 1200;

    /// <summary>Casilla mas pequena que se usa: por debajo, agrupar no ahorra trabajo.</summary>
    public const double CasillaMinimaEnGrados = 0.25;

    /// <summary>Casilla mas grande: por encima, el mapa dejaria de decir donde estan las cosas.</summary>
    public const double CasillaMaximaEnGrados = 20.0;

    /// <summary>
    /// Reparte los elementos en casillas y devuelve una marca por casilla ocupada.
    /// </summary>
    /// <typeparam name="T">Lo que se agrupa.</typeparam>
    /// <param name="elementos">Elementos a repartir.</param>
    /// <param name="donde">Como se saca la coordenada de cada elemento.</param>
    /// <param name="gradosPorCasilla">Lado de la casilla, en grados.</param>
    /// <returns>Un grupo por casilla ocupada, ordenados de mas a menos poblados.</returns>
    public static IReadOnlyList<Grupo<T>> Agrupar<T>(
        IEnumerable<T> elementos,
        Func<T, Coordenada> donde,
        double gradosPorCasilla)
    {
        ArgumentNullException.ThrowIfNull(elementos);
        ArgumentNullException.ThrowIfNull(donde);

        var lado = Math.Clamp(gradosPorCasilla, CasillaMinimaEnGrados, CasillaMaximaEnGrados);
        var casillas = new Dictionary<(int Fila, int Columna), List<T>>();

        foreach (var elemento in elementos)
        {
            var punto = donde(elemento);
            if (double.IsNaN(punto.Latitud) || double.IsNaN(punto.Longitud)) continue;

            var clave = (
                (int)Math.Floor(punto.Latitud / lado),
                (int)Math.Floor(punto.Longitud / lado));

            if (!casillas.TryGetValue(clave, out var lista))
            {
                lista = [];
                casillas[clave] = lista;
            }

            lista.Add(elemento);
        }

        var salida = new List<Grupo<T>>(casillas.Count);
        foreach (var (_, lista) in casillas)
        {
            salida.Add(new Grupo<T>(CentroDe(lista, donde), lista));
        }

        // De mas a menos poblados: asi el que dibuja puede quedarse con los primeros si aun
        // son demasiados, y los que se caen son siempre los menos representativos.
        salida.Sort((a, b) => b.Cuantos.CompareTo(a.Cuantos));
        return salida;
    }

    /// <summary>
    /// Agrupa solo lo justo para no pasar del numero de marcas pedido.
    /// </summary>
    /// <typeparam name="T">Lo que se agrupa.</typeparam>
    /// <param name="elementos">Elementos a repartir.</param>
    /// <param name="donde">Como se saca la coordenada de cada elemento.</param>
    /// <param name="marcasDeseadas">Cuantas marcas se quieren ver como mucho.</param>
    /// <returns>Los grupos ya ajustados; si los elementos caben, cada uno va suelto.</returns>
    public static IReadOnlyList<Grupo<T>> AgruparHasta<T>(
        IEnumerable<T> elementos,
        Func<T, Coordenada> donde,
        int marcasDeseadas = MarcasComodas)
    {
        ArgumentNullException.ThrowIfNull(elementos);
        ArgumentNullException.ThrowIfNull(donde);

        var lista = elementos as IReadOnlyList<T> ?? [.. elementos];
        var tope = Math.Max(1, marcasDeseadas);

        if (lista.Count <= tope)
        {
            var sueltos = new List<Grupo<T>>(lista.Count);
            foreach (var elemento in lista)
            {
                sueltos.Add(new Grupo<T>(donde(elemento), [elemento]));
            }

            return sueltos;
        }

        // Se prueba con casillas cada vez mas grandes hasta que el numero de marcas baja del
        // tope. Doblando el lado cada vez, ocho vueltas van de un cuarto de grado a media
        // vuelta al mundo: siempre termina, y casi siempre a la segunda o la tercera.
        var lado = LadoDePartida(lista.Count, tope);
        IReadOnlyList<Grupo<T>> grupos = Agrupar(lista, donde, lado);

        while (grupos.Count > tope && lado < CasillaMaximaEnGrados)
        {
            lado = Math.Min(CasillaMaximaEnGrados, lado * 2);
            grupos = Agrupar(lista, donde, lado);
        }

        return grupos.Count > tope ? [.. grupos.Take(tope)] : grupos;
    }

    /// <summary>
    /// Primer lado que se prueba, suponiendo que los puntos se repartieran por igual.
    /// </summary>
    private static double LadoDePartida(int cuantos, int tope)
    {
        // Con el mundo repartido en casillas cuadradas, para tener «tope» casillas ocupadas
        // hace falta un lado de 360/√tope. Los puntos no se reparten por igual —Europa pesa
        // muchisimo mas—, asi que esto es solo el punto de partida del tanteo.
        var estimado = 360.0 / Math.Sqrt(Math.Max(1, tope));
        return Math.Clamp(estimado * (cuantos > tope * 10 ? 1.0 : 0.5), CasillaMinimaEnGrados, CasillaMaximaEnGrados);
    }

    /// <summary>Punto medio de un grupo, que es donde acaba pintandose la marca.</summary>
    private static Coordenada CentroDe<T>(List<T> lista, Func<T, Coordenada> donde)
    {
        double latitud = 0;
        double longitud = 0;

        foreach (var elemento in lista)
        {
            var punto = donde(elemento);
            latitud += punto.Latitud;
            longitud += punto.Longitud;
        }

        return new Coordenada(latitud / lista.Count, longitud / lista.Count);
    }
}
