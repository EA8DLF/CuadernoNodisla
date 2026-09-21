using System.Globalization;
using System.Reflection;
using System.Text;
using System.Xml;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Adif;

/// <summary>
/// Escritor de ficheros ADIF 3.1.5, en sus dos formatos: ADI (texto) y ADX (XML).
/// </summary>
/// <remarks>
/// La longitud que se declara en cada campo va en bytes de UTF-8, que es lo que manda el
/// estandar. Contarla en caracteres es justo el fallo que corrompe los acentos en los ficheros
/// de otros programas.
/// </remarks>
public sealed class EscritorAdif : IEscritorAdif
{
    private static readonly UTF8Encoding Utf8SinMarca = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Version de ADIF que se declara en la cabecera.</summary>
    public const string VersionAdif = "3.1.5";

    /// <summary>Escribe los contactos en el flujo de destino.</summary>
    public async Task EscribirAsync(
        IAsyncEnumerable<Qso> qsos,
        Stream destino,
        OpcionesAdif? opciones = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(qsos);
        ArgumentNullException.ThrowIfNull(destino);

        var o = opciones ?? new OpcionesAdif();
        if (o.Adx) await EscribirAdxAsync(qsos, destino, o, ct).ConfigureAwait(false);
        else await EscribirAdiAsync(qsos, destino, o, ct).ConfigureAwait(false);
    }

    // ── ADI ──────────────────────────────────────────────────────────────────

