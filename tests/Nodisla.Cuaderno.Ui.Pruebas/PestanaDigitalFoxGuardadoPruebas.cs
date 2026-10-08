using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Ui.Desarrollo;
using Nodisla.Cuaderno.Ui.Digital;
using Nodisla.Cuaderno.Ui.VistaModelos;
using static Nodisla.Cuaderno.Ui.Pruebas.MontajeDeLaPestanaDigital;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// El lado fox registra solo, sin que el operador tenga que pulsar nada, a cada cazador del
/// pileup en cuanto recibe su RR73. Modem, salida y reloj de mentira: aqui no sale nada al aire.
/// </summary>
public sealed class PestanaDigitalFoxGuardadoPruebas
{
    private static readonly TimeSpan Periodo = TimeSpan.FromSeconds(15);

    /// <summary>Arranca el pileup de fox y dice en que paridad cae la ventana propia del fox.</summary>
    private static async Task<int> EmpezarFoxAsync(VistaModeloModemPropio modelo)
    {
        modelo.OperacionElegida = modelo.Operaciones.Single(o => o.Valor == TipoDeOperacion.Fox);
        await modelo.LlamarCqCommand.ExecuteAsync(null);
        return modelo.SecuenciadorDeFox.Paridad;
    }

    /// <summary>La primera ventana, en o despues de la referencia, en la que de verdad hablan los cazadores.</summary>
    private static DateTimeOffset PrimeraVentanaDeCazador(DateTimeOffset referencia, int paridadFox) =>
        SecuenciadorDeQso.ParidadDe(referencia, Periodo) != paridadFox ? referencia : referencia + Periodo;

    private static async Task Ventana(VistaModeloModemPropio modelo, Action soltar)
    {
        var aviso = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void AlProcesar(object? _, DateTimeOffset __) => aviso.TrySetResult();

        modelo.VentanaProcesada += AlProcesar;
        try
        {
            soltar();
            await aviso.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            modelo.VentanaProcesada -= AlProcesar;
        }
    }

    private static async Task<IReadOnlyList<Dominio.Entidades.Qso>> Contactos(RepositorioQsoEnMemoria cuaderno) =>
        (await cuaderno.BuscarAsync(new CriterioQso(), 0, 50)).Elementos;

    [Fact]
    public async Task UnCazadorQueCompletaSeRegistraSoloConSusPropiosCampos()
    {
        var modelo = Montar(out var modem, out var reloj, out var cuaderno);
        modelo.PonerElDial(Frecuencia.DesdeMegahercios(14.074m));
        var avisos = 0;
        modelo.CuadernoCambiado += (_, _) => avisos++;

        reloj.Ahora = Mediodia;
        var paridadFox = await EmpezarFoxAsync(modelo);
        var v0 = PrimeraVentanaDeCazador(Mediodia, paridadFox);

        // 1) EA5XYZ llama por primera vez: se le manda el informe y se le asigna un tono propio.
        reloj.Ahora = v0 + Periodo + TimeSpan.FromSeconds(1);
        await Ventana(modelo, () => modem.Soltar(v0, Oido("EA8DLF EA5XYZ IM98", -7, 1500, v0)));

        modelo.Cazadores.Should().ContainSingle(c => c.Indicativo == "EA5XYZ");
        var tonoAsignado = modelo.SecuenciadorDeFox.Cazadores["EA5XYZ"].TonoHz;

        // 2) Contesta con R+informe: el fox manda RR73 y lo da por completo sin esperar nada mas.
        var v1 = v0 + (2 * Periodo);
        reloj.Ahora = v1 + Periodo + TimeSpan.FromSeconds(1);
        await Ventana(modelo, () => modem.Soltar(v1, Oido("EA8DLF EA5XYZ R-12", -9, 1500, v1)));

        await modelo.GuardadoEnCurso;
        var qso = (await Contactos(cuaderno)).Should().ContainSingle(modelo.Aviso).Subject;

        qso.Call.Valor.Should().Be("EA5XYZ");
        qso.Gridsquare.Valor.Should().Be("IM98");
        qso.Mode.Principal.Should().Be("FT8");
        qso.RstSent.Texto.Should().Be("-07", "el informe enviado es el db con el que se le oyo llamar");
        qso.RstRcvd.Texto.Should().Be("-12", "el informe recibido es el que el mando en su R+informe");

        // El tono de verdad es el que le toco a EL en la mezcla, no uno fijo de pantalla.
        qso.Freq.Hercios.Should().Be(14_074_000 + tonoAsignado);
        qso.Band.Nombre.Should().Be("20m");
        qso.FreqRx.Should().BeNull("sin split, el caso de siempre no cambia en nada");

        qso.InicioUtc.Should().Be(v1, "se apunta en el momento en que de verdad se completo");
        qso.FinUtc.Should().Be(v1);
        qso.Origen.Should().Be("módem propio (fox)", "distingue un pileup de un contacto de uno a uno");

        modelo.Cazadores.Should().BeEmpty("el tono queda libre en cuanto se completa");
        avisos.Should().Be(1);
    }

