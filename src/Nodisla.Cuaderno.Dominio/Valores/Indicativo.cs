namespace Nodisla.Cuaderno.Dominio.Valores;

/// <summary>Indicativo de una estacion, normalizado a mayusculas y sin espacios.</summary>
public readonly record struct Indicativo
{
    private Indicativo(string valor) => Valor = valor;

    /// <summary>Texto normalizado del indicativo, por ejemplo <c>EA8DLF/P</c>.</summary>
    public string Valor { get; }

    public static Indicativo Vacio => default;

    public bool EsVacio => string.IsNullOrEmpty(Valor);

    /// <summary>
    /// Longitud a partir de la cual se deja de usar la pila. Un indicativo real no pasa de 24
    /// caracteres, pero <see cref="Normalizar"/> es publico y lo llama la importacion de ADIF
    /// ajeno, donde un campo puede venir con cualquier tamano.
    /// </summary>
    private const int MaximoEnPila = 256;

    /// <summary>Normaliza el texto: mayusculas, sin espacios y con las barras unificadas.</summary>
    public static string Normalizar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return string.Empty;

        char[]? prestado = null;
        var destino = texto.Length <= MaximoEnPila
            ? stackalloc char[MaximoEnPila]
            : (prestado = System.Buffers.ArrayPool<char>.Shared.Rent(texto.Length));
        try
        {
            var n = 0;
            foreach (var c in texto)
            {
                if (char.IsWhiteSpace(c)) continue;
                destino[n++] = c switch
                {
                    '\\' => '/',
                    '-' => '/',
                    _ => char.ToUpperInvariant(c),
                };
            }
            return new string(destino[..n]);
        }
        finally
        {
            if (prestado is not null) System.Buffers.ArrayPool<char>.Shared.Return(prestado);
        }
    }

    public static bool TryParse(string? texto, out Indicativo indicativo)
    {
        var v = Normalizar(texto);
        if (!EsFormaValida(v)) { indicativo = Vacio; return false; }
        indicativo = new Indicativo(v);
        return true;
    }

    /// <summary>Crea el indicativo o lanza <see cref="FormatException"/> si no es valido.</summary>
    public static Indicativo Parse(string? texto) =>
        TryParse(texto, out var i) ? i : throw new FormatException($"Indicativo no valido: {texto}");

    /// <summary>
    /// Crea el indicativo sin validar la forma. Se usa al importar ADIF ajeno, donde aparecen
    /// indicativos raros que no conviene rechazar ni perder.
    /// </summary>
    public static Indicativo Crudo(string? texto) => new(Normalizar(texto));

    /// <summary>
    /// Comprobacion de forma deliberadamente laxa: al menos una letra y un digito, longitud
    /// razonable y solo caracteres permitidos. Las excepciones reales (indicativos especiales
    /// y conmemorativos) se resuelven con la lista de indicativos no estandar, no aqui.
    /// </summary>
    public static bool EsFormaValida(string valor)
    {
        if (valor.Length is < 3 or > 24) return false;
        var tieneLetra = false;
        var tieneDigito = false;
        foreach (var c in valor)
        {
            if (char.IsAsciiLetterUpper(c)) tieneLetra = true;
            else if (char.IsAsciiDigit(c)) tieneDigito = true;
            else if (c is not ('/' or '.')) return false;
        }
        return tieneLetra && tieneDigito;
    }

    /// <summary>
    /// Parte principal del indicativo, descartando los sufijos de operacion
    /// (<c>/P</c>, <c>/M</c>, <c>/QRP</c>) y quedandose con el fragmento significativo.
    /// </summary>
    public string Base
    {
        get
        {
            if (EsVacio) return string.Empty;
            var partes = Valor.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (partes.Length == 1) return partes[0];

            var mejor = partes[0];
            foreach (var p in partes)
            {
                if (EsSufijoDeOperacion(p)) continue;
                if (EsSufijoDeOperacion(mejor) || p.Length > mejor.Length) mejor = p;
            }
            return mejor;
        }
    }

    /// <summary>Sufijos que indican forma de operacion, no ubicacion ni entidad DXCC.</summary>
    public static bool EsSufijoDeOperacion(string parte) => parte is
        "P" or "M" or "MM" or "AM" or "QRP" or "QRPP" or "A" or "B" or "J" or "LH" or "LGT" or "BCN";

    public override string ToString() => Valor;

    public static implicit operator string(Indicativo i) => i.Valor;
}
