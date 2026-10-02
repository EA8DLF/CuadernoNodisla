using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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
        MaxWidth = Vertical ? 250 : double.PositiveInfinity;
    }

    private async void AlCargar(object sender, RoutedEventArgs e)
    {
        _ventana = Window.GetWindow(this);
        if (_ventana is not null)
        {
            _ventana.PreviewKeyDown += AlPulsarTecla;
            _ventana.PreviewKeyUp += AlSoltarTecla;
            _ventana.Deactivated += AlPerderElFoco;
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
