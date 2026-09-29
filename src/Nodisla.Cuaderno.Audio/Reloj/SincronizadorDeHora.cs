using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;

namespace Nodisla.Cuaderno.Audio.Reloj;

/// <summary>
/// Pone el reloj del ordenador en hora, cuando el operador lo pide y solo entonces.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nunca se hace solo.</b> El reloj del ordenador no es del programa: es de su dueno, y
/// cambiarselo por detras mientras trabaja es de mala educacion y puede romperle otras cosas
/// —certificados, sesiones abiertas, marcas de tiempo de sus ficheros—. Aqui solo se toca
/// cuando alguien pulsa.
/// </para>
/// <para>
/// Hay dos vias y las dos piden ser administrador. Si no se es, <b>no se falla con un error de
/// acceso denegado</b>: se explica en cristiano lo que pasa y se dan las ordenes exactas para
/// hacerlo a mano, que es lo unico util que se le puede decir a alguien en ese momento.
/// </para>
/// </remarks>
public sealed class SincronizadorDeHora : ISincronizadorDeHora
{
    private readonly IRelojDelModem _reloj;
    private readonly IRelojDelSistema _sistema;
    private readonly OpcionesDelReloj _opciones;
    private readonly ILogger _registro;
    private readonly Func<DateTimeOffset> _ahoraDelSistema;

