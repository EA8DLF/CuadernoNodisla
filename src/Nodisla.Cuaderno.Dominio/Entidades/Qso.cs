using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Dominio.Entidades;

/// <summary>
/// Un contacto registrado en el cuaderno.
/// </summary>
/// <remarks>
/// Los nombres de las propiedades siguen los campos de ADIF a proposito. La importacion y la
/// exportacion ADIF son la operacion mas frecuente y mas critica del programa: traducir
/// <c>gridsquare</c> a <c>localizador</c> obligaria a mantener un diccionario que se
/// desincroniza con cada revision del estandar, a cambio de nada. Lo que ve el operador en
/// pantalla si va en espanol, resuelto en la capa de interfaz.
/// </remarks>
public sealed class Qso
{
    /// <summary>Clave interna de la base de datos.</summary>
    public long Id { get; set; }

    /// <summary>
    /// Identificador estable entre bases de datos y sincronizaciones. No cambia nunca,
    /// ni siquiera al exportar e importar en otra maquina.
    /// </summary>
    public Guid Uuid { get; set; } = Guid.NewGuid();

    /// <summary>Perfil de estacion con el que se hizo el contacto.</summary>
    public long? EstacionId { get; set; }

    // ── Clave natural ────────────────────────────────────────────────────────

    /// <summary>Indicativo del corresponsal (<c>CALL</c>).</summary>
    public Indicativo Call { get; set; }

    /// <summary>Banda de transmision (<c>BAND</c>).</summary>
    public Banda Band { get; set; }

    /// <summary>Modo y submodo (<c>MODE</c> y <c>SUBMODE</c>).</summary>
    public Modo Mode { get; set; }

    /// <summary>Instante de inicio del contacto, siempre en UTC (<c>QSO_DATE</c> + <c>TIME_ON</c>).</summary>
    public DateTimeOffset InicioUtc { get; set; }

    /// <summary>Instante de fin del contacto, en UTC (<c>QSO_DATE_OFF</c> + <c>TIME_OFF</c>).</summary>
    public DateTimeOffset? FinUtc { get; set; }

    // ── Radio ────────────────────────────────────────────────────────────────

    /// <summary>Frecuencia de transmision (<c>FREQ</c>).</summary>
    public Frecuencia Freq { get; set; }

    /// <summary>Frecuencia de recepcion en trabajo en dos frecuencias (<c>FREQ_RX</c>).</summary>
    public Frecuencia? FreqRx { get; set; }

    /// <summary>Banda de recepcion (<c>BAND_RX</c>).</summary>
    public Banda BandRx { get; set; }

    /// <summary>Informe enviado (<c>RST_SENT</c>).</summary>
    public Informe RstSent { get; set; }

    /// <summary>Informe recibido (<c>RST_RCVD</c>).</summary>
    public Informe RstRcvd { get; set; }

    /// <summary>Potencia de transmision en vatios (<c>TX_PWR</c>).</summary>
    public double? TxPwr { get; set; }

    /// <summary>Potencia del corresponsal en vatios (<c>RX_PWR</c>).</summary>
    public double? RxPwr { get; set; }

    /// <summary>Modo de propagacion (<c>PROP_MODE</c>): <c>F2</c>, <c>ES</c>, <c>SAT</c>…</summary>
    public string? PropMode { get; set; }

    /// <summary>Nombre del satelite (<c>SAT_NAME</c>).</summary>
    public string? SatName { get; set; }

    /// <summary>Modo del satelite (<c>SAT_MODE</c>).</summary>
    public string? SatMode { get; set; }

    // ── Corresponsal ─────────────────────────────────────────────────────────

    /// <summary>Nombre del operador contactado (<c>NAME</c>).</summary>
    public string? Name { get; set; }

    /// <summary>Direccion postal (<c>ADDRESS</c>).</summary>
    public string? Address { get; set; }

