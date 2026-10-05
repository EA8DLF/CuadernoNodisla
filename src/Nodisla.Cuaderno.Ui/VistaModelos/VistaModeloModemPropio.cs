using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Ui.Ajustes;
using Nodisla.Cuaderno.Ui.Conversores;
using Nodisla.Cuaderno.Ui.Digital;
using Nodisla.Cuaderno.Ui.Recursos;
using Serilog;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>
/// Como esta el codigo corrector con el que arranco el modem.
/// </summary>
/// <remarks>
/// Se pasa asi, en dos datos sueltos, y no con el objeto de las tablas, para que la pantalla
/// no tenga que conocer el modulo de los modos. Lo unico que necesita saber es si lo que se
/// esta decodificando vale para hablar con el mundo o solo con uno mismo.
/// </remarks>
/// <param name="EsElCodigoReal">
/// Falso mientras se use el codigo de pruebas: el modem funciona entero pero <b>solo se
/// entiende consigo mismo</b>, y eso hay que decirlo con todas las letras.
/// </param>
/// <param name="Procedencia">De donde salieron las tablas, para poder decirlo.</param>
public sealed record EstadoDelCorrector(bool EsElCodigoReal, string Procedencia)
{
    /// <summary>Cuando no hay modem propio detras y no procede decir nada.</summary>
    public static EstadoDelCorrector NoProcede { get; } = new(true, "no procede");
}

/// <summary>Una opcion de un selector: el valor y como se llama.</summary>
/// <typeparam name="T">Tipo del valor.</typeparam>
/// <remarks>
/// El nombre y el detalle se piden al leerlos y la opcion avisa al cambiar de idioma: asi el
/// selector sigue al idioma en caliente sin rehacer la lista (ni perder lo elegido).
/// </remarks>
public sealed class Opcion<T> : ObservableObject
{
    private readonly Func<string> _nombre;
    private readonly Func<string> _detalle;

    /// <summary>Una opcion con textos fijos.</summary>
    /// <param name="valor">El valor.</param>
    /// <param name="nombre">Como se ve.</param>
    /// <param name="detalle">Una segunda linea, si la hay.</param>
    public Opcion(T valor, string nombre, string detalle = "")
        : this(valor, () => nombre, () => detalle)
    {
    }

    /// <summary>Una opcion cuyos textos dependen del idioma.</summary>
    /// <param name="valor">El valor.</param>
    /// <param name="nombre">Como se ve, en el idioma en uso.</param>
    /// <param name="detalle">Una segunda linea, si la hay.</param>
    public Opcion(T valor, Func<string> nombre, Func<string>? detalle = null)
    {
        ArgumentNullException.ThrowIfNull(nombre);
        Valor = valor;
        _nombre = nombre;
        _detalle = detalle ?? (() => string.Empty);
        Textos.AlCambiar(this, static o => o.OnPropertyChanged(string.Empty));
    }

    /// <summary>El valor.</summary>
    public T Valor { get; }

    /// <summary>Como se ve.</summary>
    public string Nombre => _nombre();

    /// <summary>Una segunda linea, si la hay.</summary>
    public string Detalle => _detalle();

    /// <inheritdoc />
    public override string ToString() => Nombre;
}

/// <summary>Uno de los seis mensajes de la secuencia, tal y como se ve y se edita.</summary>
public sealed partial class MensajeTx : ObservableObject
{
    /// <summary>Monta el mensaje.</summary>
    public MensajeTx(int numero) => Numero = numero;

    /// <summary>1 a 6.</summary>
    public int Numero { get; }

    /// <summary>«Tx1»…</summary>
    public string Etiqueta => $"Tx{Numero}";

    /// <summary>El texto, editable.</summary>
    [ObservableProperty]
    private string _texto = string.Empty;

    /// <summary>Es el que toca en la proxima ventana propia.</summary>
    [ObservableProperty]
    private bool _esElSiguiente;
}

/// <summary>Una decodificacion del modem propio, tal y como se lee en la lista.</summary>
public sealed class FilaDeDecodificacionPropia
{
    /// <summary>Monta la fila.</summary>
    /// <param name="decodificacion">Lo que saco el modem.</param>
    /// <param name="novedad">Que tiene de nuevo el indicativo.</param>
    /// <param name="miIndicativo">El indicativo propio, para saber si el mensaje va dirigido a uno.</param>
    /// <param name="mensaje">El mensaje ya entendido. Si no se da, se analiza aqui.</param>
    /// <param name="tonoRxHz">Donde esta la recepcion, para marcar lo que cae cerca.</param>
    public FilaDeDecodificacionPropia(
        DecodificacionPropia decodificacion,
        NovedadDelIndicativo? novedad = null,
        Indicativo miIndicativo = default,
        MensajeEstandar? mensaje = null,
        int tonoRxHz = 0)
    {
        ArgumentNullException.ThrowIfNull(decodificacion);

        Decodificacion = decodificacion;
        Novedad = novedad ?? NovedadDelIndicativo.Desconocida;
        Mensaje = mensaje ?? InterpreteDeMensajes.Analizar(decodificacion.Texto);

        // OJO: «me llaman» NO es «el mensaje va dirigido a alguien». Casi todos los mensajes
        // de FT8 van dirigidos a alguien, y pintarlos todos de color acento dejaria la lista
        // entera resaltada, que es lo mismo que no resaltar nada. Se compara con el
        // indicativo propio.
        MeLlaman = !miIndicativo.EsVacio
                   && (Mensaje.VaDirigidoA(miIndicativo.Valor)
                       || (Mensaje.Segundo?.VaDirigidoA(miIndicativo.Valor) ?? false)
                       || (!decodificacion.Llamado.EsVacio
                           && string.Equals(decodificacion.Llamado.Valor, miIndicativo.Valor, StringComparison.Ordinal)));

        // Lo que sale de un WAV no trae hora (llega el 1-1-1970): «00:00:00» era mentira.
        Hora = decodificacion.VentanaUtc.Year < 2000
            ? Textos.T("Digital.Modem.Fichero")
            : decodificacion.VentanaUtc.UtcDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        Decibelios = decodificacion.Decibelios.ToString("+00;-00;+00", CultureInfo.InvariantCulture);
        Desfase = decodificacion.DesfaseSegundos.ToString("+0.0;-0.0;0.0", Textos.Cultura);
        Tono = decodificacion.TonoHz.ToString("N0", Textos.Cultura);
        Texto = decodificacion.Texto;
        Indicativo = decodificacion.Llamante.EsVacio ? Mensaje.Llamante : decodificacion.Llamante.Valor;
        Localizador = decodificacion.Locator.EsVacio ? Mensaje.Locator : decodificacion.Locator.Valor;
        Modo = DescripcionDelModo.Nombre(decodificacion.Modo);
        CercaDeRx = tonoRxHz > 0 && Math.Abs(decodificacion.TonoHz - tonoRxHz) <= 50;
        Aporta = Novedad.QueAporta.Count == 0 ? string.Empty : string.Join('\n', Novedad.QueAporta);
    }

    private FilaDeDecodificacionPropia(string texto, int tonoHz, DateTimeOffset ventana, ModoDelModem modo)
    {
        Decodificacion = new DecodificacionPropia(texto, 0, 0, tonoHz, modo, ventana);
        Novedad = NovedadDelIndicativo.Desconocida;
        Mensaje = InterpreteDeMensajes.Analizar(texto);
        Hora = ventana.UtcDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        Decibelios = "Tx";
        Desfase = string.Empty;
        Tono = tonoHz.ToString("N0", Textos.Cultura);
        Texto = texto;
        Indicativo = Mensaje.Llamado;
        Localizador = string.Empty;
        Modo = DescripcionDelModo.Nombre(modo);
        EsTx = true;
        Aporta = string.Empty;
    }

    /// <summary>Una fila de lo que se ha emitido, para que la secuencia se lea entera.</summary>
    public static FilaDeDecodificacionPropia DeTransmision(string texto, int tonoHz, DateTimeOffset ventana, ModoDelModem modo) =>
        new(texto, tonoHz, ventana, modo);

    /// <summary>Lo que saco el modem.</summary>
    public DecodificacionPropia Decodificacion { get; }

    /// <summary>Que tiene de nuevo.</summary>
    public NovedadDelIndicativo Novedad { get; }

    /// <summary>El mensaje entendido.</summary>
    public MensajeEstandar Mensaje { get; }

    /// <summary>Hora UTC de la ventana.</summary>
    public string Hora { get; }

    /// <summary>Relacion senal-ruido en decibelios, siempre con signo. «Tx» en lo emitido.</summary>
    public string Decibelios { get; }

    /// <summary>Desfase de la senal respecto a la ventana, en segundos.</summary>
    public string Desfase { get; }

    /// <summary>Tono dentro del ancho de banda de audio, en hercios.</summary>
    public string Tono { get; }

    /// <summary>El mensaje decodificado, tal cual.</summary>
    public string Texto { get; }

    /// <summary>Indicativo de quien llama.</summary>
    public string Indicativo { get; }

    /// <summary>Localizador que viajaba en el mensaje.</summary>
    public string Localizador { get; }

    /// <summary>El modo.</summary>
    public string Modo { get; }

    /// <summary>Lo que dicen los diplomas, para la nota emergente.</summary>
    public string Aporta { get; }

    /// <summary>Es una linea de lo que se emitio, no una decodificacion.</summary>
    public bool EsTx { get; }

    /// <summary>El indicativo seria nuevo en el cuaderno.</summary>
    public bool EsNuevo => Novedad.IndicativoNuevo;

    /// <summary>La entidad DXCC seria nueva.</summary>
    public bool EntidadNueva => Novedad.EntidadNueva;

    /// <summary>La entidad esta, pero no en esta banda.</summary>
    public bool NuevaEnBanda => Novedad.NuevaEnBanda;

    /// <summary>La cuadricula seria nueva.</summary>
    public bool CuadriculaNueva => Novedad.CuadriculaNueva;

    /// <summary>Ya se ha trabajado ese indicativo.</summary>
    public bool TrabajadoAntes => !EsTx && Indicativo.Length > 0 && Novedad.TrabajadoAntes && Novedad != NovedadDelIndicativo.Desconocida;

    /// <summary>Es una llamada general.</summary>
    public bool EsCq => Decodificacion.EsCq || Mensaje.Clase == ClaseDeMensaje.Cq;

    /// <summary>Le estan llamando a usted, con su indicativo.</summary>
    public bool MeLlaman { get; }

    /// <summary>El mensaje va dirigido a alguien, sea quien sea.</summary>
    public bool EsDirigido => !Decodificacion.Llamado.EsVacio || Mensaje.Llamado.Length > 0;

    /// <summary>Cae a menos de 50 Hz de la frecuencia de recepcion.</summary>
    public bool CercaDeRx { get; }

    /// <summary>
    /// Se rescato con el bloque de correccion profunda y no en la pasada normal.
    /// </summary>
    /// <remarks>
    /// Va marcada aparte porque no es lo mismo: son señales muy debiles recuperadas a base de
    /// mas esfuerzo, con algo mas de riesgo de ser falsas. El operador tiene que poder
    /// distinguirlas antes de apuntar un contacto con ellas.
    /// </remarks>
    public bool EsRescatada => Decodificacion.EsRecuperacionProfunda;

    /// <summary>
    /// Salio gracias a la pista del QSO en curso (decodificacion AP) y no a la pasada a ciegas.
    /// </summary>
    /// <remarks>
    /// Igual que con <see cref="EsRescatada"/>: el operador tiene que poder ver que esta
    /// decodificacion se apoyo en lo que ya se sabia del corresponsal.
    /// </remarks>
    public bool EsPorPista => Decodificacion.EsPorPista;

    /// <summary>Tono en hercios, como numero, para poder sintonizar ahi.</summary>
    public int TonoHz => Decodificacion.TonoHz;
}

/// <summary>
/// El modem propio de modos digitales en pantalla: cascada, decodificaciones, reloj y la
/// secuencia del contacto.
/// </summary>
/// <remarks>
/// <para>
/// Aqui esta todo lo que se opera: los seis mensajes, la secuencia automatica, el DX con su
/// distancia y acimut, las frecuencias de Rx y Tx, los filtros y colores de la lista, la tabla
/// de frecuencias de trabajo, los mandos de la cascada, PSK Reporter y los ficheros. No hay
/// puente con ningun programa de fuera.
/// </para>
/// <para>
/// <b>Aqui no se transmite por descuido.</b> Emitir un periodo de FT8 son trece segundos con
/// el equipo en antena. El camino esta escrito y pasa entero por el vigilante de PTT, pero
/// esta cerrado con un pestillo que el operador tiene que abrir a mano en cada sesion
/// —<see cref="PermitirTransmitir"/>—, no se guarda en los ajustes y ademas se pregunta antes
/// de sacar la portadora. La secuencia automatica no salta ese pestillo: solo decide que
/// mensaje toca y cuando, y la emision pasa por <see cref="EmitirTxAsync"/> como todas.
/// </para>
/// <para>
/// <b>Y no se abre el microfono de nadie al arrancar.</b> La entrada de audio se abre cuando
/// se pulsa escuchar y se cierra al parar.
/// </para>
/// </remarks>
public sealed partial class VistaModeloModemPropio : ObservableObject
{
    /// <summary>Decodificaciones que se guardan; las mas viejas se van cayendo.</summary>
    public const int DecodificacionesQueSeGuardan = 500;

