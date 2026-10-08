using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using static Nodisla.Cuaderno.Ui.Pruebas.MontajeDeLaPestanaDigital;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// Las seis funciones pedidas el 08-10-2026 para el panel del modem propio: AGCc, Filtrar,
/// Tune, modo SWL, Bypass y «Sincronizar» manual.
/// </summary>
public sealed class PanelDelModemFuncionesNuevasPruebas
{
    // ── Modo SWL: nada de Tx mientras este encendido ─────────────────────────

    [Fact]
    public async Task ModoSwlImpideHabilitarTxYParaLaSecuenciaEnCurso()
    {
        var modelo = Montar(out var modem, out var reloj, out _);
        await modelo.EscucharCommand.ExecuteAsync(null);
        modelo.PermitirTransmitir = true;
        modelo.ConfirmarQueVaATransmitir = _ => true;

        await modelo.LlamarCqCommand.ExecuteAsync(null);
        modelo.TxHabilitado.Should().BeTrue("sin SWL, llamar CQ habilita Tx con normalidad");

        modelo.ModoSwl = true;

        modelo.TxHabilitado.Should().BeFalse("encender SWL para la secuencia en curso");
        modelo.SePuedeEmitir.Should().BeFalse();

        // Con SWL encendido, ni «Tx habilitado» ni «Llamar CQ» vuelven a activar nada.
        await modelo.AlternarTxHabilitadoCommand.ExecuteAsync(null);
        modelo.TxHabilitado.Should().BeFalse();

        await modelo.LlamarCqCommand.ExecuteAsync(null);
        modelo.TxHabilitado.Should().BeFalse();
        modem.Emisiones.Should().BeEmpty("en SWL no sale nada al aire");

        // Al apagarlo, vuelve a poder habilitarse con normalidad.
        modelo.ModoSwl = false;
        modelo.SePuedeEmitir.Should().BeTrue();
    }

    [Fact]
    public void ElAvisoDeTransmisionDiceModoSwlCuandoEstaEncendido()
    {
        var modelo = Montar(out _, out _, out _);
        modelo.ModoSwl = true;
        modelo.AvisoDeLaTransmision.Should().NotBeNullOrEmpty();
    }

    // ── Bypass: ignora el dial del CAT, frecuencia fija ──────────────────────

    [Fact]
    public void BypassIgnoraElDialDelEquipoYUsaLaFrecuenciaFija()
    {
        var modelo = Montar(out var modem, out _, out _);

        modelo.PonerElDial(Frecuencia.DesdeMegahercios(14.074m));
        modem.FrecuenciaDelDial.Megahercios.Should().Be(14.074m);

        modelo.BypassDeCat = true;
        modelo.FrecuenciaDeBypassMhz.Should().Be(14.074, "al encender el bypass se parte del dial de ahora mismo");

        // El CAT dice que el equipo se ha movido, pero con bypass encendido el modem no se entera.
        modelo.PonerElDial(Frecuencia.DesdeMegahercios(7.074m));
        modem.FrecuenciaDelDial.Megahercios.Should().Be(14.074m, "con bypass, el CAT se ignora");

        // El operador fija a mano otra frecuencia.
        modelo.FrecuenciaDeBypassMhz = 21.074;
        modem.FrecuenciaDelDial.Megahercios.Should().Be(21.074m);

        // Al apagar el bypass, el siguiente aviso del CAT vuelve a mandar.
        modelo.BypassDeCat = false;
        modelo.PonerElDial(Frecuencia.DesdeMegahercios(7.074m));
        modem.FrecuenciaDelDial.Megahercios.Should().Be(7.074m);
    }

    // ── AGCc y Filtrar: se pasan al modem en caliente ────────────────────────

    [Fact]
    public void AgcCYFiltrarSePasanAlModemEnCaliente()
    {
        var modelo = Montar(out var modem, out _, out _);

        modelo.AgcActivo = true;
        modem.AgcActivo.Should().BeTrue();
        modelo.AgcActivo = false;
        modem.AgcActivo.Should().BeFalse();

        modelo.FiltroActivo = true;
        modem.FiltroActivo.Should().BeTrue();

        modelo.FiltroDesdeHz = 300;
        modem.FiltroDesdeHz.Should().Be(300);

        modelo.FiltroHastaHz = 2500;
        modem.FiltroHastaHz.Should().Be(2500);
    }

    // ── Tune: tono continuo, excluye cualquier otra emision mientras suena ──

    [Fact]
    public async Task TuneOcupaLaAntenaEImpideEmitirHastaQueSePara()
    {
        var modelo = Montar(out var modem, out _, out _);
        await modelo.EscucharCommand.ExecuteAsync(null);
        modelo.PermitirTransmitir = true;
        modelo.ConfirmarQueVaATransmitir = _ => true;
        modelo.TonoDeTransmision = 1500;

        modelo.SePuedeEmitir.Should().BeTrue("antes de pulsar Tune, se puede emitir con normalidad");
        var tarea = modelo.AlternarTonoDeAjusteCommand.ExecuteAsync(null);

        await EsperarA(() => modelo.TonoDeAjusteActivo, "Tune no llegó a arrancar");

        modem.TonosDeAjustePedidos.Should().ContainSingle().Which.Should().Be(1500);
        modelo.SePuedeEmitir.Should().BeFalse("mientras Tune suena no se puede llamar CQ ni emitir un mensaje");

        // Segunda pulsacion del mismo boton: para el tono.
        await modelo.AlternarTonoDeAjusteCommand.ExecuteAsync(null);
        await tarea;

        modelo.TonoDeAjusteActivo.Should().BeFalse();
        modelo.SePuedeEmitir.Should().BeTrue("al parar Tune, se puede volver a emitir");
    }

    [Fact]
    public async Task ModoSwlImpideEmpezarUnTuneNuevo()
    {
        var modelo = Montar(out _, out _, out _);
        await modelo.EscucharCommand.ExecuteAsync(null);
        modelo.PermitirTransmitir = true;
        modelo.ConfirmarQueVaATransmitir = _ => true;
        modelo.ModoSwl = true;

        modelo.AlternarTonoDeAjusteCommand.CanExecute(null).Should().BeFalse();
    }

    // ── Sincronizar: ajuste de ventana a partir del desfase elegido ──────────

    [Fact]
    public void SincronizarCorrigeElAjusteDeVentanaSegunElDesfaseElegido()
    {
        var modelo = Montar(out var modem, out var reloj, out _);
        var decodificacion = new DecodificacionPropia("CQ EA5XYZ IM98", -7, 0.35, 800, ModoDelModem.Ft8, reloj.Ahora) { EsCq = true };
        modelo.DecodificacionElegida = new Nodisla.Cuaderno.Ui.VistaModelos.FilaDeDecodificacionPropia(decodificacion);

        modelo.SincronizarConLaDecodificacionCommand.Execute(null);

        modem.AjusteDeVentana.Should().BeCloseTo(TimeSpan.FromSeconds(-0.35), TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public void SincronizarSinNadaElegidoNoHaceNada()
    {
        var modelo = Montar(out var modem, out _, out _);
        modelo.SincronizarConLaDecodificacionCommand.CanExecute(null).Should().BeFalse();
        modem.AjusteDeVentana.Should().Be(TimeSpan.Zero);
    }

    private static async Task EsperarA(Func<bool> condicion, string mensajeSiNoLlega)
    {
        for (var i = 0; i < 200 && !condicion(); i++) await Task.Delay(10);
        condicion().Should().BeTrue(mensajeSiNoLlega);
    }
}
