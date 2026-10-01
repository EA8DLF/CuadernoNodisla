using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Aplicacion.CasosDeUso;

/// <summary>
/// Rellena los huecos de un perfil de estacion con lo que dice el ultimo contacto hecho desde
/// esa estacion.
/// </summary>
/// <remarks>
/// <para>
/// Existe por un caso real: el perfil «Casa» de EA8DLF quedo sin localizador tras el primer
/// arranque, mientras sus contactos importados de Log4OM si lo llevaban (IL27HX). Sin
/// localizador en el perfil, cada contacto nuevo se guardaba SIN <c>MY_GRIDSQUARE</c>, la
/// franja solar no daba orto ni ocaso y el mapa no sabia desde donde trazar.
/// </para>
/// <para>
/// Solo rellena lo que esta VACIO en el perfil y solo si el contacto es de la misma estacion
/// (mismo indicativo). Lo que el operador puso a mano no se toca nunca.
/// </para>
/// </remarks>
public static class CompletarPerfilDeEstacion
{
    /// <summary>Rellena los campos vacios del perfil con los del contacto.</summary>
    /// <param name="perfil">Perfil que se completa.</param>
    /// <param name="contacto">Contacto hecho desde esa estacion.</param>
    /// <returns>Los nombres ADIF de los campos que se han rellenado; vacia si ninguno.</returns>
    public static IReadOnlyList<string> DesdeContacto(Estacion perfil, Qso contacto)
    {
        ArgumentNullException.ThrowIfNull(perfil);
        ArgumentNullException.ThrowIfNull(contacto);

        var rellenados = new List<string>();
        if (contacto.StationCallsign != perfil.StationCallsign) return rellenados;

        if (perfil.MyGridsquare.EsVacio && !contacto.MyGridsquare.EsVacio)
        {
            perfil.MyGridsquare = contacto.MyGridsquare;
            rellenados.Add("MY_GRIDSQUARE");
        }

        Texto("MY_CITY", perfil.MyCity, contacto.MyCity, v => perfil.MyCity = v);
        Texto("MY_STATE", perfil.MyState, contacto.MyState, v => perfil.MyState = v);
        Texto("MY_COUNTRY", perfil.MyCountry, contacto.MyCountry, v => perfil.MyCountry = v);
        Texto("MY_RIG", perfil.MyRig, contacto.MyRig, v => perfil.MyRig = v);
        Texto("MY_ANTENNA", perfil.MyAntenna, contacto.MyAntenna, v => perfil.MyAntenna = v);

        if (perfil.MyDxcc is null && contacto.MyDxcc is { } dxcc)
        {
            perfil.MyDxcc = dxcc;
            rellenados.Add("MY_DXCC");
        }

        if (perfil.MyCqZone is null && contacto.MyCqZone is { } cq)
        {
            perfil.MyCqZone = cq;
            rellenados.Add("MY_CQ_ZONE");
        }

        if (perfil.MyItuZone is null && contacto.MyItuZone is { } itu)
        {
            perfil.MyItuZone = itu;
            rellenados.Add("MY_ITU_ZONE");
        }

        return rellenados;

        void Texto(string campo, string? actual, string? propuesto, Action<string> poner)
        {
            if (!string.IsNullOrWhiteSpace(actual) || string.IsNullOrWhiteSpace(propuesto)) return;
            poner(propuesto.Trim());
            rellenados.Add(campo);
        }
    }
}
