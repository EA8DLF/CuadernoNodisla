using System.Text;

namespace Nodisla.Cuaderno.Integraciones.Cluster;

/// <summary>
/// Convierte el chorro de bytes de un cluster en lineas de texto limpias.
/// </summary>
/// <remarks>
/// Un cluster habla Telnet crudo: entre el texto viajan secuencias de control IAC
/// (negociacion de opciones), caracteres de control sueltos y, de vez en cuando, basura de
/// una conexion que se esta cayendo. Ademas una linea puede llegar partida entre dos
/// lecturas del socket, o pegada a la siguiente. Todo eso se resuelve aqui, para que el
/// analizador de anuncios reciba solo lineas de texto.
///
/// Las secuencias IAC se descartan sin contestarlas. Los clusters reales no negocian nada
/// que importe y un programa que conteste a ciegas puede acabar en un ida y vuelta infinito
/// con un servidor mal hecho.
/// </remarks>
internal sealed class FiltroTelnet
{
    /// <summary>Byte que abre una secuencia de control Telnet (<c>Interpret As Command</c>).</summary>
    private const byte Iac = 255;

    private const byte SubnegociacionInicio = 250;
    private const byte SubnegociacionFin = 240;
    private const byte OpcionWill = 251;
    private const byte OpcionWont = 252;
    private const byte OpcionDo = 253;
    private const byte OpcionDont = 254;

    /// <summary>
    /// Tope de bytes de una linea. Un anuncio no pasa de 120 caracteres; si algo llega mas
    /// largo es que el servidor no manda saltos de linea, y hay que cortar para no crecer
    /// sin limite.
    /// </summary>
    private const int MaximoDeLinea = 2048;

    private enum Estado
    {
        Texto,
        TrasIac,
        EsperandoOpcion,
        EnSubnegociacion,
        EnSubnegociacionTrasIac,
    }

    private readonly List<byte> _linea = new(128);
    private readonly Queue<string> _completas = new();
    private Estado _estado = Estado.Texto;
    private bool _esperandoSalto;

    /// <summary>Texto recibido que todavia no termina en salto de linea.</summary>
    /// <remarks>
    /// Los avisos de un cluster (<c>login:</c>, <c>password:</c>) llegan sin salto de linea,
    /// asi que hay que poder mirarlos antes de que la linea se cierre.
    /// </remarks>
    public string Pendiente => _linea.Count == 0 ? string.Empty : Decodificar(_linea);

    /// <summary>Numero de lineas completas a la espera de ser leidas.</summary>
    public int LineasPendientes => _completas.Count;

    /// <summary>
    /// Se queda con lo que hay pendiente y lo saca del buffer.
    /// </summary>
    /// <remarks>
    /// Los avisos del nodo (<c>login:</c>) no terminan en salto de linea: si no se sacan al
    /// contestarlos, se quedan pegados delante de la siguiente linea y convierten un anuncio
    /// perfectamente valido en una linea que no hay quien analice.
    /// </remarks>
    public string TomarPendiente()
    {
        if (_linea.Count == 0) return string.Empty;
        var texto = Decodificar(_linea);
        _linea.Clear();
        return texto;
    }

    /// <summary>Mete bytes recien leidos del socket.</summary>
    public void Anadir(ReadOnlySpan<byte> bytes)
    {
        foreach (var b in bytes) Procesar(b);
    }

    /// <summary>Saca la siguiente linea completa, si la hay.</summary>
    public bool TryLeerLinea(out string linea)
    {
        if (_completas.Count > 0)
        {
            linea = _completas.Dequeue();
            return true;
        }
        linea = string.Empty;
        return false;
    }

    /// <summary>
    /// Olvida lo recibido. Se llama al reconectar: la media linea que quedo de la conexion
    /// anterior no se puede pegar al principio de la nueva, porque saldria un anuncio falso.
    /// </summary>
    public void Reiniciar()
    {
        _linea.Clear();
        _completas.Clear();
        _estado = Estado.Texto;
        _esperandoSalto = false;
    }

