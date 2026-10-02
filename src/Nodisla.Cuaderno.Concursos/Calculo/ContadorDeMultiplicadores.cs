using System.Globalization;
using Nodisla.Cuaderno.Concursos.Catalogo;
using Nodisla.Cuaderno.Concursos.Sesion;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Concursos.Calculo;

/// <summary>
/// Lleva la cuenta de los multiplicadores conseguidos.
/// </summary>
/// <remarks>
/// El alcance de cada regla cambia por completo el resultado: en el CQ WW las zonas y los
/// paises se cuentan <b>una vez por banda</b>, y en el WPX los prefijos <b>una sola vez en
/// todo el concurso</b>. Confundirlos multiplica o divide el marcador por seis, asi que el
/// alcance viaja dentro de la clave y no en un si o un no.
/// </remarks>
public sealed class ContadorDeMultiplicadores
{
    private readonly record struct Clave(TipoDeMultiplicador Tipo, string Valor, string Donde);

    private readonly IReadOnlyList<ReglaDeMultiplicador> _reglas;
    private readonly HashSet<Clave> _vistos = [];

    /// <summary>Crea un contador para las reglas de un concurso.</summary>
    /// <param name="reglas">Reglas de multiplicador del concurso.</param>
    public ContadorDeMultiplicadores(IReadOnlyList<ReglaDeMultiplicador> reglas)
    {
        ArgumentNullException.ThrowIfNull(reglas);
        _reglas = reglas;
    }

    /// <summary>Cuantos multiplicadores distintos se llevan.</summary>
    public int Total => _vistos.Count;

    /// <summary>Todos los multiplicadores conseguidos, escritos para ensenarlos.</summary>
    public IReadOnlyList<string> Conseguidos =>
        _vistos.Select(Describir).OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>Que multiplicadores nuevos traeria este contacto, sin apuntarlos.</summary>
    /// <param name="apunte">Contacto que se esta tecleando.</param>
    /// <returns>Los nuevos, ya escritos; vacio si no trae ninguno.</returns>
    public IReadOnlyList<string> Previsualizar(ApunteDeConcurso apunte) => Calcular(apunte, apuntar: false);

    /// <summary>Apunta los multiplicadores de un contacto dado por bueno.</summary>
    /// <param name="apunte">Contacto registrado.</param>
    /// <returns>Los que ha estrenado; vacio si no ha estrenado ninguno.</returns>
    public IReadOnlyList<string> Registrar(ApunteDeConcurso apunte) => Calcular(apunte, apuntar: true);

    /// <summary>Olvida todo lo conseguido.</summary>
    public void Vaciar() => _vistos.Clear();

    private IReadOnlyList<string> Calcular(ApunteDeConcurso apunte, bool apuntar)
    {
        ArgumentNullException.ThrowIfNull(apunte);
        if (_reglas.Count == 0) return [];

        List<string>? nuevos = null;
        foreach (var regla in _reglas)
        {
            var valor = Valor(regla.Tipo, apunte);
            if (string.IsNullOrEmpty(valor)) continue;

            var clave = new Clave(regla.Tipo, valor, Donde(regla.Alcance, apunte));
            if (_vistos.Contains(clave)) continue;
            if (apuntar) _vistos.Add(clave);
            (nuevos ??= []).Add(Describir(clave));
        }
        return (IReadOnlyList<string>?)nuevos ?? [];
    }

    private static string Donde(AlcanceDeMultiplicador alcance, ApunteDeConcurso apunte) => alcance switch
    {
        AlcanceDeMultiplicador.PorBanda => apunte.Banda.Nombre,
        AlcanceDeMultiplicador.PorModo => ClaseDeModo.Abreviatura(ClaseDeModo.De(apunte.Modo)),
        AlcanceDeMultiplicador.PorBandaYModo =>
            string.Concat(apunte.Banda.Nombre, "/", ClaseDeModo.Abreviatura(ClaseDeModo.De(apunte.Modo))),
        _ => string.Empty,
    };

    private static string Valor(TipoDeMultiplicador tipo, ApunteDeConcurso apunte) => tipo switch
    {
        TipoDeMultiplicador.Dxcc => apunte.Dxcc is > 0 ? apunte.Dxcc.Value.ToString(CultureInfo.InvariantCulture) : string.Empty,
        TipoDeMultiplicador.ZonaCq => apunte.ZonaCq is > 0 ? apunte.ZonaCq.Value.ToString(CultureInfo.InvariantCulture) : string.Empty,
        TipoDeMultiplicador.ZonaItu => apunte.ZonaItu is > 0 ? apunte.ZonaItu.Value.ToString(CultureInfo.InvariantCulture) : string.Empty,
        TipoDeMultiplicador.PrefijoWpx => PrefijoWpx.De(apunte.Call),
        TipoDeMultiplicador.Locator => Recortar(apunte.Locator.Valor, 4),
        TipoDeMultiplicador.CampoLocator => Recortar(apunte.Locator.Valor, 2),
        TipoDeMultiplicador.Seccion => Limpio(apunte.Seccion),
        TipoDeMultiplicador.Provincia => Limpio(apunte.Provincia),
        TipoDeMultiplicador.Estado => Limpio(apunte.Estado),
        TipoDeMultiplicador.Sociedad => Limpio(apunte.Sociedad),
        _ => string.Empty,
    };

    private static string Limpio(string? texto) =>
        string.IsNullOrWhiteSpace(texto) ? string.Empty : texto.Trim().ToUpperInvariant();

    private static string Recortar(string? texto, int largo) =>
        string.IsNullOrWhiteSpace(texto) || texto.Length < largo
            ? string.Empty
            : texto[..largo].ToUpperInvariant();

    private static string Describir(Clave clave) =>
        clave.Donde.Length == 0
            ? $"{Nombre(clave.Tipo)} {clave.Valor}"
            : Textos.F("Servicios.Concursos.Multiplicador.En", Nombre(clave.Tipo), clave.Valor, clave.Donde);

    private static string Nombre(TipoDeMultiplicador tipo) => tipo switch
    {
        TipoDeMultiplicador.Dxcc => Textos.T("Comun.Pais"),
        TipoDeMultiplicador.ZonaCq => Textos.T("Servicios.Concursos.Multiplicador.ZonaCq"),
        TipoDeMultiplicador.ZonaItu => Textos.T("Servicios.Concursos.Multiplicador.ZonaItu"),
        TipoDeMultiplicador.PrefijoWpx => Textos.T("Servicios.Concursos.Multiplicador.Prefijo"),
        TipoDeMultiplicador.Locator => Textos.T("Libro.Localizador"),
        TipoDeMultiplicador.CampoLocator => Textos.T("Servicios.Concursos.Multiplicador.Campo"),
        TipoDeMultiplicador.Seccion => Textos.T("Servicios.Concursos.Multiplicador.Seccion"),
        TipoDeMultiplicador.Provincia => Textos.T("Servicios.Concursos.Multiplicador.Provincia"),
        TipoDeMultiplicador.Estado => Textos.T("Servicios.Concursos.Multiplicador.Estado"),
        _ => Textos.T("Servicios.Concursos.Multiplicador.Sociedad"),
    };
}
