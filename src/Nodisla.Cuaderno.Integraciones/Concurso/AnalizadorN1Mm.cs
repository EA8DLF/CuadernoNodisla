using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Integraciones.Concurso;

/// <summary>
/// Lee los mensajes XML que N1MM+ reparte por UDP.
/// </summary>
/// <remarks>
/// Cada datagrama trae un documento entero, sin trocear, con una etiqueta raiz que dice de que
/// va: <c>RadioInfo</c>, <c>contactinfo</c>, <c>contactreplace</c> o <c>contactdelete</c>. Los
/// campos van como elementos hijos, y N1MM+ anade unos cuantos en cada version, asi que aqui
/// se coge lo que interesa y se guarda el resto en crudo en vez de rechazar lo que no se
/// reconoce. Las frecuencias vienen en decenas de hercios, no en hercios: 712345 son 7,12345
/// megahercios.
/// </remarks>
public static class AnalizadorN1Mm
{
    /// <summary>Formato de las marcas de tiempo de N1MM+, que van en UTC.</summary>
    private const string FormatoDeFecha = "yyyy-MM-dd HH:mm:ss";

    /// <summary>Lee un datagrama de N1MM+. Devuelve nulo si no se reconoce.</summary>
    /// <param name="xml">Texto del datagrama.</param>
    /// <param name="motivo">Explicacion de por que no se entendio.</param>
    public static MensajeDeConcurso? Analizar(string? xml, out string? motivo)
    {
        motivo = null;
        if (string.IsNullOrWhiteSpace(xml))
        {
            motivo = "El datagrama viene vacio.";
            return null;
        }

        XElement? raiz;
        try
        {
            // Los datagramas traen su declaracion XML delante, asi que se lee como documento
            // entero y no como fragmento: un XElement suelto no siempre la digiere.
            raiz = XDocument.Parse(xml, LoadOptions.None).Root;
        }
        catch (Exception e) when (e is XmlException or InvalidOperationException or ArgumentException)
        {
            motivo = $"El datagrama no es XML valido: {e.Message}";
            return null;
        }

        if (raiz is null)
        {
            motivo = "El datagrama no trae ningun elemento.";
            return null;
        }

        var campos = LeerCampos(raiz);

        return raiz.Name.LocalName.ToLowerInvariant() switch
        {
            "radioinfo" => LeerRadio(campos),
            "contactinfo" => LeerContacto(campos, AccionSobreContacto.Anadido),
            "contactreplace" => LeerContacto(campos, AccionSobreContacto.Reemplazado),
            "contactdelete" => LeerBorrado(campos),
            _ => Desconocido(raiz.Name.LocalName, out motivo),
        };
    }

    private static MensajeDeConcurso? Desconocido(string etiqueta, out string motivo)
    {
        motivo = $"La etiqueta «{etiqueta}» no es de las que se saben leer.";
        return null;
    }

