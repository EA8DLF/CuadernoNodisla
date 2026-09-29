using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Satelites.Catalogo;
using Nodisla.Cuaderno.Satelites.Doppler;

namespace Nodisla.Cuaderno.Satelites.Cuaderno;

/// <summary>
/// Rellena en un contacto los campos que exige ADIF para un QSO por satelite.
/// </summary>
/// <remarks>
/// <para>
/// Son cinco campos y los cinco tienen que estar, porque los tres servicios de confirmacion
/// —LoTW, eQSL y ClubLog— casan los contactos por satelite con ellos y no por la banda a secas.
/// Falta uno y el contacto no confirma, aunque los dos extremos lo hayan subido.
/// </para>
/// <list type="bullet">
///   <item><c>PROP_MODE</c> = <c>SAT</c>, que es lo que declara que fue por satelite.</item>
///   <item><c>SAT_NAME</c>, la abreviatura del satelite (<c>SO-50</c>, no «Saudisat 1C»).</item>
///   <item><c>SAT_MODE</c>, el modo del transpondedor (<c>VU</c>, <c>UV</c>, <c>A</c>…).</item>
///   <item><c>FREQ</c> y <c>BAND</c>, que son los de <b>subida</b>: por donde se transmite.</item>
///   <item><c>FREQ_RX</c> y <c>BAND_RX</c>, los de <b>bajada</b>: por donde se escucha.</item>
/// </list>
/// <para>
/// <b>Se anotan las frecuencias nominales, no las del dial.</b> Durante el paso los diales van
/// corridos por el Doppler hasta diez kilohercios en 70 cm. Si se anotara lo que marcaba el
/// equipo, el corresponsal habria anotado otra cosa distinta —su Doppler no es el nuestro— y
/// las dos entradas no casarian. Lo que comparten los dos es la frecuencia del satelite.
/// </para>
/// </remarks>
public static class ContactoPorSatelite
{
    /// <summary>Valor de <c>PROP_MODE</c> para los contactos por satelite.</summary>
    public const string ModoDePropagacion = "SAT";

    /// <summary>Marca un contacto como hecho por satelite y le pone sus campos.</summary>
    /// <param name="qso">Contacto que se completa.</param>
    /// <param name="enlace">Enlace con el que se trabajo, con sus frecuencias nominales.</param>
    /// <exception cref="ArgumentNullException">Falta el contacto o el enlace.</exception>
    public static void Aplicar(Qso qso, EnlaceDeSatelite enlace)
    {
        ArgumentNullException.ThrowIfNull(qso);
        ArgumentNullException.ThrowIfNull(enlace);

        Aplicar(
            qso,
            enlace.Satelite,
            enlace.Transpondedor.Modo,
            enlace.SubidaNominal,
            enlace.BajadaNominal);
    }

    /// <summary>Marca un contacto como hecho por satelite y le pone sus campos.</summary>
    /// <param name="qso">Contacto que se completa.</param>
    /// <param name="satelite">Abreviatura del satelite, la de <c>SAT_NAME</c>.</param>
    /// <param name="modoDelSatelite">Modo del transpondedor, el de <c>SAT_MODE</c>.</param>
    /// <param name="subidaNominal">Frecuencia de subida sin corregir de Doppler.</param>
    /// <param name="bajadaNominal">Frecuencia de bajada sin corregir de Doppler.</param>
    /// <exception cref="ArgumentNullException">Falta el contacto.</exception>
    public static void Aplicar(
        Qso qso,
        string satelite,
        string? modoDelSatelite,
        Frecuencia subidaNominal,
        Frecuencia bajadaNominal)
    {
        ArgumentNullException.ThrowIfNull(qso);

        qso.PropMode = ModoDePropagacion;
        qso.SatName = Limpio(satelite);
        qso.SatMode = Limpio(modoDelSatelite);

        if (!subidaNominal.EsCero)
        {
            qso.Freq = subidaNominal;
            qso.Band = Banda.DesdeFrecuencia(subidaNominal);
        }

        if (!bajadaNominal.EsCero)
        {
            qso.FreqRx = bajadaNominal;
            qso.BandRx = Banda.DesdeFrecuencia(bajadaNominal);
        }
    }

    /// <summary>
    /// Deduce el enlace de un contacto ya anotado, para volver a sintonizarlo o repasarlo.
    /// </summary>
    /// <param name="qso">Contacto anotado.</param>
    /// <param name="catalogo">Catalogo donde buscar el satelite; si no se da, el compartido.</param>
    /// <returns>El enlace, o <c>null</c> si el contacto no es por satelite o no se reconoce.</returns>
    /// <remarks>
    /// Se busca el transpondedor cuyo margen contiene la bajada anotada. Si ninguno la contiene
    /// —porque el satelite cambio de plan o porque la anotacion es antigua— se devuelve
    /// <c>null</c> en vez de adivinar: un enlace inventado llevaria el equipo a una frecuencia
    /// que no es.
    /// </remarks>
    public static EnlaceDeSatelite? Deducir(Qso qso, CatalogoDeSatelites? catalogo = null)
    {
        ArgumentNullException.ThrowIfNull(qso);

        if (!string.Equals(qso.PropMode, ModoDePropagacion, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var satelite = (catalogo ?? CatalogoDeSatelites.Instancia).PorAbreviatura(qso.SatName);
        if (satelite is null)
        {
            return null;
        }

        var bajada = qso.FreqRx ?? Frecuencia.Cero;
        var transpondedor = satelite.Transpondedores.FirstOrDefault(t => Contiene(t, bajada, qso.SatMode));
        return transpondedor is null
            ? null
            : new EnlaceDeSatelite(satelite.Abreviatura, transpondedor, qso.Freq, bajada);
    }

    private static bool Contiene(Transpondedor t, Frecuencia bajada, string? modo)
    {
        if (!string.IsNullOrWhiteSpace(modo)
            && !string.Equals(t.Modo, modo.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (bajada.EsCero || t.BajadaInicio is not { } inicio)
        {
            return false;
        }

        var fin = t.BajadaFin ?? inicio;

        // Un margen de un solo punto —repetidor de FM— se da por bueno con holgura de un
        // kilohercio: quien anoto el contacto pudo escribir la frecuencia del dial.
        var holgura = fin.Megahercios > inicio.Megahercios ? 0m : 0.001m;
        return bajada.Megahercios >= inicio.Megahercios - holgura
               && bajada.Megahercios <= fin.Megahercios + holgura;
    }

    private static string? Limpio(string? texto) =>
        string.IsNullOrWhiteSpace(texto) ? null : texto.Trim().ToUpperInvariant();
}
