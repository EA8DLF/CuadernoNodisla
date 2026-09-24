namespace Nodisla.Cuaderno.Modos.Ldpc;

/// <summary>
/// Un codigo corrector de errores de baja densidad, definido por su matriz de paridad.
/// </summary>
/// <remarks>
/// <para>
/// <b>Que es esto, para quien no haya visto un LDPC.</b> FT8 manda 91 bits de informacion —el
/// mensaje y su CRC— pero emite 174. Los 83 bits de sobra no son una copia: son el resultado de
/// 83 sumas. Cada suma coge unos pocos bits repartidos por todo el mensaje y apunta si el numero
/// de unos entre ellos es par o impar. Eso es una «ecuacion de paridad», y la matriz de paridad
/// es simplemente la lista de que bits entra en cada ecuacion.
/// </para>
/// <para>
/// Lo de «baja densidad» quiere decir que cada ecuacion toca muy pocos bits —siete de los ciento
/// setenta y cuatro— y cada bit aparece en muy pocas ecuaciones. Esa escasez es justo lo que lo
/// hace util: cuando el ruido estropea un bit, las pocas ecuaciones donde ese bit aparece dejan
/// de cuadrar, mientras que todas las demas siguen bien. Cruzando quien se queja y quien no, se
/// averigua cual es el bit malo sin tener que probar combinaciones.
/// </para>
/// <para>
/// <b>Por que basta con la matriz de paridad.</b> Podria parecer que hacen falta dos tablas, una
/// para codificar y otra para decodificar. No: el codigo de FT8 es sistematico —los 91 primeros
/// bits de la palabra emitida son el mensaje tal cual— y eso permite despejar los 83 restantes a
/// partir de las propias ecuaciones. Aqui se despejan una sola vez, al construir el codigo,
/// resolviendo el sistema por eliminacion gaussiana en modulo dos. La ventaja practica es que
/// solo hay <b>una</b> tabla de constantes que traer y validar, no dos que puedan discrepar
/// entre si.
/// </para>
/// </remarks>
public sealed class CodigoLdpc
{
    private readonly int[][] _variablesDeCadaEcuacion;
    private readonly int[][] _ecuacionesDeCadaBit;
    private readonly ulong[] _filasDeParidad;
    private readonly int _palabrasPorFila;

    private CodigoLdpc(int longitud, int bitsDeMensaje, int[][] variablesDeCadaEcuacion, ulong[] filasDeParidad, int palabrasPorFila)
    {
        Longitud = longitud;
        BitsDeMensaje = bitsDeMensaje;
        _variablesDeCadaEcuacion = variablesDeCadaEcuacion;
        _filasDeParidad = filasDeParidad;
        _palabrasPorFila = palabrasPorFila;

        var porBit = new List<int>[longitud];
        for (var i = 0; i < longitud; i++) porBit[i] = [];
        for (var e = 0; e < variablesDeCadaEcuacion.Length; e++)
            foreach (var v in variablesDeCadaEcuacion[e])
                porBit[v].Add(e);
        _ecuacionesDeCadaBit = new int[longitud][];
        for (var i = 0; i < longitud; i++) _ecuacionesDeCadaBit[i] = [.. porBit[i]];
    }

    /// <summary>Bits que se emiten por palabra de codigo. En FT8 son 174.</summary>
    public int Longitud { get; }

    /// <summary>Bits de informacion que lleva cada palabra. En FT8 son 91.</summary>
    public int BitsDeMensaje { get; }

    /// <summary>Ecuaciones de paridad, que son <see cref="Longitud"/> menos <see cref="BitsDeMensaje"/>.</summary>
    public int Ecuaciones => _variablesDeCadaEcuacion.Length;

    /// <summary>Que bits entran en cada ecuacion de paridad.</summary>
    public IReadOnlyList<int[]> VariablesDeCadaEcuacion => _variablesDeCadaEcuacion;

    /// <summary>En que ecuaciones aparece cada bit.</summary>
    public IReadOnlyList<int[]> EcuacionesDeCadaBit => _ecuacionesDeCadaBit;

