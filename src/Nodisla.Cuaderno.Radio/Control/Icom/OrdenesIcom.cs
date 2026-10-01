using Nodisla.Cuaderno.Radio.Control.Ft710;

namespace Nodisla.Cuaderno.Radio.Control.Icom;

/// <summary>
/// Que ordenes CI-V se pueden mandar a un ICOM y cuales no.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lista escrita a mano con los manuales CI-V, no deducida.</b> En CI-V la misma orden lee o
/// escribe segun lleve datos o no: <c>1C 00</c> pregunta si transmite, <c>1C 00 01</c> transmite.
/// Por eso aqui se mira la trama entera, no solo la orden.
/// </para>
/// <para>Prohibidas siempre (ni sondeo ni en crudo):</para>
/// <list type="bullet">
/// <item><c>18 00</c> apaga el equipo. Solo sale por <see cref="ConApagadoAutorizadoAsync"/>.</item>
/// <item><c>09</c> escribe memoria, <c>0B</c> borra memoria.</item>
/// <item><c>1A 00</c>, <c>1A 01</c> con datos: escriben memoria / pila de banda. Leer (solo el numero) si vale.</item>
/// <item><c>1A 02</c>: memorias del manipulador (escritura) — no se usa.</item>
/// <item><c>1E 03</c> con datos: cambia los limites de transmision del operador.</item>
/// <item><c>17</c> manda telegrafia y <c>28</c> reproduce la memoria de voz: <b>transmiten</b>.</item>
/// <item><c>1C 00 01</c> (PTT) y <c>1C 01 02</c> (sintonizar, emite portadora): solo dentro del
/// ambito que abre el vigilante del PTT (<see cref="ConTransmisionAutorizadaAsync"/>).</item>
/// </list>
/// <para>
/// En crudo, ademas, no se deja escribir en el menu (<c>1A 05</c> con datos): una direccion o
/// velocidad CI-V mal puesta deja la radio sin control.
/// </para>
/// </remarks>
public static class OrdenesIcom
{
    private static readonly AsyncLocal<bool> ApagadoAutorizado = new();
    private static readonly AsyncLocal<bool> TransmisionAutorizada = new();

    /// <summary>Orden de apagar.</summary>
    public static IReadOnlyList<byte> Apagar { get; } = [0x18, 0x00];

    /// <summary>Orden de subir el PTT.</summary>
    public static IReadOnlyList<byte> SubirPtt { get; } = [0x1C, 0x00, 0x01];

    /// <summary>Orden de bajar el PTT.</summary>
    public static IReadOnlyList<byte> BajarPtt { get; } = [0x1C, 0x00, 0x00];

    /// <summary>Orden de sintonizar el acoplador (emite).</summary>
    public static IReadOnlyList<byte> Sintonizar { get; } = [0x1C, 0x01, 0x02];

    /// <summary>
    /// Deja pasar <c>18 00</c> solo mientras dura <paramref name="apagar"/> y solo en esa rama de
    /// la ejecucion (AsyncLocal: el sondeo, en otro hilo, no lo hereda).
    /// </summary>
    internal static async Task ConApagadoAutorizadoAsync(Func<Task> apagar)
    {
        ArgumentNullException.ThrowIfNull(apagar);
        ApagadoAutorizado.Value = true;
        try
        {
            await apagar().ConfigureAwait(false);
        }
        finally
        {
            ApagadoAutorizado.Value = false;
        }
    }

    /// <summary>
    /// Deja pasar <c>1C 00 01</c> y <c>1C 01 02</c> solo mientras dura <paramref name="transmitir"/>.
    /// Lo abre unicamente el camino del vigilante (<c>IPttDirecto</c>).
    /// </summary>
    internal static async Task ConTransmisionAutorizadaAsync(Func<Task> transmitir)
    {
        ArgumentNullException.ThrowIfNull(transmitir);
        TransmisionAutorizada.Value = true;
        try
        {
            await transmitir().ConfigureAwait(false);
        }
        finally
        {
            TransmisionAutorizada.Value = false;
        }
    }

