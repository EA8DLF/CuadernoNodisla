namespace Nodisla.Cuaderno.Modos.Ldpc;

/// <summary>
/// Recupera mensajes que la propagacion de creencias no consigue sacar.
/// </summary>
/// <remarks>
/// <para>
/// <b>El problema.</b> La propagacion de creencias funciona muy bien mientras quede algo de
/// informacion fiable en el mensaje, y se rinde de golpe cuando no queda. Medido en el banco,
/// el escalon esta entre los −18 dB, donde se saca casi todo, y los −22, donde no se saca nada.
/// Ese acantilado no es la sensibilidad del codigo: es la sensibilidad del <i>metodo</i>.
/// </para>
/// <para>
/// <b>La idea.</b> Aunque el mensaje entero sea dudoso, no todos los bits lo son por igual:
/// siempre hay unos cuantos de los que el demodulador esta bastante seguro. Si se ordenan los
/// 174 bits del mas fiable al menos fiable y se cogen los 91 mas fiables que sean independientes
/// entre si, <b>esos 91 bits determinan el mensaje entero</b>, porque 91 son justo los bits de
/// informacion del codigo. Basta con darlos por buenos, reconstruir los otros 83 a partir de
/// ellos, y ver que sale.
/// </para>
/// <para>
/// De ahi el nombre: se decodifica por <i>conjuntos ordenados</i>, ordenados por fiabilidad.
/// Ademas se prueba a darle la vuelta a cada uno de esos 91 bits, uno a uno, por si el
/// demodulador se equivoco precisamente en el que parecia mas seguro; eso da 92 mensajes
/// candidatos y se escoge el que menos contradice lo que dijo el demodulador.
/// </para>
/// <para>
/// <b>Y aqui esta el peligro, que es lo que manda en el diseno.</b> Los 92 candidatos son todos
/// palabras validas del codigo: cualquiera de ellos, si se diera por bueno, produciria un
/// indicativo con toda la pinta de ser real. Lo unico que los separa es el CRC de 14 bits, que
/// deja pasar una palabra falsa de cada dieciseis mil. Si se probara el CRC con los 92
/// candidatos, y esto se hiciera con doscientas candidatas por ventana, saldria <b>mas de un
/// contacto inventado por ventana</b>. Seria peor que no tener nada.
/// </para>
/// <para>
/// Por eso aqui <b>solo sale un candidato</b>: el mejor de los 92 segun lo que dijo el
/// demodulador. Quien llama comprueba su CRC una sola vez, igual que hace con la propagacion de
/// creencias, y el riesgo de inventar no sube ni un apice. Se pierde alguna decodificacion que
/// se podria haber sacado revisando los 92, y se gana no mentir nunca, que es el trato que el
/// propietario de este cuaderno pidio expresamente.
/// </para>
/// </remarks>
public sealed class RecuperacionProfunda
{
    private readonly CodigoLdpc _codigo;
    private readonly int _palabras;
    private readonly ulong[] _generadora;
    private readonly ulong[] _trabajo;
    private readonly int[] _orden;
    private readonly int[] _base;
    private readonly byte[] _dura;
    private readonly ulong[] _duraEmpaquetada;
    private readonly ulong[] _candidato;
    private readonly ulong[] _mejor;

    /// <summary>Prepara la recuperacion para un codigo concreto.</summary>
    /// <param name="codigo">Codigo con el que se trabaja.</param>
    public RecuperacionProfunda(CodigoLdpc codigo)
    {
        ArgumentNullException.ThrowIfNull(codigo);
        _codigo = codigo;
        _palabras = (codigo.Longitud + 63) / 64;

        // La matriz generadora no hace falta guardarla en ninguna tabla: se saca codificando los
        // 91 mensajes que llevan un solo bit puesto. Cada uno da una fila, y cualquier mensaje es
        // una suma de esas filas. Se calcula una vez al arrancar.
        _generadora = new ulong[codigo.BitsDeMensaje * _palabras];
        var unidad = new byte[codigo.BitsDeMensaje];
        for (var i = 0; i < codigo.BitsDeMensaje; i++)
        {
            Array.Clear(unidad);
            unidad[i] = 1;
            var fila = codigo.Codificar(unidad);
            for (var j = 0; j < codigo.Longitud; j++)
                if (fila[j] != 0) _generadora[(i * _palabras) + (j / 64)] |= 1UL << (j % 64);
        }

        _trabajo = new ulong[codigo.BitsDeMensaje * _palabras];
        _orden = new int[codigo.Longitud];
        _base = new int[codigo.BitsDeMensaje];
        _dura = new byte[codigo.Longitud];
        _duraEmpaquetada = new ulong[_palabras];
        _candidato = new ulong[_palabras];
        _mejor = new ulong[_palabras];
    }

