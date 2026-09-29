namespace Nodisla.Cuaderno.Radio.Control.Ft710;

/// <summary>
/// Que ancho de filtro es cada indice de la orden <c>SH</c>, segun el modo.
/// </summary>
/// <remarks>
/// <para>
/// Sale de la tabla 3 del <b>manual de referencia CAT</b> de Yaesu
/// (<c>FT-710_CAT_OM_ENG_2306-C.pdf</c>), que es un documento <b>distinto</b> del manual de
/// operacion: el de operacion describe el frontal y el de ordenes describe el CAT. Buscar en el
/// que no era costo dos rondas y estuvo a punto de costar tocarle la radio al operador sin
/// necesidad.
/// </para>
/// <para>
/// El indice <c>00</c> es «por omision» y <b>no tiene un numero</b>: depende del modo y lo pone
/// el equipo. Se ensena como tal en vez de inventarle hercios.
/// </para>
/// </remarks>
public static class AnchosDeFiltroFt710
{
    /// <summary>Como se ensena el indice cero, que no es un ancho sino «lo que ponga el equipo».</summary>
    public const string PorOmision = "por omisión";

    private static readonly int[] BandaLateral =
    [
        300, 400, 600, 850, 1100, 1200, 1500, 1650, 1800, 1950, 2100, 2250,
        2400, 2450, 2500, 2600, 2700, 2800, 2900, 3000, 3200, 3500, 4000,
    ];

    private static readonly int[] TelegrafiaYDatos =
    [
        50, 100, 150, 200, 250, 300, 350, 400, 450, 500, 600, 800,
        1200, 1400, 1700, 2000, 2400, 3000, 3200, 3500, 4000,
    ];

    /// <summary>
    /// Anchos de un modo, empezando por el indice 01. Nulo si ese modo no tiene tabla.
    /// </summary>
    /// <param name="modoDelEquipo">Modo tal y como lo nombra el equipo, por ejemplo <c>USB</c>.</param>
    /// <returns>Los anchos en hercios, o nulo.</returns>
    private static int[]? TablaDe(string? modoDelEquipo) => modoDelEquipo?.ToUpperInvariant() switch
    {
        "LSB" or "USB" => BandaLateral,
        "CW" or "CWR" or "PKTLSB" or "PKTUSB" or "DATA-L" or "DATA-U" or "PSK" => TelegrafiaYDatos,
        _ => null,
    };

    /// <summary>
    /// Anchos fijos de los modos que solo tienen uno, con el indice en el que viven.
    /// </summary>
    private static (int Indice, int Hercios)? FijoDe(string? modoDelEquipo) =>
        modoDelEquipo?.ToUpperInvariant() switch
        {
            "AM-N" => (1, 6000),
            "AM" or "FM-N" or "PKTFM-N" => (2, 9000),
            "FM" or "PKTFM" => (3, 16000),
            _ => null,
        };

    /// <summary>
    /// Pasa el indice de <c>SH</c> a hercios para un modo.
    /// </summary>
    /// <param name="indice">Indice que contesta el equipo.</param>
    /// <param name="modoDelEquipo">Modo en el que esta el equipo.</param>
    /// <returns>
    /// Los hercios, o nulo si el indice es «por omision», si el modo no se conoce o si ese
    /// indice no existe en ese modo.
    /// </returns>
    public static int? Hercios(int indice, string? modoDelEquipo)
    {
        if (indice <= 0)
        {
            // El cero es «por omision»: no es un ancho.
            return null;
        }

        if (FijoDe(modoDelEquipo) is { } fijo)
        {
            return indice == fijo.Indice ? fijo.Hercios : null;
        }

        var tabla = TablaDe(modoDelEquipo);
        return tabla is not null && indice <= tabla.Length ? tabla[indice - 1] : null;
    }

