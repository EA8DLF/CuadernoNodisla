using System.Globalization;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Dominio.Entidades;

/// <summary>Un dato que las dos copias traian distinto y del que ha habido que elegir uno.</summary>
/// <param name="ClaveNatural">Contacto afectado, para que el operador sepa cual mirar.</param>
/// <param name="Campo">Nombre ADIF del campo en discordia.</param>
/// <param name="Conservado">Valor que se ha quedado en el cuaderno.</param>
/// <param name="Descartado">Valor de la otra copia, que no se guarda.</param>
public sealed record ChoqueDeFusion(string ClaveNatural, string Campo, string Conservado, string Descartado);

/// <summary>Lo que ha pasado al fundir dos contactos.</summary>
/// <param name="HuboCambios">El contacto de destino ha quedado distinto de como estaba.</param>
/// <param name="RecuperoConfirmacion">
/// La fusion ha rescatado una confirmacion que se habria perdido descartando la otra copia.
/// Es el motivo de ser de todo esto: las confirmaciones son lo que cuenta para los diplomas.
/// </param>
/// <param name="Choques">Datos distintos en las dos copias, para que el operador los revise.</param>
public sealed record ResultadoDeFusion(
    bool HuboCambios,
    bool RecuperoConfirmacion,
    IReadOnlyList<ChoqueDeFusion> Choques);

/// <summary>
/// Funde dos contactos que son el mismo aunque vengan por separado.
/// </summary>
/// <remarks>
/// Un cuaderno real trae pares de registros con la misma clave natural —mismo indicativo,
/// banda, modo y segundo exacto— que no son copias literales: cada uno sabe algo que el otro
/// no. Un ADIF de Log4OM del cuaderno de EA8DLF tiene 43 de estos, y en varios una copia trae
/// la QSL sin recibir y la otra recibida con su fecha. Quedarse con una sola copia perderia
/// confirmaciones, y las confirmaciones son diplomas. Por eso se funden y no se descartan.
///
/// Las reglas, en orden de importancia:
/// <list type="bullet">
/// <item>Un valor siempre gana al hueco: lo que uno tiene y el otro no, se queda.</item>
/// <item>Si los dos tienen valor y difieren, gana el destino y se anota el choque. Nunca se
/// pisa un dato en silencio.</item>
/// <item>Las confirmaciones se funden via a via quedandose con el estado mas avanzado y con la
/// fecha que lo acompana. Esta parte es conmutativa a proposito: fundir A con B y B con A da
/// las mismas confirmaciones, porque de lo contrario el resultado dependeria del orden en que
/// el fichero trajera los registros.</item>
/// <item>Referencias y campos extra se unen sin repetir.</item>
/// </list>
/// Lo que no se toca: el <see cref="Qso.Uuid"/>, el <see cref="Qso.Id"/> y las marcas de
/// creacion y modificacion del destino, que son contabilidad del cuaderno y no datos del
/// contacto.
/// </remarks>
public static class FusionDeQso
{
    /// <summary>
    /// Vuelca sobre <paramref name="destino"/> lo que aporte <paramref name="origen"/>.
    /// </summary>
    /// <param name="destino">Contacto que se queda en el cuaderno y que se modifica.</param>
    /// <param name="origen">Contacto del que se toma lo que falte. No se modifica.</param>
    public static ResultadoDeFusion Fundir(Qso destino, Qso origen)
    {
        ArgumentNullException.ThrowIfNull(destino);
        ArgumentNullException.ThrowIfNull(origen);

        var c = new Contexto(destino);

        FundirClaveYRadio(c, destino, origen);
        FundirCorresponsal(c, destino, origen);
        FundirMiEstacion(c, destino, origen);
        FundirCondiciones(c, destino, origen);
        FundirConcursoYNotas(c, destino, origen);
        FundirConfirmaciones(c, destino, origen);
        FundirReferencias(c, destino, origen);
        FundirCamposExtra(c, destino, origen);

        return new ResultadoDeFusion(c.Cambios, c.RecuperoConfirmacion, c.Choques);
    }

