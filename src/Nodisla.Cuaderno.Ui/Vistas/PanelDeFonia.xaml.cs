using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// El panel de fonia. El codigo de vista hace lo que un enlace no sabe hacer: el boton que se
/// mantiene pulsado, la tecla del PTT y soltar al perder el foco.
/// </summary>
/// <remarks>
/// <para>
/// El PTT se suelta: al levantar el boton, al perder el boton el raton, al perder la ventana el
/// foco y al descargarse el panel (cambio de pestana en modo conmutado). Soltar de mas no hace
/// dano; soltar de menos deja una radio en el aire.
/// </para>
/// <para>
/// La tecla solo actua con la ventana activa y nunca con el foco en un campo de texto: si no,
/// escribir un indicativo con la tecla elegida saldria al aire.
/// </para>
/// </remarks>
public partial class PanelDeFonia : UserControl
{
    private readonly DispatcherTimer _refresco = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
    private Window? _ventana;
    private bool _teclaAbajo;

    /// <summary>Monta el panel.</summary>
    public PanelDeFonia()
    {
        InitializeComponent();
        _refresco.Tick += (_, _) => Modelo?.Refrescar();
        Loaded += AlCargar;
        Unloaded += AlDescargar;
    }

    /// <summary>
    /// En columna, al lado del frontal —donde a la derecha sobra sitio—, o en franja, cuando el
    /// frontal esta plegado. En columna no quita ni un punto de alto al contacto ni al cluster.
    /// </summary>
    public static readonly DependencyProperty VerticalProperty = DependencyProperty.Register(
        nameof(Vertical),
        typeof(bool),
        typeof(PanelDeFonia),
        new PropertyMetadata(false, (d, _) => ((PanelDeFonia)d).Disponer()));

    /// <summary>Disposicion en columna.</summary>
    public bool Vertical
    {
        get => (bool)GetValue(VerticalProperty);
        set => SetValue(VerticalProperty, value);
    }

    private VistaModeloFonia? Modelo => DataContext as VistaModeloFonia;

    private void Disponer()
    {
        Fila.Orientation = Vertical ? Orientation.Vertical : Orientation.Horizontal;
        foreach (var hijo in Fila.Children.OfType<FrameworkElement>())
        {
            if (hijo.Tag is not Tuple<Thickness, double> original)
            {
                original = Tuple.Create(hijo.Margin, hijo.Width);
                hijo.Tag = original;
            }

            hijo.Margin = Vertical ? new Thickness(0, 0, 0, 6) : original.Item1;
            hijo.Width = Vertical ? 240 : original.Item2;
        }

        BotonPtt.Height = Vertical ? 64 : 46;

        // 240 de cada fila + 18 de la tarjeta que ahora envuelve el panel entero (8 de relleno
        // y 1 de borde a cada lado, como las de HAM y Canales CB): sin sumarlos aqui, el propio
        // borde se comia esos 18 puntos y «Silencio» salia cortado a mitad de palabra.
        MaxWidth = Vertical ? 258 : double.PositiveInfinity;

        // En columna no hay sitio para NR/notch/limitador, mensajes de voz ni grabador: esa fila
        // se queda a su ancho horizontal de siempre y es ella la que, sin tocar, obliga al
        // Viewbox exterior (Stretch="Uniform") a encoger TODO el panel para que quepa junto al
        // frontal, dejando el texto ilegible. En franja (Vertical=False) vuelve a verse.
        Extras.Visibility = Vertical ? Visibility.Collapsed : Visibility.Visible;

        // Los avisos (el tipico «el equipo esta en RTTY, el PTT de fonia solo va en SSB/AM/FM»)
        // hacen EXACTAMENTE lo mismo que Extras si se dejan envolver en varias lineas: suben el
        // alto natural del panel por encima del que le da el frontal y el Viewbox exterior
        // encoge TODO -el PTT, los medidores, las letras- para que quepa. En columna van en una
        // sola linea con puntos suspensivos (el texto completo sigue en el ToolTip); en franja,
        // sin el Viewbox de por medio, se quedan como siempre.
        foreach (var aviso in Avisos.Children.OfType<TextBlock>())
        {
            aviso.TextWrapping = Vertical ? TextWrapping.NoWrap : TextWrapping.Wrap;
            aviso.TextTrimming = Vertical ? TextTrimming.CharacterEllipsis : TextTrimming.None;
            aviso.Width = Vertical ? 240 : double.NaN;
        }
    }

    private async void AlCargar(object sender, RoutedEventArgs e)
    {
        _ventana = Window.GetWindow(this);
        if (_ventana is not null)
        {
            _ventana.PreviewKeyDown += AlPulsarTecla;
            _ventana.PreviewKeyUp += AlSoltarTecla;
            _ventana.Deactivated += AlPerderElFoco;
            _ventana.PreviewMouseDown += AlClicFueraDelPopupDeDispositivos;
        }

        _refresco.Start();
        if (Modelo?.Mensajes is { } mensajes) mensajes.ConfirmarQueVaATransmitir ??= PreguntarSiTransmite;

        // Hay dos paneles montados —en columna junto al frontal y en franja— y solo uno se ve:
        // el escondido no abre nada.
        if (!IsVisible) return;
        if (Modelo is { } modelo)
        {
            await modelo.EscucharSiSePidioAsync();
            await PrepararCapturaSiSePideAsync(modelo);
        }
    }

