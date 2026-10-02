using System.Text;

namespace Nodisla.Cuaderno.Modos.Cw;

/// <summary>
/// De la llave a los caracteres: mide marcas y espacios, separa puntos de rayas, sigue la
/// velocidad y arma cada carácter.
/// </summary>
/// <remarks>
/// <para>
/// <b>Antirrebote.</b> Una marca o un hueco más cortos que un tercio de punto (y nunca más de
/// 12 ms) no cuentan: el hueco se suelda a la marca y la marca se suma al espacio.
/// </para>
/// <para>
/// <b>Punto o raya.</b> Se llevan dos medias, la del punto y la de la raya, y la frontera es la
/// media de las dos: así vale para la mano que hace rayas de 2,5 puntos y para la que las hace de
/// 4. Con las últimas doce marcas se hace además un reparto en dos grupos (el corte que deja
/// menos dispersión en logaritmos); si los dos grupos están claros (rayas de 2 a 5 puntos), las
/// medias saltan hacia ellos. Eso es lo que sigue un cambio brusco de velocidad en pocas marcas.
/// </para>
/// <para>
/// <b>Espacios.</b> El hueco entre elementos se mide también. Fin de carácter con un espacio de
/// dos huecos, fin de palabra con cinco (acotados entre 1,4 y 2,6 puntos y entre 4 y 8 puntos).
/// El carácter se escribe en cuanto el espacio pasa la frontera, sin esperar a la siguiente marca.
/// </para>
/// <para>
/// <b>Confianza.</b> Cada marca que cae cerca de un punto o una raya, cada hueco que cae cerca de
/// 1, 3 o 7 puntos y cada carácter que está en la tabla suman; lo que no, resta. Es una media
/// móvil de 0 a 1. Con ruido se queda baja.
/// </para>
/// </remarks>
public sealed class LectorDeTiempos
{
    private const double Ms = EnvolventeCw.MilisegundosPorCuadro;
    private const int MarcasRecordadas = 12;

    private readonly StringBuilder _elementos = new();
    private readonly double[] _recientes = new double[MarcasRecordadas];
    private int _nRecientes;
    private int _iRecientes;

    private double _punto;
    private double _raya;
    private double _hueco;

    private bool _enMarca;
    private double _marca;
    private double _espacio;
    private double _pendiente;
    private bool _caracterCerrado = true;
    private bool _palabraCerrada = true;

    /// <summary>Monta el lector.</summary>
    /// <param name="wpmMinima">Velocidad mínima que se sigue.</param>
    /// <param name="wpmMaxima">Velocidad máxima que se sigue.</param>
    /// <param name="wpmInicial">Velocidad de partida.</param>
    public LectorDeTiempos(double wpmMinima = 5, double wpmMaxima = 60, double wpmInicial = 20)
    {
        PuntoMinimoMs = 1200 / wpmMaxima;
        PuntoMaximoMs = 1200 / wpmMinima;
        Velocidad(wpmInicial);
    }

    /// <summary>Salta con cada carácter (o prosigno) y con cada espacio de palabra («&#160;»).</summary>
    public event Action<string>? Simbolo;

    /// <summary>Salta con cada código que no está en la tabla.</summary>
    public event Action<string>? CodigoDesconocido;

    /// <summary>Punto más corto que se admite (ms): el de la velocidad máxima.</summary>
    public double PuntoMinimoMs { get; set; }

    /// <summary>Punto más largo que se admite (ms): el de la velocidad mínima.</summary>
    public double PuntoMaximoMs { get; set; }

    /// <summary>
    /// La señal destaca tanto que el ruido no parte marcas: todas enseñan la velocidad. Lo pone
    /// quien sabe el nivel (el canal).
    /// </summary>
    public bool SenalFuerte { get; set; }

    /// <summary>Duración estimada del punto (ms).</summary>
    public double PuntoMs => _punto;

    /// <summary>
    /// Duración media de las marcas cortas de las últimas doce, sin acotar a la franja (ms); nulo
    /// mientras no hay reparto claro en cortas y largas. Dice a qué velocidad va la señal aunque
    /// la estimación esté pegada a un límite.
    /// </summary>
    public double? PuntoMedidoMs { get; private set; }

    /// <summary>Duración estimada de la raya (ms).</summary>
    public double RayaMs => _raya;

    /// <summary>Velocidad estimada en palabras por minuto (PARIS).</summary>
    public double Wpm => 1200 / Unidad;

    /// <summary>Confianza en lo que se está leyendo, de 0 a 1.</summary>
    public double Confianza { get; private set; }

