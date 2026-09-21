namespace Nodisla.Cuaderno.Adif;

/// <summary>
/// Flujo de solo lectura que primero sirve unos bytes ya leidos y despues sigue con el original.
/// </summary>
/// <remarks>
/// Hace falta para averiguar si un fichero es ADI o ADX sin exigir que el flujo se pueda
/// rebobinar: se miran los primeros bytes y luego se devuelven al principio de la cola.
/// </remarks>
internal sealed class FlujoPrefijado(byte[] prefijo, int longitudDelPrefijo, Stream resto) : Stream
{
    private int _consumido;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (_consumido < longitudDelPrefijo)
        {
            var n = Math.Min(buffer.Length, longitudDelPrefijo - _consumido);
            prefijo.AsSpan(_consumido, n).CopyTo(buffer);
            _consumido += n;
            return n;
        }
        return resto.Read(buffer);
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_consumido < longitudDelPrefijo)
        {
            var n = Math.Min(buffer.Length, longitudDelPrefijo - _consumido);
            prefijo.AsMemory(_consumido, n).CopyTo(buffer);
            _consumido += n;
            return n;
        }
        return await resto.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    public override Task<int> ReadAsync(
        byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush()
    {
        // Nada que vaciar: el flujo es de solo lectura.
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
