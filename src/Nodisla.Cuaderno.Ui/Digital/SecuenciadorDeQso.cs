namespace Nodisla.Cuaderno.Ui.Digital;

/// <summary>Un mensaje decodificado con lo que el secuenciador necesita de el.</summary>
/// <param name="Mensaje">El mensaje entendido.</param>
/// <param name="Decibelios">Con cuanta señal se oyo: es el informe que se le manda.</param>
/// <param name="TonoHz">Donde estaba: en hound es donde hay que ir a contestar al fox.</param>
public readonly record struct MensajeOido(MensajeEstandar Mensaje, int Decibelios, int TonoHz);

/// <summary>Lo que el secuenciador decide tras una ventana o una accion del operador.</summary>
/// <param name="Tx">Mensaje que toca emitir en la proxima ventana propia (1 a 6), o nulo si nada.</param>
/// <param name="TonoTx">Tono al que hay que mover la transmision, si toca moverla (hound).</param>
/// <param name="ContactoCompleto">Ha quedado hecho el contacto: es el momento de apuntarlo.</param>
/// <param name="Parada">Se ha parado, y por que. Nulo si sigue.</param>
public readonly record struct DecisionDelSecuenciador(
    int? Tx,
    int? TonoTx,
    bool ContactoCompleto,
    string? Parada)
{
    /// <summary>No hay nada que hacer.</summary>
    public static DecisionDelSecuenciador Nada => new(null, null, false, null);
}

/// <summary>
/// La secuencia automatica del contacto, como la de WSJT-X: decide <b>que mensaje toca y
/// cuando</b>, y nada mas.
/// </summary>
/// <remarks>
/// <para>
/// No transmite, no abre audio, no toca el PTT: devuelve el numero del mensaje que habria que
/// emitir en la siguiente ventana propia y quien lo llama decide si lo emite de verdad, pasando
/// por el pestillo y el vigilante. Por eso se puede probar entero sin radio.
/// </para>
/// <para>
/// <b>La paridad manda.</b> En estos modos cada uno habla en ventanas alternas: si el
/// corresponsal transmite en las pares, uno transmite en las impares. El secuenciador se queda
/// con la paridad de las ventanas propias y solo decide al cerrarse una ventana del otro; las
/// propias las ignora, que es lo que evita pisar al corresponsal cuando el operador llega tarde
/// con el doble clic.
/// </para>
/// <para>
/// <b>Nada de reloj de pared.</b> Todo se calcula con las horas de las ventanas y con el «ahora»
/// que le pasan, asi que en pruebas el tiempo es un dato mas.
/// </para>
/// </remarks>
public sealed class SecuenciadorDeQso
{
    private bool _completado;

