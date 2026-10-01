namespace Nodisla.Cuaderno.Modos.Q65;

/// <summary>
/// Guarda los ultimos periodos para sumar una misma senal a lo largo de varios.
/// </summary>
/// <remarks>
/// <para>
/// En EME una estacion repite el mismo mensaje periodo tras periodo, y cada periodo por
/// separado puede no llegar. Sumar las energias de varios periodos en la misma frecuencia y
/// el mismo desfase sube la senal en proporcion al numero de periodos y el ruido solo en
/// proporcion a su raiz, asi que la suma decodifica lo que ninguno decodificaba solo. Es el
/// promediado de mensajes de Q65, y se hace sobre energias, no sobre decisiones.
/// </para>
/// <para>
/// El problema es saber <i>donde</i> sumar: una senal que no llega tampoco destaca en el
/// sincronismo de un periodo. Por eso se suma tambien el mapa de sincronismo, que si crece
/// con los periodos, y se guardan los espectrogramas enteros de los ultimos periodos para
/// poder medir la senal en el sitio que diga el mapa sumado. Cuestan unos megabytes por
/// periodo; se guardan hasta una cantidad fija y los mas viejos se olvidan.
/// </para>
/// </remarks>
public sealed class PromediadorDeQ65
{
    private readonly List<EspectrogramaDeQ65> _espectrogramas = [];
    private MapaDeSincronismo? _mapa;

    /// <summary>Periodos que se guardan como mucho.</summary>
    public int PeriodosMaximos { get; set; } = 6;

    /// <summary>Periodos guardados.</summary>
    public int Periodos => _espectrogramas.Count;

    /// <summary>El mapa de sincronismo sumado de los periodos guardados, si hay alguno.</summary>
    public MapaDeSincronismo? Mapa => _mapa;

    /// <summary>Anade un periodo. Si se pasa del maximo, se olvida el mas viejo y se rehace la suma.</summary>
    public void Anadir(EspectrogramaDeQ65 espectrograma, MapaDeSincronismo mapa)
    {
        ArgumentNullException.ThrowIfNull(espectrograma);
        ArgumentNullException.ThrowIfNull(mapa);
        if (_espectrogramas.Count > 0 && !MismaForma(_espectrogramas[0], espectrograma))
            Limpiar();

        _espectrogramas.Add(espectrograma);
        while (_espectrogramas.Count > PeriodosMaximos) _espectrogramas.RemoveAt(0);

        // El mapa sumado se rehace desde los guardados para que olvidar un periodo tambien lo
        // reste. Rehacer es barato comparado con calcular los espectrogramas.
        _mapa = null;
        foreach (var e in _espectrogramas)
        {
            var m = ReferenceEquals(e, espectrograma) ? mapa : SincronizadorDeQ65.Mapa(e, mapa.Tablas);
            if (_mapa is null) _mapa = Copia(m);
            else _mapa.Sumar(m);
        }
    }

    /// <summary>Mide una candidata en todos los periodos guardados y devuelve la suma.</summary>
    public MedidaDeQ65 Medir(CandidataDeQ65 candidata)
    {
        if (_espectrogramas.Count == 0) throw new InvalidOperationException("No hay periodos guardados.");
        MedidaDeQ65? suma = null;
        foreach (var e in _espectrogramas)
        {
            var medida = DemoduladorDeQ65.Medir(e, candidata);
            if (suma is null) suma = medida;
            else suma.Sumar(medida);
        }
        return suma!;
    }

    /// <summary>Olvida todo.</summary>
    public void Limpiar()
    {
        _espectrogramas.Clear();
        _mapa = null;
    }

    private static bool MismaForma(EspectrogramaDeQ65 a, EspectrogramaDeQ65 b) =>
        a.Columnas == b.Columnas && a.CasillaMinima == b.CasillaMinima && a.Casillas == b.Casillas
        && ReferenceEquals(a.Parametros, b.Parametros);

    private static MapaDeSincronismo Copia(MapaDeSincronismo m)
    {
        var copia = new MapaDeSincronismo(m.PrimeraColumna, m.Columnas, m.CasillaMinima, m.Casillas, m.Tablas);
        m.Puntuaciones.CopyTo(copia.Puntuaciones, 0);
        return copia;
    }
}
