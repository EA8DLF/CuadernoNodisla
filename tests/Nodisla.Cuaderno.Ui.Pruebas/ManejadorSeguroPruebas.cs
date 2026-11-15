using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Nodisla.Cuaderno.Ui.Soporte;
using Xunit;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// Un manejador <c>async void</c> que falla no cierra el programa.
/// </summary>
public class ManejadorSeguroPruebas
{
    [Fact]
    public async Task Una_excepcion_en_el_manejador_no_se_escapa_y_se_avisa()
    {
        var fallo = new InvalidOperationException("La radio no contesta.");
        var avisos = new List<(Exception Ex, string Donde)>();
        void Apuntar(Exception ex, string donde)
        {
            if (ReferenceEquals(ex, fallo)) avisos.Add((ex, donde));
        }

        ManejadorSeguro.Fallo += Apuntar;
        try
        {
            await ManejadorSeguro.EjecutarAsync(async () =>
            {
                await Task.Yield();
                throw fallo;
            });
        }
        finally
        {
            ManejadorSeguro.Fallo -= Apuntar;
        }

        avisos.Should().ContainSingle()
            .Which.Donde.Should().Be("ManejadorSeguroPruebas.Una_excepcion_en_el_manejador_no_se_escapa_y_se_avisa");
    }

    [Fact]
    public async Task Lo_que_falla_antes_del_primer_await_tampoco_se_escapa()
    {
        var accion = async () => await ManejadorSeguro.EjecutarAsync(
            () => throw new InvalidOperationException("Falla antes de esperar nada."));

        await accion.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Una_cancelacion_no_es_un_fallo()
    {
        var avisos = 0;
        var cancelada = new OperationCanceledException("Cancelada por la prueba 7c1f.");
        void Apuntar(Exception ex, string donde)
        {
            if (ReferenceEquals(ex, cancelada)) avisos++;
        }

        ManejadorSeguro.Fallo += Apuntar;
        try
        {
            await ManejadorSeguro.EjecutarAsync(() => throw cancelada);
        }
        finally
        {
            ManejadorSeguro.Fallo -= Apuntar;
        }

        avisos.Should().Be(0);
    }

    [Fact]
    public async Task El_cuerpo_se_ejecuta_tal_cual_cuando_no_falla()
    {
        var pasos = new List<string>();

        var tarea = ManejadorSeguro.EjecutarAsync(async () =>
        {
            // Lo de antes del primer await va en el acto, como en el manejador sin envolver:
            // ahi es donde las vistas ponen e.Handled = true.
            pasos.Add("antes");
            await Task.Yield();
            pasos.Add("despues");
        });
        pasos.Should().Contain("antes");
        await tarea;

        pasos.Should().Equal("antes", "despues");
    }

    [Fact]
    public void Un_aviso_que_falla_tampoco_tumba_el_programa()
    {
        var fallo = new InvalidOperationException("Fallo de la prueba 51ab.");
        void Romper(Exception ex, string donde)
        {
            if (ReferenceEquals(ex, fallo)) throw new InvalidOperationException("El cartel no se abre.");
        }

        ManejadorSeguro.Fallo += Romper;
        try
        {
            var accion = () => ManejadorSeguro.Atender(fallo, "Prueba.Manejador");
            accion.Should().NotThrow();
        }
        finally
        {
            ManejadorSeguro.Fallo -= Romper;
        }
    }

    /// <summary>
    /// Ningun manejador <c>async void</c> de las vistas puede quedarse sin red: o va envuelto
    /// con <see cref="ManejadorSeguro"/> o todas sus esperas van dentro de un try.
    /// </summary>
    /// <remarks>
    /// Quedan fuera, a proposito, los ficheros que llevan otros: el panel de fonia y el del
    /// modem digital.
    /// </remarks>
    [Fact]
    public void Ningun_manejador_async_void_de_las_vistas_se_queda_sin_red()
    {
        string[] excluidos = ["PanelDeFonia.xaml.cs", "PanelDelModem.xaml.cs"];
        var vistas = Path.Combine(RaizDelRepositorio(), "src", "Nodisla.Cuaderno.Ui", "Vistas");

        var sinRed = new List<string>();
        foreach (var fichero in Directory.GetFiles(vistas, "*.cs", SearchOption.AllDirectories))
        {
            if (excluidos.Contains(Path.GetFileName(fichero))) continue;

            var texto = File.ReadAllText(fichero);
            foreach (Match cabecera in Regex.Matches(texto, @"async\s+void\s+(\w+)\s*\("))
            {
                var cuerpo = Cuerpo(texto, cabecera.Index);
                if (cuerpo.Contains("ManejadorSeguro.EjecutarAsync", StringComparison.Ordinal)) continue;

                // Sin envolver solo vale si no hay ninguna espera fuera de un try.
                var antesDelTry = cuerpo.Split("try", 2)[0];
                var despuesDelUltimoCatch = cuerpo[(cuerpo.LastIndexOf("catch", StringComparison.Ordinal) + 1)..];
                var protegido = cuerpo.Contains("catch (Exception", StringComparison.Ordinal)
                    && !antesDelTry.Contains("await ", StringComparison.Ordinal)
                    && !SinElBloqueDelCatch(despuesDelUltimoCatch).Contains("await ", StringComparison.Ordinal);
                if (!protegido) sinRed.Add($"{Path.GetFileName(fichero)}: {cabecera.Groups[1].Value}");
            }
        }

        sinRed.Should().BeEmpty("un manejador async void sin red cierra el programa al primer fallo");
    }

    /// <summary>El texto del metodo que empieza en <paramref name="desde"/>, hasta su llave de cierre o su «;».</summary>
    private static string Cuerpo(string texto, int desde)
    {
        var llave = texto.IndexOf('{', desde);
        var flecha = texto.IndexOf("=>", desde, StringComparison.Ordinal);
        if (flecha >= 0 && (llave < 0 || flecha < llave))
        {
            // Cuerpo de expresion: hasta el «;» que cierra, contando llaves por si lleva una lambda.
            var hondo = 0;
            for (var i = flecha; i < texto.Length; i++)
            {
                if (texto[i] == '{') hondo++;
                else if (texto[i] == '}') hondo--;
                else if (texto[i] == ';' && hondo == 0) return texto[flecha..i];
            }

            return texto[flecha..];
        }

        var nivel = 0;
        for (var i = llave; i < texto.Length; i++)
        {
            if (texto[i] == '{') nivel++;
            else if (texto[i] == '}' && --nivel == 0) return texto[llave..(i + 1)];
        }

        return texto[llave..];
    }

    /// <summary>Lo que queda detras del bloque del ultimo catch (o finally).</summary>
    private static string SinElBloqueDelCatch(string desdeElCatch)
    {
        var llave = desdeElCatch.IndexOf('{', StringComparison.Ordinal);
        if (llave < 0) return string.Empty;
        var nivel = 0;
        for (var i = llave; i < desdeElCatch.Length; i++)
        {
            if (desdeElCatch[i] == '{') nivel++;
            else if (desdeElCatch[i] == '}' && --nivel == 0) return desdeElCatch[(i + 1)..];
        }

        return string.Empty;
    }

    private static string RaizDelRepositorio()
    {
        var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
        while (carpeta is not null && !File.Exists(Path.Combine(carpeta.FullName, "CuadernoNodisla.sln")))
        {
            carpeta = carpeta.Parent;
        }

        return carpeta?.FullName ?? throw new InvalidOperationException("No se encuentra la raíz del repositorio.");
    }
}
