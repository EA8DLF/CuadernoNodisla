using System.IO;
using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Ui.Ajustes;
using Nodisla.Cuaderno.Ui.Desarrollo;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// Pruebas del apartado de audio y modos digitales: lo que se guarda y lo que avisa.
/// </summary>
/// <remarks>
/// Ni una de estas pruebas abre una tarjeta de sonido: la entrada y la salida son de mentira.
/// Que probar el nivel abra el microfono es una accion del operador, no algo que pase por
/// montar el modelo de vista, y eso es justamente lo que se comprueba aqui.
/// </remarks>
public sealed class AjustesDeAudioYDigitalPruebas : IDisposable
{
    private readonly string _carpeta = Path.Combine(
        Path.GetTempPath(),
        "CuadernoNodislaPruebas",
        Guid.NewGuid().ToString("N"));

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_carpeta)) Directory.Delete(_carpeta, recursive: true);
        }
        catch (IOException)
        {
            // Que no se pueda borrar una carpeta temporal no puede hacer fallar una prueba.
        }
    }

    [Fact]
    public void LosAjustesDeFabricaSonLosDeFt8A48000()
    {
        var ajustes = new AjustesDelPrograma();

        ajustes.Digital.FrecuenciaDeMuestreo.Should().Be(48000);
        ajustes.Digital.Modo.Should().Be(ModoDelModem.Ft8);
    }

    [Fact]
    public void AcotarDejaLosValoresRarosEnSuSitio()
    {
        var digital = new AjustesDeDigital
        {
            FrecuenciaDeMuestreo = 44100,
            TonoDeTransmisionHz = 9000,
        }.Acotar();

        digital.FrecuenciaDeMuestreo.Should().Be(48000);
        digital.TonoDeTransmisionHz.Should().Be(3000);

        new AjustesDeDigital { TonoDeTransmisionHz = 10 }.Acotar().TonoDeTransmisionHz.Should().Be(200);
    }

    [Fact]
    public void LoQueSeGuardaSeVuelveALeer()
    {
        var ajustes = new AjustesDelPrograma();
        ajustes.Digital.DispositivoDeEntrada = "el-codec-del-equipo";
        ajustes.Digital.NombreDeEntrada = "Codec USB del FT-710";
        ajustes.Digital.Modo = ModoDelModem.Ft4;
        ajustes.Digital.TonoDeTransmisionHz = 1200;
        ajustes.Digital.SaltarTx1 = true;
        ajustes.Digital.PskReporter = true;
        ajustes.Digital.Operacion = Nodisla.Cuaderno.Ui.Digital.TipoDeOperacion.Hound;
        ajustes.Digital.FrecuenciasDeTrabajo.Add(new Nodisla.Cuaderno.Ui.Digital.FrecuenciaDeTrabajo { Modo = ModoDelModem.Ft8, Megahercios = 14.090m, Nota = "propia" });
        ajustes.Guardar(_carpeta);

        var leidos = AjustesDelPrograma.Leer(_carpeta);

        leidos.Digital.SaltarTx1.Should().BeTrue();
        leidos.Digital.PskReporter.Should().BeTrue();
        leidos.Digital.Operacion.Should().Be(Nodisla.Cuaderno.Ui.Digital.TipoDeOperacion.Hound);
        leidos.Digital.FrecuenciasDeTrabajo.Should().Contain(f => f.Megahercios == 14.090m && f.Nota == "propia");
        leidos.Digital.DispositivoDeEntrada.Should().Be("el-codec-del-equipo");
        leidos.Digital.NombreDeEntrada.Should().Be("Codec USB del FT-710");
        leidos.Digital.Modo.Should().Be(ModoDelModem.Ft4);
        leidos.Digital.TonoDeTransmisionHz.Should().Be(1200);
    }

    [Fact]
    public void UnFicheroSinApartadoDeAudioNoRevienta()
    {
        Directory.CreateDirectory(_carpeta);
        File.WriteAllText(
            Path.Combine(_carpeta, AjustesDelPrograma.NombreDelFichero),
            """{ "Equipo": { "Via": "Ninguna" } }""");

        var leidos = AjustesDelPrograma.Leer(_carpeta);

        leidos.Digital.Should().NotBeNull();
        leidos.Digital.PskReporter.Should().BeFalse("la red saliente viene apagada");
        leidos.Digital.FrecuenciasDeTrabajo.Should().Contain(f => f.Megahercios == 14.074m);
    }

    [Fact]
    public void MontarElApartadoNoAbreNingunDispositivo()
    {
        var entrada = new EntradaDeAudioSimulada();
        var modelo = new VistaModeloAjustesAudio(new AjustesDelPrograma(), _carpeta, entrada, new SalidaDeAudioSimulada());

        entrada.Abierto.Should().BeNull("montar la pantalla de ajustes no puede abrirle el micrófono a nadie");
        modelo.Probando.Should().BeFalse();
        modelo.Nivel.Should().Be(0);
    }

    [Fact]
    public void ElDispositivoDelEquipoSaleElegidoSinHaberloGuardado()
    {
        var modelo = new VistaModeloAjustesAudio(
            new AjustesDelPrograma(), _carpeta, new EntradaDeAudioSimulada(), new SalidaDeAudioSimulada());

        modelo.Entradas.Should().NotBeEmpty();
        modelo.EntradaElegida.Should().NotBeNull();
        modelo.EntradaElegida!.EsDelEquipo.Should().BeTrue("el codec del equipo es el que se quiere casi siempre");
        modelo.SalidaElegida!.EsDelEquipo.Should().BeTrue();
    }

    [Fact]
    public void AvisaSiLaEntradaYLaSalidaNoSonElMismoAparato()
    {
        var modelo = new VistaModeloAjustesAudio(
            new AjustesDelPrograma(), _carpeta, new EntradaDeAudioSimulada(), new SalidaDeAudioSimulada());

        modelo.HayDiscrepanciaDeDispositivos.Should().BeFalse(
            "por defecto entrada y salida salen las dos marcadas como del equipo");
        modelo.AvisoDeDiscrepancia.Should().BeEmpty();

        // Es justo el fallo real del operador: la entrada acierta con el códec del equipo pero la
        // salida se queda en otro aparato (en su caso, el televisor).
        modelo.SalidaElegida = modelo.Salidas.First(d => !d.EsDelEquipo);

        modelo.HayDiscrepanciaDeDispositivos.Should().BeTrue();
        modelo.AvisoDeDiscrepancia.Should().Contain("no parecen del mismo aparato");
    }

    [Fact]
    public async Task ProbarElNivelAbreLaEntradaYPararLaCierra()
    {
        var entrada = new EntradaDeAudioSimulada();
        var modelo = new VistaModeloAjustesAudio(new AjustesDelPrograma(), _carpeta, entrada, new SalidaDeAudioSimulada());

        await modelo.ProbarElNivelCommand.ExecuteAsync(null);

        entrada.Abierto.Should().NotBeNull();
        modelo.Probando.Should().BeTrue();

        await modelo.PararLaPruebaCommand.ExecuteAsync(null);

        entrada.Abierto.Should().BeNull("parar tiene que soltar la tarjeta de sonido");
        modelo.Probando.Should().BeFalse();
    }

    [Fact]
    public void ElMedidorAvisaDeLaSaturacion()
    {
        var modelo = new VistaModeloAjustesAudio(
            new AjustesDelPrograma(), _carpeta, new EntradaDeAudioSimulada(), new SalidaDeAudioSimulada());

        // El nivel que de verdad tenia esta estacion: al borde de recortar.
        modelo.Nivel = 0.94;

        modelo.NivelSatura.Should().BeTrue();
        modelo.NivelTexto.Should().Contain("SATURANDO");
        modelo.NivelPorCiento.Should().BeApproximately(94, 0.001);
    }

    [Fact]
    public void ElMedidorNoSeQuejaConUnNivelBueno()
    {
        var modelo = new VistaModeloAjustesAudio(
            new AjustesDelPrograma(), _carpeta, new EntradaDeAudioSimulada(), new SalidaDeAudioSimulada());

        modelo.Nivel = 0.35;

        modelo.NivelSatura.Should().BeFalse();
        modelo.NivelCorto.Should().BeFalse();
        modelo.NivelTexto.Should().Contain("bien");
    }

    [Fact]
    public void GuardarDejaEscritoElDispositivo()
    {
        var ajustes = new AjustesDelPrograma();
        var modelo = new VistaModeloAjustesAudio(
            ajustes, _carpeta, new EntradaDeAudioSimulada(), new SalidaDeAudioSimulada());
        modelo.GuardarCommand.Execute(null);

        var leidos = AjustesDelPrograma.Leer(_carpeta);
        leidos.Digital.DispositivoDeEntrada.Should().Be("simulado-ft710-entrada");
        leidos.Digital.NombreDeEntrada.Should().Contain("FT-710");
    }
}