    /// <summary>El cuerpo pone el equipo en antena.</summary>
    /// <param name="cuerpo">Orden, suborden y datos.</param>
    /// <returns>Verdadero si transmite.</returns>
    public static bool Transmite(ReadOnlySpan<byte> cuerpo) =>
        cuerpo.SequenceEqual(SubirPtt.ToArray())
        || cuerpo.SequenceEqual(Sintonizar.ToArray())
        || (cuerpo.Length > 0 && cuerpo[0] is 0x17 or 0x28);

    /// <summary>Comprueba que un cuerpo CI-V se puede mandar al equipo.</summary>
    /// <param name="cuerpo">Orden, suborden y datos (sin preambulo, direcciones ni FD).</param>
    /// <exception cref="OrdenPeligrosaException">Si apaga, escribe memorias o transmite sin permiso.</exception>
    public static void ComprobarQueEsSegura(ReadOnlySpan<byte> cuerpo)
    {
        if (cuerpo.Length == 0) return;
        var texto = Hex.De(cuerpo);
        var orden = cuerpo[0];

        if (cuerpo.StartsWith(Apagar.ToArray()) && !(cuerpo.Length == 2 && ApagadoAutorizado.Value))
        {
            throw new OrdenPeligrosaException(texto, "apagaría el equipo");
        }

        if (orden is 0x09 or 0x0B)
        {
            throw new OrdenPeligrosaException(texto, "escribiría o borraría memorias del equipo");
        }

        if (orden == 0x1A && cuerpo.Length > 1)
        {
            var sub = cuerpo[1];
            if ((sub == 0x00 && cuerpo.Length > 4) || (sub == 0x01 && cuerpo.Length > 4) || sub == 0x02)
            {
                throw new OrdenPeligrosaException(texto, "escribiría memorias o la pila de banda del equipo");
            }
        }

        if (orden == 0x1E && cuerpo.Length > 2 && cuerpo[1] == 0x03)
        {
            throw new OrdenPeligrosaException(texto, "cambiaría los límites de transmisión del equipo");
        }

        if (orden is 0x17 or 0x28)
        {
            throw new OrdenPeligrosaException(texto, "pondría el equipo en antena (telegrafía o memoria de voz)");
        }

        if ((cuerpo.SequenceEqual(SubirPtt.ToArray()) || cuerpo.SequenceEqual(Sintonizar.ToArray()))
            && !TransmisionAutorizada.Value)
        {
            throw new OrdenPeligrosaException(texto, "pondría el equipo en antena sin pasar por el vigilante del PTT");
        }
    }

    /// <summary>Comprueba que un cuerpo vale para mandarlo en crudo desde la interfaz.</summary>
    /// <param name="cuerpo">Orden, suborden y datos.</param>
    /// <exception cref="OrdenPeligrosaException">Si es peligrosa, transmite o escribe en el menu.</exception>
    public static void ComprobarQueValeEnCrudo(ReadOnlySpan<byte> cuerpo)
    {
        var texto = Hex.De(cuerpo);
        // Cualquier 1C 00 con un dato que no sea 00 es pedir antena.
        if (Transmite(cuerpo) || (cuerpo.Length > 2 && cuerpo[0] == 0x1C && cuerpo[1] == 0x00 && cuerpo[2] != 0x00))
        {
            throw new OrdenPeligrosaException(texto, "pondría el equipo en antena sin pasar por el vigilante del PTT");
        }

        if (cuerpo.Length > 4 && cuerpo[0] == 0x1A && cuerpo[1] == 0x05)
        {
            throw new OrdenPeligrosaException(texto, "escribiría en el menú del equipo (podría dejarlo sin control CI-V)");
        }

        ComprobarQueEsSegura(cuerpo);
    }
}
