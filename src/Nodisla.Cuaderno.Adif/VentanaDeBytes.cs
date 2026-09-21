namespace Nodisla.Cuaderno.Adif;

/// <summary>
/// Ventana deslizante sobre un flujo de bytes.
/// </summary>
/// <remarks>
/// El analizador necesita mirar hacia delante para comprobar donde termina de verdad el valor
/// de un campo, pero un cuaderno exportado puede ocupar cientos de megabytes. Esta ventana
/// mantiene en memoria solo lo que hace falta para el registro en curso y crece unicamente si
/// aparece un valor mas largo que el buffer.
/// </remarks>
internal sealed class VentanaDeBytes(Stream origen)
{
    private const int TamanoInicial = 64 * 1024;

    private byte[] _datos = new byte[TamanoInicial];
    private int _inicio;
    private int _fin;
    private bool _fuenteAgotada;

    /// <summary>Bytes leidos y aun no consumidos.</summary>
    public int Disponible => _fin - _inicio;

    /// <summary>Byte situado a <paramref name="desplazamiento"/> del primero sin consumir.</summary>
    public byte En(int desplazamiento) => _datos[_inicio + desplazamiento];

    /// <summary>Trozo de la ventana, sin copiar.</summary>
    public ReadOnlySpan<byte> Trozo(int desplazamiento, int longitud) =>
        _datos.AsSpan(_inicio + desplazamiento, longitud);

    /// <summary>Da por consumidos los primeros <paramref name="bytes"/> de la ventana.</summary>
    public void Avanzar(int bytes) => _inicio += bytes;

    /// <summary>Intenta tener al menos <paramref name="minimo"/> bytes disponibles.</summary>
    public async ValueTask<bool> AsegurarAsync(int minimo, CancellationToken ct)
    {
        while (Disponible < minimo && !_fuenteAgotada)
        {
            HacerSitio(minimo);
            var leidos = await origen.ReadAsync(_datos.AsMemory(_fin, _datos.Length - _fin), ct)
                .ConfigureAwait(false);
            if (leidos == 0) _fuenteAgotada = true;
            else _fin += leidos;
        }
        return Disponible >= minimo;
    }

    /// <summary>
    /// Busca el primer byte igual a <paramref name="buscado"/> a partir del desplazamiento dado.
    /// Devuelve -1 si el flujo se acaba antes.
    /// </summary>
    public async ValueTask<int> BuscarAsync(byte buscado, int desde, CancellationToken ct)
    {
        var d = desde;
        while (true)
        {
            if (d < Disponible)
            {
                var i = _datos.AsSpan(_inicio + d, Disponible - d).IndexOf(buscado);
                if (i >= 0) return d + i;
                d = Disponible;
            }
            var antes = Disponible;
            await AsegurarAsync(d + 4096, ct).ConfigureAwait(false);
            if (Disponible == antes) return -1;
        }
    }

    private void HacerSitio(int minimo)
    {
        if (_inicio > 0)
        {
            Array.Copy(_datos, _inicio, _datos, 0, Disponible);
            _fin -= _inicio;
            _inicio = 0;
        }
        if (_datos.Length >= minimo && _fin < _datos.Length) return;

        var nuevo = new byte[Math.Max(_datos.Length * 2, minimo + 4096)];
        Array.Copy(_datos, 0, nuevo, 0, _fin);
        _datos = nuevo;
    }
}