    /// <summary>
    /// Intenta reconstruir la palabra de codigo a partir de los bits mas fiables.
    /// </summary>
    /// <param name="confianzas">Una por bit emitido; positivo quiere decir que parece un cero.</param>
    /// <param name="palabra">Destino de los bits recuperados.</param>
    /// <returns>
    /// Cierto si se pudo construir un candidato. <b>No quiere decir que sea el mensaje que se
    /// emitio</b>: eso lo decide el CRC, que comprueba quien llama.
    /// </returns>
    public bool TryRecuperar(ReadOnlySpan<float> confianzas, Span<byte> palabra)
    {
        if (confianzas.Length != _codigo.Longitud)
            throw new ArgumentException($"Hacen falta {_codigo.Longitud} confianzas y llegan {confianzas.Length}.", nameof(confianzas));
        if (palabra.Length != _codigo.Longitud)
            throw new ArgumentException($"La palabra debe tener {_codigo.Longitud} bits.", nameof(palabra));

        OrdenarPorFiabilidad(confianzas);
        if (!BuscarLaBaseMasFiable()) return false;

        // Decision dura de cada bit, que es lo que el demodulador diria si no pudiera dudar.
        Array.Clear(_duraEmpaquetada);
        for (var j = 0; j < _codigo.Longitud; j++)
        {
            _dura[j] = (byte)(confianzas[j] < 0 ? 1 : 0);
            if (_dura[j] != 0) _duraEmpaquetada[j / 64] |= 1UL << (j % 64);
        }

        // Candidato de partida: se dan por buenos los 91 bits de la base y se reconstruye el resto.
        Array.Clear(_candidato);
        for (var i = 0; i < _base.Length; i++)
            if (_dura[_base[i]] != 0) Sumar(_candidato, i);

        Array.Copy(_candidato, _mejor, _palabras);
        var mejorDistancia = Distancia(_candidato, confianzas);

        // Y ahora, uno a uno, se prueba a darle la vuelta a cada bit de la base. Sumar la fila
        // que le corresponde es exactamente eso, porque esa fila tiene un uno en ese bit y ceros
        // en los otros noventa de la base.
        for (var i = 0; i < _base.Length; i++)
        {
            Sumar(_candidato, i);
            var distancia = Distancia(_candidato, confianzas);
            if (distancia < mejorDistancia)
            {
                mejorDistancia = distancia;
                Array.Copy(_candidato, _mejor, _palabras);
            }
            Sumar(_candidato, i); // deshacer
        }

        for (var j = 0; j < _codigo.Longitud; j++)
            palabra[j] = (byte)((_mejor[j / 64] >> (j % 64)) & 1);

        // Por construccion tiene que cumplir la paridad; si no la cumpliera seria un fallo de
        // esta clase, no del ruido, y vale mas no devolver nada que devolver algo incoherente.
        return _codigo.CumpleParidad(palabra);
    }

    /// <summary>Ordena los bits del mas fiable al menos fiable.</summary>
    private void OrdenarPorFiabilidad(ReadOnlySpan<float> confianzas)
    {
        for (var i = 0; i < _orden.Length; i++) _orden[i] = i;
        var claves = new float[_orden.Length];
        for (var i = 0; i < claves.Length; i++) claves[i] = -MathF.Abs(confianzas[i]);
        Array.Sort(claves, _orden);
    }

    /// <summary>
    /// Escoge los 91 bits mas fiables que basten para determinar el mensaje entero.
    /// </summary>
    /// <remarks>
    /// No vale coger los 91 primeros sin mas: puede que entre ellos haya alguno que se deduzca
    /// de los otros, y entonces no determinarian nada. Se recorren por fiabilidad y se va
    /// quedando con los que aportan algo nuevo, haciendo eliminacion sobre la marcha; el que no
    /// aporta se salta y se pasa al siguiente, que sera algo menos fiable pero servira.
    /// </remarks>
    private bool BuscarLaBaseMasFiable()
    {
        Array.Copy(_generadora, _trabajo, _generadora.Length);
        var filasUsadas = 0;

        for (var k = 0; k < _orden.Length && filasUsadas < _base.Length; k++)
        {
            var columna = _orden[k];
            var mascara = 1UL << (columna % 64);
            var palabra = columna / 64;

            var pivote = -1;
            for (var f = filasUsadas; f < _base.Length; f++)
                if ((_trabajo[(f * _palabras) + palabra] & mascara) != 0) { pivote = f; break; }
            if (pivote < 0) continue; // ese bit se deduce de los ya elegidos: no aporta nada

            if (pivote != filasUsadas) Intercambiar(pivote, filasUsadas);

            // Se limpia esa columna en todas las demas filas, de modo que al final cada fila
            // tenga un uno en su bit de la base y ceros en los de las demas.
            for (var f = 0; f < _base.Length; f++)
            {
                if (f == filasUsadas) continue;
                if ((_trabajo[(f * _palabras) + palabra] & mascara) == 0) continue;
                for (var p = 0; p < _palabras; p++)
                    _trabajo[(f * _palabras) + p] ^= _trabajo[(filasUsadas * _palabras) + p];
            }

            _base[filasUsadas] = columna;
            filasUsadas++;
        }

        return filasUsadas == _base.Length;
    }

    private void Intercambiar(int a, int b)
    {
        for (var p = 0; p < _palabras; p++)
            (_trabajo[(a * _palabras) + p], _trabajo[(b * _palabras) + p]) =
                (_trabajo[(b * _palabras) + p], _trabajo[(a * _palabras) + p]);
    }

    private void Sumar(ulong[] destino, int fila)
    {
        for (var p = 0; p < _palabras; p++) destino[p] ^= _trabajo[(fila * _palabras) + p];
    }

    /// <summary>
    /// Cuanto contradice un candidato a lo que dijo el demodulador.
    /// </summary>
    /// <remarks>
    /// Se suma la confianza de cada bit en el que el candidato lleva la contraria. Cambiar un bit
    /// del que el demodulador dudaba cuesta poco; cambiar uno del que estaba seguro cuesta mucho.
    /// El candidato bueno es el que sale mas barato.
    /// </remarks>
    private double Distancia(ulong[] candidato, ReadOnlySpan<float> confianzas)
    {
        double suma = 0;
        for (var p = 0; p < _palabras; p++)
        {
            var distintos = candidato[p] ^ _duraEmpaquetada[p];
            while (distintos != 0)
            {
                var bit = System.Numerics.BitOperations.TrailingZeroCount(distintos);
                distintos &= distintos - 1;
                suma += Math.Abs(confianzas[(p * 64) + bit]);
            }
        }
        return suma;
    }
}
