namespace Nodisla.Cuaderno.Modos.Q65;

/// <summary>Una ecuacion de paridad sobre GF(64): la suma de las variables por sus pesos es cero.</summary>
/// <param name="Variables">Indices de las variables de la palabra (0..64) que entran.</param>
/// <param name="Pesos">Peso de cada variable, un elemento del cuerpo distinto de cero.</param>
public sealed record EcuacionQra(int[] Variables, int[] Pesos);

/// <summary>
/// El codigo QRA(65,15) de Q65: repeticion y acumulacion sobre GF(64).
/// </summary>
/// <remarks>
/// <para>
/// Un codigo de repeticion y acumulacion es lo mas sencillo que hay dentro de la familia de los
/// LDPC: cada simbolo de informacion se repite unas cuantas veces, la lista resultante se
/// baraja, cada entrada se multiplica por un peso del cuerpo y se va acumulando. Cada valor
/// intermedio del acumulador es un simbolo de paridad. Codificar es, por tanto, un recorrido
/// de 50 pasos; lo que hace potente al codigo es que ese grafo tan simple, visto desde el
/// decodificador, es un LDPC irregular sobre un cuerpo grande.
/// </para>
/// <para>
/// El diseno tiene una propiedad extra: los pesos de cada simbolo suman cero, asi que un paso
/// mas del acumulador lo devuelve a cero. Eso da una ecuacion de paridad adicional, la 51, que
/// no produce simbolo pero si ayuda a decodificar. Aqui se usan las 51.
/// </para>
/// </remarks>
public sealed class CodigoQra
{
    /// <summary>Simbolos de informacion.</summary>
    public const int SimbolosDeInformacion = TablasDeQ65.SimbolosDeInformacion;

    /// <summary>Simbolos de la palabra.</summary>
    public const int Longitud = TablasDeQ65.LongitudDeLaPalabra;

    /// <summary>Simbolos de paridad.</summary>
    public const int SimbolosDeParidad = Longitud - SimbolosDeInformacion;

    private readonly int[] _entradas;
    private readonly int[] _pesos;

    /// <summary>Construye el codigo a partir de las tablas.</summary>
    public CodigoQra(TablasDeQ65 tablas)
    {
        ArgumentNullException.ThrowIfNull(tablas);
        Tablas = tablas;
        _entradas = tablas.EntradasDelAcumulador;
        _pesos = tablas.PesosDelAcumulador.Select(CampoDeGalois64.Potencia).ToArray();

        var ecuaciones = new List<EcuacionQra>();
        for (var k = 0; k <= SimbolosDeParidad; k++)
        {
            var variables = new List<int>();
            var pesos = new List<int>();
            if (k < SimbolosDeParidad) { variables.Add(SimbolosDeInformacion + k); pesos.Add(1); }
            if (k > 0) { variables.Add(SimbolosDeInformacion + k - 1); pesos.Add(1); }
            variables.Add(_entradas[k]);
            pesos.Add(_pesos[k]);
            ecuaciones.Add(new EcuacionQra([.. variables], [.. pesos]));
        }
        Ecuaciones = ecuaciones;
    }

    /// <summary>Las tablas con las que se construyo.</summary>
    public TablasDeQ65 Tablas { get; }

    /// <summary>Las 51 ecuaciones de paridad.</summary>
    public IReadOnlyList<EcuacionQra> Ecuaciones { get; }

    /// <summary>Codifica 15 simbolos de informacion en la palabra de 65.</summary>
    public int[] Codificar(ReadOnlySpan<int> informacion)
    {
        if (informacion.Length != SimbolosDeInformacion)
            throw new ArgumentException($"Hacen falta {SimbolosDeInformacion} simbolos.", nameof(informacion));
        var palabra = new int[Longitud];
        informacion.CopyTo(palabra);
        var acumulado = 0;
        for (var k = 0; k < SimbolosDeParidad; k++)
        {
            acumulado ^= CampoDeGalois64.Multiplicar(informacion[_entradas[k]], _pesos[k]);
            palabra[SimbolosDeInformacion + k] = acumulado;
        }
        return palabra;
    }

    /// <summary>Dice si una palabra cumple las 51 ecuaciones.</summary>
    public bool CumpleLasEcuaciones(ReadOnlySpan<int> palabra)
    {
        if (palabra.Length != Longitud) return false;
        foreach (var e in Ecuaciones)
        {
            var suma = 0;
            for (var i = 0; i < e.Variables.Length; i++)
                suma ^= CampoDeGalois64.Multiplicar(palabra[e.Variables[i]], e.Pesos[i]);
            if (suma != 0) return false;
        }
        return true;
    }
}