    /// <summary>
    /// Etiquetas de todos los indices de un modo, para que la interfaz ensene hercios y no
    /// numeros internos.
    /// </summary>
    /// <param name="modoDelEquipo">Modo en el que esta el equipo.</param>
    /// <returns>Las etiquetas por indice, o nulo si de ese modo no se sabe la tabla.</returns>
    public static IReadOnlyList<string>? Etiquetas(string? modoDelEquipo)
    {
        if (FijoDe(modoDelEquipo) is { } fijo)
        {
            var etiquetas = new string[fijo.Indice + 1];
            for (var i = 0; i < etiquetas.Length; i++)
            {
                etiquetas[i] = i == 0 ? PorOmision : i == fijo.Indice ? $"{fijo.Hercios} Hz" : "—";
            }

            return etiquetas;
        }

        var tabla = TablaDe(modoDelEquipo);
        if (tabla is null)
        {
            return null;
        }

        var nombres = new string[tabla.Length + 1];
        nombres[0] = PorOmision;
        for (var i = 0; i < tabla.Length; i++)
        {
            nombres[i + 1] = $"{tabla[i]} Hz";
        }

        return nombres;
    }

    /// <summary>Indice mas alto que admite un modo. Cero si no se conoce su tabla.</summary>
    /// <param name="modoDelEquipo">Modo en el que esta el equipo.</param>
    /// <returns>El indice mayor.</returns>
    public static int IndiceMaximo(string? modoDelEquipo)
    {
        if (FijoDe(modoDelEquipo) is { } fijo)
        {
            return fijo.Indice;
        }

        return TablaDe(modoDelEquipo)?.Length ?? 0;
    }
}

/// <summary>
/// Que retardo del circuito de voz es cada indice de la orden <c>VD</c>.
/// </summary>
/// <remarks>
/// <para>
/// Del manual de referencia CAT: <c>00</c> son 30 ms, <c>01</c> a <c>06</c> van de 50 en 50
/// hasta 300 ms, y de <c>06</c> a <c>33</c> se sube hasta 3000 ms. Los extremos son literales
/// del manual; los intermedios salen de la unica progresion que los une —cien milisegundos por
/// paso— y cuadran exactamente con los 3000 ms del indice 33. Si alguna vez se ve un desajuste
/// contra el equipo, se corrige aqui.
/// </para>
/// <para>
/// <b>Trampa del firmware</b>: el manual declara cuatro cifras para este parametro y este equipo
/// contesta con dos (<c>VD08</c>). Al leer se admiten las dos longitudes.
/// </para>
/// </remarks>
public static class RetardosDeVozFt710
{
    /// <summary>Indice mas alto.</summary>
    public const int IndiceMaximo = 33;

    /// <summary>Pasa el indice de <c>VD</c> a milisegundos.</summary>
    /// <param name="indice">Indice que contesta el equipo.</param>
    /// <returns>El retardo en milisegundos.</returns>
    public static int Milisegundos(int indice)
    {
        var acotado = Math.Clamp(indice, 0, IndiceMaximo);
        return acotado switch
        {
            0 => 30,
            <= 6 => acotado * 50,
            _ => 300 + ((acotado - 6) * 100),
        };
    }

    /// <summary>Pasa unos milisegundos al indice mas cercano que admite el equipo.</summary>
    /// <param name="milisegundos">Retardo que quiere el operador.</param>
    /// <returns>El indice.</returns>
    public static int Indice(double milisegundos)
    {
        var mejor = 0;
        var distancia = double.MaxValue;
        for (var i = 0; i <= IndiceMaximo; i++)
        {
            var diferencia = Math.Abs(Milisegundos(i) - milisegundos);
            if (diferencia >= distancia)
            {
                continue;
            }

            distancia = diferencia;
            mejor = i;
        }

        return mejor;
    }

    /// <summary>Etiquetas de todos los indices, para que la interfaz ensene milisegundos.</summary>
    /// <returns>Las etiquetas por indice.</returns>
    public static IReadOnlyList<string> Etiquetas()
    {
        var nombres = new string[IndiceMaximo + 1];
        for (var i = 0; i <= IndiceMaximo; i++)
        {
            nombres[i] = $"{Milisegundos(i)} ms";
        }

        return nombres;
    }
}
