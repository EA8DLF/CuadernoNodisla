using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Integraciones.Cluster;
using Nodisla.Cuaderno.Radio;
using Nodisla.Cuaderno.Radio.Control.Ft710;
using Nodisla.Cuaderno.Radio.Control.Rigctld;
using Nodisla.Cuaderno.Radio.Ptt;
using Serilog;

namespace Nodisla.Cuaderno.Ui.Ajustes;

/// <summary>
/// Como quiere el operador que se hable con el equipo.
/// </summary>
/// <remarks>
/// Los tiempos se guardan en milisegundos o en segundos —numeros de toda la vida— y no como
/// <c>TimeSpan</c>: un fichero de ajustes tiene que poder abrirse con el bloc de notas y
/// entenderse, y <c>"00:00:00.3500000"</c> no se entiende.
/// </remarks>
public sealed class AjustesDeEquipo
{
    /// <summary>Via por la que se controla el equipo.</summary>
    public ViaDeControl Via { get; set; } = ViaDeControl.Ninguna;

    /// <summary>
    /// Buscar el equipo por todos los puertos serie en vez de usar el puerto elegido.
    /// </summary>
    /// <remarks>
    /// Viene puesto de fabrica porque es lo que funciona: este equipo ha llegado a cambiar de
    /// puerto y de velocidad entre dos encendidos, de COM3 a 115200 a COM15 a 38400.
    /// </remarks>
    public bool DetectarElPuerto { get; set; } = true;

    /// <summary>
    /// Modelo del CAT nativo: clave del catalogo (<c>yaesu-ft710</c>, <c>icom-ic7300</c>...) o
    /// <c>auto</c> para buscarlo. Nulo en los ajustes de antes de haber mas modelos, que es lo
    /// mismo que <c>auto</c>.
    /// </summary>
    public string? Modelo { get; set; }

    /// <summary>Direccion CI-V de los ICOM si se ha cambiado en el equipo (nula = la de fabrica).</summary>
    public byte? DireccionCiv { get; set; }

    /// <summary>Puerto serie elegido, por ejemplo <c>COM3</c>.</summary>
    public string? Puerto { get; set; }

    /// <summary>
    /// Velocidad del puerto serie.
    /// </summary>
    /// <remarks>
    /// 38400 de partida porque es lo que trae el FT-710 de fabrica en su menu CAT RATE, y lo
    /// que tiene puesto el equipo de Jose. La velocidad no la manda el cable: la manda el menu
    /// del equipo, asi que este numero es una pista, no una certeza.
    /// </remarks>
    public int Baudios { get; set; } = 38400;

    /// <summary>Cada cuanto se le pregunta al equipo como esta, en milisegundos.</summary>
    public int SondeoMs { get; set; } = 500;

    /// <summary>Lo que se espera a que el equipo conteste una orden, en milisegundos.</summary>
    public int EsperaDeOrdenMs { get; set; } = 350;

    /// <summary>Por donde se sube el PTT.</summary>
    public ViaDePtt PttPor { get; set; } = ViaDePtt.Cat;

    /// <summary>Maquina donde escucha <c>rigctld</c>.</summary>
    public string MaquinaDeRigctld { get; set; } = "127.0.0.1";

    /// <summary>Puerto TCP donde escucha <c>rigctld</c>.</summary>
    public int PuertoDeRigctld { get; set; } = OpcionesRigctld.PuertoDeCostumbre;

    /// <summary>Cual de los dos equipos de OmniRig se usa: 1 o 2.</summary>
    public int EquipoDeOmniRig { get; set; } = 1;

    /// <summary>Tiempo maximo que se permite estar en antena de una vez, en segundos.</summary>
    public int TiempoMaximoSegundos { get; set; } = 180;

    /// <summary>Tiempo sin latido tras el cual se baja el PTT, en segundos.</summary>
    public int TiempoSinLatidoSegundos { get; set; } = 15;

