using Nodisla.Cuaderno.Aplicacion.Puertos;

namespace Nodisla.Cuaderno.Integraciones.Digital;

/// <summary>
/// Lee un datagrama del protocolo UDP de WSJT-X y devuelve el mensaje que contiene.
/// </summary>
/// <remarks>
/// El analizador no lanza nunca. Un datagrama que no cuadra se descarta con un motivo, y uno
/// que se corta a media lectura devuelve el mensaje con los campos que si llegaron. Es la
/// unica forma de sobrevivir a que el programa del otro lado saque una version nueva: si el
/// mensaje crece por el final, lo de antes se sigue entendiendo; si aparece un tipo nuevo, se
/// descarta ese datagrama y no se pierde el resto.
/// </remarks>
public static class AnalizadorWsjt
{
    /// <summary>Numero magico con el que empieza todo datagrama del protocolo.</summary>
    public const uint Magia = 0xADBC_CBDAu;

    /// <summary>Esquema mas bajo que se sabe leer. El 1 es el de Qt 5.0 y esta roto de origen.</summary>
    public const uint EsquemaMinimo = 2;

    /// <summary>Esquema mas alto que se sabe leer, que es el que usan todos desde hace anos.</summary>
    public const uint EsquemaMaximo = 3;

    /// <summary>Lee un datagrama sin saber de quien viene.</summary>
    /// <param name="datagrama">Bytes recibidos.</param>
    public static ResultadoDeAnalisis Analizar(ReadOnlySpan<byte> datagrama) =>
        Analizar(datagrama, DialectoDigital.Desconocido);

    /// <summary>Lee un datagrama sabiendo ya que dialecto habla la instancia que lo envia.</summary>
    /// <param name="datagrama">Bytes recibidos.</param>
    /// <param name="dialectoConocido">
    /// Dialecto que ya se le habia atribuido a esa instancia. Solo importa en el mensaje de
    /// estado, que es el unico que se lee distinto segun quien lo mande.
    /// </param>
    public static ResultadoDeAnalisis Analizar(ReadOnlySpan<byte> datagrama, DialectoDigital dialectoConocido)
    {
        var lector = new LectorQDataStream(datagrama);

        if (!lector.TryUInt32(out var magia))
            return new ResultadoDeAnalisis(null, MotivoDeDescarte.DemasiadoCorto, 0,
                $"El datagrama trae {datagrama.Length} bytes y no llega ni a la magia.");

        if (magia != Magia)
            return new ResultadoDeAnalisis(null, MotivoDeDescarte.MagiaIncorrecta, 0,
                $"La magia es 0x{magia:X8} y deberia ser 0x{Magia:X8}.");

        if (!lector.TryUInt32(out var esquema))
            return new ResultadoDeAnalisis(null, MotivoDeDescarte.DemasiadoCorto, 0,
                "El datagrama se acaba antes del numero de esquema.");

        if (esquema is < EsquemaMinimo or > EsquemaMaximo)
            return new ResultadoDeAnalisis(null, MotivoDeDescarte.EsquemaNoAdmitido, esquema,
                $"El esquema {esquema} no se sabe leer; se admiten del {EsquemaMinimo} al {EsquemaMaximo}.");

        if (!lector.TryUInt32(out var tipo))
            return new ResultadoDeAnalisis(null, MotivoDeDescarte.DemasiadoCorto, esquema,
                "El datagrama se acaba antes del tipo de mensaje.");

        if (!lector.TryTexto(out var id))
            return new ResultadoDeAnalisis(null, MotivoDeDescarte.Truncado, esquema,
                $"El mensaje de tipo {tipo} se corta antes del identificador de la instancia.");

        var identificador = id ?? string.Empty;
        var clase = (TipoMensajeWsjt)tipo;

        MensajeWsjt mensaje = clase switch
        {
            TipoMensajeWsjt.Latido => LeerLatido(ref lector, identificador),
            TipoMensajeWsjt.Estado => LeerEstado(ref lector, identificador, dialectoConocido),
            TipoMensajeWsjt.Decodificacion => LeerDecodificacion(ref lector, identificador),
            TipoMensajeWsjt.QsoRegistrado => LeerQsoRegistrado(ref lector, identificador),
            TipoMensajeWsjt.AdifRegistrado => LeerAdif(ref lector, identificador),
            TipoMensajeWsjt.Cierre => new CierreWsjt(identificador),
            _ => new MensajeSinDetallar(identificador, clase, Resto(ref lector, datagrama)),
        };

        return new ResultadoDeAnalisis(mensaje, MotivoDeDescarte.Ninguno, esquema, null);
    }

    private static byte[] Resto(ref LectorQDataStream lector, ReadOnlySpan<byte> datagrama) =>
        datagrama[lector.Posicion..].ToArray();

    private static LatidoWsjt LeerLatido(ref LectorQDataStream lector, string id)
    {
        lector.TryUInt32(out var esquemaMaximo);
        lector.TryTexto(out var version);
        lector.TryTexto(out var revision);
        return new LatidoWsjt(id, esquemaMaximo, version, revision);
    }

