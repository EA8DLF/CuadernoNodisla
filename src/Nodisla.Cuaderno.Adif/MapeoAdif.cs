using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Adif;

/// <summary>
/// Traduccion entre los campos de un registro ADIF y la entidad <see cref="Qso"/>.
/// </summary>
/// <remarks>
/// La regla que manda aqui es que importar y volver a exportar no puede perder ni un dato. Se
/// cumple con un mecanismo unico: despues de rellenar el contacto se vuelve a generar la lista
/// de campos y se compara con la original; todo campo que no salga identico se guarda literal
/// en <see cref="Qso.CamposExtra"/>, y al exportar el literal tiene preferencia. Asi da igual
/// que el fichero traiga <c>V</c> en una QSL, una frecuencia con ceros de relleno o un
/// localizador que no cumple la norma: sale tal y como entro.
/// </remarks>
public static class MapeoAdif
{
    /// <summary>Campos que se aceptan por compatibilidad pero se exportan con su nombre bueno.</summary>
    private static readonly HashSet<string> Alias = new(StringComparer.OrdinalIgnoreCase) { "ANTENNA" };

    /// <summary>Campos cuyo valor se guarda para combinarlo al final del registro.</summary>
    private static readonly HashSet<string> Diferidos = new(StringComparer.OrdinalIgnoreCase)
    {
        "QSO_DATE", "TIME_ON", "QSO_DATE_OFF", "TIME_OFF",
        "SIG", "SIG_INFO", "MY_SIG", "MY_SIG_INFO",
        "MODE", "SUBMODE",
    };

    // ── Lectura ──────────────────────────────────────────────────────────────

    /// <summary>Construye un contacto a partir de los campos de un registro ADIF.</summary>
    /// <param name="campos">Campos del registro, en el orden del fichero.</param>
    /// <param name="numeroDeRegistro">Numero del registro, empezando en 1, para los avisos.</param>
    /// <param name="avisos">Lista donde se acumulan los problemas encontrados.</param>
    public static Qso LeerContacto(IReadOnlyList<CampoAdif> campos, int numeroDeRegistro, List<AvisoAdif> avisos)
    {
        ArgumentNullException.ThrowIfNull(campos);
        ArgumentNullException.ThrowIfNull(avisos);

        var qso = new Qso();
        var valores = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var orden = new List<CampoAdif>(campos.Count);

        var repeticiones = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var c in campos)
        {
            if (!valores.TryAdd(c.Nombre, c.Valor))
            {
                // ADIF no dice que hacer con un campo repetido. Manda el primero, pero el otro
                // valor tampoco se tira: se guarda con un nombre propio que no colisiona.
                repeticiones[c.Nombre] = repeticiones.TryGetValue(c.Nombre, out var n) ? n + 1 : 2;
                qso.CamposExtra.Add(new QsoCampoExtra
                {
                    Nombre = $"APP_NODISLA_REPETIDO_{repeticiones[c.Nombre]}_{c.Nombre}",
                    Valor = c.TextoParaEscribir,
                    TipoAdif = c.TipoAdif,
                });
                avisos.Add(new AvisoAdif(
                    numeroDeRegistro, c.Nombre,
                    $"El campo «{c.Nombre}» aparece repetido en el registro; manda el primer valor y "
                    + "el otro se conserva aparte.",
                    false));
                continue;
            }
            orden.Add(c);
        }

