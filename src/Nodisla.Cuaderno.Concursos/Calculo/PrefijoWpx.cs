using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Concursos.Calculo;

/// <summary>
/// Prefijo de un indicativo segun las reglas del WPX.
/// </summary>
/// <remarks>
/// <para>
/// No es «las dos primeras letras». El prefijo del WPX es la parte del indicativo hasta el
/// ultimo numero incluido, con tres matices que son los que dan los puntos: un indicativo
/// sin numero recibe un cero (<c>RAEM</c> es <c>RA0</c>), un indicativo portable con un solo
/// numero cambia el numero del prefijo (<c>N8BJQ/9</c> es <c>N9</c>), y un indicativo
/// portable con un prefijo entero delante toma ese (<c>EA8/DL1ABC</c> es <c>EA8</c>).
/// </para>
/// <para>
/// Esta clase repite lo que ya hace <c>Nodisla.Cuaderno.Diplomas</c>. Se ha escrito aparte a
/// proposito para no atar dos modulos de igual rango entre si; lo correcto es subir el
/// calculo a <c>Dominio</c>, y asi esta anotado en el informe de esta fase.
/// </para>
/// </remarks>
public static class PrefijoWpx
{
    /// <summary>Calcula el prefijo WPX de un indicativo.</summary>
    /// <param name="indicativo">Indicativo del corresponsal.</param>
    /// <returns>El prefijo, o cadena vacia si el indicativo no sirve.</returns>
    public static string De(Indicativo indicativo) => De(indicativo.Valor);

    /// <summary>Calcula el prefijo WPX de un indicativo escrito como texto.</summary>
    /// <param name="indicativo">Indicativo del corresponsal.</param>
    /// <returns>El prefijo, o cadena vacia si el indicativo no sirve.</returns>
    public static string De(string? indicativo)
    {
        if (string.IsNullOrWhiteSpace(indicativo)) return string.Empty;
        var partes = indicativo.Trim().ToUpperInvariant()
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (partes.Length == 0) return string.Empty;

        var baseCall = partes[0];
        string? anadido = null;

        for (var i = 1; i < partes.Length; i++)
        {
            var parte = partes[i];
            if (EsSufijoQueNoCuenta(parte)) continue;
            anadido = parte;
        }

        // Un prefijo delante manda sobre el indicativo: EA8/DL1ABC opera desde Canarias.
        if (partes.Length > 1 && EsPrefijo(partes[0]) && !EsPrefijo(partes[1]))
        {
            baseCall = partes[1];
            anadido = partes[0];
        }

        if (anadido is null) return Recortar(baseCall);

        // Un solo numero cambia el numero del prefijo: N8BJQ/9 es N9.
        if (anadido.Length == 1 && char.IsAsciiDigit(anadido[0]))
        {
            var prefijo = Recortar(baseCall);
            if (prefijo.Length == 0) return anadido;
            var letras = prefijo.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
            return letras + anadido;
        }

        return Recortar(anadido);
    }

    /// <summary>Recorta un indicativo hasta su ultimo numero, anadiendo cero si no tiene.</summary>
    private static string Recortar(string texto)
    {
        if (texto.Length == 0) return string.Empty;
        var ultimoDigito = -1;
        for (var i = 0; i < texto.Length; i++)
        {
            if (char.IsAsciiDigit(texto[i])) ultimoDigito = i;
        }
        // Sin numero, el WPX manda anadir un cero: RAEM cuenta como RA0.
        if (ultimoDigito < 0) return (texto.Length <= 2 ? texto : texto[..2]) + "0";
        return texto[..(ultimoDigito + 1)];
    }

    /// <summary>Un anadido que solo dice como se opera y no cambia el prefijo.</summary>
    private static bool EsSufijoQueNoCuenta(string parte) => parte is
        "P" or "M" or "MM" or "AM" or "A" or "QRP" or "LH" or "LGT" or "B" or "R" or "J";

    /// <summary>El texto tiene pinta de prefijo y no de indicativo completo.</summary>
    private static bool EsPrefijo(string parte)
    {
        if (parte.Length is 0 or > 4) return false;
        // Un prefijo acaba en numero (EA8, VP2E lleva letra detras) o es corto y sin sufijo largo.
        return char.IsAsciiDigit(parte[^1]) || parte.Length <= 3 && parte.Any(char.IsAsciiDigit);
    }
}