    /// <summary>Pasa estos ajustes a lo que entiende el modulo de radio.</summary>
    /// <returns>Los ajustes de radio equivalentes.</returns>
    public OpcionesDeRadio AOpcionesDeRadio()
    {
        var opciones = new OpcionesDeRadio
        {
            Via = Via,
            Modelo = Modelo,
            DireccionCiv = DireccionCiv,
            Ft710 =
            {
                // El puerto viaja siempre, tambien con la deteccion puesta: es la PISTA por
                // donde se prueba primero, que es lo rapido. Quien decide si ademas se barren
                // todos los puertos es quien monta el control, no estos ajustes.
                Puerto = Puerto,
                Baudios = Baudios,
                IntervaloDeSondeo = TimeSpan.FromMilliseconds(Math.Max(50, SondeoMs)),
                EsperaDeOrden = TimeSpan.FromMilliseconds(Math.Max(50, EsperaDeOrdenMs)),
                ViaDePtt = PttPor,
            },
            Rigctld =
            {
                Maquina = string.IsNullOrWhiteSpace(MaquinaDeRigctld) ? "127.0.0.1" : MaquinaDeRigctld,
                Puerto = PuertoDeRigctld,
                IntervaloDeSondeo = TimeSpan.FromMilliseconds(Math.Max(50, SondeoMs)),
                EsperaDeOrden = TimeSpan.FromMilliseconds(Math.Max(200, EsperaDeOrdenMs)),
            },
            OmniRig =
            {
                NumeroDeEquipo = EquipoDeOmniRig is 1 or 2 ? EquipoDeOmniRig : 1,
                IntervaloDeSondeo = TimeSpan.FromMilliseconds(Math.Max(50, SondeoMs)),
            },
        };

        // Los tiempos del vigilante se acotan aqui y no en la pantalla: el tope duro lo pone el
        // propio vigilante y pasarse de el revienta. Mas vale recortar en silencio que no poder
        // guardar unos ajustes.
        var tope = OpcionesDelVigilante.TiempoMaximoPermitido;
        var maximo = TimeSpan.FromSeconds(Math.Clamp(TiempoMaximoSegundos, 1, (int)tope.TotalSeconds));
        opciones.Vigilante.TiempoMaximo = maximo;
        opciones.Vigilante.TiempoSinLatido = TimeSpan.FromSeconds(Math.Max(1, TiempoSinLatidoSegundos));

        return opciones;
    }
}

/// <summary>
/// Un nodo de cluster de la lista: a que maquina se entra y como.
/// </summary>
/// <remarks>
/// La contrasena, si el nodo la pide, no esta aqui: va al almacen cifrado con la clave que
/// da <see cref="ClaveDeContrasena"/>.
/// </remarks>
public sealed class AjustesDeNodoDeCluster
{
    /// <summary>Identificador del nodo. No cambia aunque se cambie el nombre o el servidor.</summary>
    public string Id { get; set; } = NuevoId();

    /// <summary>Nombre del nodo, el que se ve en pantalla y en la columna de origen.</summary>
    public string Nombre { get; set; } = string.Empty;

    /// <summary>Maquina a la que conectarse.</summary>
    public string Servidor { get; set; } = string.Empty;

    /// <summary>Puerto de Telnet.</summary>
    public int Puerto { get; set; } = 7300;

    /// <summary>Se conecta al conectar el cluster.</summary>
    public bool Activo { get; set; } = true;

    /// <summary>Red de escucha automatica (RBN): todo lo que trae se marca como «skimmer».</summary>
    public bool EsSkimmer { get; set; }

    /// <summary>Indicativo propio de este nodo. Vacio, el comun de todos los nodos.</summary>
    public string? Indicativo { get; set; }

    /// <summary>Sufijo propio de este nodo (<c>2</c> se manda como <c>EA8DLF-2</c>). Vacio, el comun.</summary>
    public string? Sufijo { get; set; }

    /// <summary>Ordenes que se mandan al conectar: los <c>set/</c> y <c>filter</c> de este nodo.</summary>
    public IList<string> GuionDeArranque { get; set; } = [.. OpcionesCluster.GuionPredeterminado];

    /// <summary>Volver a conectar solo cuando se cae la conexion, con espera creciente.</summary>
    public bool ReconectarSolo { get; set; } = true;

    /// <summary>Clave de la contrasena de este nodo en el almacen cifrado.</summary>
    [JsonIgnore]
    public string ClaveDeContrasena => ClavesDeCredencial.ContrasenaDeNodoDeCluster(Id);

    /// <summary>Un identificador nuevo, corto y que no se repite.</summary>
    /// <returns>Ocho cifras hexadecimales.</returns>
    public static string NuevoId() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>Copia del nodo, para editar sin tocar el guardado.</summary>
    /// <returns>Otro objeto con los mismos datos.</returns>
    public AjustesDeNodoDeCluster Copiar() => new()
    {
        Id = Id,
        Nombre = Nombre,
        Servidor = Servidor,
        Puerto = Puerto,
        Activo = Activo,
        EsSkimmer = EsSkimmer,
        Indicativo = Indicativo,
        Sufijo = Sufijo,
        GuionDeArranque = [.. GuionDeArranque],
        ReconectarSolo = ReconectarSolo,
    };
}

/// <summary>
/// Como se entra al cluster de DX: la lista de nodos y lo que comparten.
/// </summary>
/// <remarks>
/// <para>
/// <b>Aqui no hay contrasenas, y no es un olvido.</b> Cada una va al almacen cifrado con la
/// proteccion de datos de la cuenta de Windows (ver
/// <see cref="AjustesDeNodoDeCluster.ClaveDeContrasena"/>). Este fichero es texto plano en la
/// carpeta del usuario: lo que se escriba aqui lo lee cualquiera que abra el bloc de notas.
/// </para>
/// <para>
/// <b>Migracion.</b> Antes habia un solo nodo, con su nombre, servidor, puerto, guion y
/// reconexion sueltos en este mismo objeto. Un fichero de entonces se lee igual: ese nodo pasa
/// a ser el primero de la lista, con el identificador
/// <see cref="ClavesDeCredencial.NodoDeClusterPrincipal"/> para que su contrasena guardada
/// siga valiendo, y los campos sueltos dejan de escribirse.
/// </para>
/// </remarks>
public sealed class AjustesDeCluster
{
    /// <summary>Nodo con el que se arranca la primera vez.</summary>
    public const string ServidorDeFabrica = "cluster.ea4rch.es";