    /// <summary>
    /// Para las capturas de la ayuda: con CUADERNO_FONIA se despliegan los dispositivos o se
    /// pone el panel «en el aire». SOLO con los puertos simulados: con la radio de verdad no
    /// hace nada, porque saldria al aire.
    /// </summary>
    private async Task PrepararCapturaSiSePideAsync(VistaModeloFonia modelo)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CUADERNO_SIMULADO"))) return;
        var que = Environment.GetEnvironmentVariable("CUADERNO_FONIA");
        if (string.IsNullOrEmpty(que)) return;

        if (que.Contains("dispositivos", StringComparison.OrdinalIgnoreCase)) Desplegar.IsChecked = true;
        if (que.Contains("escucha", StringComparison.OrdinalIgnoreCase) && !modelo.Escuchando) await modelo.AlternarEscuchaAsync();

        // El retrato no ve las ventanitas: su contenido se pone en linea, debajo del panel.
        if (que.Contains("procesado", StringComparison.OrdinalIgnoreCase))
        {
            modelo.Ajustes.ReductorActivo = true;
            modelo.Ajustes.NotchActivo = true;
            modelo.Ajustes.ProcesarMicro = true;
            PonerEnLinea(PopupProcesado);
        }

        if (que.Contains("mensajes", StringComparison.OrdinalIgnoreCase)) PonerEnLinea(PopupMensajes);
        if (que.Contains("voz", StringComparison.OrdinalIgnoreCase) && modelo.Mensajes is { } voz)
        {
            // Dos mensajes «grabados» con el microfono de mentira, para que se vean los botones.
            foreach (var n in new[] { 1, 2 })
            {
                var m = voz.Mensajes[n - 1];
                await voz.GrabarAsync(m);
                await Task.Delay(TimeSpan.FromSeconds(n == 1 ? 2.5 : 1.2));
                await voz.GrabarAsync(m);
            }
        }

        if (que.Contains("guardar", StringComparison.OrdinalIgnoreCase) && modelo.Grabacion is { } grabacion)
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            if (Environment.GetEnvironmentVariable("CUADERNO_INDICATIVO_FONIA") is { Length: > 0 } indicativo && grabacion.Entrada is { } entrada)
            {
                entrada.Indicativo = indicativo;
            }

