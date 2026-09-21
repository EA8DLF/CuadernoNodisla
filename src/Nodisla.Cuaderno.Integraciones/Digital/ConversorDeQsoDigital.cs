using System.Globalization;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Integraciones.Digital;

/// <summary>
/// Convierte el contacto que anuncia el programa de modos digitales en un QSO del cuaderno.
/// </summary>
/// <remarks>
/// El unico campo que hay que pensar es el modo. En ADIF el par correcto es <c>MODE</c> mas
/// <c>SUBMODE</c>, y no coincide con lo que dice el operador: FT8 es modo principal por
/// decision del estandar, pero FT4 es submodo de MFSK, igual que Q65 y JS8. El programa manda
/// siempre el nombre usual, asi que se pasa por <see cref="Modo.TryParse"/> y que el dominio
/// decida donde va cada cosa.
/// </remarks>
public static class ConversorDeQsoDigital
{
    /// <summary>Nombre con el que se marca el origen del contacto en el cuaderno.</summary>
    public static string NombreDeOrigen(DialectoDigital dialecto) => dialecto switch
    {
        DialectoDigital.WsjtX => "WSJT-X",
        DialectoDigital.Jtdx => "JTDX",
        DialectoDigital.Mshv => "MSHV",
        DialectoDigital.Js8Call => "JS8Call",
        _ => "Modos digitales",
    };

    /// <summary>Arma el QSO a partir del mensaje de contacto cerrado.</summary>
    /// <param name="mensaje">Contacto tal y como lo anuncio el programa.</param>
    /// <param name="dialecto">Programa que lo envio, para dejar constancia del origen.</param>
    public static Qso AQso(QsoRegistradoWsjt mensaje, DialectoDigital dialecto)
    {
        ArgumentNullException.ThrowIfNull(mensaje);

        var frecuencia = mensaje.FrecuenciaTxHz == 0
            ? Frecuencia.Cero
            : Frecuencia.DesdeHercios((long)mensaje.FrecuenciaTxHz);

        var modo = LeerModo(mensaje.Modo);

        var inicio = mensaje.InicioUtc == default ? mensaje.FinUtc : mensaje.InicioUtc;
        var fin = mensaje.FinUtc == default ? (DateTimeOffset?)null : mensaje.FinUtc;

        var qso = new Qso
        {
            Call = Indicativo.Crudo(mensaje.IndicativoDx),
            Freq = frecuencia,
            Band = Banda.DesdeFrecuencia(frecuencia),
            Mode = modo,
            InicioUtc = inicio.ToUniversalTime(),
            FinUtc = fin?.ToUniversalTime(),
            RstSent = LeerInforme(mensaje.InformeEnviado, modo),
            RstRcvd = LeerInforme(mensaje.InformeRecibido, modo),
            Comentario = Vacio(mensaje.Comentarios),
            Name = Vacio(mensaje.Nombre),
            StationCallsign = Indicativo.Crudo(mensaje.MiIndicativo),
            Operator = Vacio(mensaje.IndicativoOperador),
            TxPwr = LeerPotencia(mensaje.PotenciaTx),
            PropMode = Vacio(mensaje.ModoDePropagacion),
            StxString = Vacio(mensaje.IntercambioEnviado),
            SrxString = Vacio(mensaje.IntercambioRecibido),
            Origen = NombreDeOrigen(dialecto),
        };

        if (Locator.TryParse(mensaje.LocatorDx, out var suyo)) qso.Gridsquare = suyo;
        if (Locator.TryParse(mensaje.MiLocator, out var mio)) qso.MyGridsquare = mio;

        return qso;
    }

    /// <summary>
    /// Traduce el nombre del modo al par ADIF. Si el catalogo no lo conoce se guarda en crudo:
    /// perder un contacto porque el estandar aun no recogio un modo nuevo seria peor.
    /// </summary>
    public static Modo LeerModo(string? nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre)) return Modo.Vacio;
        return Modo.TryParse(nombre, null, out var modo) ? modo : Modo.Crudo(nombre);
    }

    /// <summary>
    /// Averigua el modo real de una decodificacion.
    /// </summary>
    /// <remarks>
    /// El campo que el protocolo llama «modo» en el aviso de decodificacion no siempre lo es:
    /// WSJT-X manda el caracter que pinta en su ventana (<c>~</c> para FT8, <c>+</c> para FT4)
    /// y MSHV manda el nombre. Cuando es un nombre de verdad se usa; cuando no, se toma el del
    /// ultimo estado de esa misma instancia, que si lo trae bien. Y si aun no ha llegado ningun
    /// estado se devuelve vacio: vacio es honesto, un <c>~</c> en la columna de modo no lo es.
    /// El texto original no se pierde, va aparte en
    /// <see cref="DecodificacionDigital.IndicadorDelPrograma"/>.
    /// </remarks>
    /// <param name="indicadorDelPrograma">Campo de modo de la decodificacion, tal y como vino.</param>
    /// <param name="modoDelEstado">Modo del ultimo estado conocido de la instancia.</param>
    public static Modo LeerModoDeDecodificacion(string? indicadorDelPrograma, Modo modoDelEstado)
    {
        if (!string.IsNullOrWhiteSpace(indicadorDelPrograma)
            && Modo.TryParse(indicadorDelPrograma, null, out var modo))
        {
            return modo;
        }

        return modoDelEstado;
    }

    /// <summary>
    /// Traduce el par modo y submodo que viaja en el mensaje de estado.
    /// </summary>
    /// <remarks>
    /// El submodo del protocolo no siempre es el submodo de ADIF: en JT65 y JT9 el programa
    /// envia una sola letra, <c>B</c>, que en ADIF se escribe pegada al modo, <c>JT65B</c>.
    /// Se prueba primero esa union y solo despues el par tal cual.
    /// </remarks>
    /// <param name="modo">Campo de modo del mensaje.</param>
    /// <param name="submodo">Campo de submodo del mensaje.</param>
    public static Modo LeerModo(string? modo, string? submodo)
    {
        if (string.IsNullOrWhiteSpace(modo) && string.IsNullOrWhiteSpace(submodo)) return Modo.Vacio;

        var s = submodo?.Trim();
        if (!string.IsNullOrEmpty(s) && s.Length == 1
            && Modo.TryParse(modo?.Trim() + s, null, out var pegado))
        {
            return pegado;
        }

        if (Modo.TryParse(modo, s, out var par)) return par;
        return Modo.Crudo(modo, s);
    }

    /// <summary>
    /// Lee un informe. En los modos por tarjeta de sonido el informe es la relacion
    /// senal-ruido en decibelios y se normaliza con signo; en cualquier otro caso se conserva
    /// tal y como venia.
    /// </summary>
    public static Informe LeerInforme(string? texto, Modo modo)
    {
        if (string.IsNullOrWhiteSpace(texto)) return Informe.Ninguno;
        var v = texto.Trim();

        if (modo.UsaInformeEnDecibelios
            && int.TryParse(v, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var db)
            && db is >= -99 and <= 99)
        {
            return Informe.DesdeDecibelios(db);
        }

        return Informe.Parse(v);
    }

    /// <summary>Lee la potencia, que llega en texto y a veces con la unidad pegada.</summary>
    public static double? LeerPotencia(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var v = texto.Trim().TrimEnd('W', 'w', ' ');
        return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var potencia)
            && potencia >= 0
            ? potencia
            : null;
    }

    private static string? Vacio(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