    private List<AjustesDeNodoDeCluster>? _nodos;

    /// <summary>
    /// Nodos a los que se conecta, en el orden en que se ensenan. El primero conectado es el
    /// que recibe las ordenes si no se elige otro.
    /// </summary>
    public List<AjustesDeNodoDeCluster> Nodos
    {
        get => _nodos ??= Migrados();
        set => _nodos = value;
    }

    /// <summary>
    /// Indicativo con el que se accede a todos los nodos que no digan otro. Vacio quiere
    /// decir «el del perfil de estacion activo».
    /// </summary>
    public string? Indicativo { get; set; }

    /// <summary>Sufijo comun del indicativo, para tener varias sesiones abiertas: <c>1</c>, <c>2</c>.</summary>
    public string? Sufijo { get; set; }

    /// <summary>Lo que se espera a que el socket abra, en segundos.</summary>
    public int EsperaDeConexionSegundos { get; set; } = 20;

    /// <summary>Espera antes del primer reintento, en segundos.</summary>
    public int PrimerReintentoSegundos { get; set; } = 5;

    /// <summary>Tope de la espera entre reintentos, en segundos.</summary>
    public int ReintentoMaximoSegundos { get; set; } = 300;

    /// <summary>Tiempo sin recibir nada tras el cual se da la conexion por muerta, en minutos.</summary>
    public int SilencioMaximoMinutos { get; set; } = 15;

    /// <summary>
    /// Diferencia de frecuencia, en kilohercios, por debajo de la cual dos anuncios de la
    /// misma estacion son el mismo.
    /// </summary>
    public decimal ToleranciaDeRepetidosKhz { get; set; } = 1.0m;

    /// <summary>Minutos durante los que un anuncio repetido se junta con el anterior.</summary>
    public int VentanaDeRepetidosMinutos { get; set; } = 10;

    // ── Lo de antes de tener varios nodos: se lee para migrar y no se vuelve a escribir ──

    /// <summary>Nombre del nodo unico de antes. Solo para migrar.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Nombre { get; set; }

    /// <summary>Servidor del nodo unico de antes. Solo para migrar.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Servidor { get; set; }

    /// <summary>Puerto del nodo unico de antes. Solo para migrar.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Puerto { get; set; }

    /// <summary>Guion del nodo unico de antes. Solo para migrar.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IList<string>? GuionDeArranque { get; set; }

    /// <summary>Reconexion del nodo unico de antes. Solo para migrar.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ReconectarSolo { get; set; }

    /// <summary>Tolerancia de repetidos ya acotada a algo razonable.</summary>
    [JsonIgnore]
    public decimal ToleranciaAcotada => Math.Clamp(ToleranciaDeRepetidosKhz, 0.1m, 10m);

    /// <summary>Ventana de repetidos ya acotada a algo razonable.</summary>
    [JsonIgnore]
    public TimeSpan VentanaAcotada => TimeSpan.FromMinutes(Math.Clamp(VentanaDeRepetidosMinutos, 1, 60));

    /// <summary>Pasa lo de antes a la lista de nodos, si no se habia hecho ya.</summary>
    /// <remarks>
    /// Si el fichero traia la lista nueva <b>y</b> los campos de antes (editado a mano, o una
    /// version que escribio los dos), manda la lista y lo de antes se tira.
    /// </remarks>
    public void Migrar()
    {
        _ = Nodos;
        LimpiarLoDeAntes();
    }

