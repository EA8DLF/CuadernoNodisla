using Nodisla.Cuaderno.Aplicacion.Puertos;

namespace Nodisla.Cuaderno.Modos.Marco;

/// <summary>
/// Los modos que sabe hacer el modem propio, por su enumerado.
/// </summary>
/// <remarks>
/// Cada modo se da de alta una vez, al montar el modem. Lo que no esta aqui no existe para la
/// pantalla: <see cref="Disponibles"/> es la lista que se ofrece al operador, y asi un modo a
/// medio hacer no aparece en el desplegable hasta que su especialista lo registra con el banco
/// en verde.
/// </remarks>
public sealed class RegistroDeModos
{
    private readonly Dictionary<ModoDelModem, IModoDigital> _modos = [];

    /// <summary>Da de alta un modo. Registrar dos veces el mismo es un error de montaje.</summary>
    public RegistroDeModos Anadir(IModoDigital modo)
    {
        ArgumentNullException.ThrowIfNull(modo);
        if (!_modos.TryAdd(modo.Modo, modo))
        {
            throw new InvalidOperationException($"El modo {modo.Modo} ya estaba registrado.");
        }

        return this;
    }

    /// <summary>Modos registrados, en el orden del enumerado.</summary>
    public IReadOnlyList<ModoDelModem> Disponibles =>
        Enum.GetValues<ModoDelModem>().Where(_modos.ContainsKey).ToArray();

    /// <summary>El modo pedido, si esta registrado.</summary>
    public bool TryObtener(ModoDelModem modo, out IModoDigital implementacion) =>
        _modos.TryGetValue(modo, out implementacion!);

    /// <summary>El modo pedido.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Si no esta registrado.</exception>
    public IModoDigital Obtener(ModoDelModem modo) =>
        TryObtener(modo, out var implementacion)
            ? implementacion
            : throw new ArgumentOutOfRangeException(nameof(modo), modo, "Modo no registrado en el modem propio.");
}
