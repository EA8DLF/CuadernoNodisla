using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Ui.Conversores;
using Serilog;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>Una instancia de WSJT-X, JTDX o similar que esta dando senales de vida.</summary>
public sealed partial class FilaDeInstancia : ObservableObject
{
    /// <summary>Monta la fila a partir del estado recibido.</summary>
    /// <param name="estado">Estado que mando el programa.</param>
    public FilaDeInstancia(EstadoDigital estado) => Recoger(estado);

    /// <summary>Nombre con el que se identifica la instancia.</summary>
    public string Identificador { get; private set; } = string.Empty;

    /// <summary>Programa que hay al otro lado.</summary>
    public DialectoDigital Dialecto { get; private set; }

    /// <summary>Nombre del programa para la pantalla.</summary>
    public string DialectoTexto => Dialecto switch
    {
        DialectoDigital.WsjtX => "WSJT-X",
        DialectoDigital.Jtdx => "JTDX",
        DialectoDigital.Mshv => "MSHV",
        DialectoDigital.Js8Call => "JS8Call",
        _ => "Desconocido",
    };

    [ObservableProperty]
    private string _frecuencia = string.Empty;

    [ObservableProperty]
    private string _modo = string.Empty;

    [ObservableProperty]
    private bool _transmitiendo;

    [ObservableProperty]
    private bool _decodificando;

    [ObservableProperty]
    private string _llamado = string.Empty;

    [ObservableProperty]
    private string _ultimaSenal = string.Empty;

    /// <summary>Actualiza la fila con un estado nuevo.</summary>
    /// <param name="estado">Estado que mando el programa.</param>
    public void Recoger(EstadoDigital estado)
    {
        ArgumentNullException.ThrowIfNull(estado);

        Identificador = estado.Identificador;
        Dialecto = estado.Dialecto;
        Frecuencia = TextoDeFrecuencia.Escribir(estado.Frecuencia);
        Modo = estado.Modo.EsVacio ? string.Empty : estado.Modo.NombreUsual;
        Transmitiendo = estado.Transmitiendo;
        Decodificando = estado.Decodificando;
        Llamado = estado.Llamado.EsVacio ? string.Empty : estado.Llamado.Valor;
        UltimaSenal = estado.RecibidoUtc.UtcDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

        OnPropertyChanged(nameof(DialectoTexto));
    }
}

/// <summary>Una decodificacion tal y como se lee en la lista.</summary>
public sealed class FilaDeDecodificacion
{
    /// <summary>Monta la fila a partir de la decodificacion recibida.</summary>
    /// <param name="decodificacion">Lo que llego del programa.</param>
    /// <param name="esNuevo">El indicativo seria nuevo en el cuaderno.</param>
    public FilaDeDecodificacion(DecodificacionDigital decodificacion, bool esNuevo)
    {
        ArgumentNullException.ThrowIfNull(decodificacion);

        Decodificacion = decodificacion;
        Identificador = decodificacion.Identificador;
        EsNuevo = esNuevo;

        Modo = decodificacion.Modo.EsVacio ? string.Empty : decodificacion.Modo.NombreUsual;
        Hora = decodificacion.InstanteUtc.UtcDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        Decibelios = decodificacion.Decibelios.ToString("+00;-00;+00", CultureInfo.InvariantCulture);
        Desfase = decodificacion.DesfaseSegundos.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture);
        Tono = decodificacion.TonoHz.ToString("N0", CultureInfo.CurrentCulture);
        Texto = decodificacion.Texto;
        Indicativo = decodificacion.Llamante.EsVacio ? string.Empty : decodificacion.Llamante.Valor;
        Localizador = decodificacion.Locator.EsVacio ? string.Empty : decodificacion.Locator.Valor;
    }

    /// <summary>Lo que llego del programa.</summary>
    public DecodificacionDigital Decodificacion { get; }

    /// <summary>Instancia que la mando.</summary>
    public string Identificador { get; }

    /// <summary>Modo en el que se decodifico.</summary>
    public string Modo { get; } = string.Empty;

    /// <summary>El indicativo sería nuevo en el cuaderno.</summary>
    public bool EsNuevo { get; }

    /// <summary>Hora UTC del periodo.</summary>
    public string Hora { get; }

    /// <summary>Relacion senal-ruido en decibelios, siempre con signo.</summary>
    public string Decibelios { get; }

    /// <summary>Desfase temporal de la senal, en segundos.</summary>
    public string Desfase { get; }

    /// <summary>Tono dentro del ancho de banda de audio, en hercios.</summary>
    public string Tono { get; }

    /// <summary>El mensaje decodificado, tal y como viene.</summary>
    public string Texto { get; }

    /// <summary>Indicativo de quien llama.</summary>
    public string Indicativo { get; }

    /// <summary>Localizador que viaja en el mensaje.</summary>
    public string Localizador { get; }

    /// <summary>El mensaje es una llamada general.</summary>
    public bool EsCq => Decodificacion.EsCq;

    /// <summary>Le están llamando a usted.</summary>
    public bool MeLlaman => !Decodificacion.Llamado.EsVacio;
}