    /// <summary>
    /// Pasa un nodo a lo que entiende la integracion del cluster.
    /// </summary>
    /// <param name="nodo">Nodo de la lista.</param>
    /// <param name="indicativoComun">
    /// Indicativo comun ya resuelto (el escrito o el del perfil activo). Se usa si el nodo no
    /// dice otro.
    /// </param>
    /// <param name="contrasena">Contrasena sacada del almacen cifrado, o nula si no hay.</param>
    /// <returns>Las opciones del nodo.</returns>
    public OpcionesCluster AOpcionesDeNodo(AjustesDeNodoDeCluster nodo, Indicativo indicativoComun, string? contrasena)
    {
        ArgumentNullException.ThrowIfNull(nodo);

        var indicativo = !string.IsNullOrWhiteSpace(nodo.Indicativo)
            && Dominio.Valores.Indicativo.TryParse(nodo.Indicativo.Trim(), out var propio)
                ? propio
                : indicativoComun;
        var sufijo = string.IsNullOrWhiteSpace(nodo.Sufijo) ? Sufijo : nodo.Sufijo;

        return new OpcionesCluster
        {
            Id = nodo.Id,
            Nombre = string.IsNullOrWhiteSpace(nodo.Nombre) ? nodo.Servidor.Trim() : nodo.Nombre.Trim(),
            Servidor = nodo.Servidor.Trim(),
            Puerto = nodo.Puerto,
            Activo = nodo.Activo,
            EsSkimmer = nodo.EsSkimmer,
            Indicativo = indicativo,
            Sufijo = string.IsNullOrWhiteSpace(sufijo) ? null : sufijo.Trim().TrimStart('-'),
            Contrasena = string.IsNullOrWhiteSpace(contrasena) ? null : contrasena,
            GuionDeArranque = nodo.GuionDeArranque.Count > 0
                ? [.. nodo.GuionDeArranque]
                : OpcionesCluster.GuionPredeterminado,
            ReconectarSolo = nodo.ReconectarSolo,
            EsperaDeConexion = TimeSpan.FromSeconds(Math.Max(1, EsperaDeConexionSegundos)),
            EsperaPrimerReintento = TimeSpan.FromSeconds(Math.Max(1, PrimerReintentoSegundos)),
            EsperaMaximaReintento = TimeSpan.FromSeconds(Math.Max(5, ReintentoMaximoSegundos)),
            SilencioMaximo = TimeSpan.FromMinutes(Math.Max(1, SilencioMaximoMinutos)),
        };
    }

    /// <summary>
    /// Saca la lista de nodos de lo que hubiera de antes, o la de fabrica si no habia nada.
    /// </summary>
    private List<AjustesDeNodoDeCluster> Migrados()
    {
        var servidor = string.IsNullOrWhiteSpace(Servidor) ? ServidorDeFabrica : Servidor.Trim();
        var puerto = Puerto is > 0 and <= 65535 ? Puerto.Value : 7300;
        var conocido = NodosConocidos.Buscar(servidor, puerto);
        var nombre = !string.IsNullOrWhiteSpace(Nombre) ? Nombre.Trim() : conocido?.Nombre ?? servidor;

        var principal = new AjustesDeNodoDeCluster
        {
            Id = ClavesDeCredencial.NodoDeClusterPrincipal,
            Nombre = nombre,
            Servidor = servidor,
            Puerto = puerto,
            Activo = true,
            EsSkimmer = conocido?.EsSkimmer ?? false,
            GuionDeArranque = GuionDeArranque is { Count: > 0 } guion
                ? [.. guion]
                : [.. OpcionesCluster.GuionPredeterminado],
            ReconectarSolo = ReconectarSolo ?? true,
        };

        LimpiarLoDeAntes();
        return [principal];
    }

    private void LimpiarLoDeAntes()
    {
        Nombre = null;
        Servidor = null;
        Puerto = null;
        GuionDeArranque = null;
        ReconectarSolo = null;
    }
}


/// <summary>
/// El audio y los modos digitales: por donde entra el sonido y quien decodifica.
/// </summary>
/// <remarks>
/// <para>
/// Los dispositivos se guardan por <b>identificador</b> y ademas por nombre. El identificador
/// es lo que abre Windows; el nombre esta para poder decir <i>cual</i> era cuando el
/// identificador ya no existe —la radio apagada, el cable cambiado de sitio— en vez de dejar
/// una lista vacia sin explicacion.
/// </para>
/// <para>
/// <b>Aqui no se abre nada.</b> Que estos ajustes tengan un dispositivo escrito no significa
/// que el programa vaya a abrir el microfono de nadie al arrancar: la entrada se abre cuando
/// el operador pulsa escuchar, o cuando pide probar el nivel.
/// </para>
/// </remarks>
public sealed class AjustesDeDigital
{
    /// <summary>Nivel de entrada a partir del cual se avisa de que se está saturando.</summary>
    /// <remarks>
    /// No es un numero de gusto: el nivel de esta estacion llegaba a 0,91–0,99, al borde de
    /// recortar. Un codec saturado no decodifica mejor por ir mas fuerte, decodifica peor,
    /// porque el recorte se reparte por todo el ancho de banda y tapa a las senales debiles.
    /// </remarks>
    public const double NivelQueSatura = 0.90;

    /// <summary>Nivel de entrada por debajo del cual la senal se queda corta.</summary>
    public const double NivelQueSeQuedaCorto = 0.03;


    /// <summary>Identificador del dispositivo de captura, tal y como lo nombra Windows.</summary>
    public string? DispositivoDeEntrada { get; set; }

    /// <summary>Nombre del dispositivo de captura, para poder decir cual era si desaparece.</summary>
    public string? NombreDeEntrada { get; set; }

    /// <summary>Identificador del dispositivo de reproduccion.</summary>
    public string? DispositivoDeSalida { get; set; }

    /// <summary>Nombre del dispositivo de reproduccion.</summary>
    public string? NombreDeSalida { get; set; }

