using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// Transmitir en CW: macros F1–F12, escritura libre y la secuencia del contacto.
/// </summary>
/// <remarks>
/// <para>
/// Mientras se ve, las teclas F1–F12 mandan las macros (como en N1MM) y <b>Esc para la
/// telegrafia en el acto</b> si hay algo saliendo o el automatico esta puesto; si no, Esc sigue
/// haciendo lo de siempre. Se engancha a la ventana al cargarse y se suelta al descargarse.
/// </para>
/// <para>
/// Si nadie ha puesto las preguntas (transmitir, cambiar a CW, ficheros), las pone esta vista
/// con los dialogos del programa: el «no volver a preguntar» es el mismo ajuste del modem.
/// </para>
/// </remarks>
public partial class MacrosCw : UserControl
{
    private Window? _ventana;

    /// <summary>Monta la vista.</summary>
    public MacrosCw() => InitializeComponent();

    private VistaModeloTransmisionCw? Modelo => DataContext as VistaModeloTransmisionCw;

    private void AlCargar(object sender, RoutedEventArgs e)
    {
        _ventana = Window.GetWindow(this);
        if (_ventana is not null) _ventana.PreviewKeyDown += AlPulsarTecla;
        if (Modelo is not { } modelo) return;

        modelo.ConfirmarQueVaATransmitir ??= PreguntarSiTransmite;
        modelo.PreguntarSiCambiaACw ??= PreguntarSiCambiaACw;
        modelo.ElegirFicheroParaExportar ??= ElegirDondeExportar;
        modelo.ElegirFicheroParaImportar ??= ElegirQueImportar;
    }

    private void AlDescargar(object sender, RoutedEventArgs e)
    {
        if (_ventana is not null) _ventana.PreviewKeyDown -= AlPulsarTecla;
        _ventana = null;
    }

    private async void AlPulsarTecla(object sender, KeyEventArgs e)
    {
        if (Modelo is not { } modelo) return;
        var tecla = e.Key == Key.System ? e.SystemKey : e.Key;

        // Esc corta siempre que haya algo que cortar, se vea o no esta zona.
        if (tecla == Key.Escape && (modelo.EnAntena || modelo.Emisor.Enviando || modelo.Automatico))
        {
            e.Handled = true;
            await modelo.PararCommand.ExecuteAsync(null);
            return;
        }

        if (!IsVisible || Keyboard.Modifiers != ModifierKeys.None) return;
        if (tecla is >= Key.F1 and <= Key.F12)
        {
            e.Handled = true;
            if (e.IsRepeat) return;
            await modelo.PulsarTeclaAsync(tecla - Key.F1 + 1);
        }
    }

    private async void AlTeclearElTextoLibre(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Modelo is not { } modelo) return;
        e.Handled = true;

        // Intro con texto lo manda; sin texto, manda lo que toca (el ESM de N1MM).
        if (string.IsNullOrWhiteSpace(modelo.TextoLibre)) await modelo.IntroCommand.ExecuteAsync(null);
        else await modelo.EnviarTextoLibreCommand.ExecuteAsync(null);
    }

    private bool PreguntarSiTransmite(string mensaje)
    {
        var dialogo = new VentanaDeConfirmacion
        {
            Owner = Window.GetWindow(this),
            Titulo = Textos.T("Cabina.TxCw.Confirmar.Titulo"),
            Detalle = Textos.F("Cabina.TxCw.Confirmar.Detalle", mensaje),
            TextoDeAceptar = Textos.T("Principal.Confirmar.TransmitirAceptar"),
            OfrecerNoVolverAPreguntar = true,
        };

        var si = dialogo.ShowDialog() == true;

        // «No volver a preguntar» solo cuenta si se ha dicho que si: cancelar no apaga nada.
        if (si && dialogo.NoVolverAPreguntar) Modelo?.NoVolverAPreguntarAlTransmitir();
        return si;
    }

    private bool PreguntarSiCambiaACw(string pregunta)
    {
        var dialogo = new VentanaDeConfirmacion
        {
            Owner = Window.GetWindow(this),
            Titulo = Textos.T("Cabina.TxCw.CambiarACw.Titulo"),
            Detalle = pregunta,
            TextoDeAceptar = Textos.T("Cabina.TxCw.CambiarACw.Aceptar"),
        };

        return dialogo.ShowDialog() == true;
    }

    private string? ElegirDondeExportar()
    {
        var dialogo = new Microsoft.Win32.SaveFileDialog
        {
            Title = Textos.T("Cabina.TxCw.Exportar.Titulo"),
            Filter = Textos.T("Cabina.TxCw.Fichero.Filtro"),
            FileName = "macros-cw.json",
        };

        return dialogo.ShowDialog(Window.GetWindow(this)) == true ? dialogo.FileName : null;
    }

    private string? ElegirQueImportar()
    {
        var dialogo = new Microsoft.Win32.OpenFileDialog
        {
            Title = Textos.T("Cabina.TxCw.Importar.Titulo"),
            Filter = Textos.T("Cabina.TxCw.Fichero.Filtro"),
            CheckFileExists = true,
        };

        return dialogo.ShowDialog(Window.GetWindow(this)) == true ? dialogo.FileName : null;
    }
}
