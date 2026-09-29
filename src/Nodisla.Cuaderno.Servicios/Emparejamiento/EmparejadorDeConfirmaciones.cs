using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Servicios.Emparejamiento;

/// <summary>Una confirmacion y el contacto del cuaderno al que corresponde.</summary>
/// <param name="Qso">Contacto del cuaderno.</param>
/// <param name="Confirmacion">Confirmacion descargada del servicio.</param>
/// <param name="Desfase">Cuanto discrepan las horas. Cero es coincidencia exacta.</param>
public sealed record ParejaDeConfirmacion(
    Qso Qso, ConfirmacionDescargada Confirmacion, TimeSpan Desfase);

/// <summary>Resultado de emparejar un lote de confirmaciones contra el cuaderno.</summary>
/// <param name="Parejas">Confirmaciones que casaron.</param>
/// <param name="SinPareja">
/// Confirmaciones que el servicio da por buenas y que no casan con ningun contacto. No son
/// basura: casi siempre significan que el cuaderno tiene mal la hora, la banda o el modo.
/// </param>
public sealed record ResultadoDelEmparejamiento(
    IReadOnlyList<ParejaDeConfirmacion> Parejas,
    IReadOnlyList<ConfirmacionDescargada> SinPareja);

/// <summary>
/// Busca, para cada confirmacion descargada, el contacto del cuaderno al que se refiere.
/// </summary>
/// <remarks>
/// <para>
/// El criterio es el del propio LoTW y sirve igual para los demas: mismo indicativo, misma
/// banda, mismo modo o grupo de modo, y horas de inicio dentro de la ventana de tolerancia.
/// Cuando varios contactos caen dentro de la ventana se elige el mas cercano en el tiempo, que
/// es tambien lo que hace LoTW.
/// </para>
/// <para>
/// Un contacto ya emparejado no se reutiliza: dos confirmaciones distintas no pueden referirse
/// al mismo contacto, y si el servicio manda dos, la segunda sale en <c>SinPareja</c> para que
/// el operador vea que algo no cuadra.
/// </para>
/// </remarks>
public sealed class EmparejadorDeConfirmaciones
{
    private readonly ILogger _log;

    /// <summary>Crea el emparejador.</summary>
    /// <param name="log">Registro de trazas.</param>
    public EmparejadorDeConfirmaciones(ILogger<EmparejadorDeConfirmaciones>? log = null) =>
        _log = log ?? NullLogger<EmparejadorDeConfirmaciones>.Instance;

    /// <summary>Empareja las confirmaciones contra los contactos del cuaderno.</summary>
    /// <param name="qsos">Contactos candidatos del cuaderno.</param>
    /// <param name="confirmaciones">Confirmaciones descargadas.</param>
    /// <param name="tolerancia">
    /// Tolerancia a aplicar. Si es nula se toma la del medio de cada confirmacion.
    /// </param>
    public ResultadoDelEmparejamiento Emparejar(
        IReadOnlyList<Qso> qsos,
        IReadOnlyList<ConfirmacionDescargada> confirmaciones,
        ToleranciaDeEmparejamiento? tolerancia = null)
    {
        ArgumentNullException.ThrowIfNull(qsos);
        ArgumentNullException.ThrowIfNull(confirmaciones);

        // Indice por indicativo: sin el, un cuaderno de cien mil contactos obliga a recorrerlo
        // entero por cada confirmacion.
        var porIndicativo = new Dictionary<string, List<Qso>>(StringComparer.OrdinalIgnoreCase);
        foreach (var qso in qsos)
        {
            if (qso.Call.EsVacio) continue;
            if (!porIndicativo.TryGetValue(qso.Call.Valor, out var lista))
            {
                lista = [];
                porIndicativo[qso.Call.Valor] = lista;
            }
            lista.Add(qso);
        }

        var usados = new HashSet<long>();
        var usadosSinId = new HashSet<Qso>();
        var parejas = new List<ParejaDeConfirmacion>();
        var sinPareja = new List<ConfirmacionDescargada>();

        foreach (var confirmacion in confirmaciones)
        {
            var reglas = tolerancia ?? ToleranciaDeEmparejamiento.Para(confirmacion.Medio);
            var elegido = Elegir(porIndicativo, confirmacion, reglas, usados, usadosSinId);
            if (elegido is null)
            {
                sinPareja.Add(confirmacion);
                continue;
            }

            var (qso, desfase) = elegido.Value;
            if (qso.Id != 0) usados.Add(qso.Id);
            else usadosSinId.Add(qso);
            parejas.Add(new ParejaDeConfirmacion(qso, confirmacion, desfase));
        }

        if (sinPareja.Count > 0)
        {
            _log.LogInformation(
                "Emparejadas {Emparejadas} confirmaciones y {SinPareja} quedaron sin pareja.",
                parejas.Count, sinPareja.Count);
        }

        return new ResultadoDelEmparejamiento(parejas, sinPareja);
    }

    private static (Qso Qso, TimeSpan Desfase)? Elegir(
        Dictionary<string, List<Qso>> porIndicativo,
        ConfirmacionDescargada confirmacion,
        ToleranciaDeEmparejamiento reglas,
        HashSet<long> usados,
        HashSet<Qso> usadosSinId)
    {
        if (confirmacion.Call.EsVacio) return null;
        if (!porIndicativo.TryGetValue(confirmacion.Call.Valor, out var candidatos)) return null;

        Qso? mejor = null;
        var mejorDesfase = TimeSpan.MaxValue;

        foreach (var qso in candidatos)
        {
            if (qso.Id != 0 ? usados.Contains(qso.Id) : usadosSinId.Contains(qso)) continue;

            var desfase = (qso.InicioUtc - confirmacion.InicioUtc).Duration();
            if (desfase > reglas.Tiempo) continue;

            if (reglas.ExigirMismaBanda && !BandasCompatibles(qso, confirmacion)) continue;

            if (reglas.ExigirMismoModo
                && !ComparadorDeModos.Coinciden(qso.Mode, confirmacion.Mode, reglas.AdmitirMismoGrupoDeModo))
            {
                continue;
            }

            if (desfase < mejorDesfase)
            {
                mejor = qso;
                mejorDesfase = desfase;
            }
        }

        return mejor is null ? null : (mejor, mejorDesfase);
    }

    /// <summary>
    /// Una banda vacia en cualquiera de los dos lados no descarta: pasa cuando la frecuencia
    /// cae fuera de la tabla de ADIF y no hay banda que deducir, y en ese caso el indicativo,
    /// la hora y el modo ya son criterio suficiente.
    /// </summary>
    private static bool BandasCompatibles(Qso qso, ConfirmacionDescargada confirmacion) =>
        qso.Band.EsVacia || confirmacion.Band.EsVacia || qso.Band == confirmacion.Band;
}
