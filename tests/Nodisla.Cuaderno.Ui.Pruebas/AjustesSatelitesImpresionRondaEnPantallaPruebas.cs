using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Ui.Desarrollo;
using Nodisla.Cuaderno.Ui.VistaModelos;
using Nodisla.Cuaderno.Ui.Vistas;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// Lo que la ventana le da a la pantalla de ajustes por encima de su propio modelo: el resumen
/// del equipo y la consola del cluster, que se enlazan contra la ventana.
/// </summary>
public sealed class VentanaDeAjustesDeMentira
{
    /// <summary>El panel del equipo.</summary>
    public required VistaModeloEquipo Equipo { get; init; }

    /// <summary>El panel del cluster.</summary>
    public required VistaModeloCluster Cluster { get; init; }

    /// <summary>La seguridad de la transmisión, que Configuración › Equipo aloja (aquí no hay).</summary>
    public VistaModeloSeguridadTx? SeguridadTx => null;

    /// <summary>El servidor para otros programas, que Configuración aloja (aquí no hay).</summary>
    public VistaModeloServidores? Servidores => null;
}

/// <summary>
/// Las pestañas Ajustes, Satélites, Imprimir y Ronda DE VERDAD, pintadas en una ventana fuera
/// de la pantalla y sin activar, a los dos tamaños de pantalla del operador, y pulsadas boton a
/// boton por los caminos de la accesibilidad. Ni raton ni foco: no se le quita el ordenador a
/// nadie.
/// </summary>
[Collection(nameof(ColeccionDeLaVentana))]
public sealed class AjustesSatelitesImpresionRondaEnPantallaPruebas : IDisposable
{
    /// <summary>Lo que queda para la pestaña con la ventana a 1920×1000 y a 1366×768.</summary>
    public static readonly TheoryData<double, double> Tamanos = new()
    {
        { 1896, 740 },
        { 1330, 490 },
    };

    private readonly BancoDeAjustes _banco = new();

    /// <inheritdoc />
    public void Dispose() => _banco.Dispose();

    // ── Enlaces, órdenes y disposición de las cuatro pestañas ─────────────

    [Theory]
    [MemberData(nameof(Tamanos))]
    public Task Ajustes_sin_enlaces_rotos_sin_botones_muertos_y_sin_nada_cortado(double ancho, double alto) =>
        HiloDeVentana.Ejecutar(async () =>
        {
            var errores = new StringBuilder();
            using var oyente = new OyenteDeEnlacesDeAjustes(errores);

            var modelo = AjustesCompletos();
            var (ventana, panel) = Pintar(new PanelDeAjustes { DataContext = modelo }, ancho, alto, Ventana());
            try
            {
                // La configuración va por apartados: se recorren todos, uno a uno.
                for (var apartado = 0; apartado < VistaModeloAjustes.Apartados.Count; apartado++)
                {
                    modelo.IndiceDelApartado = apartado;
                    await Asentar();
                    foreach (var e in Todos<Expander>(panel)) e.IsExpanded = true;
                    await Asentar();

                    var donde = $"en el apartado «{VistaModeloAjustes.Apartados[apartado]}»";
                    errores.ToString().Should().BeEmpty($"cada enlace roto es un control o un dato muerto ({donde})");
                    EnlacesRotos(ventana).Should().BeEmpty($"cada enlace roto es un control o un dato muerto ({donde})");
                    SinBotonesMuertos(panel);
                    NadaCortadoDeLado(panel);

                    // Las credenciales se leen: la casilla tiene sitio para escribir y el título cabe.
                    foreach (var casilla in Todos<PasswordBox>(panel).Where(c => c.IsVisible))
                    {
                        casilla.ActualWidth.Should().BeGreaterThan(200, $"una casilla de contraseña estrecha no se usa ({donde})");
                    }
                }

                // Las cuentas van en tarjetas, una por servicio, y no en una lista hacia abajo.
                modelo.IndiceDelApartado = VistaModeloAjustes.ApartadoCuentas;
                await Asentar();
                modelo.Tarjetas.Select(t => t.Nombre).Should().Contain(["QRZ.com", "LoTW", "eQSL.cc", "Club Log", "HamQTH", "Cluster de DX"]);
                var cajas = Todos<PasswordBox>(panel).Where(c => c.IsVisible).ToList();
                cajas.Select(c => c.TranslatePoint(new Point(0, 0), panel).X).Distinct().Count()
                    .Should().BeGreaterThan(1, "las tarjetas se reparten en columnas");
            }
            finally
            {
                ventana.Close();
            }
        });

