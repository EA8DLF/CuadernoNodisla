using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Ui.Ajustes;
using Nodisla.Cuaderno.Ui.Desarrollo;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// Un reloj que marca la hora que le digan. Nada de reloj de pared: las pruebas de ventanas
/// ponen la hora a mano, segundo a segundo.
/// </summary>
internal sealed class RelojManual : IRelojDelModem
{
    public RelojManual(DateTimeOffset ahora, CalidadDelReloj calidad = CalidadDelReloj.Bien)
    {
        Ahora = ahora;
        Calidad = calidad;
    }

    public DateTimeOffset Ahora { get; set; }

    public CalidadDelReloj Calidad { get; set; }

    public int Mediciones { get; private set; }

    public DesvioDelReloj Desvio => new(3, "pruebas", DateTimeOffset.UnixEpoch, EsFiable: true);

    public EstadoDelReloj Estado => new(Desvio, Calidad, Calidad == CalidadDelReloj.Bien ? "En hora." : "Desviado.", Calidad == CalidadDelReloj.Bien ? string.Empty : "Sincronice.", "+3 ms");

    public event EventHandler<EstadoDelReloj>? DesvioMedido;

    public Task<DesvioDelReloj> MedirAsync(bool forzar = false, CancellationToken ct = default)
    {
        Mediciones++;
        DesvioMedido?.Invoke(this, Estado);
        return Task.FromResult(Desvio);
    }

    public DateTimeOffset ProximaVentana(TimeSpan periodo) =>
        VistaModeloModemPropio.ComienzoDeVentana(Ahora, periodo) + periodo;
}

/// <summary>Un sincronizador de hora que apunta lo que le piden y no toca Windows.</summary>
internal sealed class SincronizadorDeMentira : ISincronizadorDeHora
{
    public int PuestasEnHora { get; private set; }

    public int Configuraciones { get; private set; }

    public bool SePuedePonerEnHora => true;

    public DateTimeOffset? UltimaSincronizacion => null;

    public string InstruccionesParaHacerloAMano => "w32tm /resync (a mano)";

    public bool Contestar { get; set; } = true;

    public Task<ResultadoDePuestaEnHora> PonerElRelojEnHoraAsync(CancellationToken ct = default)
    {
        PuestasEnHora++;
        return Task.FromResult(new ResultadoDePuestaEnHora(Contestar, default, "Puesto en hora.", "w32tm salida larga", -1290, 3));
    }

    public Task<ResultadoDePuestaEnHora> ConfigurarServicioDeHoraAsync(CancellationToken ct = default)
    {
        Configuraciones++;
        return Task.FromResult(new ResultadoDePuestaEnHora(Contestar, default, "Servicio configurado.", null, null, null));
    }
}

/// <summary>
/// Un modem que <b>no emite nada</b>: apunta lo que le piden emitir y a que hora del reloj de
/// mentira se lo pidieron. Las ventanas las suelta la prueba, con su hora.
/// </summary>
internal sealed class ModemApuntador : IModemPropio
{
    private readonly RelojManual _reloj;

    public ModemApuntador(RelojManual reloj) => _reloj = reloj;

    public List<(string Texto, DateTimeOffset Cuando)> Emisiones { get; } = [];

    public int Abortos { get; private set; }

    public List<string> FicherosDecodificados { get; } = [];

    public ModoDelModem Modo { get; private set; } = ModoDelModem.Ft8;

    public bool EstaEscuchando { get; private set; }

    public bool EstaEmitiendo { get; set; }

    public Frecuencia FrecuenciaDelDial { get; set; }

    public IReadOnlyList<ModoDelModem> ModosDisponibles =>
    [
        ModoDelModem.Ft8, ModoDelModem.Ft4, ModoDelModem.Wspr, ModoDelModem.Jt65, ModoDelModem.Jt9,
        ModoDelModem.Q65, ModoDelModem.Msk144, ModoDelModem.Fst4, ModoDelModem.Fst4w,
    ];

