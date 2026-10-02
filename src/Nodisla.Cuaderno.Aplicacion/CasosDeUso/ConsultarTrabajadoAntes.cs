using System.Globalization;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Aplicacion.CasosDeUso;

/// <summary>Lo que se sabe de un indicativo que ya esta en el cuaderno.</summary>
public sealed record ResultadoTrabajadoAntes
{
    /// <summary>Indicativo consultado, ya normalizado.</summary>
    public Indicativo Indicativo { get; init; }

    /// <summary>Contactos anteriores, del mas reciente al mas antiguo.</summary>
    public IReadOnlyList<Qso> Contactos { get; init; } = [];

    /// <summary>Hay al menos un contacto anterior con este indicativo.</summary>
    public bool TrabajadoAntes => Contactos.Count > 0;

    /// <summary>Veces que se ha trabajado este indicativo.</summary>
    public int Veces => Contactos.Count;

    /// <summary>Instante del contacto anterior mas reciente.</summary>
    public DateTimeOffset? UltimaVezUtc => Contactos.Count > 0 ? Contactos[0].InicioUtc : null;

    /// <summary>Bandas en las que ya se ha trabajado, de la mas baja a la mas alta.</summary>
    public IReadOnlyList<string> Bandas { get; init; } = [];

    /// <summary>Modos en los que ya se ha trabajado, por orden alfabetico.</summary>
    public IReadOnlyList<string> Modos { get; init; } = [];

    /// <summary>
    /// Se alcanzo el tope de contactos que se piden al cuaderno, asi que puede haber mas.
    /// El aviso lo dice con un «o mas» en vez de mentir con una cuenta corta.
    /// </summary>
    public bool HayMas { get; init; }

    /// <summary>Indica si seria la primera vez en esa banda.</summary>
    public bool EsNuevoEnBanda(Banda banda) =>
        banda.EsVacia || !Bandas.Contains(banda.Nombre, StringComparer.OrdinalIgnoreCase);

    /// <summary>Indica si seria la primera vez en ese modo.</summary>
    public bool EsNuevoEnModo(Modo modo) =>
        modo.EsVacio || !Modos.Contains(modo.NombreUsual, StringComparer.OrdinalIgnoreCase);

    /// <summary>Aviso de una linea, listo para ponerlo junto al campo del indicativo.</summary>
    public string Resumen
    {
        get
        {
            if (!TrabajadoAntes) return Textos.T("Servicios.Aplicacion.TrabajadoAntes.Nuevo");

            var veces = HayMas
                ? Textos.F("Servicios.Aplicacion.TrabajadoAntes.VecesOMas", Veces)
                : Veces == 1
                    ? Textos.T("Servicios.Aplicacion.TrabajadoAntes.UnaVez")
                    : Textos.F("Servicios.Aplicacion.TrabajadoAntes.Veces", Veces);

            // La fecha como se escribe en el idioma en uso; la hora UTC, siempre «HH:mm».
            var ultima = UltimaVezUtc is { } u
                ? Textos.F(
                    "Servicios.Aplicacion.TrabajadoAntes.Ultima",
                    u.UtcDateTime.ToString(Textos.T("Comun.FormatoDeFecha"), Textos.Cultura) + " "
                        + u.UtcDateTime.ToString("HH:mm", CultureInfo.InvariantCulture))
                : string.Empty;
            var bandas = Bandas.Count > 0
                ? " · " + Textos.F("Servicios.Aplicacion.TrabajadoAntes.Bandas", string.Join(", ", Bandas))
                : string.Empty;
            var modos = Modos.Count > 0
                ? " · " + Textos.F("Servicios.Aplicacion.TrabajadoAntes.Modos", string.Join(", ", Modos))
                : string.Empty;
            return $"{veces}{ultima}.{bandas}{modos}";
        }
    }
}

/// <summary>
/// Avisa de si un indicativo ya esta en el cuaderno. Es lo que se consulta mientras el
/// operador teclea, asi que tiene que ser barato y no debe fallar por un indicativo a medias.
/// </summary>
public sealed class ConsultarTrabajadoAntes(IRepositorioQso repositorioQso)
{
    /// <summary>Longitud minima a partir de la cual merece la pena consultar.</summary>
    public const int LongitudMinima = 3;

    /// <summary>
    /// Tope de contactos que se le piden al cuaderno. El aviso se dispara con cada tecla y
    /// solo ensena los ultimos; traerse el historial entero de un indicativo muy trabajado
    /// seria tirar el trabajo a la basura en cada pulsacion.
    /// </summary>
    public const int MaximoDeContactos = 50;

    /// <summary>Consulta lo tecleado por el operador. Un indicativo a medias devuelve «nada».</summary>
    public async Task<ResultadoTrabajadoAntes> EjecutarAsync(string? texto, CancellationToken ct = default)
    {
        var normalizado = Indicativo.Normalizar(texto);
        if (normalizado.Length < LongitudMinima) return new ResultadoTrabajadoAntes();
        return await EjecutarAsync(Indicativo.Crudo(normalizado), ct).ConfigureAwait(false);
    }

    /// <summary>Consulta un indicativo ya normalizado.</summary>
    public async Task<ResultadoTrabajadoAntes> EjecutarAsync(Indicativo indicativo, CancellationToken ct = default)
    {
        if (indicativo.EsVacio) return new ResultadoTrabajadoAntes();

        var anteriores = await repositorioQso
            .TrabajadoAntesAsync(indicativo, MaximoDeContactos, ct)
            .ConfigureAwait(false);

        // El puerto ya los devuelve del mas reciente al mas antiguo; se reordena por si acaso,
        // porque el aviso ensena «la ultima vez» y ahi no vale un quiza.
        var ordenados = anteriores.OrderByDescending(q => q.InicioUtc).ThenByDescending(q => q.Id).ToArray();

        var bandas = ordenados
            .Where(q => !q.Band.EsVacia)
            .Select(q => q.Band.Nombre)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => IndiceDeBanda(n))
            .ToArray();

        var modos = ordenados
            .Where(q => !q.Mode.EsVacio)
            .Select(q => q.Mode.NombreUsual)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ResultadoTrabajadoAntes
        {
            Indicativo = indicativo,
            Contactos = ordenados,
            Bandas = bandas,
            Modos = modos,
            HayMas = ordenados.Length >= MaximoDeContactos,
        };
    }

    /// <summary>Posicion de la banda en la tabla ADIF, para ordenarlas de grave a aguda.</summary>
    private static int IndiceDeBanda(string nombre)
    {
        for (var i = 0; i < Banda.Todas.Count; i++)
        {
            if (string.Equals(Banda.Todas[i].Nombre, nombre, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return int.MaxValue;
    }
}