    [Theory]
    [MemberData(nameof(Tamanos))]
    public Task Satelites_sin_enlaces_rotos_y_sin_nada_cortado(double ancho, double alto) =>
        HiloDeVentana.Ejecutar(async () =>
        {
            var errores = new StringBuilder();
            using var oyente = new OyenteDeEnlacesDeAjustes(errores);

            var modelo = _banco.Satelites();
            await modelo.ActualizarElementosCommand.ExecuteAsync(null);
            var (ventana, panel) = Pintar(new PanelDeSatelites { DataContext = modelo }, ancho, alto);
            try
            {
                await Asentar();
                EnlacesRotos(ventana).Should().BeEmpty("sin nada elegido tampoco puede quedar ningún camino roto");

                // Se elige en la lista, como con el ratón: se enciende el panel de la derecha.
                var lista = Todos<ListBox>(panel).Single();
                lista.SelectedItem = modelo.Satelites.Single(f => f.Abreviatura == "SO-50");
                await Asentar();
                modelo.HaySateliteSeleccionado.Should().BeTrue();

                errores.ToString().Should().BeEmpty();
                EnlacesRotos(ventana).Should().BeEmpty();
                SinBotonesMuertos(panel);
                NadaCortadoDeLado(panel);

                Pulsar(panel, "Seguir");
                await Asentar();
                modelo.SiguiendoDoppler.Should().BeTrue();
                Buscar<Button>(panel, "Seguir").IsEnabled.Should().BeFalse();

                Pulsar(panel, "Soltar");
                await Asentar();
                modelo.SiguiendoDoppler.Should().BeFalse();

                Pulsar(panel, "Actualizar pasos");
                await Asentar();
            }
            finally
            {
                modelo.Detener();
                ventana.Close();
            }
        });

    [Theory]
    [MemberData(nameof(Tamanos))]
    public Task Imprimir_sin_enlaces_rotos_y_con_sus_casillas_vivas(double ancho, double alto) =>
        HiloDeVentana.Ejecutar(async () =>
        {
            var errores = new StringBuilder();
            using var oyente = new OyenteDeEnlacesDeAjustes(errores);

            var modelo = _banco.Impresion();
            modelo.SoloPendientesDeEnviar = false;
            var (ventana, panel) = Pintar(new PanelDeImpresion { DataContext = modelo }, ancho, alto);
            try
            {
                Buscar<Button>(panel, "Vista previa / Imprimir…").IsEnabled.Should().BeFalse("sin etiquetas");
                Pulsar(panel, "Buscar");
                await Esperar(() => modelo.Etiquetas.Count > 0);
                await Asentar();

                errores.ToString().Should().BeEmpty();
                EnlacesRotos(ventana).Should().BeEmpty();
                SinBotonesMuertos(panel);
                NadaCortadoDeLado(panel);

                // La casilla de la primera etiqueta, como un clic: el resumen se entera.
                var casilla = Todos<CheckBox>(Todos<ListBox>(panel).Single()).First();
                Clic(casilla);
                await Asentar();
                modelo.Etiquetas[0].Elegida.Should().BeFalse();
                modelo.ResumenTexto.Should().Contain($"{modelo.Etiquetas.Count - 1} marcada(s)");

                Pulsar(panel, "Ninguna");
                await Asentar();
                Buscar<Button>(panel, "Vista previa / Imprimir…").IsEnabled.Should().BeFalse("nada marcado");
                Pulsar(panel, "Marcar todas");
                await Asentar();

                Pulsar(panel, "Vista previa / Imprimir…");
                await Esperar(() => _banco.DocumentosAbiertos.Count > 0);
                _banco.DocumentosAbiertos.Should().ContainSingle();
            }
            finally
            {
                ventana.Close();
            }
        });

