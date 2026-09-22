using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Nodisla.Cuaderno.Adif;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Datos;
using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Herramientas.Importar;

/// <summary>
/// Importa un fichero ADIF en un cuaderno de Cuaderno NODISLA y comprueba el resultado.
/// </summary>
/// <remarks>
/// Es la prueba de integracion de la Fase 1 y, a la vez, la herramienta con la que se pasa
/// un cuaderno de Log4OM al nuestro. Recorre el camino entero —leer ADIF, migrar la base,
/// guardar, volver a leer y exportar— y compara las cifras en cada paso, que es la unica
/// forma de saber que las capas encajan de verdad y no solo por separado.
///
/// La cuenta que hay que cuadrar no es «registros del fichero = contactos del cuaderno»: un
/// respaldo real trae pares de registros que son el mismo contacto visto dos veces, y esos se
/// funden. Lo que tiene que cuadrar es «registros leidos = anadidos + fundidos + ya estaban».
/// </remarks>
public static class Programa
{
    public static async Task<int> Main(string[] argumentos)
    {
        CultureInfo.CurrentCulture = new CultureInfo("es-ES", useUserOverride: false);
        try
        {
            // Sin esto la consola de Windows escupe los acentos y los guiones como basura.
            Console.OutputEncoding = System.Text.Encoding.UTF8;
        }
        catch (IOException)
        {
            // Salida redirigida a un fichero: no hay codificacion que fijar y no importa.
        }

        if (argumentos.Length is 0 or > 2 || argumentos[0] is "-h" or "--ayuda")
        {
            Console.WriteLine("""
                Importa un fichero ADIF en un cuaderno de Cuaderno NODISLA.

                  importar <fichero.adi> [cuaderno.sqlite]

                Si no se indica cuaderno, se crea uno temporal y se borra al terminar.
                """);
            return argumentos.Length == 0 ? 1 : 0;
        }

        var ficheroAdif = Path.GetFullPath(argumentos[0]);
        if (!File.Exists(ficheroAdif))
        {
            Console.Error.WriteLine($"No existe el fichero: {ficheroAdif}");
            return 1;
        }

        var temporal = argumentos.Length == 1;
        var rutaCuaderno = temporal
            ? Path.Combine(Path.GetTempPath(), $"cuaderno-prueba-{Guid.NewGuid():N}.sqlite")
            : Path.GetFullPath(argumentos[1]);

        try
        {
            return await ImportarAsync(ficheroAdif, rutaCuaderno);
        }
        finally
        {
            if (temporal) BorrarCuadernoTemporal(rutaCuaderno);
        }
    }

