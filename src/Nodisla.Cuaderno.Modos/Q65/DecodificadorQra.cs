namespace Nodisla.Cuaderno.Modos.Q65;

/// <summary>
/// Decodificador del codigo QRA por propagacion de creencias sobre GF(64).
/// </summary>
/// <remarks>
/// <para>
/// Es el mismo algoritmo de pasar mensajes que usa el LDPC de FT8, pero con simbolos de seis
/// bits en vez de bits: cada mensaje entre un simbolo y una ecuacion es una distribucion de
/// probabilidad sobre los 64 valores posibles, no un solo numero. Lo que cambia de verdad es
/// la cuenta en las ecuaciones: la distribucion de una suma de variables independientes es la
/// convolucion de sus distribuciones, y como en este cuerpo sumar es hacer o-exclusivo, esa
/// convolucion se hace en dos transformadas de Walsh-Hadamard de 64 puntos, que es el truco
/// que hace viable decodificar sobre un cuerpo grande en tiempo real.
/// </para>
/// <para>
/// Los pesos de las ecuaciones se tratan permutando las distribuciones: si una variable entra
/// multiplicada por <i>w</i>, la distribucion de <i>w·x</i> es la de <i>x</i> reordenada. Y los
/// dos simbolos del sello, que no se emiten, entran con distribucion plana: el codigo los
/// reconstruye por las ecuaciones.
/// </para>
/// <para>
/// Se trabaja en dobles y en probabilidades, no en logaritmos: con grados de tres y cinco los
/// productos no se desbordan y las transformadas salen mas limpias.
/// </para>
/// </remarks>
public sealed class DecodificadorQra
{
    private const int M = CampoDeGalois64.Orden;
    private const int N = CodigoQra.Longitud;

    private readonly CodigoQra _codigo;

    // Aristas del grafo, por ecuacion: variable, peso, inverso del peso y posicion de la arista
    // en la lista de la variable.
    private readonly int[][] _variablesDeLaEcuacion;
    private readonly int[][] _pesosDeLaEcuacion;
    private readonly int[][] _inversosDeLaEcuacion;
    private readonly int[][] _ecuacionesDeLaVariable;
    private readonly int[][] _huecoEnLaEcuacion;

    // Mensajes: uno por arista y sentido, de 64 valores cada uno, guardados por ecuacion y hueco.
    private readonly double[][][] _haciaLaEcuacion;
    private readonly double[][][] _haciaLaVariable;
    private readonly double[] _posterior = new double[N * M];
    private readonly int[] _decision = new int[N];

    /// <summary>Construye el decodificador para un codigo.</summary>
    public DecodificadorQra(CodigoQra codigo)
    {
        ArgumentNullException.ThrowIfNull(codigo);
        _codigo = codigo;
        var ecuaciones = codigo.Ecuaciones;
        _variablesDeLaEcuacion = ecuaciones.Select(e => e.Variables.ToArray()).ToArray();
        _pesosDeLaEcuacion = ecuaciones.Select(e => e.Pesos.ToArray()).ToArray();
        _inversosDeLaEcuacion = ecuaciones.Select(e => e.Pesos.Select(CampoDeGalois64.Inverso).ToArray()).ToArray();

        var porVariable = Enumerable.Range(0, N).Select(_ => new List<(int Ecuacion, int Hueco)>()).ToArray();
        for (var c = 0; c < ecuaciones.Count; c++)
            for (var h = 0; h < ecuaciones[c].Variables.Length; h++)
                porVariable[ecuaciones[c].Variables[h]].Add((c, h));
        _ecuacionesDeLaVariable = porVariable.Select(l => l.Select(x => x.Ecuacion).ToArray()).ToArray();
        _huecoEnLaEcuacion = porVariable.Select(l => l.Select(x => x.Hueco).ToArray()).ToArray();

        _haciaLaEcuacion = ecuaciones.Select(e => e.Variables.Select(_ => new double[M]).ToArray()).ToArray();
        _haciaLaVariable = ecuaciones.Select(e => e.Variables.Select(_ => new double[M]).ToArray()).ToArray();
    }

    /// <summary>Vueltas maximas de propagacion antes de darse por vencido.</summary>
    public int VueltasMaximas { get; set; } = 60;

