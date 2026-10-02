using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Idiomas;
using Serilog;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>
/// La franja solar: estado del Sol y del campo magnetico, y las horas de orto y ocaso.
/// </summary>
/// <remarks>
/// <para>
/// Es de lo que mas mira un operador antes de decidir en que banda llamar, y el programa ya lo
/// tenia calculado sin ensenarlo en ninguna parte. Va arriba del todo, en una linea, verde
/// sobre negro, porque asi se lee de reojo sin apartar la vista del dial.
/// </para>
/// <para>
/// <b>Los datos viejos se dicen.</b> El flujo solar se publica una vez al dia y el indice K
/// cada tres horas; si lo que hay es la copia guardada del disco o una medida de ayer, la
/// franja lo advierte. Ensenar indices de otro dia como si fueran de ahora lleva a llamar en
/// la banda equivocada.
/// </para>
/// </remarks>
public sealed partial class VistaModeloSolar : ObservableObject, IDisposable
{
    /// <summary>Cada cuanto se vuelve a preguntar por los indices.</summary>
    private static readonly TimeSpan CadaCuanto = TimeSpan.FromMinutes(20);

    private readonly IPropagacion _propagacion;
    private readonly System.Windows.Threading.DispatcherTimer _reloj;
    private Coordenada? _donde;
    private bool _liberado;

    /// <summary>Monta la franja y pide los indices.</summary>
    /// <param name="propagacion">Puerto de propagacion.</param>
    public VistaModeloSolar(IPropagacion propagacion)
    {
        _propagacion = propagacion ?? throw new ArgumentNullException(nameof(propagacion));
        _propagacion.IndicesActualizados += AlLlegarIndices;

        Recoger(_propagacion.Indices);

        // La calificacion del K y la procedencia se rehacen en el idioma nuevo.
        Textos.AlCambiar(this, static vm => vm.Recoger(vm._propagacion.Indices));

        _reloj = new System.Windows.Threading.DispatcherTimer { Interval = CadaCuanto };
        _reloj.Tick += (_, _) => ActualizarCommand.Execute(null);
        _reloj.Start();

        _ = ActualizarAsync();
    }

    /// <summary>Indice K de las ultimas tres horas, con su calificacion en palabras.</summary>
    [ObservableProperty]
    private string _indiceK = "—";

    /// <summary>Indice A del dia.</summary>
    [ObservableProperty]
    private string _indiceA = "—";

    /// <summary>Flujo solar a 10,7 cm.</summary>
    [ObservableProperty]
    private string _flujoSolar = "—";

    /// <summary>Numero de manchas solares.</summary>
    [ObservableProperty]
    private string _manchas = "—";

    /// <summary>Hora de salida del Sol en la estacion propia, en UTC.</summary>
    [ObservableProperty]
    private string _orto = "—";

    /// <summary>Hora de puesta del Sol en la estacion propia, en UTC.</summary>
    [ObservableProperty]
    private string _ocaso = "—";

    /// <summary>Hay tormenta geomagnetica en curso.</summary>
    [ObservableProperty]
    private bool _tormenta;

    /// <summary>Los datos no son de una descarga de ahora, sino de la copia guardada.</summary>
    [ObservableProperty]
    private bool _datosViejos;

    /// <summary>De donde salen los datos y de cuando son, para la ayuda emergente.</summary>
    [ObservableProperty]
    private string _procedencia = Textos.T("Principal.Solar.SinIndices");

    /// <summary>Se estan trayendo los indices ahora mismo.</summary>
    [ObservableProperty]
    private bool _trayendo;

    /// <summary>
    /// Dice donde esta la estacion, para poder calcular el orto y el ocaso.
    /// </summary>
    /// <param name="donde">Coordenada de la estacion propia, o nulo si no se sabe.</param>
    public void FijarEstacion(Coordenada? donde)
    {
        _donde = donde;
        RecogerElSol();
    }

    /// <summary>Vuelve a pedir los indices a la red.</summary>
    [RelayCommand]
    public async Task ActualizarAsync()
    {
        if (_liberado || Trayendo) return;

        Trayendo = true;
        try
        {
            var indices = await _propagacion.ActualizarIndicesAsync().ConfigureAwait(true);
            Recoger(indices ?? _propagacion.Indices);
        }
        catch (Exception ex)
        {
            // Sin red la franja se queda con lo ultimo que hubiera y lo dice. Que no haya
            // indices no puede dejar la ventana sin arrancar.
            Log.Debug(ex, "No se han podido traer los índices solares.");
            Recoger(_propagacion.Indices);
        }
        finally
        {
            Trayendo = false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_liberado) return;
        _liberado = true;

        _reloj.Stop();
        _propagacion.IndicesActualizados -= AlLlegarIndices;
    }

    private void AlLlegarIndices(object? origen, IndicesSolares indices) => Recoger(indices);

    private void Recoger(IndicesSolares? indices)
    {
        if (indices is null)
        {
            DatosViejos = true;
            Procedencia = Textos.T("Principal.Solar.SinIndices");
            return;
        }

        IndiceK = Numero(indices.IndiceK) is { } k ? $"{k} ({CalificarK(indices.IndiceK)})" : "—";
        IndiceA = Numero(indices.IndiceA) ?? "—";
        FlujoSolar = Numero(indices.FlujoSolar) ?? "—";
        Manchas = Numero(indices.ManchasSolares) ?? "—";
        Tormenta = indices.Tormenta;
        DatosViejos = indices.DeCache || DateTimeOffset.UtcNow - indices.MedidoUtc > TimeSpan.FromHours(26);

        Procedencia = indices.Descripcion.Length > 0
            ? indices.Descripcion
            : Textos.F("Principal.Solar.MedidoEl", indices.MedidoUtc.UtcDateTime.ToString("dd-MM-yyyy HH:mm", CultureInfo.InvariantCulture));

        RecogerElSol();
    }

    private void RecogerElSol()
    {
        if (_donde is not { } donde)
        {
            Orto = "—";
            Ocaso = "—";
            return;
        }

        var (orto, ocaso) = Aplicacion.CasosDeUso.SalidaYPuestaDelSol.Calcular(donde, DateTimeOffset.UtcNow);

        Orto = orto is { } o ? o.UtcDateTime.ToString("HH:mm", CultureInfo.InvariantCulture) + "Z" : "—";
        Ocaso = ocaso is { } c ? c.UtcDateTime.ToString("HH:mm", CultureInfo.InvariantCulture) + "Z" : "—";
    }

    private static string? Numero(double? valor) =>
        valor is { } v ? v.ToString("0.#", Textos.Cultura) : null;

    /// <summary>
    /// Pone en palabras el indice K, que es como se lee de verdad.
    /// </summary>
    /// <remarks>
    /// Un K de 2 y un K de 6 no son «dos» y «seis»: son «tranquilo» y «tormenta». La escala es
    /// la del servicio meteorologico espacial, y decirla en espanol ahorra tener que
    /// recordarla.
    /// </remarks>
    private static string CalificarK(double? k) => k switch
    {
        null => Textos.T("Principal.Solar.K.SinDato"),
        < 1 => Textos.T("Principal.Solar.K.EnCalma"),
        < 3 => Textos.T("Principal.Solar.K.Tranquilo"),
        < 4 => Textos.T("Principal.Solar.K.Movido"),
        < 5 => Textos.T("Principal.Solar.K.Inquieto"),
        < 6 => Textos.T("Principal.Solar.K.TormentaMenor"),
        < 7 => Textos.T("Principal.Solar.K.Tormenta"),
        _ => Textos.T("Principal.Solar.K.TormentaFuerte"),
    };
}
