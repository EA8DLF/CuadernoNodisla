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
/// Ademas se prueba a darle la vuelta a esos 91 bits —de uno en uno, y en grupos de hasta
/// <see cref="Orden"/> entre los menos fiables— por si el demodulador se equivoco precisamente
/// en alguno de los que parecian seguros. De todas las reconstrucciones que salen se escoge la
/// que menos contradice lo que dijo el demodulador.
/// </para>
/// <para>
/// <b>Y aqui esta el peligro, que es lo que manda en el diseno.</b> Todas esas reconstrucciones
/// son palabras validas del codigo: cualquiera de ellas, si se diera por buena, produciria un
/// indicativo con toda la pinta de ser real. Lo unico que las separa es el CRC de 14 bits, que
/// deja pasar una palabra falsa de cada dieciseis mil. Si se le dieran al CRC todas, y esto se
/// hiciera con doscientas candidatas por ventana, saldria <b>mas de un contacto inventado por
/// ventana</b>. Seria peor que no tener nada.
/// </para>
/// <para>
/// Por eso salen <see cref="MaximoDeCandidatos"/> como mucho —de serie, <b>una sola</b>: la
/// mejor—, y ademas solo si pasan el freno de <see cref="ErroresDurosMaximos"/>. Se pierde
/// alguna decodificacion que se podria haber sacado mirandolas todas, y se gana no mentir nunca,
/// que es el trato que el propietario de este cuaderno pidio expresamente.
/// </para>
/// <para>
/// <b>Que la lista no ayuda esta medido</b>, no supuesto: guardar cuatro u ocho reconstrucciones
/// en vez de una da exactamente el mismo numero de decodificaciones en el banco. Cuando esta
/// clase acierta, la buena es la primera; las demas solo anaden tiradas del CRC. Por eso el
/// valor de serie es uno, aunque se pueda subir para volver a medirlo.
/// </para>
/// <para>
/// <b>Y eso solo no basta, y hubo que medirlo para verlo.</b> Devolver un unico candidato deja
/// el riesgo igual <i>por candidata</i>, pero no por ventana, y esa es la cuenta que importa.
/// La propagacion de creencias se rinde en casi todas las candidatas de una ventana y solo
/// llega al CRC en una de cada veinte ventanas; esta clase, en cambio, <b>siempre</b> encuentra
/// una palabra valida, tambien con ruido puro, porque 91 bits fiables siempre determinan
/// <i>algo</i>. Con doscientas candidatas por ventana eso pasa de 0,05 a 200 tiradas del CRC
/// por ventana: cuatro mil veces mas oportunidades de que una palabra falsa se cuele por el
/// agujero de uno entre dieciseis mil.
/// </para>
/// <para>
/// Medido en el banco con senal sintetica: sin filtrar salen <b>12 mensajes inventados por cada
/// mil ventanas</b>, es decir unos tres por hora de escucha. Por eso la palabra reconstruida
/// tiene que pasar ademas <see cref="ErroresDurosMaximos"/>, y quien llama solo debe intentar
/// esto en candidatas con sincronismo de verdad. Con las dos cosas puestas, la cifra baja a
/// menos de un inventado por cada cien mil ventanas y no se pierde ni una decodificacion buena
/// de las medidas.
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
    private int[] _combinacion = new int[4];
    private int _maximoDeCandidatos = 1;
    private ulong[] _listaPalabras;
    private double[] _listaDistancias;
    private int[] _listaErroresDuros;
    private double _seguridadTotal;

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
        _listaPalabras = new ulong[_palabras];
        _listaDistancias = new double[1];
        _listaErroresDuros = new int[1];
    }

    /// <summary>
    /// Bits en los que la palabra reconstruida puede contradecir al demodulador, como mucho.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es el segundo sello, independiente del CRC, y es lo que hace que esto se pueda encender
    /// sin llenar el cuaderno de contactos que nunca existieron. La idea es sencilla: si la
    /// palabra reconstruida es la que de verdad se emitio, tiene que parecerse a lo que oyo el
    /// demodulador. Se cuentan los bits en que le lleva la contraria; si son demasiados, lo que
    /// se ha reconstruido no es la senal, es una palabra del codigo que casualmente cae cerca
    /// del ruido.
    /// </para>
    /// <para>
    /// El numero no esta puesto a ojo. Midiendo 180 ventanas de banco entre −22 y −17 dB, las
    /// reconstrucciones <i>buenas</i> contradecian al demodulador en 20, 22, 24 y 26 bits, y las
    /// de ruido puro en 34 de mediana, con solo 6 de cada 36.000 por debajo de 26. Veintiseis es
    /// por tanto el punto que <b>no pierde ni una</b> de las buenas y corta el 99,98 % del ruido.
    /// </para>
    /// <para>
    /// Si algun dia se sube este numero, hay que volver a pasar el banco y mirar la columna de
    /// falsos. No es un parametro de afinado: es el freno.
    /// </para>
    /// </remarks>
    public int ErroresDurosMaximos { get; set; } = 26;

    /// <summary>
    /// Que parte de la seguridad del demodulador puede contradecir la reconstruccion, de cero a uno.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es el mismo freno que <see cref="ErroresDurosMaximos"/> pero mirando lo que importa.
    /// Contar bits trata igual dos casos que no se parecen: contradecir veinte bits de los que
    /// el demodulador dudaba, que es lo que hace una senal debil de verdad, y contradecir veinte
    /// de los que estaba seguro, que es lo que hace el ruido cuando da la casualidad de que cae
    /// cerca de una palabra del codigo.
    /// </para>
    /// <para>
    /// Aqui se suma la <i>seguridad</i> de los bits contradichos y se divide por la seguridad
    /// total de la ventana. Una reconstruccion buena se lleva por delante una fraccion pequena,
    /// porque solo toca lo dudoso; una de ruido se lleva mucho mas, porque toca de todo.
    /// </para>
    /// <para>
    /// <b>Por que hizo falta.</b> Al subir el <see cref="Orden"/> se exploran cientos de miles de
    /// reconstrucciones en lugar de un centenar, y con tantas oportunidades el ruido acaba
    /// encontrando alguna que contradice pocos bits. Medido: con orden dos, el 18 % del ruido
    /// puro pasaba el freno de bits; con orden cuatro, el 65 %. Apretar el freno de bits para
    /// compensar quitaba tantas decodificaciones buenas como malas, y la ganancia del orden alto
    /// se esfumaba. Este freno es el que permite quedarse con la ganancia.
    /// </para>
    /// <para>
    /// Con uno queda apagado, que es como estaba antes de existir.
    /// </para>
    /// </remarks>
    public double FraccionDeSeguridadMaxima { get; set; } = 1.0;

    /// <summary>
    /// Cuantos bits de la base se prueban a la vez, como mucho.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Con uno solo se prueba a darle la vuelta a un bit cada vez; con dos, tambien a las
    /// parejas; con tres, a los trios. Cuanto mas alto, mas señales debiles se rescatan y mas
    /// cuesta: las combinaciones crecen como <c>K</c> elevado al orden, siendo <c>K</c> la
    /// <see cref="ProfundidadDeLaBusqueda"/>.
    /// </para>
    /// <para>
    /// <b>Subirlo no es gratis en honradez, no solo en tiempo.</b> Cuantas mas combinaciones se
    /// miran, mas facil es que alguna caiga cerca del ruido por casualidad, asi que tocar este
    /// numero obliga a volver a pasar el banco mirando la columna de falsos. No es un mando que
    /// se pueda girar a ojo.
    /// </para>
    /// <para>
    /// <b>Dos, y la historia de por que no es mas, porque merece quedar escrita para que nadie
    /// la repita.</b> Subir el orden saca mas decodificaciones: con la busqueda abierta a los 91
    /// bits salian 199 con orden dos, 219 con tres y 230 con cuatro, de 800 ventanas entre −22 y
    /// −19 dB. Parece una mejora del quince por ciento y no lo es.
    /// </para>
    /// <para>
    /// Lo que pasa es que <b>saca tambien mas palabras falsas</b>, en la misma proporcion. Con
    /// orden dos, el 18 % del ruido puro encuentra una reconstruccion que pasa el freno; con
    /// orden cuatro, el 65 %. Las tiradas del CRC por ventana pasan de 0,12 a 1,4, y como el CRC
    /// deja pasar una de cada 16.384, eso es multiplicar por doce las probabilidades de meter un
    /// contacto que nunca existio.
    /// </para>
    /// <para>
    /// Se probo a compensarlo apretando los frenos, y ahi esta la leccion: <b>apretar el freno
    /// quita tantas decodificaciones buenas como malas</b>. Midiendo ocho configuraciones de
    /// orden, profundidad y freno, todas caen en la misma curva de «aciertos contra riesgo». No
    /// hay ninguna que saque mas que las demas al mismo riesgo. El orden alto no regala nada:
    /// cambia sensibilidad por riesgo, y al deshacer el cambio se vuelve al punto de partida.
    /// </para>
    /// <para>
    /// Por eso se queda en dos, que es el punto de menor riesgo medido, y por eso el sitio donde
    /// hay que trabajar para ganar decibelios <b>no es este</b>: es la calidad de las confianzas
    /// que entrega el demodulador. Mientras a −21 dB lleguen 36 bits mal de 174, ningun orden
    /// arregla eso sin inventar.
    /// </para>
    /// </remarks>
    public int Orden { get; set; } = 2;

    /// <summary>
    /// Cuantos bits de la cola de la base entran en las combinaciones de orden dos o mas.
    /// </summary>
    /// <remarks>
    /// <para>
    /// La idea de partida era que con la senal muy debil rara vez se equivoca uno de los bits
    /// seguros, y que bastaria con combinar la cola: los ultimos que entraron en la base a
    /// reganadientes porque no quedaba nada mejor.
    /// </para>
    /// <para>
    /// <b>Y medido, la idea era buena, pero no por lo que parecia.</b> Ensanchar la ventana saca
    /// mas decodificaciones —de 141 con veinte bits a 156 con los 91, en 600 ventanas del filo—
    /// pero multiplica por cuatro las palabras de ruido que llegan al CRC. Estrechar la cola no
    /// es una manera de ahorrar tiempo: es <b>un freno mas</b>, y de los buenos. Al obligar a
    /// que las vueltas se den solo entre los bits de los que el demodulador ya dudaba, impide
    /// que la busqueda fabrique reconstrucciones tocando bits de los que estaba seguro, que es
    /// justo lo que hace el ruido cuando encuentra algo.
    /// </para>
    /// <para>
    /// Veinte es el valor con el que se midio la version que quedo verificada. Ensancharlo
    /// obliga a volver a pasar el banco mirando las tiradas del CRC, no solo los aciertos.
    /// </para>
    /// </remarks>
    public int ProfundidadDeLaBusqueda { get; set; } = 20;

    /// <summary>
    /// Cuantas combinaciones se prueban como mucho en cada orden.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es el mando que de verdad reparte el tiempo. Los trios de entre 91 bits son ciento
    /// veintiun mil, y los cuartetos, dos millones y medio: si se pusiera la misma
    /// <see cref="ProfundidadDeLaBusqueda"/> para todos los ordenes, el orden cuatro obligaria a
    /// estrecharla tanto que el orden tres saldria perdiendo. Con un tope por orden, cada uno se
    /// queda con la ventana mas ancha que le cabe, y el tres sigue recorriendo la base entera
    /// aunque el cuatro tenga que conformarse con los cuarenta primeros.
    /// </para>
    /// <para>
    /// Sube el gasto de forma predecible: el trabajo total es como mucho el tope por el orden.
    /// </para>
    /// </remarks>
    public int CombinacionesPorOrden { get; set; } = 120_000;

    /// <summary>
    /// La ventana mas ancha que le cabe a un orden sin pasarse del tope de combinaciones.
    /// </summary>
    private int ProfundidadPara(int orden)
    {
        var tope = Math.Min(ProfundidadDeLaBusqueda, _base.Length);
        if (orden <= 1) return _base.Length;

        var k = tope;
        while (k > orden && CuantasCombinaciones(k, orden) > CombinacionesPorOrden) k--;
        return k;
    }

    /// <summary>Numero de combinaciones de <paramref name="orden"/> elementos entre <paramref name="k"/>.</summary>
    /// <remarks>En coma flotante a proposito: con 91 sobre 4 el entero se queda corto enseguida.</remarks>
    private static double CuantasCombinaciones(int k, int orden)
    {
        double total = 1;
        for (var i = 0; i < orden; i++) total = total * (k - i) / (i + 1);
        return total;
    }

    /// <summary>
    /// Cuantas reconstrucciones distintas se devuelven, de la mas creible a la menos.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Con una sola se devuelve la que menos contradice al demodulador, que casi siempre es la
    /// buena pero no siempre. Guardando unas pocas y dejando que el CRC elija se recuperan las
    /// veces en que la buena quedo segunda o tercera.
    /// </para>
    /// <para>
    /// <b>Esto multiplica las tiradas del CRC</b>, que es justo lo que hay que vigilar: el CRC
    /// deja pasar una palabra falsa de cada dieciseis mil, asi que cuantas mas se le den, mas
    /// probable es que una se cuele. Lo que lo hace admisible es que a la lista <b>solo llegan
    /// las que ya han pasado el freno</b> de <see cref="ErroresDurosMaximos"/>, y el ruido casi
    /// nunca lo pasa. Medido en el banco antes de subirlo de uno.
    /// </para>
    /// </remarks>
    public int MaximoDeCandidatos
    {
        get => _maximoDeCandidatos;
        set
        {
            if (value < 1) throw new ArgumentOutOfRangeException(nameof(value), "Hace falta al menos un candidato.");
            _maximoDeCandidatos = value;
            if (_listaPalabras.Length < value * _palabras)
            {
                _listaPalabras = new ulong[value * _palabras];
                _listaDistancias = new double[value];
                _listaErroresDuros = new int[value];
            }
        }
    }

    /// <summary>Cuantas reconstrucciones dejo la ultima llamada, ya pasadas por el freno.</summary>
    public int CandidatosEncontrados { get; private set; }

    /// <summary>Copia la reconstruccion numero <paramref name="indice"/>, contando desde la mejor.</summary>
    /// <param name="indice">De cero a <see cref="CandidatosEncontrados"/> menos uno.</param>
    /// <param name="palabra">Destino, de <see cref="CodigoLdpc.Longitud"/> bits.</param>
    public void CopiarCandidato(int indice, Span<byte> palabra)
    {
        if (indice < 0 || indice >= CandidatosEncontrados) throw new ArgumentOutOfRangeException(nameof(indice));
        if (palabra.Length != _codigo.Longitud)
            throw new ArgumentException($"La palabra debe tener {_codigo.Longitud} bits.", nameof(palabra));
        var baseFila = indice * _palabras;
        for (var j = 0; j < _codigo.Longitud; j++)
            palabra[j] = (byte)((_listaPalabras[baseFila + (j / 64)] >> (j % 64)) & 1);
    }

    /// <summary>Bits en que contradijo al demodulador la reconstruccion numero <paramref name="indice"/>.</summary>
    public int ErroresDurosDe(int indice) =>
        indice < 0 || indice >= CandidatosEncontrados ? -1 : _listaErroresDuros[indice];

    /// <summary>Bits en que la mejor reconstruccion contradijo al demodulador.</summary>
    /// <remarks>Vale <c>-1</c> si no llego a construirse ninguna.</remarks>
    public int UltimosErroresDuros { get; private set; } = -1;

    /// <summary>
    /// Intenta reconstruir la palabra de codigo a partir de los bits mas fiables.
    /// </summary>
    /// <param name="confianzas">Una por bit emitido; positivo quiere decir que parece un cero.</param>
    /// <param name="palabra">Destino de los bits recuperados.</param>
    /// <returns>
    /// Cierto si se pudo construir un candidato <b>y</b> ese candidato se parece bastante a lo
    /// que oyo el demodulador. <b>No quiere decir que sea el mensaje que se emitio</b>: eso lo
    /// decide el CRC, que comprueba quien llama.
    /// </returns>
    public bool TryRecuperar(ReadOnlySpan<float> confianzas, Span<byte> palabra)
    {
        UltimosErroresDuros = -1;
        if (confianzas.Length != _codigo.Longitud)
            throw new ArgumentException($"Hacen falta {_codigo.Longitud} confianzas y llegan {confianzas.Length}.", nameof(confianzas));
        if (palabra.Length != _codigo.Longitud)
            throw new ArgumentException($"La palabra debe tener {_codigo.Longitud} bits.", nameof(palabra));

        OrdenarPorFiabilidad(confianzas);
        if (!BuscarLaBaseMasFiable()) return false;

        // Decision dura de cada bit, que es lo que el demodulador diria si no pudiera dudar, y
        // de paso la seguridad total de la ventana, que es la vara con que se mide el freno blando.
        Array.Clear(_duraEmpaquetada);
        _seguridadTotal = 0;
        for (var j = 0; j < _codigo.Longitud; j++)
        {
            _dura[j] = (byte)(confianzas[j] < 0 ? 1 : 0);
            if (_dura[j] != 0) _duraEmpaquetada[j / 64] |= 1UL << (j % 64);
            _seguridadTotal += Math.Abs(confianzas[j]);
        }

        // Candidato de partida: se dan por buenos los 91 bits de la base y se reconstruye el resto.
        Array.Clear(_candidato);
        for (var i = 0; i < _base.Length; i++)
            if (_dura[_base[i]] != 0) Sumar(_candidato, i);

        // El candidato de partida ya es una reconstruccion completa: se mira igual que las demas.
        CandidatosEncontrados = 0;
        Anotar(confianzas);

        // Y ahora se prueba a darle la vuelta a los bits de la base, primero de uno en uno y
        // luego en grupos. Sumar la fila que le corresponde a un bit es exactamente darle la
        // vuelta, porque esa fila tiene un uno en ese bit y ceros en los otros noventa.
        for (var orden = 1; orden <= Math.Max(1, Orden); orden++)
            RecorrerCombinaciones(orden, confianzas);

        if (CandidatosEncontrados == 0)
        {
            UltimosErroresDuros = -1;
            return false;
        }

        UltimosErroresDuros = _listaErroresDuros[0];
        CopiarCandidato(0, palabra);

        // Por construccion tiene que cumplir la paridad; si no la cumpliera seria un fallo de
        // esta clase, no del ruido, y vale mas no devolver nada que devolver algo incoherente.
        return _codigo.CumpleParidad(palabra);
    }

    /// <summary>
    /// Recorre todas las maneras de darle la vuelta a <paramref name="orden"/> bits a la vez.
    /// </summary>
    /// <remarks>
    /// De uno en uno se prueban los 91 bits de la base. De dos en adelante solo la cola, los
    /// <see cref="ProfundidadDeLaBusqueda"/> menos fiables, porque combinar los 91 de tres en
    /// tres serian ciento veinticinco mil vueltas para rescatar lo mismo.
    /// </remarks>
    private void RecorrerCombinaciones(int orden, ReadOnlySpan<float> confianzas)
    {
        var desde = Math.Max(0, _base.Length - ProfundidadPara(orden));
        var cuantos = _base.Length - desde;
        if (cuantos < orden) return;

        if (_combinacion.Length < orden) _combinacion = new int[orden];
        var indices = _combinacion;

        for (var k = 0; k < orden; k++)
        {
            indices[k] = desde + k;
            Sumar(_candidato, indices[k]);
        }

        while (true)
        {
            Anotar(confianzas);

            // Siguiente combinacion en orden lexicografico. Se busca la posicion mas a la
            // derecha que todavia pueda avanzar.
            var k = orden - 1;
            while (k >= 0 && indices[k] == _base.Length - orden + k) k--;
            if (k < 0) break;

            for (var q = k; q < orden; q++) Sumar(_candidato, indices[q]);   // deshacer la cola
            indices[k]++;
            for (var q = k + 1; q < orden; q++) indices[q] = indices[q - 1] + 1;
            for (var q = k; q < orden; q++) Sumar(_candidato, indices[q]);   // poner la nueva
        }

        for (var k = 0; k < orden; k++) Sumar(_candidato, indices[k]);       // dejarlo como estaba
    }

    /// <summary>
    /// Mide el candidato que hay ahora mismo y lo guarda si esta entre los mejores.
    /// </summary>
    /// <remarks>
    /// El freno se aplica <b>aqui</b>, antes de guardar: a la lista no llega nada que contradiga
    /// al demodulador en mas de <see cref="ErroresDurosMaximos"/> bits. Es lo que permite
    /// devolver varias reconstrucciones sin multiplicar por esas mismas veces el riesgo de que
    /// una palabra falsa se cuele por el CRC.
    /// </remarks>
    private void Anotar(ReadOnlySpan<float> confianzas)
    {
        var (blanda, duros) = Medir(_candidato, confianzas);
        if (duros > ErroresDurosMaximos) return;
        if (_seguridadTotal > 0 && blanda > FraccionDeSeguridadMaxima * _seguridadTotal) return;

        // Si ya esta llena y este es peor que el ultimo, no hay nada que hacer.
        if (CandidatosEncontrados == _maximoDeCandidatos && blanda >= _listaDistancias[CandidatosEncontrados - 1]) return;

        // Sitio donde entra, manteniendo la lista ordenada de mejor a peor.
        var puesto = CandidatosEncontrados;
        while (puesto > 0 && _listaDistancias[puesto - 1] > blanda) puesto--;

        // Un mismo candidato puede salir por dos caminos distintos; no se guarda dos veces.
        for (var i = 0; i < CandidatosEncontrados; i++)
            if (EsElMismo(i)) return;

        var ultimo = Math.Min(CandidatosEncontrados, _maximoDeCandidatos - 1);
        for (var i = ultimo; i > puesto; i--)
        {
            Array.Copy(_listaPalabras, (i - 1) * _palabras, _listaPalabras, i * _palabras, _palabras);
            _listaDistancias[i] = _listaDistancias[i - 1];
            _listaErroresDuros[i] = _listaErroresDuros[i - 1];
        }

        Array.Copy(_candidato, 0, _listaPalabras, puesto * _palabras, _palabras);
        _listaDistancias[puesto] = blanda;
        _listaErroresDuros[puesto] = duros;
        if (CandidatosEncontrados < _maximoDeCandidatos) CandidatosEncontrados++;
    }

    private bool EsElMismo(int indice)
    {
        var baseFila = indice * _palabras;
        for (var p = 0; p < _palabras; p++)
            if (_listaPalabras[baseFila + p] != _candidato[p]) return false;
        return true;
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
    private (double Blanda, int Duros) Medir(ulong[] candidato, ReadOnlySpan<float> confianzas)
    {
        double suma = 0;
        var duros = 0;
        for (var p = 0; p < _palabras; p++)
        {
            var distintos = candidato[p] ^ _duraEmpaquetada[p];
            duros += System.Numerics.BitOperations.PopCount(distintos);
            while (distintos != 0)
            {
                var bit = System.Numerics.BitOperations.TrailingZeroCount(distintos);
                distintos &= distintos - 1;
                suma += Math.Abs(confianzas[(p * 64) + bit]);
            }
        }
        return (suma, duros);
    }
}
