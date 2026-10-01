using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Ui.Desarrollo;

/// <summary>
/// Consulta de indicativos de mentira para los puertos simulados: nunca sale a la red.
/// </summary>
/// <remarks>
/// Con <c>CUADERNO_SIMULADO</c> no se puede usar QRZ.com de verdad: se estaria consultando con
/// las credenciales del operador desde una sesion de pruebas. Esta da una ficha inventada, y
/// lo dice en el nombre, para que el relleno del formulario se pueda ver y capturar.
/// </remarks>
public sealed class ConsultaIndicativoSimulada : IConsultaIndicativo
{
    /// <inheritdoc />
    public string Nombre => "QRZ.com (simulado)";

    /// <inheritdoc />
    public bool EstaDisponible => true;

    /// <inheritdoc />
    public Task<FichaIndicativo?> ConsultarAsync(Indicativo indicativo, CancellationToken ct = default)
    {
        if (indicativo.EsVacio) return Task.FromResult<FichaIndicativo?>(null);
        return Task.FromResult<FichaIndicativo?>(new FichaIndicativo
        {
            Indicativo = indicativo,
            Nombre = "Operador simulado",
            Localidad = "Ciudad simulada",
            Localizador = Locator.TryParse("JN11ab", out var l) ? l : Locator.Vacio,
            Pais = "Simulado",
            UsaLotw = true,
            UsaEqsl = false,
            Aviso = "Ficha inventada: sesión con puertos simulados.",
            Fuente = Nombre,
        });
    }
}

/// <summary>
/// Servicio de confirmacion de mentira: acepta todo sin tocar la red.
/// </summary>
/// <remarks>
/// Con los puertos simulados el cuaderno es de demostracion: subirlo a las cuentas de verdad
/// del operador seria un desastre. LoTW se simula sin TQSL a proposito, para que se vea la
/// pastilla roja con su motivo.
/// </remarks>
public sealed class ServicioQslSimulado(MedioDeConfirmacion medio, string nombre, bool puedeSubir = true) : IServicioQsl
{
    /// <inheritdoc />
    public MedioDeConfirmacion Medio => medio;

    /// <inheritdoc />
    public string Nombre => nombre;

    /// <inheritdoc />
    public bool EstaConfigurado => true;

    /// <inheritdoc />
    public bool PuedeSubir => puedeSubir;

    /// <inheritdoc />
    public bool PuedeDescargar => false;

    /// <inheritdoc />
    public Task<bool> ComprobarCredencialesAsync(CancellationToken ct = default) => Task.FromResult(true);

    /// <inheritdoc />
    public Task<ResultadoDeSubida> SubirAsync(
        IReadOnlyList<Qso> qsos,
        IProgress<ProgresoDeSincronizacion>? progreso = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(qsos);
        return Task.FromResult(new ResultadoDeSubida(
            qsos.Count, 0, new Dictionary<string, string>(), TimeSpan.Zero));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ConfirmacionDescargada>> DescargarAsync(
        DateTimeOffset? desdeUtc,
        IProgress<ProgresoDeSincronizacion>? progreso = null,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ConfirmacionDescargada>>([]);

    /// <summary>Los cuatro de la barra, de mentira.</summary>
    public static IReadOnlyList<IServicioQsl> Todos { get; } =
    [
        new ServicioQslSimulado(MedioDeConfirmacion.Lotw, "LoTW (simulado)", puedeSubir: false),
        new ServicioQslSimulado(MedioDeConfirmacion.Eqsl, "eQSL (simulado)"),
        new ServicioQslSimulado(MedioDeConfirmacion.ClubLog, "Club Log (simulado)"),
        new ServicioQslSimulado(MedioDeConfirmacion.QrzCom, "QRZ.com (simulado)"),
    ];
}
