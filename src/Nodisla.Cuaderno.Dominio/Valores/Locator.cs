namespace Nodisla.Cuaderno.Dominio.Valores;

/// <summary>Localizador Maidenhead (QTH locator) de 2, 4, 6, 8 o 10 caracteres.</summary>
public readonly record struct Locator
{
    private Locator(string valor) => Valor = valor;

    public string Valor { get; }

    public static Locator Vacio => default;

    public bool EsVacio => string.IsNullOrEmpty(Valor);

    /// <summary>Numero de pares: 1 campo, 2 cuadricula, 3 subcuadricula, 4 y 5 extendidos.</summary>
    public int Precision => EsVacio ? 0 : Valor.Length / 2;

    /// <summary>Los pares 0, 2 y 4 son de letras; los pares 1 y 3, de digitos.</summary>
    private static bool ParEsDeLetras(int par) => par % 2 == 0;

    public static bool TryParse(string? texto, out Locator locator)
    {
        locator = Vacio;
        if (string.IsNullOrWhiteSpace(texto)) return false;
        var v = texto.Trim();
        if (v.Length is not (2 or 4 or 6 or 8 or 10)) return false;

        Span<char> destino = stackalloc char[v.Length];
        for (var i = 0; i < v.Length; i++)
        {
            var c = v[i];
            if (ParEsDeLetras(i / 2))
            {
                var mayus = char.ToUpperInvariant(c);
                // El primer par llega hasta R (18 campos); los siguientes, hasta X (24).
                var tope = i / 2 == 0 ? 'R' : 'X';
                if (mayus < 'A' || mayus > tope) return false;
                destino[i] = mayus;
            }
            else
            {
                if (!char.IsAsciiDigit(c)) return false;
                destino[i] = c;
            }
        }
        locator = new Locator(new string(destino));
        return true;
    }

    public static Locator Parse(string? texto) =>
        TryParse(texto, out var l) ? l : throw new FormatException($"Localizador no valido: {texto}");

    /// <summary>Centro geografico del cuadro, en grados decimales.</summary>
    public (double Latitud, double Longitud) ACoordenadas()
    {
        if (EsVacio) throw new InvalidOperationException("Localizador vacio.");

        double lon = -180, lat = -90;
        double anchoLon = 360, anchoLat = 180;

        for (var par = 0; par * 2 < Valor.Length; par++)
        {
            var a = Valor[par * 2];
            var b = Valor[par * 2 + 1];
            if (ParEsDeLetras(par))
            {
                var divisor = par == 0 ? 18 : 24;
                anchoLon /= divisor; anchoLat /= divisor;
                lon += (a - 'A') * anchoLon;
                lat += (b - 'A') * anchoLat;
            }
            else
            {
                anchoLon /= 10; anchoLat /= 10;
                lon += (a - '0') * anchoLon;
                lat += (b - '0') * anchoLat;
            }
        }
        return (lat + anchoLat / 2, lon + anchoLon / 2);
    }

    /// <summary>Calcula el localizador correspondiente a unas coordenadas en grados decimales.</summary>
    public static Locator DesdeCoordenadas(double latitud, double longitud, int precision = 3)
    {
        if (precision is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(precision));

        var lon = Math.Clamp(longitud, -180, 179.99999) + 180;
        var lat = Math.Clamp(latitud, -90, 89.99999) + 90;

        var sb = new System.Text.StringBuilder(precision * 2);
        double anchoLon = 360, anchoLat = 180;
        for (var par = 0; par < precision; par++)
        {
            if (ParEsDeLetras(par))
            {
                var divisor = par == 0 ? 18 : 24;
                anchoLon /= divisor; anchoLat /= divisor;
                var i = (int)(lon / anchoLon); var j = (int)(lat / anchoLat);
                sb.Append((char)('A' + i)).Append((char)('A' + j));
                lon -= i * anchoLon; lat -= j * anchoLat;
            }
            else
            {
                anchoLon /= 10; anchoLat /= 10;
                var i = (int)(lon / anchoLon); var j = (int)(lat / anchoLat);
                sb.Append((char)('0' + i)).Append((char)('0' + j));
                lon -= i * anchoLon; lat -= j * anchoLat;
            }
        }
        return new Locator(sb.ToString());
    }

    public override string ToString() => Valor;
}