    private static Dictionary<string, string> LeerCampos(XElement raiz)
    {
        var campos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hijo in raiz.Elements())
        {
            // Si un campo se repite gana el ultimo, que es lo que hace el propio N1MM+.
            campos[hijo.Name.LocalName] = hijo.Value.Trim();
        }
        return campos;
    }

    private static EstadoDeRadioConcurso LeerRadio(Dictionary<string, string> c) =>
        new(
            Texto(c, "app"),
            Texto(c, "StationName"),
            Entero(c, "RadioNr") ?? 1,
            LeerFrecuencia(c, "Freq"),
            LeerFrecuencia(c, "TXFreq"),
            Texto(c, "Mode"),
            Texto(c, "OpCall"),
            Logico(c, "IsRunning") ?? false,
            Logico(c, "IsTransmitting") ?? false,
            Logico(c, "IsSplit") ?? false,
            Texto(c, "RadioName"),
            Texto(c, "Antenna"));

    private static ContactoDeConcurso LeerContacto(Dictionary<string, string> c, AccionSobreContacto accion) =>
        new(
            accion,
            Texto(c, "app"),
            Texto(c, "StationName"),
            Texto(c, "ID"),
            Fecha(c, "timestamp"),
            Indicativo.Crudo(Texto(c, "call")),
            Indicativo.Crudo(Texto(c, "mycall")))
        {
            Concurso = Texto(c, "contestname"),
            NumeroDeConcurso = Entero(c, "contestnr"),
            FrecuenciaRx = LeerFrecuencia(c, "rxfreq"),
            FrecuenciaTx = LeerFrecuencia(c, "txfreq"),
            Modo = Texto(c, "mode"),
            Operador = Texto(c, "operator"),
            InformeEnviado = Texto(c, "snt"),
            SerieEnviada = Entero(c, "sntnr"),
            InformeRecibido = Texto(c, "rcv"),
            SerieRecibida = Entero(c, "rcvnr"),
            Intercambio = Texto(c, "exchange1"),
            Seccion = Texto(c, "section"),
            Locator = Localizador(c, "gridsquare"),
            Comentario = Texto(c, "comment"),
            Qth = Texto(c, "qth"),
            Nombre = Texto(c, "name"),
            Potencia = Real(c, "power"),
            PrefijoDePais = Texto(c, "countryprefix"),
            PrefijoWpx = Texto(c, "wpxprefix"),
            Continente = Texto(c, "continent"),
            Zona = Entero(c, "zone"),
            Puntos = Entero(c, "points"),
            NumeroDeRadio = Entero(c, "radionr"),
            EnLlamada = Logico(c, "IsRunQSO"),
            CallAnterior = Indicativo.Crudo(Texto(c, "oldcall")),
            InstanteAnteriorUtc = c.ContainsKey("oldtimestamp") ? Fecha(c, "oldtimestamp") : (DateTimeOffset?)null,
            Campos = c,
        };

    private static BorradoDeConcurso LeerBorrado(Dictionary<string, string> c) =>
        new(
            Texto(c, "app"),
            Texto(c, "StationName"),
            Texto(c, "ID"),
            Fecha(c, "timestamp"),
            Indicativo.Crudo(Texto(c, "call")),
            Entero(c, "contestnr"));

    /// <summary>
    /// Arma el QSO del cuaderno a partir del contacto de concurso.
    /// </summary>
    /// <param name="contacto">Contacto tal y como lo anuncio N1MM+.</param>
    public static Qso AQso(ContactoDeConcurso contacto)
    {
        ArgumentNullException.ThrowIfNull(contacto);

        var tx = contacto.FrecuenciaTx.EsCero ? contacto.FrecuenciaRx : contacto.FrecuenciaTx;
        var modo = LeerModo(contacto.Modo);

        var qso = new Qso
        {
            Call = contacto.Call,
            Freq = tx,
            Band = Banda.DesdeFrecuencia(tx),
            Mode = modo,
            InicioUtc = contacto.InstanteUtc.ToUniversalTime(),
            RstSent = Informe.Parse(contacto.InformeEnviado),
            RstRcvd = Informe.Parse(contacto.InformeRecibido),
            StationCallsign = contacto.MiIndicativo,
            Operator = contacto.Operador,
            ContestId = contacto.Concurso,
            Stx = contacto.SerieEnviada,
            Srx = contacto.SerieRecibida,
            SrxString = string.IsNullOrWhiteSpace(contacto.Intercambio) ? contacto.Seccion : contacto.Intercambio,
            Gridsquare = contacto.Locator,
            Comentario = contacto.Comentario,
            Qth = contacto.Qth,
            Name = contacto.Nombre,
            TxPwr = contacto.Potencia,
            Cont = contacto.Continente,
            Pfx = contacto.PrefijoWpx,
            Cqz = contacto.Zona,
            Origen = "N1MM+",
        };

        // Solo se declara frecuencia de recepcion si de verdad se trabajo en dos frecuencias.
        if (!contacto.FrecuenciaRx.EsCero && contacto.FrecuenciaRx != tx)
        {
            qso.FreqRx = contacto.FrecuenciaRx;
            qso.BandRx = Banda.DesdeFrecuencia(contacto.FrecuenciaRx);
        }

        return qso;
    }

    /// <summary>Traduce el nombre del modo de N1MM+ al par ADIF.</summary>
    /// <param name="nombre">Modo tal y como lo nombra N1MM+.</param>
    public static Modo LeerModo(string? nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre)) return Modo.Vacio;
        var v = nombre.Trim().ToUpperInvariant();

        // N1MM+ llama FSK a la RTTY por desplazamiento de frecuencia, que en ADIF es RTTY.
        if (v == "FSK") return Modo.Parse("RTTY");

        return Modo.TryParse(v, null, out var modo) ? modo : Modo.Crudo(v);
    }

    private static string? Texto(Dictionary<string, string> c, string nombre) =>
        c.TryGetValue(nombre, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

    private static int? Entero(Dictionary<string, string> c, string nombre) =>
        Texto(c, nombre) is { } v
        && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n
            : null;

    private static double? Real(Dictionary<string, string> c, string nombre) =>
        Texto(c, nombre) is { } v
        && double.TryParse(v.TrimEnd('W', 'w', ' '), NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
            ? n
            : null;

    private static bool? Logico(Dictionary<string, string> c, string nombre)
    {
        var v = Texto(c, nombre);
        if (v is null) return null;
        if (bool.TryParse(v, out var logico)) return logico;
        return v switch
        {
            "1" or "Y" or "y" or "True" or "TRUE" => true,
            "0" or "N" or "n" or "False" or "FALSE" => false,
            _ => null,
        };
    }

    /// <summary>
    /// Lee una frecuencia de N1MM+, que llega en decenas de hercios y sin separador.
    /// </summary>
    private static Frecuencia LeerFrecuencia(Dictionary<string, string> c, string nombre)
    {
        var v = Texto(c, nombre);
        if (v is null) return Frecuencia.Cero;
        if (!long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var decenas)) return Frecuencia.Cero;
        if (decenas <= 0) return Frecuencia.Cero;
        return Frecuencia.DesdeHercios(decenas * 10);
    }

    private static Locator Localizador(Dictionary<string, string> c, string nombre) =>
        Locator.TryParse(Texto(c, nombre), out var locator) ? locator : Locator.Vacio;

    private static DateTimeOffset Fecha(Dictionary<string, string> c, string nombre)
    {
        var v = Texto(c, nombre);
        if (v is null) return default;

        if (DateTime.TryParseExact(v, FormatoDeFecha, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var exacta))
        {
            return new DateTimeOffset(exacta, TimeSpan.Zero);
        }

        // Alguna version mete milisegundos o cambia el separador; se prueba tambien lo general.
        return DateTime.TryParse(v, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var suelta)
            ? new DateTimeOffset(suelta, TimeSpan.Zero)
            : default;
    }
}