    /// <summary>En hound se llama por encima de esto, por regla.</summary>
    public const int TonoMinimoDeHound = 1000;

    /// <summary>Cada cuanto se relee el nivel de entrada para el medidor.</summary>
    private static readonly TimeSpan RitmoDelMedidor = TimeSpan.FromMilliseconds(150);

    /// <summary>Cada cuanto se manda lo pendiente a PSK Reporter.</summary>
    private static readonly TimeSpan RitmoDePskReporter = TimeSpan.FromMinutes(5);

    private readonly IModemPropio? _modem;
    private readonly IEntradaDeAudio? _entrada;
    private readonly ISalidaDeAudio? _salida;
    private readonly IControlEquipo? _equipo;
    private readonly AjustesDelPrograma _ajustes;
    private readonly RegistrarQso _registrar;
    private readonly EvaluadorDeNovedad _novedad;
    private readonly DispatcherTimer _medidor;
    private readonly DispatcherTimer _relojDePsk;
    private readonly SecuenciadorDeQso _secuenciador = new();
    private readonly ReportadorPskReporter _psk;
    private readonly GrabadorDeVentanas _grabador = new();
    private readonly List<FilaDeDecodificacionPropia> _todas = [];

    /// <summary>Lo que dura a la vista un aviso que solo informa de algo ya hecho.</summary>
    private static readonly TimeSpan DuracionDeUnAvisoInformativo = TimeSpan.FromSeconds(8);

    private readonly DispatcherTimer _caducidadDelAviso;

    private Frecuencia _dial;
    private bool _emitiendo;
    private bool _generando;
    private bool _arrancando;

    /// <summary>
    /// El mensaje libre que espera a su ventana: se emite al empezar la siguiente que toque, no
    /// a media ventana de nadie.
    /// </summary>
    private (string Texto, int Paridad)? _librePendiente;

    /// <summary>Primera emision o recepcion del contacto en curso: su hora de inicio.</summary>
    private DateTimeOffset? _inicioDelContacto;

    /// <summary>
    /// Se ha recibido R+informe: el contacto queda hecho cuando salga el RR73 propio, y es
    /// entonces cuando se apunta.
    /// </summary>
    private bool _guardarTrasElRr73;

    /// <summary>El contacto en curso ya esta en el cuaderno: no se vuelve a meter.</summary>
    private bool _contactoYaGuardado;

    /// <summary>
    /// El ultimo contacto guardado solo. Si el corresponsal repite el 73 y se le vuelve a
    /// contestar, no se apunta dos veces.
    /// </summary>
    private (string Indicativo, string Banda, string Modo, DateTimeOffset Fin)? _ultimoGuardadoSolo;

    /// <summary>Tiempo en el que otro «completo» con la misma estacion, banda y modo es el mismo contacto.</summary>
    private static readonly TimeSpan MargenDeRepeticion = TimeSpan.FromMinutes(10);

    /// <summary>Monta el panel del modem propio.</summary>
    /// <param name="reloj">La tira del reloj, que va dentro de esta pantalla.</param>
    /// <param name="ajustes">Ajustes del programa, de donde salen los dispositivos elegidos.</param>
    /// <param name="trabajadoAntes">Consulta que dice si un indicativo ya esta en el cuaderno.</param>
    /// <param name="registrar">Caso de uso que guarda un contacto.</param>
    /// <param name="corrector">Como esta el codigo corrector con el que arranco el modem.</param>
    /// <param name="modem">El modem. Puede faltar: entonces el panel lo dice y no hace nada.</param>
    /// <param name="entrada">Captura de audio. Puede faltar.</param>
    /// <param name="salida">Reproduccion hacia el equipo. Puede faltar; sin ella no se emite.</param>
    /// <param name="equipo">El equipo, para ir a la frecuencia del modo por CAT. Puede faltar.</param>
    /// <param name="novedad">Quien dice que tiene de nuevo cada indicativo. Si falta, solo se mira el «trabajado antes».</param>
    public VistaModeloModemPropio(
        VistaModeloRelojDigital reloj,
        AjustesDelPrograma ajustes,
        ConsultarTrabajadoAntes trabajadoAntes,
        RegistrarQso registrar,
        EstadoDelCorrector corrector,
        IModemPropio? modem = null,
        IEntradaDeAudio? entrada = null,
        ISalidaDeAudio? salida = null,
        IControlEquipo? equipo = null,
        EvaluadorDeNovedad? novedad = null)
    {
        ArgumentNullException.ThrowIfNull(reloj);
        ArgumentNullException.ThrowIfNull(ajustes);
        ArgumentNullException.ThrowIfNull(trabajadoAntes);
        ArgumentNullException.ThrowIfNull(registrar);
        ArgumentNullException.ThrowIfNull(corrector);

        Reloj = reloj;
        Corrector = corrector;
        _ajustes = ajustes;
        _registrar = registrar;
        _modem = modem;
        _entrada = entrada;
        _salida = salida;
        _equipo = equipo;
        _novedad = novedad ?? new EvaluadorDeNovedad(trabajadoAntes);
        _psk = new ReportadorPskReporter("CuadernoNODISLA " + (typeof(VistaModeloModemPropio).Assembly.GetName().Version?.ToString(3) ?? "0"));

        var digital = ajustes.Digital;
        _modo = digital.Modo;
        _tonoDeTransmision = digital.TonoDeTransmisionHz;
        _tonoDeRecepcion = digital.TonoDeRecepcionHz;
        _mantenerTx = digital.MantenerTx;
        _secuenciaAutomatica = digital.SecuenciaAutomatica;
        _saltarTx1 = digital.SaltarTx1;
        _llamarAlPrimero = digital.LlamarAlPrimero;
        _tx4ConRrr = digital.Tx4ConRrr;
        _ciclosSinRespuesta = digital.CiclosSinRespuesta;
        _cqDirigido = digital.CqDirigido;
        _intercambioDeConcurso = digital.IntercambioDeConcurso;
        _pskReporterActivo = digital.PskReporter;
        _guardarWav = digital.GuardarWav;
        _guardarDecodificacionesEnTexto = digital.GuardarDecodificacionesEnTexto;
        _gananciaDeLaCascada = digital.GananciaDeLaCascadaDb;
        _ceroDeLaCascada = digital.CeroDeLaCascadaDb;
        _promedioDeColumnas = digital.PromedioDeColumnas;
        _anchoVisibleHz = digital.AnchoVisibleHz;

        // Solo si el operador lo ha pedido en Ajustes: de fabrica, el pestillo arranca cerrado.
        _permitirTransmitir = digital.RecordarPermisoDeTransmitir;

        var modos = modem?.ModosDisponibles ?? [ModoDelModem.Ft8, ModoDelModem.Ft4];
        Modos = modos.Select(m => new Opcion<ModoDelModem>(m, () => DescripcionDelModo.Nombre(m), () => DescripcionDelModo.PeriodoTexto(m))).ToList();
        if (Modos.All(o => o.Valor != _modo)) _modo = Modos[0].Valor;
        _modoElegido = Modos.First(o => o.Valor == _modo);

        Operaciones = Enum.GetValues<TipoDeOperacion>()
            .Select(o => new Opcion<TipoDeOperacion>(o, () => GramaticaDeMensajes.Nombre(o))).ToList();
        _operacionElegida = Operaciones.First(o => o.Valor == digital.Operacion);

        // En los ajustes se guarda el nombre interno de la paleta, no el traducido.
        Paletas = Enum.GetValues<PaletaDeCascada>().Select(p => new Opcion<PaletaDeCascada>(p, () => NombreDePaleta(p))).ToList();
        _paletaElegida = Paletas.FirstOrDefault(p => string.Equals(p.Valor.ToString(), digital.PaletaDeLaCascada, StringComparison.OrdinalIgnoreCase)) ?? Paletas[0];

        Pintor.GananciaDb = _gananciaDeLaCascada;
        Pintor.CeroDb = _ceroDeLaCascada;
        Pintor.PromedioDeColumnas = _promedioDeColumnas;
        Pintor.Paleta = _paletaElegida.Valor;
        Pintor.AnchoVisibleHz = _anchoVisibleHz;

        foreach (var f in digital.FrecuenciasDeTrabajo) FrecuenciasDeTrabajo.Add(f);
        for (var i = 1; i <= GramaticaDeMensajes.Cuantos; i++) Mensajes.Add(new MensajeTx(i));

        _secuenciador.Periodo = DescripcionDelModo.Periodo(_modo);
        _secuenciador.Operacion = digital.Operacion;
        _secuenciador.SaltarTx1 = _saltarTx1;
        _secuenciador.LlamarAlPrimero = _llamarAlPrimero;
        _secuenciador.CiclosSinRespuesta = _ciclosSinRespuesta;

        if (_modem is not null)
        {
            _modem.CascadaActualizada += AlLlegarUnaColumna;
            _modem.VentanaLista += AlTerminarUnaVentana;
        }

        if (_entrada is not null)
        {
            _entrada.MuestrasPerdidas += AlPerderMuestras;
            _entrada.BloqueCapturado += AlCapturarUnBloque;
        }

        _medidor = new DispatcherTimer(DispatcherPriority.Background) { Interval = RitmoDelMedidor };
        _medidor.Tick += (_, _) => LeerElNivel();

        _relojDePsk = new DispatcherTimer(DispatcherPriority.Background) { Interval = RitmoDePskReporter };
        _relojDePsk.Tick += async (_, _) => await EnviarAPskReporterAsync().ConfigureAwait(true);

        _caducidadDelAviso = new DispatcherTimer(DispatcherPriority.Background) { Interval = DuracionDeUnAvisoInformativo };
        _caducidadDelAviso.Tick += (_, _) =>
        {
            _caducidadDelAviso.Stop();
            Aviso = string.Empty;
        };

        GenerarMensajes();

        // Los textos calculados (estado, secuencia, avisos de la transmision) siguen al idioma.
        Textos.AlCambiar(this, static vm => vm.OnPropertyChanged(string.Empty));
    }

    /// <summary>Nombre de una paleta de la cascada, en el idioma en uso.</summary>
    public static string NombreDePaleta(PaletaDeCascada paleta) => paleta switch
    {
        PaletaDeCascada.Gris => Textos.T("Digital.Paleta.Gris"),
        PaletaDeCascada.Fuego => Textos.T("Digital.Paleta.Fuego"),
        PaletaDeCascada.Azul => Textos.T("Digital.Paleta.Azul"),
        _ => paleta.ToString(),
    };

    /// <summary>Salta cuando entra en el cuaderno un contacto hecho con el modem propio.</summary>
    public event EventHandler? CuadernoCambiado;

    /// <summary>Salta cuando una ventana ha pasado entera por la lista y la secuencia. Es para las pruebas.</summary>
    public event EventHandler<DateTimeOffset>? VentanaProcesada;

    // ── Lo que viene de fuera ─────────────────────────────────────────────

    /// <summary>La tira del reloj: desvio, semaforo y boton de poner en hora.</summary>
    public VistaModeloRelojDigital Reloj { get; }

    /// <summary>Como esta el codigo corrector.</summary>
    public EstadoDelCorrector Corrector { get; }

    /// <summary>Quien pinta la cascada.</summary>
    public PintorDeCascada Pintor { get; } = new();

    /// <summary>Decodificaciones que pasan los filtros, de la mas reciente a la mas antigua.</summary>
    public ObservableCollection<FilaDeDecodificacionPropia> Decodificaciones { get; } = [];

    /// <summary>Los seis mensajes de la secuencia.</summary>
    public ObservableCollection<MensajeTx> Mensajes { get; } = [];

    /// <summary>Las frecuencias de trabajo, editables.</summary>
    public ObservableCollection<FrecuenciaDeTrabajo> FrecuenciasDeTrabajo { get; } = [];

    /// <summary>Los modos que sabe hacer el modem, con su periodo.</summary>
    public IReadOnlyList<Opcion<ModoDelModem>> Modos { get; }

    /// <summary>Normal, hound y los concursos.</summary>
    public IReadOnlyList<Opcion<TipoDeOperacion>> Operaciones { get; }

    /// <summary>Las paletas de la cascada.</summary>
    public IReadOnlyList<Opcion<PaletaDeCascada>> Paletas { get; }