    /// <summary>Muestras por segundo con las que trabaja el modem.</summary>
    /// <remarks>48.000 es lo que da el codec del FT-710 y lo que esperan FT8 y FT4.</remarks>
    public int FrecuenciaDeMuestreo { get; set; } = 48000;

    /// <summary>Modo con el que arranca el modem.</summary>
    public ModoDelModem Modo { get; set; } = ModoDelModem.Ft8;

    /// <summary>Tono de transmision dentro del ancho de banda de audio, en hercios.</summary>
    public int TonoDeTransmisionHz { get; set; } = 1500;

    /// <summary>Tono de recepcion: donde se mira, en hercios. Es la «Rx Freq» de WSJT-X.</summary>
    public int TonoDeRecepcionHz { get; set; } = 1500;

    /// <summary>El tono de transmision no sigue a los clics ni al doble clic («Hold Tx Freq»).</summary>
    public bool MantenerTx { get; set; }

    /// <summary>
    /// Amplitud de pico (0 a 1) de la señal digital que se manda a la tarjeta de sonido al
    /// transmitir: el «Pwr» de JTDX. Vale para todos los modos (FT8, FT4, FST4, JT65, JT9,
    /// MSK144, Q65, WSPR) y para RTTY.
    /// </summary>
    /// <remarks>
    /// De fabrica, 0,3: deliberadamente conservador, no el maximo razonable (0,5, -6 dBFS) que
    /// llevaba el programa escrito a fuego antes de que existiera este ajuste. Una grabacion en
    /// bruto (WASAPI loopback) del audio que este programa manda a la tarjeta salio limpia para
    /// una transmision que en el aire sonaba a «golpes de ruido»: la señal en el ordenador era
    /// perfecta, y lo que esa grabacion no puede ver es que, ya en analogico, ese nivel sature la
    /// entrada de datos o la interfaz del equipo. Quien tenga una entrada que admita mas, puede
    /// subirlo; quien la tenga mas sensible, bajarlo mas todavia.
    /// </remarks>
    public double NivelDeSalida { get; set; } = 0.3;

    // ── La secuencia del contacto ──────────────────────────────────────────

    /// <summary>La secuencia del contacto va sola: CQ, localizador, informe, R+informe, RR73, 73.</summary>
    public bool SecuenciaAutomatica { get; set; } = true;

    /// <summary>Al contestar a un CQ se empieza por el informe, sin mandar el localizador. Es la opcion de JTDX.</summary>
    public bool SaltarTx1 { get; set; }

    /// <summary>Llamando CQ, se contesta solo al primero que llame («Call 1st»).</summary>
    public bool LlamarAlPrimero { get; set; }

    /// <summary>
    /// Llamando CQ con <see cref="LlamarAlPrimero"/>, no elegir automaticamente a quien ya se ha
    /// trabajado antes («Ans B4»). No afecta a la respuesta a mano.
    /// </summary>
    public bool AnsB4 { get; set; }

    /// <summary>
    /// Al completar un contacto que vino de llamar CQ con <see cref="LlamarAlPrimero"/>, parar del
    /// todo en vez de seguir llamando CQ solo para el siguiente que conteste («1 QSO»).
    /// </summary>
    public bool UnSoloQso { get; set; } = true;

    /// <summary>Tx4 va con <c>RRR</c> en vez de <c>RR73</c>.</summary>
    public bool Tx4ConRrr { get; set; }

    /// <summary>Emisiones seguidas sin oir al corresponsal tras las que la secuencia se para.</summary>
    public int CiclosSinRespuesta { get; set; } = 5;

    /// <summary>Sufijo de la llamada general: vacio, <c>DX</c>, <c>EU</c>, <c>POTA</c>…</summary>
    public string CqDirigido { get; set; } = string.Empty;

    /// <summary>Como se opera: normal, hound o uno de los concursos.</summary>
    public Digital.TipoDeOperacion Operacion { get; set; } = Digital.TipoDeOperacion.Normal;

    /// <summary>Intercambio de concurso, tal como se teclea: <c>2A EMA</c>, <c>579 MA</c>, <c>570123 IO91NP</c>.</summary>
    public string IntercambioDeConcurso { get; set; } = string.Empty;

    // ── Transmitir y apuntar ───────────────────────────────────────────────

    /// <summary>
    /// Preguntar antes de cada emision del modem. Se apaga con «No volver a preguntar» en el
    /// propio dialogo. El vigilante de PTT, el reloj y la salida abierta se exigen igual.
    /// </summary>
    public bool PedirConfirmacionAlTransmitir { get; set; } = true;

    /// <summary>El pestillo «Permitir transmitir» arranca abierto. De fabrica, cerrado.</summary>
    public bool RecordarPermisoDeTransmitir { get; set; }

    /// <summary>Apuntar solo en el cuaderno el contacto que la secuencia da por completo.</summary>
    public bool RegistrarAlCompletar { get; set; } = true;

    // ── El decodificador propio: AGCc y Filtrar ─────────────────────────────

