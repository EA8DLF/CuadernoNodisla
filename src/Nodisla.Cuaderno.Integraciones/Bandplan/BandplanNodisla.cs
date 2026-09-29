using System.Globalization;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Integraciones.Bandplan;

/// <summary>
/// El bandplan de Cuaderno NODISLA: responde con el tramo en el que cae una frecuencia, que
/// se hace en el y si el modo que se quiere usar encaja.
/// </summary>
/// <remarks>
/// <para>
/// Los datos salen del recurso incrustado (ver <c>recursos/LEEME-bandplan.md</c>) y la
/// busqueda es por biseccion, porque esto se consulta con cada giro del mando del VFO.
/// </para>
/// <para>
/// <b>Avisa, no impide.</b> Todo lo que sale de aqui son mensajes para el operador. Hay
/// motivos legitimos para estar fuera del plan —escuchar, una autorizacion especial, un
/// equipo abierto—, y el cuaderno tiene que poder registrar el contacto igual: el dial de
/// esta estacion estaba en 27,555 MHz cuando se interrogo al equipo, y eso tambien pasa.
/// </para>
/// </remarks>
public sealed class BandplanNodisla : IBandplan
{
    private readonly string? _clase;

    /// <summary>Crea el bandplan sobre un plan concreto.</summary>
    /// <param name="plan">Plan de bandas que se aplica a la estacion.</param>
    /// <param name="claseDeLicencia">
    /// Clase de licencia del operador, cuando el plan distingue clases. Nulo significa que el
    /// operador tiene acceso a todo lo que el plan permite.
    /// </param>
    public BandplanNodisla(PlanDeBanda plan, string? claseDeLicencia = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        Plan = plan;
        _clase = string.IsNullOrWhiteSpace(claseDeLicencia) ? null : claseDeLicencia.Trim();
    }

    /// <summary>
    /// Bandplan de una estacion, por su region IARU y su entidad DXCC.
    /// </summary>
    /// <param name="region">Region IARU donde esta la estacion. EA8 es la Region 1.</param>
    /// <param name="dxcc">
    /// Entidad DXCC de la estacion. Si hay plan nacional publicado para ella, manda sobre el
    /// de la region. Cero para usar solo el de la region.
    /// </param>
    /// <param name="claseDeLicencia">Clase de licencia del operador, si el plan las distingue.</param>
    public static BandplanNodisla Para(
        RegionIaru region = RegionIaru.Region1,
        int dxcc = 0,
        string? claseDeLicencia = null)
    {
        var plan = CatalogoBandplanes.Predeterminado.Para(dxcc, (int)region)
            ?? throw new InvalidOperationException($"No hay plan de bandas para la región {(int)region}.");
        return new BandplanNodisla(plan, claseDeLicencia);
    }

    /// <summary>Plan con el que se responde.</summary>
    public PlanDeBanda Plan { get; }

    /// <summary>Clase de licencia del operador, si se declaro.</summary>
    public string? ClaseDeLicencia => _clase;

    /// <inheritdoc/>
    public RegionIaru Region => Plan.Region switch
    {
        2 => RegionIaru.Region2,
        3 => RegionIaru.Region3,
        _ => RegionIaru.Region1,
    };

    /// <inheritdoc/>
    public IReadOnlyList<TramoDeBanda> TramosDe(Banda banda)
    {
        var segmentos = Plan.DeLaBanda(banda);
        var tramos = new List<TramoDeBanda>(segmentos.Count);
        foreach (var s in segmentos) tramos.Add(ATramo(s));
        return tramos;
    }

    /// <inheritdoc/>
    public ConsultaDeBandplan Consultar(Frecuencia frecuencia, Modo modo = default)
    {
        var segmento = Plan.Buscar(frecuencia);
        var tramo = segmento is null ? null : ATramo(segmento);

        if (segmento is null)
        {
            return new ConsultaDeBandplan(
                frecuencia,
                null,
                DentroDeBanda: false,
                ModoEncaja: null,
                Aviso: $"{Texto(frecuencia)} queda fuera del plan de bandas de {Plan.Nombre}.");
        }

        var encaja = modo.EsVacio ? (bool?)null : Encaja(modo, segmento.Uso);
        var aviso = Redactar(frecuencia, modo, segmento, encaja);

        return new ConsultaDeBandplan(frecuencia, tramo, DentroDeBanda: true, encaja, aviso);
    }

    /// <inheritdoc/>
    public IReadOnlyList<(Frecuencia Frecuencia, string Descripcion)> FrecuenciasSenaladas(Banda banda)
    {
        var lista = new List<(Frecuencia, string)>();
        foreach (var s in Plan.Senaladas)
        {
            if (banda.EsVacia || Banda.DesdeFrecuencia(s.Frecuencia) == banda)
            {
                lista.Add((s.Frecuencia, s.Descripcion));
            }
        }
        return lista;
    }