    [Theory]
    [MemberData(nameof(Tamanos))]
    public Task Ronda_se_abre_se_llena_y_se_cierra_desde_la_pantalla(double ancho, double alto) =>
        HiloDeVentana.Ejecutar(async () =>
        {
            var errores = new StringBuilder();
            using var oyente = new OyenteDeEnlacesDeAjustes(errores);

            var modelo = _banco.Ronda();
            await modelo.CargarAsync();
            var (ventana, panel) = Pintar(new PestanaRonda { DataContext = modelo }, ancho, alto);
            try
            {
                // Banda y modo se VEN: con el estilo de la casa, un desplegable editable salía vacío.
                foreach (var desplegable in Todos<ComboBox>(panel).Where(c => c.IsVisible))
                {
                    desplegable.SelectedItem.Should().NotBeNull($"«{AutomationProperties(desplegable)}» sale con algo elegido");
                }

                Buscar<Button>(panel, "Abrir ronda").IsEnabled.Should().BeFalse("sin nombre");
                modelo.NombreRonda = "Ronda de prueba";
                await Asentar();
                Pulsar(panel, "Abrir ronda");
                await Esperar(() => modelo.HayRondaAbierta);
                await Asentar();

                modelo.IndicativoNuevo = "EA8ABC";
                await Asentar();
                Pulsar(panel, "Añadir a la ronda");
                await Esperar(() => modelo.Participantes.Count == 1);
                await Asentar();

                // La lista de participantes se puede escribir: RST y comentario.
                var rejilla = Todos<DataGrid>(panel).First(d => d.IsVisible);
                rejilla.IsReadOnly.Should().BeFalse("el RST y el comentario se escriben en la lista");
                rejilla.Columns.Where(c => !c.IsReadOnly).Select(c => c.Header?.ToString())
                    .Should().BeEquivalentTo(["RST TX", "RST RX", "COMENTARIO"]);

                errores.ToString().Should().BeEmpty();
                EnlacesRotos(ventana).Should().BeEmpty();
                SinBotonesMuertos(panel);
                NadaCortadoDeLado(panel);

                Pulsar(panel, "Confirmar trabajado");
                await Esperar(() => modelo.Participantes[0].Trabajado);
                await Asentar();
                Buscar<Button>(panel, "Confirmar trabajado").IsEnabled.Should().BeFalse("ya está en el cuaderno");

                Pulsar(panel, "Cerrar ronda");
                await Esperar(() => !modelo.HayRondaAbierta);
                modelo.Historial.Should().ContainSingle();
            }
            finally
            {
                ventana.Close();
            }
        });

    [Fact]
    public Task Ajustes_con_los_simulados_no_monta_apartados_sin_modelo() => HiloDeVentana.Ejecutar(async () =>
    {
        // Con los puertos simulados no hay CAT ni cluster: antes sus vistas se montaban
        // igual, ocultas y con el contexto nulo, y dejaban medio centenar de enlaces rotos.
        var modelo = _banco.Configuracion(audio: _banco.Audio());
        var (ventana, panel) = Pintar(new PanelDeAjustes { DataContext = modelo }, 1330, 490, Ventana());
        try
        {
            await Asentar();
            EnlacesRotos(ventana).Should().BeEmpty();

            modelo.IndiceDelApartado = VistaModeloAjustes.ApartadoEquipo;
            await Asentar();
            EnlacesRotos(ventana).Should().BeEmpty();
            Todos<AjustesDelEquipo>(panel).Should().BeEmpty();

            modelo.IndiceDelApartado = VistaModeloAjustes.ApartadoAudio;
            await Asentar();
            Todos<AjustesDeAudio>(panel).Should().ContainSingle();

            modelo.IndiceDelApartado = VistaModeloAjustes.ApartadoCluster;
            await Asentar();
            EnlacesRotos(ventana).Should().BeEmpty();
            Todos<ConexionDelCluster>(panel).Should().BeEmpty();
            Todos<TextBlock>(panel).Should().Contain(t => t.IsVisible && t.Text.StartsWith("Con los puertos simulados el cluster", StringComparison.Ordinal));
        }
        finally
        {
            ventana.Close();
        }
    });

