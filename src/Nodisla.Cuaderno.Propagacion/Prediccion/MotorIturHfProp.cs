using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Propagacion.Prediccion;

/// <summary>
/// Motor de prediccion de verdad: lanza ITURHFProp, la implementacion de referencia de la
/// Recomendacion UIT-R P.533 del Grupo de Estudio 3 de la UIT, y lee su informe.
/// </summary>
/// <remarks>
/// <para>
/// El programa vive en <c>herramientas\ITURHFProp</c>, compilado desde sus fuentes con el
/// Visual Studio de esta maquina y validado contra los nueve casos de referencia que publica la
/// UIT: los reproduce identicos. El LEEME de esa carpeta cuenta como rehacerlo.
/// </para>
/// <para>
/// <b>Lo que no cubre, y por que importa.</b> El modelo solo sabe de 1,6 a 30 MHz, asi que
/// 6 metros se le sale del rango. Ademas es un proceso externo: puede no estar, tardar de mas o
/// terminar mal. En todos esos casos <b>esas bandas caen a la aproximacion propia</b> y salen
/// marcadas con <see cref="PrediccionDeBanda.EsAproximacion"/> en cierto y con el nombre del
/// motor que las calculo de verdad. Una prediccion de respaldo ensenada como si fuera P.533
/// seria justo el engano que este modulo tiene prohibido.
/// </para>
/// </remarks>
public sealed class MotorIturHfProp : IMotorDePrediccion
{
    /// <summary>Nombre con el que se identifica este motor en pantalla.</summary>
    public static string NombreDelMotor => Textos.T("Servicios.Propagacion.MotorIturHfProp");

    /// <summary>Ruta del ejecutable dentro del proyecto, relativa a la raiz.</summary>
    /// <remarks>
    /// La carpeta se llama <c>programa</c> y no <c>bin</c> a proposito: el <c>.gitignore</c> de la
    /// raiz ignora <c>bin/</c> para no guardar compilados de .NET, y con ese nombre el motor se
    /// quedaria fuera del repositorio sin que nadie se diera cuenta hasta clonarlo en otra maquina.
    /// </remarks>
    public const string RutaRelativa = @"herramientas\ITURHFProp\programa\ITURHFProp.exe";

    private readonly OpcionesPropagacion ajustes;
    private readonly IMotorDePrediccion respaldo;
    private readonly ILogger traza;

    /// <summary>Crea el motor sobre un ejecutable concreto.</summary>
    /// <param name="rutaDelEjecutable">Ejecutable de ITURHFProp.</param>
    /// <param name="rutaDeLosDatos">Carpeta con los mapas ionosfericos y los coeficientes.</param>
    /// <param name="opciones">Ajustes de antena, ruido y exigencia de senal.</param>
    /// <param name="respaldo">Motor al que caer cuando este no puede. Por omision, la aproximacion propia.</param>
    /// <param name="registro">Registro opcional.</param>
    public MotorIturHfProp(
        string rutaDelEjecutable,
        string rutaDeLosDatos,
        OpcionesPropagacion? opciones = null,
        IMotorDePrediccion? respaldo = null,
        ILogger<MotorIturHfProp>? registro = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rutaDelEjecutable);
        ArgumentException.ThrowIfNullOrWhiteSpace(rutaDeLosDatos);