    /// <summary>
    /// Construye el codigo a partir de la matriz de paridad.
    /// </summary>
    /// <param name="matrizDeParidad">
    /// Una fila por ecuacion y una columna por bit emitido. El valor de una casilla dice si ese
    /// bit entra en esa ecuacion.
    /// </param>
    /// <param name="bitsDeMensaje">Cuantos de los primeros bits son el mensaje.</param>
    /// <exception cref="ArgumentException">Si la matriz no es coherente o no se puede despejar.</exception>
    public static CodigoLdpc DesdeMatrizDeParidad(bool[][] matrizDeParidad, int bitsDeMensaje)
    {
        ArgumentNullException.ThrowIfNull(matrizDeParidad);
        if (matrizDeParidad.Length == 0) throw new ArgumentException("La matriz de paridad esta vacía.", nameof(matrizDeParidad));

        var ecuaciones = matrizDeParidad.Length;
        var longitud = matrizDeParidad[0].Length;
        foreach (var fila in matrizDeParidad)
            if (fila.Length != longitud)
                throw new ArgumentException("Todas las filas de la matriz deben medir lo mismo.", nameof(matrizDeParidad));
        if (bitsDeMensaje <= 0 || bitsDeMensaje >= longitud)
            throw new ArgumentOutOfRangeException(nameof(bitsDeMensaje));
        if (longitud - bitsDeMensaje != ecuaciones)
            throw new ArgumentException(
                $"Un codigo sistematico necesita tantas ecuaciones como bits de paridad: hay {ecuaciones} y harían falta {longitud - bitsDeMensaje}.",
                nameof(matrizDeParidad));

        var variables = new int[ecuaciones][];
        for (var e = 0; e < ecuaciones; e++)
        {
            var lista = new List<int>();
            for (var v = 0; v < longitud; v++)
                if (matrizDeParidad[e][v]) lista.Add(v);
            if (lista.Count == 0)
                throw new ArgumentException($"La ecuación {e} no toca ningún bit.", nameof(matrizDeParidad));
            variables[e] = [.. lista];
        }

        var (filas, palabras) = DespejarLosBitsDeParidad(matrizDeParidad, bitsDeMensaje, ecuaciones, longitud);
        return new CodigoLdpc(longitud, bitsDeMensaje, variables, filas, palabras);
    }

    /// <summary>
    /// Resuelve, de una vez por todas, como se calcula cada bit de paridad a partir del mensaje.
    /// </summary>
    /// <remarks>
    /// Las ecuaciones dicen que la suma de ciertos bits del mensaje mas ciertos bits de paridad
    /// tiene que dar cero. Partiendo esas ecuaciones en la parte del mensaje y la parte de la
    /// paridad, queda un sistema lineal cuadrado en modulo dos; se invierte por eliminacion de
    /// Gauss-Jordan y el resultado es, para cada bit de paridad, la lista de bits del mensaje
    /// que hay que sumar. Esa lista se guarda como mapa de bits para poder sumarla despues a
    /// golpe de o-exclusivo sobre palabras de 64 bits.
    /// </remarks>
    private static (ulong[] Filas, int PalabrasPorFila) DespejarLosBitsDeParidad(bool[][] h, int k, int m, int n)
    {
        // Se trabaja sobre [B | A | I]: a la izquierda la parte de paridad, en medio la del
        // mensaje y a la derecha la identidad, que va recogiendo las operaciones hechas.
        var anchoIzquierda = m;
        var ancho = anchoIzquierda + k;
        var palabras = (ancho + 63) / 64;
        var tabla = new ulong[m * palabras];

        for (var e = 0; e < m; e++)
        {
            var baseFila = e * palabras;
            for (var j = 0; j < m; j++)
                if (h[e][k + j]) tabla[baseFila + (j / 64)] |= 1UL << (j % 64);
            for (var j = 0; j < k; j++)
                if (h[e][j])
                {
                    var col = anchoIzquierda + j;
                    tabla[baseFila + (col / 64)] |= 1UL << (col % 64);
                }
        }

        for (var columna = 0; columna < m; columna++)
        {
            var pivote = -1;
            for (var fila = columna; fila < m; fila++)
                if ((tabla[(fila * palabras) + (columna / 64)] & (1UL << (columna % 64))) != 0) { pivote = fila; break; }
            if (pivote < 0)
                throw new ArgumentException(
                    $"La matriz de paridad no permite despejar el bit de paridad {columna}: la parte derecha es singular y el código no es sistematico con este reparto.",
                    nameof(h));

            if (pivote != columna)
                for (var p = 0; p < palabras; p++)
                    (tabla[(columna * palabras) + p], tabla[(pivote * palabras) + p]) = (tabla[(pivote * palabras) + p], tabla[(columna * palabras) + p]);

            for (var fila = 0; fila < m; fila++)
            {
                if (fila == columna) continue;
                if ((tabla[(fila * palabras) + (columna / 64)] & (1UL << (columna % 64))) == 0) continue;
                for (var p = 0; p < palabras; p++) tabla[(fila * palabras) + p] ^= tabla[(columna * palabras) + p];
            }
        }

        // Hecha la eliminacion, la mitad derecha de cada fila es la receta del bit de paridad.
        var palabrasPorFila = (k + 63) / 64;
        var recetas = new ulong[m * palabrasPorFila];
        for (var e = 0; e < m; e++)
            for (var j = 0; j < k; j++)
            {
                var col = anchoIzquierda + j;
                if ((tabla[(e * palabras) + (col / 64)] & (1UL << (col % 64))) != 0)
                    recetas[(e * palabrasPorFila) + (j / 64)] |= 1UL << (j % 64);
            }
        return (recetas, palabrasPorFila);
    }