    // ── Lo que se pulsa en Ajustes ─────────────────────────────────────────

    [Fact]
    public Task Guardar_una_credencial_vacia_la_casilla_de_puntos() => HiloDeVentana.Ejecutar(async () =>
    {
        var modelo = AjustesCompletos();
        var (ventana, panel) = Pintar(new PanelDeAjustes { DataContext = modelo }, 1330, 490, Ventana());
        try
        {
            var casilla = Todos<PasswordBox>(panel).First();
            var guardar = Todos<Button>(panel).First(b => Equals(b.Content, "Guardar") && b.DataContext is SecretoDeServicio);
            guardar.IsEnabled.Should().BeFalse("sin nada tecleado");

            casilla.Password = "mi-clave";
            await Asentar();
            guardar.IsEnabled.Should().BeTrue();

            Invocar(guardar);
            await Asentar();

            _banco.Almacen.Guardadas.Values.Should().Contain("mi-clave");
            casilla.Password.Should().BeEmpty("guardado: los puntos se van");
        }
        finally
        {
            ventana.Close();
        }
    });

    [Fact]
    public Task Guardar_la_contrasena_del_cluster_vacia_su_casilla() => HiloDeVentana.Ejecutar(async () =>
    {
        var modelo = AjustesCompletos();
        var (ventana, panel) = Pintar(new PanelDeAjustes { DataContext = modelo }, 1330, 490, Ventana());
        try
        {
            modelo.IndiceDelApartado = VistaModeloAjustes.ApartadoCluster;
            await Asentar();
            var apartado = Todos<ConexionDelCluster>(panel).Single();
            var casilla = Todos<PasswordBox>(apartado).Single();

            casilla.Password = "del-nodo";
            await Asentar();
            Pulsar(apartado, "Guardar");
            await Asentar();

            _banco.Almacen.Guardadas[ClavesDeCredencial.ClusterContrasena].Should().Be("del-nodo");
            casilla.Password.Should().BeEmpty();
            modelo.Cluster!.EstadoDeLaContrasena.Should().Be("Guardada y cifrada");
        }
        finally
        {
            ventana.Close();
        }
    });