    /// <summary>Localidad (<c>QTH</c>).</summary>
    public string? Qth { get; set; }

    public string? Email { get; set; }

    public string? Web { get; set; }

    /// <summary>Localizador del corresponsal (<c>GRIDSQUARE</c>).</summary>
    public Locator Gridsquare { get; set; }

    /// <summary>Extension del localizador a 8 o 10 caracteres (<c>GRIDSQUARE_EXT</c>).</summary>
    public string? GridsquareExt { get; set; }

    /// <summary>Latitud en grados decimales. ADIF la escribe en otro formato; se convierte al exportar.</summary>
    public double? Lat { get; set; }

    /// <summary>Longitud en grados decimales.</summary>
    public double? Lon { get; set; }

    /// <summary>Altitud en metros (<c>ALTITUDE</c>). Log4OM no la guarda; aqui si.</summary>
    public double? Altitude { get; set; }

    /// <summary>Continente (<c>CONT</c>): <c>EU</c>, <c>NA</c>, <c>AF</c>…</summary>
    public string? Cont { get; set; }

    /// <summary>Nombre del pais (<c>COUNTRY</c>).</summary>
    public string? Country { get; set; }

    /// <summary>Numero de entidad DXCC (<c>DXCC</c>). Cero significa sin identificar.</summary>
    public int Dxcc { get; set; }

    /// <summary>Zona CQ (<c>CQZ</c>).</summary>
    public int? Cqz { get; set; }

    /// <summary>Zona ITU (<c>ITUZ</c>).</summary>
    public int? Ituz { get; set; }

    /// <summary>Prefijo WPX (<c>PFX</c>).</summary>
    public string? Pfx { get; set; }

    /// <summary>Division primaria: estado, provincia o canton (<c>STATE</c>).</summary>
    public string? State { get; set; }

    /// <summary>Division secundaria: condado o comarca (<c>CNTY</c>).</summary>
    public string? Cnty { get; set; }

    /// <summary>Region WAE u otra region especial (<c>REGION</c>).</summary>
    public string? Region { get; set; }

    /// <summary>DOK aleman (<c>DARC_DOK</c>).</summary>
    public string? DarcDok { get; set; }

    public double? Age { get; set; }

    /// <summary>Equipo del corresponsal (<c>RIG</c>).</summary>
    public string? Rig { get; set; }

    /// <summary>El corresponsal ha fallecido (<c>SILENT_KEY</c>).</summary>
    public bool SilentKey { get; set; }

    /// <summary>Indicativo para eQSL si difiere (<c>EQ_CALL</c>).</summary>
    public string? EqCall { get; set; }

    /// <summary>Operador concreto que estaba a los mandos en la otra estacion (<c>CONTACTED_OP</c>).</summary>
    public string? ContactedOp { get; set; }

    /// <summary>Gestor de QSL del corresponsal (<c>QSL_VIA</c>).</summary>
    public string? QslVia { get; set; }

    // ── Mi estacion, congelada en el momento del contacto ────────────────────

    /// <summary>Indicativo con el que se transmitio (<c>STATION_CALLSIGN</c>).</summary>
    public Indicativo StationCallsign { get; set; }

    /// <summary>Operador que estaba a los mandos (<c>OPERATOR</c>).</summary>
    public string? Operator { get; set; }

    /// <summary>Titular de la licencia (<c>OWNER_CALLSIGN</c>).</summary>
    public string? OwnerCallsign { get; set; }

    /// <summary>Mi localizador en ese momento (<c>MY_GRIDSQUARE</c>).</summary>
    public Locator MyGridsquare { get; set; }

    public string? MyCity { get; set; }
    public string? MyState { get; set; }
    public string? MyCnty { get; set; }
    public string? MyCountry { get; set; }
    public int? MyDxcc { get; set; }
    public int? MyCqZone { get; set; }
    public int? MyItuZone { get; set; }
    public double? MyLat { get; set; }
    public double? MyLon { get; set; }
    public double? MyAltitude { get; set; }
    public string? MyRig { get; set; }
    public string? MyAntenna { get; set; }

