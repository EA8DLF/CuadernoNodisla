namespace Nodisla.Cuaderno.Ui.Recursos;

/// <summary>Un spot que se quiere poner sobre el analizador.</summary>
/// <param name="Hz">Su frecuencia.</param>
/// <param name="AnchoDeEtiqueta">Lo que mide su rotulo, en puntos.</param>
/// <param name="Prioridad">Mayor, antes se coloca (una entidad nueva antes que una ya trabajada).</param>
public readonly record struct SpotParaColocar(double Hz, double AnchoDeEtiqueta, int Prioridad = 0);

/// <summary>Donde ha quedado un spot.</summary>
/// <param name="Indice">Su posicion en la lista de entrada.</param>
/// <param name="X">Donde cae su frecuencia, en puntos desde la izquierda.</param>
/// <param name="Izquierda">Borde izquierdo de su rotulo.</param>
/// <param name="Carril">Carril, de 0 (arriba) hacia abajo.</param>
public readonly record struct SpotColocado(int Indice, double X, double Izquierda, int Carril)
{
    /// <summary>El rotulo va a la izquierda de la marca (no cabia a la derecha).</summary>
    public bool RotuloALaIzquierda => Izquierda < X;
}

/// <summary>
/// Reparte los rotulos de los spots en carriles para que no se pisen.
/// </summary>
/// <remarks>
/// <para>
/// Idea de Thetis (<c>display.cs:getSpotLayer</c>, MW0LGE), reimplementada: por orden de
/// frecuencia, cada spot va al primer carril donde su rotulo no toque a otro. Aqui, ademas,
/// se colocan primero los que aportan algo (entidad nueva, banda o modo nuevos): si no caben
/// todos, los que se quedan fuera son los ya trabajados.
/// </para>
/// <para>
/// El rotulo empieza en la marca y sigue hacia la derecha; si se sale por la derecha o choca,
/// se prueba hacia la izquierda de la marca. Lo que no cabe en ningun carril no se pinta (y se
/// cuenta en <see cref="Colocar"/>).
/// </para>
/// </remarks>
public static class CarrilesDeSpots
{
    /// <summary>Hueco minimo entre dos rotulos del mismo carril, en puntos.</summary>
    public const double Separacion = 3;

    /// <summary>Coloca los spots.</summary>
    /// <param name="spots">Los spots, en cualquier orden.</param>
    /// <param name="escala">De que frecuencia a que frecuencia va el analizador.</param>
    /// <param name="ancho">Ancho del analizador en puntos.</param>
    /// <param name="carriles">Carriles que hay.</param>
    /// <param name="sinSitio">Cuantos spots a la vista no han cabido.</param>
    /// <returns>Los colocados, en orden de frecuencia.</returns>
    public static IReadOnlyList<SpotColocado> Colocar(
        IReadOnlyList<SpotParaColocar> spots, EscalaDelEspectro escala, double ancho, int carriles, out int sinSitio)
    {
        ArgumentNullException.ThrowIfNull(spots);
        sinSitio = 0;
        var colocados = new List<SpotColocado>();
        if (!escala.Valida || ancho <= 0 || carriles <= 0) return colocados;

        var ocupado = new List<(double Desde, double Hasta)>[carriles];
        for (var c = 0; c < carriles; c++) ocupado[c] = [];

        // A la vista, de mas prioridad a menos y, dentro de cada prioridad, por frecuencia.
        var orden = Enumerable.Range(0, spots.Count)
            .Where(i => escala.Contiene(spots[i].Hz))
            .OrderByDescending(i => spots[i].Prioridad)
            .ThenBy(i => spots[i].Hz)
            .ToList();

        foreach (var i in orden)
        {
            var x = escala.XDe(spots[i].Hz, ancho);
            var w = Math.Max(1, spots[i].AnchoDeEtiqueta);
            var aLaDerecha = Math.Clamp(x, 0, Math.Max(0, ancho - w));
            var aLaIzquierda = Math.Clamp(x - w, 0, Math.Max(0, ancho - w));

            var hecho = false;
            for (var c = 0; c < carriles && !hecho; c++)
            {
                foreach (var izquierda in aLaDerecha == aLaIzquierda ? new[] { aLaDerecha } : new[] { aLaDerecha, aLaIzquierda })
                {
                    if (Choca(ocupado[c], izquierda, izquierda + w)) continue;
                    ocupado[c].Add((izquierda, izquierda + w));
                    colocados.Add(new SpotColocado(i, x, izquierda, c));
                    hecho = true;
                    break;
                }
            }

            if (!hecho) sinSitio++;
        }

        colocados.Sort((a, b) => a.X.CompareTo(b.X));
        return colocados;
    }

    private static bool Choca(List<(double Desde, double Hasta)> carril, double desde, double hasta)
    {
        foreach (var (d, h) in carril)
        {
            if (desde < h + Separacion && hasta + Separacion > d) return true;
        }

        return false;
    }
}
