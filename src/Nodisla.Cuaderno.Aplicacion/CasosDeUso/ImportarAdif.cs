using System.Diagnostics;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Aplicacion.CasosDeUso;

/// <summary>Parte de una importacion de ADIF, pensado para ensenarselo al operador.</summary>
public sealed record ResultadoDeImportacion
{
    /// <summary>Registros que traia el fichero, antes de fundir nada.</summary>
    public required int RegistrosLeidos { get; init; }

    /// <summary>Contactos nuevos que han entrado en el cuaderno.</summary>
    public required int Anadidos { get; init; }

    /// <summary>Registros del fichero que eran otra copia de un contacto del mismo fichero.</summary>
    public required int FundidosEnElFichero { get; init; }

    /// <summary>Registros que han aportado algo a un contacto que ya estaba en el cuaderno.</summary>
    public required int FundidosConElCuaderno { get; init; }

    /// <summary>Registros que ya estaban en el cuaderno y no aportaban nada nuevo.</summary>
    public required int YaEstaban { get; init; }

    /// <summary>
    /// Contactos en los que la fusion ha rescatado una confirmacion que se habria perdido
    /// descartando la copia. Es la cifra que justifica fundir en lugar de saltar.
    /// </summary>
    public required int ConfirmacionesRecuperadas { get; init; }

    /// <summary>Datos que venian distintos en dos copias, para que el operador los revise.</summary>
    public IReadOnlyList<ChoqueDeFusion> Choques { get; init; } = [];

    /// <summary>Problemas de lectura del fichero.</summary>
    public IReadOnlyList<AvisoAdif> Avisos { get; init; } = [];

    /// <summary>Programa que genero el fichero, del campo <c>PROGRAMID</c>.</summary>
    public string? ProgramaOrigen { get; init; }

    /// <summary>Lo que ha tardado la importacion entera.</summary>
    public TimeSpan Duracion { get; init; }

    /// <summary>Registros del fichero que se han fundido con otro contacto.</summary>
    public int Fundidos => FundidosEnElFichero + FundidosConElCuaderno;

    /// <summary>
    /// Ningun registro del fichero se ha quedado por el camino: o entro, o se fundio con otro,
    /// o ya estaba. Si esto es falso, hay un contacto perdido y eso no puede pasar.
    /// </summary>
    public bool NoSePierdeNada =>
        RegistrosLeidos == Anadidos + FundidosEnElFichero + FundidosConElCuaderno + YaEstaban;
}

/// <summary>
/// Mete un fichero ADIF en el cuaderno.
/// </summary>
/// <remarks>
/// La parte delicada no es leer el fichero sino que hacer con los registros que comparten
/// clave natural. Un respaldo real de Log4OM trae pares con el mismo indicativo, banda, modo y
/// segundo exacto que no son copias literales: una trae la QSL sin recibir y la otra recibida
/// con su fecha. Saltarse la segunda copia —que es lo que hace un alta en lote a secas—
/// perderia confirmaciones, y las confirmaciones son diplomas. Por eso aqui se funden, primero
/// entre si dentro del fichero y despues contra lo que ya hay en el cuaderno.
/// </remarks>
public sealed class ImportarAdif(ILectorAdif lector, IRepositorioQso repositorio)
{
    // Se comprueban aqui y no al usarlos: un nulo que salta en la primera importacion, con el
    // fichero de Jose delante, es mucho peor que uno que salta al montar la aplicacion.
    private readonly ILectorAdif _lector = lector ?? throw new ArgumentNullException(nameof(lector));

    private readonly IRepositorioQso _repositorio =
        repositorio ?? throw new ArgumentNullException(nameof(repositorio));

    /// <summary>Lee el fichero, funde los duplicados y guarda lo que corresponda.</summary>
    /// <param name="origen">Flujo del fichero ADI o ADX.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    public async Task<ResultadoDeImportacion> EjecutarAsync(Stream origen, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(origen);