    private static async Task EscribirAdiAsync(
        IAsyncEnumerable<Qso> qsos, Stream destino, OpcionesAdif o, CancellationToken ct)
    {
        await using var escritor = new StreamWriter(destino, Utf8SinMarca, 64 * 1024, leaveOpen: true);

        var ahora = DateTimeOffset.UtcNow;
        await escritor.WriteLineAsync("# Cuaderno ADIF exportado por " + o.ProgramId).ConfigureAwait(false);
        await escritor.WriteLineAsync(
            "# creado: " + ahora.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC")
            .ConfigureAwait(false);
        await escritor.WriteLineAsync().ConfigureAwait(false);

        var cabecera = new StringBuilder();
        EscribirCampoAdi(cabecera, new CampoAdif("ADIF_VER", VersionAdif));
        EscribirCampoAdi(cabecera, new CampoAdif("PROGRAMID", o.ProgramId));
        EscribirCampoAdi(cabecera, new CampoAdif("PROGRAMVERSION", VersionDelPrograma()));
        EscribirCampoAdi(cabecera, new CampoAdif(
            "CREATED_TIMESTAMP",
            ahora.UtcDateTime.ToString("yyyyMMdd HHmmss", CultureInfo.InvariantCulture)));
        cabecera.Append("<EOH>");
        await escritor.WriteLineAsync(cabecera.ToString()).ConfigureAwait(false);
        await escritor.WriteLineAsync().ConfigureAwait(false);

        var linea = new StringBuilder(2048);
        await foreach (var qso in qsos.WithCancellation(ct).ConfigureAwait(false))
        {
            linea.Clear();
            foreach (var campo in MapeoAdif.EscribirContacto(qso, o.IncluirCamposExtra))
            {
                EscribirCampoAdi(linea, campo);
            }
            linea.Append("<EOR>");
            await escritor.WriteLineAsync(linea.ToString()).ConfigureAwait(false);
        }

        await escritor.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Escribe un campo en la sintaxis de ADI, con la longitud contada en bytes.</summary>
    internal static void EscribirCampoAdi(StringBuilder destino, CampoAdif campo)
    {
        var valor = campo.Valor ?? string.Empty;
        destino.Append('<').Append(campo.Nombre).Append(':');
        destino.Append(Utf8SinMarca.GetByteCount(valor).ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrEmpty(campo.TipoAdif)) destino.Append(':').Append(campo.TipoAdif);
        destino.Append('>').Append(valor).Append(' ');
    }

    // ── ADX ──────────────────────────────────────────────────────────────────

    private static async Task EscribirAdxAsync(
        IAsyncEnumerable<Qso> qsos, Stream destino, OpcionesAdif o, CancellationToken ct)
    {
        var ajustes = new XmlWriterSettings
        {
            Async = true,
            Indent = true,
            Encoding = Utf8SinMarca,
            CloseOutput = false,
            // Sin esto el retorno de carro de una nota de varias lineas se pierde al volver a
            // leer el XML: el analizador normaliza los finales de linea y «\r\n» pasa a «\n».
            NewLineHandling = NewLineHandling.Entitize,
        };

        var escritor = XmlWriter.Create(destino, ajustes);
        await using (escritor.ConfigureAwait(false))
        {
            await escritor.WriteStartDocumentAsync().ConfigureAwait(false);
            await escritor.WriteStartElementAsync(null, "ADX", null).ConfigureAwait(false);

            await escritor.WriteStartElementAsync(null, "HEADER", null).ConfigureAwait(false);
            await EscribirElementoAdxAsync(escritor, new CampoAdif("ADIF_VER", VersionAdif)).ConfigureAwait(false);
            await EscribirElementoAdxAsync(escritor, new CampoAdif("PROGRAMID", o.ProgramId)).ConfigureAwait(false);
            await EscribirElementoAdxAsync(escritor, new CampoAdif("PROGRAMVERSION", VersionDelPrograma()))
                .ConfigureAwait(false);
            await EscribirElementoAdxAsync(escritor, new CampoAdif(
                    "CREATED_TIMESTAMP",
                    DateTimeOffset.UtcNow.UtcDateTime.ToString("yyyyMMdd HHmmss", CultureInfo.InvariantCulture)))
                .ConfigureAwait(false);
            await escritor.WriteEndElementAsync().ConfigureAwait(false);

            await escritor.WriteStartElementAsync(null, "RECORDS", null).ConfigureAwait(false);
            await foreach (var qso in qsos.WithCancellation(ct).ConfigureAwait(false))
            {
                await escritor.WriteStartElementAsync(null, "RECORD", null).ConfigureAwait(false);
                foreach (var campo in MapeoAdif.EscribirContacto(qso, o.IncluirCamposExtra))
                {
                    await EscribirElementoAdxAsync(escritor, campo).ConfigureAwait(false);
                }
                await escritor.WriteEndElementAsync().ConfigureAwait(false);
            }
            await escritor.WriteEndElementAsync().ConfigureAwait(false);

            await escritor.WriteEndElementAsync().ConfigureAwait(false);
            await escritor.WriteEndDocumentAsync().ConfigureAwait(false);
            await escritor.FlushAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Escribe un campo como elemento ADX. Los campos de aplicacion van en un elemento
    /// <c>APP</c> con el programa y el nombre como atributos, tal y como pide el esquema.
    /// </summary>
    private static async Task EscribirElementoAdxAsync(XmlWriter escritor, CampoAdif campo)
    {
        var nombre = campo.Nombre;
        var valor = campo.Valor ?? string.Empty;

        if (nombre.StartsWith("APP_", StringComparison.OrdinalIgnoreCase))
        {
            var resto = nombre[4..];
            var corte = resto.IndexOf('_', StringComparison.Ordinal);
            var programa = corte > 0 ? resto[..corte] : resto;
            var propio = corte > 0 ? resto[(corte + 1)..] : string.Empty;

            await escritor.WriteStartElementAsync(null, "APP", null).ConfigureAwait(false);
            await escritor.WriteAttributeStringAsync(null, "PROGRAMID", null, programa).ConfigureAwait(false);
            await escritor.WriteAttributeStringAsync(null, "FIELDNAME", null, propio).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(campo.TipoAdif))
                await escritor.WriteAttributeStringAsync(null, "TYPE", null, campo.TipoAdif).ConfigureAwait(false);
            await escritor.WriteStringAsync(valor).ConfigureAwait(false);
            await escritor.WriteEndElementAsync().ConfigureAwait(false);
            return;
        }

        if (!EsNombreDeElementoValido(nombre))
        {
            await escritor.WriteStartElementAsync(null, "USERDEF", null).ConfigureAwait(false);
            await escritor.WriteAttributeStringAsync(null, "FIELDNAME", null, nombre).ConfigureAwait(false);
            await escritor.WriteStringAsync(valor).ConfigureAwait(false);
            await escritor.WriteEndElementAsync().ConfigureAwait(false);
            return;
        }

        await escritor.WriteStartElementAsync(null, nombre, null).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(campo.TipoAdif))
            await escritor.WriteAttributeStringAsync(null, "TYPE", null, campo.TipoAdif).ConfigureAwait(false);
        await escritor.WriteStringAsync(valor).ConfigureAwait(false);
        await escritor.WriteEndElementAsync().ConfigureAwait(false);
    }

    private static bool EsNombreDeElementoValido(string nombre)
    {
        if (nombre.Length == 0) return false;
        if (!char.IsAsciiLetter(nombre[0]) && nombre[0] != '_') return false;
        foreach (var c in nombre)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_') return false;
        }
        return true;
    }

    private static string VersionDelPrograma() =>
        typeof(EscritorAdif).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0]
        ?? typeof(EscritorAdif).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";
}
