using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Nodisla.Cuaderno.Ui.Vistas;
using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Ui.Ajustes;
using Nodisla.Cuaderno.Ui.Desarrollo;
using Nodisla.Cuaderno.Ui.VistaModelos;
using static Nodisla.Cuaderno.Ui.Pruebas.MontajeDeLaPestanaDigital;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// «No volver a preguntar» antes de transmitir y el contacto que se apunta solo al completarse.
/// Modem, salida y reloj de mentira: aqui no sale nada al aire.
/// </summary>
[Collection(nameof(ColeccionDeLaVentana))]
public sealed class PestanaDigitalGuardadoSoloPruebas
{
    private static readonly TimeSpan Paciencia = TimeSpan.FromSeconds(10);

    private static DateTimeOffset T(int segundos) => Mediodia.AddSeconds(segundos);

    // ── «No volver a preguntar» ───────────────────────────────────────────

    [Fact]
    public async Task NoVolverAPreguntarEvitaLaPreguntaYSeRecuerdaEnLosAjustes()
    {
        var carpeta = CarpetaTemporal();
        try
        {
            var ajustes = new AjustesDelPrograma();
            var modelo = Montar(ajustes, out var modem, out var reloj, out _);
            modelo.CarpetaDeDatos = carpeta;
            await Abrir(modelo);

            var preguntas = 0;
            modelo.ConfirmarQueVaATransmitir = _ =>
            {
                preguntas++;
                modelo.NoVolverAPreguntarAlTransmitir(); // lo que hace la ventana con la casilla marcada
                return true;
            };

            reloj.Ahora = T(0);
            modelo.MensajeAEmitir = "CQ EA8DLF IL18";
            await modelo.EmitirCommand.ExecuteAsync(null);
            preguntas.Should().Be(1);
            modem.Emisiones.Should().HaveCount(1);

            // La segunda emision ya no pregunta, pero sale igual por el mismo camino.
            reloj.Ahora = T(30);
            await modelo.EmitirCommand.ExecuteAsync(null);
            preguntas.Should().Be(1);
            modem.Emisiones.Should().HaveCount(2);

            modelo.PedirConfirmacionAlTransmitir.Should().BeFalse();
            AjustesDelPrograma.Leer(carpeta).Digital.PedirConfirmacionAlTransmitir.Should().BeFalse("se guarda en el fichero");

            // Sin preguntar, lo que es seguridad del equipo se sigue exigiendo: pestillo cerrado, no sale.
            modelo.PermitirTransmitir = false;
            reloj.Ahora = T(60);
            await modelo.EmitirCommand.ExecuteAsync(null);
            modem.Emisiones.Should().HaveCount(2);
        }
        finally
        {
            Directory.Delete(carpeta, recursive: true);
        }
    }

    [Fact]
    public void DeFabricaSePreguntaElPestilloArrancaCerradoYSeGuardaAlCompletar()
    {
        var d = new AjustesDelPrograma().Digital;
        d.PedirConfirmacionAlTransmitir.Should().BeTrue();
        d.RecordarPermisoDeTransmitir.Should().BeFalse();
        d.RegistrarAlCompletar.Should().BeTrue();

        Montar(new AjustesDelPrograma(), out _, out _, out _).PermitirTransmitir.Should().BeFalse();
    }

    [Fact]
    public void RecordarElPermisoDejaElPestilloAbiertoAlArrancar()
    {
        var ajustes = new AjustesDelPrograma();
        ajustes.Digital.RecordarPermisoDeTransmitir = true;
        Montar(ajustes, out _, out _, out _).PermitirTransmitir.Should().BeTrue();
    }