    private static async Task<int> ImportarAsync(string ficheroAdif, string rutaCuaderno)
    {
        var servicios = new ServiceCollection()
            .AnadirDatosDelCuaderno(o => o.Ruta = rutaCuaderno)
            .BuildServiceProvider();

        using var ambito = servicios.CreateScope();
        var repositorio = ambito.ServiceProvider.GetRequiredService<IRepositorioQso>();
        var migrador = ambito.ServiceProvider.GetRequiredService<MigradorDeCuaderno>();
        var importar = new ImportarAdif(new LectorAdif(), repositorio);

        Titulo("Cuaderno NODISLA · importacion de ADIF");
        Console.WriteLine($"Fichero   : {ficheroAdif}");
        Console.WriteLine($"Tamano    : {new FileInfo(ficheroAdif).Length / 1024.0 / 1024.0:N2} MB");
        Console.WriteLine($"Cuaderno  : {rutaCuaderno}");

        // ── 1. Preparar la base ──────────────────────────────────────────────
        var reloj = Stopwatch.StartNew();
        var copia = await migrador.AplicarMigracionesAsync();
        Paso("Base preparada", reloj.Elapsed);
        if (copia is not null) Console.WriteLine($"           copia previa en {copia}");

        // ── 2. Importar ──────────────────────────────────────────────────────
        var primera = await EjecutarAsync(importar, ficheroAdif);
        Paso($"ADIF leido: {primera.RegistrosLeidos:N0} registros", primera.Duracion);
        Console.WriteLine($"           generado por {primera.ProgramaOrigen ?? "(sin declarar)"}");

        foreach (var grupo in primera.Avisos.GroupBy(a => a.Nivel).OrderByDescending(g => g.Key))
        {
            Console.WriteLine($"           {grupo.Count():N0} avisos de nivel {grupo.Key}");
            foreach (var muestra in grupo.Take(2))
            {
                Console.WriteLine($"             · registro {muestra.NumeroDeRegistro}, {muestra.Campo}: {muestra.Mensaje}");
            }
        }

        if (primera.Avisos.Any(a => a.EsFatal))
        {
            Console.Error.WriteLine("Hay registros descartados. Se aborta para no guardar un cuaderno incompleto.");
            return 2;
        }

        ResumirImportacion("Primera pasada", primera);

        // ── 3. Reimportar el mismo fichero: no debe entrar nada nuevo ─────────
        var segunda = await EjecutarAsync(importar, ficheroAdif);
        ResumirImportacion("Segunda pasada", segunda);

        // ── 4. Comprobar lo guardado ─────────────────────────────────────────
        var enCuaderno = await repositorio.ContarAsync();
        var unicosDelFichero = primera.RegistrosLeidos - primera.FundidosEnElFichero;

        Console.WriteLine();
        Titulo("Comprobaciones");

        var correcto = true;
        correcto &= Comprobar("Ningun registro del fichero se pierde (leidos = anadidos + fundidos + ya estaban)",
            primera.NoSePierdeNada,
            $"{primera.RegistrosLeidos:N0} leidos frente a {primera.Anadidos:N0} + {primera.Fundidos:N0} + {primera.YaEstaban:N0}");
        correcto &= Comprobar("Todos los contactos distintos del fichero estan en el cuaderno",
            enCuaderno == unicosDelFichero, $"{enCuaderno:N0} de {unicosDelFichero:N0}");
        correcto &= Comprobar("Reimportar no duplica nada",
            segunda.Anadidos == 0, $"{segunda.Anadidos:N0} nuevos en la segunda pasada");
        correcto &= Comprobar("Reimportar no cambia nada (la fusion es idempotente)",
            segunda.FundidosConElCuaderno == 0,
            $"{segunda.FundidosConElCuaderno:N0} contactos modificados al repetir la importacion");
        correcto &= Comprobar("La segunda pasada tampoco pierde registros",
            segunda.NoSePierdeNada, "las cifras de la segunda pasada no cuadran");

        // ── 5. Exportar y volver a leer ──────────────────────────────────────
        var rutaExportada = Path.Combine(Path.GetTempPath(), $"exportado-{Guid.NewGuid():N}.adi");
        IReadOnlyList<Qso> delCuaderno;
        try
        {
            reloj.Restart();
            var pagina = await repositorio.BuscarAsync(new CriterioQso(), 0, int.MaxValue);
            delCuaderno = pagina.Elementos;
            await using (var salida = File.Create(rutaExportada))
            {
                await new EscritorAdif().EscribirAsync(Enumerar(delCuaderno), salida);
            }

            LecturaAdif revuelta;
            await using (var flujo = File.OpenRead(rutaExportada))
            {
                revuelta = await new LectorAdif().LeerAsync(flujo);
            }
            Paso($"Exportado y releido: {revuelta.Qsos.Count:N0} contactos", reloj.Elapsed);

            correcto &= Comprobar("La exportacion conserva el numero de contactos",
                revuelta.Qsos.Count == enCuaderno,
                $"{revuelta.Qsos.Count:N0} de {enCuaderno:N0}");
            correcto &= Comprobar("Ningun contacto pierde el indicativo al pasar por la base",
                revuelta.Qsos.All(q => !q.Call.EsVacio), "hay indicativos vacios");
            correcto &= Comprobar("Las claves naturales coinciden una a una",
                revuelta.Qsos.Select(q => q.ClaveNatural).ToHashSet()
                    .SetEquals(delCuaderno.Select(q => q.ClaveNatural)),
                "el conjunto de claves no es el mismo");
        }
        finally
        {
            if (File.Exists(rutaExportada)) File.Delete(rutaExportada);
        }

        correcto &= ComprobarQueLaFusionEsConmutativa(ficheroAdif, out var paresProbados);
        Console.WriteLine($"         ({paresProbados:N0} pares del fichero fundidos en los dos sentidos)");

        // ── 6. Resumen del cuaderno ──────────────────────────────────────────
        Console.WriteLine();
        Titulo("Resumen del cuaderno");
        ResumirPorClave("Bandas", delCuaderno.Where(q => !q.Band.EsVacia).Select(q => q.Band.Nombre));
        ResumirPorClave("Modos", delCuaderno.Select(q => q.Mode.NombreUsual));
        Console.WriteLine($"  Entidades DXCC distintas : {delCuaderno.Select(q => q.Dxcc).Where(d => d > 0).Distinct().Count():N0}");
        Console.WriteLine($"  Contactos confirmados    : {delCuaderno.Count(q => q.Confirmaciones.Any(c => c.EstaConfirmada)):N0}");
        Console.WriteLine($"  Campos no modelados      : {delCuaderno.Sum(q => q.CamposExtra.Count):N0}");

        var primero = delCuaderno.Min(q => q.InicioUtc);
        var ultimo = delCuaderno.Max(q => q.InicioUtc);
        Console.WriteLine($"  Del {primero:dd-MM-yyyy} al {ultimo:dd-MM-yyyy}");

        Console.WriteLine();
        Console.WriteLine(correcto ? "TODO CORRECTO" : "HAY COMPROBACIONES QUE FALLAN");
        return correcto ? 0 : 3;
    }

