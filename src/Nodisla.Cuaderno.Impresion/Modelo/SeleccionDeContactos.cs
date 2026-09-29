using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Impresion.Modelo;

/// <summary>Que contactos entran en la tirada de etiquetas.</summary>
public sealed class SeleccionDeContactos
{
    /// <summary>Solo los contactos desde esta fecha, en UTC.</summary>
    public DateTimeOffset? DesdeUtc { get; set; }

    /// <summary>Solo los contactos hasta esta fecha, en UTC.</summary>
    public DateTimeOffset? HastaUtc { get; set; }

    /// <summary>
    /// Solo los que tengan la QSL en papel marcada como pendiente de enviar.
    /// </summary>
    /// <remarks>
    /// Es lo normal y por eso viene puesto: la tirada de etiquetas sale de la cola de tarjetas
    /// por mandar. Quitarlo sirve para reimprimir una etiqueta perdida.
    /// </remarks>
    public bool SoloPendientesDeEnviar { get; set; } = true;

    /// <summary>
    /// Dejar fuera los contactos que ya estan confirmados en papel por el corresponsal.
    /// </summary>
    /// <remarks>
    /// Apagado por omision. Confirmar tambien lo que ya llego es correcto y es lo que se espera:
    /// si el otro mando su tarjeta, se le devuelve la nuestra.
    /// </remarks>
    public bool ExcluirYaConfirmados { get; set; }

    /// <summary>Si se dan bandas, solo cuentan los contactos en esas bandas.</summary>
    public IReadOnlyCollection<string> Bandas { get; init; } = [];

    /// <summary>Si se dan vias, solo cuentan las tarjetas que van por esas vias.</summary>
    public IReadOnlyCollection<ViaDeEnvio> Vias { get; init; } = [];

    /// <summary>Si se dan indicativos, solo cuentan los contactos con esas estaciones.</summary>
    public IReadOnlyCollection<string> Indicativos { get; init; } = [];

    /// <summary>
    /// Cuantos contactos como mucho caben en una etiqueta.
    /// </summary>
    /// <remarks>
    /// Cuatro. Es lo que entra en una etiqueta de 38 mm de alto con el encabezado y el pie sin
    /// que el texto baje de seis puntos, que es donde deja de leerse a simple vista. Con mas
    /// contactos con el mismo corresponsal se reparten en varias etiquetas, que es preferible a
    /// una etiqueta ilegible.
    /// </remarks>
    public int ContactosPorEtiqueta { get; set; } = 4;

    /// <summary>Decide si un contacto entra en la tirada.</summary>
    /// <param name="qso">Contacto del cuaderno.</param>
    /// <returns><c>true</c> si hay que imprimirlo.</returns>
    /// <exception cref="ArgumentNullException">No se ha dado contacto.</exception>
    public bool Entra(Qso qso)
    {
        ArgumentNullException.ThrowIfNull(qso);

        if (qso.Call.EsVacio)
        {
            return false;
        }

        if (DesdeUtc is { } desde && qso.InicioUtc < desde)
        {
            return false;
        }

        if (HastaUtc is { } hasta && qso.InicioUtc > hasta)
        {
            return false;
        }

        if (Bandas.Count > 0
            && !Bandas.Contains(qso.Band.Nombre, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Indicativos.Count > 0
            && !Indicativos.Contains(qso.Call.Valor, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var papel = qso.Confirmaciones.FirstOrDefault(c => c.Medio == MedioDeConfirmacion.Papel);

        if (Vias.Count > 0 && !Vias.Contains(papel?.Via ?? ViaDeEnvio.Ninguna))
        {
            return false;
        }

        if (SoloPendientesDeEnviar
            && (papel is null || papel.Enviado != EstadoDeConfirmacion.Pendiente))
        {
            return false;
        }

        if (ExcluirYaConfirmados && papel is { EstaConfirmada: true })
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Agrupa los contactos elegidos en etiquetas, una por corresponsal.
    /// </summary>
    /// <param name="contactos">Contactos del cuaderno, en cualquier orden.</param>
    /// <param name="miIndicativo">Indicativo propio, por si el contacto no lo trae.</param>
    /// <param name="miLocalizador">Localizador propio, por si el contacto no lo trae.</param>
    /// <param name="mensaje">Frase al pie de todas las etiquetas.</param>
    /// <returns>Las etiquetas, ordenadas por indicativo del corresponsal.</returns>
    /// <remarks>
    /// <para>
    /// Se agrupa por corresponsal <b>y por gestor</b>: si con el mismo indicativo hay contactos
    /// que van por buro y otros por un gestor distinto, son dos envios distintos y por tanto dos
    /// etiquetas, aunque el indicativo sea el mismo.
    /// </para>
    /// <para>
    /// El orden final es alfabetico por indicativo. Parece un detalle y no lo es: las tarjetas
    /// del buro se entregan ordenadas por prefijo, y una hoja de etiquetas ordenada se pega del
    /// tiron sin ir buscando.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">No se ha dado la lista de contactos.</exception>
    public IReadOnlyList<EtiquetaDeQsl> Agrupar(
        IEnumerable<Qso> contactos,
        Indicativo miIndicativo,
        Locator miLocalizador = default,
        string? mensaje = null)
    {
        ArgumentNullException.ThrowIfNull(contactos);

        var porEtiqueta = Math.Max(1, ContactosPorEtiqueta);
        var etiquetas = new List<EtiquetaDeQsl>();

        var grupos = contactos
            .Where(Entra)
            .GroupBy(q => (
                Corresponsal: q.Call.Valor,
                Gestor: string.IsNullOrWhiteSpace(q.QslVia) ? string.Empty : q.QslVia.Trim().ToUpperInvariant()))
            .OrderBy(g => g.Key.Corresponsal, StringComparer.Ordinal)
            .ThenBy(g => g.Key.Gestor, StringComparer.Ordinal);

        foreach (var grupo in grupos)
        {
            var enOrden = grupo.OrderBy(q => q.InicioUtc).ToList();
            var cabeza = enOrden[0];
            var via = cabeza.Confirmaciones
                .FirstOrDefault(c => c.Medio == MedioDeConfirmacion.Papel)?.Via ?? ViaDeEnvio.Ninguna;

            for (var i = 0; i < enOrden.Count; i += porEtiqueta)
            {
                var trozo = enOrden.Skip(i).Take(porEtiqueta).Select(LineaDeContacto.Desde).ToList();

                etiquetas.Add(new EtiquetaDeQsl(
                    cabeza.Call,
                    cabeza.StationCallsign.EsVacio ? miIndicativo : cabeza.StationCallsign,
                    trozo,
                    grupo.Key.Gestor.Length == 0 ? null : grupo.Key.Gestor,
                    via,
                    cabeza.MyGridsquare.EsVacio ? miLocalizador : cabeza.MyGridsquare,
                    mensaje));
            }
        }

        return etiquetas;
    }
}
