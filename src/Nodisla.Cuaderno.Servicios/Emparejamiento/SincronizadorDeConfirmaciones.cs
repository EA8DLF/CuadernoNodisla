using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Servicios.Emparejamiento;

/// <summary>
/// Junta las tres piezas de una sincronizacion: descargar del servicio, emparejar contra el
/// cuaderno y aplicar lo que mejore el estado de cada contacto.
/// </summary>
/// <remarks>
/// Vive aparte de los servicios para que todos se comporten igual y para poder probar el
/// recorrido completo sin red: los servicios solo saben hablar con su API, y quien decide que
/// se toca del cuaderno es siempre esta clase.
/// </remarks>
public sealed class SincronizadorDeConfirmaciones
{
    private readonly EmparejadorDeConfirmaciones _emparejador;
    private readonly ILogger _log;

    /// <summary>Crea el sincronizador.</summary>
    /// <param name="emparejador">Emparejador a usar.</param>
    /// <param name="log">Registro de trazas.</param>
    public SincronizadorDeConfirmaciones(
        EmparejadorDeConfirmaciones? emparejador = null,
        ILogger<SincronizadorDeConfirmaciones>? log = null)
    {
        _emparejador = emparejador ?? new EmparejadorDeConfirmaciones();
        _log = log ?? NullLogger<SincronizadorDeConfirmaciones>.Instance;
    }

    /// <summary>Contactos a los que la sincronizacion cambio algo.</summary>
    /// <remarks>Se expone aparte para que quien llama sepa exactamente que hay que guardar.</remarks>
    public IReadOnlyList<Qso> Modificados { get; private set; } = [];

    /// <summary>Descarga del servicio y aplica lo que proceda a los contactos indicados.</summary>
    /// <param name="servicio">Servicio del que descargar.</param>
    /// <param name="qsos">Contactos candidatos del cuaderno.</param>
    /// <param name="desdeUtc">Desde cuando pedir; nulo para pedirlo todo.</param>
    /// <param name="tolerancia">Tolerancia de emparejamiento; nula para la del servicio.</param>
    /// <param name="progreso">Avisos de avance, que se le pasan tal cual al servicio.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    public async Task<ResultadoDeSincronizacion> SincronizarAsync(
        IServicioQsl servicio,
        IReadOnlyList<Qso> qsos,
        DateTimeOffset? desdeUtc,
        ToleranciaDeEmparejamiento? tolerancia = null,
        IProgress<ProgresoDeSincronizacion>? progreso = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(servicio);
        ArgumentNullException.ThrowIfNull(qsos);

        var reloj = Stopwatch.StartNew();

        // Un servicio que no sabe descargar no se consulta: devolver cero confirmaciones como
        // si se hubiera mirado le haria creer al operador que no tiene nada nuevo.
        if (!servicio.PuedeDescargar)
        {
            _log.LogInformation(
                "{Servicio} no permite descargar confirmaciones; no se ha consultado.", servicio.Nombre);
            Modificados = [];
            return new ResultadoDeSincronizacion(0, 0, [], reloj.Elapsed);
        }

        var descargadas = await servicio.DescargarAsync(desdeUtc, progreso, ct).ConfigureAwait(false);
        var emparejamiento = _emparejador.Emparejar(qsos, descargadas, tolerancia);

        progreso?.Report(new ProgresoDeSincronizacion(
            servicio.Nombre, emparejamiento.Parejas.Count, descargadas.Count,
            Textos.T("Servicios.Emparejando")));

        var modificados = new List<Qso>();
        foreach (var pareja in emparejamiento.Parejas)
        {
            if (AplicadorDeConfirmaciones.Aplicar(pareja.Qso, pareja.Confirmacion))
            {
                pareja.Qso.ModificadoUtc = DateTimeOffset.UtcNow;
                modificados.Add(pareja.Qso);
            }
        }

        Modificados = modificados;
        reloj.Stop();

        _log.LogInformation(
            "{Servicio}: {Descargadas} confirmaciones, {Emparejadas} emparejadas, "
            + "{Modificados} contactos mejorados, {SinPareja} sin pareja en {Duracion}.",
            servicio.Nombre, descargadas.Count, emparejamiento.Parejas.Count,
            modificados.Count, emparejamiento.SinPareja.Count, reloj.Elapsed);

        return new ResultadoDeSincronizacion(
            descargadas.Count,
            emparejamiento.Parejas.Count,
            emparejamiento.SinPareja,
            reloj.Elapsed);
    }
}