    [Fact]
    public Task ElDialogoDeTransmitirOfreceNoVolverAPreguntar() => HiloDeVentana.Ejecutar(async () =>
    {
        var dialogo = new VentanaDeConfirmacion
        {
            Titulo = "El módem va a transmitir",
            Detalle = "Se va a emitir «CQ EA8DLF IL18».\n\n" +
                      "Un período de FT8 son TRECE SEGUNDOS con el equipo en antena, sin pausa.\n\n" +
                      "Compruebe que hay una antena o una carga artificial conectada y que el reloj " +
                      "está en hora: con el reloj desviado se transmite fuera de ventana y se molesta " +
                      "a las demás estaciones sin enterarse.\n\n" +
                      "La transmisión va vigilada: se suelta sola si algo va mal, y el botón " +
                      "«SOLTAR PTT» la corta en cualquier momento.",
            TextoDeAceptar = "Sí, transmitir",
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -8000,
            Top = 0,
        };

        dialogo.OfrecerNoVolverAPreguntar.Should().BeFalse("los demás diálogos no la llevan");
        dialogo.OfrecerNoVolverAPreguntar = true;
        dialogo.NoVolverAPreguntar.Should().BeFalse("de entrada se sigue preguntando");
        dialogo.NoVolverAPreguntar = true;

        dialogo.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            dialogo.UpdateLayout();
            dialogo.NoVolverAPreguntar.Should().BeTrue();

            if (Environment.GetEnvironmentVariable("CUADERNO_CAPTURA_DIALOGO") is { Length: > 0 } ruta)
            {
                Retratar((FrameworkElement)dialogo.Content, dialogo.Background, ruta);
            }
        }
        finally
        {
            dialogo.Close();
        }
    });

    private static void Retratar(FrameworkElement contenido, Brush fondo, string ruta)
    {
        var ancho = contenido.ActualWidth + contenido.Margin.Left + contenido.Margin.Right;
        var alto = contenido.ActualHeight + contenido.Margin.Top + contenido.Margin.Bottom;
        var dibujo = new DrawingVisual();
        using (var lienzo = dibujo.RenderOpen())
        {
            lienzo.DrawRectangle(fondo, null, new Rect(0, 0, ancho, alto));
            lienzo.DrawRectangle(new VisualBrush(contenido), null, new Rect(contenido.Margin.Left, contenido.Margin.Top, contenido.ActualWidth, contenido.ActualHeight));
        }

        var imagen = new RenderTargetBitmap((int)Math.Ceiling(ancho), (int)Math.Ceiling(alto), 96, 96, PixelFormats.Pbgra32);
        imagen.Render(dibujo);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(imagen));
        using var fichero = File.Create(ruta);
        png.Save(fichero);
    }

    // ── Guardar solo al completarse ───────────────────────────────────────

    [Fact]
    public async Task UnContactoCompletoSeGuardaSoloUnaVezConTodosLosCampos()
    {
        var modelo = Montar(new AjustesDelPrograma(), out var modem, out var reloj, out var cuaderno);
        await Abrir(modelo);
        modelo.PonerElDial(Frecuencia.DesdeMegahercios(14.074m));
        var avisos = 0;
        modelo.CuadernoCambiado += (_, _) => avisos++;

        // 12:00:00 CQ EA5XYZ; se le contesta en las impares.
        reloj.Ahora = T(16);
        await Ventana(modelo, () => modem.Soltar(T(0), Oido("CQ EA5XYZ IM98", -7, 800, T(0))));
        modelo.DecodificacionElegida = modelo.Decodificaciones.Single(f => f.Texto.StartsWith("CQ", StringComparison.Ordinal));
        await modelo.PrepararRespuestaCommand.ExecuteAsync(null);

        reloj.Ahora = T(46);
        await Ventana(modelo, () => modem.Soltar(T(30), Oido("EA8DLF EA5XYZ -12", -9, 800, T(30))));
        (await Contactos(cuaderno)).Should().BeEmpty("con el informe aun no esta completo");

        // RR73 del corresponsal: completo, se guarda solo.
        reloj.Ahora = T(76);
        await Ventana(modelo, () => modem.Soltar(T(60), Oido("EA8DLF EA5XYZ RR73", -8, 800, T(60))));
        modem.Emisiones.Should().HaveCount(3, "el 73 propio sale igual");

        await modelo.GuardadoEnCurso;
        var qso = (await Contactos(cuaderno)).Should().ContainSingle(modelo.Aviso).Subject;
        qso.Call.Valor.Should().Be("EA5XYZ");
        qso.Gridsquare.ToString().Should().Be("IM98");
        qso.RstSent.ToString().Should().Be("-07");
        qso.RstRcvd.ToString().Should().Be("-12");
        qso.Freq.Hercios.Should().Be(14_074_000 + 800, "dial más tono de transmisión");
        qso.Band.Nombre.Should().Be("20m");
        qso.Mode.Principal.Should().Be("FT8");
        qso.Mode.Submodo.Should().BeNull();
        qso.InicioUtc.Should().Be(T(0), "empieza con el CQ al que se contestó");
        qso.FinUtc.Should().Be(T(76));
        modelo.Aviso.Should().Be("Guardado: EA5XYZ 20m FT8");
        avisos.Should().Be(1, "el mismo evento que el guardado manual refresca cuaderno, mapa y diplomas");

        // Repite el RR73 y el operador le vuelve a contestar con doble clic: no se duplica.
        reloj.Ahora = T(106);
        await Ventana(modelo, () => modem.Soltar(T(90), Oido("EA8DLF EA5XYZ RR73", -8, 800, T(90))));
        modelo.DecodificacionElegida = modelo.Decodificaciones.First(f => f.Texto == "EA8DLF EA5XYZ RR73");
        await modelo.PrepararRespuestaCommand.ExecuteAsync(null);
        reloj.Ahora = T(136);
        await Ventana(modelo, () => modem.Soltar(T(120), Oido("EA8DLF EA5XYZ 73", -8, 800, T(120))));

        await modelo.GuardadoEnCurso;
        (await Contactos(cuaderno)).Should().ContainSingle();
        avisos.Should().Be(1);

        // Y el boton manual tampoco lo mete dos veces.
        await modelo.RegistrarContactoCommand.ExecuteAsync(null);
        (await Contactos(cuaderno)).Should().ContainSingle();
    }

    [Fact]
    public async Task TrasRecibirRInformeSeGuardaAlSalirElRr73Propio()
    {
        var modelo = Montar(new AjustesDelPrograma(), out var modem, out var reloj, out var cuaderno);
        await Abrir(modelo);
        modelo.PonerElDial(Frecuencia.DesdeMegahercios(7.074m));

        // EA5XYZ me llama con su localizador: le contesto con el informe.
        reloj.Ahora = T(16);
        await Ventana(modelo, () => modem.Soltar(T(0), Oido("EA8DLF EA5XYZ IM98", -3, 1200, T(0))));
        modelo.DecodificacionElegida = modelo.Decodificaciones.Single();
        await modelo.PrepararRespuestaCommand.ExecuteAsync(null);
        modem.Emisiones.Should().ContainSingle();

        // R+informe: completo, pero se apunta cuando salga mi RR73. Este llega tarde y pierde turno.
        reloj.Ahora = T(49);
        await Ventana(modelo, () => modem.Soltar(T(30), Oido("EA8DLF EA5XYZ R-05", -4, 1200, T(30))));
        modem.Emisiones.Should().ContainSingle();
        (await Contactos(cuaderno)).Should().BeEmpty("el RR73 propio aún no ha salido");

        reloj.Ahora = T(61);
        await Ventana(modelo, () => modem.Soltar(T(45)));
        reloj.Ahora = T(76);
        await Ventana(modelo, () => modem.Soltar(T(60)));
        modem.Emisiones.Should().HaveCount(2);
        modem.Emisiones[1].Texto.Should().EndWith("RR73");

        await modelo.GuardadoEnCurso;
        var qso = (await Contactos(cuaderno)).Should().ContainSingle(modelo.Aviso).Subject;
        qso.RstRcvd.ToString().Should().Be("-05");
        qso.Band.Nombre.Should().Be("40m");
        qso.InicioUtc.Should().Be(T(0));
        qso.FinUtc.Should().Be(T(76));
    }

    [Fact]
    public async Task UnContactoAbandonadoNoSeGuarda()
    {
        var modelo = Montar(new AjustesDelPrograma(), out var modem, out var reloj, out var cuaderno);
        await Abrir(modelo);
        modelo.PonerElDial(Frecuencia.DesdeMegahercios(14.074m));

        reloj.Ahora = T(16);
        await Ventana(modelo, () => modem.Soltar(T(0), Oido("CQ EA5XYZ IM98", -7, 800, T(0))));
        modelo.DecodificacionElegida = modelo.Decodificaciones.Single();
        await modelo.PrepararRespuestaCommand.ExecuteAsync(null);

        reloj.Ahora = T(46);
        await Ventana(modelo, () => modem.Soltar(T(30), Oido("EA8DLF EA5XYZ -12", -9, 800, T(30))));

        // Desaparece: ventanas vacias hasta que el vigilante para la secuencia.
        for (var n = 1; n <= 12 && modelo.TxHabilitado; n++)
        {
            reloj.Ahora = T(46 + (30 * n));
            await Ventana(modelo, () => modem.Soltar(T(30 + (30 * n))));
        }

        modelo.TxHabilitado.Should().BeFalse("el vigilante la ha parado");
        modelo.Aviso.Should().Contain("desaparecido");
        (await Contactos(cuaderno)).Should().BeEmpty();
    }

    [Fact]
    public async Task ConLaOpcionApagadaNoSeGuardaSolo()
    {
        var ajustes = new AjustesDelPrograma();
        ajustes.Digital.RegistrarAlCompletar = false;
        var modelo = Montar(ajustes, out var modem, out var reloj, out var cuaderno);
        await Abrir(modelo);
        modelo.PonerElDial(Frecuencia.DesdeMegahercios(14.074m));

        reloj.Ahora = T(16);
        await Ventana(modelo, () => modem.Soltar(T(0), Oido("CQ EA5XYZ IM98", -7, 800, T(0))));
        modelo.DecodificacionElegida = modelo.Decodificaciones.Single();
        await modelo.PrepararRespuestaCommand.ExecuteAsync(null);
        reloj.Ahora = T(46);
        await Ventana(modelo, () => modem.Soltar(T(30), Oido("EA8DLF EA5XYZ -12", -9, 800, T(30))));
        reloj.Ahora = T(76);
        await Ventana(modelo, () => modem.Soltar(T(60), Oido("EA8DLF EA5XYZ RR73", -8, 800, T(60))));

        modelo.ContactoCompleto.Should().BeTrue();
        (await Contactos(cuaderno)).Should().BeEmpty();

        await modelo.RegistrarContactoCommand.ExecuteAsync(null);
        (await Contactos(cuaderno)).Should().ContainSingle();
    }

    // ── Montaje ───────────────────────────────────────────────────────────

    private static VistaModeloModemPropio Montar(
        AjustesDelPrograma ajustes,
        out ModemApuntador modem,
        out RelojManual reloj,
        out RepositorioQsoEnMemoria cuaderno)
    {
        reloj = new RelojManual(Mediodia);
        modem = new ModemApuntador(reloj);
        cuaderno = new RepositorioQsoEnMemoria([]);

        return new VistaModeloModemPropio(
            new VistaModeloRelojDigital(reloj, null),
            ajustes,
            new ConsultarTrabajadoAntes(cuaderno),
            new RegistrarQso(cuaderno, new RepositorioEstacionEnMemoria()),
            EstadoDelCorrector.NoProcede,
            modem,
            entrada: null,
            salida: new SalidaDeAudioSimulada())
        {
            ConfirmarQueVaATransmitir = _ => false,
            MiIndicativo = Indicativo.Parse("EA8DLF"),
            MiLocalizador = Locator.Parse("IL18"),
        };
    }

    /// <summary>Escuchando, con el pestillo abierto y diciendo que si a la pregunta.</summary>
    private static async Task Abrir(VistaModeloModemPropio modelo)
    {
        await modelo.EscucharCommand.ExecuteAsync(null);
        modelo.PermitirTransmitir = true;
        modelo.ConfirmarQueVaATransmitir = _ => true;
    }

    private static async Task<IReadOnlyList<Qso>> Contactos(RepositorioQsoEnMemoria cuaderno) =>
        (await cuaderno.BuscarAsync(new CriterioQso(), 0, 50)).Elementos;

    private static string CarpetaTemporal()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "cuaderno-pruebas-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(carpeta);
        return carpeta;
    }

    private static async Task Ventana(VistaModeloModemPropio modelo, Action soltar)
    {
        var aviso = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void AlProcesar(object? _, DateTimeOffset __) => aviso.TrySetResult();

        modelo.VentanaProcesada += AlProcesar;
        try
        {
            soltar();
            await aviso.Task.WaitAsync(Paciencia);
        }
        finally
        {
            modelo.VentanaProcesada -= AlProcesar;
        }
    }
}