    /// <summary>
    /// Anade los bits de paridad a un mensaje y devuelve la palabra de codigo completa.
    /// </summary>
    /// <param name="mensaje">Los <see cref="BitsDeMensaje"/> bits de informacion.</param>
    /// <returns>Los <see cref="Longitud"/> bits que hay que emitir.</returns>
    public byte[] Codificar(ReadOnlySpan<byte> mensaje)
    {
        if (mensaje.Length != BitsDeMensaje)
            throw new ArgumentException($"El mensaje debe tener {BitsDeMensaje} bits y tiene {mensaje.Length}.", nameof(mensaje));

        Span<ulong> empaquetado = stackalloc ulong[_palabrasPorFila];
        for (var i = 0; i < BitsDeMensaje; i++)
            if (mensaje[i] != 0) empaquetado[i / 64] |= 1UL << (i % 64);

        var palabra = new byte[Longitud];
        mensaje.CopyTo(palabra);
        for (var e = 0; e < Ecuaciones; e++)
        {
            ulong acumulado = 0;
            for (var p = 0; p < _palabrasPorFila; p++)
                acumulado ^= _filasDeParidad[(e * _palabrasPorFila) + p] & empaquetado[p];
            // La paridad del bit es la paridad del numero de unos que quedan.
            palabra[BitsDeMensaje + e] = (byte)(System.Numerics.BitOperations.PopCount(acumulado) & 1);
        }
        return palabra;
    }

    /// <summary>Dice si una palabra cumple todas las ecuaciones de paridad.</summary>
    /// <remarks>
    /// Es lo que el decodificador mira en cada vuelta para saber si ya ha terminado. Cumplirlas
    /// todas <b>no</b> significa que el mensaje sea el que se emitio: significa que es
    /// <i>alguna</i> palabra valida del codigo. Quien decide si es la buena es el CRC.
    /// </remarks>
    public bool CumpleParidad(ReadOnlySpan<byte> palabra)
    {
        if (palabra.Length != Longitud) return false;
        for (var e = 0; e < _variablesDeCadaEcuacion.Length; e++)
        {
            var suma = 0;
            foreach (var v in _variablesDeCadaEcuacion[e]) suma ^= palabra[v] & 1;
            if (suma != 0) return false;
        }
        return true;
    }

    /// <summary>Cuantas ecuaciones no cuadran. Cero significa palabra valida.</summary>
    public int EcuacionesQueFallan(ReadOnlySpan<byte> palabra)
    {
        var fallos = 0;
        for (var e = 0; e < _variablesDeCadaEcuacion.Length; e++)
        {
            var suma = 0;
            foreach (var v in _variablesDeCadaEcuacion[e]) suma ^= palabra[v] & 1;
            if (suma != 0) fallos++;
        }
        return fallos;
    }

