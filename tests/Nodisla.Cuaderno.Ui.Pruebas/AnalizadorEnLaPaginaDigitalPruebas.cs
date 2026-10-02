using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Ui.VistaModelos;
using Nodisla.Cuaderno.Ui.Vistas;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// Con la ventana principal de verdad (puertos simulados, FT-710): el analizador de la radio
/// tiene que leer en la cabina, seguir leyendo al pasar a Digital —donde el frontal es el del
/// modelo y su pantalla lleva el analizador— y otra vez al volver a la cabina. Y ningun enlace
/// del frontal a la ventana (RelativeSource AncestorType=Window) puede quedarse roto.
/// </summary>
/// <remarks>Fallo del operador del 01-10-2026: «cuando paso a digitales no aparece el espectro del equipo».</remarks>
[Collection(nameof(ColeccionDeLaVentana))]
public sealed class AnalizadorEnLaPaginaDigitalPruebas
{
    private static readonly string[] Variables = ["CUADERNO_SIMULADO", "CUADERNO_CARPETA", "CUADERNO_MODELO"];

    // Los Icom no van: su catalogo no tiene analizador (no se lee el espectro por CI-V).
    [Theory]
    [InlineData("yaesu-ft710", "FrontalFt710")]
    [InlineData("yaesu-ftdx10", "FrontalFtdx10")]
    [InlineData("yaesu-ftdx101", "FrontalFtdx101")]
    [InlineData("yaesu-ft991", "FrontalFt991")]
    public Task Cabina_digital_y_cabina_el_analizador_del_equipo_lee_en_las_dos_paginas(string clave, string nombreDelFrontal) => HiloDeVentana.Ejecutar(async () =>
    {
        var antes = Variables.ToDictionary(v => v, Environment.GetEnvironmentVariable);
        var carpeta = Path.Combine(Path.GetTempPath(), "cuaderno-digital-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("CUADERNO_SIMULADO", "1");
        Environment.SetEnvironmentVariable("CUADERNO_CARPETA", carpeta);
        Environment.SetEnvironmentVariable("CUADERNO_MODELO", clave);

        // La ventana principal usa tambien la marca, que el hilo de ventana de las pruebas no carga.
        var recursos = Application.Current.Resources;
        if (recursos["MarcaDelCuaderno"] is null)
        {
            recursos.MergedDictionaries.Add((ResourceDictionary)Application.LoadComponent(
                new Uri("/Nodisla.Cuaderno.Ui;component/Recursos/Marca.xaml", UriKind.Relative)));
        }

        var analizador = new AnalizadorQueCuenta();
        var servicios = new ServiceCollection();
        servicios.AddLogging();
        servicios.AnadirCuaderno();
        servicios.AddSingleton<IAnalizadorDeEspectro>(analizador);
        await using var proveedor = servicios.BuildServiceProvider();

        var ventana = proveedor.GetRequiredService<VentanaPrincipal>();
        var modelo = (VistaModeloPrincipal)ventana.DataContext;
        ventana.WindowStartupLocation = WindowStartupLocation.Manual;
        ventana.ShowActivated = false;
        ventana.ShowInTaskbar = false;
        ventana.Left = -10000;
        ventana.Top = -10000;
        ventana.Width = 1460;
        ventana.Height = 1000;

        try
        {
            ventana.Show();
            await Asentar();
            if (!modelo.Equipo.Conectado) await modelo.Equipo.ConectarAsync();
            await Asentar();
            modelo.Equipo.NombreDelFrontal.Should().Be(nombreDelFrontal);

            analizador.Leyendo.Should().BeTrue("en la cabina el frontal del modelo lleva el analizador");

            modelo.IndiceDeLaPestana = 1;
            await Asentar();
            var digital = Todos<PestanaDigital>(ventana).Should().ContainSingle().Subject;
            Todos<FrameworkElement>(digital).Where(e => e.GetType().Name == nombreDelFrontal)
                .Should().ContainSingle("en Digital el frontal es el del modelo conectado");
            var delDigital = Todos<AnalizadorDelEquipo>(digital).Should().ContainSingle("la pantalla del modelo lleva el analizador").Subject;
            delDigital.IsVisible.Should().BeTrue();
            delDigital.DataContext.Should().BeSameAs(
                modelo.Analizador,
                "el frontal de Digital se puso con la pagina fuera de la ventana y su enlace al analizador tiene que resolverse al verse");
            analizador.Leyendo.Should().BeTrue(
                "en Digital el analizador esta a la vista (inicios {0}, paradas {1})", analizador.Inicios, analizador.Detenciones);
            EnlacesRotosALaVentana(ventana).Should().BeEmpty(
                "en Digital ningun enlace a la ventana del frontal puesto al conectar puede quedarse roto (p. ej. «Abrir el audio» de MULTI)");

            modelo.IndiceDeLaPestana = 0;
            await Asentar();
            analizador.Leyendo.Should().BeTrue("de vuelta en la cabina");
            EnlacesRotosALaVentana(ventana).Should().BeEmpty("de vuelta en la cabina");
        }
        finally
        {
            ventana.Close();
            foreach (var (nombre, valor) in antes) Environment.SetEnvironmentVariable(nombre, valor);
            try { Directory.Delete(carpeta, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    });

    private static async Task Asentar()
    {
        for (var i = 0; i < 4; i++)
        {
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        }
    }

    /// <summary>Los enlaces con <c>RelativeSource AncestorType=Window</c> a la vista que no han encontrado su fuente.</summary>
    private static List<string> EnlacesRotosALaVentana(Window ventana)
    {
        var rotos = new List<string>();
        foreach (var elemento in Todos<DependencyObject>(ventana))
        {
            var valores = elemento.GetLocalValueEnumerator();
            while (valores.MoveNext())
            {
                if (System.Windows.Data.BindingOperations.GetBindingExpression(elemento, valores.Current.Property) is
                    {
                        Status: not System.Windows.Data.BindingStatus.Active,
                        ParentBinding.RelativeSource: { Mode: System.Windows.Data.RelativeSourceMode.FindAncestor } fuente,
                    } enlace
                    && fuente.AncestorType == typeof(Window))
                {
                    rotos.Add($"{elemento.GetType().Name}.{valores.Current.Property.Name} <- {enlace.ParentBinding.Path?.Path} ({enlace.Status})");
                }
            }
        }

        return rotos;
    }

    private static List<T> Todos<T>(DependencyObject raiz)
        where T : DependencyObject
    {
        var hallados = new List<T>();
        var pendientes = new Stack<DependencyObject>();
        pendientes.Push(raiz);
        while (pendientes.Count > 0)
        {
            var actual = pendientes.Pop();
            if (actual is T t) hallados.Add(t);
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(actual); i++) pendientes.Push(VisualTreeHelper.GetChild(actual, i));
        }

        return hallados;
    }

    private sealed class AnalizadorQueCuenta : IAnalizadorDeEspectro
    {
        public int Inicios { get; private set; }

        public int Detenciones { get; private set; }

        public bool Leyendo { get; private set; }

        public string Origen => "De mentira";

        public EstadoDelAnalizador Estado => EstadoDelAnalizador.Parado;

        public string Motivo => string.Empty;

        public event EventHandler<TrazaDeEspectro>? TrazaRecibida
        {
            add { }
            remove { }
        }

        public event EventHandler? EstadoCambiado
        {
            add { }
            remove { }
        }

        public void Iniciar()
        {
            Inicios++;
            Leyendo = true;
        }

        public Task DetenerAsync()
        {
            Detenciones++;
            Leyendo = false;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
