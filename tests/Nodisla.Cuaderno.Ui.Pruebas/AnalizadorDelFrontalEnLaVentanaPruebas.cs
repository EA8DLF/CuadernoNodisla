using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Radio.Control;
using Nodisla.Cuaderno.Radio.Control.Ft710;
using Nodisla.Cuaderno.Radio.Ptt;
using Nodisla.Cuaderno.Ui.VistaModelos;
using Nodisla.Cuaderno.Ui.Vistas;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// El analizador de la radio tiene que ARRANCAR cuando el frontal del FT-710 se ve dentro de
/// una ventana, tambien cuando ese frontal lo pone el selector al conectar (FrontalConBotonera
/// cambia el dibujo generico por el del FT-710), y pararse al ocultarlo.
/// </summary>
[Collection(nameof(ColeccionDeLaVentana))]
public sealed class AnalizadorDelFrontalEnLaVentanaPruebas
{
    [Fact]
    public Task Al_conectar_el_ft710_el_analizador_del_frontal_arranca() => HiloDeVentana.Ejecutar(async () =>
    {
        var canal = new FrontalFt710Pruebas.CanalReal();
        var control = new ControlFt710(canal, new OpcionesFt710
        {
            IntervaloDeSondeo = TimeSpan.FromHours(1),
            Esperar = (_, _) => Task.CompletedTask,
        });
        await using var conmutable = new ControlEquipoConmutable(control);
        var vigilante = new VigilantePtt(conmutable, new OpcionesDelVigilante { EngancharseAlCierreDelProceso = false });
        var equipo = new VistaModeloEquipo(conmutable, vigilante)
        {
            ConfirmarAccion = _ => true,
            ConfirmarQueVaATransmitir = _ => true,
        };

        var analizador = new AnalizadorQueCuenta();
        var errores = new StringBuilder();
        using var oyente = new OyenteDeEnlacesDelFrontal(errores);
        var frontal = new FrontalConBotonera { DataContext = equipo };
        var tarjeta = new System.Windows.Controls.Border { Child = frontal };
        var ventana = new Window
        {
            DataContext = new ContextoDeVentana(new VistaModeloAnalizador(analizador)),
            Content = tarjeta,
            Width = 1460,
            Height = 600,
            WindowStyle = WindowStyle.None,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
            Top = -10000,
        };

        try
        {
            ventana.Show();
            await Asentar();

            // Sin conectar, el frontal generico, que tambien lo ensena.
            analizador.Leyendo.Should().BeTrue("el frontal de antes de conectar tambien tiene el analizador a la vista");

            await equipo.ConectarAsync();
            await Asentar();

            equipo.NombreDelFrontal.Should().Be("FrontalFt710");
            Todos<FrontalFt710>(frontal).Should().ContainSingle("al conectar se pone el frontal del FT-710");
            Todos<AnalizadorDelEquipo>(frontal).Should().ContainSingle();
            analizador.Leyendo.Should().BeTrue(
                "el frontal del FT-710 esta a la vista: su analizador tiene que leer (inicios {0}, paradas {1}). Enlaces: {2}",
                analizador.Inicios, analizador.Detenciones, errores);

            // Y al ocultarlo se para, para volver a arrancar al mostrarlo.
            frontal.Visibility = Visibility.Collapsed;
            await Asentar();
            analizador.Leyendo.Should().BeFalse();
            frontal.Visibility = Visibility.Visible;
            await Asentar();
            analizador.Leyendo.Should().BeTrue();
        }
        finally
        {
            ventana.Close();
        }
    });

    private static async Task Asentar()
    {
        for (var i = 0; i < 3; i++)
        {
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        }
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
            var hijos = System.Windows.Media.VisualTreeHelper.GetChildrenCount(actual);
            for (var i = 0; i < hijos; i++) pendientes.Push(System.Windows.Media.VisualTreeHelper.GetChild(actual, i));
        }

        return hallados;
    }

    /// <summary>Lo que el frontal busca en la ventana: <c>DataContext.Analizador</c>.</summary>
    public sealed class ContextoDeVentana(VistaModeloAnalizador analizador)
    {
        /// <summary>El analizador.</summary>
        public VistaModeloAnalizador Analizador { get; } = analizador;
    }

    private sealed class OyenteDeEnlacesDelFrontal : TraceListener
    {
        private readonly StringBuilder _errores;

        public OyenteDeEnlacesDelFrontal(StringBuilder errores)
        {
            _errores = errores;
            PresentationTraceSources.Refresh();
            PresentationTraceSources.DataBindingSource.Listeners.Add(this);
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
        }

        public override void Write(string? message) => _errores.Append(message);

        public override void WriteLine(string? message) => _errores.AppendLine(message);

        protected override void Dispose(bool disposing)
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(this);
            base.Dispose(disposing);
        }
    }

    private sealed class AnalizadorQueCuenta : IAnalizadorDeEspectro
    {
        public int Inicios { get; private set; }

        public int Detenciones { get; private set; }

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

        public bool Leyendo { get; private set; }

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
