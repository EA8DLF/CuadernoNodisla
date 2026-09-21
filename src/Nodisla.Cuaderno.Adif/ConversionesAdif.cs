using System.Globalization;

namespace Nodisla.Cuaderno.Adif;

/// <summary>
/// Conversion entre los tipos de dato de ADIF y los del modelo.
/// </summary>
/// <remarks>
/// ADIF no usa el formato que uno esperaria en casi nada: las fechas van pegadas sin separador,
/// las horas pueden traer o no los segundos y las coordenadas vienen en grados y minutos con
/// una letra delante. Todo eso se resuelve aqui, en los dos sentidos, para que el mapeo de
/// campos no se llene de casos particulares.
/// </remarks>
public static class ConversionesAdif
{
    private static readonly CultureInfo Invariante = CultureInfo.InvariantCulture;

    // ── Fechas y horas ───────────────────────────────────────────────────────

    /// <summary>Lee una fecha ADIF con forma <c>AAAAMMDD</c>.</summary>
    public static bool TryLeerFecha(string? texto, out DateOnly fecha)
    {
        fecha = default;
        if (string.IsNullOrWhiteSpace(texto)) return false;
        var v = texto.Trim();
        if (v.Length != 8) return false;
        return DateOnly.TryParseExact(v, "yyyyMMdd", Invariante, DateTimeStyles.None, out fecha);
    }

    /// <summary>
    /// Lee una hora ADIF. El estandar admite <c>HHMM</c> y <c>HHMMSS</c>, y los ficheros reales
    /// traen las dos formas incluso dentro del mismo cuaderno.
    /// </summary>
    public static bool TryLeerHora(string? texto, out TimeSpan hora)
    {
        hora = default;
        if (string.IsNullOrWhiteSpace(texto)) return false;
        var v = texto.Trim();
        if (v.Length is not (4 or 6)) return false;
        foreach (var c in v)
        {
            if (!char.IsAsciiDigit(c)) return false;
        }

        var h = int.Parse(v[..2], Invariante);
        var m = int.Parse(v.Substring(2, 2), Invariante);
        var s = v.Length == 6 ? int.Parse(v.Substring(4, 2), Invariante) : 0;
        if (h > 23 || m > 59 || s > 59) return false;
        hora = new TimeSpan(h, m, s);
        return true;
    }

