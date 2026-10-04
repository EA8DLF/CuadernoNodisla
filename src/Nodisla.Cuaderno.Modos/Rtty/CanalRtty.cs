using Nodisla.Cuaderno.Modos.Cw;

namespace Nodisla.Cuaderno.Modos.Rtty;

/// <summary>
/// El canal de recepción de RTTY: en continuo, sobre el audio ya sintonizado a los dos tonos.
/// </summary>
/// <remarks>
/// <para>
/// <b>Todo propio.</b> La envolvente de cada tono reutiliza <see cref="EnvolventeCw"/> (el mismo
/// filtro estrecho ajustable que usa el decodificador de CW; su ancho se puede volver a tocar aquí
/// sin tocar una línea del filtro): una instancia para el tono de marca y otra para el de espacio.
/// </para>
/// <para>
/// <b>Sin AGC propio.</b> A diferencia de CW, decidir si un cuadro es marca o espacio no necesita
/// seguir el nivel de la señal: basta con comparar la potencia de los dos tonos entre sí, y los dos
/// reciben el mismo ruido de fondo.
/// </para>
/// <para>
/// <b>El silenciador no puede ser el de CW.</b> <see cref="UmbralAdaptativo"/> separa, por
/// percentiles, los ratos callados de los ratos con la llave abajo; da por hecho que lo normal es
/// el silencio y la marca es la excepción (en CW la llave está arriba la mayor parte del tiempo).
/// En RTTY pasa justo lo contrario: mientras alguien transmite, a cada instante hay marca o
/// espacio, así que la potencia mayor de las dos envolventes está casi siempre arriba y los
/// percentiles no ven ningún «hueco» con el que compararla — el silenciador de CW no abriría
/// nunca.
/// </para>
/// <para>
/// Aquí el silenciador es un seguidor de piso con <b>dos tiempos distintos</b>. La potencia que
/// mira (la suma de las dos envolventes, no el máximo: con dos tonos de ruido independientes el
/// máximo tiene más cola y dispara falsos abiertos) se promedia primero sobre un tramo largo
/// (<see cref="MilisegundosDeLaVentanaDelSilenciador"/>, muchas veces un bit): un solo bit de
/// ruido puede destacar por azar, pero una docena seguidos ya no, así que hace falta esa media
/// larga para que el silenciador no se deje engañar y aun así siga pudiendo decidir bit a bit. El
/// piso de esa media baja de golpe en cuanto ve un tramo más flojo que él (el silencio de verdad
/// se reconoce enseguida) y solo sube muy despacio con una señal sostenida (constante de un
/// minuto, para no cerrarse él solo a media transmisión larga). Mientras la media no destaque
/// <see cref="_umbralDb"/> sobre ese piso, se le da al lector una línea en reposo (marca continua)
/// para que nunca intente arrancar una trama con puro ruido.
/// </para>
/// <para>
/// La comparación bit a bit, en cambio, sí necesita resolución fina: usa una ventana bastante más
/// corta (una fracción del bit) para no mezclarse con el bit siguiente, apoyada en que el
/// silenciador ya ha filtrado el ruido de fondo antes de dejarla actuar.
/// </para>
/// <para>
/// <b>Enganche.</b> Como en CW, lo decodificado no se entrega hasta que se enganchan
/// <see cref="CaracteresParaEngancharse"/> caracteres válidos seguidos (el bit de parada hace de
/// validación, ver <see cref="LectorDeBaudot"/>); antes se guarda y al engancharse se suelta de
/// golpe. Un carácter inválido antes de engancharse lo tira todo y empieza a contar de nuevo. Ya
/// enganchado, un fallo suelto no desengancha: una ráfaga de QRN en medio de una señal buena no
/// tiene por qué tirar el QSO entero.
/// </para>
/// </remarks>
public sealed class CanalRtty
{
    /// <summary>Caracteres válidos seguidos que hacen falta para empezar a entregar texto.</summary>
    public const int CaracteresParaEngancharse = 8;

    /// <summary>Constante de subida del piso de ruido, en milisegundos (ver el porqué en la clase).</summary>
    private const double ConstanteDeSubidaMs = 60_000;

    /// <summary>Ventana del silenciador: bastantes bits, para que el ruido no la pase por azar.</summary>
    public const double MilisegundosDeLaVentanaDelSilenciador = 300;

    private readonly EnvolventeCw _marca;
    private readonly EnvolventeCw _espacio;
    private readonly LectorDeBaudot _lector;
    private readonly List<string> _pendiente = [];
    private readonly double _umbralDb;
    private readonly double _subidaPorCuadro;