    /// <summary>Lo que dura una ventana del modo en curso.</summary>
    public TimeSpan Periodo { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>El indicativo propio, para saber que va dirigido a uno.</summary>
    public string MiIndicativo { get; set; } = string.Empty;

    /// <summary>Como se opera.</summary>
    public TipoDeOperacion Operacion { get; set; }

    /// <summary>Al contestar a un CQ, empezar por el informe (Tx2) en vez de por el localizador (Tx1). Es la opcion de JTDX.</summary>
    public bool SaltarTx1 { get; set; }

    /// <summary>Llamando CQ, contestar solo al primero que llame.</summary>
    public bool LlamarAlPrimero { get; set; }

    /// <summary>Emisiones seguidas sin noticias del corresponsal tras las que se para.</summary>
    public int CiclosSinRespuesta { get; set; } = 5;

    /// <summary>Emisiones seguidas llamando CQ sin que nadie conteste tras las que se para (el vigilante de Tx).</summary>
    public int CiclosMaximosLlamandoCq { get; set; } = 24;

    /// <summary>La secuencia esta en marcha.</summary>
    public bool Activo { get; private set; }

    /// <summary>Mensaje que toca (1 a 6), o 0 si ninguno.</summary>
    public int TxActual { get; private set; }

    /// <summary>Paridad de las ventanas propias: 0 o 1, o -1 si aun no se ha fijado.</summary>
    public int Paridad { get; private set; } = -1;

    /// <summary>El corresponsal.</summary>
    public string DxCall { get; private set; } = string.Empty;

    /// <summary>Su localizador, si lo ha dicho.</summary>
    public string DxGrid { get; private set; } = string.Empty;

    /// <summary>Informe que se le manda.</summary>
    public int? InformeEnviado { get; private set; }

    /// <summary>Informe que ha mandado el.</summary>
    public int? InformeRecibido { get; private set; }

    /// <summary>Intercambio de concurso que ha mandado el.</summary>
    public string IntercambioRecibido { get; private set; } = string.Empty;

    /// <summary>Emisiones seguidas sin oir al corresponsal (o sin que nadie conteste al CQ).</summary>
    public int EmisionesSinNoticias { get; private set; }

    /// <summary>
    /// La secuencia se ha parado porque el contacto ha terminado de verdad: el corresponsal ha
    /// cerrado con RR73/RRR/73, o se ha enviado el 73. No se pone si se para por abandono.
    /// </summary>
    public bool Terminado { get; private set; }

    /// <summary>La ultima ventana que se ha procesado.</summary>
    public DateTimeOffset? UltimaVentana { get; private set; }

    /// <summary>Paridad de la ventana que empieza en ese instante.</summary>
    public static int ParidadDe(DateTimeOffset ventana, TimeSpan periodo) =>
        (int)((ventana.UtcTicks / periodo.Ticks) & 1);

    /// <summary>Paridad de la primera ventana que empieza despues de «ahora».</summary>
    public static int ParidadDeLaSiguiente(DateTimeOffset ahora, TimeSpan periodo) =>
        (int)(((ahora.UtcTicks / periodo.Ticks) + 1) & 1);

    /// <summary>Empieza a llamar CQ. Sale en la siguiente ventana, sea cual sea su paridad.</summary>
    public DecisionDelSecuenciador LlamarCq(DateTimeOffset ahora)
    {
        Limpiar();
        Activo = true;
        TxActual = 6;
        Paridad = ParidadDeLaSiguiente(ahora, Periodo);
        return new DecisionDelSecuenciador(6, null, false, null);
    }

    /// <summary>El operador ha elegido un mensaje a mano: sale en la siguiente ventana.</summary>
    public DecisionDelSecuenciador Enviar(int tx, DateTimeOffset ahora)
    {
        if (tx is < 1 or > 6) return DecisionDelSecuenciador.Nada;

        Activo = true;
        Terminado = false;
        TxActual = tx;
        if (Paridad < 0) Paridad = ParidadDeLaSiguiente(ahora, Periodo);
        EmisionesSinNoticias = 0;
        return new DecisionDelSecuenciador(tx, null, false, null);
    }

    /// <summary>
    /// El doble clic sobre una decodificacion: fija el corresponsal, la paridad y el mensaje con
    /// el que se le contesta.
    /// </summary>
    /// <param name="oido">Lo que se ha decodificado.</param>
    /// <param name="ventana">Ventana en la que se oyo: se le contesta en la contraria.</param>
    /// <param name="ahora">Instante de la accion, para saber si la siguiente ventana ya es la propia.</param>
    /// <returns>
    /// Con <c>Tx</c> si se puede emitir ya —la siguiente ventana es de la paridad propia— o sin el
    /// si hay que esperar a que cierre la ventana del otro.
    /// </returns>
    public DecisionDelSecuenciador Iniciar(MensajeOido oido, DateTimeOffset ventana, DateTimeOffset ahora)
    {
        var m = oido.Mensaje;
        if (m.Llamante.Length == 0 || m.Clase == ClaseDeMensaje.Otro) return DecisionDelSecuenciador.Nada;

        Limpiar();
        Activo = true;
        DxCall = m.Llamante;
        DxGrid = m.Locator;
        InformeEnviado = oido.Decibelios;
        Paridad = 1 - ParidadDe(ventana, Periodo);

        var paraMi = m.VaDirigidoA(MiIndicativo);
        int? tono = null;

        switch (m.Clase)
        {
            case ClaseDeMensaje.Cq:
            case ClaseDeMensaje.Llamada when !paraMi:
                TxActual = Operacion == TipoDeOperacion.Hound ? 1 : SaltarTx1 ? 2 : 1;
                break;

            case ClaseDeMensaje.Llamada:
                // Me llama con su localizador: le contesto con el informe (o el intercambio).
                TxActual = 2;
                break;

            case ClaseDeMensaje.Informe:
                if (!paraMi) { TxActual = SaltarTx1 ? 2 : 1; break; }
                InformeRecibido = m.Informe;
                IntercambioRecibido = m.Intercambio;
                TxActual = 3;
                if (Operacion == TipoDeOperacion.Hound) tono = oido.TonoHz;
                break;

            case ClaseDeMensaje.InformeConR:
                if (!paraMi) { TxActual = SaltarTx1 ? 2 : 1; break; }
                InformeRecibido = m.Informe;
                IntercambioRecibido = m.Intercambio;
                TxActual = 4;
                _completado = true;
                break;

            case ClaseDeMensaje.Rr73:
            case ClaseDeMensaje.Rrr:
                if (!paraMi) { TxActual = SaltarTx1 ? 2 : 1; break; }
                TxActual = 5;
                _completado = true;
                break;

            case ClaseDeMensaje.S73:
                TxActual = paraMi ? 5 : (SaltarTx1 ? 2 : 1);
                break;

            default:
                Activo = false;
                return DecisionDelSecuenciador.Nada;
        }

        var yaToca = ParidadDeLaSiguiente(ahora, Periodo) == Paridad;
        return new DecisionDelSecuenciador(yaToca ? TxActual : null, tono, _completado, null);
    }

    /// <summary>
    /// Ha cerrado una ventana: con lo que se ha oido, decide que toca en la siguiente.
    /// </summary>
    /// <param name="ventana">Instante en que empezo la ventana que acaba de cerrar.</param>
    /// <param name="oidos">Lo decodificado en ella.</param>
    public DecisionDelSecuenciador Procesar(DateTimeOffset ventana, IReadOnlyList<MensajeOido> oidos)
    {
        ArgumentNullException.ThrowIfNull(oidos);
        UltimaVentana = ventana;

        if (!Activo) return DecisionDelSecuenciador.Nada;

        // La ventana que acaba de cerrar era la propia: aqui no se decide nada.
        if (Paridad >= 0 && ParidadDe(ventana, Periodo) == Paridad) return DecisionDelSecuenciador.Nada;

        var paraMi = oidos.Where(o => VaDirigidoAMi(o.Mensaje)).ToList();
        var delDx = DxCall.Length > 0
            ? paraMi.Where(o => o.Mensaje.LoManda(DxCall) || (o.Mensaje.Segundo?.LoManda(DxCall) ?? false)).ToList()
            : [];

        var completoAntes = _completado;
        int? tono = null;

        if (TxActual == 6)
        {
            if (LlamarAlPrimero && paraMi.Count > 0)
            {
                var primero = paraMi.FirstOrDefault(o => o.Mensaje.Clase is ClaseDeMensaje.Llamada or ClaseDeMensaje.Informe);
                if (primero.Mensaje is not null)
                {
                    DxCall = primero.Mensaje.Llamante;
                    DxGrid = primero.Mensaje.Locator;
                    InformeEnviado = primero.Decibelios;
                    EmisionesSinNoticias = 0;

                    if (primero.Mensaje.Clase == ClaseDeMensaje.Informe)
                    {
                        InformeRecibido = primero.Mensaje.Informe;
                        IntercambioRecibido = primero.Mensaje.Intercambio;
                        TxActual = 3;
                    }
                    else
                    {
                        TxActual = 2;
                    }
                }
            }
        }
        else if (delDx.Count > 0)
        {
            EmisionesSinNoticias = 0;

            foreach (var oido in delDx)
            {
                var m = oido.Mensaje.VaDirigidoA(MiIndicativo) ? oido.Mensaje : oido.Mensaje.Segundo!;
                var clase = Interpretar(m);

                switch (clase)
                {
                    case ClaseDeMensaje.Llamada:
                        // Sigue llamandome con su localizador: no ha oido mi informe. Se repite.
                        if (m.Locator.Length > 0) DxGrid = m.Locator;
                        if (TxActual == 1) TxActual = 2;
                        break;

                    case ClaseDeMensaje.Informe:
                        InformeRecibido = m.Informe;
                        IntercambioRecibido = m.Intercambio;
                        if (TxActual is 1 or 2 or 3) TxActual = 3;
                        if (Operacion == TipoDeOperacion.Hound) tono = oido.TonoHz;
                        break;

                    case ClaseDeMensaje.InformeConR:
                        InformeRecibido ??= m.Informe;
                        if (IntercambioRecibido.Length == 0) IntercambioRecibido = m.Intercambio;
                        if (TxActual is 1 or 2 or 3) { TxActual = 4; _completado = true; }
                        break;

                    case ClaseDeMensaje.Rr73:
                    case ClaseDeMensaje.Rrr:
                        _completado = true;
                        if (Operacion == TipoDeOperacion.Hound || TxActual == 4)
                        {
                            // El fox no espera un 73; y si ya mande RR73 y el contesta RR73, hemos terminado.
                            return Terminar("Contacto terminado.", completoAntes);
                        }

                        TxActual = 5;
                        break;

                    case ClaseDeMensaje.S73:
                        _completado = true;
                        if (TxActual == 4) return Terminar("Contacto terminado.", completoAntes);
                        if (TxActual == 5) return Terminar("Contacto terminado.", completoAntes);
                        TxActual = 5;
                        break;
                }
            }
        }

        // Los vigilantes: llamando CQ sin que nadie conteste, o con el corresponsal desaparecido.
        if (TxActual == 6 && EmisionesSinNoticias >= CiclosMaximosLlamandoCq)
        {
            return Parar($"Vigilante de Tx: {EmisionesSinNoticias} llamadas de CQ sin respuesta.", completoAntes);
        }

        if (TxActual != 6 && EmisionesSinNoticias >= CiclosSinRespuesta)
        {
            return Parar($"{DxCall} ha desaparecido: {EmisionesSinNoticias} emisiones sin oírle.", completoAntes);
        }

        return new DecisionDelSecuenciador(TxActual, tono, _completado && !completoAntes, null);
    }

    /// <summary>
    /// El mensaje ha salido al aire. Cuenta la emision y, si era el 73, da la secuencia por
    /// terminada.
    /// </summary>
    public DecisionDelSecuenciador EmisionHecha(int tx)
    {
        if (!Activo) return DecisionDelSecuenciador.Nada;

        EmisionesSinNoticias++;

        if (tx == 5)
        {
            var completoAntes = _completado;
            _completado = true;
            return Terminar("Enviado el 73.", completoAntes);
        }

        return DecisionDelSecuenciador.Nada;
    }

    /// <summary>
    /// El operador marca a mano el siguiente mensaje: si la secuencia esta en marcha, ese es el
    /// que sale en la proxima ventana propia. Parada, no la arranca.
    /// </summary>
    public void ElegirSiguiente(int tx)
    {
        if (!Activo || tx is < 1 or > 6) return;
        TxActual = tx;
    }

    /// <summary>El operador ha parado a mano.</summary>
    public void Parar()
    {
        Activo = false;
        TxActual = 0;
    }

    /// <summary>Cambia de modo: el periodo es otro y la paridad no vale.</summary>
    public void CambiarPeriodo(TimeSpan periodo)
    {
        Periodo = periodo;
        Parar();
        Paridad = -1;
    }

    private DecisionDelSecuenciador Parar(string motivo, bool completoAntes)
    {
        Activo = false;
        TxActual = 0;
        return new DecisionDelSecuenciador(null, null, _completado && !completoAntes, motivo);
    }

    private DecisionDelSecuenciador Terminar(string motivo, bool completoAntes)
    {
        Terminado = true;
        return Parar(motivo, completoAntes);
    }

    private void Limpiar()
    {
        Terminado = false;
        DxCall = string.Empty;
        DxGrid = string.Empty;
        InformeEnviado = null;
        InformeRecibido = null;
        IntercambioRecibido = string.Empty;
        EmisionesSinNoticias = 0;
        _completado = false;
        TxActual = 0;
    }

    private bool VaDirigidoAMi(MensajeEstandar m) =>
        m.VaDirigidoA(MiIndicativo) || (m.Segundo?.VaDirigidoA(MiIndicativo) ?? false);

    /// <summary>
    /// En los concursos donde el intercambio es el localizador, una «llamada con localizador» del
    /// corresponsal cuando ya estamos en marcha es su intercambio, no una llamada.
    /// </summary>
    private ClaseDeMensaje Interpretar(MensajeEstandar m)
    {
        if (m.Clase == ClaseDeMensaje.Llamada
            && m.Locator.Length > 0
            && GramaticaDeMensajes.IntercambioEsLocalizador(Operacion)
            && TxActual is 1 or 2)
        {
            return ClaseDeMensaje.Informe;
        }

        return m.Clase;
    }
}
