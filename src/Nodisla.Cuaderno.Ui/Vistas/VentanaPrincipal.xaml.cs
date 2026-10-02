using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Ui.Recursos;
using Nodisla.Cuaderno.Ui.VistaModelos;
using Serilog;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// Ventana principal del cuaderno.
/// </summary>
/// <remarks>
/// <para>
/// La pantalla principal es la <b>cabina de operación</b>: el equipo, la entrada de contacto,
/// el cluster y los modos digitales. El cuaderno tiene su propia pestaña. En la Fase 1 ocupaba
/// la ventana entera porque no habia nada mas, y esa herencia era justo lo que hacia que la
/// pantalla no sirviera para operar.
/// </para>
/// <para>
/// El codigo de aqui se limita a lo que es propio de la vista: mover el foco, repartir el alto
/// y preguntar antes de borrar o de transmitir.
/// </para>
/// </remarks>
public partial class VentanaPrincipal : Window
{
    private readonly VistaModeloPrincipal _vistaModelo;
    /// <summary>
    /// La aplicacion corre con los puertos simulados, y hay que decirlo en pantalla.
    /// </summary>
    /// <remarks>
    /// Va como propiedad estatica porque el aviso vive en la barra de estado, que se dibuja
    /// antes de que haya modelo de vista al que preguntarle.
    /// </remarks>
    public static bool EnPruebas => ConfiguracionDeServicios.ConPuertosSimulados;

    private readonly CrearPerfilDeEstacion _perfiles;

    /// <summary>
    /// Como pedir la bienvenida del cuaderno vacio. Nulo con los puertos simulados, donde el
    /// cuaderno viene lleno de contactos de relleno y no hay nada que ofrecer.
    /// </summary>
    private readonly Func<VentanaDeCuadernoVacio>? _cuadernoVacio;
    private readonly Func<VentanaDePrimerArranque> _ventanaDePrimerArranque;
    private bool _dibujadoComprobado;
    private bool _ajustandoComposicion;

