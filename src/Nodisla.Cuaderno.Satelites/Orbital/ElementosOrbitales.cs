using System.Globalization;

namespace Nodisla.Cuaderno.Satelites.Orbital;

/// <summary>
/// Un juego de elementos orbitales de dos lineas (lo que todo el mundo llama TLE).
/// </summary>
/// <remarks>
/// Los angulos se guardan en grados y el movimiento medio en vueltas por dia, que es como
/// vienen escritos en el fichero. El propagador los convierte a sus unidades internas; aqui
/// se conservan tal cual para poder ensenarlos al operador sin deshacer ninguna cuenta.
/// <para>
/// Los elementos <b>caducan</b>: describen la orbita en el instante de su epoca y se degradan
/// a razon de aproximadamente un kilometro de error por dia en orbita baja. Por eso
/// <see cref="Epoca"/> es parte del dato y no un detalle interno: ninguna prediccion deberia
/// ensenarse sin decir de cuando son los elementos con los que se hizo.
/// </para>
/// </remarks>
public sealed record ElementosOrbitales
{
    /// <summary>Nombre del satelite, de la linea 0 si el fichero la trae.</summary>
    public required string Nombre { get; init; }

    /// <summary>Numero de catalogo NORAD.</summary>
    public required int NumeroCatalogo { get; init; }

    /// <summary>Designacion internacional (<c>98067A</c> y similares), vacia si no venia.</summary>
    public string DesignacionInternacional { get; init; } = string.Empty;

    /// <summary>Clasificacion: <c>U</c> sin clasificar, <c>C</c> clasificado, <c>S</c> secreto.</summary>
    public char Clasificacion { get; init; } = 'U';

    /// <summary>Instante al que se refieren los elementos, en UTC.</summary>
    public required DateTimeOffset Epoca { get; init; }

    /// <summary>Primera derivada del movimiento medio, en vueltas por dia al cuadrado, ya dividida entre dos.</summary>
    public double PrimeraDerivadaMovimientoMedio { get; init; }

    /// <summary>Segunda derivada del movimiento medio, en vueltas por dia al cubo, ya dividida entre seis.</summary>
    public double SegundaDerivadaMovimientoMedio { get; init; }

    /// <summary>Coeficiente de rozamiento atmosferico B*, en radios terrestres inversos.</summary>
    public double BEstrella { get; init; }

    /// <summary>Numero de juego de elementos.</summary>
    public int NumeroDeJuego { get; init; }

    /// <summary>Inclinacion en grados.</summary>
    public required double InclinacionGrados { get; init; }

    /// <summary>Ascension recta del nodo ascendente en grados.</summary>
    public required double NodoAscendenteGrados { get; init; }

    /// <summary>Excentricidad, sin unidades.</summary>
    public required double Excentricidad { get; init; }

    /// <summary>Argumento del perigeo en grados.</summary>
    public required double ArgumentoPerigeoGrados { get; init; }

    /// <summary>Anomalia media en grados.</summary>
    public required double AnomaliaMediaGrados { get; init; }

    /// <summary>Movimiento medio en vueltas por dia, tal como viene en la linea 2 (kozai).</summary>
    public required double MovimientoMedioVueltasDia { get; init; }

    /// <summary>Numero de vuelta en la epoca.</summary>
    public int NumeroDeVuelta { get; init; }

    /// <summary>Periodo orbital aproximado que se deduce del movimiento medio.</summary>
    public TimeSpan Periodo => MovimientoMedioVueltasDia > 0
        ? TimeSpan.FromMinutes(1440.0 / MovimientoMedioVueltasDia)
        : TimeSpan.Zero;

    /// <summary>
    /// El satelite esta en orbita alta y necesita el modelo de espacio profundo (SDP4).
    /// </summary>
    /// <remarks>
    /// El corte son los 225 minutos de periodo que fija el propio Spacetrack Report #3. Por
    /// encima de el mandan las perturbaciones del Sol y la Luna y el modelo de orbita baja
    /// deja de valer: mas vale decirlo que dar un numero equivocado.
    /// </remarks>
    public bool EsEspacioProfundo => Periodo.TotalMinutes >= 225.0;

    /// <summary>Cuanto hace que se tomaron estos elementos.</summary>
    /// <param name="ahoraUtc">Momento con el que se compara.</param>
    public TimeSpan Antiguedad(DateTimeOffset ahoraUtc)
    {
        var edad = ahoraUtc - Epoca;
        return edad < TimeSpan.Zero ? TimeSpan.Zero : edad;
    }

    /// <summary>
    /// Los elementos son lo bastante recientes como para fiarse de la prediccion sin avisar.
    /// </summary>
    /// <param name="ahoraUtc">Momento con el que se compara.</param>
    /// <param name="frescura">Plazo dentro del cual se dan por buenos.</param>
    public bool EstanFrescos(DateTimeOffset ahoraUtc, TimeSpan frescura) =>
        Antiguedad(ahoraUtc) <= frescura;

    /// <summary>Frase para el operador que dice de cuando son los elementos.</summary>
    /// <param name="ahoraUtc">Momento con el que se compara.</param>
    /// <param name="frescura">Plazo dentro del cual se dan por buenos.</param>
    /// <remarks>
    /// Nunca se calla la edad. Unos elementos de hace tres semanas ensenados como si fueran de
    /// hoy hacen que el operador apunte la antena donde no hay nada y crea que la culpa es suya.
    /// </remarks>
    public string DescribirAntiguedad(DateTimeOffset ahoraUtc, TimeSpan frescura)
    {
        var es = CultureInfo.GetCultureInfo("es-ES");
        var epoca = Epoca.ToUniversalTime().ToString("dd/MM/yyyy HH:mm", es);
        var edad = TextoDeEdad(Antiguedad(ahoraUtc));

        return EstanFrescos(ahoraUtc, frescura)
            ? $"Elementos del {epoca} UTC, hace {edad}."
            : $"Elementos caducados: son del {epoca} UTC, hace {edad}. "
              + "La predicción se degrada; conviene descargarlos otra vez.";
    }

    /// <summary>Pone una duracion en palabras cortas: "35 min", "2 h 10 min", "3 días".</summary>
    /// <param name="edad">Duracion que se quiere describir.</param>
    public static string TextoDeEdad(TimeSpan edad)
    {
        if (edad < TimeSpan.FromMinutes(1))
        {
            return "menos de un minuto";
        }

        if (edad < TimeSpan.FromHours(1))
        {
            return $"{(int)edad.TotalMinutes} min";
        }

        if (edad < TimeSpan.FromDays(1))
        {
            var horas = (int)edad.TotalHours;
            var minutos = edad.Minutes;
            return minutos == 0 ? $"{horas} h" : $"{horas} h {minutos} min";
        }

        var dias = (int)edad.TotalDays;
        return dias == 1 ? "1 día" : $"{dias} días";
    }
}
