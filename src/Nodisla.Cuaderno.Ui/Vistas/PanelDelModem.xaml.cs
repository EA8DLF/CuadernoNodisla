using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// El panel del modem propio: cascada, decodificaciones, reloj y contacto.
/// </summary>
/// <remarks>
/// <para>
/// Los dos dobles clics (en una decodificacion y en un mensaje Tx) van por el evento
/// <see cref="Control.MouseDoubleClick"/> y no por un <c>MouseBinding</c>: la fila de la lista
/// y la caja de texto marcan el clic como atendido, y el <c>MouseBinding</c> no se enteraba.
/// Eran dos de los gestos principales de la pestaña y no hacian nada.
/// </para>
/// <para>
/// El alto se reparte aqui tambien: si el hueco es mas bajo que lo minimo para operar, el
/// contenido se queda en ese minimo y se desplaza; si hay sitio, mide justo el hueco.
/// </para>
/// </remarks>
public partial class PanelDelModem : UserControl
{
    /// <summary>Alto minimo de la cascada, en puntos.</summary>
    public const double AltoMinimoDeLaCascada = 90;

    /// <summary>Alto minimo de la lista de decodificaciones, en puntos.</summary>
    public const double AltoMinimoDeLaLista = 100;

    /// <summary>
    /// Alto por debajo del cual el panel se desplaza en vez de apretarse: cabecera, reloj,
    /// cascada, mandos de Rx/Tx, lista y el pie con el pestillo y «Cortar y soltar PTT».
    /// </summary>
    public const double AltoMinimo = 430;

    /// <summary>Monta el panel.</summary>
    public PanelDelModem()
    {
        InitializeComponent();
        Loaded += AlCargar;
    }

    private static bool _escuchaPedidaHecha;

    /// <summary>
    /// Verificacion sin tocar la ventana del operador: con <c>CUADERNO_ESCUCHAR</c> puesta, se
    /// pulsa «Escuchar» sola al abrir. Solo abre la ENTRADA de audio (con
    /// <c>CUADERNO_AUDIO_REAL</c> no hay salida registrada que abrir).
    /// </summary>
    private async void AlCargar(object sender, RoutedEventArgs e)
    {
        // Con CUADERNO_DECODIFICAR_WAV=fichero se pasa ese WAV por «WAV…» al abrir, sin
        // dialogo: para comprobar la lista con una grabacion real del aire.
        if (!_escuchaPedidaHecha
            && Environment.GetEnvironmentVariable("CUADERNO_DECODIFICAR_WAV") is { Length: > 0 } wav
            && Modelo is { } conWav)
        {
            _escuchaPedidaHecha = true;
            var dialogo = conWav.ElegirFicheroWav;
            conWav.ElegirFicheroWav = () => wav;
            try
            {
                await conWav.DecodificarFicheroCommand.ExecuteAsync(null);
            }
            finally
            {
                conWav.ElegirFicheroWav = dialogo;
            }

            return;
        }

        if (_escuchaPedidaHecha || Environment.GetEnvironmentVariable("CUADERNO_ESCUCHAR") is not { Length: > 0 }) return;
        if (Modelo is not { } modelo) return;
        _escuchaPedidaHecha = true;

        // Y con CUADERNO_GUARDAR_WAV, cada ventana a un WAV, para poder repasar el aire real.
        if (Environment.GetEnvironmentVariable("CUADERNO_GUARDAR_WAV") is { Length: > 0 }) modelo.GuardarWav = true;
        if (modelo.EscucharCommand.CanExecute(null)) modelo.EscucharCommand.Execute(null);
    }

    private VistaModeloModemPropio? Modelo => DataContext as VistaModeloModemPropio;

    private void AlCambiarElHueco(object sender, SizeChangedEventArgs e)
    {
        var hueco = Desplazador.ActualHeight;
        if (hueco <= 0) return;
        Contenido.Height = Math.Max(hueco, AltoMinimo);
    }

    private void AlDobleClicEnUnaDecodificacion(object sender, MouseButtonEventArgs e)
    {
        if (Modelo is not { } modelo || sender is not ListBoxItem { DataContext: FilaDeDecodificacionPropia fila }) return;

        modelo.DecodificacionElegida = fila;
        if (modelo.PrepararRespuestaCommand.CanExecute(null)) modelo.PrepararRespuestaCommand.Execute(null);
        e.Handled = true;
    }

    /// <summary>
    /// La radio de «siguiente», se marque con el raton, con el teclado o por accesibilidad.
    /// Por el evento y no por una orden: la orden solo salta con el clic.
    /// </summary>
    private void AlMarcarElSiguiente(object sender, RoutedEventArgs e)
    {
        if (Modelo is not { } modelo || sender is not FrameworkElement { DataContext: MensajeTx mensaje }) return;
        if (modelo.TxSiguiente != mensaje.Numero) modelo.ElegirTxCommand.Execute(mensaje);
    }

    private void AlDobleClicEnUnMensaje(object sender, MouseButtonEventArgs e)
    {
        if (Modelo is not { } modelo || sender is not FrameworkElement { DataContext: MensajeTx mensaje }) return;

        modelo.EnviarTxCommand.Execute(mensaje);
        e.Handled = true;
    }
}
