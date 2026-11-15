using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Ui.Digital;

/// <summary>Un mensaje decodificado con lo que el secuenciador necesita de el.</summary>
/// <param name="Mensaje">El mensaje entendido.</param>
/// <param name="Decibelios">Con cuanta señal se oyo: es el informe que se le manda.</param>
/// <param name="TonoHz">Donde estaba: en hound es donde hay que ir a contestar al fox.</param>
/// <param name="TrabajadoAntes">Ya se ha trabajado a ese indicativo: para el filtro de «AnsB4».</param>
public readonly record struct MensajeOido(MensajeEstandar Mensaje, int Decibelios, int TonoHz, bool TrabajadoAntes = false);

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
    /// <summary>
    /// El corresponsal ha cambiado sin pasar por una parada: se ha dejado al anterior (contacto
    /// acabado, o desaparecido) y se atiende a este, que esperaba en la cola o repite porque no
    /// copio el cierre. Quien recibe la decision tiene que cerrar lo del anterior <b>antes</b> de
    /// tomar los datos del nuevo. Nulo si el corresponsal sigue siendo el mismo.
    /// </summary>
    public string? PasaA { get; init; }

    /// <summary>
    /// Con <see cref="PasaA"/>: el contacto que se deja habia terminado de verdad (el otro cerro
    /// con RR73/RRR/73, o salio el cierre propio), y no es que se abandone a medias.
    /// </summary>
    public bool AnteriorTerminado { get; init; }

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

    /// <summary>
    /// Llamando CQ, elegir solo a quien conteste —al primero, si son varios— y contestarle en la
    /// siguiente ventana propia. De fabrica, puesto: sin esto hay que elegir a mano a cada uno que
    /// llama, y se pasa. Quitado, la cola de espera se sigue llenando, pero nadie se elige solo.
    /// </summary>
    public bool LlamarAlPrimero { get; set; } = true;

    /// <summary>
    /// Tx4 va con <c>RRR</c> en vez de <c>RR73</c>. Cambia cuando acaba un contacto: <c>RR73</c>
    /// es «recibido y adios» y no pide respuesta, asi que el contacto se cierra al emitirlo;
    /// <c>RRR</c> es solo «recibido», y hay que esperar el 73 del otro.
    /// </summary>
    public bool Tx4ConRrr { get; set; }

    /// <summary>
    /// Al acabar un contacto nacido del CQ, pasar solo al siguiente de la cola. Quien lo apaga es
    /// la pantalla cuando el contacto que acaba no ha quedado apuntado en el cuaderno: pasar al
    /// siguiente borraria de la vista los datos que el operador aun tiene que guardar.
    /// </summary>
    public bool PasarSoloAlSiguiente { get; set; } = true;

    /// <summary>
    /// Los que han contestado al CQ y esperan su turno. Solo se usa en operacion
    /// <see cref="TipoDeOperacion.Normal"/> y en una sesion nacida de llamar CQ.
    /// </summary>
    public ColaDeLlamantes Cola { get; } = new();

    /// <summary>
    /// Llamando CQ con <see cref="LlamarAlPrimero"/>, no elegir automaticamente a una estacion ya
    /// trabajada antes («Ans B4» de JTDX). No afecta a la respuesta a mano (doble clic): eso lo
    /// sigue decidiendo el operador, vea la lista como la vea.
    /// </summary>
    public bool AnsB4 { get; set; }

    /// <summary>
    /// Al completar un contacto que vino de llamar CQ con <see cref="LlamarAlPrimero"/>, parar del
    /// todo en vez de seguir llamando CQ solo para el siguiente que conteste («Enable Tx» de JTDX
    /// durante un pileup, si esto no esta marcado). De fabrica, a true: no cambiar de la noche a
    /// la mañana el comportamiento de siempre (pararse tras cada contacto) sin que el operador lo
    /// pida.
    /// </summary>
    public bool UnSoloQso { get; set; } = true;

    /// <summary>
    /// La sesion en marcha viene de un «Llamar CQ» (<see cref="LlamarCq"/>) y no se ha estrechado
    /// a un contacto elegido a mano: es la que puede seguir sola de un contacto al siguiente.
    /// </summary>
    private bool _llamandoCqSolo;

    /// <summary>
    /// Con quienes ya se ha completado contacto desde que se esta en esta banda y modo: si
    /// vuelven a llamar no se les elige solos ni hacen cola (seria apuntar dos veces lo mismo).
    /// </summary>
    private readonly HashSet<string> _completadosEnLaSesion = new(StringComparer.Ordinal);

    /// <summary>
    /// El ultimo contacto que se cerro al emitir el RR73 propio, sin esperar confirmacion. Si no
    /// lo copio y repite su R+informe mientras se llama CQ, se le repite el RR73 con estos datos.
    /// </summary>
    private (string Indicativo, string Grid, int? Enviado, int? Recibido)? _cerradoConRr73;

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
        Cola.Vaciar();
        _cerradoConRr73 = null;
        Activo = true;
        TxActual = 6;
        Paridad = ParidadDeLaSiguiente(ahora, Periodo);
        _llamandoCqSolo = true;
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
        // Es una orden a mano del operador, no la continuacion de un «Llamar CQ» en marcha...
        // salvo que lo que mande sea precisamente el CQ: mandar el Tx6 a mano es llamar CQ, igual
        // que con el boton, y quien conteste se atiende igual.
        if (tx == 6)
        {
            if (!_llamandoCqSolo) Cola.Vaciar();
            _llamandoCqSolo = true;
        }
        else
        {
            _llamandoCqSolo = false;
            Cola.Vaciar();
        }

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

        // Lo que se oye de uno mismo (el propio CQ decodificado) no es una estacion a la que llamar.
        if (EsMio(m.Llamante)) return DecisionDelSecuenciador.Nada;

        Limpiar();
        Activo = true;
        // Es el operador quien ha elegido a mano con el doble clic: no es la continuacion de un
        // «Llamar CQ», asi que al completarse no sigue llamando CQ solo, y los que esperaban en
        // la cola del CQ dejan de esperar.
        _llamandoCqSolo = false;
        Cola.Vaciar();
        _cerradoConRr73 = null;
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

        var esLaPropia = Paridad >= 0 && ParidadDe(ventana, Periodo) == Paridad;
        if (!esLaPropia)
        {
            // Lo que se oye de uno mismo no cuenta: ni llama, ni contesta, ni hace cola.
            if (oidos.Any(o => EsMio(o.Mensaje.Llamante))) oidos = oidos.Where(o => !EsMio(o.Mensaje.Llamante)).ToList();
            AtenderALaCola(ventana, oidos);
        }

        if (!Activo) return DecisionDelSecuenciador.Nada;

        // La ventana que acaba de cerrar era la propia: aqui no se decide nada.
        if (esLaPropia) return DecisionDelSecuenciador.Nada;

        var paraMi = oidos.Where(o => VaDirigidoAMi(o.Mensaje)).ToList();
        var delDx = DxCall.Length > 0
            ? paraMi.Where(o => o.Mensaje.LoManda(DxCall) || (o.Mensaje.Segundo?.LoManda(DxCall) ?? false)).ToList()
            : [];

        var completoAntes = _completado;
        int? tono = null;
        string? pasaA = null;

        if (TxActual == 6 && RepiteElQueNoCopioElRr73(paraMi) is { } repite)
        {
            // Se le repite el RR73. Ese contacto ya se dio por completo en su momento: no se
            // vuelve a anunciar, que seria apuntarlo dos veces.
            Limpiar();
            DxCall = repite.Indicativo;
            DxGrid = repite.Grid;
            InformeEnviado = repite.Enviado;
            InformeRecibido = repite.Recibido;
            TxActual = 4;
            _completado = true;
            completoAntes = true;
            pasaA = DxCall;
        }
        else if (TxActual == 6 && HayCola)
        {
            if (LlamarAlPrimero && Cola.Siguiente(AnsB4) is { } siguiente)
            {
                Atender(siguiente);
            }
        }
        else if (TxActual == 6)
        {
            if (LlamarAlPrimero && paraMi.Count > 0)
            {
                // AnsB4: no se elige a quien ya se ha trabajado. Sigue en la lista y se le puede
                // contestar a mano, pero la seleccion automatica lo salta.
                var candidatos = AnsB4 ? paraMi.Where(o => !o.TrabajadoAntes).ToList() : paraMi;
                var primero = candidatos.FirstOrDefault(o => o.Mensaje.Clase is ClaseDeMensaje.Llamada or ClaseDeMensaje.Informe);
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
                            return TerminarYSeguir(Textos.T("Digital.Secuencia.ContactoTerminado"), completoAntes);
                        }

                        TxActual = 5;
                        break;

                    case ClaseDeMensaje.S73:
                        _completado = true;
                        if (TxActual is 4 or 5) return TerminarYSeguir(Textos.T("Digital.Secuencia.ContactoTerminado"), completoAntes);
                        TxActual = 5;
                        break;
                }
            }
        }

        // Los vigilantes: llamando CQ sin que nadie conteste, o con el corresponsal desaparecido.
        if (TxActual == 6 && EmisionesSinNoticias >= CiclosMaximosLlamandoCq)
        {
            return Parar(Textos.F("Digital.Secuencia.VigilanteCq", EmisionesSinNoticias), completoAntes);
        }

        if (TxActual != 6 && EmisionesSinNoticias >= CiclosSinRespuesta)
        {
            // Si venia del CQ y hay quien espera, no se deja a los demas colgados por uno que se
            // ha ido: se pasa al siguiente. Sin nadie esperando se para, como siempre (aqui no se
            // vuelve a CQ solo: a un abandono lo mira el operador).
            var veniaDeCqSolo = _llamandoCqSolo;
            var parada = Parar(Textos.F("Digital.Secuencia.Desaparecido", DxCall, EmisionesSinNoticias), completoAntes);
            return SeguirSiToca(parada, veniaDeCqSolo, volverACq: false);
        }

        return new DecisionDelSecuenciador(TxActual, tono, _completado && !completoAntes, null) { PasaA = pasaA };
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
            // Capturado ANTES de terminar: Terminar/Parar apaga _llamandoCqSolo al parar, y aqui
            // hace falta saber como estaba la sesion justo antes de cerrarse.
            var veniaDeCqSolo = _llamandoCqSolo;
            var completoAntes = _completado;
            _completado = true;
            var decision = Terminar(Textos.T("Digital.Secuencia.Enviado73"), completoAntes);
            return SeguirSiToca(decision, veniaDeCqSolo, volverACq: true);
        }

        if (tx == 4 && ElRr73PropioCierraElContacto)
        {
            // RR73 es «recibido y adios»: no pide respuesta. Emitido, el contacto esta acabado por
            // este lado, llegue o no el 73 del otro; ni se le espera ni se repite el RR73 a ciegas.
            // Si no lo copio y repite su R+informe, se le vuelve a mandar (vease Procesar).
            _cerradoConRr73 = (DxCall, DxGrid, InformeEnviado, InformeRecibido);
            var decision = Terminar(Textos.T("Digital.Secuencia.ContactoTerminado"), completoAntes: true);
            return SeguirSiToca(decision, veniaDeCqSolo: true, volverACq: true);
        }

        return DecisionDelSecuenciador.Nada;
    }

    /// <summary>
    /// El contacto en curso se cierra al emitir el RR73 propio: nacio del CQ, en operacion normal,
    /// ya esta completo (se recibio su R+informe) y Tx4 es <c>RR73</c> y no <c>RRR</c>.
    /// </summary>
    /// <remarks>
    /// Solo se aplica a los contactos nacidos del CQ: el contestado a mano con el doble clic se
    /// queda como siempre (espera el 73 del otro). Con <c>RRR</c> tambien se espera.
    /// </remarks>
    private bool ElRr73PropioCierraElContacto =>
        TxActual == 4 && _completado && _llamandoCqSolo && !Tx4ConRrr && Operacion == TipoDeOperacion.Normal;

    /// <summary>La cola de espera esta en uso: operacion normal y sesion nacida de llamar CQ.</summary>
    private bool HayCola => _llamandoCqSolo && Operacion == TipoDeOperacion.Normal;

    /// <summary>
    /// El operador elige a uno de la lista de espera: se le contesta ya, dejando lo que hubiera a
    /// medias. La sesion sigue siendo la del CQ (al acabar con el se sigue con la cola), que es
    /// la diferencia con el doble clic sobre una decodificacion.
    /// </summary>
    /// <param name="indicativo">A quien, de los que esperan.</param>
    /// <param name="ahora">Instante de la accion, para saber si la siguiente ventana ya es la propia.</param>
    public DecisionDelSecuenciador AtenderDeLaCola(string indicativo, DateTimeOffset ahora)
    {
        if (Cola.Buscar(indicativo) is not { } llamante) return DecisionDelSecuenciador.Nada;

        // Si la secuencia estaba parada del todo y sin paridad, se le contesta en la contraria a
        // la suya.
        if (Paridad < 0) Paridad = 1 - ParidadDe(llamante.OidoEn, Periodo);

        // El que se deja: si ya estaba completo, cuenta como hecho aunque no llegara a salir el cierre.
        if (_completado && DxCall.Length > 0) _completadosEnLaSesion.Add(DxCall);

        Atender(llamante);
        var yaToca = ParidadDeLaSiguiente(ahora, Periodo) == Paridad;
        return new DecisionDelSecuenciador(yaToca ? TxActual : null, null, false, null) { PasaA = llamante.Indicativo };
    }

    /// <summary>
    /// Cambio de banda o de frecuencia: los que esperaban ya no estan ahi, y lo trabajado en la
    /// otra banda no cuenta en esta. No para la secuencia: eso lo decide quien la lleva.
    /// </summary>
    public void OlvidarLaSesion()
    {
        Cola.Vaciar();
        _completadosEnLaSesion.Clear();
        _cerradoConRr73 = null;
    }

    /// <summary>Pone a uno de la cola de corresponsal: Tx2 con su informe, o Tx3 si abrio con el suyo.</summary>
    private void Atender(LlamanteEnEspera llamante)
    {
        Limpiar();
        Activo = true;
        _llamandoCqSolo = true;
        DxCall = llamante.Indicativo;
        DxGrid = llamante.Localizador;
        InformeEnviado = llamante.Decibelios;

        if (llamante.AbrioConInforme)
        {
            InformeRecibido = llamante.InformeRecibido;
            IntercambioRecibido = llamante.IntercambioRecibido;
            TxActual = 3;
        }
        else
        {
            TxActual = 2;
        }

        Cola.Quitar(llamante.Indicativo);
    }

    /// <summary>
    /// Pone la cola al dia con lo oido en una ventana de los otros: entra quien me llama, sale
    /// quien se va con otro o llama CQ, y caduca quien lleva demasiado sin oirse.
    /// </summary>
    private void AtenderALaCola(DateTimeOffset ventana, IReadOnlyList<MensajeOido> oidos)
    {
        var admite = Activo && HayCola;
        if (!admite && Cola.EnEspera.Count == 0) return;

        foreach (var oido in oidos)
        {
            var m = oido.Mensaje;
            if (m.Llamante.Length == 0) continue;

            if (m.VaDirigidoA(MiIndicativo))
            {
                if (!admite) continue;
                if (m.Clase is not (ClaseDeMensaje.Llamada or ClaseDeMensaje.Informe)) continue;
                if (string.Equals(m.Llamante, DxCall, StringComparison.Ordinal)) continue;
                if (_completadosEnLaSesion.Contains(m.Llamante)) continue;
                Cola.Anotar(oido, ventana);
            }
            else if (!VaDirigidoAMi(m))
            {
                // Llama CQ, o llama o contesta a otro: ya no me esta esperando.
                Cola.Quitar(m.Llamante);
            }
        }

        Cola.Caducar(ventana, Periodo);
    }

    /// <summary>
    /// Llamando CQ, el corresponsal al que se despidio con RR73 repite su R+informe: no lo copio.
    /// </summary>
    private (string Indicativo, string Grid, int? Enviado, int? Recibido)? RepiteElQueNoCopioElRr73(List<MensajeOido> paraMi)
    {
        if (_cerradoConRr73 is not { } cerrado || !HayCola) return null;

        return paraMi.Any(o => o.Mensaje.Clase == ClaseDeMensaje.InformeConR && o.Mensaje.LoManda(cerrado.Indicativo))
            ? cerrado
            : null;
    }

    /// <summary>Da el contacto por terminado y, si venia del CQ, sigue con la cola o con el CQ.</summary>
    private DecisionDelSecuenciador TerminarYSeguir(string motivo, bool completoAntes)
    {
        var veniaDeCqSolo = _llamandoCqSolo;
        return SeguirSiToca(Terminar(motivo, completoAntes), veniaDeCqSolo, volverACq: true);
    }

    /// <summary>
    /// Lo que pasa tras cerrarse (o abandonarse) un contacto que venia de llamar CQ, en vez de
    /// pararse del todo.
    /// </summary>
    /// <remarks>
    /// Por este orden: si hay alguien valido esperando en la cola, se pasa directamente a el —sin
    /// CQ por medio, y diga lo que diga <see cref="UnSoloQso"/>: el que espera manda—; si no, y
    /// <see cref="UnSoloQso"/> no esta marcado, se vuelve a Tx6 y se sigue llamando CQ; si no, se
    /// para como siempre. Un contacto contestado a mano se para siempre.
    /// </remarks>
    /// <param name="decision">La parada que se iba a devolver.</param>
    /// <param name="veniaDeCqSolo">Como estaba la sesion justo antes de cerrarse: Parar lo apaga.</param>
    /// <param name="volverACq">Sin nadie en la cola, se puede volver a llamar CQ (si «1 QSO» no esta marcado).</param>
    private DecisionDelSecuenciador SeguirSiToca(DecisionDelSecuenciador decision, bool veniaDeCqSolo, bool volverACq)
    {
        if (!veniaDeCqSolo || decision.Parada is null) return decision;

        if (Operacion == TipoDeOperacion.Normal && LlamarAlPrimero && PasarSoloAlSiguiente
            && Cola.Siguiente(AnsB4) is { } siguiente)
        {
            var terminado = Terminado;
            Atender(siguiente);
            return decision with { Tx = TxActual, Parada = null, PasaA = siguiente.Indicativo, AnteriorTerminado = terminado };
        }

        if (UnSoloQso || !volverACq) return decision;

        Limpiar();
        Activo = true;
        TxActual = 6;
        _llamandoCqSolo = true;
        return decision with { Tx = 6, Parada = null };
    }

    /// <summary>
    /// El operador marca a mano el siguiente mensaje: si la secuencia esta en marcha, ese es el
    /// que sale en la proxima ventana propia. Parada, no la arranca.
    /// </summary>
    public void ElegirSiguiente(int tx)
    {
        if (!Activo || tx is < 1 or > 6) return;
        TxActual = tx;

        // Marcar el CQ como siguiente es ponerse a llamar CQ: quien conteste se atiende igual
        // que con el boton.
        if (tx == 6) _llamandoCqSolo = true;
    }

    /// <summary>El operador ha parado a mano.</summary>
    public void Parar()
    {
        Activo = false;
        TxActual = 0;
        _llamandoCqSolo = false;
        Cola.Vaciar();
        _cerradoConRr73 = null;
    }

    /// <summary>Cambia de modo: el periodo es otro y la paridad no vale.</summary>
    public void CambiarPeriodo(TimeSpan periodo)
    {
        Periodo = periodo;
        Parar();
        OlvidarLaSesion();
        Paridad = -1;
    }

    private DecisionDelSecuenciador Parar(string motivo, bool completoAntes)
    {
        // Con quien se ha completado ya no hay nada pendiente en esta sesion: ni cola ni eleccion.
        if (_completado && DxCall.Length > 0)
        {
            _completadosEnLaSesion.Add(DxCall);
            Cola.Quitar(DxCall);
        }

        Activo = false;
        TxActual = 0;
        _llamandoCqSolo = false;
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

    /// <summary>Ese indicativo es el propio, con o sin sufijo de operacion.</summary>
    private bool EsMio(string indicativo) =>
        indicativo.Length > 0 && MiIndicativo.Length > 0 && MismoIndicativo.Son(indicativo, MiIndicativo);

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