    /// <summary>
    /// Cuanto avanza un estado hacia la confirmacion. Sirve para elegir entre dos estados de
    /// la misma via sin depender del orden en que lleguen.
    /// </summary>
    /// <remarks>
    /// La escala que importa es <c>Verificado &gt; Confirmado &gt; Solicitado &gt; Pendiente
    /// &gt; Ninguno</c>. Los tres estados que no van camino de confirmar nada —rechazado,
    /// devuelto e invalido— se colocan por encima de «no consta», porque son informacion, y
    /// por debajo de «pendiente», porque no son progreso.
    /// </remarks>
    public static int Avance(EstadoDeConfirmacion estado) => estado switch
    {
        EstadoDeConfirmacion.Ninguno => 0,
        EstadoDeConfirmacion.Rechazado => 1,
        EstadoDeConfirmacion.Devuelto => 2,
        EstadoDeConfirmacion.Invalido => 3,
        EstadoDeConfirmacion.Pendiente => 4,
        EstadoDeConfirmacion.Solicitado => 5,
        EstadoDeConfirmacion.Confirmado => 6,
        EstadoDeConfirmacion.Verificado => 7,
        _ => 0,
    };

    /// <summary>El mas avanzado de los dos estados.</summary>
    public static EstadoDeConfirmacion MasAvanzado(EstadoDeConfirmacion a, EstadoDeConfirmacion b) =>
        Avance(b) > Avance(a) ? b : a;

    // ── Bloques de campos ────────────────────────────────────────────────────

    private static void FundirClaveYRadio(Contexto c, Qso d, Qso o)
    {
        Valor(c, "CALL", d, o, q => q.Call, (q, v) => q.Call = v, v => v.EsVacio);
        Valor(c, "BAND", d, o, q => q.Band, (q, v) => q.Band = v, v => v.EsVacia);
        Valor(c, "BAND_RX", d, o, q => q.BandRx, (q, v) => q.BandRx = v, v => v.EsVacia);
        FundirModo(c, d, o);
        Valor(c, "FREQ", d, o, q => q.Freq, (q, v) => q.Freq = v, v => v.EsCero);
        Valor(c, "FREQ_RX", d, o, q => q.FreqRx, (q, v) => q.FreqRx = v, v => v is null || v.Value.EsCero);
        Valor(c, "TIME_OFF", d, o, q => q.FinUtc, (q, v) => q.FinUtc = v, v => v is null);
        Valor(c, "RST_SENT", d, o, q => q.RstSent, (q, v) => q.RstSent = v, v => v.EsVacio);
        Valor(c, "RST_RCVD", d, o, q => q.RstRcvd, (q, v) => q.RstRcvd = v, v => v.EsVacio);
        Valor(c, "TX_PWR", d, o, q => q.TxPwr, (q, v) => q.TxPwr = v, v => v is null);
        Valor(c, "RX_PWR", d, o, q => q.RxPwr, (q, v) => q.RxPwr = v, v => v is null);
        Texto(c, "PROP_MODE", d, o, q => q.PropMode, (q, v) => q.PropMode = v);
        Texto(c, "SAT_NAME", d, o, q => q.SatName, (q, v) => q.SatName = v);
        Texto(c, "SAT_MODE", d, o, q => q.SatMode, (q, v) => q.SatMode = v);
    }

    /// <summary>
    /// El modo se funde aparte porque el submodo suele ser justo lo que distingue a las dos
    /// copias: una dice <c>MFSK</c> a secas y la otra <c>MFSK/FT4</c>. Eso no es un choque,
    /// es una copia que sabe mas que la otra.
    /// </summary>
    private static void FundirModo(Contexto c, Qso d, Qso o)
    {
        if (o.Mode.EsVacio) return;
        if (d.Mode.EsVacio) { d.Mode = o.Mode; c.MarcarCambio(); return; }
        if (d.Mode == o.Mode) return;

        // Con el mismo modo principal, que uno traiga submodo y el otro no, no es un choque en
        // ninguna de las dos direcciones: es una copia que sabe mas que la otra. Comprobar solo
        // un sentido hacia que el informe al operador se llenara de choques falsos segun el
        // orden en que el fichero trajera los registros.
        if (string.Equals(d.Mode.Principal, o.Mode.Principal, StringComparison.OrdinalIgnoreCase))
        {
            if (d.Mode.Submodo is null && o.Mode.Submodo is not null)
            {
                d.Mode = o.Mode;
                c.MarcarCambio();
                return;
            }
            if (o.Mode.Submodo is null) return;
        }

        c.Anotar("MODE", d.Mode.ToString(), o.Mode.ToString());
    }