    [Fact]
    public async Task VariosCazadoresALaVezSeRegistranCadaUnoPorSuCuenta()
    {
        var modelo = Montar(out var modem, out var reloj, out var cuaderno);
        modelo.PonerElDial(Frecuencia.DesdeMegahercios(14.074m));

        reloj.Ahora = Mediodia;
        var paridadFox = await EmpezarFoxAsync(modelo);
        var v0 = PrimeraVentanaDeCazador(Mediodia, paridadFox);

        reloj.Ahora = v0 + Periodo + TimeSpan.FromSeconds(1);
        await Ventana(modelo, () => modem.Soltar(
            v0,
            Oido("EA8DLF EA1AAA IL18", -5, 1000, v0),
            Oido("EA8DLF EA2BBB IL28", -8, 1500, v0),
            Oido("EA8DLF EA3CCC IL38", -10, 2000, v0)));

        modelo.Cazadores.Should().HaveCount(3);
        var tonos = modelo.SecuenciadorDeFox.Cazadores.ToDictionary(p => p.Key, p => p.Value.TonoHz);

        var v1 = v0 + (2 * Periodo);
        reloj.Ahora = v1 + Periodo + TimeSpan.FromSeconds(1);
        await Ventana(modelo, () => modem.Soltar(
            v1,
            Oido("EA8DLF EA1AAA R-07", -5, 1000, v1),
            Oido("EA8DLF EA2BBB R-08", -8, 1500, v1),
            Oido("EA8DLF EA3CCC R-10", -10, 2000, v1)));

        await modelo.GuardadoEnCurso;
        var guardados = await Contactos(cuaderno);
        guardados.Should().HaveCount(3, "los tres se completan en la misma ventana y cada uno es su propio contacto");

        foreach (var (indicativo, informe) in new[] { ("EA1AAA", -7), ("EA2BBB", -8), ("EA3CCC", -10) })
        {
            var qso = guardados.Should().ContainSingle(q => q.Call.Valor == indicativo).Subject;
            qso.RstRcvd.Texto.Should().Be(informe.ToString("+00;-00;+00", System.Globalization.CultureInfo.InvariantCulture));
            qso.Freq.Hercios.Should().Be(14_074_000 + tonos[indicativo], "cada cazador tiene su propio tono, aunque compartan VFO de Tx");
        }

        guardados.Select(q => q.Freq.Hercios).Distinct().Should().HaveCount(3, "tonos distintos, frecuencias distintas");
        modelo.Cazadores.Should().BeEmpty();
    }

    [Fact]
    public async Task ConSplitElCazadorLlevaFreqRxDelTonoEnElQueDeVerdadSeLeOyo()
    {
        var modelo = Montar(out var modem, out var reloj, out var cuaderno);

        // Como una DXpedicion: transmite en 14.074 (VFO B), escucha ancho en 14.080 (VFO A).
        modelo.PonerElDial(Frecuencia.DesdeMegahercios(14.074m), Frecuencia.DesdeMegahercios(14.080m));

        reloj.Ahora = Mediodia;
        var paridadFox = await EmpezarFoxAsync(modelo);
        var v0 = PrimeraVentanaDeCazador(Mediodia, paridadFox);

        reloj.Ahora = v0 + Periodo + TimeSpan.FromSeconds(1);
        await Ventana(modelo, () => modem.Soltar(v0, Oido("EA8DLF EA5XYZ IM98", -7, 1800, v0)));

        var v1 = v0 + (2 * Periodo);
        reloj.Ahora = v1 + Periodo + TimeSpan.FromSeconds(1);
        await Ventana(modelo, () => modem.Soltar(v1, Oido("EA8DLF EA5XYZ R-12", -9, 1800, v1)));

        await modelo.GuardadoEnCurso;
        var qso = (await Contactos(cuaderno)).Should().ContainSingle().Subject;

        qso.FreqRx.Should().NotBeNull();
        qso.FreqRx!.Value.Hercios.Should().Be(14_080_000 + 1800, "el VFO de Rx mas el tono en el que de verdad se le oyo a EL");
        qso.BandRx.Nombre.Should().Be("20m");
    }