    /// <summary>
    /// «AGCc»: control de ganancia/compresion del propio decodificador, aplicado al audio antes
    /// de decodificar. Distinto del AGC del equipo, que sigue siendo cosa del CAT.
    /// </summary>
    public bool AgcActivo { get; set; }

    /// <summary>«Filtrar»: filtra el audio real (no solo lo que se pinta) antes de decodificar.</summary>
    public bool FiltroActivo { get; set; }

    /// <summary>Limite inferior del filtro de paso de banda, en hercios.</summary>
    public int FiltroDesdeHz { get; set; } = 200;

    /// <summary>Limite superior del filtro de paso de banda, en hercios.</summary>
    public int FiltroHastaHz { get; set; } = 2900;

    // ── Modo SWL ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Modo SWL: solo escucha y registra lo oido, sin intencion de contestar. El secuenciador no
    /// reacciona y no se activa nada de Tx mientras este encendido.
    /// </summary>
    public bool ModoSwl { get; set; }

    // ── Red y ficheros ─────────────────────────────────────────────────────

    /// <summary>
    /// Mandar a PSK Reporter lo que se oye. <b>Es red saliente</b> y viene apagado: manda al mundo
    /// el indicativo y el localizador propios con cada decodificacion.
    /// </summary>
    public bool PskReporter { get; set; }

    /// <summary>Guardar en un WAV el audio de cada ventana, en la carpeta <c>wav</c> de los datos.</summary>
    public bool GuardarWav { get; set; }

    /// <summary>Apuntar todas las decodificaciones en <c>decodificaciones.txt</c>, como el ALL.TXT de WSJT-X.</summary>
    public bool GuardarDecodificacionesEnTexto { get; set; }

    // ── La cascada ─────────────────────────────────────────────────────────

    /// <summary>Ganancia de la cascada, en decibelios: mas brillo.</summary>
    public double GananciaDeLaCascadaDb { get; set; }

    /// <summary>Cero de la cascada, en decibelios: donde empieza el negro respecto al ruido.</summary>
    public double CeroDeLaCascadaDb { get; set; }

    /// <summary>Columnas que se promedian antes de pintar una fila: mas de una suaviza y saca las señales debiles.</summary>
    public int PromedioDeColumnas { get; set; } = 1;

    /// <summary>Paleta de la cascada.</summary>
    public string PaletaDeLaCascada { get; set; } = "Nodisla";

    /// <summary>Hasta que frecuencia se pinta, en hercios.</summary>
    public int AnchoVisibleHz { get; set; } = 3000;

    // ── Las frecuencias de trabajo ─────────────────────────────────────────

    /// <summary>Frecuencias de trabajo por banda y modo. De fabrica, las de WSJT-X.</summary>
    public List<Digital.FrecuenciaDeTrabajo> FrecuenciasDeTrabajo { get; set; } = Digital.FrecuenciasDeTrabajo.DeFabrica();

    /// <summary>Deja estos ajustes dentro de unos limites con sentido.</summary>
    /// <returns>Los mismos ajustes, ya acotados.</returns>
    public AjustesDeDigital Acotar()
    {
        // 48.000 es lo normal; se admiten 12.000 y 24.000 porque hay codecs que no dan mas.
        if (FrecuenciaDeMuestreo is not (12000 or 24000 or 48000 or 96000)) FrecuenciaDeMuestreo = 48000;

        // El tono se acota al ancho util de audio: por debajo de 200 Hz y por encima de 3.000
        // el filtro del equipo se lo come, y transmitir ahi es transmitir para nadie.
        TonoDeTransmisionHz = Math.Clamp(TonoDeTransmisionHz, 200, 3000);
        // Igual que exige Modulador.Sintetizar de cada modo: por encima de cero y hasta 1.
        NivelDeSalida = Math.Clamp(NivelDeSalida, 0.01, 1.0);
        TonoDeRecepcionHz = Math.Clamp(TonoDeRecepcionHz, 200, 5000);
        CiclosSinRespuesta = Math.Clamp(CiclosSinRespuesta, 1, 100);
        GananciaDeLaCascadaDb = Math.Clamp(GananciaDeLaCascadaDb, -30, 30);
        CeroDeLaCascadaDb = Math.Clamp(CeroDeLaCascadaDb, -30, 30);
        PromedioDeColumnas = Math.Clamp(PromedioDeColumnas, 1, 10);
        AnchoVisibleHz = Math.Clamp(AnchoVisibleHz, 1000, 5000);
        FiltroDesdeHz = Math.Clamp(FiltroDesdeHz, 50, 4500);
        FiltroHastaHz = Math.Clamp(FiltroHastaHz, FiltroDesdeHz + 50, 5000);
        CqDirigido ??= string.Empty;
        IntercambioDeConcurso ??= string.Empty;
        PaletaDeLaCascada = string.IsNullOrWhiteSpace(PaletaDeLaCascada) ? "Nodisla" : PaletaDeLaCascada;
        FrecuenciasDeTrabajo ??= Digital.FrecuenciasDeTrabajo.DeFabrica();
        FrecuenciasDeTrabajo.RemoveAll(f => f is null || f.Megahercios <= 0);
        return this;
    }
}

