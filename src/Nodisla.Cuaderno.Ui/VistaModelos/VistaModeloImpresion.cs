using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Impresion;
using Nodisla.Cuaderno.Impresion.Modelo;
using Nodisla.Cuaderno.Ui.Ajustes;
using Serilog;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>Una etiqueta resultante del filtro, con su casilla para marcarla o desmarcarla.</summary>
public sealed partial class FilaDeEtiqueta : ObservableObject
{
    /// <summary>Monta la fila sobre la etiqueta ya compuesta.</summary>
    /// <param name="etiqueta">Etiqueta con sus contactos.</param>
    public FilaDeEtiqueta(EtiquetaDeQsl etiqueta)
    {
        Etiqueta = etiqueta ?? throw new ArgumentNullException(nameof(etiqueta));
        Textos.AlCambiar(this, static f => f.OnPropertyChanged(string.Empty));
    }

    /// <summary>La etiqueta tal cual la compuso el modelo de impresion.</summary>
    public EtiquetaDeQsl Etiqueta { get; }

    /// <summary>A quien va.</summary>
    public string Encabezado => Etiqueta.Encabezado;

    /// <summary>Por donde va.</summary>
    public string ViaTexto => Etiqueta.TextoDeLaVia.Length == 0 ? Textos.T("Qsl.Impresion.SinVia") : Etiqueta.TextoDeLaVia;

    /// <summary>Cuantos contactos confirma, escrito.</summary>
    public string ContactosTexto => Etiqueta.Contactos.Count == 1
        ? Textos.T("Qsl.Impresion.UnContacto")
        : Textos.F("Qsl.Impresion.NContactos", Etiqueta.Contactos.Count);

    /// <summary>Se imprime esta etiqueta.</summary>
    [ObservableProperty]
    private bool _elegida = true;
}

/// <summary>
/// La pantalla de impresion de etiquetas de QSL: filtrar, elegir plantilla y ver el PDF de
/// verdad antes de gastar una hoja de etiquetas.
/// </summary>
/// <remarks>
/// <para>
/// <b>La vista previa es el PDF que se imprime, byte a byte.</b> No hay un dibujo aparte para
/// la pantalla: se generan los mismos bytes que <see cref="IGeneradorDeImpresos"/> compondria
/// para la impresora, se guardan en disco y se abren con el visor de PDF que tenga instalado
/// el sistema. Es la unica forma de que la vista previa signifique algo de verdad.
/// </para>
/// <para>
/// El filtro final lo aplica <see cref="SeleccionDeContactos"/>, el mismo que usa el motor de
/// impresion: banda, via, indicativo y pendientes de enviar se deciden en un solo sitio y no
/// se duplican aqui.
/// </para>
/// </remarks>
public sealed partial class VistaModeloImpresion : ObservableObject
{
    private readonly BuscarEnCuaderno _buscar;
    private readonly IGeneradorDeImpresos _generador;
    private readonly AjustesDelPrograma _ajustes;
    private readonly string _carpetaDeDatos;

    /// <summary>Monta la pantalla de impresion.</summary>
    /// <param name="buscar">Consulta del cuaderno, para traer los contactos que entran en el filtro.</param>
    /// <param name="generador">Quien compone el PDF de etiquetas.</param>
    /// <param name="ajustes">Ajustes del programa, para recordar la plantilla elegida.</param>
    /// <param name="carpetaDeDatos">Carpeta de datos, donde se guarda la vista previa.</param>
    public VistaModeloImpresion(
        BuscarEnCuaderno buscar,
        IGeneradorDeImpresos generador,
        AjustesDelPrograma ajustes,
        string carpetaDeDatos)
    {
        _buscar = buscar ?? throw new ArgumentNullException(nameof(buscar));
        _generador = generador ?? throw new ArgumentNullException(nameof(generador));
        _ajustes = ajustes ?? throw new ArgumentNullException(nameof(ajustes));
        ArgumentException.ThrowIfNullOrWhiteSpace(carpetaDeDatos);
        _carpetaDeDatos = carpetaDeDatos;

        // Sin propiedad de por medio: cargar lo guardado no tiene que disparar un guardado.
        _plantillaElegida = PlantillaDeEtiquetas.Conocidas
            .FirstOrDefault(p => string.Equals(p.Referencia, ajustes.Impresion.Plantilla, StringComparison.OrdinalIgnoreCase))
            ?? PlantillaDeEtiquetas.AveryL7160;
        _contorno = ajustes.Impresion.Contorno;
        _mensaje = ajustes.Impresion.Mensaje;
        _empezarEnLaEtiqueta = Math.Clamp(ajustes.Impresion.PrimeraCasilla + 1, 1, _plantillaElegida.PorHoja);
        Textos.AlCambiar(this, static vm => vm.OnPropertyChanged(nameof(ResumenTexto)));

        // Con CUADERNO_IMPRIMIR puesta se busca sola nada mas abrir la pantalla, para poder
        // capturarla con etiquetas de verdad sin tener que darle clics a la ventana del
        // operador. Es el mismo recurso que CUADERNO_PROBAR_CAT; en uso normal no esta puesta.
        if (Environment.GetEnvironmentVariable("CUADERNO_IMPRIMIR") is { Length: > 0 })
        {
            SoloPendientesDeEnviar = false;
            _ = BuscarAsync();
        }
    }