    // ── Concurso ─────────────────────────────────────────────────────────────

    /// <summary>Identificador del concurso (<c>CONTEST_ID</c>).</summary>
    public string? ContestId { get; set; }

    /// <summary>Intercambio enviado (<c>STX_STRING</c>).</summary>
    public string? StxString { get; set; }

    /// <summary>Numero de serie enviado (<c>STX</c>).</summary>
    public int? Stx { get; set; }

    /// <summary>Intercambio recibido (<c>SRX_STRING</c>).</summary>
    public string? SrxString { get; set; }

    /// <summary>Numero de serie recibido (<c>SRX</c>).</summary>
    public int? Srx { get; set; }

    // ── Metadatos propios ────────────────────────────────────────────────────

    /// <summary>Comentario visible, se exporta como <c>COMMENT</c>.</summary>
    public string? Comentario { get; set; }

    /// <summary>Notas privadas, se exportan como <c>NOTES</c>.</summary>
    public string? Notas { get; set; }

    /// <summary>De donde vino el contacto: teclado, WSJT-X, importacion ADIF, cluster…</summary>
    public string? Origen { get; set; }

    public DateTimeOffset CreadoUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset ModificadoUtc { get; set; } = DateTimeOffset.UtcNow;

    // ── Colecciones ──────────────────────────────────────────────────────────

    /// <summary>Confirmaciones por cada via: papel, LoTW, eQSL, ClubLog…</summary>
    public List<QsoConfirmacion> Confirmaciones { get; } = [];

    /// <summary>Referencias de programas de activacion, propias y del corresponsal.</summary>
    public List<QsoReferencia> Referencias { get; } = [];

    /// <summary>Campos ADIF no modelados y campos <c>APP_*</c> de otros programas.</summary>
    public List<QsoCampoExtra> CamposExtra { get; } = [];

    // ── Calculos ─────────────────────────────────────────────────────────────

    /// <summary>Duracion del contacto, si se registro la hora de fin.</summary>
    public TimeSpan? Duracion => FinUtc is { } fin ? fin - InicioUtc : null;

    /// <summary>Coordenada del corresponsal, del campo explicito o deducida del localizador.</summary>
    public Coordenada? CoordenadaCorresponsal =>
        Lat is { } la && Lon is { } lo ? new Coordenada(la, lo)
        : !Gridsquare.EsVacio ? Coordenada.Desde(Gridsquare)
        : null;

    /// <summary>Coordenada de mi estacion, del campo explicito o deducida de mi localizador.</summary>
    public Coordenada? CoordenadaPropia =>
        MyLat is { } la && MyLon is { } lo ? new Coordenada(la, lo)
        : !MyGridsquare.EsVacio ? Coordenada.Desde(MyGridsquare)
        : null;

    /// <summary>Distancia del contacto en kilometros, si se conocen las dos posiciones.</summary>
    public double? DistanciaKm =>
        CoordenadaPropia is { } origen && CoordenadaCorresponsal is { } destino
            ? Geodesia.DistanciaKm(origen, destino)
            : null;

    /// <summary>Rumbo de antena hacia el corresponsal, en grados desde el norte.</summary>
    public double? RumboGrados =>
        CoordenadaPropia is { } origen && CoordenadaCorresponsal is { } destino
            ? Geodesia.RumboGrados(origen, destino)
            : null;

    /// <summary>
    /// Clave natural compatible con Log4OM, usada para detectar duplicados al importar.
    /// El segundo exacto forma parte de la clave, igual que en el original.
    /// </summary>
    public string ClaveNatural =>
        $"{Call.Valor}|{Band.Nombre}|{Mode.Principal}|{InicioUtc.UtcDateTime:yyyy-MM-dd HH:mm:ss}";
}