/// <summary>Como se sigue el Doppler de los satelites.</summary>
/// <remarks>
/// Solo guarda que VFO lleva cada sentido: el seguimiento en si nunca arranca solo, lo pide el
/// operador desde el panel de satelites cada vez.
/// </remarks>
public sealed class AjustesDeSatelites
{
    /// <summary>VFO por el que se transmite: el que lleva la subida.</summary>
    public NombreDeVfo VfoDeSubida { get; set; } = NombreDeVfo.A;

    /// <summary>VFO por el que se escucha: el que lleva la bajada.</summary>
    public NombreDeVfo VfoDeBajada { get; set; } = NombreDeVfo.B;
}

/// <summary>Como se dejo la pantalla de impresion de QSL la ultima vez.</summary>
public sealed class AjustesDeImpresion
{
    /// <summary>Referencia de la plantilla de etiquetas elegida.</summary>
    public string Plantilla { get; set; } = "L7160 / 3652";

    /// <summary>En que casilla de la primera hoja se empieza a imprimir.</summary>
    public int PrimeraCasilla { get; set; }

    /// <summary>Dibujar el contorno de la etiqueta, para la prueba en papel normal.</summary>
    public bool Contorno { get; set; }

    /// <summary>Frase al pie de las etiquetas.</summary>
    public string Mensaje { get; set; } = string.Empty;
}

/// <summary>
/// Las cuentas de los servicios en linea y la subida automatica.
/// </summary>
/// <remarks>
/// <para>
/// Aqui van los <b>usuarios</b>, que no son secretos. Las contrasenas y claves van al almacen
/// cifrado, como siempre. Un usuario vacio quiere decir «el indicativo del perfil de estacion
/// activo», que es lo que usan QRZ.com, LoTW, eQSL y HamQTH en la inmensa mayoria de los casos.
/// </para>
/// <para>
/// Las casillas de subida son <c>bool?</c> a proposito: nulo es «lo que salga de las
/// credenciales» (activada si hay credenciales guardadas). Solo cuando el operador toca la
/// casilla se queda fija.
/// </para>
/// </remarks>
public sealed class AjustesDeServicios
{
    /// <summary>Completar los contactos con la ficha de QRZ.com / HamQTH.</summary>
    public bool CompletarConQrz { get; set; } = true;

    /// <summary>Usuario de QRZ.com para la consulta XML. Vacio: el indicativo del perfil.</summary>
    public string? UsuarioQrz { get; set; }

    /// <summary>Usuario de HamQTH, la consulta de reserva. Vacio: el indicativo del perfil.</summary>
    public string? UsuarioHamQth { get; set; }

    /// <summary>Usuario de LoTW. Vacio: el indicativo del perfil.</summary>
    public string? UsuarioLotw { get; set; }

    /// <summary>Ubicacion de estacion de TQSL con la que se firma.</summary>
    public string? UbicacionTqsl { get; set; }

    /// <summary>Ruta de tqsl.exe si no esta donde se instala siempre.</summary>
    public string? RutaTqsl { get; set; }

    /// <summary>Usuario de eQSL.cc. Vacio: el indicativo del perfil.</summary>
    public string? UsuarioEqsl { get; set; }

    /// <summary>Apodo del QTH en eQSL, si la cuenta tiene varios.</summary>
    public string? ApodoEqsl { get; set; }

    /// <summary>Correo de la cuenta de Club Log.</summary>
    public string? CorreoClubLog { get; set; }

    /// <summary>Indicativo del cuaderno de Club Log. Vacio: el del perfil.</summary>
    public string? IndicativoClubLog { get; set; }

    /// <summary>Subir solo a LoTW. Nulo: si hay credenciales.</summary>
    public bool? SubirALotw { get; set; }

    /// <summary>Subir solo a eQSL. Nulo: si hay credenciales.</summary>
    public bool? SubirAEqsl { get; set; }

    /// <summary>Subir solo a Club Log. Nulo: si hay credenciales.</summary>
    public bool? SubirAClubLog { get; set; }

    /// <summary>Subir solo al cuaderno de QRZ.com. Nulo: si hay credenciales.</summary>
    public bool? SubirAQrz { get; set; }
}

/// <summary>
/// Lo que el operador ha configurado del equipo y del cluster.
/// </summary>
/// <remarks>
/// <para>
/// Va en un fichero aparte de <see cref="EstadoDeLosPaneles"/> a proposito. Aquel guarda como
/// dejo la ventana —que panel estaba abierto, cuanto medía la columna—, y se escribe solo al
/// cerrar. Esto es <b>configuracion</b>: la escribe el operador a sabiendas, la puede querer
/// copiar a otro ordenador y la puede querer leer con el bloc de notas cuando algo no conecta.
/// </para>
/// <para>
/// <b>Los secretos no estan aqui.</b> Van al almacen cifrado con DPAPI, que solo puede
/// descifrar la cuenta de Windows que los escribio.
/// </para>
/// </remarks>
public sealed class AjustesDelPrograma
{
    /// <summary>Nombre del fichero dentro de la carpeta de datos del programa.</summary>
    public const string NombreDelFichero = "ajustes.json";

