using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Ui.Conversores;
using Serilog;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>
/// El reloj del modem, a la vista y con un boton para arreglarlo.
/// </summary>
/// <remarks>
/// <para>
/// Esto no es un adorno de la pestana Digital: es lo primero que hay que mirar cuando el modem
/// no saca nada. El reloj de esta maquina se fue de −870 ms a −1,29 s <b>en tres dias</b>, y
/// con ese desvio FT8 deja de decodificar y, peor, se transmite fuera de ventana, molestando a
/// los demas sin enterarse.
/// </para>
/// <para>
/// <b>La regla de cuantos milisegundos son demasiados no esta aqui.</b> Vive en el reloj, que
/// devuelve el estado ya interpretado —veredicto, consejo y escalon— y esta pantalla se limita
/// a pintarlo. Si algun dia se cambia el umbral, no hay que tocar ni una linea de la interfaz.
/// </para>
/// <para>
/// <b>Nunca se pone el reloj en hora solo.</b> Se mide, se dice como esta, y corregir es una
/// pulsacion del operador: es el reloj de su maquina, no el nuestro.
/// </para>
/// </remarks>
public sealed partial class VistaModeloRelojDigital : ObservableObject
{
    /// <summary>
    /// Cada cuanto se le pregunta al reloj como esta.
    /// </summary>
    /// <remarks>
    /// Un minuto no es un minuto de red: el reloj guarda su ultima medida y solo sale a
    /// preguntar cuando caduca. Lo que se consigue con este ritmo es que la pantalla no se
    /// quede con un numero de hace media hora.
    /// </remarks>
    public static readonly TimeSpan RitmoDeVigilancia = TimeSpan.FromMinutes(1);

    private readonly IRelojDelModem _reloj;
    private readonly ISincronizadorDeHora? _sincronizador;
    private DispatcherTimer? _vigilancia;

    /// <summary>Monta la tira del reloj.</summary>
    /// <param name="reloj">El reloj corregido del modem.</param>
    /// <param name="sincronizador">
    /// Quien sabe poner el reloj en hora. Puede faltar —en pruebas, o si el modulo no esta
    /// registrado—: entonces se enseña el desvio pero no se ofrece corregirlo.
    /// </param>
    /// <summary>La hora corregida del reloj del modem: la que manda sobre las ventanas.</summary>
    public DateTimeOffset Ahora => _reloj.Ahora;

    public VistaModeloRelojDigital(IRelojDelModem reloj, ISincronizadorDeHora? sincronizador = null)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        _reloj = reloj;
        _sincronizador = sincronizador;
        _estado = reloj.Estado;

