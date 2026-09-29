using Microsoft.Extensions.Logging;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Modos.Marco;

namespace Nodisla.Cuaderno.Modos.Jt9;

/// <summary>
/// JT9 para el modem propio: ventanas de un minuto, 9-FSK de 1,7 Hz de espaciado, codigo
/// convolucional K=32 con decodificacion secuencial de Fano.
/// </summary>
/// <remarks>
/// Se registra en <see cref="RegistroDeModos"/> con <c>registro.Anadir(new ModoJt9())</c>. Como
/// JT65, la transmision empieza en el segundo 1 del minuto: el modem tiene que retrasar la
/// salida de <see cref="Generar"/> ese segundo (<see cref="ComienzoNominal"/>).
/// </remarks>
public sealed class ModoJt9 : IModoDigital
{
    private readonly CodificadorJt9 _codificador = new();

    /// <summary>Crea el modo.</summary>
    public ModoJt9(ILogger? registro = null)
    {
        Decodificador = new DecodificadorJt9(registro);
    }

    /// <summary>El decodificador, con sus mandos, para el banco de medida.</summary>
    public DecodificadorJt9 Decodificador { get; }

    /// <inheritdoc/>
    public ModoDelModem Modo => ModoDelModem.Jt9;

    /// <inheritdoc/>
    public TimeSpan Periodo => TimeSpan.FromSeconds(ParametrosJt9.PeriodoSegundos);

    /// <inheritdoc/>
    public TimeSpan ArranqueDentroDelPeriodo => TimeSpan.Zero;

    /// <summary>Cuanto despues del arranque de la ventana empieza a sonar la senal.</summary>
    public TimeSpan ComienzoNominal => TimeSpan.FromSeconds(ParametrosJt9.ComienzoNominalSegundos);

    /// <inheritdoc/>
    public int FrecuenciaDeAnalisis => ParametrosJt9.FrecuenciaDeAnalisis;

    /// <inheritdoc/>
    public IReadOnlyList<DecodificacionPropia> Decodificar(ReadOnlySpan<float> audio, DateTimeOffset ventanaUtc, CancellationToken ct) =>
        Decodificador.Decodificar(audio, ventanaUtc, ct).Decodificaciones;

    /// <inheritdoc/>
    public float[] Generar(string mensaje, int tonoHz, int frecuenciaDeMuestreo)
    {
        if (!_codificador.TryCodificar(mensaje, out var tonos, out var motivo))
            throw new FormatException(motivo);
        return ModuladorJt9.Sintetizar(tonos, tonoHz, frecuenciaDeMuestreo);
    }
}
