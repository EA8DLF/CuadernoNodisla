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

        Titulo("Cuaderno NODISLA · importacion de ADIF");
        Console.WriteLine($"Fichero   : {ficheroAdif}");
        Console.WriteLine($"Tamano    : {new FileInfo(ficheroAdif).Length / 1024.0 / 1024.0:N2} MB");
        Console.WriteLine($"Cuaderno  : {rutaCuaderno}");

        // ── 1. Preparar la base ──────────────────────────────────────────────
        var reloj = Stopwatch.StartNew();
        var copia = await migrador.AplicarMigracionesAsync();
        Paso("Base preparada", reloj.Elapsed);
        if (copia is not null) Console.WriteLine($"           copia previa en {copia}");

        // ── 2. Importar, fundiendo los contactos repetidos ───────────────────
        var importar = new ImportarAdif(new LectorAdif(), repositorio);

        ResultadoDeImportacion primera;
        await using (var flujo = File.OpenRead(ficheroAdif))
        {
            primera = await importar.DesdeAsync(flujo);
        }
        Paso($"Importado: {primera.RegistrosLeidos:N0} registros leidos", primera.Duracion);
        Console.WriteLine($"           {primera.Anadidos:N0} nuevos · "
            + $"{primera.FundidosEnElFichero:N0} fundidos dentro del fichero · "
            + $"{primera.FundidosConElCuaderno:N0} fundidos con el cuaderno · "
            + $"{primera.YaEstaban:N0} ya estaban");
        Console.WriteLine($"           {primera.ConfirmacionesRecuperadas:N0} fusiones recuperan confirmaciones");

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

        MostrarChoques(primera.Choques);

        // ── 3. Reimportar el mismo fichero: no debe entrar nada ──────────────
        ResultadoDeImportacion segunda;
        await using (var flujo = File.OpenRead(ficheroAdif))
        {
            segunda = await importar.DesdeAsync(flujo);
        }
        Paso($"Reimportado: {segunda.Anadidos:N0} nuevos, {segunda.YaEstaban:N0} ya estaban", segunda.Duracion);

        // ── 4. Comprobar lo guardado ─────────────────────────────────────────
        var enCuaderno = await repositorio.ContarAsync();
        Console.WriteLine();
        Titulo("Comprobaciones");

        var correcto = true;
        correcto &= Comprobar("Ningun registro del fichero se pierde",
            primera.SinPerdidas, $"{primera.ContactosDistintos:N0} distintos, "
                + $"{primera.Anadidos + primera.FundidosConElCuaderno + primera.YaEstaban:N0} colocados");
        correcto &= Comprobar("El cuaderno tiene los contactos distintos del fichero",
            enCuaderno == primera.ContactosDistintos,
            $"{enCuaderno:N0} de {primera.ContactosDistintos:N0}");
        correcto &= Comprobar("Reimportar no duplica nada",
            segunda.Anadidos == 0, $"{segunda.Anadidos:N0} nuevos en la segunda pasada");
        correcto &= Comprobar("Reimportar no cambia el cuaderno",
            segunda.FundidosConElCuaderno == 0,
            $"{segunda.FundidosConElCuaderno:N0} contactos modificados sin motivo");

        // ── 6. Exportar y volver a leer ──────────────────────────────────────
        var rutaExportada = Path.Combine(Path.GetTempPath(), $"exportado-{Guid.NewGuid():N}.adi");
        Pagina<Qso> pagina;
        try
        {
            reloj.Restart();
            pagina = await repositorio.BuscarAsync(new CriterioQso(), 0, int.MaxValue);
            await using (var salida = File.Create(rutaExportada))
            {
                await new EscritorAdif().EscribirAsync(Enumerar(pagina.Elementos), salida);
            }

            LecturaAdif revuelta;
            await using (var flujo = File.OpenRead(rutaExportada))
            {
                revuelta = await new LectorAdif().LeerAsync(flujo);
            }
            Paso($"Exportado y releido: {revuelta.Qsos.Count:N0} contactos", reloj.Elapsed);

            correcto &= Comprobar("La exportacion conserva el numero de contactos",
                revuelta.Qsos.Count == primera.ContactosDistintos,
                $"{revuelta.Qsos.Count:N0} de {primera.ContactosDistintos:N0}");
            correcto &= Comprobar("Ningun contacto pierde el indicativo al pasar por la base",
                revuelta.Qsos.All(q => !q.Call.EsVacio), "hay indicativos vacios");
            correcto &= Comprobar("Las claves naturales coinciden una a una",
                revuelta.Qsos.Select(q => q.ClaveNatural).ToHashSet()
                    .SetEquals(pagina.Elementos.Select(q => q.ClaveNatural)),
                "el conjunto de claves no es el mismo");
        }
        finally
        {
            if (File.Exists(rutaExportada)) File.Delete(rutaExportada);
        }

        // ── 7. Resumen del cuaderno ──────────────────────────────────────────
        Console.WriteLine();
        Titulo("Resumen del cuaderno");
        var delCuaderno = pagina.Elementos;
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

    /// <summary>
    /// Enseña los datos que venian distintos en dos copias del mismo contacto.
    /// </summary>
    /// <remarks>
    /// Son los unicos casos donde la fusion ha tenido que elegir, asi que son los unicos que
    /// el operador necesita mirar. Se agrupan por campo porque en un cuaderno real el mismo
    /// campo choca una y otra vez por el mismo motivo.
    /// </remarks>
    private static void MostrarChoques(IReadOnlyList<ChoqueDeFusion> choques)
    {
        if (choques.Count == 0)
        {
            Console.WriteLine("           sin datos en discordia");
            return;
        }

        Console.WriteLine($"           {choques.Count:N0} datos en discordia, por campo:");
        foreach (var grupo in choques.GroupBy(c => c.Campo).OrderByDescending(g => g.Count()).Take(10))
        {
            var ejemplo = grupo.First();
            Console.WriteLine($"             · {grupo.Key,-24} {grupo.Count(),4} — "
                + $"se queda «{Recortar(ejemplo.Conservado)}», se descarta «{Recortar(ejemplo.Descartado)}»");
        }
    }

    private static string Recortar(string texto) =>
        texto.Length <= 28 ? texto : string.Concat(texto.AsSpan(0, 27), "…");

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