    private static void FundirCorresponsal(Contexto c, Qso d, Qso o)
    {
        Texto(c, "NAME", d, o, q => q.Name, (q, v) => q.Name = v);
        Texto(c, "ADDRESS", d, o, q => q.Address, (q, v) => q.Address = v);
        Texto(c, "QTH", d, o, q => q.Qth, (q, v) => q.Qth = v);
        Texto(c, "EMAIL", d, o, q => q.Email, (q, v) => q.Email = v);
        Texto(c, "WEB", d, o, q => q.Web, (q, v) => q.Web = v);
        Valor(c, "GRIDSQUARE", d, o, q => q.Gridsquare, (q, v) => q.Gridsquare = v, v => v.EsVacio);
        Texto(c, "GRIDSQUARE_EXT", d, o, q => q.GridsquareExt, (q, v) => q.GridsquareExt = v);
        Valor(c, "LAT", d, o, q => q.Lat, (q, v) => q.Lat = v, v => v is null);
        Valor(c, "LON", d, o, q => q.Lon, (q, v) => q.Lon = v, v => v is null);
        Valor(c, "ALTITUDE", d, o, q => q.Altitude, (q, v) => q.Altitude = v, v => v is null);
        Texto(c, "CONT", d, o, q => q.Cont, (q, v) => q.Cont = v);
        Texto(c, "COUNTRY", d, o, q => q.Country, (q, v) => q.Country = v);
        Valor(c, "DXCC", d, o, q => q.Dxcc, (q, v) => q.Dxcc = v, v => v == 0);
        Valor(c, "CQZ", d, o, q => q.Cqz, (q, v) => q.Cqz = v, v => v is null);
        Valor(c, "ITUZ", d, o, q => q.Ituz, (q, v) => q.Ituz = v, v => v is null);
        Texto(c, "PFX", d, o, q => q.Pfx, (q, v) => q.Pfx = v);
        Texto(c, "STATE", d, o, q => q.State, (q, v) => q.State = v);
        Texto(c, "CNTY", d, o, q => q.Cnty, (q, v) => q.Cnty = v);
        Texto(c, "REGION", d, o, q => q.Region, (q, v) => q.Region = v);
        Texto(c, "DARC_DOK", d, o, q => q.DarcDok, (q, v) => q.DarcDok = v);
        Valor(c, "AGE", d, o, q => q.Age, (q, v) => q.Age = v, v => v is null);
        Texto(c, "RIG", d, o, q => q.Rig, (q, v) => q.Rig = v);
        Valor(c, "SILENT_KEY", d, o, q => q.SilentKey, (q, v) => q.SilentKey = v, v => !v);
        Texto(c, "EQ_CALL", d, o, q => q.EqCall, (q, v) => q.EqCall = v);
        Texto(c, "CONTACTED_OP", d, o, q => q.ContactedOp, (q, v) => q.ContactedOp = v);
        Texto(c, "QSL_VIA", d, o, q => q.QslVia, (q, v) => q.QslVia = v);
        Texto(c, "IOTA_ISLAND_ID", d, o, q => q.IotaIslandId, (q, v) => q.IotaIslandId = v);
    }