    private static EstadoWsjt LeerEstado(ref LectorQDataStream lector, string id, DialectoDigital dialecto)
    {
        // Hasta «modo rapido» los dos dialectos dicen exactamente lo mismo.
        lector.TryUInt64(out var dial);
        lector.TryTexto(out var modo);
        lector.TryTexto(out var dxCall);
        lector.TryTexto(out var informe);
        lector.TryTexto(out var modoTx);
        lector.TryLogico(out var txHabilitado);
        lector.TryLogico(out var transmitiendo);
        lector.TryLogico(out var decodificando);
        lector.TryUInt32(out var rxDf);
        lector.TryUInt32(out var txDf);
        lector.TryTexto(out var deCall);
        lector.TryTexto(out var deGrid);
        lector.TryTexto(out var dxGrid);
        lector.TryLogico(out var perroGuardian);
        lector.TryTexto(out var submodo);
        var hayModoRapido = lector.TryLogico(out var modoRapido);

        var estado = new EstadoWsjt(id)
        {
            DialHz = dial,
            Modo = modo,
            IndicativoDx = dxCall,
            Informe = informe,
            ModoTx = modoTx,
            TxHabilitado = txHabilitado,
            Transmitiendo = transmitiendo,
            Decodificando = decodificando,
            RxDf = rxDf,
            TxDf = txDf,
            IndicativoDe = deCall,
            LocatorDe = deGrid,
            LocatorDx = dxGrid,
            PerroGuardianTx = perroGuardian,
            Submodo = submodo,
            ModoRapido = modoRapido,
        };

        if (!hayModoRapido) return estado;

        // Aqui se separan los caminos: un byte que WSJT-X llama modo de operacion especial
        // y JTDX llama «transmito yo primero». Ocupan lo mismo, asi que no se distinguen por
        // el byte sino por lo que viene detras: WSJT-X sigue con cuatro campos mas y JTDX no.
        if (!lector.TryByte(out var divergente)) return estado;
        var quedaMas = !lector.Agotado;
        var terminoComoJtdx = !quedaMas;

        var pareceJtdx = dialecto is DialectoDigital.Jtdx
            || (dialecto is DialectoDigital.Desconocido && terminoComoJtdx);

        if (pareceJtdx)
        {
            return estado with { TxPrimero = divergente != 0, TerminoComoJtdx = terminoComoJtdx };
        }

        lector.TryUInt32(out var tolerancia);
        lector.TryUInt32(out var periodo);
        lector.TryTexto(out var configuracion);
        lector.TryTexto(out var mensajeTx);

        return estado with
        {
            ModoDeOperacionEspecial = divergente,
            ToleranciaDeFrecuencia = tolerancia,
            PeriodoTr = periodo,
            NombreDeConfiguracion = configuracion,
            MensajeTx = mensajeTx,
            TerminoComoJtdx = terminoComoJtdx,
        };
    }

    private static DecodificacionWsjt LeerDecodificacion(ref LectorQDataStream lector, string id)
    {
        lector.TryLogico(out var nueva);
        lector.TryHora(out var hora);
        lector.TryInt32(out var snr);
        lector.TryDoble(out var desfase);
        lector.TryUInt32(out var delta);
        lector.TryTexto(out var modo);
        lector.TryTexto(out var mensaje);

        var decodificacion = new DecodificacionWsjt(id, nueva, hora, snr, desfase, delta, modo, mensaje);

        if (lector.TryLogico(out var bajaConfianza))
            decodificacion = decodificacion with { BajaConfianza = bajaConfianza };
        if (lector.TryLogico(out var fueraDeAire))
            decodificacion = decodificacion with { FueraDeAire = fueraDeAire };

        return decodificacion;
    }

    private static QsoRegistradoWsjt LeerQsoRegistrado(ref LectorQDataStream lector, string id)
    {
        lector.TryFechaHoraUtc(out var fin);
        lector.TryTexto(out var dxCall);
        lector.TryTexto(out var dxGrid);
        lector.TryUInt64(out var frecuencia);
        lector.TryTexto(out var modo);
        lector.TryTexto(out var informeEnviado);
        lector.TryTexto(out var informeRecibido);
        lector.TryTexto(out var potencia);
        lector.TryTexto(out var comentarios);
        lector.TryTexto(out var nombre);
        lector.TryFechaHoraUtc(out var inicio);
        lector.TryTexto(out var operador);
        lector.TryTexto(out var miCall);
        lector.TryTexto(out var miGrid);

        var qso = new QsoRegistradoWsjt(
            id, fin, dxCall, dxGrid, frecuencia, modo, informeEnviado, informeRecibido,
            potencia, comentarios, nombre, inicio, operador, miCall, miGrid);

        // Los tres que siguen son de WSJT-X. JTDX corta el mensaje aqui y no pasa nada.
        if (lector.TryTexto(out var intercambioEnviado))
            qso = qso with { IntercambioEnviado = intercambioEnviado ?? string.Empty };
        if (lector.TryTexto(out var intercambioRecibido))
            qso = qso with { IntercambioRecibido = intercambioRecibido ?? string.Empty };
        if (lector.TryTexto(out var propagacion))
            qso = qso with { ModoDePropagacion = propagacion };

        return qso;
    }

    private static AdifRegistradoWsjt LeerAdif(ref LectorQDataStream lector, string id)
    {
        lector.TryTexto(out var texto);
        return new AdifRegistradoWsjt(id, texto);
    }
}
