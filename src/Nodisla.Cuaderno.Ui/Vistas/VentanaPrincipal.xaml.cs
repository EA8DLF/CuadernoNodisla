using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Ui.Recursos;
using Nodisla.Cuaderno.Ui.VistaModelos;
using Serilog;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// Ventana principal del cuaderno. El codigo de aqui se limita a lo que es propio de la vista:
/// mover el foco, ordenar al pulsar una cabecera y preguntar antes de borrar.
/// </summary>
public partial class VentanaPrincipal : Window
{
    private readonly VistaModeloPrincipal _vistaModelo;
    private readonly CrearPerfilDeEstacion _perfiles;
    private readonly Func<VentanaDePrimerArranque> _ventanaDePrimerArranque;
    private bool _dibujadoComprobado;
    private bool _ajustandoComposicion;

    /// <summary>Monta la ventana con su modelo de vista.</summary>
    public VentanaPrincipal(
        VistaModeloPrincipal vistaModelo,
        CrearPerfilDeEstacion perfiles,
        Func<VentanaDePrimerArranque> ventanaDePrimerArranque)
    {
        ArgumentNullException.ThrowIfNull(vistaModelo);
        _vistaModelo = vistaModelo;
        _perfiles = perfiles;
        _ventanaDePrimerArranque = ventanaDePrimerArranque;

        InitializeComponent();
        DataContext = vistaModelo;

        _vistaModelo.Cuaderno.ConfirmarBorrado = PreguntarSiBorrar;
        ContentRendered += AlTerminarElPrimerDibujado;

        // Lo que cambia el alto que pide la zona de entrada obliga a repartir de nuevo.
        _vistaModelo.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(VistaModeloPrincipal.TamanoDeLetra)
                or nameof(VistaModeloPrincipal.EntradaPlegada)
                or nameof(VistaModeloPrincipal.DatosAmpliadosPedidos))
            {
                Dispatcher.BeginInvoke(AjustarComposicion, DispatcherPriority.Loaded);
            }
        };

        _vistaModelo.Entrada.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(VistaModeloEntradaQso.HayTrabajadoAntes)
                or nameof(VistaModeloEntradaQso.Tono))
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

            await _vistaModelo.InicializarAsync().ConfigureAwait(true);
            MarcarOrdenEnLasCabeceras();
            CampoIndicativo.Focus();
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
    private async Task<bool> PedirPerfilSiHaceFaltaAsync()
    {
        if (await _perfiles.HayAlgunoAsync().ConfigureAwait(true)) return true;

        Log.Information("No hay ningun perfil de estacion; se pide el del primer arranque.");

        var dialogo = _ventanaDePrimerArranque();
        dialogo.Owner = this;

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

    private void AlCerrar(object sender, EventArgs e) => _vistaModelo.Detener();

    private void AlCambiarDeTamano(object sender, SizeChangedEventArgs e) => AjustarComposicion();

    /// <summary>
    /// Reparte el alto de la ventana. La regla es que la zona de entrada nunca se corta a media
    /// fila: o un bloque se ve entero, o se pliega entero. Por eso no se mide con formulas sino
    /// con la composicion real: se enciende todo, se mide, y si al cuaderno no le queda sitio
    /// para sus contactos se va plegando por orden de menos util a mas util.
    /// </summary>
    /// <remarks>
    /// Orden de prioridad, de mas a menos: la primera fila de entrada —indicativo, banda, modo,
    /// frecuencia e informes— no se toca nunca; despues, tres contactos del cuaderno; y lo demas
    /// cede: primero la leyenda de atajos, luego la lista de contactos previos del aviso, luego
    /// la segunda fila de la entrada y por ultimo la cabecera, cuyo contenido —perfil, hora y
    /// escala— sigue disponible en la barra de estado y con Ctrl + «+» y Ctrl + «−».
    /// </remarks>
    private void AjustarComposicion()
    {
        if (_ajustandoComposicion) return;

        try
        {
            _ajustandoComposicion = true;

            _vistaModelo.OlvidarPliegueAutomatico();
            UpdateLayout();
            if (LeCabeAlCuaderno()) return;

            _vistaModelo.LeyendaSinSitio = true;
            UpdateLayout();
            if (LeCabeAlCuaderno()) return;

            _vistaModelo.ContactosPreviosSinSitio = true;
            UpdateLayout();
            if (LeCabeAlCuaderno()) return;

            _vistaModelo.DatosAmpliadosSinSitio = true;
            UpdateLayout();
            if (LeCabeAlCuaderno()) return;

            _vistaModelo.CabeceraSinSitio = true;
            UpdateLayout();
            if (LeCabeAlCuaderno()) return;

            _vistaModelo.AvisoCompacto = true;
            UpdateLayout();
        }
        finally
        {
            _ajustandoComposicion = false;
        }
    }

    /// <summary>
    /// El cuaderno tiene sitio para su cabecera y al menos tres contactos. Se mide en letras
    /// para que la cuenta siga valiendo con la escala al 200 %.
    /// </summary>
    private bool LeCabeAlCuaderno()
    {
        const double LetrasPorFila = 2.6;
        const double LetrasDeCabecera = 3.4;

        // Tres es el minimo que se exige; se pide uno mas para que el ultimo no quede a medias.
        const int ContactosMinimos = 4;

        var minimo = _vistaModelo.TamanoDeLetra * (LetrasDeCabecera + (ContactosMinimos * LetrasPorFila));
        return RejillaDelCuaderno.ActualHeight >= minimo;
    }

    /// <summary>Atajos que necesitan mover el foco, que es cosa de la ventana y no del modelo.</summary>
    private void AlTeclearEnLaVentana(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.F3:
                CampoBusqueda.Focus();
                CampoBusqueda.SelectAll();
                e.Handled = true;
                break;

            case Key.F4:
                CampoIndicativo.Focus();
                CampoIndicativo.SelectAll();
                e.Handled = true;
                break;
        }
    }

    private async void AlTeclearEnLaRejilla(object sender, KeyEventArgs e)
    {
        try
        {
            if (e.Key == Key.Delete)
            {
                e.Handled = true;
                await _vistaModelo.Cuaderno.EliminarSeleccionadoAsync().ConfigureAwait(true);
            }
            else if (e.Key == Key.Enter)
            {
                e.Handled = true;
                _vistaModelo.Cuaderno.EditarSeleccionado();
            }
        }
        catch (Exception ex)
        {
            MostrarFallo(ex);
        }
    }

    private void AlPulsarDosVeces(object sender, MouseButtonEventArgs e) =>
        _vistaModelo.Cuaderno.EditarSeleccionado();

    /// <summary>
    /// La rejilla solo tiene una pagina cargada, asi que ordenarla por su cuenta daria un
    /// resultado falso: se cancela y se vuelve a pedir el cuaderno ya ordenado.
    /// </summary>
    private async void AlOrdenar(object sender, DataGridSortingEventArgs e)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(e);
            e.Handled = true;

            // La cabecera lleva en SortMemberPath el nombre del campo de CampoDeOrden; una
            // columna sin nombre valido —o que no se puede ordenar— simplemente no ordena.
            if (!Enum.TryParse<CampoDeOrden>(e.Column.SortMemberPath, out var campo)) return;

            await _vistaModelo.Cuaderno.OrdenarPorCampoAsync(campo).ConfigureAwait(true);
            MarcarOrdenEnLasCabeceras();
        }
        catch (Exception ex)
        {
            MostrarFallo(ex);
        }
    }

    /// <summary>Pone la flecha de orden en la columna por la que se esta ordenando.</summary>
    private void MarcarOrdenEnLasCabeceras()
    {
        var campo = _vistaModelo.Cuaderno.OrdenarPor.ToString();
        var sentido = _vistaModelo.Cuaderno.Descendente
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;

        foreach (var columna in RejillaDelCuaderno.Columns)
        {
            columna.SortDirection = string.Equals(columna.SortMemberPath, campo, StringComparison.Ordinal)
                ? sentido
                : null;
        }
    }

    private bool PreguntarSiBorrar(FilaDeQso fila)
    {
        var dialogo = new VentanaDeConfirmacion
        {
            Owner = this,
            Titulo = "Borrar el contacto",
            Detalle = $"Se va a borrar el contacto con {fila.Indicativo} del {fila.Fecha} a las " +
                      $"{fila.Hora} UTC en {fila.Banda} {fila.Modo}.\n\nEsta operación no se puede deshacer.",
            TextoDeAceptar = "Sí, borrar el contacto",
        };

        return dialogo.ShowDialog() == true;
    }

    private void MostrarFallo(Exception ex)
    {
        var dialogo = new VentanaDeConfirmacion
        {
            Owner = this,
            Titulo = "Ha ocurrido un fallo",
            Detalle = ex.Message,
            TextoDeAceptar = "Entendido",
            SoloAviso = true,
        };
        dialogo.ShowDialog();
    }
}