    // Ventana corta (una fracción del bit): suaviza lo justo para decidir qué tono gana sin
    // mezclarse con el bit siguiente. Se apoya en que el silenciador ya ha dejado pasar solo
    // tramos con señal de verdad.
    private readonly double[] _historialMarca;
    private readonly double[] _historialEspacio;
    private int _indiceDelHistorial;
    private double _sumaMarca;
    private double _sumaEspacio;

    // Ventana larga (el silenciador): la potencia combinada de los dos tonos, promediada sobre
    // muchos bits, para que un bit suelto de ruido no abra nada (ver el porqué en la clase).
    private readonly double[] _historialDelSilenciador;
    private int _indiceDelSilenciador;
    private double _sumaDelSilenciador;
    private double _pisoDb = double.NaN;
    private double _ultimaDb = double.NaN;
    private int _validosSeguidos;

    /// <summary>Monta el canal.</summary>
    /// <param name="frecuenciaDeMuestreo">Muestras por segundo del audio que llega.</param>
    /// <param name="tonoDeMarcaHz">Tono de marca que se sigue.</param>
    /// <param name="parametros">Baudios, desplazamiento, parada e inversión.</param>
    /// <param name="anchoDelFiltroHz">
    /// Ancho de cada filtro a −3 dB. 45 Hz por omisión: con 170 Hz de desplazamiento, el primer
    /// cero del filtro (a <c>fs/_largo</c>, bastante más allá del propio ancho a −3 dB) cae mucho
    /// antes de llegar al tono vecino, así que marca y espacio quedan de verdad separados y no se
    /// contaminan el uno al otro con ruido de banda ancha; con 45,45 baudios el bit ocupa unos 45 Hz
    /// de espectro (Carson), así que 45 Hz de filtro no recorta la propia señal. Con 850 Hz de
    /// desplazamiento se puede abrir bastante más sin perder esa separación.
    /// </param>
    /// <param name="umbralDb">Lo que tiene que destacar la señal sobre el piso de ruido para abrir el silenciador.</param>
    public CanalRtty(int frecuenciaDeMuestreo, double tonoDeMarcaHz, ParametrosRtty parametros, double anchoDelFiltroHz = 45, double umbralDb = 14)
    {
        ArgumentNullException.ThrowIfNull(parametros);
        Parametros = parametros.Acotado();
        TonoDeMarcaHz = tonoDeMarcaHz;
        _marca = new EnvolventeCw(frecuenciaDeMuestreo, tonoDeMarcaHz, anchoDelFiltroHz);
        _espacio = new EnvolventeCw(frecuenciaDeMuestreo, TonoDeEspacioHz(tonoDeMarcaHz, Parametros), anchoDelFiltroHz);
        _umbralDb = umbralDb;
        _subidaPorCuadro = 1 - Math.Exp(-EnvolventeCw.MilisegundosPorCuadro / ConstanteDeSubidaMs);
        var framesPorBit = Parametros.DuracionDelBit * 1000 / EnvolventeCw.MilisegundosPorCuadro;
        var ventana = Math.Max(1, (int)Math.Round(0.4 * framesPorBit));
        _historialMarca = new double[ventana];
        _historialEspacio = new double[ventana];
        var ventanaDelSilenciador = Math.Max(1, (int)Math.Round(MilisegundosDeLaVentanaDelSilenciador / EnvolventeCw.MilisegundosPorCuadro));
        _historialDelSilenciador = new double[ventanaDelSilenciador];
        _lector = new LectorDeBaudot(Parametros);
        _lector.Caracter += AlDecodificar;
    }

    /// <summary>Salta con cada carácter ya enganchado (en el hilo del audio).</summary>
    public event Action<string>? Texto;

    /// <summary>Tono de marca que se sigue.</summary>
    public double TonoDeMarcaHz { get; private set; }

    /// <summary>Los parámetros en uso.</summary>
    public ParametrosRtty Parametros { get; private set; }

    /// <summary>El canal está enganchado a una señal.</summary>
    public bool Enganchado { get; private set; }

    /// <summary>Lo que destaca la señal sobre el piso de ruido (dB): la medida de nivel del canal.</summary>
    public double SeparacionDb => double.IsNaN(_pisoDb) ? 0 : Math.Max(0, _ultimaDb - _pisoDb);

    /// <summary>Juego Baudot en el que está (LETRAS/CIFRAS), con USOS aplicado.</summary>
    public JuegoBaudot Juego => _lector.Juego;