    /// <summary>
    /// Abre el PDF compuesto. Por omision, con el visor que tenga el sistema; las pruebas ponen
    /// otro que solo apunta la ruta, para no abrirle ventanas a nadie.
    /// </summary>
    public Action<string> AbrirDocumento { get; set; } =
        ruta => Process.Start(new ProcessStartInfo(ruta) { UseShellExecute = true });

    /// <summary>Perfil de estacion con el que se filtra y se completa lo que falte. Lo fija la ventana.</summary>
    public long? EstacionId { get; set; }

    /// <summary>El editor de la tarjeta QSL (subpestaña «Tarjeta QSL»), o nulo si no se registró.</summary>
    public VistaModeloQsl? Qsl { get; init; }

    /// <summary>
    /// Abrir en la subpestaña «Tarjeta QSL». Solo con <c>CUADERNO_QSL</c> puesta, para poder
    /// capturarla sin darle clics a la ventana del operador.
    /// </summary>
    public bool VerTarjetaQsl { get; } = Environment.GetEnvironmentVariable("CUADERNO_QSL") is { Length: > 0 };

    /// <summary>Indicativo propio, para las etiquetas cuyo contacto no lo traiga. Lo fija la ventana.</summary>
    public Indicativo MiIndicativo { get; set; } = Indicativo.Vacio;

    /// <summary>Localizador propio, con el mismo motivo. Lo fija la ventana.</summary>
    public Locator MiLocalizador { get; set; }

    /// <summary>Las plantillas de etiquetas que trae el programa.</summary>
    public IReadOnlyList<PlantillaDeEtiquetas> Plantillas => PlantillaDeEtiquetas.Conocidas;

    /// <summary>Las etiquetas que ha dejado el ultimo filtro.</summary>
    public ObservableCollection<FilaDeEtiqueta> Etiquetas { get; } = [];

    [ObservableProperty]
    private DateTime? _desde;

    [ObservableProperty]
    private DateTime? _hasta;

    [ObservableProperty]
    private string _bandas = string.Empty;

    [ObservableProperty]
    private string _indicativos = string.Empty;

    [ObservableProperty]
    private bool _viaBuro;

    [ObservableProperty]
    private bool _viaDirecto;

    [ObservableProperty]
    private bool _viaGestor;

    [ObservableProperty]
    private bool _viaElectronico;

    [ObservableProperty]
    private bool _soloPendientesDeEnviar = true;

    [ObservableProperty]
    private bool _excluirYaConfirmados;

    [ObservableProperty]
    private PlantillaDeEtiquetas _plantillaElegida;

    [ObservableProperty]
    private int _empezarEnLaEtiqueta;

    [ObservableProperty]
    private bool _contorno;