    /// <summary>Monta la ventana con su modelo de vista.</summary>
    public VentanaPrincipal(
        VistaModeloPrincipal vistaModelo,
        CrearPerfilDeEstacion perfiles,
        Func<VentanaDePrimerArranque> ventanaDePrimerArranque,
        Func<VentanaDeCuadernoVacio>? cuadernoVacio = null)
    {
        ArgumentNullException.ThrowIfNull(vistaModelo);
        _vistaModelo = vistaModelo;
        _perfiles = perfiles;
        _ventanaDePrimerArranque = ventanaDePrimerArranque;
        _cuadernoVacio = cuadernoVacio;

        InitializeComponent();
        DataContext = vistaModelo;

        _vistaModelo.Cuaderno.ConfirmarBorrado = PreguntarSiBorrar;
        _vistaModelo.Equipo.ConfirmarQueVaATransmitir = PreguntarSiTransmite;

        // A/B (SV;) y A=B (AB;/BA;) cambian los VFO de verdad: siempre con el operador delante.
        _vistaModelo.Equipo.ConfirmarAccion = PreguntarSiCambiaLosVfos;

        // Pulsación larga de LOCK: apagar la radio siempre pregunta antes.
        _vistaModelo.Equipo.ConfirmarApagado = PreguntarSiApaga;

        // El módem propio también pregunta antes de salir al aire, y con su propio texto:
        // un período de FT8 son TRECE SEGUNDOS con el equipo en antena, y eso no se parece a
        // pulsar el acoplador.
        _vistaModelo.Modem.ConfirmarQueVaATransmitir = PreguntarSiEmiteElModem;
        _vistaModelo.Modem.ElegirFicheroWav = PedirUnWav;
        ContentRendered += AlTerminarElPrimerDibujado;

        // Lo que cambia el alto que piden los bloques obliga a repartir de nuevo.
        _vistaModelo.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(VistaModeloPrincipal.TamanoDeLetra)
                or nameof(VistaModeloPrincipal.DatosAmpliadosPedidos))
            {
                Dispatcher.BeginInvoke(AjustarComposicion, DispatcherPriority.Loaded);
            }
        };
    }

    private async void AlCargar(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!await PedirPerfilSiHaceFaltaAsync().ConfigureAwait(true)) return;

            await OfrecerTraerElCuadernoAsync().ConfigureAwait(true);

            await _vistaModelo.InicializarAsync().ConfigureAwait(true);
            Cuaderno.MarcarOrdenEnLasCabeceras();
            PonerLaEstacionEnElMapa();
            Operar.EnfocarIndicativo();

            // El catalogo de diplomas y las cifras de ajustes se traen al abrir, no al entrar
            // en la pestana: asi la primera visita no se queda mirando un hueco vacio.
            await _vistaModelo.Diplomas.CargarAsync().ConfigureAwait(true);
            await _vistaModelo.Configuracion.RefrescarAsync().ConfigureAwait(true);

            // Verificacion sin molestar: si se ha pedido una captura, la ventana se retrata
            // sola y se cierra. No hay que traerla al frente ni darle un solo clic.
            Desarrollo.RetratoDeLaVentana.ProgramarSiSePide(this);
        }
        catch (Exception ex)
        {
            MostrarFallo(ex);
        }
    }

    /// <summary>
    /// Sin perfil de estacion no se deja operar: se pide antes de cargar nada y, si el operador
    /// no quiere crearlo, el programa se cierra. Nunca se llega a la entrada sin perfil.
    /// </summary>
    /// <summary>
    /// Con el cuaderno vacio, ofrece traerse un ADIF antes de ensenar una rejilla en blanco.
    /// </summary>
    /// <remarks>
    /// <b>No importa nada solo.</b> Se explica que el cuaderno esta vacio, donde vive, y se
    /// ofrece; decide el operador. Si dice que no, se sigue con el cuaderno vacio, que es una
    /// respuesta perfectamente valida.
    /// </remarks>
    private async Task OfrecerTraerElCuadernoAsync()
    {
        if (_cuadernoVacio is null) return;

        try
        {
            if (await _vistaModelo.HayContactosAsync().ConfigureAwait(true)) return;

            var ventana = _cuadernoVacio();
            ventana.Owner = this;
            Desarrollo.RetratoDeLaVentana.PrepararDialogo(ventana);
            ventana.ShowDialog();
        }
        catch (Exception ex)
        {
            // Que falle la bienvenida no puede impedir abrir el cuaderno.
            Serilog.Log.Error(ex, "No se ha podido ofrecer la importación inicial.");
        }
    }

    private async Task<bool> PedirPerfilSiHaceFaltaAsync()
    {
        if (await _perfiles.HayAlgunoAsync().ConfigureAwait(true)) return true;

        Log.Information("No hay ningun perfil de estacion; se pide el del primer arranque.");

        var dialogo = _ventanaDePrimerArranque();
        dialogo.Owner = this;
        Desarrollo.RetratoDeLaVentana.PrepararDialogo(dialogo);

        if (dialogo.ShowDialog() == true) return true;

        Log.Information("El operador ha salido sin crear el perfil; se cierra el programa.");
        Application.Current.Shutdown();
        return false;
    }

    /// <summary>
    /// Ya se ha pintado la primera pasada: se comprueba que se haya pintado de verdad. Hay
    /// equipos donde el dibujado por hardware deja la ventana en blanco sin dar ningun error.
    /// </summary>
    private void AlTerminarElPrimerDibujado(object? sender, EventArgs e)
    {
        if (_dibujadoComprobado) return;
        _dibujadoComprobado = true;

        var estado = ComprobacionDeDibujado.Comprobar(this);
        Log.Information(
            "Comprobación del dibujado: {Estado} (modo {Modo}, nivel {Nivel}).",
            estado,
            RenderOptions.ProcessRenderMode,
            RenderCapability.Tier >> 16);

        if (estado != EstadoDelDibujado.EnBlanco) return;

        Log.Warning("La ventana sale en blanco con dibujado por hardware; se pasa a software.");
        ComprobacionDeDibujado.PasarADibujadoPorSoftware(this);

        var segunda = ComprobacionDeDibujado.Comprobar(this);
        if (segunda == EstadoDelDibujado.Correcto)
        {
            Log.Information("Con dibujado por software la ventana ya se pinta correctamente.");
        }
        else
        {
            Log.Error("La ventana sigue sin pintarse ({Estado}) ni siquiera por software.", segunda);
        }
    }

    /// <summary>Pone en el mapa la estacion propia, sacada del localizador del perfil activo.</summary>
    private void PonerLaEstacionEnElMapa()
    {
        if (_vistaModelo.EstacionActiva is not { } estacion) return;

        _vistaModelo.Mapa.FijarEstacion(estacion.MyGridsquare, estacion.StationCallsign.Valor);
    }

    private void AlCerrar(object sender, EventArgs e)
    {
        // La instancia apartada de verificacion no pisa lo que haya dejado el operador.
        if (Environment.GetEnvironmentVariable("CUADERNO_APARTADA") is not { Length: > 0 })
        {
            _vistaModelo.GuardarEstadoDeLosPaneles(App.CarpetaDeDatos);
        }

        _vistaModelo.Detener();
    }

    private void AlCambiarDeTamano(object sender, SizeChangedEventArgs e) => AjustarComposicion();

    /// <summary>
    /// Reparte el alto de la ventana.
    /// </summary>
    /// <remarks>
    /// La regla no ha cambiado desde la Fase 1: un bloque se ve entero o se pliega entero,
    /// nunca se corta a media fila. Lo que ceden son los que tienen su contenido repetido en
    /// otro sitio: primero la leyenda de atajos, que es una ayuda y no un dato; despues los
    /// contactos previos del aviso; despues la segunda fila de la entrada; y por ultimo la
    /// cabecera, cuyo perfil y reloj siguen en la barra de estado.
    /// </remarks>
    private void AjustarComposicion()
    {
        if (_ajustandoComposicion) return;

        try
        {
            _ajustandoComposicion = true;

            _vistaModelo.OlvidarPliegueAutomatico();
            UpdateLayout();
            if (LeCabeALaCabina()) return;

            _vistaModelo.LeyendaSinSitio = true;
            UpdateLayout();
            if (LeCabeALaCabina()) return;

            _vistaModelo.ContactosPreviosSinSitio = true;
            UpdateLayout();
            if (LeCabeALaCabina()) return;

            _vistaModelo.DatosAmpliadosSinSitio = true;
            UpdateLayout();
            if (LeCabeALaCabina()) return;

            _vistaModelo.CabeceraSinSitio = true;
            UpdateLayout();
        }
        finally
        {
            _ajustandoComposicion = false;
        }
    }

    /// <summary>
    /// La cabina tiene alto para lo suyo.
    /// </summary>
    /// <remarks>
    /// Se mide en letras, no en pixeles, para que la cuenta siga valiendo con la escala al
    /// 200 %: a esa escala todo pide el doble de alto, y una cuenta en pixeles dejaria la
    /// ventana partida.
    /// </remarks>
    private bool LeCabeALaCabina()
    {
        // Lo que necesita la cabina para que se vean las cuatro franjas: el equipo, una fila
        // de entrada, unos cuantos spots y unas cuantas decodificaciones. Por debajo de esto
        // hay que empezar a plegar lo accesorio.
        const double LetrasDeAltoMinimo = 46.0;

        return Operar.ActualHeight >= _vistaModelo.TamanoDeLetra * LetrasDeAltoMinimo
               || _vistaModelo.IndiceDeLaPestana != 0;
    }

    /// <summary>Atajos que necesitan mover el foco, que es cosa de la ventana y no del modelo.</summary>
    private void AlTeclearEnLaVentana(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.F3:
                _vistaModelo.VerElCuaderno();
                // Loaded y no Input: la primera vez la pestaña aun no esta dibujada cuando se
                // atiende la tecla, y el foco no tenia donde caer. Loaded va justo despues de
                // maquetarla (ContextIdle tambien valia, pero con el reloj y la cascada pintando
                // sin parar llegaba a tardar mas de medio segundo).
                Dispatcher.BeginInvoke(Cuaderno.EnfocarBusqueda, DispatcherPriority.Loaded);
                e.Handled = true;
                break;

            case Key.F4:
                _vistaModelo.VerOperar();
                Dispatcher.BeginInvoke(Operar.EnfocarIndicativo, DispatcherPriority.Loaded);
                e.Handled = true;
                break;
        }
    }

    private void AlFallarElCuaderno(object? origen, Exception ex) => MostrarFallo(ex);

    private bool PreguntarSiBorrar(FilaDeQso fila)
    {
        var dialogo = new VentanaDeConfirmacion
        {
            Owner = this,
            Titulo = Textos.T("Principal.Confirmar.BorrarTitulo"),
            Detalle = Textos.F("Principal.Confirmar.BorrarDetalle", fila.Indicativo, fila.Fecha, fila.Hora, fila.Banda, fila.Modo),
            TextoDeAceptar = Textos.T("Principal.Confirmar.BorrarAceptar"),
        };

        return dialogo.ShowDialog() == true;
    }

    /// <summary>
    /// Avisa antes de accionar un mando que pone el equipo en antena.
    /// </summary>
    /// <remarks>
    /// El acoplador de antena es el caso tipico: sintonizar emite portadora. El operador tiene
    /// que saber que va a salir al aire <b>antes</b> de que salga, no enterarse por el
    /// medidor de potencia.
    /// </remarks>
    private bool PreguntarSiApaga(string que)
    {
        var dialogo = new VentanaDeConfirmacion
        {
            Owner = this,
            Titulo = que,
            Detalle = Textos.T("Principal.Confirmar.ApagarDetalle"),
            TextoDeAceptar = Textos.T("Principal.Confirmar.ApagarAceptar"),
        };

        return dialogo.ShowDialog() == true;
    }

    private bool PreguntarSiCambiaLosVfos(string que)
    {
        var dialogo = new VentanaDeConfirmacion
        {
            Owner = this,
            Titulo = Textos.T("Principal.Confirmar.VfosTitulo"),
            Detalle = Textos.F("Principal.Confirmar.VfosDetalle", que),
            TextoDeAceptar = Textos.T("Principal.Confirmar.VfosAceptar"),
        };

        return dialogo.ShowDialog() == true;
    }

    private bool PreguntarSiTransmite(string mando)
    {
        var dialogo = new VentanaDeConfirmacion
        {
            Owner = this,
            Titulo = Textos.T("Principal.Confirmar.TransmitirTitulo"),
            Detalle = Textos.F("Principal.Confirmar.TransmitirDetalle", mando),
            TextoDeAceptar = Textos.T("Principal.Confirmar.TransmitirAceptar"),
        };

        return dialogo.ShowDialog() == true;
    }

    /// <summary>
    /// Avisa antes de que el modem propio saque un periodo al aire.
    /// </summary>
    /// <remarks>
    /// El texto es distinto del de los mandos del equipo a proposito: aqui lo que sale no es
    /// un instante de portadora sino <b>trece segundos seguidos</b> de señal modulada, y
    /// ademas alineados a una ventana que comparten todos los demas. Quien transmite con el
    /// reloj mal, o sin antena, se entera tarde.
    /// </remarks>
    /// <summary>Pide al operador un WAV para pasarlo por el decodificador.</summary>
    private string? PedirUnWav()
    {
        var dialogo = new Microsoft.Win32.OpenFileDialog
        {
            Title = Textos.T("Principal.Wav.Titulo"),
            Filter = Textos.T("Principal.Wav.Filtro"),
            CheckFileExists = true,
        };

        return dialogo.ShowDialog(this) == true ? dialogo.FileName : null;
    }

    private bool PreguntarSiEmiteElModem(string mensaje)
    {
        var dialogo = new VentanaDeConfirmacion
        {
            Owner = this,
            Titulo = Textos.T("Principal.Confirmar.ModemTitulo"),
            Detalle = Textos.F("Principal.Confirmar.ModemDetalle", mensaje),
            TextoDeAceptar = Textos.T("Principal.Confirmar.TransmitirAceptar"),
            OfrecerNoVolverAPreguntar = true,
        };

        var si = dialogo.ShowDialog() == true;

        // «No volver a preguntar» solo cuenta si se ha dicho que sí: cancelar no apaga nada.
        if (si && dialogo.NoVolverAPreguntar) _vistaModelo.Modem.NoVolverAPreguntarAlTransmitir();
        return si;
    }

    private void MostrarFallo(Exception ex)
    {
        // Al registro ANTES de ensenar nada: un fallo que solo aparece en un cartel se pierde
        // en cuanto alguien le da a «Entendido», y entonces no hay manera de saber que paso.
        Serilog.Log.Error(ex, "Fallo al arrancar la ventana principal.");

        var dialogo = new VentanaDeConfirmacion
        {
            Owner = this,
            Titulo = Textos.T("Principal.Fallo.Titulo"),
            Detalle = ex.Message,
            TextoDeAceptar = Textos.T("Principal.Fallo.Entendido"),
            SoloAviso = true,
        };
        dialogo.ShowDialog();
    }
}
