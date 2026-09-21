using System.Diagnostics;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Aplicacion.CasosDeUso;

/// <summary>Parte de lo ocurrido al importar un fichero ADIF.</summary>
/// <param name="RegistrosLeidos">Registros que traia el fichero.</param>
/// <param name="Anadidos">Contactos nuevos que han entrado en el cuaderno.</param>
/// <param name="FundidosEnElFichero">
/// Registros del propio fichero que resultaron ser el mismo contacto y se fundieron entre si.
/// </param>
/// <param name="FundidosConElCuaderno">
/// Contactos que ya estaban en el cuaderno y han recibido datos del fichero.
/// </param>
/// <param name="YaEstaban">Contactos que ya estaban y a los que el fichero no aportaba nada.</param>
/// <param name="ConfirmacionesRecuperadas">
/// Fusiones que han rescatado informacion de confirmacion. Es la cifra que justifica fundir
/// en vez de descartar: cada una es un dato que cuenta para los diplomas y que se habria perdido.
/// </param>
/// <param name="Choques">Datos que venian distintos en dos copias del mismo contacto.</param>
/// <param name="Avisos">Avisos del analizador de ADIF.</param>
/// <param name="Duracion">Lo que ha tardado la importacion entera.</param>
public sealed record ResultadoDeImportacion(
    int RegistrosLeidos,
    int Anadidos,
    int FundidosEnElFichero,
    int FundidosConElCuaderno,
    int YaEstaban,
    int ConfirmacionesRecuperadas,
    IReadOnlyList<ChoqueDeFusion> Choques,
    IReadOnlyList<AvisoAdif> Avisos,
    TimeSpan Duracion)
{
    /// <summary>Contactos distintos que traia el fichero, ya descontadas las copias repetidas.</summary>
    public int ContactosDistintos => RegistrosLeidos - FundidosEnElFichero;

    /// <summary>No se ha descartado ningun registro del fichero.</summary>
    public bool SinPerdidas => Anadidos + FundidosConElCuaderno + YaEstaban == ContactosDistintos;

    /// <summary>Resumen de una linea para la barra de estado.</summary>
    public string Resumen =>
        $"{RegistrosLeidos:N0} registros leidos: {Anadidos:N0} nuevos, " +
        $"{FundidosEnElFichero + FundidosConElCuaderno:N0} fundidos, {YaEstaban:N0} ya estaban.";
}

/// <summary>
/// Importa un fichero ADIF en el cuaderno fundiendo los contactos repetidos.
/// </summary>
/// <remarks>
/// La politica es <b>fundir, no descartar</b>, y no es una preferencia estetica. Un respaldo
/// real de Log4OM del cuaderno de EA8DLF trae 43 pares de registros con la misma clave natural
/// que no son copias literales: difieren en informes, submodo, indices de propagacion y —lo
/// que importa— en las confirmaciones, de modo que una copia da una QSL por recibida y la otra
/// no. Quedarse con una sola copia perderia confirmaciones, y las confirmaciones son diplomas.
/// Log4OM no exporta ningun identificador propio de contacto, asi que las copias solo se
/// distinguen por su contenido y la unica salida que no pierde nada es fundirlas.
/// </remarks>
/// <param name="lector">Analizador de ADIF.</param>
/// <param name="repositorio">Cuaderno donde se guarda.</param>
public sealed class ImportarAdif(ILectorAdif lector, IRepositorioQso repositorio)
{
    private readonly ILectorAdif _lector = lector ?? throw new ArgumentNullException(nameof(lector));
    private readonly IRepositorioQso _repositorio =
        repositorio ?? throw new ArgumentNullException(nameof(repositorio));

    /// <summary>Lee el fichero y lo vuelca en el cuaderno.</summary>
    /// <param name="origen">Flujo con el contenido del fichero ADIF.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    public async Task<ResultadoDeImportacion> DesdeAsync(Stream origen, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(origen);

        var reloj = Stopwatch.StartNew();
        var lectura = await _lector.LeerAsync(origen, ct).ConfigureAwait(false);

        var choques = new List<ChoqueDeFusion>();
        var recuperadas = 0;

        // ── 1. Fundir las copias que vienen repetidas dentro del propio fichero ──
        var porClave = new Dictionary<string, Qso>(StringComparer.OrdinalIgnoreCase);
        var fundidosEnElFichero = 0;

        foreach (var qso in lectura.Qsos)
        {
            ct.ThrowIfCancellationRequested();

            if (porClave.TryGetValue(qso.ClaveNatural, out var yaVisto))
            {
                var fusion = FusionDeQso.Fundir(yaVisto, qso);
                choques.AddRange(fusion.Choques);
                if (fusion.RecuperoConfirmacion) recuperadas++;
                fundidosEnElFichero++;
            }
            else
            {
                porClave.Add(qso.ClaveNatural, qso);
            }
        }

        // ── 2. Contrastar con lo que ya hay en el cuaderno ───────────────────
        // Si el cuaderno esta vacio no hace falta preguntar por cada contacto, que son
        // tantas consultas como registros traiga el fichero.
        var cuadernoVacio = await _repositorio.ContarAsync(ct).ConfigureAwait(false) == 0;

        var nuevos = new List<Qso>(porClave.Count);
        var fundidosConElCuaderno = 0;
        var yaEstaban = 0;

        foreach (var qso in porClave.Values)
        {
            ct.ThrowIfCancellationRequested();

            var existente = cuadernoVacio
                ? null
                : await _repositorio.BuscarDuplicadoAsync(qso, ct).ConfigureAwait(false);

            if (existente is null)
            {
                nuevos.Add(qso);
                continue;
            }

            var fusion = FusionDeQso.Fundir(existente, qso);
            choques.AddRange(fusion.Choques);
            if (fusion.RecuperoConfirmacion) recuperadas++;

            if (fusion.HuboCambios)
            {
                existente.ModificadoUtc = DateTimeOffset.UtcNow;
                await _repositorio.ActualizarAsync(existente, ct).ConfigureAwait(false);
                fundidosConElCuaderno++;
            }
            else
            {
                yaEstaban++;
            }
        }

        // ── 3. Guardar los nuevos ────────────────────────────────────────────
        // Ya estan deduplicados, asi que un duplicado aqui seria un fallo nuestro y debe
        // hacerse notar en vez de pasar desapercibido.
        var anadidos = 0;
        if (nuevos.Count > 0)
        {
            var lote = await _repositorio
                .AnadirLoteAsync(nuevos, omitirDuplicados: false, ct)
                .ConfigureAwait(false);
            anadidos = lote.Anadidos;
        }

        return new ResultadoDeImportacion(
            RegistrosLeidos: lectura.Qsos.Count,
            Anadidos: anadidos,
            FundidosEnElFichero: fundidosEnElFichero,
            FundidosConElCuaderno: fundidosConElCuaderno,
            YaEstaban: yaEstaban,
            ConfirmacionesRecuperadas: recuperadas,
            Choques: choques,
            Avisos: lectura.Avisos,
            Duracion: reloj.Elapsed);
    }
}