    private static void FundirMiEstacion(Contexto c, Qso d, Qso o)
    {
        Valor(c, "STATION_CALLSIGN", d, o,
            q => q.StationCallsign, (q, v) => q.StationCallsign = v, v => v.EsVacio);
        Texto(c, "OPERATOR", d, o, q => q.Operator, (q, v) => q.Operator = v);
        Texto(c, "OWNER_CALLSIGN", d, o, q => q.OwnerCallsign, (q, v) => q.OwnerCallsign = v);
        Valor(c, "MY_GRIDSQUARE", d, o, q => q.MyGridsquare, (q, v) => q.MyGridsquare = v, v => v.EsVacio);
        Texto(c, "MY_CITY", d, o, q => q.MyCity, (q, v) => q.MyCity = v);
        Texto(c, "MY_STATE", d, o, q => q.MyState, (q, v) => q.MyState = v);
        Texto(c, "MY_CNTY", d, o, q => q.MyCnty, (q, v) => q.MyCnty = v);
        Texto(c, "MY_COUNTRY", d, o, q => q.MyCountry, (q, v) => q.MyCountry = v);
        Valor(c, "MY_DXCC", d, o, q => q.MyDxcc, (q, v) => q.MyDxcc = v, v => v is null);
        Valor(c, "MY_CQ_ZONE", d, o, q => q.MyCqZone, (q, v) => q.MyCqZone = v, v => v is null);
        Valor(c, "MY_ITU_ZONE", d, o, q => q.MyItuZone, (q, v) => q.MyItuZone = v, v => v is null);
        Valor(c, "MY_LAT", d, o, q => q.MyLat, (q, v) => q.MyLat = v, v => v is null);
        Valor(c, "MY_LON", d, o, q => q.MyLon, (q, v) => q.MyLon = v, v => v is null);
        Valor(c, "MY_ALTITUDE", d, o, q => q.MyAltitude, (q, v) => q.MyAltitude = v, v => v is null);
        Texto(c, "MY_RIG", d, o, q => q.MyRig, (q, v) => q.MyRig = v);
        Texto(c, "MY_ANTENNA", d, o, q => q.MyAntenna, (q, v) => q.MyAntenna = v);
        Texto(c, "MY_NAME", d, o, q => q.MyName, (q, v) => q.MyName = v);
    }

    private static void FundirCondiciones(Contexto c, Qso d, Qso o)
    {
        Valor(c, "ANT_AZ", d, o, q => q.AntAz, (q, v) => q.AntAz = v, v => v is null);
        Valor(c, "ANT_EL", d, o, q => q.AntEl, (q, v) => q.AntEl = v, v => v is null);
        Valor(c, "DISTANCE", d, o, q => q.Distance, (q, v) => q.Distance = v, v => v is null);
        Valor(c, "A_INDEX", d, o, q => q.AIndex, (q, v) => q.AIndex = v, v => v is null);
        Valor(c, "K_INDEX", d, o, q => q.KIndex, (q, v) => q.KIndex = v, v => v is null);
        Valor(c, "SFI", d, o, q => q.Sfi, (q, v) => q.Sfi = v, v => v is null);
        Valor(c, "SWL", d, o, q => q.Swl, (q, v) => q.Swl = v, v => !v);
        Texto(c, "QSO_COMPLETE", d, o, q => q.QsoComplete, (q, v) => q.QsoComplete = v);
        Valor(c, "QSO_RANDOM", d, o, q => q.QsoRandom, (q, v) => q.QsoRandom = v, v => v is null);
    }

    private static void FundirConcursoYNotas(Contexto c, Qso d, Qso o)
    {
        Texto(c, "CONTEST_ID", d, o, q => q.ContestId, (q, v) => q.ContestId = v);
        Texto(c, "STX_STRING", d, o, q => q.StxString, (q, v) => q.StxString = v);
        Valor(c, "STX", d, o, q => q.Stx, (q, v) => q.Stx = v, v => v is null);
        Texto(c, "SRX_STRING", d, o, q => q.SrxString, (q, v) => q.SrxString = v);
        Valor(c, "SRX", d, o, q => q.Srx, (q, v) => q.Srx = v, v => v is null);
        Texto(c, "COMMENT", d, o, q => q.Comentario, (q, v) => q.Comentario = v);
        Texto(c, "NOTES", d, o, q => q.Notas, (q, v) => q.Notas = v);
        Texto(c, "QSLMSG", d, o, q => q.QslMsg, (q, v) => q.QslMsg = v);
        Texto(c, "Origen", d, o, q => q.Origen, (q, v) => q.Origen = v);
    }

    // ── Colecciones ──────────────────────────────────────────────────────────