/// <summary>
/// Panel de modos digitales: que programas estan conectados, que se esta decodificando y los
/// contactos que llegan ya cerrados para guardarlos en el cuaderno.
/// </summary>
/// <remarks>
/// Lo que decide la forma de este panel es una frase del puerto: los metodos devuelven
/// <c>bool</c> porque no todos los programas admiten todo. JTDX, por ejemplo, no tiene el
/// mensaje de resaltado. La regla aqui es que un boton que no se puede usar sale desactivado,
/// nunca falla al pulsarlo: el operador tiene que ver de un vistazo lo que puede hacer con la
/// instancia que tiene elegida.
/// </remarks>
public sealed partial class VistaModeloDigital : ObservableObject
{
    /// <summary>Decodificaciones que se guardan; las mas viejas se van cayendo.</summary>
    public const int DecodificacionesQueSeGuardan = 400;

    /// <summary>Contactos cerrados que se guardan a la espera de meterlos en el cuaderno.</summary>
    public const int ContactosEnEspera = 100;

    private readonly IPuenteDigital _puente;
    private readonly ConsultarTrabajadoAntes _trabajadoAntes;
    private readonly RegistrarQso _registrar;

    /// <summary>Monta el panel sobre el puente con los programas de modos digitales.</summary>
    /// <param name="puente">Puente por el que llegan las decodificaciones.</param>
    /// <param name="trabajadoAntes">Consulta que dice si un indicativo ya esta en el cuaderno.</param>
    /// <param name="registrar">Caso de uso que guarda un contacto.</param>
    public VistaModeloDigital(
        IPuenteDigital puente,
        ConsultarTrabajadoAntes trabajadoAntes,
        RegistrarQso registrar)
    {
        ArgumentNullException.ThrowIfNull(puente);
        ArgumentNullException.ThrowIfNull(trabajadoAntes);
        ArgumentNullException.ThrowIfNull(registrar);

        _puente = puente;
        _trabajadoAntes = trabajadoAntes;
        _registrar = registrar;

        _puente.EstadoRecibido += AlLlegarEstado;
        _puente.Decodificado += AlDecodificar;
        _puente.QsoRegistrado += AlCerrarUnContacto;
        _puente.InstanciaPerdida += AlPerderInstancia;
        _puente.AdifRecibido += AlLlegarAdif;
    }

    /// <summary>Salta cuando se guarda en el cuaderno un contacto llegado de los digitales.</summary>
    public event EventHandler? CuadernoCambiado;

    /// <summary>Instancias que estan dando senales de vida.</summary>
    public ObservableCollection<FilaDeInstancia> Instancias { get; } = [];

    /// <summary>Decodificaciones en vivo, de la mas reciente a la mas antigua.</summary>
    public ObservableCollection<FilaDeDecodificacion> Decodificaciones { get; } = [];

