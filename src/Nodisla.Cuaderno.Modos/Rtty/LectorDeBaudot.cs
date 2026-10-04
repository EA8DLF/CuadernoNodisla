using Nodisla.Cuaderno.Modos.Cw;

namespace Nodisla.Cuaderno.Modos.Rtty;

/// <summary>
/// Recupera el reloj de bits de la línea (arranque/datos/parada) a partir de si cada cuadro es
/// marca o espacio, y arma los caracteres Baudot.
/// </summary>
/// <remarks>
/// <para>
/// A diferencia de la telegrafía, en RTTY <b>no hace falta estimar la velocidad</b>: el bit de
/// arranque siempre dura lo mismo, así que basta con encontrar su flanco de bajada (marca → espacio
/// tras un reposo) y, desde ahí, muestrear cada bit a mitad de su hueco con el tiempo fijo que
/// marcan los baudios configurados. Cada carácter resincroniza el reloj con su propio arranque, así
/// que la deriva de un carácter a otro no se acumula.
/// </para>
/// <para>
/// <b>El bit de parada es la validación.</b> Si al llegar a su instante la línea no está en marca,
/// el carácter se descarta entero: es justo el «enganche» de CW pero gratis, porque lo da el propio
/// protocolo en vez de tener que medirlo.
/// </para>
/// </remarks>
public sealed class LectorDeBaudot
{
    private const double Ms = EnvolventeCw.MilisegundosPorCuadro;

    private double _framesPorBit;
    private readonly double[] _instantes = new double[7]; // 0=arranque, 1..5=datos (LSB..MSB), 6=parada
    private bool _enMarcha;
    private double _tDesdeArranque;
    private int _siguiente;
    private byte _dato;
    private bool _ultimaMarca = true;
    private JuegoBaudot _juego = JuegoBaudot.Letras;

    /// <summary>Monta el lector.</summary>
    /// <param name="parametros">Baudios y bits de parada.</param>
    public LectorDeBaudot(ParametrosRtty parametros) => Configurar(parametros);

    /// <summary>Salta con cada trama completa: si el bit de parada fue válido y, si lo fue, el texto.</summary>
    public event Action<bool, string>? Caracter;

    /// <summary>El juego (LETRAS/CIFRAS) en el que está ahora mismo, con USOS aplicado.</summary>
    public JuegoBaudot Juego => _juego;

    /// <summary>Cambia los tiempos (en el siguiente reposo; si había una trama a medias, se aborta).</summary>
    public void Configurar(ParametrosRtty parametros)
    {
        ArgumentNullException.ThrowIfNull(parametros);
        var p = parametros.Acotado();
        _framesPorBit = p.DuracionDelBit * 1000 / Ms;
        _instantes[0] = 0.5 * _framesPorBit; // mitad del arranque
        for (var k = 0; k < 5; k++) _instantes[k + 1] = (1.5 + k) * _framesPorBit; // mitad de cada dato
        _instantes[6] = (6 * _framesPorBit) + (0.5 * p.BitsDeParada * _framesPorBit); // mitad de la parada
        _enMarcha = false;
    }

    /// <summary>Olvida la trama a medias y el juego (vuelve a LETRAS).</summary>
    public void Reiniciar()
    {
        _enMarcha = false;
        _ultimaMarca = true;
        _juego = JuegoBaudot.Letras;
    }

    /// <summary>Un cuadro: si la línea está en marca o en espacio.</summary>
    public void Paso(bool marca)
    {
        if (!_enMarcha)
        {
            if (_ultimaMarca && !marca)
            {
                // Flanco de bajada tras reposo: posible bit de arranque.
                _enMarcha = true;
                _tDesdeArranque = 0;
                _siguiente = 0;
                _dato = 0;
            }

            _ultimaMarca = marca;
            return;
        }

        _tDesdeArranque += 1;
        _ultimaMarca = marca;

        if (_siguiente > 6 || _tDesdeArranque < _instantes[_siguiente]) return;

        if (_siguiente == 0)
        {
            if (marca)
            {
                // No era un arranque de verdad (ruido de un cuadro): se abandona sin tocar el juego.
                _enMarcha = false;
                return;
            }
        }
        else if (_siguiente <= 5)
        {
            if (marca) _dato |= (byte)(1 << (_siguiente - 1));
        }
        else
        {
            var valido = marca; // la parada tiene que ser marca
            var texto = valido ? TablaBaudot.Decodificar(_dato, ref _juego) : string.Empty;
            Caracter?.Invoke(valido, texto);
            _enMarcha = false;
            return;
        }

        _siguiente++;
    }
}
