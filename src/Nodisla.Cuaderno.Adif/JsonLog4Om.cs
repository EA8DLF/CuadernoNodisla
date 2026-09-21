using System.Text.Json;
using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Adif;

/// <summary>
/// Lectura de los campos JSON que Log4OM mete dentro del ADIF.
/// </summary>
/// <remarks>
/// El original guarda las confirmaciones y las referencias de diploma como JSON embebido en
/// <c>APP_L4ONG_QSO_CONFIRMATIONS</c> y <c>APP_L4ONG_QSO_AWARD_REFERENCES</c>, con claves de
/// dos letras. Aqui se traduce a las tablas hijas del modelo. El texto original se conserva
/// ademas tal cual entre los campos extra, de modo que al exportar sale exactamente igual.
/// </remarks>
internal static class JsonLog4Om
{
    /// <summary>Campo del ADIF de Log4OM con las confirmaciones.</summary>
    public const string CampoConfirmaciones = "APP_L4ONG_QSO_CONFIRMATIONS";

    /// <summary>Campo del ADIF de Log4OM con las referencias de diploma del corresponsal.</summary>
    public const string CampoReferencias = "APP_L4ONG_QSO_AWARD_REFERENCES";

    /// <summary>Campo del ADIF de Log4OM con mis referencias de diploma.</summary>
    public const string CampoMisReferencias = "APP_L4ONG_MY_AWARD_REFERENCES";

    /// <summary>Traduce el codigo de servicio del JSON a una via de confirmacion.</summary>
    public static MedioDeConfirmacion? MedioDesdeCodigo(string? codigo) =>
        (codigo ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "QSL" => MedioDeConfirmacion.Papel,
            "EQSL" => MedioDeConfirmacion.Eqsl,
            "LOTW" => MedioDeConfirmacion.Lotw,
            "QRZCOM" => MedioDeConfirmacion.QrzCom,
            "HAMQTH" => MedioDeConfirmacion.HamQth,
            "HRDLOG" => MedioDeConfirmacion.HrdLog,
            "CLUBLOG" => MedioDeConfirmacion.ClubLog,
            "QRZCQ" => MedioDeConfirmacion.QrzCq,
            _ => null,
        };

    private static EstadoDeConfirmacion? EstadoDesdeTexto(string? texto) =>
        (texto ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "YES" => EstadoDeConfirmacion.Confirmado,
            "NO" => EstadoDeConfirmacion.Ninguno,
            "REQUESTED" => EstadoDeConfirmacion.Solicitado,
            "QUEUED" => EstadoDeConfirmacion.Pendiente,
            "IGNORE" => EstadoDeConfirmacion.Rechazado,
            "INVALID" => EstadoDeConfirmacion.Invalido,
            "RETURNED" => EstadoDeConfirmacion.Devuelto,
            _ => null,
        };

    private static ViaDeEnvio? ViaDesdeTexto(string? texto) =>
        (texto ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "ELECTRONIC" => ViaDeEnvio.Electronico,
            "BUREAU" => ViaDeEnvio.Buro,
            "DIRECT" => ViaDeEnvio.Directo,
            "MANAGER" => ViaDeEnvio.Gestor,
            _ => null,
        };

    /// <summary>
    /// Completa las confirmaciones del contacto con lo que diga el JSON. Lo que ya venia en los
    /// campos ADIF normales manda: el JSON solo rellena los huecos, sobre todo las fechas.
    /// </summary>
    public static bool TryAplicarConfirmaciones(string json, Qso qso)
    {
        if (!TryLeerArreglo(json, out var documento)) return false;
        using (documento)
        {
            foreach (var entrada in documento.RootElement.EnumerateArray())
            {
                if (entrada.ValueKind != JsonValueKind.Object) continue;
                var medio = MedioDesdeCodigo(Texto(entrada, "CT"));
                if (medio is null) continue;

                var confirmacion = ConfirmacionesAdif.Obtener(qso, medio.Value);

                if (confirmacion.Enviado == EstadoDeConfirmacion.Ninguno
                    && EstadoDesdeTexto(Texto(entrada, "S")) is { } enviado)
                {
                    confirmacion.Enviado = enviado;
                }
                if (confirmacion.Recibido == EstadoDeConfirmacion.Ninguno
                    && EstadoDesdeTexto(Texto(entrada, "R")) is { } recibido)
                {
                    confirmacion.Recibido = recibido;
                }
                if (confirmacion.Via == ViaDeEnvio.Ninguna && ViaDesdeTexto(Texto(entrada, "SV")) is { } via)
                {
                    confirmacion.Via = via;
                }
                if (confirmacion.EnviadoUtc is null && ConversionesAdif.TryLeerIso(Texto(entrada, "SD"), out var sd))
                {
                    confirmacion.EnviadoUtc = sd;
                }
                if (confirmacion.RecibidoUtc is null && ConversionesAdif.TryLeerIso(Texto(entrada, "RD"), out var rd))
                {
                    confirmacion.RecibidoUtc = rd;
                }
            }
        }
        return true;
    }

    /// <summary>
    /// Anade al contacto las referencias de activacion que aparezcan en el JSON.
    /// </summary>
    /// <remarks>
    /// Solo se traen los programas que son de verdad una referencia visitable —IOTA, SOTA,
    /// POTA, WWFF, WCA, DME—. Las entradas de DXCC, VUCC, WAC o WAE que Log4OM mete en la misma
    /// lista no son referencias sino progreso de diploma calculado, y se recalculan solas.
    /// </remarks>
    public static bool TryAplicarReferencias(string json, Qso qso, LadoDeReferencia lado)
    {
        if (!TryLeerArreglo(json, out var documento)) return false;
        using (documento)
        {
            foreach (var entrada in documento.RootElement.EnumerateArray())
            {
                if (entrada.ValueKind != JsonValueKind.Object) continue;
                var tipo = ReferenciasAdif.TipoDesdeCodigo(Texto(entrada, "AC"));
                if (tipo is null) continue;
                var codigo = Texto(entrada, "R");
                if (string.IsNullOrWhiteSpace(codigo)) continue;
                ReferenciasAdif.Anadir(qso, tipo.Value, null, codigo, lado);
            }
        }
        return true;
    }

    private static bool TryLeerArreglo(string json, out JsonDocument documento)
    {
        documento = null!;
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            var d = JsonDocument.Parse(json);
            if (d.RootElement.ValueKind != JsonValueKind.Array)
            {
                d.Dispose();
                return false;
            }
            documento = d;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? Texto(JsonElement objeto, string clave) =>
        objeto.TryGetProperty(clave, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