    /// <summary>Combina fecha y hora ADIF en un instante UTC.</summary>
    public static bool TryCombinarUtc(string? fechaTexto, string? horaTexto, out DateTimeOffset instante)
    {
        instante = default;
        if (!TryLeerFecha(fechaTexto, out var fecha)) return false;
        _ = TryLeerHora(horaTexto, out var hora);
        instante = new DateTimeOffset(fecha.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) + hora, TimeSpan.Zero);
        return true;
    }

    /// <summary>Escribe la parte de fecha en formato ADIF.</summary>
    public static string EscribirFecha(DateTimeOffset instante) =>
        instante.UtcDateTime.ToString("yyyyMMdd", Invariante);

    /// <summary>Escribe la parte de hora en formato ADIF, siempre con segundos.</summary>
    public static string EscribirHora(DateTimeOffset instante) =>
        instante.UtcDateTime.ToString("HHmmss", Invariante);

    /// <summary>Lee una marca de tiempo ISO-8601 como las que guarda Log4OM en sus campos JSON.</summary>
    public static bool TryLeerIso(string? texto, out DateTimeOffset instante)
    {
        instante = default;
        if (string.IsNullOrWhiteSpace(texto)) return false;
        if (!DateTimeOffset.TryParse(
                texto.Trim(), Invariante, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var v))
        {
            return false;
        }
        instante = v;
        return true;
    }

    // ── Coordenadas ──────────────────────────────────────────────────────────

    /// <summary>
    /// Lee una coordenada en el formato de ADIF: letra de hemisferio, grados y minutos
    /// decimales, por ejemplo <c>N027 58.950</c>.
    /// </summary>
    /// <remarks>
    /// Si el valor no trae letra de hemisferio se admite tambien como grados decimales a secas,
    /// porque hay programas que exportan asi aunque el estandar no lo permita.
    /// </remarks>
    public static bool TryLeerCoordenada(string? texto, out double grados)
    {
        grados = 0;
        if (string.IsNullOrWhiteSpace(texto)) return false;
        var v = texto.Trim();

        var signo = char.ToUpperInvariant(v[0]) switch
        {
            'N' or 'E' => 1,
            'S' or 'W' => -1,
            _ => 0,
        };

        if (signo == 0)
        {
            return double.TryParse(v, NumberStyles.Float, Invariante, out grados);
        }

        var resto = v[1..].Trim();
        if (resto.Length == 0) return false;

        string textoGrados;
        string textoMinutos;
        var espacio = resto.IndexOf(' ');
        if (espacio > 0)
        {
            textoGrados = resto[..espacio];
            textoMinutos = resto[(espacio + 1)..].Trim();
        }
        else
        {
            // Variante pegada: NDDDMM.MMM.
            var punto = resto.IndexOf('.');
            if (punto >= 3)
            {
                textoGrados = resto[..(punto - 2)];
                textoMinutos = resto[(punto - 2)..];
            }
            else
            {
                textoGrados = resto;
                textoMinutos = "0";
            }
        }

        if (!int.TryParse(textoGrados, NumberStyles.Integer, Invariante, out var g)) return false;
        if (!double.TryParse(textoMinutos, NumberStyles.Float, Invariante, out var m)) return false;

        grados = signo * (g + (m / 60.0));
        return true;
    }

    /// <summary>Escribe una latitud en formato ADIF.</summary>
    public static string EscribirLatitud(double grados) => EscribirCoordenada(grados, 'N', 'S');

    /// <summary>Escribe una longitud en formato ADIF.</summary>
    public static string EscribirLongitud(double grados) => EscribirCoordenada(grados, 'E', 'W');

    private static string EscribirCoordenada(double grados, char positivo, char negativo)
    {
        var letra = grados < 0 ? negativo : positivo;
        var absoluto = Math.Abs(grados);
        var enteros = (int)absoluto;
        var minutos = Math.Round((absoluto - enteros) * 60.0, 3, MidpointRounding.AwayFromZero);
        if (minutos >= 60.0)
        {
            minutos -= 60.0;
            enteros++;
        }
        return string.Create(
            Invariante,
            $"{letra}{enteros.ToString("D3", Invariante)} {minutos.ToString("00.000", Invariante)}");
    }

    // ── Numeros y logicos ────────────────────────────────────────────────────

    /// <summary>Lee un entero ADIF.</summary>
    public static bool TryLeerEntero(string? texto, out int valor) =>
        int.TryParse((texto ?? string.Empty).Trim(), NumberStyles.Integer, Invariante, out valor);

    /// <summary>Lee un numero real ADIF, que siempre usa el punto como separador decimal.</summary>
    public static bool TryLeerReal(string? texto, out double valor) =>
        double.TryParse((texto ?? string.Empty).Trim(), NumberStyles.Float, Invariante, out valor);

    /// <summary>Escribe un numero real sin ceros de relleno ni separador de millares.</summary>
    public static string EscribirReal(double valor) => valor.ToString("0.##########", Invariante);

    /// <summary>Escribe un entero.</summary>
    public static string EscribirEntero(int valor) => valor.ToString(Invariante);

    /// <summary>Lee un campo logico ADIF, que vale <c>Y</c> o <c>N</c>.</summary>
    public static bool TryLeerLogico(string? texto, out bool valor)
    {
        valor = false;
        if (string.IsNullOrWhiteSpace(texto)) return false;
        switch (char.ToUpperInvariant(texto.Trim()[0]))
        {
            case 'Y': valor = true; return true;
            case 'N': valor = false; return true;
            default: return false;
        }
    }

    /// <summary>Escribe un campo logico ADIF.</summary>
    public static string EscribirLogico(bool valor) => valor ? "Y" : "N";
}