    private static void FundirConfirmaciones(Contexto c, Qso d, Qso o)
    {
        foreach (var deOrigen in o.Confirmaciones)
        {
            QsoConfirmacion? enDestino = null;
            foreach (var x in d.Confirmaciones)
            {
                if (x.Medio == deOrigen.Medio) { enDestino = x; break; }
            }

            if (enDestino is null)
            {
                d.Confirmaciones.Add(Copiar(deOrigen));
                c.MarcarCambio();
                if (deOrigen.Enviado != EstadoDeConfirmacion.Ninguno
                    || deOrigen.Recibido != EstadoDeConfirmacion.Ninguno)
                {
                    c.MarcarConfirmacionRecuperada();
                }
                continue;
            }

            FundirUnaConfirmacion(c, enDestino, deOrigen);
        }
    }

    private static void FundirUnaConfirmacion(Contexto c, QsoConfirmacion d, QsoConfirmacion o)
    {
        // Los estados de partida se guardan ANTES de tocar nada: si se leen despues de
        // escribirlos, el destino parece estar siempre en el estado ganador y la eleccion de
        // fecha se hace con datos falsos. Ese era el motivo de que la fusion no fuese
        // conmutativa y de que un estado perdedor pudiera imponer su fecha.
        var partidaEnviado = d.Enviado;
        var partidaRecibido = d.Recibido;

        var enviado = MasAvanzado(partidaEnviado, o.Enviado);
        var recibido = MasAvanzado(partidaRecibido, o.Recibido);

        if (enviado != partidaEnviado)
        {
            d.Enviado = enviado;
            c.MarcarCambio();
            c.MarcarConfirmacionRecuperada();
        }
        if (recibido != partidaRecibido)
        {
            d.Recibido = recibido;
            c.MarcarCambio();
            c.MarcarConfirmacionRecuperada();
        }

        var fechaEnviado = FechaDelEstado(enviado, partidaEnviado, d.EnviadoUtc, o.Enviado, o.EnviadoUtc);
        if (fechaEnviado != d.EnviadoUtc) { d.EnviadoUtc = fechaEnviado; c.MarcarCambio(); }

        var fechaRecibido = FechaDelEstado(recibido, partidaRecibido, d.RecibidoUtc, o.Recibido, o.RecibidoUtc);
        if (fechaRecibido != d.RecibidoUtc) { d.RecibidoUtc = fechaRecibido; c.MarcarCambio(); }

        if (o.Via != ViaDeEnvio.Ninguna)
        {
            if (d.Via == ViaDeEnvio.Ninguna) { d.Via = o.Via; c.MarcarCambio(); }
            else if (d.Via != o.Via) c.Anotar($"{d.Medio}.Via", d.Via.ToString(), o.Via.ToString());
        }

        if (!string.IsNullOrWhiteSpace(o.Nota))
        {
            if (string.IsNullOrWhiteSpace(d.Nota)) { d.Nota = o.Nota; c.MarcarCambio(); }
            else if (!string.Equals(d.Nota, o.Nota, StringComparison.Ordinal))
            {
                c.Anotar($"{d.Medio}.Nota", d.Nota, o.Nota);
            }
        }
    }

    /// <summary>
    /// Fecha que acompana al estado ganador. Se prefiere la de los lados que estan en ese
    /// estado y, entre varias, la mas antigua; asi el resultado no depende del orden.
    /// </summary>
    private static DateTimeOffset? FechaDelEstado(
        EstadoDeConfirmacion ganador,
        EstadoDeConfirmacion estadoA,
        DateTimeOffset? fechaA,
        EstadoDeConfirmacion estadoB,
        DateTimeOffset? fechaB)
    {
        DateTimeOffset? elegida = null;

        if (estadoA == ganador && fechaA is { } a) elegida = a;
        if (estadoB == ganador && fechaB is { } b) elegida = elegida is { } ya && ya <= b ? ya : b;
        if (elegida is not null) return elegida;

        if (fechaA is { } a2) elegida = a2;
        if (fechaB is { } b2) elegida = elegida is { } ya2 && ya2 <= b2 ? ya2 : b2;
        return elegida;
    }

