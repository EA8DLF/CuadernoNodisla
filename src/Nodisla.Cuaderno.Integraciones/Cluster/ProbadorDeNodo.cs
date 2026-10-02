using System.Diagnostics;
using System.Net.Sockets;
using System.Text;

namespace Nodisla.Cuaderno.Integraciones.Cluster;

/// <summary>Lo que ha salido de probar un nodo.</summary>
/// <param name="Responde">El puerto ha contestado.</param>
/// <param name="Tiempo">Lo que ha tardado en abrir.</param>
/// <param name="Saludo">Primera linea que manda el nodo, si manda algo.</param>
/// <param name="Error">Por que no ha contestado, si no lo ha hecho.</param>
public sealed record PruebaDeNodo(bool Responde, TimeSpan Tiempo, string? Saludo, string? Error);

/// <summary>
/// Comprueba si un nodo contesta, <b>sin entrar</b>.
/// </summary>
/// <remarks>
/// Abre el puerto, lee lo que el nodo diga de saludo durante un momento y cierra. No manda ni
/// un byte: ni indicativo ni nada. Asi se puede probar un nodo sin dejar rastro de sesion con
/// el indicativo de nadie y sin que el nodo nos cuente como conectados.
/// </remarks>
public static class ProbadorDeNodo
{
    /// <summary>Prueba el nodo.</summary>
    /// <param name="servidor">Maquina.</param>
    /// <param name="puerto">Puerto de Telnet.</param>
    /// <param name="espera">Lo que se espera a que abra.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>Lo que ha salido.</returns>
    public static async Task<PruebaDeNodo> ProbarAsync(
        string servidor,
        int puerto,
        TimeSpan? espera = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(servidor);

        var reloj = Stopwatch.StartNew();
        using var cliente = new TcpClient();
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(espera ?? TimeSpan.FromSeconds(8));

        try
        {
            await cliente.ConnectAsync(servidor.Trim(), puerto, limite.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new PruebaDeNodo(false, reloj.Elapsed, null, new SocketException((int)SocketError.TimedOut).Message);
        }
        catch (SocketException ex)
        {
            return new PruebaDeNodo(false, reloj.Elapsed, null, ex.Message);
        }

        var abierto = reloj.Elapsed;
        string? saludo = null;
        try
        {
            using var lectura = CancellationTokenSource.CreateLinkedTokenSource(ct);
            lectura.CancelAfter(TimeSpan.FromSeconds(2));
            var buffer = new byte[512];
            var leidos = await cliente.GetStream().ReadAsync(buffer, lectura.Token).ConfigureAwait(false);
            saludo = PrimeraLinea(buffer.AsSpan(0, leidos));
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException)
        {
            // Hay nodos que no saludan hasta que se les dice algo: con haber abierto basta.
            if (ct.IsCancellationRequested) throw;
        }

        return new PruebaDeNodo(true, abierto, saludo, null);
    }

    /// <summary>Primera linea con letras, sin secuencias de control de Telnet.</summary>
    private static string? PrimeraLinea(ReadOnlySpan<byte> bytes)
    {
        var filtro = new FiltroTelnet();
        filtro.Anadir(bytes);
        while (filtro.TryLeerLinea(out var linea))
        {
            var limpia = Limpiar(linea);
            if (limpia.Length > 0) return limpia;
        }

        var pendiente = Limpiar(filtro.Pendiente);
        return pendiente.Length > 0 ? pendiente : null;
    }

    private static string Limpiar(string texto)
    {
        var sb = new StringBuilder(texto.Length);
        foreach (var c in texto)
        {
            if (!char.IsControl(c)) sb.Append(c);
        }

        var limpio = sb.ToString().Trim().Trim('*', '-', ' ');
        return limpio.Length > 100 ? limpio[..100] + "…" : limpio;
    }
}