    /// <summary>Perfil de estacion con el que se guardan los contactos. Lo fija la ventana.</summary>
    public long? EstacionId
    {
        get => _novedad.EstacionId;
        set => _novedad.EstacionId = value;
    }

    /// <summary>Carpeta de datos del programa, para los WAV y el registro de texto. La fija el montaje.</summary>
    public string? CarpetaDeDatos { get; set; }

    /// <summary>
    /// El indicativo con el que se opera. Lo fija la ventana desde el perfil activo.
    /// </summary>
    public Indicativo MiIndicativo
    {
        get => _miIndicativo;
        set
        {
            _miIndicativo = value;
            _secuenciador.MiIndicativo = value.EsVacio ? string.Empty : value.Valor;
            _psk.MiIndicativo = value.EsVacio ? string.Empty : value.Valor;
            GenerarMensajes();
        }
    }

    private Indicativo _miIndicativo;

    /// <summary>El localizador propio, del perfil. Sin el no hay distancia ni Tx1.</summary>
    public Locator MiLocalizador
    {
        get => _miLocalizador;
        set
        {
            _miLocalizador = value;
            _psk.MiLocalizador = value.EsVacio ? string.Empty : value.Valor;
            GenerarMensajes();
            OnPropertyChanged(nameof(DistanciaYAcimut));
        }
    }

    private Locator _miLocalizador;

    /// <summary>
    /// Lo que se pregunta antes de sacar la portadora. Lo pone la ventana.
    /// </summary>
    /// <remarks>
    /// Va aqui y no en el XAML porque un dialogo es cosa de la ventana, y porque en pruebas se
    /// sustituye por una funcion que siempre dice que no: asi la suite no puede transmitir ni
    /// por accidente.
    /// </remarks>
    public Func<string, bool>? ConfirmarQueVaATransmitir { get; set; }

    /// <summary>
    /// Se pregunta antes de emitir. Se apaga con «No volver a preguntar» y se vuelve a encender
    /// en Configuración › Audio y digitales. Lo demas —pestillo, reloj, salida, vigilante— se
    /// exige igual.
    /// </summary>
    public bool PedirConfirmacionAlTransmitir
    {
        get => _ajustes.Digital.PedirConfirmacionAlTransmitir;
        set
        {
            if (_ajustes.Digital.PedirConfirmacionAlTransmitir == value) return;
            _ajustes.Digital.PedirConfirmacionAlTransmitir = value;
            OnPropertyChanged();
        }
    }

