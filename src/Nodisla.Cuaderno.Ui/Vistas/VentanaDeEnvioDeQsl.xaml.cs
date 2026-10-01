using System.Windows;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>Ver y enviar la QSL de unos contactos.</summary>
public partial class VentanaDeEnvioDeQsl : Window
{
    /// <summary>Crea la ventana.</summary>
    public VentanaDeEnvioDeQsl() => InitializeComponent();

    /// <summary>Abre la ventana con estos contactos y espera a que se cierre.</summary>
    /// <param name="dueña">Ventana sobre la que se abre.</param>
    /// <param name="editor">El editor de QSL, que sabe crear el envio.</param>
    /// <param name="ids">Los contactos.</param>
    /// <returns>El modelo, para saber que se ha mandado.</returns>
    public static VistaModeloEnvioQsl Mostrar(Window? dueña, VistaModeloQsl editor, IReadOnlyList<long> ids)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(ids);
        var modelo = editor.CrearEnvio(ids);
        var ventana = new VentanaDeEnvioDeQsl { DataContext = modelo };
        if (dueña is { IsLoaded: true }) ventana.Owner = dueña;

        // Se carga mientras se ve la ventana: buscar el correo en QRZ puede tardar un poco.
        ventana.Loaded += async (_, _) => await modelo.CargarAsync().ConfigureAwait(true);
        ventana.ShowDialog();
        return modelo;
    }

    private void AlCerrar(object sender, RoutedEventArgs e)
    {
        if (DataContext is VistaModeloEnvioQsl { Ocupado: true } modelo) modelo.CancelarCommand.Execute(null);
        Close();
    }
}
