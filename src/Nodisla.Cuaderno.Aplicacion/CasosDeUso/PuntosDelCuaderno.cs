using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Dxcc;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Aplicacion.CasosDeUso;

/// <summary>De donde ha salido la posicion que se pinta en el mapa.</summary>
public enum OrigenDeLaPosicion
{
    /// <summary>Del localizador que dio el corresponsal: precision de unos kilometros.</summary>
    Localizador,

    /// <summary>Del centro de la entidad DXCC: vale para el mapa, no para medir distancias.</summary>
    Entidad,
}

/// <summary>Un contacto ya listo para pintarlo en el mapa.</summary>
/// <param name="Indicativo">Indicativo del corresponsal.</param>
/// <param name="Donde">Donde se pinta.</param>
/// <param name="Origen">Si la posicion viene del localizador o del centro del pais.</param>
/// <param name="Banda">Banda del contacto.</param>
/// <param name="Modo">Modo del contacto.</param>
/// <param name="CuandoUtc">Momento del contacto.</param>
/// <param name="Pais">Entidad DXCC, si se conoce.</param>
public sealed record ContactoEnElMapa(
    Indicativo Indicativo,
    Coordenada Donde,
    OrigenDeLaPosicion Origen,
    string Banda,
    string Modo,
    DateTimeOffset CuandoUtc,
    string? Pais);

/// <summary>
/// Saca del cuaderno los contactos que se pueden situar en el mapa.
/// </summary>
/// <remarks>
/// Se lee por paginas y no de un tiron: un cuaderno de treinta anos no cabe comodo en memoria
/// de golpe, y leer por trozos deja que la interfaz siga respondiendo mientras carga.
///
/// Un contacto sin localizador no se tira: se situa en el centro de su entidad DXCC, que es lo
/// que hacen todos los cuadernos. Se marca de donde sale la posicion para que el mapa pueda
/// pintarlos distinto y el operador no confunda un punto exacto con uno aproximado.
/// </remarks>
public sealed class PuntosDelCuaderno(IRepositorioQso repositorioQso, IResolutorDxcc resolutorDxcc)
{
    /// <summary>Contactos que se piden en cada vuelta.</summary>
    public const int ContactosPorVuelta = 2000;

    /// <summary>
    /// Devuelve los contactos del cuaderno situados en el mapa, del mas reciente al mas antiguo.
    /// </summary>
    /// <param name="maximo">Tope de contactos que se leen.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>Los contactos que se han podido situar.</returns>
    public async Task<IReadOnlyList<ContactoEnElMapa>> EjecutarAsync(
        int maximo = 20_000,
        CancellationToken ct = default)
    {
        var tope = Math.Max(1, maximo);
        var salida = new List<ContactoEnElMapa>(Math.Min(tope, ContactosPorVuelta));
        var criterio = new CriterioQso { OrdenarPor = CampoDeOrden.Fecha, Descendente = true };

        var desplazamiento = 0;
        while (salida.Count < tope)
        {
            ct.ThrowIfCancellationRequested();

            var cuantos = Math.Min(ContactosPorVuelta, tope - salida.Count);
            var pagina = await repositorioQso
                .BuscarAsync(criterio, desplazamiento, cuantos, ct)
                .ConfigureAwait(false);

            if (pagina.Elementos.Count == 0) break;

            foreach (var qso in pagina.Elementos)
            {
                if (Situar(qso) is { } punto) salida.Add(punto);
            }

            desplazamiento += pagina.Elementos.Count;
            if (desplazamiento >= pagina.TotalFiltrado) break;
        }

        return salida;
    }

    /// <summary>
    /// Pone un contacto sobre el mapa, o devuelve nulo si no hay manera de situarlo.
    /// </summary>
    /// <param name="qso">Contacto del cuaderno.</param>
    /// <returns>El contacto situado, o nulo si no se sabe donde esta.</returns>
    public ContactoEnElMapa? Situar(Qso qso)
    {
        ArgumentNullException.ThrowIfNull(qso);

        if (!qso.Gridsquare.EsVacio)
        {
            return new ContactoEnElMapa(
                qso.Call,
                Coordenada.Desde(qso.Gridsquare),
                OrigenDeLaPosicion.Localizador,
                qso.Band.Nombre,
                qso.Mode.NombreUsual,
                qso.InicioUtc,
                qso.Country);
        }

        var entidad = qso.Dxcc > 0
            ? resolutorDxcc.PorNumero(qso.Dxcc)
            : resolutorDxcc.Resolver(qso.Call, DateOnly.FromDateTime(qso.InicioUtc.UtcDateTime)).Entidad;

        if (entidad is null) return null;

        return new ContactoEnElMapa(
            qso.Call,
            entidad.Coordenada,
            OrigenDeLaPosicion.Entidad,
            qso.Band.Nombre,
            qso.Mode.NombreUsual,
            qso.InicioUtc,
            qso.Country ?? entidad.NombreParaMostrar);
    }
}