    [Fact]
    public async Task ElMismoCazadorNoSeRegistraDosVecesSiVuelveATrabajarseEnSeguida()
    {
        var modelo = Montar(out var modem, out var reloj, out var cuaderno);
        modelo.PonerElDial(Frecuencia.DesdeMegahercios(14.074m));
        var avisos = 0;
        modelo.CuadernoCambiado += (_, _) => avisos++;

        reloj.Ahora = Mediodia;
        var paridadFox = await EmpezarFoxAsync(modelo);
        var v0 = PrimeraVentanaDeCazador(Mediodia, paridadFox);

        reloj.Ahora = v0 + Periodo + TimeSpan.FromSeconds(1);
        await Ventana(modelo, () => modem.Soltar(v0, Oido("EA8DLF EA5XYZ IM98", -7, 1500, v0)));
        var v1 = v0 + (2 * Periodo);
        reloj.Ahora = v1 + Periodo + TimeSpan.FromSeconds(1);
        await Ventana(modelo, () => modem.Soltar(v1, Oido("EA8DLF EA5XYZ R-12", -9, 1500, v1)));
        await modelo.GuardadoEnCurso;
        (await Contactos(cuaderno)).Should().ContainSingle();
        avisos.Should().Be(1);

        // La misma estacion vuelve a entrar en el pileup muy poco despues (dentro del margen de
        // repeticion) y completa otra vez: no se apunta dos veces.
        var v2 = v1 + (2 * Periodo);
        reloj.Ahora = v2 + Periodo + TimeSpan.FromSeconds(1);
        await Ventana(modelo, () => modem.Soltar(v2, Oido("EA8DLF EA5XYZ IM98", -7, 1500, v2)));
        var v3 = v2 + (2 * Periodo);
        reloj.Ahora = v3 + Periodo + TimeSpan.FromSeconds(1);
        await Ventana(modelo, () => modem.Soltar(v3, Oido("EA8DLF EA5XYZ R-12", -9, 1500, v3)));
        await modelo.GuardadoEnCurso;

        (await Contactos(cuaderno)).Should().ContainSingle("el mismo cazador en el margen de repeticion no duplica");
        avisos.Should().Be(1);
    }

    [Fact]
    public async Task ConLaOpcionApagadaElPileupNoSeRegistraSolo()
    {
        var modelo = Montar(out var modem, out var reloj, out var cuaderno);
        modelo.RegistrarAlCompletar = false;
        modelo.PonerElDial(Frecuencia.DesdeMegahercios(14.074m));

        reloj.Ahora = Mediodia;
        var paridadFox = await EmpezarFoxAsync(modelo);
        var v0 = PrimeraVentanaDeCazador(Mediodia, paridadFox);

        reloj.Ahora = v0 + Periodo + TimeSpan.FromSeconds(1);
        await Ventana(modelo, () => modem.Soltar(v0, Oido("EA8DLF EA5XYZ IM98", -7, 1500, v0)));
        var v1 = v0 + (2 * Periodo);
        reloj.Ahora = v1 + Periodo + TimeSpan.FromSeconds(1);
        await Ventana(modelo, () => modem.Soltar(v1, Oido("EA8DLF EA5XYZ R-12", -9, 1500, v1)));

        await modelo.GuardadoEnCurso;
        modelo.Cazadores.Should().BeEmpty("el pileup completa el contacto igual, solo que no se apunta solo");
        (await Contactos(cuaderno)).Should().BeEmpty();
    }

    private static VistaModeloModemPropio Montar(
        out ModemApuntador modem,
        out RelojManual reloj,
        out RepositorioQsoEnMemoria cuaderno) =>
        MontajeDeLaPestanaDigital.Montar(out modem, out reloj, out cuaderno);
}