    [Fact]
    public Task Los_botones_de_ajustes_hacen_lo_que_dicen() => HiloDeVentana.Ejecutar(async () =>
    {
        var modelo = AjustesCompletos();
        var ruta = System.IO.Path.Combine(_banco.Carpeta, "desde-la-pantalla.adi");
        modelo.ElegirFicheroParaExportar = () => ruta;
        var (ventana, panel) = Pintar(new PanelDeAjustes { DataContext = modelo }, 1896, 740, Ventana());
        try
        {
            modelo.IndiceDelApartado = VistaModeloAjustes.ApartadoLibro;
            await Asentar();
            Pulsar(panel, "Exportar ADIF…");
            await Esperar(() => !modelo.Ocupado && System.IO.File.Exists(ruta));
            modelo.ParteDeLaImportacion.Should().Contain("contactos exportados");

            // «Volver a comprobar» va en la tarjeta de LoTW.
            modelo.IndiceDelApartado = VistaModeloAjustes.ApartadoCuentas;
            await Asentar();
            Pulsar(panel, "Volver a comprobar");
            await Asentar();
            modelo.MotivoDeNoPoderSubirALotw.Should().NotBeEmpty();

            // «Configurar…» de la tarjeta del cluster lleva a su apartado.
            Invocar(Todos<Button>(panel).First(b => b.IsVisible && Equals(b.Content, "Configurar…")
                && b.DataContext is TarjetaDeServicio { Nombre: "Cluster de DX" }));
            await Asentar();
            modelo.IndiceDelApartado.Should().Be(VistaModeloAjustes.ApartadoCluster);

            // CAT: probar (con un montaje de mentira), aplicar y descartar.
            modelo.IndiceDelApartado = VistaModeloAjustes.ApartadoEquipo;
            await Asentar();
            Pulsar(panel, "Probar");
            await Esperar(() => modelo.Cat!.HayParte && modelo.Cat.Resultado != ResultadoDePrueba.Probando);
            modelo.Cat!.HayParte.Should().BeTrue();
            Pulsar(panel, "Descartar");
            await Asentar();

            // Cluster: aplicar sin servidor avisa en rojo.
            modelo.Cluster!.NodoSeleccionado!.Servidor = string.Empty;
            modelo.Cluster.Indicativo = "EA8DLF";
            modelo.IndiceDelApartado = VistaModeloAjustes.ApartadoCluster;
            await Asentar();
            var apartado = Todos<ConexionDelCluster>(panel).Single();
            Pulsar(apartado, "Aplicar");
            await Esperar(() => modelo.Cluster.HayParte);
            modelo.Cluster.Fallo.Should().BeTrue();

            // Audio: probar el nivel y pararlo; guardar.
            modelo.IndiceDelApartado = VistaModeloAjustes.ApartadoAudio;
            await Asentar();
            Pulsar(panel, "Probar el nivel");
            await Esperar(() => modelo.Audio!.Probando);
            await Asentar();
            Pulsar(panel, "Parar la prueba");
            await Esperar(() => !modelo.Audio!.Probando);
            var audio = Todos<AjustesDeAudio>(panel).Single();
            Pulsar(audio, "Guardar");
            await Asentar();
            modelo.Audio!.Parte.Should().Be("Guardado.");
        }
        finally
        {
            modelo.Audio?.Detener();
            ventana.Close();
        }
    });

    // ── Montaje ───────────────────────────────────────────────────────────

    private VistaModeloAjustes AjustesCompletos() =>
        _banco.Configuracion(_banco.Cat(), _banco.Cluster(), _banco.Audio());

    private VentanaDeAjustesDeMentira Ventana()
    {
        var equipo = new EquipoSimulado();
        var fuente = new FuenteSpotsSimulada();
        return new VentanaDeAjustesDeMentira
        {
            Equipo = new VistaModeloEquipo(equipo, new VigilantePttDeDesarrollo(equipo)),
            Cluster = new VistaModeloCluster(new SeguirElCluster(
                fuente,
                new ConsultasDeInformeEnMemoria([], _banco.Dxcc),
                _banco.Dxcc)),
        };
    }

    // ── Utilidades ────────────────────────────────────────────────────────

    private static (Window Ventana, T Panel) Pintar<T>(T panel, double ancho, double alto, object? deLaVentana = null)
        where T : FrameworkElement
    {
        var ventana = new Window
        {
            Content = panel,
            DataContext = deLaVentana,
            Width = ancho,
            Height = alto,
            SizeToContent = SizeToContent.Manual,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -8000,
            Top = 0,
        };

        // El DataContext del panel se pone antes: el de la ventana no lo pisa.
        ventana.Show();
        ventana.UpdateLayout();
        return (ventana, panel);
    }

    private static async Task Asentar()
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    /// <summary>Espera a que se cumpla algo que hace una orden asincrona, con tope.</summary>
    private static async Task Esperar(Func<bool> condicion)
    {
        for (var i = 0; i < 200 && !condicion(); i++)
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await Task.Delay(10);
        }