    [ObservableProperty]
    private string _mensaje = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuscarCommand))]
    private bool _buscando;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VistaPreviaCommand))]
    private bool _generando;

    [ObservableProperty]
    private string _aviso = string.Empty;

    [ObservableProperty]
    private int _contactosEncontrados;

    /// <summary>Cuantas etiquetas ha dejado el filtro y cuantas estan marcadas, en una linea.</summary>
    public string ResumenTexto => Etiquetas.Count == 0
        ? Textos.T("Qsl.Impresion.SinEtiquetas")
        : Textos.F("Qsl.Impresion.Resumen", Etiquetas.Count, ContactosEncontrados, Etiquetas.Count(e => e.Elegida));

    /// <summary>Busca en el cuaderno y compone las etiquetas segun el filtro.</summary>
    [RelayCommand(CanExecute = nameof(SePuedeBuscar))]
    public async Task BuscarAsync()
    {
        Buscando = true;
        Aviso = string.Empty;

        try
        {
            var criterio = new CriterioQso
            {
                EstacionId = EstacionId,
                DesdeUtc = Desde is { } desde ? new DateTimeOffset(DateTime.SpecifyKind(desde.Date, DateTimeKind.Utc)) : null,
                HastaUtc = Hasta is { } hasta
                    ? new DateTimeOffset(DateTime.SpecifyKind(hasta.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc))
                    : null,
            };

            var contactos = await TraerTodosAsync(criterio).ConfigureAwait(true);
            ContactosEncontrados = contactos.Count;

            var seleccion = new SeleccionDeContactos
            {
                DesdeUtc = criterio.DesdeUtc,
                HastaUtc = criterio.HastaUtc,
                SoloPendientesDeEnviar = SoloPendientesDeEnviar,
                ExcluirYaConfirmados = ExcluirYaConfirmados,
                Bandas = SepararLista(Bandas),
                Indicativos = SepararLista(Indicativos),
                Vias = ViasElegidas(),
            };

            var etiquetas = seleccion.Agrupar(contactos, MiIndicativo, MiLocalizador, Mensaje);

            foreach (var vieja in Etiquetas) vieja.PropertyChanged -= AlMarcarUnaEtiqueta;
            Etiquetas.Clear();
            foreach (var etiqueta in etiquetas)
            {
                var fila = new FilaDeEtiqueta(etiqueta);
                fila.PropertyChanged += AlMarcarUnaEtiqueta;
                Etiquetas.Add(fila);
            }

            AvisarDeLasMarcas();
            Aviso = Etiquetas.Count == 0
                ? Textos.T("Qsl.Impresion.NingunContacto")
                : string.Empty;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido buscar los contactos para imprimir.");
            Aviso = Textos.F("Qsl.Impresion.NoBuscar", ex.Message);
        }
        finally
        {
            Buscando = false;
        }
    }

    /// <summary>Marca todas las etiquetas encontradas.</summary>
    [RelayCommand(CanExecute = nameof(HayEtiquetas))]
    public void MarcarTodas()
    {
        foreach (var fila in Etiquetas) fila.Elegida = true;
        AvisarDeLasMarcas();
    }

    /// <summary>Desmarca todas las etiquetas encontradas.</summary>
    [RelayCommand(CanExecute = nameof(HayEtiquetas))]
    public void DesmarcarTodas()
    {
        foreach (var fila in Etiquetas) fila.Elegida = false;
        AvisarDeLasMarcas();
    }

    /// <summary>
    /// Una casilla de la lista ha cambiado: el resumen y la vista previa lo tienen que saber.
    /// </summary>
    /// <remarks>
    /// Antes solo se enteraban con «Marcar todas» y «Ninguna»: se desmarcaba una a mano y el
    /// resumen seguia contandola, y con todas desmarcadas a mano el boton seguia encendido.
    /// </remarks>
    private void AlMarcarUnaEtiqueta(object? origen, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FilaDeEtiqueta.Elegida)) AvisarDeLasMarcas();
    }

    private void AvisarDeLasMarcas()
    {
        OnPropertyChanged(nameof(ResumenTexto));
        VistaPreviaCommand.NotifyCanExecuteChanged();
        MarcarTodasCommand.NotifyCanExecuteChanged();
        DesmarcarTodasCommand.NotifyCanExecuteChanged();
    }

    private bool HayEtiquetas() => Etiquetas.Count > 0;

    /// <summary>
    /// Compone el PDF con las etiquetas marcadas y lo abre con el visor del sistema.
    /// </summary>
    /// <remarks>
    /// Son los mismos bytes que se mandarian a la impresora: no hay una vista previa aparte.
    /// Imprimir de verdad es cosa del visor que se abra —Edge, Acrobat, el que tenga
    /// instalado el operador—, que ya sabe imprimir un PDF.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(SePuedeVerLaVistaPrevia))]
    public async Task VistaPreviaAsync()
    {
        // La frase al pie es la que hay AHORA en la casilla, no la que habia al buscar: antes
        // se cambiaba la frase despues de «Buscar» y el PDF salia con la vieja.
        var pie = string.IsNullOrWhiteSpace(Mensaje) ? null : Mensaje.Trim();
        var elegidas = Etiquetas
            .Where(f => f.Elegida)
            .Select(f => f.Etiqueta with { Mensaje = pie })
            .ToList();
        if (elegidas.Count == 0)
        {
            Aviso = Textos.T("Qsl.Impresion.NingunaMarcada");
            return;
        }

        Generando = true;
        Aviso = string.Empty;

        try
        {
            var opciones = new OpcionesDeImpresion
            {
                DibujarContorno = Contorno,
                PrimeraCasilla = Math.Clamp(EmpezarEnLaEtiqueta - 1, 0, PlantillaElegida.PorHoja - 1),
            };

            var impreso = _generador.Etiquetas(elegidas, PlantillaElegida, opciones);

            var carpeta = Path.Combine(_carpetaDeDatos, "impresion");
            Directory.CreateDirectory(carpeta);
            var ruta = Path.Combine(carpeta, "vista-previa-etiquetas.pdf");
            await File.WriteAllBytesAsync(ruta, impreso.Bytes).ConfigureAwait(true);

            AbrirDocumento(ruta);

            Aviso = Textos.F("Qsl.Impresion.VistaAbierta", impreso.Paginas, elegidas.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido componer o abrir la vista previa de etiquetas.");
            Aviso = Textos.F("Qsl.Impresion.NoVistaPrevia", ex.Message);
        }
        finally
        {
            Generando = false;
        }
    }

    partial void OnPlantillaElegidaChanged(PlantillaDeEtiquetas value)
    {
        EmpezarEnLaEtiqueta = Math.Clamp(EmpezarEnLaEtiqueta, 1, value.PorHoja);
        _ajustes.Impresion.Plantilla = value.Referencia;
        _ajustes.Guardar(_carpetaDeDatos);
    }

    partial void OnEmpezarEnLaEtiquetaChanged(int value)
    {
        // Lo que se ve es lo que se usa: un 0 o un 99 en una hoja de 21 se corrige en la casilla.
        var acotado = Math.Clamp(value, 1, PlantillaElegida.PorHoja);
        if (acotado != value)
        {
            EmpezarEnLaEtiqueta = acotado;
            return;
        }

        _ajustes.Impresion.PrimeraCasilla = Math.Clamp(value - 1, 0, PlantillaElegida.PorHoja - 1);
        _ajustes.Guardar(_carpetaDeDatos);
    }

    partial void OnContornoChanged(bool value)
    {
        _ajustes.Impresion.Contorno = value;
        _ajustes.Guardar(_carpetaDeDatos);
    }

    partial void OnMensajeChanged(string value)
    {
        _ajustes.Impresion.Mensaje = value;
        _ajustes.Guardar(_carpetaDeDatos);
    }

    private bool SePuedeBuscar() => !Buscando;

    private bool SePuedeVerLaVistaPrevia() => !Generando && Etiquetas.Any(f => f.Elegida);

    private async Task<List<Qso>> TraerTodosAsync(CriterioQso criterio)
    {
        var todos = new List<Qso>();
        var desplazamiento = 0;

        while (true)
        {
            var pagina = await _buscar
                .EjecutarAsync(criterio, desplazamiento, BuscarEnCuaderno.LimiteMaximo)
                .ConfigureAwait(true);

            todos.AddRange(pagina.Elementos);

            if (pagina.Elementos.Count < BuscarEnCuaderno.LimiteMaximo || todos.Count >= pagina.TotalFiltrado)
            {
                break;
            }

            desplazamiento += BuscarEnCuaderno.LimiteMaximo;
        }

        return todos;
    }

    private IReadOnlyCollection<ViaDeEnvio> ViasElegidas()
    {
        var vias = new List<ViaDeEnvio>(4);
        if (ViaBuro) vias.Add(ViaDeEnvio.Buro);
        if (ViaDirecto) vias.Add(ViaDeEnvio.Directo);
        if (ViaGestor) vias.Add(ViaDeEnvio.Gestor);
        if (ViaElectronico) vias.Add(ViaDeEnvio.Electronico);
        return vias;
    }

    private static IReadOnlyCollection<string> SepararLista(string texto) =>
        string.IsNullOrWhiteSpace(texto)
            ? []
            : texto.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
}