            grabacion.GuardarLoUltimo();
        }

        if (que.Contains("aire", StringComparison.OrdinalIgnoreCase))
        {
            await Task.Delay(TimeSpan.FromSeconds(4));
            modelo.Ajustes.PttConmutado = true;
            await modelo.PttAbajoAsync();
        }
    }

    private void PonerEnLinea(Popup ventanita)
    {
        if (ventanita.Child is not { } contenido) return;
        ventanita.Child = null;
        if (contenido is FrameworkElement fe) fe.Margin = new Thickness(0, 6, 12, 0);
        EnLineaParaCaptura.Children.Add(contenido);
    }

    private async void AlDescargar(object sender, RoutedEventArgs e)
    {
        _refresco.Stop();
        if (_ventana is not null)
        {
            _ventana.PreviewKeyDown -= AlPulsarTecla;
            _ventana.PreviewKeyUp -= AlSoltarTecla;
            _ventana.Deactivated -= AlPerderElFoco;
            _ventana.PreviewMouseDown -= AlClicFueraDelPopupDeDispositivos;
            _ventana = null;
        }

        _teclaAbajo = false;
        if (Modelo is { } modelo) await modelo.AlPerderElFocoAsync();
    }

    private async void AlPulsarElPtt(object sender, MouseButtonEventArgs e)
    {
        if (Modelo is { } modelo) await modelo.PttAbajoAsync();
    }

    private async void AlSoltarElPtt(object sender, MouseButtonEventArgs e)
    {
        if (Modelo is { } modelo) await modelo.PttArribaAsync();
    }

    private async void AlPerderElRatonDelPtt(object sender, MouseEventArgs e)
    {
        // El boton suelta la captura al levantar el raton y tambien si otra cosa se la quita.
        // En mantener, en los dos casos se acaba la pasada.
        if (Modelo is { } modelo) await modelo.PttArribaAsync();
    }

    private async void AlPerderElFoco(object? sender, EventArgs e)
    {
        _teclaAbajo = false;
        if (Modelo is { } modelo) await modelo.AlPerderElFocoAsync();
    }

    /// <summary>
    /// Cierra el Popup de Dispositivos al primer clic de verdad fuera de el, como un
    /// desplegable normal. Un Popup con AllowsTransparency se pinta en su propia raiz de
    /// presentacion: un clic dentro de el (o dentro del Popup interno de uno de sus cuatro
    /// ComboBox mientras elige) nunca pasa por este manejador de la ventana, asi que elegir un
    /// dispositivo no lo toca -ni hace falta mirar si algun combo tiene el desplegable abierto-.
    /// Solo llega aqui un clic que SI vive en el arbol de la ventana: fuera del todo. El boton
    /// "Desplegar" se excluye aparte porque su propio Click ya abre y cierra el Popup; si este
    /// manejador tambien lo cerrase, el toggle del boton (que corre despues, al soltar) lo
    /// reabriria enseguida.
    /// </summary>
    private void AlClicFueraDelPopupDeDispositivos(object sender, MouseButtonEventArgs e)
    {
        if (!PopupDispositivos.IsOpen) return;
        if (e.OriginalSource is not DependencyObject origen) return;
        if (EsODesciendeDe(origen, Desplegar)) return;
        if (PopupDispositivos.Child is DependencyObject contenido && EsODesciendeDe(origen, contenido)) return;

        Desplegar.IsChecked = false;
    }

    private static bool EsODesciendeDe(DependencyObject nodo, DependencyObject posibleAncestro)
    {
        var actual = (DependencyObject?)nodo;
        while (actual is not null)
        {
            if (ReferenceEquals(actual, posibleAncestro)) return true;
            actual = actual is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(actual) : LogicalTreeHelper.GetParent(actual);
        }

        return false;
    }

    private async void AlPulsarTecla(object sender, KeyEventArgs e)
    {
        if (await AtenderTeclaDeMensajeAsync(e)) return;
        if (!EsLaTeclaDelPtt(e)) return;
        e.Handled = true;
        if (e.IsRepeat || _teclaAbajo) return;

        _teclaAbajo = true;
        if (Modelo is { } modelo) await modelo.PttAbajoAsync();
    }

    private async void AlSoltarTecla(object sender, KeyEventArgs e)
    {
        if (!_teclaAbajo || !EsLaTecla(e)) return;
        e.Handled = true;
        _teclaAbajo = false;
        if (Modelo is { } modelo) await modelo.PttArribaAsync();
    }

    /// <summary>
    /// Esc corta un mensaje grabado en el aire. F1–F6 lanzan los mensajes, solo con la opcion
    /// puesta, el panel a la vista, la ventana activa, el equipo en modo de voz, la tecla con
    /// mensaje grabado y el foco fuera de un campo de texto. Si no, la tecla sigue haciendo lo
    /// de siempre (F1, la ayuda).
    /// </summary>
    private async Task<bool> AtenderTeclaDeMensajeAsync(KeyEventArgs e)
    {
        if (Modelo is not { Mensajes: { } mensajes } modelo) return false;
        var tecla = e.Key == Key.System ? e.SystemKey : e.Key;

        if (tecla == Key.Escape && !e.Handled && modelo.EnviandoMensaje)
        {
            e.Handled = true;
            await mensajes.PararAsync();
            return true;
        }

        if (tecla is < Key.F1 or > Key.F6 || Keyboard.Modifiers != ModifierKeys.None) return false;
        if (!modelo.Ajustes.TeclasDeMensajes || _ventana is not { IsActive: true } || !IsVisible) return false;
        if (Keyboard.FocusedElement is TextBoxBase or PasswordBox or ComboBox { IsEditable: true }) return false;
        if (!modelo.PuedeTransmitir) return false;

        var numero = tecla - Key.F1 + 1;
        if (!mensajes.Mensajes.Any(m => m.Numero == numero && m.Grabado)) return false;

        e.Handled = true;
        if (!e.IsRepeat) await mensajes.PulsarTeclaAsync(numero);
        return true;
    }

    private bool PreguntarSiTransmite(string mensaje)
    {
        var dialogo = new VentanaDeConfirmacion
        {
            Owner = Window.GetWindow(this),
            Titulo = Textos.T("Cabina.Fonia.Voz.Confirmar.Titulo"),
            Detalle = mensaje,
            TextoDeAceptar = Textos.T("Principal.Confirmar.TransmitirAceptar"),
            OfrecerNoVolverAPreguntar = true,
        };

        var si = dialogo.ShowDialog() == true;
        // «No volver a preguntar» solo cuenta si se ha dicho que si; es el mismo ajuste del modem y la CW.
        if (si && dialogo.NoVolverAPreguntar) Modelo?.Mensajes?.NoVolverAPreguntarAlTransmitir();

        return si;
    }

    private bool EsLaTeclaDelPtt(KeyEventArgs e)
    {
        if (_ventana is not { IsActive: true } || !IsVisible) return false;
        if (!EsLaTecla(e)) return false;

        // Nunca con el foco en un sitio donde se escribe.
        return Keyboard.FocusedElement is not (TextBoxBase or PasswordBox or ComboBox { IsEditable: true });
    }

    private bool EsLaTecla(KeyEventArgs e)
    {
        var nombre = Modelo?.Ajustes.TeclaDelPtt?.Nombre;
        if (string.IsNullOrEmpty(nombre) || !Enum.TryParse<Key>(nombre, out var tecla)) return false;

        var pulsada = e.Key == Key.System ? e.SystemKey : e.Key;
        return pulsada == tecla;
    }
}