    private static readonly JsonSerializerOptions Formato = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Como se habla con el equipo.</summary>
    public AjustesDeEquipo Equipo { get; set; } = new();

    /// <summary>Como se entra al cluster.</summary>
    public AjustesDeCluster Cluster { get; set; } = new();

    /// <summary>El audio y los modos digitales.</summary>
    public AjustesDeDigital Digital { get; set; } = new();

    /// <summary>El seguimiento Doppler de satelites.</summary>
    public AjustesDeSatelites Satelites { get; set; } = new();

    /// <summary>La pantalla de impresion de etiquetas de QSL.</summary>
    public AjustesDeImpresion Impresion { get; set; } = new();

    /// <summary>Las cuentas de QRZ.com, LoTW, eQSL, Club Log y HamQTH, y la subida automatica.</summary>
    public AjustesDeServicios Servicios { get; set; } = new();

    /// <summary>Fonía por el ordenador: altavoces, micrófono y PTT de fonía.</summary>
    public AjustesDeFonia Fonia { get; set; } = new();

    /// <summary>El decodificador de telegrafía.</summary>
    public AjustesDeCw Cw { get; set; } = new();

    /// <summary>RTTY: decodificador y emisor propios.</summary>
    public AjustesDeRtty Rtty { get; set; } = new();

    /// <summary>El analizador de la propia radio: spots encima, clic, suelo de ruido y colores.</summary>
    public AjustesDelAnalizador Analizador { get; set; } = new();

    /// <summary>
    /// Idioma del programa: «es», «en», «pt», «fr», «it» o «de». Nulo, el del sistema si es uno
    /// de esos seis; si no, inglés.
    /// </summary>
    public string? Idioma { get; set; }

    /// <summary>Lee los ajustes guardados, o devuelve los de fabrica si no hay ninguno.</summary>
    /// <param name="carpeta">Carpeta de datos del programa.</param>
    /// <returns>Los ajustes leidos, nunca nulos.</returns>
    public static AjustesDelPrograma Leer(string carpeta)
    {
        try
        {
            var ruta = Path.Combine(carpeta, NombreDelFichero);
            if (!File.Exists(ruta)) return new AjustesDelPrograma();

            var leidos = JsonSerializer.Deserialize<AjustesDelPrograma>(File.ReadAllText(ruta), Formato);
            if (leidos is null) return new AjustesDelPrograma();

            // Un fichero a medias —editado a mano, o de una version anterior— deja partes
            // nulas. Se rellenan con las de fabrica en vez de reventar al primer uso.
            leidos.Equipo ??= new AjustesDeEquipo();
            leidos.Cluster ??= new AjustesDeCluster();
            leidos.Cluster.Migrar();
            leidos.Digital ??= new AjustesDeDigital();
            leidos.Digital.Acotar();
            leidos.Satelites ??= new AjustesDeSatelites();
            leidos.Impresion ??= new AjustesDeImpresion();
            leidos.Servicios ??= new AjustesDeServicios();
            leidos.Fonia ??= new AjustesDeFonia();
            leidos.Fonia.Acotar();
            leidos.Cw ??= new AjustesDeCw();
            leidos.Cw.Acotar();
            leidos.Rtty ??= new AjustesDeRtty();
            leidos.Rtty.Acotar();
            leidos.Analizador ??= new AjustesDelAnalizador();
            leidos.Analizador.Acotar();
            return leidos;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "No se han podido leer los ajustes; se usan los de fábrica.");
            return new AjustesDelPrograma();
        }
    }

    /// <summary>
    /// Guarda los ajustes en la carpeta de datos del programa.
    /// </summary>
    /// <remarks>
    /// Se escribe primero un fichero temporal y luego se reemplaza el bueno. Un corte de luz en
    /// mitad de la escritura dejaria el fichero a medias, y un fichero de ajustes a medias es
    /// un programa que arranca sin saber por donde esta la radio.
    /// </remarks>
    /// <param name="carpeta">Carpeta de datos del programa.</param>
    public void Guardar(string carpeta)
    {
        try
        {
            Directory.CreateDirectory(carpeta);
            Cluster.Migrar();

            var ruta = Path.Combine(carpeta, NombreDelFichero);
            var temporal = ruta + ".nuevo";
            File.WriteAllText(temporal, JsonSerializer.Serialize(this, Formato));

            if (File.Exists(ruta)) File.Replace(temporal, ruta, null);
            else File.Move(temporal, ruta);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "No se han podido guardar los ajustes.");
        }
    }
}