    /// <summary>El contacto que la secuencia da por completo se apunta solo en el cuaderno.</summary>
    public bool RegistrarAlCompletar
    {
        get => _ajustes.Digital.RegistrarAlCompletar;
        set
        {
            if (_ajustes.Digital.RegistrarAlCompletar == value) return;
            _ajustes.Digital.RegistrarAlCompletar = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// «No volver a preguntar» del dialogo: deja de preguntar y lo guarda en los ajustes.
    /// </summary>
    public void NoVolverAPreguntarAlTransmitir()
    {
        PedirConfirmacionAlTransmitir = false;
        Log.Warning("El operador ha pedido no volver a confirmar las emisiones del módem propio.");
        if (CarpetaDeDatos is not { Length: > 0 } carpeta) return;

        try
        {
            _ajustes.Guardar(carpeta);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido guardar «no volver a preguntar».");
        }
    }

    /// <summary>Pregunta antes de transmitir, si esta pedido. Sin pregunta puesta, no se pregunta.</summary>
    private bool OperadorConforme(string que) =>
        !PedirConfirmacionAlTransmitir || ConfirmarQueVaATransmitir is not { } preguntar || preguntar(que);

    /// <summary>Quien pide al operador un fichero WAV. Lo pone la ventana.</summary>
    public Func<string?>? ElegirFicheroWav { get; set; }

    /// <summary>Hay un modem propio detras.</summary>
    public bool HayModem => _modem is not null;

    /// <summary>Hay equipo al que mandar por CAT.</summary>
    public bool HayEquipo => _equipo is not null;

    /// <summary>
    /// El modem arranco sin la tabla del codigo corrector de verdad.
    /// </summary>
    public bool SinLaTablaDelCorrector => HayModem && !Corrector.EsElCodigoReal;

    /// <summary>De donde salieron las tablas del protocolo.</summary>
    public string ProcedenciaDeLasTablas => Corrector.Procedencia;

    // ── Estado observable ─────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstadoTexto))]
    [NotifyCanExecuteChangedFor(nameof(EscucharCommand))]
    [NotifyCanExecuteChangedFor(nameof(PararCommand))]
    [NotifyCanExecuteChangedFor(nameof(EmitirCommand))]
    [NotifyPropertyChangedFor(nameof(NivelCorto))]
    private bool _escuchando;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModoTexto))]
    [NotifyPropertyChangedFor(nameof(PeriodoTexto))]
    [NotifyPropertyChangedFor(nameof(EstadoTexto))]
    [NotifyPropertyChangedFor(nameof(EsFt8))]
    [NotifyPropertyChangedFor(nameof(EsFt4))]
    [NotifyPropertyChangedFor(nameof(FrecuenciaDelModoTexto))]
    [NotifyPropertyChangedFor(nameof(EsBaliza))]
    private ModoDelModem _modo;

    [ObservableProperty]
    private Opcion<ModoDelModem> _modoElegido;

    [ObservableProperty]
    private Opcion<TipoDeOperacion> _operacionElegida;

    [ObservableProperty]
    private Opcion<PaletaDeCascada> _paletaElegida;

    [ObservableProperty]
    private WriteableBitmap? _imagenDeLaCascada;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NivelPorCiento))]
    [NotifyPropertyChangedFor(nameof(NivelSatura))]
    [NotifyPropertyChangedFor(nameof(NivelCorto))]
    [NotifyPropertyChangedFor(nameof(NivelTexto))]
    private double _nivelDeEntrada;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayAviso))]
    private string _aviso = string.Empty;

    [ObservableProperty]
    private string _ultimaVentana = string.Empty;

    /// <summary>La ultima ventana tardo mas que el propio periodo: el ordenador no da abasto.</summary>
    [ObservableProperty]
    private bool _llegoTarde;

    // Filtros de la lista.
    [ObservableProperty]
    private bool _soloNuevos;

    [ObservableProperty]
    private bool _soloCq;

    [ObservableProperty]
    private bool _soloMeLlaman;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrepararRespuestaCommand))]
    [NotifyCanExecuteChangedFor(nameof(SintonizarEnLaDecodificacionCommand))]
    private FilaDeDecodificacionPropia? _decodificacionElegida;

    // Rx y Tx.
    [ObservableProperty]
    private int _tonoDeTransmision = 1500;

    [ObservableProperty]
    private int _tonoDeRecepcion = 1500;

    [ObservableProperty]
    private bool _mantenerTx;

    /// <summary>
    /// El pestillo de la transmision.
    /// </summary>
    /// <remarks>
    /// Empieza cerrado en cada arranque y <b>no se guarda</b>. Que el operador lo dejara
    /// abierto un dia no puede significar que el programa abra al dia siguiente listo para
    /// poner el equipo en antena.
    /// </remarks>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EmitirCommand))]
    [NotifyPropertyChangedFor(nameof(AvisoDeLaTransmision))]
    [NotifyPropertyChangedFor(nameof(SePuedeEmitir))]
    private bool _permitirTransmitir;

    /// <summary>
    /// Tx habilitado: la secuencia emite sola en cada ventana propia. Es el «Enable Tx» de
    /// WSJT-X. No salta el pestillo: sin el, no se enciende.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstadoDeLaSecuencia))]
    private bool _txHabilitado;

    /// <summary>
    /// El operador ha apagado el modo digital (FT8/FT4 y demás) a propósito: ni escucha ni puede
    /// transmitir, así que tampoco disputa la entrada ni la salida de audio compartidas mientras
    /// esté así. Independiente de CW y de RTTY: cada modo se apaga por su cuenta. No se guarda
    /// entre sesiones, igual que el pestillo.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EscucharCommand))]
    [NotifyCanExecuteChangedFor(nameof(EmitirCommand))]
    [NotifyPropertyChangedFor(nameof(SePuedeEmitir))]
    [NotifyPropertyChangedFor(nameof(AvisoDeLaTransmision))]
    private bool _modoApagado;

    // El DX.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayContactoEnCurso))]
    [NotifyCanExecuteChangedFor(nameof(RegistrarContactoCommand))]
    [NotifyCanExecuteChangedFor(nameof(OlvidarContactoCommand))]
    private string _corresponsal = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DistanciaYAcimut))]
    private string _localizadorDelCorresponsal = string.Empty;

    [ObservableProperty]
    private string _informeEnviado = string.Empty;

    [ObservableProperty]
    private string _informeRecibido = string.Empty;

    [ObservableProperty]
    private string _mensajeAEmitir = string.Empty;

    /// <summary>La secuencia ha dado el contacto por hecho: toca guardarlo.</summary>
    [ObservableProperty]
    private bool _contactoCompleto;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstadoDeLaSecuencia))]
    private int _txSiguiente = 6;

    // La secuencia.
    [ObservableProperty]
    private bool _secuenciaAutomatica = true;

    [ObservableProperty]
    private bool _saltarTx1;

    [ObservableProperty]
    private bool _llamarAlPrimero;

    [ObservableProperty]
    private bool _tx4ConRrr;

    [ObservableProperty]
    private int _ciclosSinRespuesta = 5;

    [ObservableProperty]
    private string _cqDirigido = string.Empty;

    [ObservableProperty]
    private string _intercambioDeConcurso = string.Empty;

    // Red y ficheros.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PskReporterTexto))]
    private bool _pskReporterActivo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PskReporterTexto))]
    private int _recepcionesInformadas;

    [ObservableProperty]
    private bool _guardarWav;

    [ObservableProperty]
    private bool _guardarDecodificacionesEnTexto;

    // La cascada.
    [ObservableProperty]
    private double _gananciaDeLaCascada;

    [ObservableProperty]
    private double _ceroDeLaCascada;

    [ObservableProperty]
    private int _promedioDeColumnas = 1;

    [ObservableProperty]
    private int _anchoVisibleHz = 3000;

    // Frecuencias de trabajo.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(QuitarFrecuenciaCommand))]
    [NotifyCanExecuteChangedFor(nameof(IrALaFrecuenciaElegidaCommand))]
    private FrecuenciaDeTrabajo? _frecuenciaElegida;

    // ── Texto derivado ────────────────────────────────────────────────────

    /// <summary>Si se esta escuchando o no, en una linea.</summary>
    public string EstadoTexto => !HayModem
        ? Textos.T("Digital.Modem.Estado.SinModem")
        : Escuchando
            ? Textos.F("Digital.Modem.Estado.Escuchando", ModoTexto, DispositivoDeEntradaTexto)
            : Textos.T("Digital.Modem.Estado.Parado");

    /// <summary>El modo, en letras.</summary>
    public string ModoTexto => DescripcionDelModo.Nombre(Modo);

    /// <summary>Se esta en FT8.</summary>
    public bool EsFt8
    {
        get => Modo == ModoDelModem.Ft8;
        set { if (value) Modo = ModoDelModem.Ft8; }
    }

    /// <summary>Se esta en FT4.</summary>
    public bool EsFt4
    {
        get => Modo == ModoDelModem.Ft4;
        set { if (value) Modo = ModoDelModem.Ft4; }
    }

    /// <summary>El modo es una baliza: no hay contacto que secuenciar.</summary>
    public bool EsBaliza => DescripcionDelModo.EsBaliza(Modo);

    /// <summary>Lo que dura una ventana del modo elegido.</summary>
    public string PeriodoTexto => DescripcionDelModo.PeriodoTexto(Modo);

    /// <summary>La secuencia, en una linea.</summary>
    public string EstadoDeLaSecuencia => !_secuenciador.Activo
        ? Textos.T(TxHabilitado ? "Digital.Modem.Secuencia.TxSinSecuencia" : "Digital.Modem.Secuencia.Parada")
        : (_secuenciador.DxCall.Length > 0, TxHabilitado) switch
        {
            (false, true) => Textos.F("Digital.Modem.Secuencia.EnMarcha", _secuenciador.TxActual),
            (true, true) => Textos.F("Digital.Modem.Secuencia.EnMarchaCon", _secuenciador.TxActual, _secuenciador.DxCall),
            (false, false) => Textos.F("Digital.Modem.Secuencia.EnMarchaSinTx", _secuenciador.TxActual),
            (true, false) => Textos.F("Digital.Modem.Secuencia.EnMarchaConSinTx", _secuenciador.TxActual, _secuenciador.DxCall),
        };

    /// <summary>
    /// La secuencia esta en marcha pero Tx no esta habilitado: no va a salir nada solo.
    /// </summary>
    /// <remarks>
    /// Pasa siempre tras un reinicio (los dos candados empiezan cerrados a proposito) y es facil
    /// no darse cuenta: a diferencia del pestillo (<see cref="PermitirTransmitir"/>), que tiene
    /// su propio aviso destacado, esto solo se veia como una frase mas en el texto tenue del
    /// estado de la secuencia. Confirmado el 05-10-2026: tras actualizar de version, Jose volvio
    /// a abrir el pestillo pero no volvio a marcar Tx habilitado, y estuvo mas de cinco minutos
    /// sin que saliera nada solo, creyendo que el programa no emitia.
    /// </remarks>
    public bool AvisoDeSecuenciaSinTx => _secuenciador.Activo && !TxHabilitado;

    /// <summary>Avisa de los dos a la vez: son el mismo cambio para quien mira la pantalla.</summary>
    private void NotificarEstadoDeLaSecuencia()
    {
        OnPropertyChanged(nameof(EstadoDeLaSecuencia));
        OnPropertyChanged(nameof(AvisoDeSecuenciaSinTx));
    }

    /// <summary>La frecuencia de trabajo del modo para la banda del dial, en letras.</summary>
    public string FrecuenciaDelModoTexto
    {
        get
        {
            var f = Digital.FrecuenciasDeTrabajo.Para(FrecuenciasDeTrabajo, Modo, _dial);
            return f is null
                ? Textos.F("Digital.Modem.SinFrecuenciaDeTrabajo", ModoTexto)
                : Textos.F("Digital.Modem.FrecuenciaDelModo", ModoTexto, f.Banda, f.Megahercios.ToString("0.000###", CultureInfo.CurrentCulture));
        }
    }

    /// <summary>Distancia y acimut al corresponsal, si hay los dos localizadores.</summary>
    public string DistanciaYAcimut
    {
        get
        {
            if (MiLocalizador.EsVacio || !Locator.TryParse(LocalizadorDelCorresponsal, out var suyo)) return string.Empty;

            var origen = Coordenada.Desde(MiLocalizador);
            var destino = Coordenada.Desde(suyo);
            var km = Geodesia.DistanciaKm(origen, destino);
            var rumbo = Geodesia.RumboGrados(origen, destino);
            return string.Create(Textos.Cultura, $"{km:N0} km · {rumbo:0}°");
        }
    }

    /// <summary>Cuantas recepciones han salido hacia PSK Reporter.</summary>
    public string PskReporterTexto => !PskReporterActivo
        ? Textos.T("Digital.Modem.PskApagado")
        : Textos.F("Digital.Modem.Psk", RecepcionesInformadas, _psk.Pendientes);

    /// <summary>Dispositivo de entrada que se va a abrir, dicho con su nombre.</summary>
    public string DispositivoDeEntradaTexto
    {
        get
        {
            if (_entrada?.Abierto is { } abierto) return abierto.Nombre;

            var elegido = ElegirLaEntrada();
            if (elegido is not null) return elegido.Nombre;

            return _ajustes.Digital.NombreDeEntrada is { Length: > 0 } guardado
                ? Textos.F("Digital.Modem.DispositivoNoEsta", guardado)
                : Textos.T("Digital.Modem.SinDispositivoDeEntrada");
        }
    }

    /// <summary>El nivel de entrada en tanto por ciento, para el medidor.</summary>
    public double NivelPorCiento => Math.Clamp(NivelDeEntrada, 0, 1) * 100;

    /// <summary>El nivel esta al borde de recortar.</summary>
    public bool NivelSatura => NivelDeEntrada >= AjustesDeDigital.NivelQueSatura;

    /// <summary>El nivel se queda corto.</summary>
    public bool NivelCorto => Escuchando && NivelDeEntrada < AjustesDeDigital.NivelQueSeQuedaCorto;

    /// <summary>El nivel de entrada, en palabras y corto.</summary>
    public string NivelTexto => NivelSatura
        ? Textos.F("Digital.Modem.Nivel.Saturando", NivelDeEntrada)
        : NivelCorto
            ? Textos.F("Digital.Modem.Nivel.MuyBajo", NivelDeEntrada)
            : string.Create(Textos.Cultura, $"{NivelDeEntrada:0.00}");

    /// <summary>Hay algo que decir.</summary>
    public bool HayAviso => !string.IsNullOrEmpty(Aviso);

    /// <summary>Hay un contacto a medias.</summary>
    public bool HayContactoEnCurso => !string.IsNullOrWhiteSpace(Corresponsal);

    /// <summary>
    /// Se puede emitir: el modo no esta apagado, hay modem, la salida de audio esta REALMENTE
    /// abierta y el pestillo lo esta tambien.
    /// </summary>
    public bool SePuedeEmitir => !ModoApagado && HayModem && _salida?.Abierto is not null && PermitirTransmitir;

    /// <summary>Por que el botón de emitir está como está.</summary>
    public string AvisoDeLaTransmision => ModoApagado
        ? Textos.T("Digital.Modem.Transmision.ModoApagado")
        : _salida?.Abierto is null
            ? Textos.T("Digital.Modem.Transmision.SinSalida")
            : PermitirTransmitir
                ? Textos.T("Digital.Modem.Transmision.Permitida")
                : Textos.T("Digital.Modem.Transmision.Cerrada");

    /// <summary>El secuenciador, para que las pruebas lo miren.</summary>
    public SecuenciadorDeQso Secuenciador => _secuenciador;

    /// <summary>El reportador de PSK Reporter, para que las pruebas lo miren.</summary>
    public ReportadorPskReporter PskReporter => _psk;

    // ── Lo que llama la ventana ───────────────────────────────────────────

    /// <summary>
    /// El equipo ha cambiado de frecuencia: el modem necesita el dial para poder componer el
    /// contacto.
    /// </summary>
    public void PonerElDial(Frecuencia frecuencia)
    {
        _dial = frecuencia;
        if (_modem is not null) _modem.FrecuenciaDelDial = frecuencia;
        OnPropertyChanged(nameof(FrecuenciaDelModoTexto));
    }

    /// <summary>El operador ha entrado en la pestana Digital: se empieza a mirar el reloj.</summary>
    public void Asomarse() => Reloj.Vigilar();

    /// <summary>Pone el tono de transmision (y el de recepcion) donde se ha tocado la cascada.</summary>
    public void SintonizarEn(double hercios)
    {
        var hz = (int)Math.Clamp(Math.Round(hercios), 200, 3000);
        TonoDeRecepcion = hz;
        if (!MantenerTx) TonoDeTransmision = hz;
    }

    /// <summary>Deja de escuchar al modem. Lo llama la ventana al cerrarse.</summary>
    public void Detener()
    {
        _medidor.Stop();
        _relojDePsk.Stop();

        if (_modem is not null)
        {
            _modem.CascadaActualizada -= AlLlegarUnaColumna;
            _modem.VentanaLista -= AlTerminarUnaVentana;
        }

        if (_entrada is not null)
        {
            _entrada.MuestrasPerdidas -= AlPerderMuestras;
            _entrada.BloqueCapturado -= AlCapturarUnBloque;
        }

        Reloj.Detener();
    }

    /// <summary>Guarda lo elegido en los ajustes, para la proxima sesion.</summary>
    public void GuardarLoElegido(string carpeta)
    {
        var d = _ajustes.Digital;
        d.Modo = Modo;
        d.TonoDeTransmisionHz = TonoDeTransmision;
        d.TonoDeRecepcionHz = TonoDeRecepcion;
        d.MantenerTx = MantenerTx;
        d.SecuenciaAutomatica = SecuenciaAutomatica;
        d.SaltarTx1 = SaltarTx1;
        d.LlamarAlPrimero = LlamarAlPrimero;
        d.Tx4ConRrr = Tx4ConRrr;
        d.CiclosSinRespuesta = CiclosSinRespuesta;
        d.CqDirigido = CqDirigido;
        d.Operacion = OperacionElegida.Valor;
        d.IntercambioDeConcurso = IntercambioDeConcurso;
        d.PskReporter = PskReporterActivo;
        d.GuardarWav = GuardarWav;
        d.GuardarDecodificacionesEnTexto = GuardarDecodificacionesEnTexto;
        d.GananciaDeLaCascadaDb = GananciaDeLaCascada;
        d.CeroDeLaCascadaDb = CeroDeLaCascada;
        d.PromedioDeColumnas = PromedioDeColumnas;
        d.PaletaDeLaCascada = PaletaElegida.Valor.ToString();
        d.AnchoVisibleHz = AnchoVisibleHz;
        d.FrecuenciasDeTrabajo = FrecuenciasDeTrabajo.ToList();
        d.Acotar();
        _ajustes.Guardar(carpeta);
    }

    // ── Escuchar y parar ──────────────────────────────────────────────────

    /// <summary>
    /// Empieza a escuchar: abre la entrada Y la salida de audio, y arranca el modem.
    /// </summary>
    /// <remarks>
    /// La salida se abre aqui, junto con la entrada, y no se deja para cuando se pulse
    /// «Emitir». El modem propio exige que le entreguen la salida ya abierta —<b>no sabe abrir
    /// una tarjeta de sonido</b>—. Un fallo al abrir la salida no aborta el escuchar:
    /// quedarse sin poder transmitir es una falta menor comparada con quedarse tambien sin
    /// poder recibir, y <see cref="SePuedeEmitir"/> ya se encarga de mantener el boton de
    /// emitir apagado mientras la salida no este abierta.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(SePuedeEscuchar))]
    public async Task EscucharAsync()
    {
        if (_modem is null || ModoApagado) return;

        // Dos «Escuchar» seguidos (el botón de aquí, el del visor de VFO, el arranque
        // automático) abrían la tarjeta dos veces: el segundo espera fuera.
        if (_arrancando || Escuchando) return;
        _arrancando = true;

        try
        {
            Aviso = string.Empty;

            if (_entrada is not null)
            {
                var dispositivo = ElegirLaEntrada();
                if (dispositivo is null)
                {
                    Aviso = Textos.T("Digital.Modem.Aviso.SinEntrada");
                    return;
                }

                await _entrada
                    .AbrirAsync(dispositivo.Id, _ajustes.Digital.FrecuenciaDeMuestreo)
                    .ConfigureAwait(true);
            }

            if (_salida is not null)
            {
                var dispositivoDeSalida = ElegirLaSalida();
                if (dispositivoDeSalida is null)
                {
                    Aviso = Textos.T("Digital.Modem.Aviso.SinSalida");
                }
                else
                {
                    try
                    {
                        await _salida
                            .AbrirAsync(dispositivoDeSalida.Id, _ajustes.Digital.FrecuenciaDeMuestreo)
                            .ConfigureAwait(true);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "No se ha podido abrir la salida de audio del módem propio.");
                        Aviso = Textos.F("Digital.Modem.Aviso.NoSeAbreLaSalida", dispositivoDeSalida.Nombre, ex.Message);
                    }
                }
            }

            Pintor.Limpiar();
            await _modem.EscucharAsync(Modo).ConfigureAwait(true);

            Escuchando = true;
            _medidor.Start();
            if (PskReporterActivo) _relojDePsk.Start();
            OnPropertyChanged(nameof(DispositivoDeEntradaTexto));
            OnPropertyChanged(nameof(EstadoTexto));
            OnPropertyChanged(nameof(SePuedeEmitir));
            OnPropertyChanged(nameof(AvisoDeLaTransmision));
            EmitirCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido arrancar el módem propio.");
            Aviso = Textos.F("Digital.Aviso.NoSePudoEscuchar", ex.Message);
            _arrancando = false;
            await PararAsync().ConfigureAwait(true);
        }
        finally
        {
            _arrancando = false;
        }
    }

    /// <summary>Para el modem, la secuencia y cierra el audio.</summary>
    [RelayCommand(CanExecute = nameof(SePuedeParar))]
    public async Task PararAsync()
    {
        _medidor.Stop();
        _relojDePsk.Stop();
        NivelDeEntrada = 0;
        DetenerTx();

        try
        {
            if (_modem is not null) await _modem.PararAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Fallo al parar el módem propio.");
        }

        try
        {
            // La tarjeta se suelta SIEMPRE, aunque parar el modem haya fallado: dejar el
            // microfono de alguien abierto porque se cayo otra cosa no es aceptable.
            if (_entrada is not null) await _entrada.CerrarAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Fallo al cerrar la entrada de audio.");
        }

        try
        {
            if (_salida is not null) await _salida.CerrarAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Fallo al cerrar la salida de audio.");
        }

        await EnviarAPskReporterAsync().ConfigureAwait(true);

        Escuchando = false;
        OnPropertyChanged(nameof(EstadoTexto));
        OnPropertyChanged(nameof(SePuedeEmitir));
        OnPropertyChanged(nameof(AvisoDeLaTransmision));
        EmitirCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Vacia la lista de decodificaciones y limpia la cascada.</summary>
    [RelayCommand]
    public void Limpiar()
    {
        _todas.Clear();
        Decodificaciones.Clear();
        Pintor.Limpiar();
        UltimaVentana = string.Empty;
        LlegoTarde = false;
    }

    // ── Los mensajes y la secuencia ───────────────────────────────────────

    /// <summary>Compone Tx1 a Tx6 con lo que hay: DX, localizadores e informe.</summary>
    [RelayCommand]
    public void GenerarMensajes()
    {
        if (Mensajes.Count < GramaticaDeMensajes.Cuantos) return;

        _generando = true;
        try
        {
            var informe = int.TryParse(InformeEnviado, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var db) ? db : 0;
            var textos = GramaticaDeMensajes.Generar(new DatosDeLosMensajes(
                MiIndicativo.EsVacio ? string.Empty : MiIndicativo.Valor,
                MiLocalizador.EsVacio ? string.Empty : MiLocalizador.Valor,
                Corresponsal,
                LocalizadorDelCorresponsal,
                informe,
                CqDirigido,
                IntercambioDeConcurso,
                Tx4ConRrr,
                OperacionElegida.Valor));

            for (var i = 0; i < GramaticaDeMensajes.Cuantos; i++) Mensajes[i].Texto = textos[i];
            MarcarElSiguiente(TxSiguiente);
        }
        finally
        {
            _generando = false;
        }
    }

    /// <summary>Marca un mensaje como el siguiente: es la radio de cada fila.</summary>
    /// <remarks>
    /// Si la secuencia esta en marcha, la eleccion del operador manda sobre la de la secuencia:
    /// es lo que sale en la proxima ventana propia.
    /// </remarks>
    [RelayCommand]
    public void ElegirTx(MensajeTx? mensaje)
    {
        if (mensaje is null) return;
        TxSiguiente = mensaje.Numero;
        _secuenciador.ElegirSiguiente(mensaje.Numero);
        MarcarElSiguiente(TxSiguiente);
        NotificarEstadoDeLaSecuencia();
    }

    /// <summary>
    /// El doble clic sobre un mensaje (o su boton «Enviar»): ese mensaje sale en la proxima
    /// ventana propia. Si Tx no esta habilitado, se intenta habilitar, y eso pasa por el
    /// pestillo y por la pregunta.
    /// </summary>
    [RelayCommand]
    public async Task EnviarTxAsync(MensajeTx? mensaje)
    {
        if (mensaje is null) return;

        TxSiguiente = mensaje.Numero;
        _secuenciador.Enviar(mensaje.Numero, Reloj.Ahora);
        NotificarEstadoDeLaSecuencia();

        if (!TxHabilitado && !IntentarHabilitarTx()) return;
        await EmitirSiEsSuVentanaAsync().ConfigureAwait(true);
    }

    /// <summary>Empieza a llamar CQ con Tx6: la secuencia queda en marcha.</summary>
    [RelayCommand]
    public async Task LlamarCqAsync()
    {
        if (EsBaliza)
        {
            Aviso = Textos.F("Digital.Modem.Aviso.BalizaNoCq", ModoTexto);
            return;
        }

        OlvidarContacto();
        _secuenciador.LlamarCq(Reloj.Ahora);
        TxSiguiente = 6;
        NotificarEstadoDeLaSecuencia();

        if (!TxHabilitado && !IntentarHabilitarTx()) return;
        Informar(Textos.F("Digital.Modem.Aviso.LlamandoCq", ProximaVentanaTexto(_secuenciador.Paridad)));
        await EmitirSiEsSuVentanaAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// La casilla «Tx habilitado». Encenderla pasa por el pestillo y por la pregunta, igual
    /// que cualquier otro camino al aire; y si no hay secuencia en marcha, la pone en marcha
    /// con el mensaje marcado como siguiente, como hace WSJT-X.
    /// </summary>
    [RelayCommand]
    public async Task AlternarTxHabilitadoAsync()
    {
        try
        {
            if (TxHabilitado)
            {
                TxHabilitado = false;
                return;
            }

            if (EsBaliza)
            {
                Aviso = Textos.F("Digital.Modem.Aviso.BalizaSinSecuencia", ModoTexto);
                return;
            }

            if (!IntentarHabilitarTx()) return;

            if (!_secuenciador.Activo) _secuenciador.Enviar(TxSiguiente, Reloj.Ahora);
            else _secuenciador.ElegirSiguiente(TxSiguiente);

            Informar(Textos.F("Digital.Modem.Aviso.TxHabilitadoSale", TxSiguiente, ProximaVentanaTexto(_secuenciador.Paridad)));
            await EmitirSiEsSuVentanaAsync().ConfigureAwait(true);
        }
        finally
        {
            // La casilla se vuelve a pintar con lo que de verdad hay, aunque se haya negado.
            OnPropertyChanged(nameof(TxHabilitado));
            NotificarEstadoDeLaSecuencia();
        }
    }

    /// <summary>
    /// «Detener»: para la secuencia, deshabilita Tx, anula el mensaje libre pendiente y, si
    /// hay una emision en curso, la corta. Es el «Halt Tx».
    /// </summary>
    [RelayCommand]
    public async Task DetenerTxAsync()
    {
        var habiaEmision = _emitiendo || (_modem?.EstaEmitiendo ?? false);
        DetenerTx();
        _librePendiente = null;

        if (habiaEmision && _modem is not null)
        {
            try
            {
                await _modem.AbortarEmisionAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "No se ha podido cortar la emisión al detener.");
                Aviso = Textos.F("Digital.Modem.Aviso.NoSePudoCortar", ex.Message);
                return;
            }
        }

        Informar(Textos.T(habiaEmision ? "Digital.Modem.Aviso.DetenidoConEmision" : "Digital.Modem.Aviso.DetenidoSinEmision"));
    }

    /// <summary>Para la secuencia y deshabilita Tx, sin tocar lo que ya esta sonando.</summary>
    public void DetenerTx()
    {
        _secuenciador.Parar();
        TxHabilitado = false;
        NotificarEstadoDeLaSecuencia();
        MarcarElSiguiente(TxSiguiente);
    }

    /// <summary>Quita el aviso de la vista.</summary>
    [RelayCommand]
    public void CerrarAviso() => Aviso = string.Empty;

    /// <summary>
    /// El doble clic sobre una decodificacion: fija el DX, compone los mensajes, pone la
    /// secuencia en marcha y, si Tx esta (o se puede poner) habilitado, contesta.
    /// </summary>
    /// <remarks>
    /// Con el pestillo cerrado no sale nada: se deja todo preparado y el aviso lo dice.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(HayDecodificacionElegida))]
    public async Task PrepararRespuestaAsync()
    {
        if (DecodificacionElegida is not { } fila || fila.EsTx) return;

        var mensaje = fila.Mensaje;
        var suyo = mensaje.Llamante.Length > 0 ? mensaje.Llamante : fila.Indicativo;
        if (suyo.Length == 0)
        {
            Aviso = Textos.T("Digital.Modem.Aviso.SinIndicativo");
            return;
        }

        // Si el mensaje del fox lleva dos, el que me interesa es el que va dirigido a mi.
        if (!MiIndicativo.EsVacio && mensaje.Segundo is { } segundo && segundo.VaDirigidoA(MiIndicativo.Valor))
        {
            mensaje = segundo;
            suyo = segundo.Llamante;
        }

        Corresponsal = suyo;
        ReiniciarElContacto();
        _inicioDelContacto = fila.Decodificacion.VentanaUtc;
        LocalizadorDelCorresponsal = fila.Localizador.Length >= 4 ? fila.Localizador : LocalizadorDelCorresponsal;

        // El informe que se manda es la señal con la que se ha oido; el que se recibe, el que
        // mande el corresponsal. Hasta que lo mande, se deja lo mismo: es lo que hace todo el
        // mundo, y el operador lo puede corregir antes de guardar.
        InformeEnviado = fila.Decodificacion.Decibelios.ToString("+00;-00;+00", CultureInfo.InvariantCulture);
        InformeRecibido = mensaje.Informe is { } db ? db.ToString("+00;-00;+00", CultureInfo.InvariantCulture) : InformeEnviado;
        ContactoCompleto = false;

        TonoDeRecepcion = fila.TonoHz;
        if (!MantenerTx) TonoDeTransmision = fila.TonoHz;

        GenerarMensajes();

        var decision = _secuenciador.Iniciar(
            new MensajeOido(mensaje, fila.Decodificacion.Decibelios, fila.TonoHz),
            fila.Decodificacion.VentanaUtc,
            Reloj.Ahora);

        if (_secuenciador.Activo)
        {
            TxSiguiente = _secuenciador.TxActual;
            MensajeAEmitir = Mensajes[TxSiguiente - 1].Texto;
        }

        NotificarEstadoDeLaSecuencia();
        Aviso = Textos.F("Digital.Modem.Aviso.ContactoPreparado", suyo, TxSiguiente);

        if (decision.ContactoCompleto)
        {
            ContactoCompleto = true;
            AlCompletarseElContacto();
        }

        if (decision.TonoTx is { } tono && !MantenerTx) TonoDeTransmision = tono;
        if (EsBaliza || !_secuenciador.Activo) return;

        if (!TxHabilitado && !IntentarHabilitarTx()) return;

        // No se usa el «ya toca» del secuenciador para emitir: se emite solo si AHORA es el
        // principio de una ventana propia. Antes se emitia en el acto y, pulsando a media
        // ventana, se salia encima del corresponsal.
        Aviso = Textos.F("Digital.Modem.Aviso.Contestando", suyo, TxSiguiente, ProximaVentanaTexto(_secuenciador.Paridad));
        await EmitirSiEsSuVentanaAsync().ConfigureAwait(true);
    }

    /// <summary>Lleva la recepcion (y la transmision, si no esta fijada) al tono de la decodificacion elegida.</summary>
    [RelayCommand(CanExecute = nameof(HayDecodificacionElegida))]
    public void SintonizarEnLaDecodificacion()
    {
        if (DecodificacionElegida is { } fila) SintonizarEn(fila.TonoHz);
    }

    /// <summary>Olvida el contacto a medias.</summary>
    [RelayCommand(CanExecute = nameof(HayContactoEnCurso))]
    public void OlvidarContacto()
    {
        Corresponsal = string.Empty;
        LocalizadorDelCorresponsal = string.Empty;
        InformeEnviado = string.Empty;
        InformeRecibido = string.Empty;
        MensajeAEmitir = string.Empty;
        ContactoCompleto = false;
        Aviso = string.Empty;
        GenerarMensajes();
    }

    /// <summary>
    /// Mete en el cuaderno el contacto que se ha hecho con el modem propio.
    /// </summary>
    /// <remarks>
    /// La frecuencia que se apunta es la del dial <b>mas el tono de audio</b>, que es donde de
    /// verdad estaba la señal. Y el modo se apunta como manda ADIF: <b>FT8 es modo principal</b>,
    /// mientras que <b>FT4 es submodo de MFSK</b>.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(HayContactoEnCurso))]
    public async Task RegistrarContactoAsync()
    {
        if (!Indicativo.TryParse(Corresponsal, out var suyo))
        {
            Aviso = Textos.F("Digital.Modem.Aviso.IndicativoNoValido", Corresponsal);
            return;
        }

        if (_contactoYaGuardado)
        {
            Aviso = Textos.F("Digital.Modem.Aviso.YaGuardado", suyo.Valor);
            return;
        }

        var qso = ConstruirElContacto(suyo);
        if (!await MeterEnElCuadernoAsync(qso).ConfigureAwait(true)) return;

        OlvidarContacto();
        Aviso = Textos.F("Digital.Aviso.ContactoGuardado", suyo.Valor);
        CuadernoCambiado?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Apunta solo el contacto que la secuencia ha dado por completo, por el mismo camino que
    /// el boton «Guardar en el cuaderno». No lo apunta dos veces.
    /// </summary>
    private async Task GuardarElContactoCompletoAsync()
    {
        if (!RegistrarAlCompletar || _contactoYaGuardado) return;
        if (!Indicativo.TryParse(Corresponsal, out var suyo)) return;

        var qso = ConstruirElContacto(suyo);
        var banda = qso.Freq.EsCero ? string.Empty : Banda.DesdeFrecuencia(qso.Freq).Nombre ?? string.Empty;
        var modo = DescripcionDelModo.Nombre(Modo);
        var fin = qso.FinUtc ?? qso.InicioUtc;

        if (_ultimoGuardadoSolo is { } ultimo
            && ultimo.Indicativo == suyo.Valor && ultimo.Banda == banda && ultimo.Modo == modo
            && (fin - ultimo.Fin).Duration() < MargenDeRepeticion)
        {
            _contactoYaGuardado = true;
            return;
        }

        // Se marca antes de esperar: un segundo «completo» que llegue mientras tanto no duplica.
        _contactoYaGuardado = true;
        if (!await MeterEnElCuadernoAsync(qso).ConfigureAwait(true))
        {
            _contactoYaGuardado = false;
            return;
        }

        _ultimoGuardadoSolo = (suyo.Valor, banda, modo, fin);
        Log.Information("Contacto con {Indicativo} guardado solo al completarse ({Banda} {Modo}).", suyo.Valor, banda, modo);
        Informar(banda.Length > 0
            ? Textos.F("Digital.Modem.Aviso.GuardadoSolo", suyo.Valor, banda, modo)
            : Textos.F("Digital.Modem.Aviso.GuardadoSoloSinBanda", suyo.Valor, modo));
        CuadernoCambiado?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// El ultimo guardado solo que se ha lanzado. La interfaz no espera por el; las pruebas si.
    /// </summary>
    public Task GuardadoEnCurso { get; private set; } = Task.CompletedTask;

    /// <summary>La secuencia da el contacto por hecho: se apunta ya, o al salir el RR73 propio.</summary>
    private void AlCompletarseElContacto()
    {
        if (!RegistrarAlCompletar || _contactoYaGuardado) return;

        if (_secuenciador.Activo && _secuenciador.TxActual == 4)
        {
            _guardarTrasElRr73 = true;
            Aviso = Textos.F("Digital.Modem.Aviso.SeGuardaTrasRr73", Corresponsal);
            return;
        }

        GuardadoEnCurso = GuardarElContactoCompletoAsync();
    }

    /// <summary>
    /// El contacto con lo que hay en pantalla. La frecuencia es la del dial <b>mas el tono de
    /// audio</b>, que es donde de verdad estaba la señal, y el modo va como manda ADIF: <b>FT8
    /// es modo principal</b>, <b>FT4 es submodo de MFSK</b>.
    /// </summary>
    private Qso ConstruirElContacto(Indicativo suyo)
    {
        var fin = Reloj.Ahora;
        var inicio = _inicioDelContacto is { } empezo && empezo <= fin ? empezo : fin;

        var qso = new Qso
        {
            Call = suyo,
            Mode = DescripcionDelModo.ModoAdif(Modo),
            Freq = FrecuenciaDelContacto(),
            InicioUtc = inicio,
            FinUtc = fin,
            RstSent = Informe.Parse(InformeEnviado),
            RstRcvd = Informe.Parse(InformeRecibido),
            Origen = "módem propio",
        };

        if (Locator.TryParse(LocalizadorDelCorresponsal, out var rejilla)) qso.Gridsquare = rejilla;
        return qso;
    }

    private async Task<bool> MeterEnElCuadernoAsync(Qso qso)
    {
        try
        {
            var resultado = await _registrar
                .EjecutarAsync(new PeticionDeRegistro
                {
                    Qso = qso,
                    EstacionId = EstacionId,
                    AdmitirDuplicado = true,
                })
                .ConfigureAwait(true);

            if (!resultado.Correcto)
            {
                Aviso = Textos.F("Digital.Aviso.NoSePudoGuardar", string.Join("; ", resultado.Errores));
                return false;
            }

            _novedad.Olvidar();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido guardar el contacto del módem propio.");
            Aviso = Textos.F("Digital.Aviso.NoSePudoGuardarElContacto", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Emite el mensaje libre en la siguiente ventana que toque.
    /// </summary>
    /// <remarks>
    /// <b>Esto pone el equipo EN ANTENA trece segundos.</b> Pasa entero por el vigilante de
    /// PTT —de eso se encarga el modem—, exige que el pestillo este abierto y pregunta antes.
    /// Ademas se niega si el reloj esta tan desviado que se transmitiria fuera de ventana.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(SePuedeEmitirAhora))]
    public async Task EmitirAsync()
    {
        var texto = MensajeAEmitir.Trim();
        if (texto.Length == 0)
        {
            Aviso = Textos.T("Digital.Modem.Aviso.SinMensaje");
            return;
        }

        if (!OperadorConforme(texto)) return;

        if (Reloj.RelojFueraDeVentana || !SePuedeEmitir)
        {
            // Lo dice EmitirTextoAsync, que es quien tiene las razones escritas.
            await EmitirTextoAsync(texto, 0).ConfigureAwait(true);
            return;
        }

        // Sale al principio de una ventana, no en el acto: si se pulsa a los pocos segundos
        // de empezar una, en esa; si no, en la siguiente (la de la secuencia, si hay una).
        var periodo = DescripcionDelModo.Periodo(Modo);
        var ahora = Reloj.Ahora;
        var paridad = _secuenciador.Paridad >= 0 && _secuenciador.Activo
            ? _secuenciador.Paridad
            : EsMomentoDeEmitir(SecuenciadorDeQso.ParidadDe(ComienzoDeVentana(ahora, periodo), periodo))
                ? SecuenciadorDeQso.ParidadDe(ComienzoDeVentana(ahora, periodo), periodo)
                : SecuenciadorDeQso.ParidadDeLaSiguiente(ahora, periodo);

        if (EsMomentoDeEmitir(paridad))
        {
            await EmitirTextoAsync(texto, 0).ConfigureAwait(true);
            return;
        }

        _librePendiente = (texto, paridad);
        Aviso = Textos.F("Digital.Modem.Aviso.LibrePendiente", texto, ProximaVentanaTexto(paridad));
    }

    /// <summary>
    /// Corta la emision en curso, suelta el PTT y para la secuencia. Funciona siempre, haya
    /// o no emision: soltar un PTT que ya estaba suelto no hace daño.
    /// </summary>
    [RelayCommand]
    public async Task AbortarEmisionAsync()
    {
        var habiaEmision = _emitiendo || (_modem?.EstaEmitiendo ?? false);
        DetenerTx();
        _librePendiente = null;
        if (_modem is null) return;

        try
        {
            await _modem.AbortarEmisionAsync().ConfigureAwait(true);

            // El aviso dice lo que ha pasado de verdad y se va solo: antes se quedaba puesto
            // para siempre, aunque no hubiera habido ninguna emision que cortar.
            Informar(habiaEmision
                ? Textos.T("Digital.Modem.Aviso.EmisionCortada")
                : Textos.T("Digital.Modem.Aviso.NoHabiaEmision"));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido abortar la emisión.");
            Aviso = Textos.F("Digital.Modem.Aviso.NoSePudoCortar", ex.Message);
        }
    }

    // ── Frecuencias de trabajo ────────────────────────────────────────────

    /// <summary>Lleva el equipo, por CAT, a la frecuencia de trabajo del modo en la banda del dial.</summary>
    [RelayCommand]
    public async Task IrALaFrecuenciaDelModoAsync()
    {
        var f = Digital.FrecuenciasDeTrabajo.Para(FrecuenciasDeTrabajo, Modo, _dial);
        if (f is null)
        {
            Aviso = Textos.F("Digital.Modem.Aviso.SinFrecuenciaApuntada", ModoTexto);
            return;
        }

        await IrAAsync(f).ConfigureAwait(true);
    }

    /// <summary>Lleva el equipo a la frecuencia elegida en la tabla.</summary>
    [RelayCommand(CanExecute = nameof(HayFrecuenciaElegida))]
    public async Task IrALaFrecuenciaElegidaAsync()
    {
        if (FrecuenciaElegida is { } f) await IrAAsync(f).ConfigureAwait(true);
    }

    /// <summary>Añade una fila a la tabla, con el modo actual.</summary>
    [RelayCommand]
    public void AnadirFrecuencia()
    {
        var nueva = new FrecuenciaDeTrabajo { Modo = Modo, Megahercios = _dial.EsCero ? 14.074m : _dial.Megahercios };
        FrecuenciasDeTrabajo.Add(nueva);
        FrecuenciaElegida = nueva;
        OnPropertyChanged(nameof(FrecuenciaDelModoTexto));
    }

    /// <summary>Quita la fila elegida.</summary>
    [RelayCommand(CanExecute = nameof(HayFrecuenciaElegida))]
    public void QuitarFrecuencia()
    {
        if (FrecuenciaElegida is { } f) FrecuenciasDeTrabajo.Remove(f);
        FrecuenciaElegida = null;
        OnPropertyChanged(nameof(FrecuenciaDelModoTexto));
    }

    /// <summary>Vuelve a poner las de WSJT-X.</summary>
    [RelayCommand]
    public void RestaurarFrecuencias()
    {
        FrecuenciasDeTrabajo.Clear();
        foreach (var f in Digital.FrecuenciasDeTrabajo.DeFabrica()) FrecuenciasDeTrabajo.Add(f);
        OnPropertyChanged(nameof(FrecuenciaDelModoTexto));
    }

    // ── Ficheros ──────────────────────────────────────────────────────────

    /// <summary>Pide un WAV y lo pasa por el decodificador: lo que salga entra en la lista, sin tocar la secuencia.</summary>
    [RelayCommand]
    public async Task DecodificarFicheroAsync()
    {
        if (_modem is null) return;

        var ruta = ElegirFicheroWav?.Invoke();
        if (string.IsNullOrEmpty(ruta)) return;

        try
        {
            Aviso = Textos.F("Digital.Modem.Aviso.Decodificando", Path.GetFileName(ruta));
            var decodificaciones = await _modem.DecodificarFicheroAsync(ruta, Modo).ConfigureAwait(true);
            var ventana = new VentanaDecodificada(DateTimeOffset.UtcNow, decodificaciones, TimeSpan.Zero, double.NaN);
            await ProcesarVentanaAsync(ventana, alimentarLaSecuencia: false).ConfigureAwait(true);
            Aviso = Textos.F("Digital.Modem.Aviso.Decodificado", Path.GetFileName(ruta), decodificaciones.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido decodificar el fichero {Ruta}.", ruta);
            Aviso = Textos.F("Digital.Modem.Aviso.NoSePudoDecodificar", ex.Message);
        }
    }

    // ── La emision, por un solo sitio ─────────────────────────────────────

    /// <summary>
    /// Emite el mensaje Tx<paramref name="tx"/> en la siguiente ventana. Es el unico camino de
    /// la secuencia hacia el aire, y pasa por todo: modem, salida abierta, pestillo, reloj y
    /// vigilante.
    /// </summary>
    public async Task EmitirTxAsync(int tx)
    {
        if (tx is < 1 or > 6 || !TxHabilitado || _emitiendo) return;

        var texto = Mensajes[tx - 1].Texto.Trim();
        if (texto.Length == 0 || texto.Contains('<', StringComparison.Ordinal))
        {
            Aviso = Textos.F("Digital.Modem.Aviso.TxIncompleto", tx, texto);
            DetenerTx();
            return;
        }

        if (OperacionElegida.Valor == TipoDeOperacion.Hound && TonoDeTransmision < TonoMinimoDeHound && tx == 1)
        {
            Aviso = Textos.F("Digital.Modem.Aviso.HoundTono", TonoMinimoDeHound);
            DetenerTx();
            return;
        }

        MensajeAEmitir = texto;
        MarcarElSiguiente(tx);

        var salio = await EmitirTextoAsync(texto, tx).ConfigureAwait(true);
        if (!salio) return;

        if (_guardarTrasElRr73 && tx is 4 or 5)
        {
            _guardarTrasElRr73 = false;
            GuardadoEnCurso = GuardarElContactoCompletoAsync();
            await GuardadoEnCurso.ConfigureAwait(true);
        }

        var tras = _secuenciador.EmisionHecha(tx);
        AplicarLaDecision(tras);
    }

    private async Task<bool> EmitirTextoAsync(string texto, int tx)
    {
        if (_modem is null || !SePuedeEmitir)
        {
            Aviso = AvisoDeLaTransmision;
            if (tx > 0) DetenerTx();
            return false;
        }

        if (Reloj.RelojFueraDeVentana)
        {
            Aviso = Textos.T("Digital.Modem.Aviso.RelojMal");
            if (tx > 0) DetenerTx();
            return false;
        }

        if (_emitiendo) return false;
        _emitiendo = true;

        try
        {
            var ventana = Reloj.Ahora;
            await _modem.EmitirAsync(texto, TonoDeTransmision).ConfigureAwait(true);
            Informar(Textos.F("Digital.Modem.Aviso.Emitido", texto));
            AnadirALaLista(FilaDeDecodificacionPropia.DeTransmision(texto, TonoDeTransmision, ventana, Modo));
            if (tx > 0 && HayContactoEnCurso) _inicioDelContacto ??= ventana;
            ApuntarEnElRegistro(ventana, "Tx", texto, 0, 0, TonoDeTransmision);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido emitir con el módem propio.");
            // El módem envuelve el motivo en una ArgumentException, que le pega «(Parameter
            // 'texto')»: al operador se le enseña el motivo tal cual, que ya está en español.
            var motivo = ex is ArgumentException { InnerException: FormatException f } ? f.Message : ex.Message;
            Aviso = Textos.F("Digital.Modem.Aviso.NoSePudoEmitir", motivo);
            if (tx > 0) DetenerTx();
            return false;
        }
        finally
        {
            _emitiendo = false;
        }
    }

    /// <summary>
    /// Intenta poner Tx habilitado: hace falta el pestillo, la salida abierta y que el
    /// operador diga que si. Si algo falta, se dice y no se enciende.
    /// </summary>
    private bool IntentarHabilitarTx()
    {
        if (!SePuedeEmitir)
        {
            Aviso = Textos.F("Digital.Modem.Aviso.PreparadoSinSalir", AvisoDeLaTransmision);
            return false;
        }

        if (!OperadorConforme(Textos.T("Digital.Modem.Pregunta.Secuencia")))
        {
            return false;
        }

        TxHabilitado = true;
        return true;
    }

    /// <summary>
    /// Lo mas tarde que se admite empezar dentro de una ventana: la septima parte y media del
    /// periodo (2 s en FT8, 1 s en FT4). Mas alla, el otro lado ya no decodifica bien y lo que
    /// se hace es pisar la ventana; se espera a la siguiente propia.
    /// </summary>
    public static TimeSpan RetrasoMaximo(TimeSpan periodo) => periodo / 7.5;

    /// <summary>Comienzo de la ventana en la que cae <paramref name="instante"/>.</summary>
    public static DateTimeOffset ComienzoDeVentana(DateTimeOffset instante, TimeSpan periodo) =>
        new(instante.UtcTicks - (instante.UtcTicks % periodo.Ticks), TimeSpan.Zero);

    /// <summary>
    /// Ahora mismo es el principio de una ventana de esa paridad, con tiempo de sobra para que
    /// el mensaje llegue entero y a su hora.
    /// </summary>
    public bool EsMomentoDeEmitir(int paridad)
    {
        if (paridad < 0) return false;

        var periodo = DescripcionDelModo.Periodo(Modo);
        var ahora = Reloj.Ahora;
        var inicio = ComienzoDeVentana(ahora, periodo);
        return SecuenciadorDeQso.ParidadDe(inicio, periodo) == paridad
               && ahora - inicio <= RetrasoMaximo(periodo);
    }

    private string ProximaVentanaTexto(int paridad)
    {
        var periodo = DescripcionDelModo.Periodo(Modo);
        var inicio = ComienzoDeVentana(Reloj.Ahora, periodo);
        if (EsMomentoDeEmitir(paridad)) return Textos.T("Digital.Modem.Ahora");

        var siguiente = inicio + periodo;
        if (paridad >= 0 && SecuenciadorDeQso.ParidadDe(siguiente, periodo) != paridad) siguiente += periodo;
        return siguiente.UtcDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " UTC";
    }

    /// <summary>
    /// Si la secuencia esta en marcha con Tx habilitado y AHORA empieza una ventana propia,
    /// emite lo que toque. Si no, no hace nada: lo hara la siguiente ventana decodificada.
    /// </summary>
    private async Task EmitirSiEsSuVentanaAsync()
    {
        if (!TxHabilitado || !_secuenciador.Activo || _emitiendo) return;
        if (!EsMomentoDeEmitir(_secuenciador.Paridad)) return;

        var tx = SecuenciaAutomatica ? _secuenciador.TxActual : TxSiguiente;
        if (tx is >= 1 and <= 6) await EmitirTxAsync(tx).ConfigureAwait(true);
    }

    /// <summary>Un aviso que solo cuenta algo ya hecho: se va solo al rato.</summary>
    private void Informar(string texto)
    {
        Aviso = texto;
        try
        {
            _caducidadDelAviso.Start();
        }
        catch (InvalidOperationException)
        {
            // Sin hilo de ventana (en pruebas) se queda puesto; tiene su boton de cerrar.
        }
    }

    partial void OnAvisoChanged(string value) => _caducidadDelAviso?.Stop();

    private void AplicarLaDecision(DecisionDelSecuenciador decision)
    {
        if (decision.ContactoCompleto)
        {
            ContactoCompleto = true;
            Aviso = Textos.F("Digital.Modem.Aviso.GuardeloEnElCuaderno", Corresponsal);
            AlCompletarseElContacto();
        }

        if (decision.Parada is { } motivo)
        {
            TxHabilitado = false;
            Aviso = decision.ContactoCompleto ? Aviso : motivo;

            // Se esperaba el RR73 propio, pero el corresponsal ha cerrado antes: tambien vale.
            if (_guardarTrasElRr73 && _secuenciador.Terminado)
            {
                _guardarTrasElRr73 = false;
                GuardadoEnCurso = GuardarElContactoCompletoAsync();
            }
        }

        if (decision.TonoTx is { } tono && !MantenerTx) TonoDeTransmision = tono;

        if (_secuenciador.Activo && _secuenciador.TxActual > 0)
        {
            TxSiguiente = _secuenciador.TxActual;
            MensajeAEmitir = Mensajes[TxSiguiente - 1].Texto;
        }

        NotificarEstadoDeLaSecuencia();
    }

    private void MarcarElSiguiente(int tx)
    {
        foreach (var m in Mensajes) m.EsElSiguiente = m.Numero == tx;
    }

    // ── Lo que llega del modem ────────────────────────────────────────────

    private void AlLlegarUnaColumna(object? origen, ColumnaDeCascada columna) => Hilo.EnLaVentana(() =>
    {
        Pintor.Anadir(columna);
        if (!ReferenceEquals(ImagenDeLaCascada, Pintor.Imagen)) ImagenDeLaCascada = Pintor.Imagen;
    });

    /// <summary>
    /// Mete en la lista lo que ha salido de una ventana y se lo pasa a la secuencia.
    /// </summary>
    /// <remarks>
    /// Es un manejador asincrono —consulta el cuaderno para saber que hay de nuevo— y por eso
    /// se traga todo: una decodificacion rara no puede tumbar el programa en mitad de una
    /// apertura.
    /// </remarks>
    private async void AlTerminarUnaVentana(object? origen, VentanaDecodificada ventana)
    {
        try
        {
            await ProcesarVentanaAsync(ventana, alimentarLaSecuencia: true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A Information, no a Debug: una ventana que no se procesa puede ser una emision
            // perdida, y eso tiene que quedar en el registro de verdad, no solo si alguien
            // hubiera subido el nivel a mano. Confirmado el 05-10-2026 en auditoria: con el
            // registro a Information (de serie), esto no dejaba ni una linea.
            Log.Error(ex, "No se ha podido procesar una ventana del módem propio.");
        }
    }

    private async Task ProcesarVentanaAsync(VentanaDecodificada ventana, bool alimentarLaSecuencia)
    {
        var banda = _dial.EsCero ? Banda.Vacia : Banda.DesdeFrecuencia(_dial);
        var modoAdif = DescripcionDelModo.ModoAdif(Modo);
        var tonoRx = TonoDeRecepcion;
        var mi = MiIndicativo;

        var filas = new List<FilaDeDecodificacionPropia>(ventana.Decodificaciones.Count);
        var oidos = new List<MensajeOido>(ventana.Decodificaciones.Count);

        foreach (var decodificacion in ventana.Decodificaciones)
        {
            var mensaje = InterpreteDeMensajes.Analizar(decodificacion.Texto);
            var llamante = !decodificacion.Llamante.EsVacio
                ? decodificacion.Llamante
                : Indicativo.TryParse(mensaje.Llamante, out var i) ? i : Indicativo.Vacio;
            var locator = !decodificacion.Locator.EsVacio
                ? decodificacion.Locator
                : Locator.TryParse(mensaje.Locator, out var l) ? l : Locator.Vacio;

            var novedad = await _novedad.EvaluarAsync(llamante, locator, banda, modoAdif).ConfigureAwait(false);
            filas.Add(new FilaDeDecodificacionPropia(decodificacion, novedad, mi, mensaje, tonoRx));
            oidos.Add(new MensajeOido(mensaje, decodificacion.Decibelios, decodificacion.TonoHz));

            if (PskReporterActivo && !llamante.EsVacio && !locator.EsVacio && !_dial.EsCero)
            {
                _psk.Anotar(new RecepcionParaInformar(
                    llamante.Valor, locator.Valor, _dial.Hercios + decodificacion.TonoHz,
                    decodificacion.Decibelios, DescripcionDelModo.Nombre(decodificacion.Modo), decodificacion.VentanaUtc));
            }
        }

        Hilo.EnLaVentana(() =>
        {
            foreach (var fila in filas)
            {
                AnadirALaLista(fila);
                ApuntarEnElRegistro(
                    fila.Decodificacion.VentanaUtc, "Rx", fila.Texto,
                    fila.Decodificacion.Decibelios, fila.Decodificacion.DesfaseSegundos, fila.TonoHz);
            }

            if (!double.IsNaN(ventana.RuidoDbm))
            {
                LlegoTarde = ventana.LlegoTarde;
                UltimaVentana = Textos.F(
                    "Digital.Modem.UltimaVentana",
                    ventana.VentanaUtc.UtcDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                    ventana.Decodificaciones.Count,
                    ventana.DuracionDelProceso.TotalSeconds);

                if (ventana.LlegoTarde)
                {
                    Aviso = Textos.T("Digital.Modem.Aviso.VentanaTarde");
                }
            }

            if (GuardarWav && CarpetaDeDatos is { Length: > 0 } carpeta && !double.IsNaN(ventana.RuidoDbm))
            {
                try
                {
                    _grabador.Guardar(Path.Combine(carpeta, "wav"), ventana.VentanaUtc, DescripcionDelModo.Periodo(Modo));
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "No se ha podido guardar el WAV de la ventana.");
                }
            }

            OnPropertyChanged(nameof(PskReporterTexto));

            if (alimentarLaSecuencia && !EsBaliza)
            {
                // Con la secuencia automatica apagada no se avanza sola: se repite el mensaje
                // que haya marcado el operador en cada ventana propia, como en WSJT-X.
                if (SecuenciaAutomatica)
                {
                    var decision = _secuenciador.Procesar(ventana.VentanaUtc, oidos);
                    var antes = Corresponsal;
                    RecogerLoQueDijoElDx();
                    if (HayContactoEnCurso && !string.Equals(antes, Corresponsal, StringComparison.Ordinal))
                    {
                        _inicioDelContacto = ventana.VentanaUtc;
                    }

                    AplicarLaDecision(decision);
                }

                // Se emite solo si ahora empieza una ventana propia y se llega a tiempo. Si el
                // decodificador tardo mas de la cuenta, se pierde este turno en vez de salir
                // tarde encima de la ventana de otro.
                //
                // Esto se dispara y se olvida dentro de un delegado sincrono (Hilo.EnLaVentana):
                // una excepcion que saltara dentro, pasado el primer await, no la coge ningun
                // try/catch de los de alrededor ni el manejador de excepciones del Dispatcher, y
                // se perdia sin dejar ni una linea en el registro (visto en auditoria el
                // 05-10-2026). El ContinueWith es la red: si falla, al menos queda apuntado.
                _ = EmitirSiEsSuVentanaAsync().ContinueWith(
                    t => Log.Error(t.Exception, "No se ha podido emitir al terminar la ventana."),
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);
            }

            if (alimentarLaSecuencia && _librePendiente is { } libre && EsMomentoDeEmitir(libre.Paridad))
            {
                _librePendiente = null;
                _ = EmitirTextoAsync(libre.Texto, 0);
            }

            VentanaProcesada?.Invoke(this, ventana.VentanaUtc);
        });
    }

    /// <summary>Lo que el secuenciador ha aprendido del DX pasa a los campos del contacto.</summary>
    private void RecogerLoQueDijoElDx()
    {
        // El decodificador aprovecha lo que ya se sabe del QSO (decodificacion AP) aunque el
        // secuenciador este parado: si se acaba de completar un contacto y DxCall queda vacio,
        // esto lo apaga solo en la siguiente llamada.
        _modem?.FijarPistaDeQso(_secuenciador.MiIndicativo, _secuenciador.DxCall);

        if (!_secuenciador.Activo && _secuenciador.DxCall.Length == 0) return;

        var cambia = false;
        if (_secuenciador.DxCall.Length > 0 && !string.Equals(Corresponsal, _secuenciador.DxCall, StringComparison.Ordinal))
        {
            Corresponsal = _secuenciador.DxCall;
            cambia = true;
        }

        if (_secuenciador.DxGrid.Length >= 4 && !string.Equals(LocalizadorDelCorresponsal, _secuenciador.DxGrid, StringComparison.Ordinal))
        {
            LocalizadorDelCorresponsal = _secuenciador.DxGrid;
            cambia = true;
        }

        if (_secuenciador.InformeEnviado is { } enviado)
        {
            var texto = enviado.ToString("+00;-00;+00", CultureInfo.InvariantCulture);
            if (InformeEnviado != texto) { InformeEnviado = texto; cambia = true; }
        }

        if (_secuenciador.InformeRecibido is { } recibido)
        {
            InformeRecibido = recibido.ToString("+00;-00;+00", CultureInfo.InvariantCulture);
        }
        else if (_secuenciador.IntercambioRecibido.Length > 0)
        {
            InformeRecibido = _secuenciador.IntercambioRecibido;
        }

        if (cambia) GenerarMensajes();
    }

    private void AnadirALaLista(FilaDeDecodificacionPropia fila)
    {
        _todas.Insert(0, fila);
        while (_todas.Count > DecodificacionesQueSeGuardan) _todas.RemoveAt(_todas.Count - 1);

        if (PasaLosFiltros(fila)) Decodificaciones.Insert(0, fila);
        while (Decodificaciones.Count > DecodificacionesQueSeGuardan) Decodificaciones.RemoveAt(Decodificaciones.Count - 1);
    }

    private bool PasaLosFiltros(FilaDeDecodificacionPropia fila)
    {
        if (fila.EsTx) return true;
        if (SoloNuevos && !fila.Novedad.HayAlgo) return false;
        if (SoloCq && !fila.EsCq) return false;
        if (SoloMeLlaman && !fila.MeLlaman) return false;
        return true;
    }

    private void VolverAFiltrar()
    {
        Decodificaciones.Clear();
        foreach (var fila in _todas.Where(PasaLosFiltros)) Decodificaciones.Add(fila);
    }

    private void ApuntarEnElRegistro(DateTimeOffset ventana, string sentido, string texto, int db, double desfase, int tono)
    {
        if (!GuardarDecodificacionesEnTexto || CarpetaDeDatos is not { Length: > 0 } carpeta) return;

        try
        {
            var linea = string.Create(
                CultureInfo.InvariantCulture,
                $"{ventana.UtcDateTime:yyMMdd_HHmmss} {_dial.Megahercios,10:0.000} {sentido} {ModoTexto,-6} {db,4} {desfase,5:0.0} {tono,5} {texto}\n");
            File.AppendAllText(Path.Combine(carpeta, "decodificaciones.txt"), linea);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "No se ha podido apuntar la decodificación en el registro de texto.");
        }
    }

    private void AlCapturarUnBloque(object? origen, BloqueDeAudio bloque)
    {
        if (GuardarWav) _grabador.Anadir(bloque);
    }

    private void AlPerderMuestras(object? origen, HuecoDeAudio hueco) => Hilo.EnLaVentana(() =>
        Aviso = Textos.F(
            "Digital.Modem.Aviso.MuestrasPerdidas",
            hueco.Muestras,
            hueco.InstanteUtc.UtcDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture)));

    private async Task EnviarAPskReporterAsync()
    {
        if (!PskReporterActivo || _psk.Pendientes == 0) return;

        var cuantos = _psk.Pendientes;
        var paquete = _psk.Empaquetar(DateTimeOffset.UtcNow);
        if (paquete is null) return;

        try
        {
            await ReportadorPskReporter.EnviarAsync(paquete).ConfigureAwait(true);
            RecepcionesInformadas += cuantos;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "No se ha podido mandar a PSK Reporter.");
        }

        OnPropertyChanged(nameof(PskReporterTexto));
    }

    private async Task IrAAsync(FrecuenciaDeTrabajo f)
    {
        if (_equipo is null)
        {
            Aviso = Textos.T("Digital.Modem.Aviso.SinEquipo");
            return;
        }

        try
        {
            await _equipo.PonerFrecuenciaAsync(f.Frecuencia).ConfigureAwait(true);
            Aviso = Textos.F("Digital.Modem.Aviso.EquipoEn", f.Megahercios.ToString("0.000###", CultureInfo.CurrentCulture), f.ModoTexto, f.Banda);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido mover el equipo a la frecuencia de trabajo.");
            Aviso = Textos.F("Digital.Modem.Aviso.NoSePudoMover", ex.Message);
        }
    }

    // ── Pequeños ──────────────────────────────────────────────────────────

    private bool SePuedeEscuchar() => HayModem && !Escuchando && !ModoApagado;

    private bool SePuedeParar() => Escuchando;

    private bool SePuedeEmitirAhora() => SePuedeEmitir;

    private bool HayDecodificacionElegida() => DecodificacionElegida is not null;

    private bool HayFrecuenciaElegida() => FrecuenciaElegida is not null;

    private Frecuencia FrecuenciaDelContacto() => _dial.EsCero
        ? Frecuencia.Cero
        : Frecuencia.DesdeHercios(_dial.Hercios + TonoDeTransmision);

    private DispositivoDeAudio? ElegirLaEntrada()
    {
        if (_entrada is null) return null;

        var guardado = _ajustes.Digital.DispositivoDeEntrada;
        if (!string.IsNullOrEmpty(guardado))
        {
            var elegido = _entrada.Dispositivos.FirstOrDefault(d => d.Id == guardado);
            if (elegido is not null) return elegido;
        }

        return _entrada.Dispositivos.FirstOrDefault(d => d.EsDelEquipo);
    }

    private DispositivoDeAudio? ElegirLaSalida()
    {
        if (_salida is null) return null;

        var guardado = _ajustes.Digital.DispositivoDeSalida;
        if (!string.IsNullOrEmpty(guardado))
        {
            var elegido = _salida.Dispositivos.FirstOrDefault(d => d.Id == guardado);
            if (elegido is not null) return elegido;
        }

        return _salida.Dispositivos.FirstOrDefault(d => d.EsDelEquipo);
    }

    private void LeerElNivel()
    {
        if (_entrada is null) return;
        NivelDeEntrada = _entrada.Nivel;
    }

    private async Task ReiniciarAsync()
    {
        await PararAsync().ConfigureAwait(true);
        await EscucharAsync().ConfigureAwait(true);
    }

    // ── Reacciones a los cambios ──────────────────────────────────────────

    partial void OnModoChanged(ModoDelModem value)
    {
        _secuenciador.CambiarPeriodo(DescripcionDelModo.Periodo(value));
        TxHabilitado = false;
        NotificarEstadoDeLaSecuencia();

        var opcion = Modos.FirstOrDefault(o => o.Valor == value);
        if (opcion is not null && !ReferenceEquals(ModoElegido, opcion)) ModoElegido = opcion;

        if (!Escuchando || _modem is null) return;

        // Cambiar de modo en caliente: se para y se vuelve a arrancar, porque el periodo es
        // otro y el troceado de ventanas tiene que empezar de cero.
        _ = ReiniciarAsync();
    }

    partial void OnModoElegidoChanged(Opcion<ModoDelModem> value)
    {
        if (value is not null && Modo != value.Valor) Modo = value.Valor;
    }

    partial void OnOperacionElegidaChanged(Opcion<TipoDeOperacion> value)
    {
        if (value is null) return;
        _secuenciador.Operacion = value.Valor;
        GenerarMensajes();
    }

    partial void OnPaletaElegidaChanged(Opcion<PaletaDeCascada> value)
    {
        if (value is not null) Pintor.Paleta = value.Valor;
    }

    partial void OnPermitirTransmitirChanged(bool value)
    {
        if (value)
        {
            Log.Warning("El operador ha abierto el pestillo de la transmisión del módem propio.");
        }
        else
        {
            TxHabilitado = false;
        }
    }

    partial void OnTxHabilitadoChanged(bool value)
    {
        if (value) Log.Warning("Tx habilitado en el módem propio: la secuencia emitirá sola.");
        NotificarEstadoDeLaSecuencia();
    }

    partial void OnModoApagadoChanged(bool value)
    {
        if (!value || !Escuchando) return;

        // Apagar el modo para de verdad: suelta la entrada y la salida compartidas en vez de
        // dejarlas abiertas sin usarlas (que es justo lo que no se puede permitir con RTTY
        // disputando la misma salida).
        Log.Warning("El operador ha apagado el modo digital: se deja de escuchar.");
        _ = PararAsync().ContinueWith(
            t => Log.Error(t.Exception, "No se ha podido parar el módem propio al apagar el modo."),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    partial void OnTxSiguienteChanged(int value) => MarcarElSiguiente(value);

    partial void OnSoloNuevosChanged(bool value) => VolverAFiltrar();

    partial void OnSoloCqChanged(bool value) => VolverAFiltrar();

    partial void OnSoloMeLlamanChanged(bool value) => VolverAFiltrar();

    partial void OnSaltarTx1Changed(bool value) => _secuenciador.SaltarTx1 = value;

    partial void OnLlamarAlPrimeroChanged(bool value) => _secuenciador.LlamarAlPrimero = value;

    partial void OnCiclosSinRespuestaChanged(int value) => _secuenciador.CiclosSinRespuesta = Math.Clamp(value, 1, 100);

    partial void OnTx4ConRrrChanged(bool value) => GenerarMensajes();

    partial void OnCqDirigidoChanged(string value) => GenerarMensajes();

    partial void OnIntercambioDeConcursoChanged(string value) => GenerarMensajes();

    partial void OnCorresponsalChanged(string value)
    {
        ReiniciarElContacto();
        if (!_generando) GenerarMensajes();
        _ = BuscarElNombreAsync(value);
    }

    /// <summary>
    /// Quien completa los contactos con la ficha de QRZ.com. Sin el (modo simulado, o sin
    /// cuenta), el nombre del corresponsal simplemente no sale.
    /// </summary>
    public CompletadorDeQso? Completador { get; init; }

    /// <summary>La pausa antes de pedir el nombre; las pruebas la sustituyen para no esperar.</summary>
    public Func<TimeSpan, CancellationToken, Task> EsperarAntesDelNombre { get; init; } = Task.Delay;

    /// <summary>Nombre del corresponsal segun su ficha de QRZ.com, para verlo junto al DX Call.</summary>
    [ObservableProperty]
    private string _nombreDelCorresponsal = string.Empty;

    private CancellationTokenSource? _busquedaDelNombre;

    /// <summary>
    /// Pide la ficha del corresponsal y deja su nombre a la vista. Espera un momento antes de
    /// preguntar para no consultar por cada letra tecleada, y descarta la respuesta si entre
    /// tanto ha cambiado el corresponsal.
    /// </summary>
    private async Task BuscarElNombreAsync(string corresponsal)
    {
        _busquedaDelNombre?.Cancel();
        NombreDelCorresponsal = string.Empty;
        if (Completador is not { EstaDisponible: true }) return;
        if (!Nodisla.Cuaderno.Dominio.Valores.Indicativo.TryParse(corresponsal, out var indicativo)) return;

        var cts = new CancellationTokenSource();
        _busquedaDelNombre = cts;
        try
        {
            await EsperarAntesDelNombre(TimeSpan.FromMilliseconds(400), cts.Token).ConfigureAwait(true);
            var ficha = await Completador.ConsultarAsync(indicativo, cts.Token).ConfigureAwait(true);
            if (cts.IsCancellationRequested || ficha is null) return;
            if (!string.Equals(Corresponsal, corresponsal, StringComparison.Ordinal)) return;
            NombreDelCorresponsal = ficha.Nombre?.Trim() ?? string.Empty;
        }
        catch (OperationCanceledException)
        {
            // Llego otro corresponsal: esta respuesta ya no interesa.
        }
        catch (Exception ex)
        {
            // Sin red o sin cuenta no es un fallo del contacto: simplemente no hay nombre.
            Log.Debug(ex, "No se ha podido traer el nombre de {Corresponsal}.", corresponsal);
        }
    }

    /// <summary>Otro corresponsal, otro contacto: nada de lo guardado del anterior vale.</summary>
    private void ReiniciarElContacto()
    {
        _inicioDelContacto = null;
        _guardarTrasElRr73 = false;
        _contactoYaGuardado = false;
    }

    partial void OnLocalizadorDelCorresponsalChanged(string value)
    {
        if (!_generando) GenerarMensajes();
    }

    partial void OnInformeEnviadoChanged(string value)
    {
        if (!_generando) GenerarMensajes();
    }

    partial void OnPskReporterActivoChanged(bool value)
    {
        if (value && Escuchando) _relojDePsk.Start();
        if (!value) _relojDePsk.Stop();
    }

    partial void OnGananciaDeLaCascadaChanged(double value) => Pintor.GananciaDb = value;

    partial void OnCeroDeLaCascadaChanged(double value) => Pintor.CeroDb = value;

    partial void OnPromedioDeColumnasChanged(int value) => Pintor.PromedioDeColumnas = Math.Clamp(value, 1, 10);

    partial void OnAnchoVisibleHzChanged(int value) => Pintor.AnchoVisibleHz = Math.Clamp(value, 1000, 5000);
}