    private static void FundirReferencias(Contexto c, Qso d, Qso o)
    {
        foreach (var r in o.Referencias)
        {
            var repetida = false;
            foreach (var x in d.Referencias)
            {
                if (x.Tipo == r.Tipo
                    && x.Lado == r.Lado
                    && string.Equals(x.Codigo, r.Codigo, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(
                        x.NombrePrograma ?? string.Empty,
                        r.NombrePrograma ?? string.Empty,
                        StringComparison.OrdinalIgnoreCase))
                {
                    repetida = true;
                    break;
                }
            }
            if (repetida) continue;

            d.Referencias.Add(new QsoReferencia
            {
                Tipo = r.Tipo,
                NombrePrograma = r.NombrePrograma,
                Codigo = r.Codigo,
                Lado = r.Lado,
                Descripcion = r.Descripcion,
            });
            c.MarcarCambio();
        }
    }

    private static void FundirCamposExtra(Contexto c, Qso d, Qso o)
    {
        foreach (var e in o.CamposExtra)
        {
            QsoCampoExtra? existente = null;
            foreach (var x in d.CamposExtra)
            {
                if (string.Equals(x.Nombre, e.Nombre, StringComparison.OrdinalIgnoreCase))
                {
                    existente = x;
                    break;
                }
            }

            if (existente is null)
            {
                d.CamposExtra.Add(new QsoCampoExtra
                {
                    Nombre = e.Nombre,
                    Valor = e.Valor,
                    TipoAdif = e.TipoAdif,
                });
                c.MarcarCambio();
                continue;
            }

            if (!string.Equals(existente.Valor, e.Valor, StringComparison.Ordinal))
            {
                c.Anotar(e.Nombre, existente.Valor, e.Valor);
            }
        }
    }

    private static QsoConfirmacion Copiar(QsoConfirmacion origen) => new()
    {
        Medio = origen.Medio,
        Enviado = origen.Enviado,
        Recibido = origen.Recibido,
        EnviadoUtc = origen.EnviadoUtc,
        RecibidoUtc = origen.RecibidoUtc,
        Via = origen.Via,
        Nota = origen.Nota,
    };

    // ── Ayudas ───────────────────────────────────────────────────────────────

    private static void Texto(
        Contexto c, string campo, Qso d, Qso o, Func<Qso, string?> leer, Action<Qso, string?> escribir)
    {
        var a = leer(d);
        var b = leer(o);
        if (string.IsNullOrWhiteSpace(b)) return;
        if (string.IsNullOrWhiteSpace(a)) { escribir(d, b); c.MarcarCambio(); return; }
        if (!string.Equals(a, b, StringComparison.Ordinal)) c.Anotar(campo, a, b);
    }

    private static void Valor<T>(
        Contexto c, string campo, Qso d, Qso o,
        Func<Qso, T> leer, Action<Qso, T> escribir, Func<T, bool> estaVacio)
    {
        var a = leer(d);
        var b = leer(o);
        if (estaVacio(b)) return;
        if (estaVacio(a)) { escribir(d, b); c.MarcarCambio(); return; }
        if (!EqualityComparer<T>.Default.Equals(a, b)) c.Anotar(campo, ComoTexto(a), ComoTexto(b));
    }

    private static string ComoTexto<T>(T valor) => valor switch
    {
        null => string.Empty,
        bool b => b ? "Y" : "N",
        DateTimeOffset f => f.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => valor.ToString() ?? string.Empty,
    };

    /// <summary>Lleva la cuenta de lo que va pasando durante la fusion.</summary>
    private sealed class Contexto(Qso destino)
    {
        private readonly string _clave = destino.ClaveNatural;

        public List<ChoqueDeFusion> Choques { get; } = [];

        public bool Cambios { get; private set; }

        public bool RecuperoConfirmacion { get; private set; }

        public void MarcarCambio() => Cambios = true;

        public void MarcarConfirmacionRecuperada() => RecuperoConfirmacion = true;

        public void Anotar(string campo, string conservado, string descartado) =>
            Choques.Add(new ChoqueDeFusion(_clave, campo, conservado, descartado));
    }
}
