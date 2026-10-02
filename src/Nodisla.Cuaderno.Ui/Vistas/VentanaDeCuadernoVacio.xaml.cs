using System.IO;
using System.Windows;
using Microsoft.Win32;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// Lo que se ensena la primera vez, con el cuaderno recien creado y vacio.
/// </summary>
/// <remarks>
/// <para>
/// Una rejilla vacia sin explicacion parece un programa roto. Aqui se dice que pasa, donde
/// vive el cuaderno y se ofrece traer lo que ya tenga en otro programa.
/// </para>
/// <para>
/// <b>No se importa nada solo.</b> Meter miles de contactos en el cuaderno de alguien sin que
/// lo haya pedido no se hace, por muy bien que salga.
/// </para>
/// </remarks>
public partial class VentanaDeCuadernoVacio : Window
{
    private readonly VistaModeloCuadernoVacio _modelo;

    /// <summary>Monta la ventana.</summary>
    /// <param name="modelo">Modelo de la bienvenida.</param>
    public VentanaDeCuadernoVacio(VistaModeloCuadernoVacio modelo)
    {
        _modelo = modelo ?? throw new ArgumentNullException(nameof(modelo));

        InitializeComponent();
        DataContext = modelo;

        Loaded += AlCargar;
    }

    /// <summary>
    /// Arranque desatendido: con <c>CUADERNO_IMPORTAR</c> puesta, importa ese fichero sin
    /// preguntar y se queda ensenando el parte.
    /// </summary>
    /// <remarks>
    /// Es la unica manera de comprobar el ciclo completo —cuaderno vacio, importar, cerrar,
    /// abrir— sin abrirle un dialogo de ficheros en la cara a quien este trabajando en el
    /// equipo. En uso normal la variable no esta puesta y no hace nada: la importacion la pide
    /// el operador con el boton.
    /// </remarks>
    private async void AlCargar(object sender, RoutedEventArgs e)
    {
        if (Environment.GetEnvironmentVariable("CUADERNO_IMPORTAR") is not { Length: > 0 } ruta) return;

        await _modelo.ImportarAsync(ruta).ConfigureAwait(true);

        // Y se cierra: si se quedara abierta, el arranque desatendido se colgaria aqui
        // esperando un clic que no va a llegar.
        DialogResult = true;
        Close();
    }

    private async void AlPedirImportar(object sender, RoutedEventArgs e)
    {
        var dialogo = new OpenFileDialog
        {
            Title = Textos.T("Libro.Vacio.DialogoTitulo"),
            Filter = Textos.T("Libro.Vacio.DialogoFiltro"),
            CheckFileExists = true,
        };

        // Se abre donde Log4OM deja sus respaldos: es el sitio mas probable y ahorra buscar.
        if (Directory.Exists(_modelo.CarpetaDeRespaldos))
        {
            dialogo.InitialDirectory = _modelo.CarpetaDeRespaldos;
        }

        if (dialogo.ShowDialog(this) != true) return;

        BotonImportar.IsEnabled = false;
        try
        {
            await _modelo.ImportarAsync(dialogo.FileName).ConfigureAwait(true);
        }
        finally
        {
            BotonImportar.IsEnabled = true;
        }
    }

    private void AlEmpezarDeCero(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
