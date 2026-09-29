using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Integraciones.Bandplan;

/// <summary>
/// Un plan de bandas completo: el de una region IARU o el de un pais concreto.
/// </summary>
/// <remarks>
/// Los tramos se guardan ordenados por frecuencia para poder buscar por biseccion. La
/// consulta se hace con cada cambio de VFO, asi que tiene que ser inmediata.
/// </remarks>
public sealed class PlanDeBanda
{
    private readonly SegmentoBandplan[] _segmentos;
    private readonly FrecuenciaSenalada[] _senaladas;

    /// <summary>
    /// Para cada posicion, el final mas alto de todos los tramos hasta ella. Permite cortar
    /// la busqueda hacia atras en cuanto se sabe que ya no puede haber tramo que contenga
    /// la frecuencia, aunque los tramos se solapen.
    /// </summary>
    private readonly decimal[] _finalMaximo;

    internal PlanDeBanda(
        string identificador,
        int region,
        int dxcc,
        string nombre,
        SegmentoBandplan[] segmentos,
        FrecuenciaSenalada[] senaladas)
    {
        Identificador = identificador;
        Region = region;
        Dxcc = dxcc;
        Nombre = nombre;
        _segmentos = segmentos;
        _senaladas = senaladas;

        _finalMaximo = new decimal[segmentos.Length];
        var maximo = decimal.MinValue;
        for (var i = 0; i < segmentos.Length; i++)
        {
            if (segmentos[i].Hasta.Megahercios > maximo) maximo = segmentos[i].Hasta.Megahercios;
            _finalMaximo[i] = maximo;
        }
    }

    /// <summary>Identificador del plan, por ejemplo <c>r1</c> o <c>dxcc291</c>.</summary>
    public string Identificador { get; }

    /// <summary>Region IARU del plan. Cero si el plan es nacional.</summary>
    public int Region { get; }

    /// <summary>Entidad DXCC del plan. Cero si el plan es de una region entera.</summary>
    public int Dxcc { get; }

    /// <summary>Nombre del plan para mostrarlo al operador.</summary>
    public string Nombre { get; }

    /// <summary>El plan es el de un pais concreto y no el de una region entera.</summary>
    public bool EsNacional => Dxcc != 0;

    /// <summary>Tramos del plan, ordenados por frecuencia.</summary>
    public IReadOnlyList<SegmentoBandplan> Segmentos => _segmentos;

    /// <summary>Frecuencias con nombre propio del plan, ordenadas por frecuencia.</summary>
    public IReadOnlyList<FrecuenciaSenalada> Senaladas => _senaladas;

    /// <summary>
    /// Tramo en el que cae la frecuencia, o nulo si el plan no cubre esa frecuencia.
    /// </summary>
    public SegmentoBandplan? Buscar(Frecuencia frecuencia)
    {
        if (frecuencia.EsCero || _segmentos.Length == 0) return null;
        var mhz = frecuencia.Megahercios;

        // Biseccion sobre el limite inferior: se busca el ultimo tramo que empiece en la
        // frecuencia o antes.
        var izquierda = 0;
        var derecha = _segmentos.Length - 1;
        var candidato = -1;
        while (izquierda <= derecha)
        {
            var medio = izquierda + ((derecha - izquierda) / 2);
            if (_segmentos[medio].Desde.Megahercios <= mhz)
            {
                candidato = medio;
                izquierda = medio + 1;
            }
            else
            {
                derecha = medio - 1;
            }
        }
        if (candidato < 0) return null;

        // Desde ahi hacia atras, el primer tramo que contenga la frecuencia. El tramo que
        // solo la toca por el final se guarda aparte: vale si ningun otro la contiene, que
        // es lo que pasa en el limite superior de cada banda.
        SegmentoBandplan? enElBorde = null;
        for (var i = candidato; i >= 0 && _finalMaximo[i] >= mhz; i--)
        {
            var s = _segmentos[i];
            if (s.Desde.Megahercios > mhz) continue;
            if (mhz < s.Hasta.Megahercios) return s;
            if (mhz == s.Hasta.Megahercios) enElBorde ??= s;
        }
        return enElBorde;
    }

    /// <summary>
    /// Frecuencia con nombre propio mas cercana, dentro de la tolerancia dada en hercios.
    /// </summary>
    public FrecuenciaSenalada? Senalada(Frecuencia frecuencia, int toleranciaHz = 500)
    {
        FrecuenciaSenalada? mejor = null;
        var menor = long.MaxValue;
        foreach (var s in _senaladas)
        {
            var distancia = Math.Abs(s.Frecuencia.Hercios - frecuencia.Hercios);
            if (distancia <= toleranciaHz && distancia < menor)
            {
                menor = distancia;
                mejor = s;
            }
        }
        return mejor;
    }

    /// <summary>Tramos que el plan define para una banda.</summary>
    public IReadOnlyList<SegmentoBandplan> DeLaBanda(Banda banda)
    {
        if (banda.EsVacia) return [];
        var lista = new List<SegmentoBandplan>();
        foreach (var s in _segmentos)
        {
            if (s.Banda == banda) lista.Add(s);
        }
        return lista;
    }

    /// <summary>Bandas que el plan cubre, de la mas baja a la mas alta.</summary>
    public IReadOnlyList<Banda> Bandas
    {
        get
        {
            var vistas = new List<Banda>();
            foreach (var s in _segmentos)
            {
                if (!vistas.Contains(s.Banda)) vistas.Add(s.Banda);
            }
            return vistas;
        }
    }

    /// <summary>Nombre del plan.</summary>
    public override string ToString() => Nombre;
}