    /// <summary>
    /// Modo que el plan sugiere para una frecuencia, cuando lo dice.
    /// </summary>
    /// <remarks>
    /// Primero mira las frecuencias con nombre propio (las de FT8 y FT4, por ejemplo) y
    /// despues el uso del tramo.
    /// </remarks>
    public string? ModoSugerido(Frecuencia frecuencia)
    {
        var senalada = Plan.Senalada(frecuencia);
        if (senalada is not null) return senalada.Modo;

        var segmento = Plan.Buscar(frecuencia);
        return segmento?.Uso switch
        {
            UsoDelTramo.Cw => "CW",
            UsoDelTramo.DigitalEstrecho or UsoDelTramo.DigitalAncho => "DIGITAL",
            UsoDelTramo.Fonia or UsoDelTramo.FoniaEImagen => segmento.Modulacion ?? "SSB",
            _ => null,
        };
    }

    /// <summary>
    /// Indica si el operador puede transmitir en la frecuencia con su clase de licencia.
    /// </summary>
    /// <remarks>
    /// Los planes de Log4OM no traen clases de licencia, asi que hoy esto solo dice que no
    /// cuando se carga un plan que si las declara. Sigue siendo un aviso, no un cerrojo.
    /// </remarks>
    public bool TieneAccesoALaFrecuencia(Frecuencia frecuencia) =>
        Plan.Buscar(frecuencia)?.AdmiteClase(_clase) ?? false;

    /// <summary>Convierte un segmento del recurso en el tramo del contrato.</summary>
    private static TramoDeBanda ATramo(SegmentoBandplan s) => new(
        s.Banda,
        s.Desde,
        s.Hasta,
        s.Uso,
        Descripcion(s),
        AnchoMaximoHz: null);

    /// <summary>Texto del tramo para mostrarlo, en castellano.</summary>
    private static string Descripcion(SegmentoBandplan s) =>
        s.Modulacion is null or "" ? $"{Nombre(s.Uso)} en {s.Banda}" : $"{Nombre(s.Uso)} en {s.Banda} ({s.Modulacion})";

    /// <summary>Redacta el aviso para el operador. Nulo si no hay nada que decir.</summary>
    private string? Redactar(Frecuencia frecuencia, Modo modo, SegmentoBandplan segmento, bool? encaja)
    {
        if (!segmento.AdmiteClase(_clase))
        {
            return $"{Texto(frecuencia)} está en el tramo de {Nombre(segmento.Uso)} de " +
                $"{segmento.Banda}, y tu licencia ({_clase}) no tiene acceso a ese tramo.";
        }

        if (encaja == false)
        {
            return $"{Texto(frecuencia)} es el tramo de {Nombre(segmento.Uso)} de {segmento.Banda}; " +
                $"vas a transmitir en {modo.NombreUsual}.";
        }

        return null;
    }

    /// <summary>Indica si un modo ADIF encaja con el uso que el plan da al tramo.</summary>
    public static bool Encaja(Modo modo, UsoDelTramo uso)
    {
        if (modo.EsVacio) return true;
        return uso switch
        {
            UsoDelTramo.Todos or UsoDelTramo.Reservado => true,
            UsoDelTramo.Cw => modo.Principal.Equals("CW", StringComparison.OrdinalIgnoreCase),
            UsoDelTramo.Fonia or UsoDelTramo.FoniaEImagen => EsDeVoz(modo),
            UsoDelTramo.DigitalEstrecho or UsoDelTramo.DigitalAncho => EsDeDatos(modo),
            UsoDelTramo.Baliza => true,
            _ => true,
        };
    }

    private static bool EsDeVoz(Modo modo) => modo.Principal.ToUpperInvariant() is
        "SSB" or "AM" or "FM" or "DIGITALVOICE" or "VOI" or "SSTV" or "FAX" or "ATV";

    private static bool EsDeDatos(Modo modo) => !EsDeVoz(modo) &&
        !modo.Principal.Equals("CW", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Escribe la frecuencia como la lee el operador: en kilohercios y con el punto de los
    /// miles, que es como vienen en los anuncios y en los planes de banda.
    /// </summary>
    private static string Texto(Frecuencia frecuencia) =>
        frecuencia.Kilohercios.ToString("#,##0.###", CultureInfo.GetCultureInfo("es-ES")) + " kHz";

    /// <summary>Nombre en castellano de un uso, para los mensajes.</summary>
    private static string Nombre(UsoDelTramo uso) => uso switch
    {
        UsoDelTramo.Cw => "CW",
        UsoDelTramo.DigitalEstrecho => "modos digitales",
        UsoDelTramo.DigitalAncho => "modos digitales de banda ancha",
        UsoDelTramo.Fonia => "fonía",
        UsoDelTramo.FoniaEImagen => "fonía e imagen",
        UsoDelTramo.Baliza => "balizas",
        UsoDelTramo.Reservado => "uso reservado",
        _ => "todos los modos",
    };
}