        RutaDelEjecutable = Path.GetFullPath(rutaDelEjecutable);
        RutaDeLosDatos = Path.GetFullPath(rutaDeLosDatos);
        ajustes = opciones ?? new OpcionesPropagacion();
        this.respaldo = respaldo ?? new MotorAproximacionNodisla(ajustes);
        traza = registro ?? NullLogger<MotorIturHfProp>.Instance;
    }

    /// <summary>Ejecutable que se lanza.</summary>
    public string RutaDelEjecutable { get; }

    /// <summary>Carpeta de los mapas ionosfericos y los coeficientes.</summary>
    public string RutaDeLosDatos { get; }

    /// <inheritdoc/>
    public string Nombre => NombreDelMotor;

    /// <summary>
    /// Falso: este motor calcula de verdad. Aun asi, cada prediccion lleva su propia bandera,
    /// porque las bandas que caigan al respaldo <b>si</b> son aproximacion.
    /// </summary>
    public bool EsAproximacion => false;

    /// <summary>
    /// Busca el ejecutable dentro del proyecto y devuelve el motor, o nulo si no esta instalado.
    /// </summary>
    /// <param name="opciones">Ajustes; si traen ruta del motor externo, manda esa.</param>
    /// <param name="respaldo">Motor al que caer. Por omision, la aproximacion propia.</param>
    /// <param name="registro">Registro opcional.</param>
    /// <remarks>
    /// Se busca subiendo desde la carpeta de la aplicacion, que es donde acaba estando cuando se
    /// ejecuta desde <c>bin\Debug</c> durante el desarrollo.
    /// </remarks>
    public static MotorIturHfProp? Localizar(
        OpcionesPropagacion? opciones = null,
        IMotorDePrediccion? respaldo = null,
        ILogger<MotorIturHfProp>? registro = null)
    {
        var ajustes = opciones ?? new OpcionesPropagacion();
        if (!ajustes.UsarMotorExterno)
        {
            return null;
        }

        var ejecutable = ajustes.RutaDelMotorExterno;

        if (string.IsNullOrWhiteSpace(ejecutable) || !File.Exists(ejecutable))
        {
            ejecutable = BuscarHaciaArriba();
        }

        if (ejecutable is null || !File.Exists(ejecutable))
        {
            return null;
        }

        var datos = CarpetaDeDatos(ejecutable);
        return datos is null
            ? null
            : new MotorIturHfProp(ejecutable, datos, ajustes, respaldo, registro);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PrediccionDeBanda>> PredecirAsync(
        SolicitudDePrediccion solicitud,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);

        // El respaldo se calcula siempre y primero: es barato, y asi hay red debajo pase lo que
        // pase con el proceso externo.
        var deRespaldo = await respaldo.PredecirAsync(solicitud, ct).ConfigureAwait(false);

        var enRango = BandasEnRango();
        if (enRango.Count == 0)
        {
            return deRespaldo;
        }

        var condiciones = MotorAproximacionNodisla.CondicionesDesde(solicitud.Indices);
        var filas = await EjecutarAsync(solicitud, enRango, condiciones.ManchasSolares, ct)
            .ConfigureAwait(false);

        if (filas.Count == 0)
        {
            traza.LogWarning(
                "ITURHFProp no ha devuelto nada utilizable; las {Bandas} bandas salen de la aproximación.",
                deRespaldo.Count);
            return deRespaldo;
        }

        return Mezclar(deRespaldo, filas);
    }

    /// <summary>
    /// Junta lo que ha calculado el modelo con lo que queda del respaldo, banda por banda.
    /// </summary>
    private IReadOnlyList<PrediccionDeBanda> Mezclar(
        IReadOnlyList<PrediccionDeBanda> deRespaldo,
        IReadOnlyList<FilaIturHfProp> filas)
    {
        var resultado = new List<PrediccionDeBanda>(deRespaldo.Count);
        foreach (var prevision in deRespaldo)
        {
            var frecuencia = FrecuenciaDe(prevision.Banda);
            var fila = frecuencia is null
                ? null
                : filas.FirstOrDefault(f => Math.Abs(f.FrecuenciaMhz - frecuencia.Value) < 0.005);

            if (fila is null)
            {
                // Esta banda no la ha calculado el modelo: se queda la del respaldo, que ya viene
                // marcada como aproximacion por quien la hizo.
                resultado.Add(prevision);
                continue;
            }

            // La misma regla que se aplica a la aproximacion propia: una relacion senal-ruido de
            // ciento ochenta decibelios negativos no es una senal debil, es que no hay circuito, y
            // ensenar el numero invita a compararlo con el de al lado como si significara algo.
            // Vale para cualquier motor, tambien para el de la UIT.
            var hayAlgoQueDecir = fila.RelacionSenalRuidoDb > ModeloMufLuf.RelacionSinSentidoDb;

            resultado.Add(new PrediccionDeBanda(
                prevision.Banda,
                prevision.HoraUtc,
                fila.FiabilidadBasica,
                hayAlgoQueDecir ? fila.PotenciaRecibidaDbw : null,
                hayAlgoQueDecir ? fila.RelacionSenalRuidoDb : null,
                fila.Saltos ?? prevision.Saltos)
            {
                SinDatosSolares = prevision.SinDatosSolares,
                Motor = NombreDelMotor,
                EsAproximacion = false,
            });
        }

        return resultado;
    }

    /// <summary>Lanza el proceso y lee su informe. Devuelve vacio si algo sale mal.</summary>
    private async Task<IReadOnlyList<FilaIturHfProp>> EjecutarAsync(
        SolicitudDePrediccion solicitud,
        IReadOnlyList<double> frecuencias,
        double manchas,
        CancellationToken ct)
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "nodisla-p533-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(carpeta);
            var entrada = Path.Combine(carpeta, "trayecto.in");
            var salida = Path.Combine(carpeta, "trayecto.out");

            await File.WriteAllTextAsync(
                entrada,
                EntradaIturHfProp.Componer(
                    solicitud.Origen,
                    solicitud.Destino,
                    solicitud.MomentoUtc,
                    frecuencias,
                    manchas,
                    solicitud.PotenciaVatios,
                    ajustes,
                    RutaDeLosDatos),
                ct).ConfigureAwait(false);

            var arranque = new ProcessStartInfo(RutaDelEjecutable)
            {
                // El ejecutable carga P533.dll y P372.dll por nombre: tiene que correr en su casa.
                WorkingDirectory = Path.GetDirectoryName(RutaDelEjecutable)!,
                UseShellExecute = false,
                // Nada de ventanas: el operador esta trabajando en este ordenador.
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            arranque.ArgumentList.Add(entrada);
            arranque.ArgumentList.Add(salida);

            using var proceso = Process.Start(arranque);
            if (proceso is null)
            {
                traza.LogWarning("No se pudo arrancar {Ejecutable}.", RutaDelEjecutable);
                return [];
            }

            using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limite.CancelAfter(ajustes.EsperaDelMotorExterno);

            try
            {
                // Hay que vaciar las salidas o el proceso se bloquea al llenarse la tuberia.
                var traza1 = proceso.StandardOutput.ReadToEndAsync(limite.Token);
                var traza2 = proceso.StandardError.ReadToEndAsync(limite.Token);
                await proceso.WaitForExitAsync(limite.Token).ConfigureAwait(false);
                await Task.WhenAll(traza1, traza2).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                MatarSinRuido(proceso);
                traza.LogWarning(
                    "ITURHFProp ha tardado más de {Espera} y se ha cortado.",
                    ajustes.EsperaDelMotorExterno);
                return [];
            }

            if (proceso.ExitCode != 0)
            {
                traza.LogWarning("ITURHFProp ha terminado con código {Codigo}.", proceso.ExitCode);
                return [];
            }

            if (!File.Exists(salida))
            {
                traza.LogWarning("ITURHFProp no ha dejado informe en {Salida}.", salida);
                return [];
            }

            var informe = await File.ReadAllTextAsync(salida, ct).ConfigureAwait(false);
            var filas = InformeIturHfProp.Leer(informe);
            if (filas.Count == 0)
            {
                traza.LogWarning("El informe de ITURHFProp no se ha podido interpretar.");
            }

            return filas;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or System.ComponentModel.Win32Exception
                                       or InvalidOperationException)
        {
            traza.LogWarning(ex, "Ha fallado la llamada a ITURHFProp; se usará la aproximación.");
            return [];
        }
        finally
        {
            try
            {
                if (Directory.Exists(carpeta))
                {
                    Directory.Delete(carpeta, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                traza.LogDebug(ex, "No se pudo borrar la carpeta temporal {Carpeta}.", carpeta);
            }
        }
    }

    private static void MatarSinRuido(Process proceso)
    {
        try
        {
            if (!proceso.HasExited)
            {
                proceso.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception
                                       or NotSupportedException)
        {
            // Si ya se ha muerto solo, no hay nada que hacer.
        }
    }

    /// <summary>Frecuencias de las bandas que el modelo sabe calcular.</summary>
    private static List<double> BandasEnRango() =>
        BandasDeTrabajo.Bandas
            .Select(b => b.FrecuenciaMhz)
            .Where(EntradaIturHfProp.EstaEnRango)
            .ToList();

    private static double? FrecuenciaDe(Banda banda)
    {
        foreach (var (candidata, frecuencia) in BandasDeTrabajo.Bandas)
        {
            if (candidata == banda)
            {
                return EntradaIturHfProp.EstaEnRango(frecuencia) ? frecuencia : null;
            }
        }

        return null;
    }

    /// <summary>Busca el ejecutable subiendo desde la carpeta de la aplicacion.</summary>
    private static string? BuscarHaciaArriba()
    {
        var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
        for (var saltos = 0; carpeta is not null && saltos < 8; saltos++, carpeta = carpeta.Parent)
        {
            var candidato = Path.Combine(carpeta.FullName, RutaRelativa);
            if (File.Exists(candidato))
            {
                return candidato;
            }
        }

        return null;
    }

    /// <summary>Carpeta de datos que corresponde a un ejecutable dado.</summary>
    private static string? CarpetaDeDatos(string ejecutable)
    {
        var bin = Path.GetDirectoryName(Path.GetFullPath(ejecutable));
        var raiz = bin is null ? null : Path.GetDirectoryName(bin);
        if (raiz is null)
        {
            return null;
        }

        var datos = Path.Combine(raiz, "datos");
        return Directory.Exists(datos) ? datos : null;
    }

    /// <summary>Texto para el operador con lo que se ha encontrado instalado.</summary>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{NombreDelMotor} en {RutaDelEjecutable}");
}
