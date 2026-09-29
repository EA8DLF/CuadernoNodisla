namespace Nodisla.Cuaderno.Dominio.Dxcc;

/// <summary>Despiece de un indicativo en las partes que deciden la entidad DXCC.</summary>
internal readonly record struct IndicativoAnalizado
{
    /// <summary>Indicativo entero y normalizado, con todos sus anadidos.</summary>
    public required string Completo { get; init; }

    /// <summary>Indicativo sin los anadidos que solo dicen como se opera (<c>/P</c>, <c>/QRP</c>).</summary>
    public required string Nucleo { get; init; }

    /// <summary>Parte que decide la entidad: el prefijo de operacion si lo hay, si no el indicativo.</summary>
    public required string Clave { get; init; }

    /// <summary>El indicativo no corresponde a ninguna entidad: movil maritimo o aeronautico.</summary>
    public bool SinEntidad { get; init; }

    /// <summary>Habia dos partes con pinta de indicativo completo y no se pudo decidir.</summary>
    public bool Ambiguo { get; init; }
}

/// <summary>
/// Separa un indicativo en prefijo de operacion, indicativo y sufijos.
/// </summary>
/// <remarks>
/// Las reglas que aplica son las de uso: <c>EA8DLF/P</c> sigue siendo EA8; en
/// <c>EA8/DL1ABC</c> manda el prefijo anadido, no el aleman; <c>HC7AE/1</c> cambia de
/// distrito dentro del mismo pais; y <c>/MM</c> y <c>/AM</c> no tienen entidad porque
/// quien opera esta en el mar o en el aire.
/// </remarks>
internal static class AnalizadorIndicativo
{
    /// <summary>Anadidos que hablan de como se opera, no de donde.</summary>
    private static bool EsSufijoDeOperacion(string parte) => parte is
        "P" or "M" or "QRP" or "QRPP" or "A" or "B" or "J" or "LH" or "LGT" or "BCN"
        or "R" or "AG" or "KT" or "PM" or "SK" or "FF" or "MOBILE" or "PORTABLE";

    /// <summary>Anadidos que dejan al indicativo fuera de cualquier entidad DXCC.</summary>
    private static bool EsSinEntidad(string parte) => parte is "MM" or "AM";

    /// <summary>Despieza el indicativo ya normalizado.</summary>
    public static IndicativoAnalizado Analizar(string valor)
    {
        if (string.IsNullOrEmpty(valor))
        {
            return new IndicativoAnalizado
            {
                Completo = string.Empty, Nucleo = string.Empty, Clave = string.Empty,
                SinEntidad = true,
            };
        }

        var partes = valor.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (partes.Length == 1)
        {
            return new IndicativoAnalizado { Completo = valor, Nucleo = valor, Clave = valor };
        }

        var sinEntidad = false;
        var candidatos = new List<string>(partes.Length);
        foreach (var p in partes)
        {
            if (EsSinEntidad(p)) { sinEntidad = true; continue; }
            if (EsSufijoDeOperacion(p)) continue;
            candidatos.Add(p);
        }

        var nucleo = candidatos.Count == 0 ? partes[0] : string.Join('/', candidatos);

        if (sinEntidad)
        {
            return new IndicativoAnalizado
            {
                Completo = valor, Nucleo = nucleo, Clave = string.Empty, SinEntidad = true,
            };
        }

        if (candidatos.Count == 0)
        {
            return new IndicativoAnalizado { Completo = valor, Nucleo = nucleo, Clave = partes[0] };
        }

        if (candidatos.Count == 1)
        {
            return new IndicativoAnalizado { Completo = valor, Nucleo = nucleo, Clave = candidatos[0] };
        }

        // Un solo digito suelto es cambio de distrito dentro del mismo pais: KB1EFS/2.
        var digito = candidatos.FindIndex(static p => p.Length == 1 && char.IsAsciiDigit(p[0]));
        if (digito >= 0 && candidatos.Count == 2)
        {
            var otro = candidatos[1 - digito];
            return new IndicativoAnalizado
            {
                Completo = valor, Nucleo = nucleo, Clave = CambiarDistrito(otro, candidatos[digito][0]),
            };
        }

        // De las partes que quedan, la mas corta es el prefijo de operacion: EA5/ON4ANV.
        var mejor = candidatos[0];
        foreach (var p in candidatos)
        {
            if (p.Length < mejor.Length) mejor = p;
        }

        var ambiguo = candidatos.Count(p => p.Length >= 5 && PareceIndicativoCompleto(p)) >= 2;

        return new IndicativoAnalizado
        {
            Completo = valor, Nucleo = nucleo, Clave = mejor, Ambiguo = ambiguo,
        };
    }

    /// <summary>
    /// Cambia el digito de distrito de un indicativo: el ultimo digito que todavia tiene
    /// letras detras. <c>HC7AE</c> con <c>1</c> se convierte en <c>HC1AE</c>.
    /// </summary>
    private static string CambiarDistrito(string indicativo, char digito)
    {
        for (var i = indicativo.Length - 2; i >= 0; i--)
        {
            if (char.IsAsciiDigit(indicativo[i]))
            {
                return string.Concat(indicativo.AsSpan(0, i), digito.ToString(), indicativo.AsSpan(i + 1));
            }
        }
        return indicativo;
    }

    /// <summary>Un texto con forma de indicativo entero: digito de distrito y sufijo de letras.</summary>
    private static bool PareceIndicativoCompleto(string parte)
    {
        if (parte.Length < 4 || !char.IsAsciiLetter(parte[^1])) return false;
        for (var i = 1; i < parte.Length - 1; i++)
        {
            if (char.IsAsciiDigit(parte[i]) && char.IsAsciiLetter(parte[i + 1])) return true;
        }
        return false;
    }
}