        var reloj = Stopwatch.StartNew();
        var lectura = await _lector.LeerAsync(origen, ct).ConfigureAwait(false);

        var choques = new List<ChoqueDeFusion>();
        var recuperadas = 0;

        var unicos = FundirDuplicadosDelFichero(lectura.Qsos, choques, ref recuperadas, out var fundidosEnFichero);

        // Con el cuaderno vacio no hay con que chocar, y son tantas consultas como contactos.
        var cuadernoVacio = await _repositorio.ContarAsync(ct).ConfigureAwait(false) == 0;

        var aAnadir = new List<Qso>(unicos.Count);
        var fundidosConCuaderno = 0;
        var yaEstaban = 0;

        foreach (var qso in unicos)
        {
            ct.ThrowIfCancellationRequested();

            var existente = cuadernoVacio
                ? null
                : await _repositorio.BuscarDuplicadoAsync(qso, ct).ConfigureAwait(false);

            if (existente is null)
            {
                aAnadir.Add(qso);
                continue;
            }

            var fusion = FusionDeQso.Fundir(existente, qso);
            choques.AddRange(fusion.Choques);
            if (fusion.RecuperoConfirmacion) recuperadas++;

            if (fusion.HuboCambios)
            {
                existente.ModificadoUtc = DateTimeOffset.UtcNow;
                await _repositorio.ActualizarAsync(existente, ct).ConfigureAwait(false);
                fundidosConCuaderno++;
            }
            else
            {
                yaEstaban++;
            }
        }

        var lote = aAnadir.Count == 0
            ? new ResultadoDeLote(0, 0, TimeSpan.Zero)
            : await _repositorio.AnadirLoteAsync(aAnadir, omitirDuplicados: true, ct).ConfigureAwait(false);

        return new ResultadoDeImportacion
        {
            RegistrosLeidos = lectura.Qsos.Count,
            Anadidos = lote.Anadidos,
            FundidosEnElFichero = fundidosEnFichero,
            FundidosConElCuaderno = fundidosConCuaderno,

            // Los omitidos por el lote solo aparecen si alguien escribio en el cuaderno a la
            // vez que nosotros; se cuentan como «ya estaban» para que las cifras cuadren.
            YaEstaban = yaEstaban + lote.OmitidosPorDuplicado,
            ConfirmacionesRecuperadas = recuperadas,
            Choques = choques,
            Avisos = lectura.Avisos,
            ProgramaOrigen = lectura.ProgramaOrigen,
            Duracion = reloj.Elapsed,
        };
    }

    /// <summary>
    /// Funde entre si los registros del fichero que comparten clave natural y devuelve la
    /// lista de contactos distintos, en el orden en que aparecian.
    /// </summary>
    /// <remarks>
    /// Gana el primero que aparece, que es el que se queda en el cuaderno; los demas le vuelcan
    /// lo que sepan de mas. El orden importa poco porque la parte que de verdad decide
    /// —las confirmaciones— se funde de forma conmutativa.
    /// </remarks>
    public static List<Qso> FundirDuplicadosDelFichero(
        IReadOnlyList<Qso> leidos,
        List<ChoqueDeFusion> choques,
        ref int confirmacionesRecuperadas,
        out int fundidos)
    {
        ArgumentNullException.ThrowIfNull(leidos);
        ArgumentNullException.ThrowIfNull(choques);

        var porClave = new Dictionary<string, Qso>(leidos.Count, StringComparer.Ordinal);
        var unicos = new List<Qso>(leidos.Count);
        fundidos = 0;

        foreach (var qso in leidos)
        {
            if (porClave.TryGetValue(qso.ClaveNatural, out var primero))
            {
                var fusion = FusionDeQso.Fundir(primero, qso);
                choques.AddRange(fusion.Choques);
                if (fusion.RecuperoConfirmacion) confirmacionesRecuperadas++;
                fundidos++;
                continue;
            }

            porClave[qso.ClaveNatural] = qso;
            unicos.Add(qso);
        }

        return unicos;
    }
}