    public event EventHandler<ColumnaDeCascada>? CascadaActualizada;

    public event EventHandler<VentanaDecodificada>? VentanaLista;

    public void Soltar(DateTimeOffset ventana, params DecodificacionPropia[] decodificaciones) =>
        VentanaLista?.Invoke(this, new VentanaDecodificada(ventana, decodificaciones, TimeSpan.FromSeconds(1), -118));

    public void SoltarColumna(ColumnaDeCascada columna) => CascadaActualizada?.Invoke(this, columna);

    public Task EscucharAsync(ModoDelModem modo, CancellationToken ct = default)
    {
        Modo = modo;
        EstaEscuchando = true;
        return Task.CompletedTask;
    }

    public Task PararAsync(CancellationToken ct = default)
    {
        EstaEscuchando = false;
        return Task.CompletedTask;
    }

    public Task EmitirAsync(string texto, int tonoHz, CancellationToken ct = default)
    {
        Emisiones.Add((texto, _reloj.Ahora));
        return Task.CompletedTask;
    }

    public Task AbortarEmisionAsync(CancellationToken ct = default)
    {
        Abortos++;
        EstaEmitiendo = false;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DecodificacionPropia>> DecodificarFicheroAsync(
        string rutaWav, ModoDelModem modo, CancellationToken ct = default)
    {
        FicherosDecodificados.Add(rutaWav);
        IReadOnlyList<DecodificacionPropia> salen =
        [
            new("CQ EA5XYZ IM98", -7, 0.1, 800, modo, _reloj.Ahora) { EsCq = true },
        ];
        return Task.FromResult(salen);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Un equipo de mentira que solo apunta a donde le mandan.</summary>
internal static class MontajeDeLaPestanaDigital
{
    /// <summary>
    /// Una hora con paridad 0 en FT8: 12:00:00 UTC de un dia ya pasado. Pasado a proposito: el
    /// caso de uso de registrar rechaza contactos en el futuro, y con la fecha de hoy fallaria
    /// segun la hora a la que se pasen las pruebas.
    /// </summary>
    public static readonly DateTimeOffset Mediodia = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    public static VistaModeloModemPropio Montar(
        out ModemApuntador modem,
        out RelojManual reloj,
        out RepositorioQsoEnMemoria cuaderno,
        ISincronizadorDeHora? sincronizador = null,
        IControlEquipo? equipo = null)
    {
        reloj = new RelojManual(Mediodia);
        modem = new ModemApuntador(reloj);
        cuaderno = new RepositorioQsoEnMemoria([]);

        return new VistaModeloModemPropio(
            new VistaModeloRelojDigital(reloj, sincronizador),
            new AjustesDelPrograma(),
            new ConsultarTrabajadoAntes(cuaderno),
            new RegistrarQso(cuaderno, new RepositorioEstacionEnMemoria()),
            EstadoDelCorrector.NoProcede,
            modem,
            entrada: null,
            salida: new SalidaDeAudioSimulada(),
            equipo: equipo)
        {
            // Por defecto, NO. Las pruebas que necesitan pasar la pregunta lo dicen, y aun
            // asi el modem es de mentira: apunta, no transmite.
            ConfirmarQueVaATransmitir = _ => false,
            MiIndicativo = Indicativo.Parse("EA8DLF"),
            MiLocalizador = Locator.Parse("IL18"),
        };
    }

    public static DecodificacionPropia Oido(string texto, int db, int tono, DateTimeOffset ventana)
    {
        var partes = texto.Split(' ');
        return new DecodificacionPropia(texto, db, 0.2, tono, ModoDelModem.Ft8, ventana)
        {
            EsCq = partes[0] == "CQ",
            Llamante = Indicativo.TryParse(partes[1], out var quien) ? quien : Indicativo.Vacio,
            Locator = Locator.TryParse(partes[^1], out var rejilla) ? rejilla : Locator.Vacio,
        };
    }
}