    /// <summary>Crea el sincronizador.</summary>
    /// <param name="reloj">El reloj del modem, de donde sale el desvio medido.</param>
    /// <param name="sistema">Quien toca el reloj de Windows; si es nulo, el de verdad.</param>
    /// <param name="opciones">Ajustes; si es nulo, los de partida.</param>
    /// <param name="registro">Donde se anota lo que pasa; si es nulo, no se anota nada.</param>
    /// <param name="ahoraDelSistema">De donde se lee la hora local; si es nulo, la del sistema.</param>
    /// <exception cref="ArgumentNullException">Si falta el reloj del modem.</exception>
    public SincronizadorDeHora(
        IRelojDelModem reloj,
        IRelojDelSistema? sistema = null,
        OpcionesDelReloj? opciones = null,
        ILogger? registro = null,
        Func<DateTimeOffset>? ahoraDelSistema = null)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        _reloj = reloj;
        _sistema = sistema ?? new RelojDelSistemaDeWindows();
        _opciones = (opciones ?? new OpcionesDelReloj()).Copiar();
        _registro = registro ?? NullLogger.Instance;
        _ahoraDelSistema = ahoraDelSistema ?? (() => DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public bool SePuedePonerEnHora
    {
        get
        {
            try
            {
                return _sistema.HayPermisosParaCambiarLaHora;
            }
            catch (Exception fallo)
            {
                _registro.LogDebug(fallo, "No se ha podido saber si se puede cambiar la hora del sistema.");
                return false;
            }
        }
    }

    /// <inheritdoc />
    public DateTimeOffset? UltimaSincronizacion { get; private set; }

    /// <inheritdoc />
    public string InstruccionesParaHacerloAMano => Instrucciones(_opciones.ServidorRecomendado);

    /// <summary>Servidor de hora que se propone para dejar el ordenador en hora para siempre.</summary>
    public string ServidorRecomendado => _opciones.ServidorRecomendado;

    /// <summary>Lo ultimo que se intento, entero, para poder ensenarlo.</summary>
    /// <remarks>
    /// El puerto solo pide la fecha; esto guarda ademas el mensaje y el detalle, que es lo que
    /// hay que volver a pintar si el operador cierra el aviso y luego pregunta que paso.
    /// </remarks>
    public ResultadoDePuestaEnHora? UltimoIntento { get; private set; }

    /// <inheritdoc />
    /// <remarks>
    /// Antes de tocar nada se vuelve a medir <b>forzando</b>: corregir con una medida de hace
    /// media hora seria meter un error nuevo en vez de quitarlo.
    /// </remarks>
    public async Task<ResultadoDePuestaEnHora> PonerElRelojEnHoraAsync(CancellationToken ct = default)
    {
        var desvio = await _reloj.MedirAsync(true, ct).ConfigureAwait(false);

        if (!desvio.EsFiable)
        {
            return Anotar(new ResultadoDePuestaEnHora(
                Hecho: false,
                ViaDeSincronizacion.HoraPuestaAMano,
                "No se puede poner el reloj en hora porque no se ha podido medir el desvío: no contestó "
                    + "ningún servidor de hora. Comprueba la conexión a internet y vuelve a intentarlo.",
                Detalle: null,
                DesvioAntesMs: null,
                DesvioDespuesMs: null));
        }

        if (!SePuedePonerEnHora)
        {
            return Anotar(new ResultadoDePuestaEnHora(
                Hecho: false,
                ViaDeSincronizacion.HoraPuestaAMano,
                "Para cambiar la hora del ordenador hace falta ejecutar el programa como administrador. "
                    + "Sin eso, la otra manera —y la mejor, porque deja el ordenador en hora para "
                    + "siempre— es activar la sincronización de hora de Windows contra "
                    + $"{_opciones.ServidorRecomendado}.",
                InstruccionesParaHacerloAMano,
                desvio.DesvioMs,
                DesvioDespuesMs: null));
        }

        try
        {
            _sistema.PonerHoraUtc(_reloj.Ahora.UtcDateTime);
        }
        catch (Exception fallo)
        {
            _registro.LogError(fallo, "No se ha podido poner el reloj del ordenador en hora.");

            return Anotar(new ResultadoDePuestaEnHora(
                Hecho: false,
                ViaDeSincronizacion.HoraPuestaAMano,
                "Windows no ha dejado cambiar la hora del sistema. Puedes hacerlo a mano o activar la "
                    + $"sincronización de hora de Windows contra {_opciones.ServidorRecomendado}.",
                fallo.Message + Environment.NewLine + InstruccionesParaHacerloAMano,
                desvio.DesvioMs,
                DesvioDespuesMs: null));
        }

        // Se vuelve a medir para dejar escrito como quedo, no para fiarse de lo que uno cree.
        var despues = await _reloj.MedirAsync(true, ct).ConfigureAwait(false);

        _registro.LogWarning(
            "Se ha puesto el reloj del ordenador en hora a petición del operador: estaba {Antes:F0} ms "
                + "desviado según {Fuente} y ha quedado en {Despues:F0} ms.",
            desvio.DesvioMs,
            desvio.Fuente,
            despues.DesvioMs);

        return Anotar(new ResultadoDePuestaEnHora(
            Hecho: true,
            ViaDeSincronizacion.HoraPuestaAMano,
            string.Create(
                CultureInfo.CurrentCulture,
                $"Reloj puesto en hora: estaba {VeredictoDelReloj.ParaMostrar(desvio.DesvioMs)} según "
                    + $"{desvio.Fuente} y ha quedado en {VeredictoDelReloj.ParaMostrar(despues.DesvioMs)}."),
            Detalle: null,
            desvio.DesvioMs,
            despues.DesvioMs));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Esta es la via buena: no deja el ordenador en hora hoy, lo deja en hora siempre, y es
    /// Windows quien se encarga a partir de entonces.
    /// </remarks>
    public async Task<ResultadoDePuestaEnHora> ConfigurarServicioDeHoraAsync(CancellationToken ct = default)
    {
        var servidor = _opciones.ServidorRecomendado;
        var antes = _reloj.Desvio.EsFiable ? _reloj.Desvio.DesvioMs : (double?)null;

        if (!SePuedePonerEnHora)
        {
            return Anotar(new ResultadoDePuestaEnHora(
                Hecho: false,
                ViaDeSincronizacion.ServicioConfigurado,
                "Para configurar la hora de Windows hace falta ejecutar el programa como administrador. "
                    + "Puedes hacerlo tú mismo con tres órdenes.",
                InstruccionesParaHacerloAMano,
                antes,
                DesvioDespuesMs: null));
        }

        var pasos = new (string Programa, string Argumentos)[]
        {
            ("w32tm", $"/config /manualpeerlist:\"{servidor},0x8\" /syncfromflags:manual /update"),
            ("net", "start w32time"),
            ("w32tm", "/resync"),
        };

        var contado = new List<string>();

        foreach (var (programa, argumentos) in pasos)
        {
            try
            {
                var (codigo, salida) = await _sistema.EjecutarAsync(programa, argumentos, ct).ConfigureAwait(false);
                contado.Add($"{programa} {argumentos} -> {codigo}: {salida}");

                // «net start» falla si el servicio ya estaba arrancado, y eso no es un problema.
                if (codigo != 0 && programa != "net")
                {
                    return Anotar(new ResultadoDePuestaEnHora(
                        Hecho: false,
                        ViaDeSincronizacion.ServicioConfigurado,
                        $"Windows no ha aceptado configurar la hora contra {servidor}. Puedes intentarlo a mano.",
                        string.Join(Environment.NewLine, contado)
                            + Environment.NewLine
                            + InstruccionesParaHacerloAMano,
                        antes,
                        DesvioDespuesMs: null));
                }
            }
            catch (Exception fallo) when (fallo is not OperationCanceledException)
            {
                _registro.LogError(fallo, "Falló la orden {Programa} al configurar la hora de Windows.", programa);

                return Anotar(new ResultadoDePuestaEnHora(
                    Hecho: false,
                    ViaDeSincronizacion.ServicioConfigurado,
                    "No se ha podido configurar el servicio de hora de Windows. Puedes hacerlo a mano.",
                    fallo.Message + Environment.NewLine + InstruccionesParaHacerloAMano,
                    antes,
                    DesvioDespuesMs: null));
            }
        }

        var despues = await _reloj.MedirAsync(true, ct).ConfigureAwait(false);

        _registro.LogWarning(
            "Se ha configurado la hora de Windows contra {Servidor} a petición del operador: el desvío "
                + "ha quedado en {Despues:F0} ms.",
            servidor,
            despues.DesvioMs);

        return Anotar(new ResultadoDePuestaEnHora(
            Hecho: true,
            ViaDeSincronizacion.ServicioConfigurado,
            string.Create(
                CultureInfo.CurrentCulture,
                $"Windows sincronizará la hora contra {servidor} a partir de ahora. El desvío ha quedado "
                    + $"en {VeredictoDelReloj.ParaMostrar(despues.DesvioMs)}."),
            string.Join(Environment.NewLine, contado),
            antes,
            despues.DesvioMs));
    }

    /// <summary>Las ordenes para dejar el ordenador en hora sin el programa.</summary>
    private static string Instrucciones(string servidor) => string.Join(
        Environment.NewLine,
        "Abre una consola como administrador y escribe:",
        string.Create(
            CultureInfo.InvariantCulture,
            $"    w32tm /config /manualpeerlist:\"{servidor},0x8\" /syncfromflags:manual /update"),
        "    net start w32time",
        "    w32tm /resync");

    /// <summary>Guarda el rastro de lo ultimo que se hizo.</summary>
    private ResultadoDePuestaEnHora Anotar(ResultadoDePuestaEnHora resultado)
    {
        UltimoIntento = resultado;

        if (resultado.Hecho)
        {
            UltimaSincronizacion = _ahoraDelSistema();
        }
        else
        {
            _registro.LogInformation("No se ha tocado el reloj del ordenador: {Mensaje}", resultado.Mensaje);
        }

        return resultado;
    }
}