    /// <summary>Contactos que los programas dan por cerrados y esperan a entrar en el cuaderno.</summary>
    public ObservableCollection<FilaDeQso> Cerrados { get; } = [];

    /// <summary>
    /// El ADIF en crudo que manda el programa al cerrar un contacto.
    /// </summary>
    /// <remarks>
    /// Se guarda aparte del aviso de contacto cerrado porque trae campos que el aviso binario
    /// no lleva. El operador lo ve tal cual: cuando algo no cuadre, ahi esta lo que de verdad
    /// dijo el programa.
    /// </remarks>
    public ObservableCollection<string> Adif { get; } = [];

    /// <summary>Puerto UDP en el que se escucha.</summary>
    public int Puerto => _puente.Puerto;

    /// <summary>Perfil de estacion con el que se guardan los contactos. Lo fija la ventana.</summary>
    public long? EstacionId { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstadoTexto))]
    [NotifyCanExecuteChangedFor(nameof(ArrancarCommand))]
    [NotifyCanExecuteChangedFor(nameof(PararCommand))]
    private bool _escuchando;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AdmiteResponder))]
    [NotifyPropertyChangedFor(nameof(AdmiteResaltar))]
    [NotifyPropertyChangedFor(nameof(AvisoDeCapacidades))]
    [NotifyPropertyChangedFor(nameof(AdmiteLlamarCq))]
    [NotifyPropertyChangedFor(nameof(AdmiteCambiarTono))]
    [NotifyPropertyChangedFor(nameof(AdmiteCambiarConfiguracion))]
    [NotifyCanExecuteChangedFor(nameof(ResponderCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResaltarCommand))]
    [NotifyCanExecuteChangedFor(nameof(LlamarCqCommand))]
    [NotifyCanExecuteChangedFor(nameof(PonerTonoTxCommand))]
    [NotifyCanExecuteChangedFor(nameof(CambiarConfiguracionCommand))]
    private FilaDeInstancia? _instanciaElegida;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResponderCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResaltarCommand))]
    private FilaDeDecodificacion? _decodificacionElegida;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCerradoCommand))]
    private FilaDeQso? _cerradoElegido;

    [ObservableProperty]
    private bool _soloNuevos;

    [ObservableProperty]
    private string _tonoDeTransmision = "1500";

    [ObservableProperty]
    private string _configuracion = string.Empty;

    [ObservableProperty]
    private string _aviso = string.Empty;

    /// <summary>Si se esta escuchando o no, en una linea.</summary>
    public string EstadoTexto => Escuchando
        ? $"Escuchando en el puerto UDP {Puerto.ToString(CultureInfo.InvariantCulture)}"
        : "Sin escuchar";

    /// <summary>
    /// La instancia elegida admite que el cuaderno le diga a quien llamar.
    /// </summary>
    /// <remarks>
    /// No se adivina por el nombre del programa: se pregunta al puente, que es quien sabe que
    /// mensajes entiende el dialecto que tiene delante.
    /// </remarks>
    public bool AdmiteResponder => Capacidades.PuedeResponder;

    /// <summary>La instancia elegida admite resaltar indicativos en su ventana.</summary>
    public bool AdmiteResaltar => Capacidades.PuedeResaltar;

    /// <summary>La instancia elegida admite lanzar una llamada general.</summary>
    public bool AdmiteLlamarCq => Capacidades.PuedeLlamarCq;

    /// <summary>La instancia elegida admite que le cambiemos el tono de transmision.</summary>
    public bool AdmiteCambiarTono => Capacidades.PuedeCambiarTonoTx;

    /// <summary>La instancia elegida admite cambiar de configuracion.</summary>
    public bool AdmiteCambiarConfiguracion => Capacidades.PuedeCambiarConfiguracion;

    /// <summary>Lo que sabe hacer la instancia elegida, tal y como lo declara el puente.</summary>
    public CapacidadesDigitales Capacidades => InstanciaElegida is { } fila
        ? _puente.Capacidades(fila.Identificador)
        : CapacidadesDigitales.Desconocidas;

    /// <summary>Explica por que hay botones desactivados, cuando los hay.</summary>
    public string AvisoDeCapacidades
    {
        get
        {
            if (InstanciaElegida is not { } fila) return "Elija una instancia para poder actuar sobre ella.";

            var capacidades = Capacidades;
            var faltan = new List<string>(5);
            if (!capacidades.PuedeResponder) faltan.Add("responder a una llamada");
            if (!capacidades.PuedeResaltar) faltan.Add("resaltar indicativos");
            if (!capacidades.PuedeLlamarCq) faltan.Add("lanzar una llamada general");
            if (!capacidades.PuedeCambiarTonoTx) faltan.Add("cambiar el tono de transmisión");
            if (!capacidades.PuedeCambiarConfiguracion) faltan.Add("cambiar de configuración");

            return faltan.Count == 0
                ? $"{fila.DialectoTexto} admite todo lo que ofrece este panel."
                : $"{fila.DialectoTexto} no admite {string.Join(", ni ", faltan)}.";
        }
    }

    /// <summary>Empieza a escuchar a los programas de modos digitales.</summary>
    [RelayCommand(CanExecute = nameof(SePuedeArrancar))]
    public async Task ArrancarAsync()
    {
        try
        {
            await _puente.ArrancarAsync().ConfigureAwait(true);
            Escuchando = true;
            Aviso = string.Empty;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido abrir el puerto de los modos digitales.");
            Aviso = $"No se ha podido escuchar: {ex.Message}";
        }
    }

    /// <summary>Deja de escuchar.</summary>
    [RelayCommand(CanExecute = nameof(SePuedeParar))]
    public async Task PararAsync()
    {
        try
        {
            await _puente.PararAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Fallo al cerrar el puerto de los modos digitales.");
        }
        finally
        {
            Escuchando = false;
            Instancias.Clear();
            InstanciaElegida = null;
        }
    }

    /// <summary>Le dice al programa que responda a la llamada elegida.</summary>
    [RelayCommand(CanExecute = nameof(SePuedeResponder))]
    public async Task ResponderAsync()
    {
        if (InstanciaElegida is not { } instancia || DecodificacionElegida is not { } fila) return;

        try
        {
            var hecho = await _puente
                .ResponderAAsync(fila.Decodificacion)
                .ConfigureAwait(true);

            Aviso = hecho
                ? $"Se le ha pedido a {instancia.DialectoTexto} que llame a {fila.Indicativo}."
                : $"{instancia.DialectoTexto} no admite que se le diga a quién llamar.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido pedir la respuesta al programa de modos digitales.");
            Aviso = $"No se ha podido responder: {ex.Message}";
        }
    }

    /// <summary>Resalta en la ventana del programa el indicativo elegido.</summary>
    [RelayCommand(CanExecute = nameof(SePuedeResaltar))]
    public async Task ResaltarAsync()
    {
        if (InstanciaElegida is not { } instancia || DecodificacionElegida is not { } fila) return;

        try
        {
            var hecho = await _puente
                .ResaltarAsync(
                    instancia.Identificador,
                    fila.Decodificacion.Llamante,
                    fila.EsNuevo)
                .ConfigureAwait(true);

            Aviso = hecho
                ? $"{fila.Indicativo} resaltado en {instancia.DialectoTexto}."
                : $"{instancia.DialectoTexto} no admite resaltar indicativos.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido resaltar el indicativo.");
            Aviso = $"No se ha podido resaltar: {ex.Message}";
        }
    }

    /// <summary>Guarda en el cuaderno el contacto cerrado que este elegido.</summary>
    [RelayCommand(CanExecute = nameof(SePuedeGuardarCerrado))]
    public async Task GuardarCerradoAsync()
    {
        if (CerradoElegido is not { Qso: { } qso } fila) return;

        try
        {
            // Se admite el duplicado a proposito: el programa de digitales ya dio el contacto
            // por cerrado, y hacerle repetirlo al operador seria peor que guardarlo dos veces.
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
                Aviso = $"No se ha podido guardar: {string.Join("; ", resultado.Errores)}";
                return;
            }

            Cerrados.Remove(fila);
            CerradoElegido = null;
            Aviso = $"Contacto con {qso.Call.Valor} guardado en el cuaderno.";
            CuadernoCambiado?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido guardar el contacto llegado de los modos digitales.");
            Aviso = $"No se ha podido guardar el contacto: {ex.Message}";
        }
    }

    /// <summary>
    /// Pide al programa que lance una llamada general.
    /// </summary>
    /// <remarks>
    /// Sale al aire por el programa de modos digitales, que tiene su propio control del PTT.
    /// El boton de panico del panel del equipo sigue cortando la transmision, porque quien
    /// suelta el PTT es el vigilante y no el programa.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(SePuedeLlamarCq))]
    public async Task LlamarCqAsync()
    {
        if (InstanciaElegida is not { } instancia) return;

        try
        {
            var hecho = await _puente.LlamarCqAsync(instancia.Identificador).ConfigureAwait(true);
            Aviso = hecho
                ? $"{instancia.DialectoTexto} está lanzando una llamada general."
                : $"{instancia.DialectoTexto} no admite lanzar una llamada general.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido lanzar la llamada general.");
            Aviso = $"No se ha podido llamar: {ex.Message}";
        }
    }

    /// <summary>Cambia el tono de transmision dentro del ancho de banda de audio.</summary>
    [RelayCommand(CanExecute = nameof(SePuedeCambiarElTono))]
    public async Task PonerTonoTxAsync()
    {
        if (InstanciaElegida is not { } instancia) return;

        if (!int.TryParse(TonoDeTransmision, NumberStyles.Integer, CultureInfo.CurrentCulture, out var tono))
        {
            Aviso = "El tono se escribe en hercios, sólo con cifras.";
            return;
        }

        try
        {
            var hecho = await _puente.PonerTonoTxAsync(instancia.Identificador, tono).ConfigureAwait(true);
            Aviso = hecho
                ? $"Tono de transmisión puesto en {tono.ToString("N0", CultureInfo.CurrentCulture)} Hz."
                : $"{instancia.DialectoTexto} no admite cambiar el tono de transmisión.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido cambiar el tono de transmisión.");
            Aviso = $"No se ha podido cambiar el tono: {ex.Message}";
        }
    }

    /// <summary>Cambia la configuracion activa del programa.</summary>
    [RelayCommand(CanExecute = nameof(SePuedeCambiarLaConfiguracion))]
    public async Task CambiarConfiguracionAsync()
    {
        if (InstanciaElegida is not { } instancia) return;

        var nombre = Configuracion.Trim();
        if (nombre.Length == 0) return;

        try
        {
            var hecho = await _puente
                .CambiarConfiguracionAsync(instancia.Identificador, nombre)
                .ConfigureAwait(true);

            Aviso = hecho
                ? $"{instancia.DialectoTexto} ha pasado a la configuración «{nombre}»."
                : $"{instancia.DialectoTexto} no admite cambiar de configuración.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido cambiar la configuración del programa.");
            Aviso = $"No se ha podido cambiar la configuración: {ex.Message}";
        }
    }

    /// <summary>Vacia la lista de decodificaciones.</summary>
    [RelayCommand]
    public void LimpiarDecodificaciones() => Decodificaciones.Clear();

    /// <summary>Deja de escuchar al puente. Lo llama la ventana al cerrarse.</summary>
    public void Detener()
    {
        _puente.EstadoRecibido -= AlLlegarEstado;
        _puente.Decodificado -= AlDecodificar;
        _puente.QsoRegistrado -= AlCerrarUnContacto;
        _puente.InstanciaPerdida -= AlPerderInstancia;
        _puente.AdifRecibido -= AlLlegarAdif;
    }

    private bool SePuedeArrancar() => !Escuchando;

    private bool SePuedeParar() => Escuchando;

    private bool SePuedeResponder() => AdmiteResponder && DecodificacionElegida is not null;

    private bool SePuedeResaltar() => AdmiteResaltar && DecodificacionElegida is not null;

    private bool SePuedeLlamarCq() => AdmiteLlamarCq;

    private bool SePuedeCambiarElTono() => AdmiteCambiarTono;

    private bool SePuedeCambiarLaConfiguracion() => AdmiteCambiarConfiguracion;

    private bool SePuedeGuardarCerrado() => CerradoElegido is not null;

    private void AlLlegarEstado(object? origen, EstadoDigital estado) => Hilo.EnLaVentana(() =>
    {
        var fila = Instancias.FirstOrDefault(
            i => string.Equals(i.Identificador, estado.Identificador, StringComparison.Ordinal));

        if (fila is null)
        {
            fila = new FilaDeInstancia(estado);
            Instancias.Add(fila);
            InstanciaElegida ??= fila;
        }
        else
        {
            fila.Recoger(estado);
        }

        if (!ReferenceEquals(fila, InstanciaElegida)) return;

        OnPropertyChanged(nameof(Capacidades));
        OnPropertyChanged(nameof(AvisoDeCapacidades));
        OnPropertyChanged(nameof(AdmiteResponder));
        OnPropertyChanged(nameof(AdmiteResaltar));
        OnPropertyChanged(nameof(AdmiteLlamarCq));
        OnPropertyChanged(nameof(AdmiteCambiarTono));
        OnPropertyChanged(nameof(AdmiteCambiarConfiguracion));
    });

    private void AlLlegarAdif(object? origen, string adif) => Hilo.EnLaVentana(() =>
    {
        Adif.Insert(0, adif);
        while (Adif.Count > ContactosEnEspera) Adif.RemoveAt(Adif.Count - 1);
    });

    /// <summary>
    /// Mete una decodificacion en la lista, resaltada si el indicativo seria nuevo.
    /// </summary>
    /// <remarks>
    /// Es un manejador de evento asincrono —consulta el cuaderno— y por eso se traga todo: una
    /// decodificacion rara no puede tumbar el programa a mitad de una apertura.
    /// </remarks>
    private async void AlDecodificar(object? origen, DecodificacionDigital decodificacion)
    {
        try
        {
            var nuevo = false;
            if (!decodificacion.Llamante.EsVacio)
            {
                var visto = await _trabajadoAntes
                    .EjecutarAsync(decodificacion.Llamante)
                    .ConfigureAwait(false);
                nuevo = !visto.TrabajadoAntes;
            }

            Hilo.EnLaVentana(() =>
            {
                if (SoloNuevos && !nuevo) return;

                Decodificaciones.Insert(0, new FilaDeDecodificacion(decodificacion, nuevo));
                while (Decodificaciones.Count > DecodificacionesQueSeGuardan)
                {
                    Decodificaciones.RemoveAt(Decodificaciones.Count - 1);
                }
            });
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "No se ha podido procesar una decodificación.");
        }
    }

    private void AlCerrarUnContacto(object? origen, Qso qso) => Hilo.EnLaVentana(() =>
    {
        Cerrados.Insert(0, new FilaDeQso(qso));
        while (Cerrados.Count > ContactosEnEspera) Cerrados.RemoveAt(Cerrados.Count - 1);
    });

    private void AlPerderInstancia(object? origen, string identificador) => Hilo.EnLaVentana(() =>
    {
        var fila = Instancias.FirstOrDefault(
            i => string.Equals(i.Identificador, identificador, StringComparison.Ordinal));

        if (fila is null) return;

        Instancias.Remove(fila);
        if (ReferenceEquals(fila, InstanciaElegida)) InstanciaElegida = Instancias.FirstOrDefault();

        Aviso = $"Se ha perdido la instancia «{identificador}».";
    });
}
