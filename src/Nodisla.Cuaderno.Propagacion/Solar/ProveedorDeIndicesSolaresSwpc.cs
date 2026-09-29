using System.Net.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;

namespace Nodisla.Cuaderno.Propagacion.Solar;

/// <summary>
/// Trae los indices solares de los boletines del Space Weather Prediction Center del NOAA.
/// </summary>
/// <remarks>
/// <para>
/// Se usan cuatro boletines, todos publicos y sin clave: <c>wwv.txt</c> para el flujo solar y los
/// indices A y K, <c>daily-solar-indices.txt</c> para las manchas y el flujo con fecha completa,
/// y los dos resumenes del viento solar para la velocidad y la componente Bz. Si <c>wwv.txt</c>
/// falla se recurre a <c>daily-geomagnetic-indices.txt</c> para no quedarse sin indice A.
/// </para>
/// <para>
/// Cada valor lleva su propia hora de medida. La que se publica en <see cref="IndicesSolares"/>
/// es la <b>mas antigua</b> de las que componen la lectura, nunca la mas reciente: mas vale que
/// el operador crea que los datos son mas viejos de lo que son que al reves.
/// </para>
/// </remarks>
/// <param name="fabrica">Fabrica de clientes HTTP.</param>
/// <param name="opciones">Ajustes de red y de cache.</param>
/// <param name="registro">Registro opcional.</param>
public sealed class ProveedorDeIndicesSolaresSwpc(
    IHttpClientFactory fabrica,
    OpcionesPropagacion? opciones = null,
    ILogger<ProveedorDeIndicesSolaresSwpc>? registro = null)
{
    /// <summary>Nombre de la fuente, tal como se ensena al operador.</summary>
    public const string NombreDeLaFuente = "NOAA SWPC";

    private readonly OpcionesPropagacion ajustes = opciones ?? new OpcionesPropagacion();
    private readonly ILogger traza = registro ?? NullLogger<ProveedorDeIndicesSolaresSwpc>.Instance;

    /// <summary>Trae los indices de la red.</summary>
    /// <param name="ahoraUtc">Momento actual, que se apunta como hora de obtencion.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>Los indices, o nulo si no se pudo sacar ni un solo valor.</returns>
    public async Task<LecturaDeIndices?> TraerAsync(DateTimeOffset ahoraUtc, CancellationToken ct = default)
    {
        var boletin = DescargarAsync(FuentesSwpc.UrlBoletinGeofisico, ct);
        var diarios = DescargarAsync(FuentesSwpc.UrlIndicesSolaresDiarios, ct);
        var viento = DescargarAsync(FuentesSwpc.UrlVientoSolar, ct);
        var campo = DescargarAsync(FuentesSwpc.UrlCampoMagnetico, ct);
        await Task.WhenAll(boletin, diarios, viento, campo).ConfigureAwait(false);

        var wwv = FuentesSwpc.LeerBoletinGeofisico(await boletin.ConfigureAwait(false));
        var solarDiario = FuentesSwpc.LeerIndicesSolaresDiarios(await diarios.ConfigureAwait(false));
        var vientoSolar = FuentesSwpc.LeerVientoSolar(await viento.ConfigureAwait(false));
        var bz = FuentesSwpc.LeerCampoBz(await campo.ConfigureAwait(false));

        LecturaInstantanea? aDeRespaldo = null;
        if (wwv is null)
        {
            traza.LogWarning("El boletin geofisico del SWPC no se pudo leer; se prueba con los indices diarios.");
            var geomagneticos = await DescargarAsync(FuentesSwpc.UrlIndicesGeomagneticosDiarios, ct)
                .ConfigureAwait(false);
            aDeRespaldo = FuentesSwpc.LeerIndiceAPlanetario(geomagneticos);
        }

        var indices = Componer(wwv, solarDiario, vientoSolar, bz, aDeRespaldo);
        if (indices is null)
        {
            traza.LogWarning("No se pudo traer ningun indice solar del SWPC.");
            return null;
        }

        return new LecturaDeIndices(indices, ahoraUtc, OrigenDeLosIndices.Red, NombreDeLaFuente);
    }

    /// <summary>
    /// Junta lo que haya llegado de cada boletin en un solo juego de indices.
    /// </summary>
    /// <param name="wwv">Boletin geofisico.</param>
    /// <param name="solarDiario">Ultima fila de los indices solares diarios.</param>
    /// <param name="viento">Velocidad del viento solar.</param>
    /// <param name="bz">Componente Bz del campo interplanetario.</param>
    /// <param name="aDeRespaldo">Indice A sacado de los indices geomagneticos diarios.</param>
    /// <returns>Los indices, o nulo si no hay ni un valor.</returns>
    public static IndicesSolares? Componer(
        LecturaWwv? wwv,
        LecturaDiariaSolar? solarDiario,
        LecturaInstantanea? viento,
        LecturaInstantanea? bz,
        LecturaInstantanea? aDeRespaldo = null)
    {
        var momentos = new List<DateTimeOffset>();

        // El flujo diario trae fecha completa, asi que manda sobre el del boletin, que nombra el
        // dia sin ano.
        double? flujo = null;
        if (solarDiario?.FlujoSolar is not null)
        {
            flujo = solarDiario.FlujoSolar;
            momentos.Add(solarDiario.DiaUtc.AddHours(20));
        }
        else if (wwv?.FlujoSolar is not null)
        {
            flujo = wwv.FlujoSolar;
            momentos.Add(wwv.FlujoMedidoUtc ?? wwv.EmitidoUtc);
        }

        double? manchas = null;
        if (solarDiario?.Manchas is not null)
        {
            manchas = solarDiario.Manchas;
            momentos.Add(solarDiario.DiaUtc.AddHours(20));
        }

        double? indiceA = null;
        if (wwv?.IndiceA is not null)
        {
            indiceA = wwv.IndiceA;
            momentos.Add(wwv.AMedidoUtc ?? wwv.EmitidoUtc);
        }
        else if (aDeRespaldo?.Valor is not null)
        {
            indiceA = aDeRespaldo.Valor;
            momentos.Add(aDeRespaldo.MedidoUtc);
        }

        double? indiceK = null;
        if (wwv?.IndiceK is not null)
        {
            indiceK = wwv.IndiceK;
            momentos.Add(wwv.KMedidoUtc ?? wwv.EmitidoUtc);
        }

        double? velocidad = null;
        if (viento?.Valor is not null)
        {
            velocidad = viento.Valor;
            momentos.Add(viento.MedidoUtc);
        }

        double? campoBz = null;
        if (bz?.Valor is not null)
        {
            campoBz = bz.Valor;
            momentos.Add(bz.MedidoUtc);
        }

        if (momentos.Count == 0)
        {
            return null;
        }

        var tormenta = (wwv?.Tormenta ?? false)
                       || (indiceK is not null && indiceK >= FuentesSwpc.IndiceKDeTormenta);

        // La medida mas antigua manda: el conjunto no es mas fresco que su pieza mas vieja.
        var medido = momentos.Min();
        return new IndicesSolares(flujo, indiceA, indiceK, manchas, velocidad, campoBz, tormenta, medido);
    }

    /// <summary>Descarga un boletin, con reintentos. Devuelve nulo si no hubo manera.</summary>
    private async Task<string?> DescargarAsync(string url, CancellationToken ct)
    {
        var espera = ajustes.EsperaEntreReintentos;
        for (var intento = 0; intento <= ajustes.Reintentos; intento++)
        {
            try
            {
                var cliente = fabrica.CreateClient(ajustes.NombreDelClienteHttp);
                using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
                limite.CancelAfter(ajustes.EsperaDeRed);

                using var respuesta = await cliente
                    .GetAsync(url, HttpCompletionOption.ResponseContentRead, limite.Token)
                    .ConfigureAwait(false);
                respuesta.EnsureSuccessStatusCode();
                return await respuesta.Content.ReadAsStringAsync(limite.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
            {
                traza.LogDebug(ex, "Fallo al traer {Url}, intento {Intento}.", url, intento + 1);
                if (intento == ajustes.Reintentos)
                {
                    traza.LogWarning("No se pudo traer {Url} tras {Intentos} intentos.", url, intento + 1);
                    return null;
                }

                await Task.Delay(espera, ct).ConfigureAwait(false);
                espera += espera;
            }
        }

        return null;
    }
}