        var nombresExtra = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var campo in orden)
        {
            if (Diferidos.Contains(campo.Nombre)) continue;
            if (AplicarCampo(qso, campo, numeroDeRegistro, avisos)) continue;

            qso.CamposExtra.Add(new QsoCampoExtra
            {
                Nombre = campo.Nombre,
                Valor = campo.TextoParaEscribir,
                TipoAdif = campo.TipoAdif,
            });
            nombresExtra.Add(campo.Nombre);
        }

        AplicarModo(qso, valores);
        AplicarFechas(qso, valores, numeroDeRegistro, avisos);
        AplicarSig(qso, valores);
        AplicarJson(qso, valores, numeroDeRegistro, avisos);
        ConservarLoQueNoSeRegenera(qso, orden, nombresExtra);

        return qso;
    }

    private static void AplicarModo(Qso qso, Dictionary<string, string> valores)
    {
        valores.TryGetValue("MODE", out var modo);
        valores.TryGetValue("SUBMODE", out var submodo);
        if (string.IsNullOrWhiteSpace(modo) && string.IsNullOrWhiteSpace(submodo)) return;

        qso.Mode = Modo.TryParse(modo, submodo, out var m) ? m : Modo.Crudo(modo, submodo);
    }

    private static void AplicarFechas(
        Qso qso, Dictionary<string, string> valores, int numero, List<AvisoAdif> avisos)
    {
        valores.TryGetValue("QSO_DATE", out var fecha);
        valores.TryGetValue("TIME_ON", out var hora);

        if (ConversionesAdif.TryCombinarUtc(fecha, hora, out var inicio))
        {
            qso.InicioUtc = inicio;
        }
        else if (!string.IsNullOrWhiteSpace(fecha) || !string.IsNullOrWhiteSpace(hora))
        {
            avisos.Add(new AvisoAdif(
                numero, "QSO_DATE",
                $"No se entiende la fecha u hora de inicio («{fecha}» «{hora}»); el contacto queda sin fecha.",
                false));
        }

        valores.TryGetValue("QSO_DATE_OFF", out var fechaFin);
        valores.TryGetValue("TIME_OFF", out var horaFin);
        if (string.IsNullOrWhiteSpace(fechaFin)) fechaFin = fecha;

        if (!string.IsNullOrWhiteSpace(horaFin) || !string.IsNullOrWhiteSpace(valorDe(valores, "QSO_DATE_OFF")))
        {
            if (ConversionesAdif.TryCombinarUtc(fechaFin, horaFin, out var fin))
            {
                qso.FinUtc = fin;
            }
            else
            {
                avisos.Add(new AvisoAdif(
                    numero, "TIME_OFF",
                    $"No se entiende la fecha u hora de fin («{fechaFin}» «{horaFin}»); se ignora.",
                    false));
            }
        }

        static string? valorDe(Dictionary<string, string> v, string n) => v.TryGetValue(n, out var r) ? r : null;
    }

    private static void AplicarSig(Qso qso, Dictionary<string, string> valores)
    {
        Par(qso, valores, "SIG", "SIG_INFO", LadoDeReferencia.Corresponsal);
        Par(qso, valores, "MY_SIG", "MY_SIG_INFO", LadoDeReferencia.Propia);

        static void Par(Qso qso, Dictionary<string, string> valores, string sig, string info, LadoDeReferencia lado)
        {
            valores.TryGetValue(sig, out var programa);
            valores.TryGetValue(info, out var codigo);
            if (string.IsNullOrWhiteSpace(programa) && string.IsNullOrWhiteSpace(codigo)) return;
            ReferenciasAdif.Anadir(qso, TipoDeReferencia.Otra, programa, codigo ?? string.Empty, lado);
        }
    }

    private static void AplicarJson(
        Qso qso, Dictionary<string, string> valores, int numero, List<AvisoAdif> avisos)
    {
        if (valores.TryGetValue(JsonLog4Om.CampoConfirmaciones, out var confirmaciones)
            && !JsonLog4Om.TryAplicarConfirmaciones(confirmaciones, qso))
        {
            avisos.Add(new AvisoAdif(
                numero, JsonLog4Om.CampoConfirmaciones,
                "El JSON de confirmaciones no se entiende; se conserva tal cual pero no se interpreta.",
                false));
        }

        if (valores.TryGetValue(JsonLog4Om.CampoReferencias, out var referencias)
            && !JsonLog4Om.TryAplicarReferencias(referencias, qso, LadoDeReferencia.Corresponsal))
        {
            avisos.Add(new AvisoAdif(
                numero, JsonLog4Om.CampoReferencias,
                "El JSON de referencias no se entiende; se conserva tal cual pero no se interpreta.",
                false));
        }

        if (valores.TryGetValue(JsonLog4Om.CampoMisReferencias, out var mias))
        {
            JsonLog4Om.TryAplicarReferencias(mias, qso, LadoDeReferencia.Propia);
        }
    }

    /// <summary>
    /// Guarda literalmente todo campo que la exportacion no devolveria igual. Es la red de
    /// seguridad que garantiza la ida y vuelta sin perdida.
    /// </summary>
    private static void ConservarLoQueNoSeRegenera(
        Qso qso, List<CampoAdif> orden, HashSet<string> nombresExtra)
    {
        var generados = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in EscribirContacto(qso, incluirExtras: false)) generados[c.Nombre] = c.Valor;

        foreach (var campo in orden)
        {
            if (Alias.Contains(campo.Nombre)) continue;
            if (nombresExtra.Contains(campo.Nombre)) continue;

            var literal = campo.TextoParaEscribir;
            if (generados.TryGetValue(campo.Nombre, out var v)
                && string.Equals(v, literal, StringComparison.Ordinal))
            {
                continue;
            }

            qso.CamposExtra.Add(new QsoCampoExtra
            {
                Nombre = campo.Nombre,
                Valor = literal,
                TipoAdif = campo.TipoAdif,
            });
            nombresExtra.Add(campo.Nombre);
        }
    }

    /// <summary>Coloca un campo en su propiedad. Devuelve falso si el campo no se conoce.</summary>
    private static bool AplicarCampo(Qso qso, CampoAdif campo, int numero, List<AvisoAdif> avisos)
    {
        var valor = campo.Valor;
        var vacio = string.IsNullOrWhiteSpace(valor);

        switch (campo.Nombre)
        {
            // ── Clave natural y radio ────────────────────────────────────────
            case "CALL": qso.Call = Indicativo.Crudo(valor); return true;
            case "BAND": LeerBanda(valor, b => qso.Band = b, campo, numero, avisos); return true;
            case "BAND_RX": LeerBanda(valor, b => qso.BandRx = b, campo, numero, avisos); return true;
            case "FREQ": LeerFrecuencia(valor, f => qso.Freq = f, campo, numero, avisos); return true;
            case "FREQ_RX": LeerFrecuencia(valor, f => qso.FreqRx = f, campo, numero, avisos); return true;
            case "RST_SENT": qso.RstSent = Informe.Parse(valor); return true;
            case "RST_RCVD": qso.RstRcvd = Informe.Parse(valor); return true;
            case "TX_PWR": qso.TxPwr = LeerReal(valor, campo, numero, avisos); return true;
            case "RX_PWR": qso.RxPwr = LeerReal(valor, campo, numero, avisos); return true;
            case "PROP_MODE": qso.PropMode = Texto(valor); return true;
            case "SAT_NAME": qso.SatName = Texto(valor); return true;
            case "SAT_MODE": qso.SatMode = Texto(valor); return true;

            // ── Corresponsal ─────────────────────────────────────────────────
            case "NAME": qso.Name = Texto(valor); return true;
            case "ADDRESS": qso.Address = Texto(valor); return true;
            case "QTH": qso.Qth = Texto(valor); return true;
            case "EMAIL": qso.Email = Texto(valor); return true;
            case "WEB": qso.Web = Texto(valor); return true;
            case "GRIDSQUARE": LeerLocator(valor, l => qso.Gridsquare = l, campo, numero, avisos); return true;
            case "GRIDSQUARE_EXT": qso.GridsquareExt = Texto(valor); return true;
            case "LAT": qso.Lat = LeerCoordenada(valor, campo, numero, avisos); return true;
            case "LON": qso.Lon = LeerCoordenada(valor, campo, numero, avisos); return true;
            case "ALTITUDE": qso.Altitude = LeerReal(valor, campo, numero, avisos); return true;
            case "CONT": qso.Cont = Texto(valor); return true;
            case "COUNTRY": qso.Country = Texto(valor); return true;
            case "DXCC": qso.Dxcc = LeerEntero(valor, campo, numero, avisos) ?? 0; return true;
            case "CQZ": qso.Cqz = LeerEntero(valor, campo, numero, avisos); return true;
            case "ITUZ": qso.Ituz = LeerEntero(valor, campo, numero, avisos); return true;
            case "PFX": qso.Pfx = Texto(valor); return true;
            case "STATE": qso.State = Texto(valor); return true;
            case "CNTY": qso.Cnty = Texto(valor); return true;
            case "REGION": qso.Region = Texto(valor); return true;
            case "DARC_DOK": qso.DarcDok = Texto(valor); return true;
            case "AGE": qso.Age = LeerReal(valor, campo, numero, avisos); return true;
            case "RIG": qso.Rig = Texto(valor); return true;
            case "SILENT_KEY":
                if (ConversionesAdif.TryLeerLogico(valor, out var sk)) qso.SilentKey = sk;
                return true;
            case "EQ_CALL": qso.EqCall = Texto(valor); return true;
            case "CONTACTED_OP": qso.ContactedOp = Texto(valor); return true;
            case "QSL_VIA": qso.QslVia = Texto(valor); return true;

            // ── Mi estacion ──────────────────────────────────────────────────
            case "STATION_CALLSIGN": qso.StationCallsign = Indicativo.Crudo(valor); return true;
            case "OPERATOR": qso.Operator = Texto(valor); return true;
            case "OWNER_CALLSIGN": qso.OwnerCallsign = Texto(valor); return true;
            case "MY_GRIDSQUARE": LeerLocator(valor, l => qso.MyGridsquare = l, campo, numero, avisos); return true;
            case "MY_CITY": qso.MyCity = Texto(valor); return true;
            case "MY_STATE": qso.MyState = Texto(valor); return true;
            case "MY_CNTY": qso.MyCnty = Texto(valor); return true;
            case "MY_COUNTRY": qso.MyCountry = Texto(valor); return true;
            case "MY_DXCC": qso.MyDxcc = LeerEntero(valor, campo, numero, avisos); return true;
            case "MY_CQ_ZONE": qso.MyCqZone = LeerEntero(valor, campo, numero, avisos); return true;
            case "MY_ITU_ZONE": qso.MyItuZone = LeerEntero(valor, campo, numero, avisos); return true;
            case "MY_LAT": qso.MyLat = LeerCoordenada(valor, campo, numero, avisos); return true;
            case "MY_LON": qso.MyLon = LeerCoordenada(valor, campo, numero, avisos); return true;
            case "MY_ALTITUDE": qso.MyAltitude = LeerReal(valor, campo, numero, avisos); return true;
            case "MY_RIG": qso.MyRig = Texto(valor); return true;
            case "MY_ANTENNA":
            case "ANTENNA": qso.MyAntenna = Texto(valor); return true;

            // ── Concurso ─────────────────────────────────────────────────────
            case "CONTEST_ID": qso.ContestId = Texto(valor); return true;
            case "STX": qso.Stx = LeerEntero(valor, campo, numero, avisos); return true;
            case "STX_STRING": qso.StxString = Texto(valor); return true;
            case "SRX": qso.Srx = LeerEntero(valor, campo, numero, avisos); return true;
            case "SRX_STRING": qso.SrxString = Texto(valor); return true;

            // ── Notas ────────────────────────────────────────────────────────
            case "COMMENT": qso.Comentario = Texto(valor); return true;
            case "NOTES": qso.Notas = Texto(valor); return true;

            default:
                if (!vacio && ConfirmacionesAdif.PorCampo.TryGetValue(campo.Nombre, out var descriptor))
                {
                    AplicarConfirmacion(qso, descriptor, campo, numero, avisos);
                    return true;
                }
                if (!vacio && ReferenciasAdif.PorCampo.TryGetValue(campo.Nombre, out var referencia))
                {
                    ReferenciasAdif.Anadir(qso, referencia.Tipo, null, valor, referencia.Lado);
                    return true;
                }
                return false;
        }
    }

    private static void AplicarConfirmacion(
        Qso qso, ConfirmacionesAdif.Descriptor d, CampoAdif campo, int numero, List<AvisoAdif> avisos)
    {
        var confirmacion = ConfirmacionesAdif.Obtener(qso, d.Medio);
        var nombre = campo.Nombre;

        if (nombre.Equals(d.Enviado, StringComparison.OrdinalIgnoreCase))
        {
            if (ConfirmacionesAdif.TryLeerEstado(campo.Valor, d.EsSubida, out var e)) confirmacion.Enviado = e;
            else Aviso(avisos, numero, campo, "no es un estado de confirmacion valido");
        }
        else if (nombre.Equals(d.Recibido, StringComparison.OrdinalIgnoreCase))
        {
            if (ConfirmacionesAdif.TryLeerEstado(campo.Valor, d.EsSubida, out var e)) confirmacion.Recibido = e;
            else Aviso(avisos, numero, campo, "no es un estado de confirmacion valido");
        }
        else if (nombre.Equals(d.FechaEnviado, StringComparison.OrdinalIgnoreCase))
        {
            if (ConversionesAdif.TryCombinarUtc(campo.Valor, null, out var f)) confirmacion.EnviadoUtc = f;
            else Aviso(avisos, numero, campo, "no es una fecha valida");
        }
        else if (nombre.Equals(d.FechaRecibido, StringComparison.OrdinalIgnoreCase))
        {
            if (ConversionesAdif.TryCombinarUtc(campo.Valor, null, out var f)) confirmacion.RecibidoUtc = f;
            else Aviso(avisos, numero, campo, "no es una fecha valida");
        }
        else if (nombre.Equals(d.ViaEnviado, StringComparison.OrdinalIgnoreCase)
                 || nombre.Equals(d.ViaRecibido, StringComparison.OrdinalIgnoreCase))
        {
            if (ConfirmacionesAdif.TryLeerVia(campo.Valor, out var via)) confirmacion.Via = via;
            else Aviso(avisos, numero, campo, "no es una via de envio valida");
        }
    }

    // ── Ayudas de lectura ────────────────────────────────────────────────────

    private static string? Texto(string valor) => string.IsNullOrEmpty(valor) ? null : valor;

    private static void Aviso(List<AvisoAdif> avisos, int numero, CampoAdif campo, string problema) =>
        avisos.Add(new AvisoAdif(
            numero, campo.Nombre,
            $"El campo «{campo.Nombre}» con valor «{campo.Valor}» {problema}; se conserva sin interpretar.",
            false));

    private static void LeerBanda(
        string valor, Action<Banda> asignar, CampoAdif campo, int numero, List<AvisoAdif> avisos)
    {
        if (string.IsNullOrWhiteSpace(valor)) return;
        if (Banda.TryParse(valor, out var b)) asignar(b);
        else Aviso(avisos, numero, campo, "no es una banda de ADIF");
    }

    private static void LeerFrecuencia(
        string valor, Action<Frecuencia> asignar, CampoAdif campo, int numero, List<AvisoAdif> avisos)
    {
        if (string.IsNullOrWhiteSpace(valor)) return;
        if (Frecuencia.TryParseAdif(valor, out var f)) asignar(f);
        else Aviso(avisos, numero, campo, "no es una frecuencia valida");
    }

    private static void LeerLocator(
        string valor, Action<Locator> asignar, CampoAdif campo, int numero, List<AvisoAdif> avisos)
    {
        if (string.IsNullOrWhiteSpace(valor)) return;
        if (Locator.TryParse(valor, out var l)) asignar(l);
        else Aviso(avisos, numero, campo, "no es un localizador Maidenhead valido");
    }

    private static int? LeerEntero(string valor, CampoAdif campo, int numero, List<AvisoAdif> avisos)
    {
        if (string.IsNullOrWhiteSpace(valor)) return null;
        if (ConversionesAdif.TryLeerEntero(valor, out var n)) return n;
        Aviso(avisos, numero, campo, "no es un numero entero");
        return null;
    }

    private static double? LeerReal(string valor, CampoAdif campo, int numero, List<AvisoAdif> avisos)
    {
        if (string.IsNullOrWhiteSpace(valor)) return null;
        if (ConversionesAdif.TryLeerReal(valor, out var n)) return n;
        Aviso(avisos, numero, campo, "no es un numero");
        return null;
    }

    private static double? LeerCoordenada(string valor, CampoAdif campo, int numero, List<AvisoAdif> avisos)
    {
        if (string.IsNullOrWhiteSpace(valor)) return null;
        if (ConversionesAdif.TryLeerCoordenada(valor, out var g)) return g;
        Aviso(avisos, numero, campo, "no es una coordenada en grados y minutos de ADIF");
        return null;
    }

    // ── Escritura ────────────────────────────────────────────────────────────

    /// <summary>Genera los campos ADIF de un contacto, en el orden habitual de los cuadernos.</summary>
    /// <param name="qso">Contacto a exportar.</param>
    /// <param name="incluirExtras">
    /// Si se vuelcan los campos conservados al importar. Con esto desactivado se escribe la
    /// version canonica de cada campo y se pierde la fidelidad literal del fichero de origen.
    /// </param>
    public static List<CampoAdif> EscribirContacto(Qso qso, bool incluirExtras = true)
    {
        ArgumentNullException.ThrowIfNull(qso);

        var campos = new List<CampoAdif>(64);

        void Poner(string nombre, string? valor)
        {
            if (!string.IsNullOrEmpty(valor)) campos.Add(new CampoAdif(nombre, valor));
        }

        Poner("CALL", qso.Call.Valor);
        if (qso.InicioUtc != default)
        {
            Poner("QSO_DATE", ConversionesAdif.EscribirFecha(qso.InicioUtc));
            Poner("TIME_ON", ConversionesAdif.EscribirHora(qso.InicioUtc));
        }
        if (qso.FinUtc is { } fin)
        {
            Poner("QSO_DATE_OFF", ConversionesAdif.EscribirFecha(fin));
            Poner("TIME_OFF", ConversionesAdif.EscribirHora(fin));
        }
        Poner("BAND", qso.Band.Nombre);
        Poner("BAND_RX", qso.BandRx.Nombre);
        Poner("MODE", qso.Mode.Principal);
        Poner("SUBMODE", qso.Mode.Submodo);
        if (!qso.Freq.EsCero) Poner("FREQ", EscribirFrecuencia(qso.Freq));
        if (qso.FreqRx is { } frx && !frx.EsCero) Poner("FREQ_RX", EscribirFrecuencia(frx));
        Poner("RST_SENT", qso.RstSent.Texto);
        Poner("RST_RCVD", qso.RstRcvd.Texto);
        if (qso.TxPwr is { } txp) Poner("TX_PWR", ConversionesAdif.EscribirReal(txp));
        if (qso.RxPwr is { } rxp) Poner("RX_PWR", ConversionesAdif.EscribirReal(rxp));
        Poner("PROP_MODE", qso.PropMode);
        Poner("SAT_NAME", qso.SatName);
        Poner("SAT_MODE", qso.SatMode);

        Poner("NAME", qso.Name);
        Poner("ADDRESS", qso.Address);
        Poner("QTH", qso.Qth);
        Poner("EMAIL", qso.Email);
        Poner("WEB", qso.Web);
        Poner("GRIDSQUARE", EscribirLocator(qso.Gridsquare));
        Poner("GRIDSQUARE_EXT", qso.GridsquareExt);
        if (qso.Lat is { } lat) Poner("LAT", ConversionesAdif.EscribirLatitud(lat));
        if (qso.Lon is { } lon) Poner("LON", ConversionesAdif.EscribirLongitud(lon));
        if (qso.Altitude is { } alt) Poner("ALTITUDE", ConversionesAdif.EscribirReal(alt));
        Poner("CONT", qso.Cont);
        Poner("COUNTRY", qso.Country);
        if (qso.Dxcc != 0) Poner("DXCC", ConversionesAdif.EscribirEntero(qso.Dxcc));
        if (qso.Cqz is { } cqz) Poner("CQZ", ConversionesAdif.EscribirEntero(cqz));
        if (qso.Ituz is { } ituz) Poner("ITUZ", ConversionesAdif.EscribirEntero(ituz));
        Poner("PFX", qso.Pfx);
        Poner("STATE", qso.State);
        Poner("CNTY", qso.Cnty);
        Poner("REGION", qso.Region);
        Poner("DARC_DOK", qso.DarcDok);
        if (qso.Age is { } age) Poner("AGE", ConversionesAdif.EscribirReal(age));
        Poner("RIG", qso.Rig);
        if (qso.SilentKey) Poner("SILENT_KEY", ConversionesAdif.EscribirLogico(true));
        Poner("EQ_CALL", qso.EqCall);
        Poner("CONTACTED_OP", qso.ContactedOp);
        Poner("QSL_VIA", qso.QslVia);

        Poner("STATION_CALLSIGN", qso.StationCallsign.Valor);
        Poner("OPERATOR", qso.Operator);
        Poner("OWNER_CALLSIGN", qso.OwnerCallsign);
        Poner("MY_GRIDSQUARE", EscribirLocator(qso.MyGridsquare));
        Poner("MY_CITY", qso.MyCity);
        Poner("MY_STATE", qso.MyState);
        Poner("MY_CNTY", qso.MyCnty);
        Poner("MY_COUNTRY", qso.MyCountry);
        if (qso.MyDxcc is { } mydxcc) Poner("MY_DXCC", ConversionesAdif.EscribirEntero(mydxcc));
        if (qso.MyCqZone is { } mycq) Poner("MY_CQ_ZONE", ConversionesAdif.EscribirEntero(mycq));
        if (qso.MyItuZone is { } myitu) Poner("MY_ITU_ZONE", ConversionesAdif.EscribirEntero(myitu));
        if (qso.MyLat is { } mylat) Poner("MY_LAT", ConversionesAdif.EscribirLatitud(mylat));
        if (qso.MyLon is { } mylon) Poner("MY_LON", ConversionesAdif.EscribirLongitud(mylon));
        if (qso.MyAltitude is { } myalt) Poner("MY_ALTITUDE", ConversionesAdif.EscribirReal(myalt));
        Poner("MY_RIG", qso.MyRig);
        Poner("MY_ANTENNA", qso.MyAntenna);

        Poner("CONTEST_ID", qso.ContestId);
        if (qso.Stx is { } stx) Poner("STX", ConversionesAdif.EscribirEntero(stx));
        Poner("STX_STRING", qso.StxString);
        if (qso.Srx is { } srx) Poner("SRX", ConversionesAdif.EscribirEntero(srx));
        Poner("SRX_STRING", qso.SrxString);

        Poner("COMMENT", qso.Comentario);
        Poner("NOTES", qso.Notas);

        EscribirConfirmaciones(qso, Poner);
        EscribirReferencias(qso, Poner);

        return incluirExtras ? Superponer(campos, qso.CamposExtra) : campos;
    }

    private static void EscribirConfirmaciones(Qso qso, Action<string, string?> poner)
    {
        foreach (var d in ConfirmacionesAdif.Descriptores)
        {
            QsoConfirmacion? confirmacion = null;
            foreach (var c in qso.Confirmaciones)
            {
                if (c.Medio == d.Medio) { confirmacion = c; break; }
            }
            if (confirmacion is null) continue;

            poner(d.Enviado, ConfirmacionesAdif.EscribirEstado(confirmacion.Enviado, d.EsSubida));
            poner(d.Recibido, ConfirmacionesAdif.EscribirEstado(confirmacion.Recibido, d.EsSubida));
            if (d.FechaEnviado is not null && confirmacion.EnviadoUtc is { } se)
                poner(d.FechaEnviado, ConversionesAdif.EscribirFecha(se));
            if (d.FechaRecibido is not null && confirmacion.RecibidoUtc is { } re)
                poner(d.FechaRecibido, ConversionesAdif.EscribirFecha(re));
            if (ConfirmacionesAdif.EscribirVia(confirmacion.Via) is { } via)
            {
                if (d.ViaEnviado is not null) poner(d.ViaEnviado, via);
                if (d.ViaRecibido is not null) poner(d.ViaRecibido, via);
            }
        }
    }

    private static void EscribirReferencias(Qso qso, Action<string, string?> poner)
    {
        foreach (var d in ReferenciasAdif.Descriptores)
        {
            foreach (var lado in new[] { LadoDeReferencia.Corresponsal, LadoDeReferencia.Propia })
            {
                var codigos = qso.Referencias
                    .Where(r => r.Tipo == d.Tipo && r.Lado == lado && !string.IsNullOrWhiteSpace(r.Codigo))
                    .Select(r => r.Codigo);
                var junto = string.Join(",", codigos);
                if (junto.Length > 0) poner(lado == LadoDeReferencia.Corresponsal ? d.Corresponsal : d.Propia, junto);
            }
        }

        foreach (var lado in new[] { LadoDeReferencia.Corresponsal, LadoDeReferencia.Propia })
        {
            var suelta = qso.Referencias.FirstOrDefault(r =>
                r.Lado == lado && r.Tipo is TipoDeReferencia.Otra or TipoDeReferencia.Wca or TipoDeReferencia.Dme);
            if (suelta is null) continue;

            var programa = suelta.NombrePrograma
                ?? (suelta.Tipo == TipoDeReferencia.Otra ? null : suelta.Tipo.ToString().ToUpperInvariant());
            poner(lado == LadoDeReferencia.Corresponsal ? "SIG" : "MY_SIG", programa);
            poner(lado == LadoDeReferencia.Corresponsal ? "SIG_INFO" : "MY_SIG_INFO", suelta.Codigo);
        }
    }

    /// <summary>Coloca los campos conservados encima de los generados, sustituyendo por nombre.</summary>
    /// <remarks>
    /// Cuando lo conservado es exactamente lo generado mas un rabo de texto detras —el caso del
    /// <c>&lt;CNTY:10&gt;CA,VENTURA // Ventura</c> de Log4OM— no se sustituye el valor: se
    /// escribe el texto completo pero declarando la longitud del valor bueno. Asi el fichero
    /// sale igual que entro y volver a leerlo devuelve otra vez el condado limpio.
    /// </remarks>
    private static List<CampoAdif> Superponer(List<CampoAdif> campos, IReadOnlyList<QsoCampoExtra> extras)
    {
        if (extras.Count == 0) return campos;

        var indice = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < campos.Count; i++) indice[campos[i].Nombre] = i;

        foreach (var extra in extras)
        {
            var nombre = extra.Nombre.ToUpperInvariant();
            if (!indice.TryGetValue(nombre, out var i))
            {
                indice[nombre] = campos.Count;
                campos.Add(new CampoAdif(nombre, extra.Valor, extra.TipoAdif));
                continue;
            }

            var generado = campos[i].Valor;
            campos[i] = generado.Length > 0
                && extra.Valor.Length > generado.Length
                && extra.Valor.StartsWith(generado, StringComparison.Ordinal)
                    ? new CampoAdif(nombre, generado, extra.TipoAdif, extra.Valor)
                    : new CampoAdif(nombre, extra.Valor, extra.TipoAdif);
        }
        return campos;
    }

    /// <summary>
    /// Escribe la frecuencia con seis decimales, que es lo que hacen los cuadernos al uso y lo
    /// que evita que una ida y vuelta cambie <c>14.187000</c> por <c>14.187</c>.
    /// </summary>
    private static string EscribirFrecuencia(Frecuencia f) =>
        f.Megahercios.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Escribe el localizador con la capitalizacion habitual: el primer par en mayusculas, el
    /// tercero en minusculas y el quinto otra vez en mayusculas.
    /// </summary>
    private static string? EscribirLocator(Locator locator)
    {
        if (locator.EsVacio) return null;
        var v = locator.Valor;
        var destino = new char[v.Length];
        for (var i = 0; i < v.Length; i++)
        {
            destino[i] = i / 2 == 2 ? char.ToLowerInvariant(v[i]) : v[i];
        }
        return new string(destino);
    }
}
