using Microsoft.Extensions.Logging;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Modos.Marco;

namespace Nodisla.Cuaderno.Modos.Jt65;

/// <summary>
/// JT65 para el modem propio: ventanas de un minuto, 65 tonos, Reed-Solomon (63,12) con
/// decodificador blando.
/// </summary>
/// <remarks>
/// <para>
/// Se registra en <see cref="RegistroDeModos"/> con <c>registro.Anadir(new ModoJt65())</c>.
/// El submodo por omision es el A, que es el de HF; para EME se construye con B o C.
/// </para>
/// <para>
/// La transmision de JT65 empieza en el segundo 1 del minuto, no en el 0: el modem tiene que
/// retrasar la salida de <see cref="Generar"/> ese segundo respecto al arranque de la ventana
/// (<see cref="ComienzoNominal"/>).
/// </para>
/// </remarks>
public sealed class ModoJt65 : IModoDigital
{
    private readonly CodificadorJt65 _codificador = new();

    /// <summary>Crea el modo.</summary>
    /// <param name="submodo">A (HF), B o C (EME).</param>
    /// <param name="registro">Para dejar constancia; por omision no se traza nada.</param>
    public ModoJt65(SubmodoJt65 submodo = SubmodoJt65.A, ILogger? registro = null)
    {
        Parametros = ParametrosJt65.De(submodo);
        Decodificador = new DecodificadorJt65(Parametros, registro);
    }

    /// <summary>Submodo.</summary>
    public ParametrosJt65 Parametros { get; }

    /// <summary>El decodificador, con sus mandos, para el banco de medida.</summary>
    public DecodificadorJt65 Decodificador { get; }

    /// <inheritdoc/>
    public ModoDelModem Modo => ModoDelModem.Jt65;

    /// <inheritdoc/>
    public TimeSpan Periodo => TimeSpan.FromSeconds(ParametrosJt65.PeriodoSegundos);

    /// <inheritdoc/>
    public TimeSpan ArranqueDentroDelPeriodo => TimeSpan.Zero;

    /// <summary>Cuanto despues del arranque de la ventana empieza a sonar la senal.</summary>
    public TimeSpan ComienzoNominal => TimeSpan.FromSeconds(ParametrosJt65.ComienzoNominalSegundos);

    /// <inheritdoc/>
    public int FrecuenciaDeAnalisis => ParametrosJt65.FrecuenciaDeAnalisis;

    /// <inheritdoc/>
    public IReadOnlyList<DecodificacionPropia> Decodificar(ReadOnlySpan<float> audio, DateTimeOffset ventanaUtc, CancellationToken ct) =>
        Decodificador.Decodificar(audio, ventanaUtc, ct).Decodificaciones;

    /// <inheritdoc/>
    public float[] Generar(string mensaje, int tonoHz, int frecuenciaDeMuestreo)
    {
        if (!_codificador.TryCodificar(mensaje, out var tonos, out var motivo))
            throw new FormatException(motivo);
        return ModuladorJt65.Sintetizar(Parametros, tonos, tonoHz, frecuenciaDeMuestreo);
    }
}