    /// <summary>Caracteres válidos seguidos, sin ningún código desconocido en medio.</summary>
    public int ValidosSeguidos { get; private set; }

    /// <summary>Tiempo que lleva la llave arriba (ms).</summary>
    public double SilencioMs => _enMarca ? 0 : _espacio;

    /// <summary>La llave está abajo (ya pasado el antirrebote).</summary>
    public bool EnMarca => _enMarca;

    /// <summary>Hay elementos de un carácter que todavía no se ha cerrado.</summary>
    public bool CaracterAMedias => _elementos.Length > 0;

    private double Unidad => (_punto + Math.Clamp(_hueco, 0.6 * _punto, 1.4 * _punto) + (_raya / 3)) / 3;

    private double Antirrebote => SenalFuerte ? Math.Clamp(0.2 * _punto, 4, 8) : Math.Clamp(0.4 * _punto, 4, 30);

    /// <summary>Pone la velocidad de partida.</summary>
    public void Velocidad(double wpm)
    {
        _punto = Math.Clamp(1200 / wpm, PuntoMinimoMs, PuntoMaximoMs);
        _raya = 3 * _punto;
        _hueco = _punto;
    }

    /// <summary>Un cuadro de envolvente.</summary>
    /// <param name="marca">Llave abajo en este cuadro.</param>
    public void Paso(bool marca)
    {
        if (_enMarca)
        {
            if (marca)
            {
                _marca += _pendiente + Ms;
                _pendiente = 0;
                return;
            }

            _pendiente += Ms;
            if (_pendiente < Antirrebote) return;

            _enMarca = false;
            CerrarMarca(_marca);
            _espacio = _pendiente;
            _pendiente = 0;
            MirarElEspacio();
            return;
        }

        if (!marca)
        {
            _espacio += _pendiente + Ms;
            _pendiente = 0;
            MirarElEspacio();
            return;
        }

        _pendiente += Ms;
        if (_pendiente < Antirrebote) return;

        AbrirMarca(_espacio);
        _enMarca = true;
        _marca = _pendiente;
        _pendiente = 0;
    }

    /// <summary>Cierra lo que haya a medias (fin de la señal).</summary>
    public void Vaciar()
    {
        if (_enMarca)
        {
            _enMarca = false;
            CerrarMarca(_marca);
            _espacio = 0;
        }

        if (_elementos.Length > 0) EmitirCaracter();
    }

    /// <summary>Olvida lo leído y la confianza, pero no la velocidad.</summary>
    public void Reiniciar()
    {
        _elementos.Clear();
        _enMarca = false;
        _marca = _espacio = _pendiente = 0;
        _caracterCerrado = _palabraCerrada = true;
        Confianza = 0;
        ValidosSeguidos = 0;
        _nRecientes = _iRecientes = 0;
    }

    private void AbrirMarca(double espacio)
    {
        if (_caracterCerrado || _elementos.Length == 0)
        {
            // Hueco entre caracteres o palabras: la confianza mira si cae cerca de 3 o de 7.
            if (espacio < 30 * _punto) Puntuar(Cerca(espacio, 3 * Unidad, 0.5) || espacio >= 5 * Unidad);
        }
        else
        {
            // Hueco dentro de un carácter: se aprende.
            _hueco += 0.2 * (espacio - _hueco);
            _hueco = Math.Clamp(_hueco, 0.4 * _punto, 2.0 * _punto);
            Puntuar(Cerca(espacio, _punto, 0.5));
        }

        _caracterCerrado = false;
        _palabraCerrada = false;
    }

    private void CerrarMarca(double d)
    {
        // Una portadora (afinando, o una señal que no es telegrafía) no es una raya.
        if (d > Math.Max(6 * _raya, 1500))
        {
            _elementos.Clear();
            Puntuar(false);
            Puntuar(false);
            ValidosSeguidos = 0;
            return;
        }

        // Las marcas mucho más cortas que el punto son, casi siempre, trozos de una marca que el
        // ruido ha partido: cuentan como punto, pero no enseñan la velocidad (si lo hicieran, la
        // velocidad estimada se dispararía, el suavizado se acortaría y el ruido partiría más
        // marcas). Un cambio real de velocidad se sigue igual, a pasos.
        var deFiar = SenalFuerte || d >= 0.45 * _punto;
        if (deFiar)
        {
            Recordar(d);
            Reestimar();
        }

        var frontera = (_punto + _raya) / 2;
        var esRaya = d >= frontera;

        if (esRaya)
        {
            _raya += 0.2 * (d - _raya);
        }
        else
        {
            _punto += 0.2 * ((SenalFuerte ? d : Math.Max(d, 0.6 * _punto)) - _punto);
        }

        Acotar();
        Puntuar(Cerca(d, esRaya ? _raya : _punto, 0.45));
        _elementos.Append(esRaya ? '-' : '.');
    }