        condicion().Should().BeTrue("la orden tenía que haber terminado");
    }

    private static void Pulsar(DependencyObject raiz, string rotulo) => Invocar(Buscar<Button>(raiz, rotulo));

    private static void Invocar(Button boton)
    {
        boton.IsEnabled.Should().BeTrue($"«{boton.Content}» tiene que poder pulsarse aquí");
        ((IInvokeProvider)new ButtonAutomationPeer(boton)).Invoke();
    }

    private static void Clic(ButtonBase control) =>
        typeof(ButtonBase).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(control, null);

    private static string AutomationProperties(DependencyObject control) =>
        System.Windows.Automation.AutomationProperties.GetName(control);

    /// <summary>
    /// Pregunta a cada enlace vivo como esta, igual que el retrato de la ventana: el rastreo de
    /// WPF, fuera del depurador, no siempre escribe.
    /// </summary>
    private static List<string> EnlacesRotos(DependencyObject raiz)
    {
        var rotos = new List<string>();
        var vistos = new HashSet<DependencyObject>();
        var pendientes = new Stack<DependencyObject>();
        pendientes.Push(raiz);

        while (pendientes.Count > 0)
        {
            var nodo = pendientes.Pop();
            if (!vistos.Add(nodo)) continue;

            var valores = nodo.GetLocalValueEnumerator();
            while (valores.MoveNext())
            {
                if (System.Windows.Data.BindingOperations.GetBindingExpression(nodo, valores.Current.Property) is
                    { Status: System.Windows.Data.BindingStatus.PathError or System.Windows.Data.BindingStatus.UpdateTargetError } b)
                {
                    rotos.Add($"{nodo.GetType().Name}.{valores.Current.Property.Name} <- {b.ParentBinding.Path?.Path} ({b.DataItem?.GetType().Name ?? "nulo"})");
                }
            }

            if (nodo is Visual)
            {
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(nodo); i++) pendientes.Push(VisualTreeHelper.GetChild(nodo, i));
            }

            foreach (var hijo in LogicalTreeHelper.GetChildren(nodo).OfType<DependencyObject>()) pendientes.Push(hijo);
        }

        return rotos;
    }

    /// <summary>Todo boton visible lleva orden: uno sin orden es un boton que no hace nada.</summary>
    private static void SinBotonesMuertos(DependencyObject panel)
    {
        var sinOrden = Todos<ButtonBase>(panel)
            .Where(b => b.IsVisible && b is not ToggleButton and not DataGridColumnHeader && b.TemplatedParent is null && b.Command is null)
            .Select(b => b.Content?.ToString() ?? b.GetType().Name)
            .ToList();

        sinOrden.Should().BeEmpty("todo botón tiene que hacer algo");
    }

    /// <summary>Ningun boton ni casilla visible se sale por la derecha de la pestaña.</summary>
    private static void NadaCortadoDeLado(FrameworkElement panel)
    {
        var cortados = Todos<Control>(panel)
            .Where(c => c.IsVisible && c is Button or ComboBox or TextBox or PasswordBox or CheckBox && c.TemplatedParent is null)
            .Where(c =>
            {
                var esquina = c.TransformToAncestor(panel).Transform(new Point(c.ActualWidth, 0));
                return esquina.X > panel.ActualWidth + 1;
            })
            .Select(c => $"{c.GetType().Name} «{(c as ContentControl)?.Content ?? System.Windows.Automation.AutomationProperties.GetName(c)}»")
            .ToList();

        cortados.Should().BeEmpty("nada se puede quedar cortado por la derecha");
    }

    private static T Buscar<T>(DependencyObject raiz, string rotulo) where T : ContentControl =>
        Todos<T>(raiz).First(c => c.IsVisible && string.Equals(c.Content?.ToString(), rotulo, StringComparison.Ordinal));

    private static IEnumerable<T> Todos<T>(DependencyObject raiz) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(raiz); i++)
        {
            var hijo = VisualTreeHelper.GetChild(raiz, i);
            if (hijo is T t) yield return t;
            foreach (var nieto in Todos<T>(hijo)) yield return nieto;
        }
    }

    /// <summary>Recoge los errores de enlace de WPF mientras vive.</summary>
    private sealed class OyenteDeEnlacesDeAjustes : TraceListener
    {
        private readonly StringBuilder _errores;

        public OyenteDeEnlacesDeAjustes(StringBuilder errores)
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
}
