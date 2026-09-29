using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Concursos.Calculo;

/// <summary>
/// Prefijo de un indicativo segun las reglas del WPX.
/// </summary>
/// <remarks>
/// <para>
/// No es «las dos primeras letras». El prefijo del WPX es la parte del indicativo hasta el
/// ultimo numero incluido, con estos matices, que son los que dan los puntos:
/// </para>
/// <list type="bullet">
/// <item>Un indicativo sin numero recibe un cero tras las dos primeras letras: <c>RAEM</c>
/// cuenta como <c>RA0</c>.</item>
/// <item>Un solo numero detras cambia el numero del prefijo: <c>N8BJQ/9</c> es <c>N9</c>.</item>
/// <item>Con un designador portable, «the portable designator will then become the prefix»:
/// el designador pasa a ser el prefijo <b>entero</b>, no recortado hasta su ultimo numero.
/// Por eso <c>VP2E/K1ABC</c> es <c>VP2E</c> (Anguila) y no <c>VP2</c>: <c>VP2E</c>,
/// <c>VP2M</c> y <c>VP2V</c> son multiplicadores distintos. En cambio <c>VP2EABC</c>, que es
/// un indicativo completo y no un designador, si cuenta como <c>VP2</c>.</item>
/// <item>Un designador portable sin numero recibe un cero tras su segunda letra:
/// <c>PA/N8BJQ</c> es <c>PA0</c>.</item>
/// <item><c>/P</c>, <c>/M</c>, <c>/MM</c>, <c>/AM</c>, <c>/A</c>, <c>/E</c>, <c>/J</c> y
/// demas anadidos que solo dicen como se opera no son prefijos y no cambian nada.</item>
/// </list>
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

        // Un designador delante manda sobre el indicativo: EA8/DL1ABC opera desde Canarias.
        if (partes.Length > 1 && EsDesignadorDePrefijo(partes[0]) && !EsDesignadorDePrefijo(partes[1]))
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

        return ComoPrefijo(anadido);
    }

    /// <summary>
    /// Convierte un designador portable en el prefijo que cuenta.
    /// </summary>
    /// <remarks>
    /// El designador pasa a ser el prefijo entero y <b>no</b> se recorta hasta su ultimo
    /// numero: <c>VP2E</c> es <c>VP2E</c>, que no es el mismo multiplicador que <c>VP2M</c>.
    /// Si no lleva numero se le anade un cero tras la segunda letra (<c>PA</c> es <c>PA0</c>).
    /// Solo cuando en lugar de un designador viene un indicativo completo se recorta.
    /// </remarks>
    private static string ComoPrefijo(string parte)
    {
        if (parte.Length == 0) return string.Empty;
        if (!parte.Any(char.IsAsciiDigit)) return (parte.Length <= 2 ? parte : parte[..2]) + "0";
        return EsDesignadorDePrefijo(parte) ? parte : Recortar(parte);
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
        "P" or "M" or "MM" or "AM" or "A" or "E" or "QRP" or "LH" or "LGT" or "B" or "R" or "J";

    /// <summary>
    /// El texto tiene forma de designador de prefijo (<c>EA8</c>, <c>PA</c>, <c>KH9</c>,
    /// <c>VP2E</c>) y no de indicativo completo (<c>K1ABC</c>, <c>DL1ABC</c>).
    /// </summary>
    /// <remarks>
    /// Un designador no pasa de cuatro caracteres y detras de su ultimo numero lleva como mucho
    /// una letra; un indicativo completo lleva dos o mas. Un designador sin ningun numero
    /// (<c>PA</c>, <c>DL</c>, <c>F</c>) tambien vale: el reglamento lo contempla y le pone un
    /// cero detras.
    /// </remarks>
    private static bool EsDesignadorDePrefijo(string parte)
    {
        if (parte.Length is 0 or > 4) return false;
        var ultimoDigito = -1;
        for (var i = 0; i < parte.Length; i++)
        {
            if (char.IsAsciiDigit(parte[i])) ultimoDigito = i;
        }
        if (ultimoDigito < 0) return true;
        return parte.Length - ultimoDigito <= 2;
    }
}