    private void Procesar(byte b)
    {
        switch (_estado)
        {
            case Estado.TrasIac:
                switch (b)
                {
                    case Iac:
                        // IAC IAC es un 255 literal dentro del texto.
                        _estado = Estado.Texto;
                        AnadirLiteral((char)Iac);
                        break;
                    case OpcionWill or OpcionWont or OpcionDo or OpcionDont:
                        _estado = Estado.EsperandoOpcion;
                        break;
                    case SubnegociacionInicio:
                        _estado = Estado.EnSubnegociacion;
                        break;
                    default:
                        // Orden de un solo byte: se descarta entera.
                        _estado = Estado.Texto;
                        break;
                }
                return;

            case Estado.EsperandoOpcion:
                _estado = Estado.Texto;
                return;

            case Estado.EnSubnegociacion:
                if (b == Iac) _estado = Estado.EnSubnegociacionTrasIac;
                return;

            case Estado.EnSubnegociacionTrasIac:
                _estado = b == SubnegociacionFin ? Estado.Texto : Estado.EnSubnegociacion;
                return;

            case Estado.Texto:
            default:
                break;
        }

        if (b == Iac)
        {
            _estado = Estado.TrasIac;
            return;
        }

        switch (b)
        {
            case (byte)'\r':
                Cerrar();
                _esperandoSalto = true;
                return;

            case (byte)'\n':
                // El salto que sigue a un retorno ya cerro la linea.
                if (_esperandoSalto) _esperandoSalto = false;
                else Cerrar();
                return;
        }

        _esperandoSalto = false;
        if (b == '\t')
        {
            AnadirLiteral(' ');
            return;
        }
        // Caracteres de control sueltos (campana, nulos, escapes): no son texto.
        if (b < 0x20 || b == 0x7F) return;

        _linea.Add(b);
        if (_linea.Count >= MaximoDeLinea) Cerrar();
    }

    private void AnadirLiteral(char c) => _linea.Add((byte)c);

    private void Cerrar()
    {
        if (_linea.Count == 0) return;
        var texto = Decodificar(_linea).TrimEnd();
        _linea.Clear();
        if (texto.Length > 0) _completas.Enqueue(texto);
    }

    private static readonly UTF8Encoding Utf8Estricto =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Convierte bytes en texto. Los clusters escupen ASCII, pero algun nodo escribe los
    /// acentos en UTF-8 y otro en la pagina de codigos de Windows; se prueba UTF-8 estricto
    /// y se cae a Windows-1252, que nunca falla.
    /// </summary>
    private static string Decodificar(List<byte> bytes)
    {
        var crudos = bytes.ToArray();
        try
        {
            return Utf8Estricto.GetString(crudos);
        }
        catch (DecoderFallbackException)
        {
            var destino = new char[crudos.Length];
            for (var i = 0; i < crudos.Length; i++)
            {
                var b = crudos[i];
                destino[i] = b < 0x80 ? (char)b
                    : b < 0xA0 ? AltosDeWindows1252[b - 0x80]
                    : (char)b;
            }
            return new string(destino);
        }
    }

    /// <summary>
    /// Los 32 codigos que Windows-1252 asigna de forma distinta a ISO-8859-1. El resto de
    /// valores altos coinciden con Unicode punto por punto.
    /// </summary>
    private static readonly char[] AltosDeWindows1252 =
    [
        '\u20AC', '\u0081', '\u201A', '\u0192', '\u201E', '\u2026', '\u2020', '\u2021',
        '\u02C6', '\u2030', '\u0160', '\u2039', '\u0152', '\u008D', '\u017D', '\u008F',
        '\u0090', '\u2018', '\u2019', '\u201C', '\u201D', '\u2022', '\u2013', '\u2014',
        '\u02DC', '\u2122', '\u0161', '\u203A', '\u0153', '\u009D', '\u017E', '\u0178',
    ];
}