    /// <summary>Mueve el canal a otro tono de marca sin perder lo que lleva.</summary>
    public void Afinar(double tonoDeMarcaHz)
    {
        TonoDeMarcaHz = tonoDeMarcaHz;
        _marca.Afinar(tonoDeMarcaHz);
        _espacio.Afinar(TonoDeEspacioHz(tonoDeMarcaHz, Parametros));
    }

    /// <summary>Cambia los parámetros (ancho aparte: pásalo de nuevo al construir si hace falta otro).</summary>
    public void Configurar(ParametrosRtty parametros)
    {
        ArgumentNullException.ThrowIfNull(parametros);
        Parametros = parametros.Acotado();
        _espacio.Afinar(TonoDeEspacioHz(TonoDeMarcaHz, Parametros));
        _lector.Configurar(Parametros);
    }

    /// <summary>Una muestra de audio (ya a la frecuencia del canal).</summary>
    public void Anadir(float x)
    {
        var completoMarca = _marca.Anadir(x, out var potenciaMarca);
        var completoEspacio = _espacio.Anadir(x, out var potenciaEspacio);
        if (!completoMarca || !completoEspacio) return; // completan a la vez: mismo fs y mismo cuadro

        // Ventana corta: quién gana entre marca y espacio, para la decisión bit a bit.
        var ventana = _historialMarca.Length;
        _sumaMarca += potenciaMarca - _historialMarca[_indiceDelHistorial];
        _sumaEspacio += potenciaEspacio - _historialEspacio[_indiceDelHistorial];
        _historialMarca[_indiceDelHistorial] = potenciaMarca;
        _historialEspacio[_indiceDelHistorial] = potenciaEspacio;
        _indiceDelHistorial = (_indiceDelHistorial + 1) % ventana;

        // Ventana larga: si hay señal de verdad o no (el silenciador).
        var ventanaDelSilenciador = _historialDelSilenciador.Length;
        var combinada = potenciaMarca + potenciaEspacio;
        _sumaDelSilenciador += combinada - _historialDelSilenciador[_indiceDelSilenciador];
        _historialDelSilenciador[_indiceDelSilenciador] = combinada;
        _indiceDelSilenciador = (_indiceDelSilenciador + 1) % ventanaDelSilenciador;

        var db = 10 * Math.Log10((_sumaDelSilenciador / ventanaDelSilenciador) + 1e-15);
        _ultimaDb = db;
        if (double.IsNaN(_pisoDb) || db < _pisoDb) _pisoDb = db; // el ruido de verdad se reconoce al momento
        else _pisoDb += (db - _pisoDb) * _subidaPorCuadro; // una señal sostenida solo arrastra el piso muy despacio

        var abierto = db - _pisoDb > _umbralDb;
        // Sin señal por encima del piso, se da reposo (marca): así el lector nunca arranca una
        // trama con puro ruido (ver el porqué en la clase).
        var marca = !abierto || _sumaMarca >= _sumaEspacio;
        _lector.Paso(marca);
    }

    /// <summary>Un bloque de audio de recepción.</summary>
    public void Anadir(ReadOnlySpan<float> muestras)
    {
        foreach (var x in muestras) Anadir(x);
    }

    /// <summary>Olvida lo enganchado y lo pendiente; vuelve a buscar desde cero.</summary>
    public void Reiniciar()
    {
        Enganchado = false;
        _validosSeguidos = 0;
        _pendiente.Clear();
        _lector.Reiniciar();
        _pisoDb = double.NaN;
        _ultimaDb = double.NaN;
        Array.Clear(_historialMarca);
        Array.Clear(_historialEspacio);
        _sumaMarca = 0;
        _sumaEspacio = 0;
        _indiceDelHistorial = 0;
        Array.Clear(_historialDelSilenciador);
        _sumaDelSilenciador = 0;
        _indiceDelSilenciador = 0;
    }

    private static double TonoDeEspacioHz(double tonoDeMarcaHz, ParametrosRtty p) =>
        p.Invertido ? tonoDeMarcaHz + p.DesplazamientoHz : tonoDeMarcaHz - p.DesplazamientoHz;

    private void AlDecodificar(bool valido, string texto)
    {
        if (!valido)
        {
            _validosSeguidos = 0;
            if (!Enganchado) _pendiente.Clear();
            return;
        }

        _validosSeguidos++;
        if (Enganchado)
        {
            if (texto.Length > 0) Texto?.Invoke(texto);
            return;
        }

        if (texto.Length > 0) _pendiente.Add(texto);
        if (_validosSeguidos < CaracteresParaEngancharse) return;

        Enganchado = true;
        foreach (var t in _pendiente) Texto?.Invoke(t);
        _pendiente.Clear();
    }
}
