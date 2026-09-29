using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Ui.Conversores;
using Serilog;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>Lo que el equipo acaba de poner en el dial.</summary>
/// <param name="Frecuencia">Frecuencia del VFO activo.</param>
/// <param name="Modo">Modo que tiene puesto el equipo.</param>
public sealed record DialDelEquipo(Frecuencia Frecuencia, Modo Modo, Frecuencia? FrecuenciaRx = null);

/// <summary>
/// Panel del equipo: que frecuencia y que modo tiene puestos, si esta en antena y el boton
/// que suelta el PTT pase lo que pase.
/// </summary>
/// <remarks>
/// Dos cosas mandan en el diseno de este panel.
///
/// La primera es que la frecuencia y el modo se leen de un vistazo mientras se teclea un
/// indicativo, asi que van en grande y no compiten con nada.
///
/// La segunda es el boton de panico. Suelta el PTT sin preguntar y sin esperar a nada: un PTT
/// que se queda pegado quema la etapa final del equipo o el amplificador. Por eso el boton
/// esta siempre activo —tambien con el equipo desconectado, porque la desconexion puede ser
/// justo el sintoma— y por eso no pide confirmacion.
/// </remarks>
public sealed partial class VistaModeloEquipo : ObservableObject
{
    private readonly IControlEquipo _equipo;
    private readonly IVigilantePtt _vigilante;
    private readonly DispatcherTimer _medicion;

