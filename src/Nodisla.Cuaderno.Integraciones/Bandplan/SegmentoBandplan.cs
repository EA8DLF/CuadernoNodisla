using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Integraciones.Bandplan;

/// <summary>
/// Un tramo del plan de bandas tal y como viene en el recurso: desde una frecuencia hasta
/// otra, con el uso que le corresponde.
/// </summary>
/// <remarks>
/// El limite inferior entra en el tramo y el superior no, salvo en el ultimo tramo de cada
/// banda, donde si entra. Los planes se escriben encadenados (el final de uno es el
/// principio del siguiente) y con este criterio una frecuencia nunca cae en dos tramos.
/// </remarks>
public sealed record SegmentoBandplan
{
    /// <summary>Identificador del plan al que pertenece, por ejemplo <c>r1</c>.</summary>
    public required string Plan { get; init; }

    /// <summary>Banda ADIF del tramo, tal y como la nombra el plan.</summary>
    public required Banda Banda { get; init; }

    /// <summary>Frecuencia donde empieza el tramo.</summary>
    public required Frecuencia Desde { get; init; }

    /// <summary>Frecuencia donde termina el tramo.</summary>
    public required Frecuencia Hasta { get; init; }

    /// <summary>Uso que el plan asigna al tramo.</summary>
    public required UsoDelTramo Uso { get; init; }

    /// <summary>
    /// Banda lateral habitual del tramo, <c>USB</c> o <c>LSB</c>. Es una convencion, no una
    /// norma: por debajo de 10 MHz se usa banda lateral inferior.
    /// </summary>
    public string? Modulacion { get; init; }

    /// <summary>
    /// Clases de licencia que pueden transmitir en el tramo. Vacia significa «todas»: los
    /// planes de Log4OM no traen esta informacion.
    /// </summary>
    public IReadOnlyList<string> Clases { get; init; } = [];

    /// <summary>Anchura del tramo en kilohercios.</summary>
    public decimal AnchuraKhz => Hasta.Kilohercios - Desde.Kilohercios;

    /// <summary>Indica si la clase de licencia dada puede transmitir en el tramo.</summary>
    public bool AdmiteClase(string? clase)
    {
        if (Clases.Count == 0) return true;
        if (string.IsNullOrWhiteSpace(clase)) return false;
        foreach (var c in Clases)
        {
            if (string.Equals(c, clase, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>Texto corto del tramo para mostrarlo al operador.</summary>
    public override string ToString() =>
        $"{Banda} {Desde.Kilohercios:0.###}-{Hasta.Kilohercios:0.###} kHz {Uso}";
}

/// <summary>
/// Frecuencia con nombre propio dentro del plan: la de un modo digital concreto, una
/// frecuencia de llamada o un segmento de balizas.
/// </summary>
/// <param name="Frecuencia">Frecuencia exacta.</param>
/// <param name="Modo">Modo o uso al que esta dedicada, por ejemplo <c>FT8</c>.</param>
/// <param name="Nota">Aclaracion, cuando el plan la trae.</param>
public sealed record FrecuenciaSenalada(Frecuencia Frecuencia, string Modo, string? Nota = null)
{
    /// <summary>Texto para mostrarlo al operador.</summary>
    public string Descripcion => Nota is null ? Modo : $"{Modo} ({Nota})";

    /// <summary>Texto corto para mostrarlo al operador.</summary>
    public override string ToString() => $"{Frecuencia.Kilohercios:0.###} kHz {Descripcion}";
}