    private void MirarElEspacio()
    {
        var u = Unidad;
        var finDeCaracter = Math.Clamp(2.0 * _hueco, 1.4 * u, 2.6 * u);
        var finDePalabra = Math.Clamp(5.0 * _hueco, 4.0 * u, 8.0 * u);

        if (!_caracterCerrado && _espacio >= finDeCaracter)
        {
            _caracterCerrado = true;
            if (_elementos.Length > 0) EmitirCaracter();
        }

        if (!_palabraCerrada && _espacio >= finDePalabra)
        {
            _palabraCerrada = true;
            Simbolo?.Invoke(" ");
        }
    }

    private void EmitirCaracter()
    {
        var codigo = _elementos.ToString();
        _elementos.Clear();
        if (TablaMorse.TryDecodificar(codigo, out var texto))
        {
            ValidosSeguidos++;
            Puntuar(true);
            Simbolo?.Invoke(texto);
            return;
        }

        ValidosSeguidos = 0;
        Puntuar(false);
        Puntuar(false);
        CodigoDesconocido?.Invoke(codigo);
    }

    private void Recordar(double d)
    {
        _recientes[_iRecientes] = d;
        _iRecientes = (_iRecientes + 1) % MarcasRecordadas;
        if (_nRecientes < MarcasRecordadas) _nRecientes++;
    }

    /// <summary>
    /// Reparte las marcas recientes en cortas y largas por el corte de menor dispersión (en
    /// logaritmos) y, si el reparto es claro, acerca las medias a los dos grupos.
    /// </summary>
    private void Reestimar()
    {
        if (_nRecientes < 6) return;
        Span<double> l = stackalloc double[_nRecientes];
        for (var i = 0; i < _nRecientes; i++) l[i] = Math.Log(_recientes[i]);
        l.Sort();

        var mejor = double.MaxValue;
        var corte = -1;
        for (var k = 1; k < l.Length; k++)
        {
            var dispersion = Dispersion(l[..k]) + Dispersion(l[k..]);
            if (dispersion < mejor)
            {
                mejor = dispersion;
                corte = k;
            }
        }

        if (corte <= 0) return;
        var cortas = Math.Exp(Media(l[..corte]));
        var largas = Math.Exp(Media(l[corte..]));
        var razon = largas / cortas;
        if (razon is >= 2.0 and <= 5.0 && corte >= 2 && l.Length - corte >= 2)
        {
            PuntoMedidoMs = cortas;
            _punto += 0.4 * (cortas - _punto);
            _raya += 0.4 * (largas - _raya);
            return;
        }

        // Todas parecidas (una racha de solo puntos o solo rayas): si no casan con ninguna de
        // las dos medias, es otra velocidad. Se decide por cuál de las dos queda más cerca.
        var todas = Math.Exp(Media(l));
        if (Math.Exp(l[^1] - l[0]) < 1.8)
        {
            var aPunto = Math.Abs(Math.Log(todas / _punto));
            var aRaya = Math.Abs(Math.Log(todas / _raya));
            if (Math.Min(aPunto, aRaya) > Math.Log(1.6))
            {
                if (aPunto < aRaya) _punto += 0.5 * (todas - _punto);
                else _raya += 0.5 * (todas - _raya);
            }
        }
    }

    private void Acotar()
    {
        _punto = Math.Clamp(_punto, PuntoMinimoMs, PuntoMaximoMs);
        _raya = Math.Clamp(_raya, 2.0 * _punto, 5.0 * _punto);
        if (_raya > 5.0 * _punto) _punto = _raya / 5.0;
        _hueco = Math.Clamp(_hueco, 0.4 * _punto, 2.0 * _punto);
    }

    private void Puntuar(bool bien) => Confianza += 0.12 * ((bien ? 1.0 : 0.0) - Confianza);

    private static bool Cerca(double d, double referencia, double tolerancia) =>
        Math.Abs(Math.Log(d / referencia)) < tolerancia;

    private static double Media(ReadOnlySpan<double> v)
    {
        double s = 0;
        foreach (var x in v) s += x;
        return s / v.Length;
    }

    private static double Dispersion(ReadOnlySpan<double> v)
    {
        var m = Media(v);
        double s = 0;
        foreach (var x in v) s += (x - m) * (x - m);
        return s;
    }
}
