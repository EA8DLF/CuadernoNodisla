using System.Globalization;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Radio.Ptt;

/// <summary>
/// Las salvaguardas de transmision que comprueba el vigilante del PTT: plan de banda, ROE y
/// potencia maxima por banda.
/// </summary>
/// <remarks>
/// Es inmutable: para cambiarla se le da otra entera al vigilante
/// (<see cref="VigilantePtt.CambiarSeguridad"/>). Asi el hilo vigilante nunca ve media
/// configuracion.
/// </remarks>
public sealed record OpcionesDeSeguridadDeTx
{
    /// <summary>No se sube el PTT si el canal que ocupa la emision se sale del plan de banda.</summary>
    public bool BloquearFueraDeBanda { get; init; } = true;

    /// <summary>
    /// Bandas en las que se deja transmitir fuera del plan (licencias especiales). Solo se
    /// llenan con una confirmacion explicita del operador. Clave: <see cref="ClaveDeBanda"/>.
    /// </summary>
    public IReadOnlySet<string> BandasLiberadas { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Se lee la ROE durante la transmision y se corta si sube.</summary>
    public bool VigilarRoe { get; init; } = true;

    /// <summary>ROE por encima de la cual se corta (tras <see cref="LecturasSeguidas"/>).</summary>
    public double RoeMaxima { get; init; } = 2.5;

    /// <summary>Lecturas seguidas por encima de <see cref="RoeMaxima"/> para cortar.</summary>
    public int LecturasSeguidas { get; init; } = 3;

    /// <summary>ROE que se toma por antena abierta o en corto: corte en la primera lectura.</summary>
    public double RoeDeAntenaAbierta { get; init; } = 3.0;

    /// <summary>Cada cuanto se lee la ROE durante la transmision.</summary>
    public TimeSpan PasoDeRoe { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Potencia maxima por banda, en vatios (clave: <see cref="ClaveDeBanda"/>). Se aplica antes
    /// de subir el PTT; sin entrada, sin limite.
    /// </summary>
    public IReadOnlyDictionary<string, int> PotenciaMaximaPorBanda { get; init; } = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>Pausa minima tras un corte antes de poder rearmar, aunque esten todas las fuentes sueltas.</summary>
    public TimeSpan PausaTrasUnCorte { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Como se llama la banda de una frecuencia para las listas de arriba: la de ADIF («40m») o,
    /// fuera de las de aficionado, los megahercios enteros («27 MHz»).
    /// </summary>
    /// <param name="frecuencia">La frecuencia.</param>
    /// <returns>La clave.</returns>
    public static string ClaveDeBanda(Frecuencia frecuencia)
    {
        var banda = Banda.DesdeFrecuencia(frecuencia);
        return banda.EsVacia
            ? string.Create(CultureInfo.InvariantCulture, $"{Math.Floor(frecuencia.Megahercios)} MHz")
            : banda.Nombre;
    }
}

/// <summary>Lo que sale de leer la ROE durante una transmision.</summary>
/// <param name="Roe">La ROE, o nula si no se pudo leer.</param>
/// <param name="HayPotencia">Hay potencia de salida: sin ella (CW con la llave arriba) la ROE no significa nada.</param>
/// <param name="AlarmaDelEquipo">El propio equipo avisa de ROE alta (en el FT-710, «Hi-SWR» de <c>RI0;</c>).</param>
public sealed record LecturaDeRoe(double? Roe, bool HayPotencia, bool AlarmaDelEquipo);

/// <summary>Un equipo cuya ROE se puede leer mientras transmite.</summary>
public interface IMedidorDeRoe
{
    /// <summary>Lee la ROE, si hay potencia y si el equipo da la alarma de ROE.</summary>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>La lectura, o nula si el equipo no la da.</returns>
    Task<LecturaDeRoe?> LeerRoeAsync(CancellationToken ct = default);
}

/// <summary>Se ha pedido antena y una salvaguarda no la deja subir.</summary>
public sealed class TransmisionBloqueadaException : InvalidOperationException
{
    /// <summary>Crea la excepcion.</summary>
    /// <param name="porque">El motivo, para enseñarlo tal cual.</param>
    public TransmisionBloqueadaException(string porque)
        : base(porque)
    {
    }
}

/// <summary>
/// El canal que ocupa una emision y si cabe en el plan de banda.
/// </summary>
/// <remarks>
/// Se mira el canal entero y no solo la portadora, como hace Thetis: en CW la portadora mas o
/// menos medio ancho del filtro; en banda lateral superior (y los modos de datos) de la
/// portadora hacia arriba; en inferior, hacia abajo; en AM y FM, a los dos lados. Los dos
/// bordes tienen que caer dentro del plan y en la misma banda.
/// </remarks>
public static class PlanDeBandaDeTransmision
{
    /// <summary>Ancho que se supone cuando el equipo no dice el de su filtro.</summary>
    /// <param name="modo">El modo.</param>
    /// <returns>Hercios.</returns>
    public static int AnchoPorOmision(Modo modo)
    {
        var m = Nombre(modo);
        if (m.StartsWith("CW", StringComparison.Ordinal)) return 500;
        if (m.StartsWith("AM", StringComparison.Ordinal)) return 6000;
        if (m.StartsWith("FM", StringComparison.Ordinal)) return 16000;
        return 3000;
    }

    /// <summary>Los bordes del canal que ocupa la emision.</summary>
    /// <param name="portadora">Frecuencia del equipo (la del VFO que transmite).</param>
    /// <param name="modo">El modo.</param>
    /// <param name="anchoHz">El ancho del filtro de transmision, si se sabe.</param>
    /// <returns>Borde inferior y superior.</returns>
    public static (Frecuencia Inferior, Frecuencia Superior) Bordes(Frecuencia portadora, Modo modo, int? anchoHz)
    {
        var m = Nombre(modo);
        var omision = AnchoPorOmision(modo);

        // Un ancho raro (un indice en vez de hercios, o mas que el del modo) no puede estrechar
        // el canal: se toma el mayor de los dos, que es lo prudente.
        // En CW se usa el del filtro si lo hay (puede ser mas estrecho que 500 Hz).
        var esCw = m.StartsWith("CW", StringComparison.Ordinal);
        var ancho = anchoHz is > 0 and < 50_000 ? (esCw ? anchoHz.Value : Math.Max(anchoHz.Value, omision)) : omision;
        var f = portadora.Hercios;

        if (m.StartsWith("CW", StringComparison.Ordinal) || m.StartsWith("AM", StringComparison.Ordinal) || m.StartsWith("FM", StringComparison.Ordinal))
        {
            return (Frecuencia.DesdeHercios(f - (ancho / 2)), Frecuencia.DesdeHercios(f + (ancho / 2)));
        }

        var inferior = m is "LSB" || m.EndsWith("-L", StringComparison.Ordinal) || (m is "SSB" && portadora.Megahercios < 10m);
        return inferior
            ? (Frecuencia.DesdeHercios(f - ancho), portadora)
            : (portadora, Frecuencia.DesdeHercios(f + ancho));
    }

    /// <summary>
    /// Por que no se puede transmitir en esa frecuencia, o nulo si se puede.
    /// </summary>
    /// <param name="portadora">Frecuencia del VFO que transmite.</param>
    /// <param name="modo">Modo de transmision.</param>
    /// <param name="anchoHz">Ancho del filtro, si se sabe.</param>
    /// <param name="bandplan">El plan de banda del programa; nulo, los limites de banda de ADIF.</param>
    /// <param name="seguridad">Las salvaguardas.</param>
    /// <returns>El motivo, o nulo.</returns>
    public static string? MotivoParaNoTransmitir(Frecuencia portadora, Modo modo, int? anchoHz, IBandplan? bandplan, OpcionesDeSeguridadDeTx seguridad)
    {
        ArgumentNullException.ThrowIfNull(seguridad);
        if (!seguridad.BloquearFueraDeBanda || portadora.EsCero) return null;
        if (seguridad.BandasLiberadas.Contains(OpcionesDeSeguridadDeTx.ClaveDeBanda(portadora))) return null;

        var (inferior, superior) = Bordes(portadora, modo, anchoHz);
        bool dentro;
        if (bandplan is not null)
        {
            var abajo = bandplan.Consultar(inferior);
            var arriba = bandplan.Consultar(superior);
            dentro = abajo.DentroDeBanda && arriba.DentroDeBanda
                && abajo.Tramo is { } t1 && arriba.Tramo is { } t2 && t1.Banda == t2.Banda;
        }
        else
        {
            var b1 = Banda.DesdeFrecuencia(inferior);
            dentro = !b1.EsVacia && b1 == Banda.DesdeFrecuencia(superior);
        }

        return dentro
            ? null
            : Textos.F(
                "Servicios.Radio.Seguridad.FueraDelPlan",
                Mhz(portadora),
                Mhz(inferior),
                Mhz(superior),
                Nombre(modo));
    }

    private static string Mhz(Frecuencia f) => f.Megahercios.ToString("0.000###", CultureInfo.InvariantCulture);

    private static string Nombre(Modo modo) => (modo.NombreUsual ?? string.Empty).ToUpperInvariant();
}