    private static async Task<ResultadoDeImportacion> EjecutarAsync(ImportarAdif importar, string fichero)
    {
        await using var flujo = File.OpenRead(fichero);
        return await importar.EjecutarAsync(flujo);
    }

    /// <summary>Cuenta lo que ha pasado en una pasada de importacion.</summary>
    private static void ResumirImportacion(string titulo, ResultadoDeImportacion r)
    {
        Console.WriteLine();
        Console.WriteLine($"  {titulo}");
        Console.WriteLine($"    Anadidos                     : {r.Anadidos:N0}");
        Console.WriteLine($"    Fundidos dentro del fichero  : {r.FundidosEnElFichero:N0}");
        Console.WriteLine($"    Fundidos con el cuaderno     : {r.FundidosConElCuaderno:N0}");
        Console.WriteLine($"    Ya estaban sin nada nuevo    : {r.YaEstaban:N0}");
        Console.WriteLine($"    Confirmaciones recuperadas   : {r.ConfirmacionesRecuperadas:N0}");

        if (r.Choques.Count == 0) return;

        var porCampo = r.Choques.GroupBy(c => c.Campo, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ToList();
        Console.WriteLine($"    Datos en discordia           : {r.Choques.Count:N0} en {porCampo.Count:N0} campos");

        // Un cero no es un hueco para la fusion, asi que si la copia que manda trae cero y la
        // otra un valor de verdad, se conserva el cero. Conviene que el operador lo sepa.
        var ceros = r.Choques.Count(c => c.Conservado is "0" or "0.0" && c.Descartado is not ("0" or "0.0"));
        if (ceros > 0)
        {
            Console.WriteLine($"      (de ellos {ceros:N0} conservan un cero frente a un valor; revisar)");
        }
        foreach (var g in porCampo.Take(12))
        {
            var ejemplo = g.First();
            Console.WriteLine(
                $"      · {g.Key,-32} {g.Count(),4:N0}  p.ej. «{Recortar(ejemplo.Conservado)}» frente a «{Recortar(ejemplo.Descartado)}»");
        }
        if (porCampo.Count > 12) Console.WriteLine($"      · … y {porCampo.Count - 12:N0} campos mas");
    }

    /// <summary>
    /// Comprueba sobre los pares de verdad del fichero que fundir A con B y B con A deja las
    /// mismas confirmaciones. Si dependiera del orden, el cuaderno cambiaria segun como
    /// estuviera ordenado el fichero, que es justo lo que no puede pasar con los diplomas.
    /// </summary>
    private static bool ComprobarQueLaFusionEsConmutativa(string ficheroAdif, out int pares)
    {
        pares = 0;
        LecturaAdif lectura;
        using (var flujo = File.OpenRead(ficheroAdif))
        {
            lectura = new LectorAdif().LeerAsync(flujo).GetAwaiter().GetResult();
        }

        var porClave = new Dictionary<string, Qso>(StringComparer.Ordinal);
        var iguales = true;

        foreach (var qso in lectura.Qsos)
        {
            if (!porClave.TryGetValue(qso.ClaveNatural, out var primero))
            {
                porClave[qso.ClaveNatural] = qso;
                continue;
            }

            pares++;
            var haciaDelante = Clonar(primero);
            FusionDeQso.Fundir(haciaDelante, qso);
            var haciaAtras = Clonar(qso);
            FusionDeQso.Fundir(haciaAtras, primero);

            if (!MismasConfirmaciones(haciaDelante, haciaAtras)) iguales = false;
            porClave[qso.ClaveNatural] = haciaDelante;
        }

        return Comprobar("Fundir en un sentido o en el otro deja las mismas confirmaciones",
            iguales, "hay pares cuyo resultado depende del orden");
    }

    private static bool MismasConfirmaciones(Qso a, Qso b)
    {
        static IOrderedEnumerable<QsoConfirmacion> Ordenadas(Qso q) => q.Confirmaciones.OrderBy(c => c.Medio);

        if (a.Confirmaciones.Count != b.Confirmaciones.Count) return false;
        return Ordenadas(a).Zip(Ordenadas(b)).All(p =>
            p.First.Medio == p.Second.Medio
            && p.First.Enviado == p.Second.Enviado
            && p.First.Recibido == p.Second.Recibido
            && p.First.EnviadoUtc == p.Second.EnviadoUtc
            && p.First.RecibidoUtc == p.Second.RecibidoUtc);
    }

    /// <summary>Copia lo justo para poder fundir sin tocar el contacto original.</summary>
    private static Qso Clonar(Qso original)
    {
        var copia = new Qso
        {
            Call = original.Call,
            Band = original.Band,
            Mode = original.Mode,
            InicioUtc = original.InicioUtc,
        };
        foreach (var c in original.Confirmaciones)
        {
            copia.Confirmaciones.Add(new QsoConfirmacion
            {
                Medio = c.Medio,
                Enviado = c.Enviado,
                Recibido = c.Recibido,
                EnviadoUtc = c.EnviadoUtc,
                RecibidoUtc = c.RecibidoUtc,
                Via = c.Via,
                Nota = c.Nota,
            });
        }
        return copia;
    }

    private static string Recortar(string texto) =>
        texto.Length <= 24 ? texto : texto[..24] + "…";

    private static async IAsyncEnumerable<Qso> Enumerar(IReadOnlyList<Qso> qsos)
    {
        foreach (var q in qsos)
        {
            yield return q;
        }
        await Task.CompletedTask;
    }

    private static void ResumirPorClave(string titulo, IEnumerable<string> valores)
    {
        var cuenta = valores.GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ToList();
        var muestra = string.Join(", ", cuenta.Take(6).Select(g => $"{g.Key} ({g.Count():N0})"));
        Console.WriteLine($"  {titulo,-24} : {cuenta.Count:N0} distintos — {muestra}");
    }

    private static void Titulo(string texto)
    {
        Console.WriteLine();
        Console.WriteLine(texto);
        Console.WriteLine(new string('─', texto.Length));
    }

    private static void Paso(string texto, TimeSpan tiempo) =>
        Console.WriteLine($"  [{tiempo.TotalSeconds,6:N2} s] {texto}");

    private static bool Comprobar(string descripcion, bool correcto, string detalleSiFalla)
    {
        Console.WriteLine(correcto
            ? $"  OK    {descripcion}"
            : $"  FALLA {descripcion} — {detalleSiFalla}");
        return correcto;
    }

    private static void BorrarCuadernoTemporal(string ruta)
    {
        foreach (var f in new[] { ruta, ruta + "-wal", ruta + "-shm" })
        {
            try
            {
                if (File.Exists(f)) File.Delete(f);
            }
            catch (IOException)
            {
                // El fichero temporal se queda; no merece hacer fracasar la importacion.
            }
        }
    }
}