    /// <summary>
    /// Construye un codigo de pruebas con la misma forma que el de FT8, pero que <b>no es</b> el de FT8.
    /// </summary>
    /// <param name="semilla">Semilla del generador, para que el codigo salga siempre igual.</param>
    /// <param name="longitud">Bits por palabra.</param>
    /// <param name="bitsDeMensaje">Bits de informacion.</param>
    /// <remarks>
    /// <para>
    /// Existe para poder probar y medir toda la cadena —modulacion, sincronismo, demodulacion y
    /// correccion— mientras no este disponible la tabla del codigo real. Tiene las mismas
    /// dimensiones y una densidad parecida, asi que las cifras del banco de medida son
    /// representativas, pero <b>una senal codificada con esto no la decodifica nadie mas</b>.
    /// </para>
    /// <para>
    /// La parte de paridad se construye triangular con unos en la diagonal a proposito: asi el
    /// sistema siempre se puede despejar y la construccion no falla nunca por azar.
    /// </para>
    /// </remarks>
    public static CodigoLdpc ConstruirDePrueba(int semilla = 8, int longitud = 174, int bitsDeMensaje = 91)
    {
        var ecuaciones = longitud - bitsDeMensaje;
        var azar = new Random(semilla);
        var h = new bool[ecuaciones][];
        for (var e = 0; e < ecuaciones; e++) h[e] = new bool[longitud];

        // Cuantas variables comparten ya cada pareja de ecuaciones. Mantener esto en cero o uno
        // es lo que evita los ciclos cortos, que es de lo que depende que el codigo sea bueno.
        var compartidas = new int[ecuaciones, ecuaciones];

        bool CabeEn(List<int> grupo, int candidata)
        {
            foreach (var otra in grupo)
                if (compartidas[Math.Min(otra, candidata), Math.Max(otra, candidata)] > 0) return false;
            return true;
        }

        void Anotar(List<int> grupo)
        {
            for (var i = 0; i < grupo.Count; i++)
                for (var j = i + 1; j < grupo.Count; j++)
                    compartidas[Math.Min(grupo[i], grupo[j]), Math.Max(grupo[i], grupo[j])]++;
        }

        // Parte de la paridad. Se recorre por columnas y no por filas: una misma columna puede
        // acabar en tres ecuaciones, y si se recorriera por filas se anotarian solo dos de las
        // tres parejas que eso crea, dejando ciclos cortos sin detectar.
        var libres = new List<int>(ecuaciones);
        for (var j = 0; j < ecuaciones; j++)
        {
            // La diagonal hace la parte de paridad triangular con unos en la diagonal, y eso
            // garantiza que el sistema siempre se puede despejar.
            h[j][bitsDeMensaje + j] = true;
            var grupo = new List<int> { j };
            while (grupo.Count < 3)
            {
                libres.Clear();
                for (var e = j + 1; e < ecuaciones; e++)
                    if (CabeEn(grupo, e)) libres.Add(e);
                if (libres.Count == 0) break;
                var elegida = libres[azar.Next(libres.Count)];
                h[elegida][bitsDeMensaje + j] = true;
                grupo.Add(elegida);
            }
            Anotar(grupo);
        }

        // Parte del mensaje: cada bit de informacion entra en tres ecuaciones que no se hayan
        // cruzado antes. En vez de probar al azar y descartar —que se atasca cuando quedan pocas
        // combinaciones libres— se filtra primero cuales valen y se elige entre esas.
        for (var v = 0; v < bitsDeMensaje; v++)
        {
            var grupo = new List<int>(3);
            while (grupo.Count < 3)
            {
                libres.Clear();
                for (var e = 0; e < ecuaciones; e++)
                    if (!grupo.Contains(e) && CabeEn(grupo, e)) libres.Add(e);
                if (libres.Count == 0) break;
                grupo.Add(libres[azar.Next(libres.Count)]);
            }
            // Solo si de verdad no quedaba sitio se afloja la condicion: mas vale un codigo con
            // algun ciclo corto que una construccion que no termine nunca.
            while (grupo.Count < 3)
            {
                var e = azar.Next(ecuaciones);
                if (!grupo.Contains(e)) grupo.Add(e);
            }
            foreach (var e in grupo) h[e][v] = true;
            Anotar(grupo);
        }

        return DesdeMatrizDeParidad(h, bitsDeMensaje);
    }

    /// <summary>
    /// Cuantas parejas de ecuaciones comparten mas de una variable.
    /// </summary>
    /// <remarks>
    /// Dos ecuaciones que comparten dos variables forman un ciclo de longitud cuatro en el grafo
    /// del codigo. La propagacion de creencias se atasca en esos ciclos: las dos ecuaciones se
    /// repiten la una a la otra lo mismo que ya sabian y se convencen entre si de algo que no
    /// han comprobado. Un codigo bueno no tiene ninguno; este numero permite comprobarlo.
    /// </remarks>
    public int ParejasDeEcuacionesQueSeSolapan()
    {
        var solapes = 0;
        for (var a = 0; a < Ecuaciones; a++)
            for (var b = a + 1; b < Ecuaciones; b++)
            {
                var comunes = 0;
                foreach (var v in _variablesDeCadaEcuacion[a])
                    if (Array.IndexOf(_variablesDeCadaEcuacion[b], v) >= 0) comunes++;
                if (comunes > 1) solapes++;
            }
        return solapes;
    }
}
