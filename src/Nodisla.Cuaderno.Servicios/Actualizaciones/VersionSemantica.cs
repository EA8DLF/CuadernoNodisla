using System.Globalization;

namespace Nodisla.Cuaderno.Servicios.Actualizaciones;

/// <summary>
/// Numero de version al estilo SemVer 2.0: <c>mayor.menor.parche[-previa][+compilacion]</c>.
/// </summary>
/// <remarks>
/// <para>
/// Acepta tambien lo que no es SemVer estricto pero sale en la practica: la <c>v</c> de las
/// etiquetas de GitHub (<c>v0.2.0</c>), los numeros de ensamblado de cuatro cifras
/// (<c>0.1.0.0</c>, la cuarta tiene que ser cero o se compara como una cifra mas) y las versiones
/// cortas (<c>1.2</c> es <c>1.2.0</c>).
/// </para>
/// <para>
/// Lo que va detras de <c>+</c> (el identificador de compilacion que el SDK de .NET pega a la
/// version informativa, por ejemplo <c>0.1.0+1e0207d</c>) no cuenta para comparar, como manda
/// SemVer.
/// </para>
/// </remarks>
public sealed class VersionSemantica : IComparable<VersionSemantica>, IEquatable<VersionSemantica>
{
    private VersionSemantica(int[] cifras, string[] previa, string texto)
    {
        Cifras = cifras;
        Previa = previa;
        Texto = texto;
    }

    /// <summary>Las cifras numericas: al menos tres (mayor, menor, parche), a veces cuatro.</summary>
    public IReadOnlyList<int> Cifras { get; }

    /// <summary>Los identificadores de version previa (<c>beta.2</c> son dos). Vacio si es estable.</summary>
    public IReadOnlyList<string> Previa { get; }

    /// <summary>La version tal como se leyo, sin la <c>v</c> ni la parte de compilacion.</summary>
    public string Texto { get; }

    /// <summary>Es una version previa (alfa, beta, candidata...).</summary>
    public bool EsPrevia => Previa.Count > 0;

    /// <summary>Intenta leer una version.</summary>
    /// <param name="texto">Etiqueta o numero de version.</param>
    /// <param name="version">La version leida, o nula si no es una version.</param>
    /// <returns>Cierto si se pudo leer.</returns>
    public static bool TryAnalizar(string? texto, out VersionSemantica? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(texto)) return false;

        var limpio = texto.Trim();
        if (limpio.StartsWith('v') || limpio.StartsWith('V')) limpio = limpio[1..];

        var mas = limpio.IndexOf('+', StringComparison.Ordinal);
        if (mas >= 0) limpio = limpio[..mas];

        var guion = limpio.IndexOf('-', StringComparison.Ordinal);
        var nucleo = guion >= 0 ? limpio[..guion] : limpio;
        var previa = guion >= 0 ? limpio[(guion + 1)..] : string.Empty;

        var partes = nucleo.Split('.');
        if (partes.Length is < 1 or > 4) return false;

        var cifras = new int[Math.Max(3, partes.Length)];
        for (var i = 0; i < partes.Length; i++)
        {
            if (partes[i].Length == 0 || !partes[i].All(char.IsAsciiDigit)) return false;
            if (!int.TryParse(partes[i], NumberStyles.None, CultureInfo.InvariantCulture, out cifras[i])) return false;
        }

        string[] identificadores = [];
        if (guion >= 0)
        {
            identificadores = previa.Split('.');
            if (identificadores.Any(p => p.Length == 0 || !p.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')))
            {
                return false;
            }
        }

        version = new VersionSemantica(cifras, identificadores, limpio);
        return true;
    }

    /// <summary>Lee una version o lanza si no lo es.</summary>
    /// <param name="texto">Etiqueta o numero de version.</param>
    public static VersionSemantica Analizar(string texto) =>
        TryAnalizar(texto, out var version)
            ? version!
            : throw new FormatException($"«{texto}» no es un número de versión.");

    /// <inheritdoc />
    public int CompareTo(VersionSemantica? otra)
    {
        if (otra is null) return 1;

        var largo = Math.Max(Cifras.Count, otra.Cifras.Count);
        for (var i = 0; i < largo; i++)
        {
            var a = i < Cifras.Count ? Cifras[i] : 0;
            var b = i < otra.Cifras.Count ? otra.Cifras[i] : 0;
            if (a != b) return a.CompareTo(b);
        }

        // Una version estable es mayor que cualquier previa del mismo numero: 1.0.0 > 1.0.0-rc.1.
        if (!EsPrevia && !otra.EsPrevia) return 0;
        if (!EsPrevia) return 1;
        if (!otra.EsPrevia) return -1;

        var comunes = Math.Min(Previa.Count, otra.Previa.Count);
        for (var i = 0; i < comunes; i++)
        {
            var comparacion = CompararIdentificador(Previa[i], otra.Previa[i]);
            if (comparacion != 0) return comparacion;
        }

        return Previa.Count.CompareTo(otra.Previa.Count);
    }

    // SemVer: los numericos se comparan como numeros y van antes que los alfanumericos, que
    // se comparan por orden ASCII.
    private static int CompararIdentificador(string a, string b)
    {
        var aNum = int.TryParse(a, NumberStyles.None, CultureInfo.InvariantCulture, out var na);
        var bNum = int.TryParse(b, NumberStyles.None, CultureInfo.InvariantCulture, out var nb);
        if (aNum && bNum) return na.CompareTo(nb);
        if (aNum) return -1;
        if (bNum) return 1;
        return string.CompareOrdinal(a, b);
    }

    /// <inheritdoc />
    public bool Equals(VersionSemantica? otra) => CompareTo(otra) == 0;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is VersionSemantica otra && Equals(otra);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        // Sin los ceros finales: 0.1.0 y 0.1.0.0 son la misma version y deben dar el mismo hash.
        var largo = Cifras.Count;
        while (largo > 0 && Cifras[largo - 1] == 0) largo--;
        for (var i = 0; i < largo; i++) hash.Add(Cifras[i]);
        foreach (var p in Previa) hash.Add(p, StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    /// <summary>Mayor que.</summary>
    public static bool operator >(VersionSemantica a, VersionSemantica b) => a.CompareTo(b) > 0;

    /// <summary>Menor que.</summary>
    public static bool operator <(VersionSemantica a, VersionSemantica b) => a.CompareTo(b) < 0;

    /// <summary>Mayor o igual.</summary>
    public static bool operator >=(VersionSemantica a, VersionSemantica b) => a.CompareTo(b) >= 0;

    /// <summary>Menor o igual.</summary>
    public static bool operator <=(VersionSemantica a, VersionSemantica b) => a.CompareTo(b) <= 0;

    /// <inheritdoc />
    public override string ToString() => Texto;
}