    /// <summary>
    /// Intenta decodificar a partir de las probabilidades de cada simbolo.
    /// </summary>
    /// <param name="intrinsecas">
    /// 65 filas de 64 probabilidades, una fila por simbolo de la palabra. Los simbolos no
    /// emitidos llevan la fila plana. No hace falta que esten normalizadas.
    /// </param>
    /// <param name="palabra">Los 65 simbolos decididos, si se encontro una palabra valida.</param>
    /// <param name="vueltas">Vueltas que hicieron falta.</param>
    /// <returns>Cierto si las decisiones cumplen las 51 ecuaciones.</returns>
    public bool TryDecodificar(ReadOnlySpan<double> intrinsecas, Span<int> palabra, out int vueltas)
    {
        if (intrinsecas.Length != N * M) throw new ArgumentException($"Hacen falta {N}x{M} probabilidades.", nameof(intrinsecas));
        if (palabra.Length != N) throw new ArgumentException($"Hacen falta {N} simbolos.", nameof(palabra));

        foreach (var ecuacion in _haciaLaVariable)
            foreach (var mensaje in ecuacion) Array.Fill(mensaje, 1.0 / M);

        Span<double> producto = stackalloc double[M];
        Span<double> transformadas = stackalloc double[3 * M];
        Span<double> suma = stackalloc double[M];

        for (vueltas = 1; vueltas <= VueltasMaximas; vueltas++)
        {
            // De las variables a las ecuaciones: cada variable manda lo que cree, sin contar lo
            // que le dijo esa misma ecuacion.
            for (var v = 0; v < N; v++)
            {
                var ecuaciones = _ecuacionesDeLaVariable[v];
                var huecos = _huecoEnLaEcuacion[v];
                var intrinseca = intrinsecas.Slice(v * M, M);
                var posterior = _posterior.AsSpan(v * M, M);

                intrinseca.CopyTo(posterior);
                for (var i = 0; i < ecuaciones.Length; i++)
                {
                    var entrante = _haciaLaVariable[ecuaciones[i]][huecos[i]];
                    for (var x = 0; x < M; x++) posterior[x] *= entrante[x];
                }
                Normalizar(posterior);
                _decision[v] = Argmax(posterior);

                for (var i = 0; i < ecuaciones.Length; i++)
                {
                    intrinseca.CopyTo(producto);
                    for (var j = 0; j < ecuaciones.Length; j++)
                    {
                        if (j == i) continue;
                        var entrante = _haciaLaVariable[ecuaciones[j]][huecos[j]];
                        for (var x = 0; x < M; x++) producto[x] *= entrante[x];
                    }
                    Normalizar(producto);
                    producto.CopyTo(_haciaLaEcuacion[ecuaciones[i]][huecos[i]]);
                }
            }

            if (_codigo.CumpleLasEcuaciones(_decision))
            {
                _decision.CopyTo(palabra);
                return true;
            }

            // De las ecuaciones a las variables: lo que la ecuacion dice de cada variable es la
            // distribucion de la suma de las demas, convolucionada por Walsh-Hadamard.
            for (var c = 0; c < _variablesDeLaEcuacion.Length; c++)
            {
                var variables = _variablesDeLaEcuacion[c];
                var pesos = _pesosDeLaEcuacion[c];
                var inversos = _inversosDeLaEcuacion[c];
                var grado = variables.Length;

                for (var h = 0; h < grado; h++)
                {
                    // Distribucion de w*x: la de x reordenada, y a la transformada.
                    var entrante = _haciaLaEcuacion[c][h];
                    var t = transformadas.Slice(h * M, M);
                    for (var y = 0; y < M; y++) t[y] = entrante[CampoDeGalois64.Multiplicar(inversos[h], y)];
                    WalshHadamard(t);
                }

                for (var h = 0; h < grado; h++)
                {
                    suma.Fill(1.0);
                    for (var k = 0; k < grado; k++)
                    {
                        if (k == h) continue;
                        var t = transformadas.Slice(k * M, M);
                        for (var y = 0; y < M; y++) suma[y] *= t[y];
                    }
                    WalshHadamard(suma);
                    var saliente = _haciaLaVariable[c][h];
                    for (var x = 0; x < M; x++)
                    {
                        var valor = suma[CampoDeGalois64.Multiplicar(pesos[h], x)];
                        saliente[x] = valor > 0 ? valor : 0;
                    }
                    Normalizar(saliente);
                }
            }
        }

        vueltas = VueltasMaximas;
        return false;
    }

    /// <summary>Transformada de Walsh-Hadamard en el sitio, sin escalar.</summary>
    private static void WalshHadamard(Span<double> v)
    {
        for (var paso = 1; paso < M; paso <<= 1)
        {
            for (var i = 0; i < M; i += paso << 1)
            {
                for (var j = i; j < i + paso; j++)
                {
                    var a = v[j];
                    var b = v[j + paso];
                    v[j] = a + b;
                    v[j + paso] = a - b;
                }
            }
        }
    }

    private static void Normalizar(Span<double> v)
    {
        double total = 0;
        foreach (var x in v) total += x;
        if (total <= 0)
        {
            v.Fill(1.0 / M);
            return;
        }
        // Un suelo diminuto evita que una probabilidad exactamente cero anule para siempre un
        // valor que otra ecuacion podria rescatar.
        var escala = 1.0 / total;
        for (var i = 0; i < v.Length; i++)
        {
            var p = v[i] * escala;
            v[i] = p < 1e-30 ? 1e-30 : p;
        }
    }

    private static int Argmax(ReadOnlySpan<double> v)
    {
        var mejor = 0;
        for (var i = 1; i < v.Length; i++)
            if (v[i] > v[mejor]) mejor = i;
        return mejor;
    }
}