    /// <summary>Monta el panel sobre el control del equipo y su vigilante.</summary>
    /// <param name="equipo">Control del equipo.</param>
    /// <param name="vigilante">Vigilante que garantiza que el PTT se suelta.</param>
    /// <param name="bandplan">Plan de bandas con el que se avisa de fuera de banda.</param>
    public VistaModeloEquipo(IControlEquipo equipo, IVigilantePtt vigilante, IBandplan? bandplan = null)
    {
        ArgumentNullException.ThrowIfNull(equipo);
        ArgumentNullException.ThrowIfNull(vigilante);

        _equipo = equipo;
        _vigilante = vigilante;

        A = new VistaModeloVfo(NombreDeVfo.A, bandplan);
        B = new VistaModeloVfo(NombreDeVfo.B, bandplan);

        _equipo.EstadoCambiado += AlCambiarElEstado;
        _vigilante.PttSoltado += AlSoltarElPtt;

        // Si detras hay un intermediario, el control de verdad puede cambiar mientras el
        // programa esta abierto —el operador pasa de «Ninguna» a «FT-710» en los ajustes— y
        // este panel tiene que enterarse: los mandos, las memorias y hasta si el equipo es
        // avanzado dejan de valer.
        if (_equipo is IControlEquipoConmutable conmutable)
        {
            conmutable.ControlCambiado += AlCambiarElControl;
        }

        // Los medidores se leen dos veces por segundo: mas a menudo no se aprecia y solo
        // carga el puerto serie, que es un recurso lento y compartido.
        _medicion = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500),
        };
        _medicion.Tick += async (_, _) => await MedirAsync().ConfigureAwait(true);

        ConstruirLosMandos();
        RehacerLaBotonera();
        Recoger(_equipo.Estado);
    }

    /// <summary>
    /// Control de equipo de verdad, saltandose el intermediario si lo hay.
    /// </summary>
    /// <remarks>
    /// Hay que preguntarle a este y no a <c>_equipo</c> si el equipo es avanzado o si tiene dos
    /// VFO: el intermediario delega esas cosas, pero no las <b>es</b>, y un <c>is</c> contra el
    /// diria que no siempre. Se lee cada vez, nunca se guarda: justo por eso existe.
    /// </remarks>
    private IControlEquipo Real => _equipo is IControlEquipoConmutable conmutable ? conmutable.Actual : _equipo;

    /// <summary>
    /// Mandos que este equipo declara, ya listos para pintarlos.
    /// </summary>
    /// <remarks>
    /// Sale de preguntarle al equipo, no de una lista escrita a mano. Con un FT-710 se llena
    /// entera; con un equipo que solo sepa de frecuencia y modo, se queda vacia y el panel no
    /// ensena la seccion. Anadir un equipo nuevo no toca ni una linea de la interfaz.
    /// </remarks>
    public ObservableCollection<VistaModeloMando> Mandos { get; } = [];

    /// <summary>Los mandos agrupados por familia, que es como se pintan.</summary>
    public ICollectionView MandosPorGrupo { get; private set; } = CollectionViewSource.GetDefaultView(Array.Empty<VistaModeloMando>());

    /// <summary>Memorias que tiene guardadas el equipo.</summary>
    public ObservableCollection<MemoriaDeEquipo> Memorias { get; } = [];

    /// <summary>VFO A.</summary>
    public VistaModeloVfo A { get; }

    /// <summary>VFO B.</summary>
    public VistaModeloVfo B { get; }

    /// <summary>
    /// El VFO que manda en el equipo (el que se ve en grande en el visor), como en la radio: con
    /// el B elegido, el B arriba y el A abajo.
    /// </summary>
    public VistaModeloVfo Principal => B.EsElActivo && !A.EsElActivo ? B : A;

    /// <summary>El otro VFO, el que se ve en pequeño.</summary>
    public VistaModeloVfo Secundario => ReferenceEquals(Principal, B) ? A : B;

    private VistaModeloVfo? _principalAvisado;

    /// <summary>Frecuencia de recepcion del ultimo aviso del dial (con split).</summary>
    private Dominio.Valores.Frecuencia? _rxAnterior;

    private void AvisarDelVfoActivo()
    {
        if (ReferenceEquals(_principalAvisado, Principal)) return;
        _principalAvisado = Principal;
        OnPropertyChanged(nameof(Principal));
        OnPropertyChanged(nameof(Secundario));
    }

    /// <summary>El equipo sabe informar de sus dos VFO a la vez.</summary>
    public bool TieneDosVfos => Real is IEquipoConDosVfos;

    /// <summary>
    /// Un mando concreto, para poder ponerlo donde toca en el frontal dibujado.
    /// </summary>
    /// <remarks>
    /// Devuelve el <b>mismo</b> objeto que esta en <see cref="Mandos"/>: el dibujo y la lista
    /// accionan lo mismo, no cada uno lo suyo. Girar el mando en el frontal mueve el
    /// deslizador de la lista, y al reves.
    /// </remarks>
    /// <param name="mando">Mando que se busca.</param>
    /// <returns>El mando, o nulo si este equipo no lo tiene.</returns>
    public VistaModeloMando? MandoDe(MandoDeEquipo mando) =>
        Mandos.FirstOrDefault(m => m.Mando == mando);

    /// <summary>Mando de volumen, el de abajo a la derecha del frontal.</summary>
    public VistaModeloMando? MandoDeVolumen => MandoDe(MandoDeEquipo.Volumen);

    /// <summary>Mando de ganancia de radiofrecuencia, el de arriba a la derecha.</summary>
    public VistaModeloMando? MandoDeGananciaRf => MandoDe(MandoDeEquipo.GananciaRf);

    /// <summary>Mando de potencia, que en el frontal cae bajo el de funcion.</summary>
    public VistaModeloMando? MandoDePotencia => MandoDe(MandoDeEquipo.Potencia);

    /// <summary>Ancho del filtro de recepcion, el mando de paso del frontal.</summary>
    public VistaModeloMando? MandoDeAnchoDeFiltro => MandoDe(MandoDeEquipo.AnchoDeFiltro);

    /// <summary>El equipo admite mandos, medidores y memorias.</summary>
    public bool EsAvanzado => Real is IEquipoAvanzado;

    /// <summary>Nombre comercial del equipo.</summary>
    public string NombreDelEquipo => Real is IEquipoAvanzado avanzado
        ? avanzado.NombreDelEquipo
        : "Equipo genérico";

    /// <summary>Modelo del catalogo que se maneja, o nulo (rigctld, OmniRig, sin equipo).</summary>
    public Radio.Modelos.ModeloDeEquipo? Modelo => (Real as Radio.Modelos.IEquipoDeModelo)?.Modelo;

    /// <summary>
    /// Clase del frontal dibujado que corresponde al equipo (<c>FrontalFt710</c>,
    /// <c>FrontalIc7300</c>...), o <c>FrontalGenerico</c> si el modelo no tiene dibujo propio.
    /// </summary>
    public string NombreDelFrontal => Modelo?.Frontal ?? "FrontalGenerico";

    /// <summary>El modelo esta programado segun su manual y no se ha probado con la radio.</summary>
    public bool SinProbarConRadio => Modelo is { ProbadoConRadio: false };

    /// <summary>El equipo tiene esa tecla (para deshabilitar la del dibujo si no).</summary>
    /// <param name="tecla">Tecla.</param>
    /// <returns>Verdadero si el control la admite.</returns>
    public bool TieneTecla(TeclaDelEquipo tecla) => Real is IEquipoConTeclas t && t.Teclas.Contains(tecla);

    /// <summary>Salta cada vez que el equipo mueve el dial o cambia de modo.</summary>
    public event EventHandler<DialDelEquipo>? DialCambiado;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstadoTexto))]
    [NotifyPropertyChangedFor(nameof(OrigenDeLaFrecuencia))]
    [NotifyPropertyChangedFor(nameof(TextoDelBotonDeConexion))]
    [NotifyCanExecuteChangedFor(nameof(ConectarCommand))]
    [NotifyCanExecuteChangedFor(nameof(DesconectarCommand))]
    [NotifyCanExecuteChangedFor(nameof(IntercambiarVfosCommand))]
    [NotifyCanExecuteChangedFor(nameof(IgualarVfosCommand))]
    [NotifyCanExecuteChangedFor(nameof(ActivarVfoCommand))]
    [NotifyCanExecuteChangedFor(nameof(AlternarMandoCommand))]
    [NotifyCanExecuteChangedFor(nameof(SiguienteModoCommand))]
    [NotifyCanExecuteChangedFor(nameof(SiguienteBandaCommand))]
    [NotifyCanExecuteChangedFor(nameof(IrALaMemoriaCommand))]
    [NotifyCanExecuteChangedFor(nameof(EnviarOrdenEnCrudoCommand))]
    [NotifyPropertyChangedFor(nameof(LockDisponible))]
    private bool _conectado;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstadoTexto))]
    private bool _transmitiendo;

    [ObservableProperty]
    private bool _conectando;

    [ObservableProperty]
    private string _frecuencia = "—";

    [ObservableProperty]
    private string _modo = "—";

    [ObservableProperty]
    private string _banda = "—";

    [ObservableProperty]
    private string _vfo = string.Empty;

    [ObservableProperty]
    private string _potencia = string.Empty;

    [ObservableProperty]
    private string _senal = string.Empty;

    [ObservableProperty]
    private string _ultimaLectura = string.Empty;

    [ObservableProperty]
    private string _aviso = string.Empty;

    /// <summary>
    /// Medidor de senal como fraccion de cero a uno, para pintarlo como barra.
    /// </summary>
    /// <remarks>
    /// La escala S llega hasta S9 y desde ahi sigue en decibelios sobre S9; el equipo informa
    /// hasta unos S9+60. Se reparte la barra: dos tercios para S0-S9 y el tercio de arriba
    /// para lo que pasa de S9, que es como esta graduado el medidor de la radio.
    /// </remarks>
    [ObservableProperty]
    private double _nivelDeSenal;

    /// <summary>Potencia de salida como fraccion de cero a uno sobre los 100 W del equipo.</summary>
    [ObservableProperty]
    private double _nivelDePotencia;

    [ObservableProperty]
    private string _medidorS = "—";

    [ObservableProperty]
    private string _medidorPotencia = "—";

    [ObservableProperty]
    private string _medidorRoe = "—";

    [ObservableProperty]
    private string _medidorAlc = "—";

    [ObservableProperty]
    private string _medidorCorriente = "—";

    [ObservableProperty]
    private string _medidorTension = "—";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(IrALaMemoriaCommand))]
    private MemoriaDeEquipo? _memoriaElegida;

    /// <summary>Atenuador de entrada, para el indicador naranja del visor.</summary>
    [ObservableProperty]
    private string _atenuador = "—";

    /// <summary>Preamplificador de entrada, para el indicador naranja del visor.</summary>
    [ObservableProperty]
    private string _preamplificador = "—";

    /// <summary>Filtro de muesca automatico, para el indicador naranja del visor.</summary>
    [ObservableProperty]
    private string _muescaAutomatica = "—";

    /// <summary>Constante del control automatico de ganancia, para el visor.</summary>
    [ObservableProperty]
    private string _agc = "—";

    /// <summary>Cuantos spots se estan viendo, para la franja inferior del visor.</summary>
    [ObservableProperty]
    private string _resumenDeSpots = "—";

    /// <summary>Cuantos anuncios de la banda caben en la pantalla del equipo.</summary>
    private const int EnLaBandaComoMucho = 9;

    /// <summary>Quien esta anunciado ahora mismo en la banda del VFO que recibe.</summary>
    public ObservableCollection<FilaDeSpot> EnLaBanda { get; } = [];

    /// <summary>Titulo de la lista de la banda, para la pantalla del equipo.</summary>
    [ObservableProperty]
    private string _tituloDeLaBanda = "EN LA BANDA";

    /// <summary>No hay nadie anunciado en la banda del VFO que recibe.</summary>
    [ObservableProperty]
    private bool _bandaVacia = true;

    /// <summary>
    /// Lo que el equipo pondria en la esquina del analizador de espectro.
    /// </summary>
    /// <remarks>
    /// De momento dice la via de control y el modo, que es lo que el operador necesita saber
    /// mientras el analizador sea nuestro y no del equipo.
    /// </remarks>
    public string EstadoDelEspectro => Conectado ? $"{ViaTexto}  ·  {Modo}" : "SIN CONEXIÓN";

    [ObservableProperty]
    private string _ordenEnCrudo = string.Empty;

    [ObservableProperty]
    private string _respuestaEnCrudo = string.Empty;

    /// <summary>
    /// La frecuencia del equipo no cae en ninguna banda de aficionado.
    /// </summary>
    /// <remarks>
    /// Pasa de verdad: el dia que se capturo el CAT del FT-710 del operador, el dial estaba en
    /// 27.555 MHz. El panel lo dice en vez de romperse, y el contacto se puede registrar
    /// igual con la banda vacia, que es lo que manda ADIF para ese caso.
    /// </remarks>
    [ObservableProperty]
    private bool _fueraDeBanda;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoDeSplit))]
    private bool _split;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoDeRit))]
    private bool _rit;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoDeRit))]
    private int _desplazamientoRit;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoDeXit))]
    private bool _xit;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoDeXit))]
    private int _desplazamientoXit;

    /// <summary>Estado del trabajo en dos frecuencias, escrito para el operador.</summary>
    public string TextoDeSplit => Split ? "SPLIT" : string.Empty;

    /// <summary>Desplazamiento de recepcion, escrito para el operador.</summary>
    public string TextoDeRit => Rit
        ? $"RIT {DesplazamientoRit.ToString("+0;-0;0", CultureInfo.InvariantCulture)} Hz"
        : string.Empty;

    /// <summary>Desplazamiento de transmision, escrito para el operador.</summary>
    public string TextoDeXit => Xit
        ? $"XIT {DesplazamientoXit.ToString("+0;-0;0", CultureInfo.InvariantCulture)} Hz"
        : string.Empty;

    /// <summary>Via por la que se habla con el equipo, para la pantalla.</summary>
    public string ViaTexto => _equipo.Via switch
    {
        ViaDeControl.CatNativo => "FT-710 (CAT nativo)",
        ViaDeControl.Rigctld => "Hamlib (rigctld)",
        ViaDeControl.OmniRig => "OmniRig",
        _ => "Sin control del equipo",
    };

    /// <summary>Como esta el equipo, en una linea.</summary>
    public string EstadoTexto => this switch
    {
        { Transmitiendo: true } => "EN ANTENA",
        { Conectado: true } => "Conectado",
        _ => "Sin conexión",
    };

    /// <summary>
    /// De donde salen la frecuencia y el modo del formulario de entrada.
    /// </summary>
    /// <remarks>
    /// Es la frase que evita el error mas caro del cuaderno: registrar veinte contactos en la
    /// frecuencia equivocada porque se creia que el programa la estaba siguiendo del dial y
    /// resulta que el equipo se habia desconectado.
    /// </remarks>
    public string OrigenDeLaFrecuencia => Conectado
        ? "La frecuencia y el modo los pone el equipo"
        : "La frecuencia y el modo los pone usted";

    /// <summary>Texto del boton que conecta o desconecta.</summary>
    public string TextoDelBotonDeConexion => Conectado ? "Desconectar" : "Conectar";

    /// <summary>
    /// Lo que hace falta tras conectar, se conecte como se conecte (botón Conectar o encender
    /// con LOCK): mandos, memorias y medidores.
    /// </summary>
    /// <remarks>
    /// El FT-710 no sabe que mandos tiene hasta que se conecta y los pregunta uno a uno. Los
    /// mandos se construian al montar el panel, con el equipo aun sin conectar, y la lista se
    /// quedaba VACIA (27-09-2026). Y encender con LOCK conectaba sin pasar por aqui: ATT, IPO,
    /// DNF, AGC y el S-meter salian en guiones (29-09-2026).
    /// </remarks>
    internal async Task PrepararTrasConectarAsync()
    {
        RehacerLosMandos();
        await RecogerLosMandosAsync().ConfigureAwait(true);
        RefrescarLosBotonesDeMando();
        await CargarLasMemoriasAsync().ConfigureAwait(true);
        _medicion.Start();
    }

    /// <summary>Conecta con el equipo.</summary>
    [RelayCommand(CanExecute = nameof(SePuedeConectar))]
    public async Task ConectarAsync()
    {
        try
        {
            Conectando = true;
            Aviso = string.Empty;
            await _equipo.ConectarAsync().ConfigureAwait(true);
            await PrepararTrasConectarAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido conectar con el equipo.");
            Aviso = $"No se ha podido conectar con el equipo: {ex.Message}";
        }
        finally
        {
            Conectando = false;
        }
    }

    /// <summary>Cierra la comunicacion con el equipo.</summary>
    [RelayCommand(CanExecute = nameof(SePuedeDesconectar))]
    public async Task DesconectarAsync()
    {
        try
        {
            _medicion.Stop();

            // Nunca se desconecta con el PTT pedido: primero se suelta.
            if (_vigilante.EnAntena) await _vigilante.SoltarYaAsync(MotivoDeSuelta.Cierre).ConfigureAwait(true);
            await _equipo.DesconectarAsync().ConfigureAwait(true);
            BorrarLosMedidores();
            Memorias.Clear();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Fallo al desconectar del equipo.");
            Aviso = $"Fallo al desconectar: {ex.Message}";
        }
    }

    /// <summary>
    /// Suelta el PTT ahora mismo. Es el boton de panico.
    /// </summary>
    /// <remarks>
    /// No tiene condicion para poder pulsarse: si el programa se ha hecho un lio con el estado,
    /// el boton tiene que funcionar igual. Vale mas soltar un PTT que ya estaba suelto que no
    /// poder soltar uno que esta pegado.
    /// </remarks>
    [RelayCommand]
    public async Task SoltarPttAsync()
    {
        try
        {
            await _vigilante.SoltarYaAsync(MotivoDeSuelta.Panico).ConfigureAwait(true);
            Aviso = "PTT soltado por el operador.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "El botón de pánico no ha podido soltar el PTT.");
            Aviso = $"¡Atención! No se ha podido soltar el PTT: {ex.Message}";
        }
    }

    /// <summary>Lleva el equipo a la memoria elegida.</summary>
    [RelayCommand(CanExecute = nameof(SePuedeIrALaMemoria))]
    public async Task IrALaMemoriaAsync()
    {
        if (Real is not IEquipoAvanzado avanzado || MemoriaElegida is not { Ocupada: true } memoria) return;

        try
        {
            await avanzado.IrAMemoriaAsync(memoria.Numero).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido ir a la memoria {Numero}.", memoria.Numero);
            Aviso = $"No se ha podido ir a la memoria {memoria.Numero}: {ex.Message}";
        }
    }

    /// <summary>
    /// Envia al equipo una orden CAT tal cual.
    /// </summary>
    /// <remarks>
    /// Es la valvula de escape para lo que el modelo de mandos no cubra y para mirar por que
    /// algo no responde. Va al final del panel y con su aviso, porque una orden mal escrita
    /// puede dejar el equipo en un estado raro.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(SePuedeEnviarOrden))]
    public async Task EnviarOrdenEnCrudoAsync()
    {
        if (Real is not IEquipoAvanzado avanzado) return;

        var orden = OrdenEnCrudo.Trim();
        if (orden.Length == 0) return;

        try
        {
            var respuesta = await avanzado.OrdenEnCrudoAsync(orden).ConfigureAwait(true);
            RespuestaEnCrudo = respuesta ?? "(el equipo no ha contestado)";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Fallo al enviar la orden CAT «{Orden}».", orden);
            RespuestaEnCrudo = $"Fallo: {ex.Message}";
        }
    }

    /// <summary>Deja de escuchar al equipo. Lo llama la ventana al cerrarse.</summary>
    public void Detener()
    {
        _medicion.Stop();
        _equipo.EstadoCambiado -= AlCambiarElEstado;
        _vigilante.PttSoltado -= AlSoltarElPtt;

        if (_equipo is IControlEquipoConmutable conmutable)
        {
            conmutable.ControlCambiado -= AlCambiarElControl;
        }
    }

    /// <summary>
    /// Pasa a los dos VFO lo que el equipo informa de ellos.
    /// </summary>
    /// <remarks>
    /// Un equipo que no sepa informar de los dos —el control generico por Hamlib, por
    /// ejemplo— deja el VFO B en blanco en vez de inventarse un valor. Eso tambien es
    /// informacion: dice que ese equipo no lo cuenta.
    /// </remarks>
    /// <summary>
    /// Pasa al visor los cuatro indicadores de recepcion que el equipo ensena en naranja.
    /// </summary>
    /// <remarks>
    /// Se leen de los mandos que ya estan construidos, no del equipo otra vez: es el mismo
    /// objeto que acciona el frontal y la lista, asi que se mantienen solos al dia.
    /// </remarks>
    private void RecogerLosIndicadores()
    {
        Atenuador = TextoDelIndicador(MandoDeEquipo.Atenuador);
        Preamplificador = TextoDelIndicador(MandoDeEquipo.Preamplificador);
        MuescaAutomatica = TextoDelIndicador(MandoDeEquipo.MuescaAutomatica);
        Agc = TextoDelIndicador(MandoDeEquipo.Agc);
    }

    /// <summary>
    /// Como se escribe un indicador del visor: corto, como en la pantalla de la radio («OFF»,
    /// «IPO», «AMP1», «AUTO», «SLOW»…), o un guion si el equipo no lo tiene. La frase entera va
    /// en la ayuda emergente.
    /// </summary>
    private string TextoDelIndicador(MandoDeEquipo mando)
    {
        if (MandoDe(mando) is not { Disponible: true } vista) return "—";
        if (vista.EsInterruptor) return vista.Encendido ? "ON" : "OFF";

        return TextoCorto(mando, (int)Math.Round(vista.Valor), vista.ValorTexto);
    }

    /// <summary>El valor de ATT, IPO o AGC como lo escribe la radio en su pantalla.</summary>
    /// <param name="mando">Mando.</param>
    /// <param name="posicion">Posicion del mando.</param>
    /// <param name="largo">Nombre largo de la posicion.</param>
    /// <returns>El texto corto.</returns>
    public static string TextoCorto(MandoDeEquipo mando, int posicion, string largo)
    {
        largo ??= string.Empty;
        return mando switch
        {
            MandoDeEquipo.Preamplificador => posicion switch { 0 => "IPO", 1 => "AMP1", 2 => "AMP2", _ => largo },
            MandoDeEquipo.Atenuador => posicion == 0 || largo.StartsWith("Apag", StringComparison.OrdinalIgnoreCase)
                ? "OFF"
                : largo.Replace(" ", string.Empty, StringComparison.Ordinal),
            MandoDeEquipo.Agc => largo switch
            {
                _ when largo.StartsWith("Auto", StringComparison.OrdinalIgnoreCase) => "AUTO",
                _ when largo.StartsWith("Rápid", StringComparison.OrdinalIgnoreCase) => "FAST",
                _ when largo.StartsWith("Medio", StringComparison.OrdinalIgnoreCase) => "MID",
                _ when largo.StartsWith("Lent", StringComparison.OrdinalIgnoreCase) => "SLOW",
                _ => "OFF",
            },
            _ => largo,
        };
    }

    private void RecogerLosVfos(EstadoDelEquipo estado)
    {
        if (Real is not IEquipoConDosVfos conDos)
        {
            // Un equipo que solo informa del VFO activo: ese se pinta con lo que se sabe, y el
            // otro se queda en blanco. Antes se quedaban LOS DOS con guiones.
            var enB = string.Equals(estado.Vfo, "VFO B", StringComparison.OrdinalIgnoreCase);
            var activo = new EstadoDeUnVfo(
                enB ? NombreDeVfo.B : NombreDeVfo.A,
                estado.Frecuencia,
                estado.Modo,
                EsElActivo: true,
                Transmite: true,
                Recibe: true,
                AnchoDeFiltroHz: null);
            A.Recoger(enB ? EstadoDeUnVfo.SinDatos(NombreDeVfo.A) : activo);
            B.Recoger(enB ? activo : EstadoDeUnVfo.SinDatos(NombreDeVfo.B));
            AvisarDelVfoActivo();
            return;
        }

        var vfos = conDos.Vfos;
        A.Recoger(vfos.A);
        B.Recoger(vfos.B);
        AvisarDelVfoActivo();

        Split = vfos.Split;
        Rit = vfos.Rit;
        DesplazamientoRit = vfos.DesplazamientoRitHz;
        Xit = vfos.Xit;
        DesplazamientoXit = vfos.DesplazamientoXitHz;
    }

    /// <summary>Lleva a los dos VFO los spots que se estan viendo en el cluster.</summary>
    /// <param name="spots">Spots que pasan el filtro del panel de cluster.</param>
    public void PonerSpots(IEnumerable<FilaDeSpot> spots)
    {
        ArgumentNullException.ThrowIfNull(spots);

        var lista = spots as IReadOnlyList<FilaDeSpot> ?? [.. spots];
        A.PonerAnuncios(lista);
        B.PonerAnuncios(lista);

        ResumenDeSpots = lista.Count.ToString("N0", CultureInfo.CurrentCulture);
        RecogerLaBanda(lista);
    }

    /// <summary>
    /// Deja en <see cref="EnLaBanda"/> quien esta anunciado en la banda del VFO que recibe.
    /// </summary>
    /// <remarks>
    /// Es lo que va donde el equipo pone el analizador de espectro. Un hueco negro no dice
    /// nada; una lista corta de quien esta ahora mismo en la banda, ordenada por frecuencia,
    /// es justo lo que se mira de un vistazo antes de girar el dial. En la fase cuatro ese
    /// sitio lo ocupara la cascada de verdad.
    /// </remarks>
    /// <param name="lista">Spots que pasan el filtro del panel de cluster.</param>
    private void RecogerLaBanda(IReadOnlyList<FilaDeSpot> lista)
    {
        var banda = Principal.Banda;

        var enLaBanda = lista
            .Where(f => string.Equals(f.Banda, banda, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.Spot.Frecuencia)
            .Take(EnLaBandaComoMucho)
            .ToList();

        // Si en la banda del dial no hay nadie, la pantalla no se queda en negro: ensena lo
        // ultimo que ha entrado por el cluster, de la banda que sea. Saber que en 6 m acaba de
        // salir una entidad nueva es justo el motivo por el que uno cambia de banda.
        var enSuBanda = enLaBanda.Count > 0;
        if (!enSuBanda)
        {
            enLaBanda = [.. lista.Take(EnLaBandaComoMucho)];
        }

        EnLaBanda.Clear();
        foreach (var fila in enLaBanda) EnLaBanda.Add(fila);

        TituloDeLaBanda = enSuBanda
            ? $"EN {banda}"
            : banda.Length == 0 || banda == "—"
                ? "EN EL AIRE"
                : $"EN EL AIRE · NADIE EN {banda}";

        BandaVacia = EnLaBanda.Count == 0;
    }

    /// <summary>Intercambia el contenido de los dos VFO.</summary>
    [RelayCommand(CanExecute = nameof(SePuedeTocarLosVfos))]
    public async Task IntercambiarVfosAsync()
    {
        if (Real is not IEquipoConDosVfos conDos) return;
        if (!Confirmar("Intercambiar el contenido de los VFO A y B en el equipo (A/B).")) return;

        try
        {
            await conDos.IntercambiarVfosAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se han podido intercambiar los VFO.");
            Aviso = $"No se han podido intercambiar los VFO: {ex.Message}";
        }
    }

    /// <summary>Copia el VFO activo sobre el otro.</summary>
    [RelayCommand(CanExecute = nameof(SePuedeTocarLosVfos))]
    public async Task IgualarVfosAsync()
    {
        if (Real is not IEquipoConDosVfos conDos) return;
        if (!Confirmar("Copiar el VFO activo sobre el otro (A=B). El otro VFO se pierde.")) return;

        try
        {
            await conDos.IgualarVfosAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se han podido igualar los VFO.");
            Aviso = $"No se han podido igualar los VFO: {ex.Message}";
        }
    }

    /// <summary>Hace activo el VFO indicado.</summary>
    /// <param name="vfo">VFO que pasa a ser el activo.</param>
    [RelayCommand(CanExecute = nameof(SePuedeTocarLosVfos))]
    public async Task ActivarVfoAsync(VistaModeloVfo? vfo)
    {
        if (vfo is null || Real is not IEquipoConDosVfos conDos) return;

        try
        {
            await conDos.PonerVfoActivoAsync(vfo.Nombre).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido cambiar de VFO.");
            Aviso = $"No se ha podido cambiar de VFO: {ex.Message}";
        }
    }

    /// <summary>
    /// Enciende o apaga un mando de los que son interruptor.
    /// </summary>
    /// <remarks>
    /// Es lo que hay detras de los botones del frontal dibujado —SPLIT, CLAR, NB, VOX—.
    /// Acciona el mismo objeto que la lista de mandos.
    /// </remarks>
    /// <param name="mando">Mando que se alterna.</param>
    [RelayCommand(CanExecute = nameof(SePuedeAlternar))]
    public async Task AlternarMandoAsync(MandoDeEquipo mando)
    {
        if (MandoDe(mando) is not { Disponible: true, SoloLectura: false } vista) return;

        // Un mando que pone el equipo en antena nunca se acciona a pelo: pasa por el
        // vigilante, que lo suelta pase lo que pase. Pero encender o apagar el acoplador
        // (AC001/AC000) NO emite: solo la ultima posicion, «Sintonizar», lo hace. Antes la
        // tecla TUNE subia el PTT para mandar AC001, es decir, emitia portadora para nada.
        if (vista.TransmiteAlAccionar && vista.TransmiteCon(vista.Encendido ? 0 : 1))
        {
            await AccionarTransmitiendoAsync(vista).ConfigureAwait(true);
            return;
        }

        vista.Encendido = !vista.Encendido;
    }

    /// <summary>Pasa el equipo al modo siguiente de los habituales.</summary>
    [RelayCommand(CanExecute = nameof(SePuedeAccionar))]
    public async Task SiguienteModoAsync()
    {
        string[] vuelta = ["LSB", "USB", "CW", "FT8", "RTTY", "AM", "FM"];

        var actual = Array.FindIndex(vuelta, m => string.Equals(m, Modo, StringComparison.OrdinalIgnoreCase));
        var siguiente = Dominio.Valores.Modo.Parse(vuelta[(actual + 1 + vuelta.Length) % vuelta.Length]);

        try
        {
            await _equipo.PonerModoAsync(siguiente).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido cambiar de modo.");
            Aviso = $"No se ha podido cambiar de modo: {ex.Message}";
        }
    }

    /// <summary>Pasa el equipo a la banda siguiente, al principio de su tramo.</summary>
    [RelayCommand(CanExecute = nameof(SePuedeAccionar))]
    public async Task SiguienteBandaAsync()
    {
        var bandas = Dominio.Valores.Banda.Todas;
        if (bandas.Count == 0) return;

        var actual = -1;
        for (var i = 0; i < bandas.Count; i++)
        {
            if (string.Equals(bandas[i].Nombre, Banda, StringComparison.OrdinalIgnoreCase)) actual = i;
        }

        var siguiente = bandas[(actual + 1 + bandas.Count) % bandas.Count];

        try
        {
            // Se entra por el principio del tramo, que es donde empieza la banda.
            await _equipo
                .PonerFrecuenciaAsync(Dominio.Valores.Frecuencia.DesdeMegahercios(siguiente.Limite.Inferior))
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido cambiar de banda.");
            Aviso = $"No se ha podido cambiar de banda: {ex.Message}";
        }
    }

    /// <summary>
    /// Acciona, dentro de una transmision vigilada, un mando que pone el equipo en antena.
    /// </summary>
    /// <remarks>
    /// El acoplador de antena es el caso tipico: sintonizar emite portadora. Se avisa al
    /// operador, se pide la antena al vigilante y el mando se acciona dentro de ella, para
    /// que algo suelte el PTT si la cosa se tuerce.
    /// </remarks>
    public async Task AccionarTransmitiendoAsync(VistaModeloMando vista)
    {
        ArgumentNullException.ThrowIfNull(vista);

        if (ConfirmarQueVaATransmitir is { } preguntar && !preguntar(vista.Nombre)) return;

        try
        {
            await using var antena = await _vigilante
                .PedirAntenaAsync($"Accionar «{vista.Nombre}»")
                .ConfigureAwait(true);

            vista.Encendido = !vista.Encendido;
            antena.Latir();

            Aviso = $"«{vista.Nombre}» accionado con el equipo en antena.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Fallo accionando el mando {Mando} en transmisión.", vista.Mando);
            Aviso = $"No se ha podido accionar «{vista.Nombre}»: {ex.Message}";
        }
    }

    /// <summary>
    /// Se pregunta antes de accionar un mando que pone el equipo en antena.
    /// </summary>
    /// <remarks>
    /// Lo rellena la ventana. Si nadie lo rellena, no se pregunta: el aviso es cosa de la
    /// vista, no del modelo, y un modelo que se quede esperando un dialogo que nadie atiende
    /// deja el programa colgado.
    /// </remarks>
    public Func<string, bool>? ConfirmarQueVaATransmitir { get; set; }

    private bool SePuedeAccionar() => Conectado;

    /// <summary>
    /// Una tecla del frontal se puede pulsar si el equipo esta conectado Y tiene ese mando.
    /// </summary>
    /// <remarks>
    /// Antes bastaba con estar conectado: CLAR, por ejemplo, salia encendida con un FT-710 que
    /// no admite RT por CAT, y pulsarla no hacia nada. Un boton que no hace nada tiene que
    /// verse apagado.
    /// </remarks>
    private bool SePuedeAlternar(MandoDeEquipo mando) =>
        Conectado && MandoDe(mando) is { Disponible: true, SoloLectura: false };

    /// <summary>
    /// Pregunta al operador antes de una accion que cambia el equipo sin vuelta atras facil.
    /// </summary>
    /// <remarks>
    /// Si nadie ha puesto quien pregunte, <b>no se hace</b>: intercambiar o pisar un VFO sin
    /// que el operador lo haya confirmado ya nos paso una vez.
    /// </remarks>
    private bool Confirmar(string que)
    {
        if (ConfirmarAccion is { } preguntar && preguntar(que)) return true;

        Aviso = ConfirmarAccion is null ? $"No se ha hecho: {que} necesita confirmación." : string.Empty;
        return false;
    }

    /// <summary>Lo rellena la ventana: pregunta al operador antes de SV, AB o BA.</summary>
    public Func<string, bool>? ConfirmarAccion { get; set; }

    private bool SePuedeTocarLosVfos() => Conectado && TieneDosVfos;

    private bool SePuedeIrALaMemoria() => Conectado && MemoriaElegida is { Ocupada: true };

    private bool SePuedeEnviarOrden() => EsAvanzado && Conectado;

    /// <summary>
    /// Construye un control por cada mando que el equipo declara.
    /// </summary>
    /// <remarks>
    /// Se hace una sola vez y al montar el panel: la lista de mandos de un equipo no cambia
    /// mientras esta enchufado.
    /// </remarks>
    private void ConstruirLosMandos()
    {
        Mandos.Clear();

        if (Real is not IEquipoAvanzado avanzado) return;

        foreach (var mando in avanzado.Mandos)
        {
            if (avanzado.Rango(mando) is not { } rango) continue;
            Mandos.Add(new VistaModeloMando(avanzado, rango));
        }

        // La vista por omision de la coleccion es SIEMPRE la misma: si se anade la agrupacion
        // cada vez que se rehacen los mandos, la lista sale agrupada dos y tres veces.
        var vista = CollectionViewSource.GetDefaultView(Mandos);
        vista.GroupDescriptions.Clear();
        vista.GroupDescriptions.Add(new PropertyGroupDescription(nameof(VistaModeloMando.Grupo)));
        MandosPorGrupo = vista;
        EscucharLosMandosDelFrontal();
    }

    /// <summary>Vuelve a construir los mandos y avisa a todo lo que los pinta.</summary>
    private void RehacerLosMandos()
    {
        ConstruirLosMandos();

        OnPropertyChanged(nameof(MandosPorGrupo));
        OnPropertyChanged(nameof(EsAvanzado));
        OnPropertyChanged(nameof(PuedeEncenderOApagar));
        OnPropertyChanged(nameof(LockDisponible));
        OnPropertyChanged(nameof(TieneDosVfos));
        OnPropertyChanged(nameof(NombreDelEquipo));
        OnPropertyChanged(nameof(Modelo));
        OnPropertyChanged(nameof(NombreDelFrontal));
        OnPropertyChanged(nameof(SinProbarConRadio));
        OnPropertyChanged(nameof(MandoDeVolumen));
        OnPropertyChanged(nameof(MandoDeGananciaRf));
        OnPropertyChanged(nameof(MandoDePotencia));
        OnPropertyChanged(nameof(MandoDeAnchoDeFiltro));
    }

    /// <summary>
    /// Las teclas del frontal vuelven a preguntarse si pueden pulsarse, y el visor recoge los
    /// indicadores con los valores recien leidos.
    /// </summary>
    private void RefrescarLosBotonesDeMando()
    {
        AlternarMandoCommand.NotifyCanExecuteChanged();
        IntercambiarVfosCommand.NotifyCanExecuteChanged();
        IgualarVfosCommand.NotifyCanExecuteChanged();
        ActivarVfoCommand.NotifyCanExecuteChanged();
        EnviarOrdenEnCrudoCommand.NotifyCanExecuteChanged();
        RecogerLosIndicadores();
        RecogerLosVfos(_equipo.Estado);
        AvisarDelFrontal();
    }

    private async Task RecogerLosMandosAsync()
    {
        foreach (var mando in Mandos)
        {
            await mando.RecogerAsync().ConfigureAwait(true);
        }
    }

    private async Task CargarLasMemoriasAsync()
    {
        if (Real is not IEquipoAvanzado avanzado) return;

        try
        {
            Memorias.Clear();
            foreach (var memoria in await avanzado.LeerMemoriasAsync().ConfigureAwait(true))
            {
                Memorias.Add(memoria);
            }

            // Se elige la primera memoria con contenido: una lista desplegable en blanco
            // parece un fallo, y el operador no sabe si es que no hay memorias o que no se
            // han leido.
            MemoriaElegida ??= Memorias.FirstOrDefault(m => m.Ocupada);
            RecogerLaBotonera();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "No se han podido leer las memorias del equipo.");
        }
    }

    /// <summary>
    /// Lee los medidores y los escribe.
    /// </summary>
    /// <remarks>
    /// Lo que el equipo no informa se queda con un guion, nunca con un cero: un cero se lee
    /// como una medida, y «no lo se» no es lo mismo que «vale cero».
    /// </remarks>
    private async Task MedirAsync()
    {
        if (Real is not IEquipoAvanzado avanzado || !Conectado) return;

        LatirElMox();

        try
        {
            // Lo que se toca en la propia radio tiene que verse en el frontal dibujado.
            await RefrescarElFrontalAsync().ConfigureAwait(true);
            var lectura = await avanzado.LeerMedidoresAsync().ConfigureAwait(true);

            MedidorS = lectura.UnidadesS is { } s ? $"S{s.ToString("N1", CultureInfo.CurrentCulture)}" : "—";
            MedidorPotencia = lectura.PotenciaVatios is { } w ? $"{w.ToString("N0", CultureInfo.CurrentCulture)} W" : "—";

            // Dos tercios de barra para S0-S9 y el tercio restante para lo que pasa de S9,
            // que es como esta graduada la escala del equipo.
            NivelDeSenal = lectura.UnidadesS is { } unidades
                ? Math.Clamp(unidades <= 9 ? unidades / 9.0 * 0.66 : 0.66 + ((unidades - 9) / 6.0 * 0.34), 0, 1)
                : 0;

            NivelDePotencia = lectura.PotenciaVatios is { } vatios
                ? Math.Clamp(vatios / 100.0, 0, 1)
                : 0;
            MedidorRoe = lectura.Roe is { } roe ? roe.ToString("N2", CultureInfo.CurrentCulture) : "—";
            MedidorAlc = lectura.Alc is { } alc ? alc.ToString("N2", CultureInfo.CurrentCulture) : "—";
            MedidorCorriente = lectura.CorrienteAmperios is { } a ? $"{a.ToString("N1", CultureInfo.CurrentCulture)} A" : "—";
            MedidorTension = lectura.TensionVoltios is { } v ? $"{v.ToString("N1", CultureInfo.CurrentCulture)} V" : "—";
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "No se han podido leer los medidores.");
            BorrarLosMedidores();
        }
    }

    private void BorrarLosMedidores()
    {
        MedidorS = "—";
        MedidorPotencia = "—";
        MedidorRoe = "—";
        MedidorAlc = "—";
        MedidorCorriente = "—";
        MedidorTension = "—";
        NivelDeSenal = 0;
        NivelDePotencia = 0;
    }

    private bool SePuedeConectar() => !Conectado;

    partial void OnConectadoChanged(bool value)
    {
        AvisarDelFrontal();
        AvisarDeLaBotonera();
    }

    private bool SePuedeDesconectar() => Conectado;

    private void AlCambiarElEstado(object? origen, EstadoDelEquipo estado) =>
        Hilo.EnLaVentana(() => Recoger(estado));

    /// <summary>
    /// Rehace el panel cuando el operador cambia la via de control en los ajustes.
    /// </summary>
    /// <remarks>
    /// No basta con volver a leer el estado: con el equipo nuevo cambian los mandos que hay,
    /// si es avanzado, si tiene dos VFO y hasta como se llama. Todo eso estaba calculado una
    /// sola vez al montar el panel, y aqui se vuelve a calcular y se avisa a la interfaz.
    /// </remarks>
    private void AlCambiarElControl(object? origen, IControlEquipo nuevo) => Hilo.EnLaVentana(() =>
    {
        _medicion.Stop();
        Memorias.Clear();
        MemoriaElegida = null;
        BorrarLosMedidores();

        RehacerLosMandos();
        RehacerLaBotonera();
        OnPropertyChanged(nameof(ViaTexto));
        OnPropertyChanged(nameof(EstadoDelEspectro));
        AlternarMandoCommand.NotifyCanExecuteChanged();

        // Los botones que dependen de lo que sepa hacer el equipo —los VFO, la orden en
        // crudo— tienen que volver a preguntarse si pueden pulsarse.
        IntercambiarVfosCommand.NotifyCanExecuteChanged();
        IgualarVfosCommand.NotifyCanExecuteChanged();
        ActivarVfoCommand.NotifyCanExecuteChanged();
        EnviarOrdenEnCrudoCommand.NotifyCanExecuteChanged();
        IrALaMemoriaCommand.NotifyCanExecuteChanged();

        Aviso = $"Cambiada la vía de control del equipo: {ViaTexto}.";
        Recoger(nuevo.Estado);
    });

    private void AlSoltarElPtt(object? origen, MotivoDeSuelta motivo) => Hilo.EnLaVentana(() =>
    {
        Transmitiendo = false;

        // Si el vigilante ha soltado un MOX (tope, panico), el MOX ya no esta puesto.
        if (_mox is not null)
        {
            _mox = null;
            OnPropertyChanged(nameof(EnMox));
        }

        // Una suelta normal no se cuenta; las demas si, porque explican por que se corto.
        Aviso = motivo switch
        {
            MotivoDeSuelta.TiempoAgotado => "Se soltó el PTT: se agotó el tiempo máximo de transmisión.",
            MotivoDeSuelta.SinLatido => "Se soltó el PTT: quien transmitía dejó de dar señales de vida.",
            MotivoDeSuelta.Excepcion => "Se soltó el PTT por un fallo durante la transmisión.",
            MotivoDeSuelta.Panico => "PTT soltado por el operador.",
            _ => Aviso,
        };
    });

    /// <summary>Pasa el estado del equipo a lo que se lee en pantalla.</summary>
    private void Recoger(EstadoDelEquipo estado)
    {
        var frecuenciaAnterior = Frecuencia;
        var modoAnterior = Modo;
        var estabaConectado = Conectado;

        Conectado = estado.Conectado;

        // Se conecte como se conecte —boton Conectar, encender con LOCK, o la radio que vuelve
        // sola tras encenderla con su tecla—, al pasar de caida a viva se rehacen mandos,
        // memorias y medidores. Si no, los botones del frontal se quedaban sin mando detras
        // (deshabilitados) y ATT/IPO/DNF/AGC y el S-meter en guiones (29-09-2026). Si ya lo hizo
        // el boton Conectar, no se repite.
        if (estado.Conectado && !estabaConectado && !Conectando)
        {
            _ = PrepararTrasConectarAsync();
        }
        Transmitiendo = estado.Transmitiendo;

        if (!estado.Conectado)
        {
            Frecuencia = "—";
            Modo = "—";
            Banda = "—";
            Vfo = string.Empty;
            Potencia = string.Empty;
            Senal = string.Empty;
            UltimaLectura = string.Empty;
            A.Recoger(EstadoDeUnVfo.SinDatos(NombreDeVfo.A));
            B.Recoger(EstadoDeUnVfo.SinDatos(NombreDeVfo.B));
            Split = false;
            Rit = false;
            Xit = false;
            RecogerLaBotonera();
            return;
        }

        // La frecuencia va SIEMPRE con punto decimal, como el dial del equipo y como ADIF.
        Frecuencia = TextoDeFrecuencia.Escribir(estado.Frecuencia);
        Modo = estado.Modo.EsVacio ? "—" : estado.Modo.NombreUsual;

        // No toda frecuencia tiene banda: 27.555 MHz no esta en la tabla de ADIF, y ahi
        // estaba el dial del FT-710 el dia de la captura. Se dice y se sigue operando.
        FueraDeBanda = estado.Banda.EsVacia;
        Banda = FueraDeBanda ? "fuera de banda" : estado.Banda.Nombre;
        Vfo = estado.Vfo ?? string.Empty;

        Potencia = estado.PotenciaVatios is { } vatios
            ? $"{vatios.ToString("N0", CultureInfo.CurrentCulture)} W"
            : string.Empty;

        Senal = estado.SenalRecibida is { } unidades
            ? $"S{unidades.ToString("N1", CultureInfo.CurrentCulture)}"
            : string.Empty;

        UltimaLectura = estado.LeidoUtc.UtcDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

        RecogerLosVfos(estado);
        RecogerLosIndicadores();
        RecogerLaBotonera();
        OnPropertyChanged(nameof(EstadoDelEspectro));

        if (!string.Equals(Frecuencia, frecuenciaAnterior, StringComparison.Ordinal)
            || !string.Equals(Modo, modoAnterior, StringComparison.Ordinal)
            || estado.FrecuenciaRx != _rxAnterior)
        {
            _rxAnterior = estado.FrecuenciaRx;
            DialCambiado?.Invoke(this, new DialDelEquipo(estado.Frecuencia, estado.Modo, estado.FrecuenciaRx));
        }
    }
}