        _reloj.DesvioMedido += AlMedirElDesvio;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DesvioTexto))]
    [NotifyPropertyChangedFor(nameof(Veredicto))]
    [NotifyPropertyChangedFor(nameof(Consejo))]
    [NotifyPropertyChangedFor(nameof(HayConsejo))]
    [NotifyPropertyChangedFor(nameof(RelojBien))]
    [NotifyPropertyChangedFor(nameof(RelojRegular))]
    [NotifyPropertyChangedFor(nameof(RelojFueraDeVentana))]
    [NotifyPropertyChangedFor(nameof(RelojSinMedir))]
    [NotifyPropertyChangedFor(nameof(HayQueHacerAlgo))]
    private EstadoDelReloj _estado;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MedirCommand))]
    [NotifyCanExecuteChangedFor(nameof(PonerEnHoraCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfigurarElServicioCommand))]
    private bool _ocupado;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayParte))]
    private string _parte = string.Empty;

    /// <summary>Lo que hay que teclear para hacerlo a mano, si no se puede desde aqui.</summary>
    [ObservableProperty]
    private string _instruccionesParaHacerloAMano = string.Empty;

    /// <summary>El desvio escrito para leer: «−1,29 s», «+350 ms».</summary>
    public string DesvioTexto => Estado.DesvioParaMostrar;

    /// <summary>Como esta el reloj, en una frase.</summary>
    public string Veredicto => Estado.Veredicto;

    /// <summary>Que hacer al respecto, o vacio si no hay nada que hacer.</summary>
    public string Consejo => Estado.Consejo;

    /// <summary>Hay algo que aconsejar.</summary>
    public bool HayConsejo => !string.IsNullOrEmpty(Estado.Consejo);

    /// <summary>Hay algo que el operador deberia hacer.</summary>
    public bool HayQueHacerAlgo => Estado.HayQueHacerAlgo;

    /// <summary>Semaforo en verde: el desvio no estorba.</summary>
    public bool RelojBien => Estado.Calidad == CalidadDelReloj.Bien;

    /// <summary>Semaforo en ambar: se decodifica peor de lo que se podria.</summary>
    public bool RelojRegular => Estado.Calidad == CalidadDelReloj.Regular;

    /// <summary>Semaforo en rojo: ademas, se transmite fuera de ventana.</summary>
    public bool RelojFueraDeVentana => Estado.Calidad == CalidadDelReloj.FueraDeVentana;

    /// <summary>Todavia no se ha podido medir.</summary>
    public bool RelojSinMedir => Estado.Calidad == CalidadDelReloj.SinMedir;

    /// <summary>Se puede escribir la hora del sistema desde aqui, con los permisos que hay.</summary>
    public bool SePuedePonerEnHora => _sincronizador?.SePuedePonerEnHora ?? false;

    /// <summary>Hay quien sepa poner el reloj en hora.</summary>
    public bool HaySincronizador => _sincronizador is not null;

    /// <summary>Hay un parte del ultimo intento.</summary>
    public bool HayParte => !string.IsNullOrEmpty(Parte);

    /// <summary>
    /// Empieza a mirar el reloj, y lo mide una primera vez.
    /// </summary>
    /// <remarks>
    /// <b>No se llama al montar la pantalla.</b> Se llama cuando el operador entra en la
    /// pestana Digital: abrir el programa no tiene por que ponerse a hablar con servidores de
    /// hora de nadie. A partir de ahi se queda vigilando, porque un reloj se va durante una
    /// tarde de concurso y medir solo al principio no sirve de nada.
    /// </remarks>
    public void Vigilar()
    {
        if (_vigilancia is not null) return;

        _vigilancia = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = RitmoDeVigilancia,
        };
        _vigilancia.Tick += async (_, _) => await MedirElDesvioAsync(forzar: false).ConfigureAwait(true);
        _vigilancia.Start();

        _ = MedirCommand.ExecuteAsync(null);
    }

    /// <summary>Deja de mirar el reloj. Lo llama la ventana al cerrarse.</summary>
    public void Detener()
    {
        _vigilancia?.Stop();
        _vigilancia = null;
        _reloj.DesvioMedido -= AlMedirElDesvio;
    }

    /// <summary>Mide el desvio contra los servidores de hora.</summary>
    /// <remarks>
    /// Medir no necesita sincronizador ni permisos: se puede saber que el reloj esta mal
    /// aunque no se pueda arreglar desde aqui. Y saberlo ya vale de algo.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(SePuedeMedir))]
    public Task MedirAsync() => MedirElDesvioAsync(forzar: true);

    /// <summary>
    /// Pone el reloj del ordenador en hora.
    /// </summary>
    /// <remarks>
    /// Se mide forzando antes de corregir, y de eso se encarga el sincronizador: corregir con
    /// una medida de hace media hora mete un error nuevo en vez de quitarlo.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(SePuedePonerloEnHora))]
    public async Task PonerEnHoraAsync()
    {
        if (_sincronizador is null) return;

        Ocupado = true;
        try
        {
            var resultado = await _sincronizador.PonerElRelojEnHoraAsync().ConfigureAwait(true);
            Recoger(resultado);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido poner el reloj en hora.");
            Parte = $"No se ha podido poner el reloj en hora: {ex.Message}";
        }
        finally
        {
            Ocupado = false;
            Estado = _reloj.Estado;
        }
    }

    /// <summary>
    /// Deja el servicio de hora de Windows apuntando a un buen servidor.
    /// </summary>
    /// <remarks>
    /// Es la alternativa cuando no hay permisos para escribir la hora, y es mejor que la otra:
    /// arregla el problema para siempre y no solo hoy.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(SePuedeTrabajar))]
    public async Task ConfigurarElServicioAsync()
    {
        if (_sincronizador is null) return;

        Ocupado = true;
        try
        {
            var resultado = await _sincronizador.ConfigurarServicioDeHoraAsync().ConfigureAwait(true);
            Recoger(resultado);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido configurar el servicio de hora de Windows.");
            Parte = $"No se ha podido configurar el servicio de hora: {ex.Message}";
        }
        finally
        {
            Ocupado = false;
        }
    }

    private bool SePuedeMedir() => !Ocupado;

    private bool SePuedeTrabajar() => !Ocupado && HaySincronizador;

    private bool SePuedePonerloEnHora() => !Ocupado && SePuedePonerEnHora;

    private async Task MedirElDesvioAsync(bool forzar)
    {
        Ocupado = true;
        try
        {
            await _reloj.MedirAsync(forzar).ConfigureAwait(true);
            Estado = _reloj.Estado;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "No se ha podido medir el desvío del reloj.");
            Parte = $"No se ha podido medir el reloj: {ex.Message}";
        }
        finally
        {
            Ocupado = false;
        }
    }

    private void Recoger(ResultadoDePuestaEnHora resultado)
    {
        Estado = _reloj.Estado;
        // Las ordenes para hacerlo a mano solo hacen falta si no se pudo: antes se quedaban
        // puestas (cinco lineas de w32tm) con el reloj ya a 3 ms.
        InstruccionesParaHacerloAMano = resultado.Hecho
            ? string.Empty
            : resultado.Detalle
              ?? _sincronizador?.InstruccionesParaHacerloAMano
              ?? string.Empty;

        var antesYDespues = resultado is { DesvioAntesMs: { } antes, DesvioDespuesMs: { } despues }
            ? string.Create(
                CultureInfo.CurrentCulture,
                $" Antes: {antes:+0;-0} ms. Después: {despues:+0;-0} ms.")
            : string.Empty;

        Parte = resultado.Mensaje + antesYDespues;
    }

    partial void OnEstadoChanged(EstadoDelReloj value)
    {
        // Con el reloj en hora sobran las ordenes para arreglarlo a mano.
        if (value.Calidad == CalidadDelReloj.Bien) InstruccionesParaHacerloAMano = string.Empty;
    }

    private void AlMedirElDesvio(object? origen, EstadoDelReloj estado) =>
        Hilo.EnLaVentana(() => Estado = estado);
}
