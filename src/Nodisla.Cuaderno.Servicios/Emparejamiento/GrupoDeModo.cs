using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Servicios.Emparejamiento;

/// <summary>Grupos de modo con los que comparan los servicios de confirmacion.</summary>
public enum GrupoDeModo
{
    /// <summary>No se sabe encuadrar.</summary>
    Desconocido,

    /// <summary>Telegrafia.</summary>
    Telegrafia,

    /// <summary>Voz, analogica o digital.</summary>
    Fonia,

    /// <summary>Modos de datos.</summary>
    Datos,

    /// <summary>Imagen: televisiones y facsimil.</summary>
    Imagen,
}

/// <summary>
/// Compara modos entre el cuaderno y un servicio.
/// </summary>
/// <remarks>
/// Aqui esta la mitad del problema de emparejar: el cuaderno guarda el par ADIF correcto
/// (<c>MFSK</c> con submodo <c>FT4</c>) y el servicio puede decir <c>MFSK</c>, <c>FT4</c> o
/// <c>DATA</c>, segun quien subiera el contacto y con que programa. Se compara en tres pasos,
/// de lo estricto a lo laxo: nombre usual, modo principal y, por ultimo, grupo.
/// </remarks>
public static class ComparadorDeModos
{
    private static readonly string[] ModosDeFonia =
        ["SSB", "AM", "FM", "DIGITALVOICE", "VOI", "V4"];

    private static readonly string[] ModosDeImagen = ["SSTV", "ATV", "FAX"];

    /// <summary>Encuadra un modo ADIF en su grupo.</summary>
    /// <param name="modo">Modo del cuaderno.</param>
    public static GrupoDeModo GrupoDe(Modo modo)
    {
        if (modo.EsVacio) return GrupoDeModo.Desconocido;
        var principal = modo.Principal;
        if (principal is "CW") return GrupoDeModo.Telegrafia;
        if (Array.IndexOf(ModosDeFonia, principal) >= 0) return GrupoDeModo.Fonia;
        if (Array.IndexOf(ModosDeImagen, principal) >= 0) return GrupoDeModo.Imagen;
        return GrupoDeModo.Datos;
    }

    /// <summary>
    /// Interpreta el texto de modo que devuelve un servicio. Resuelve los submodos a su modo
    /// principal y acepta los nombres genericos que usan algunos servicios.
    /// </summary>
    /// <param name="texto">Modo tal y como lo escribe el servicio.</param>
    public static Modo Interpretar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return Modo.Vacio;
        var t = texto.Trim().ToUpperInvariant();

        // Nombres genericos que no estan en la tabla de ADIF pero que usan los servicios.
        if (t is "DATA" or "DIGI" or "DIGITAL") return Modo.Crudo("MFSK");
        if (t is "PHONE" or "VOICE") return Modo.Crudo("SSB");

        return Modo.TryParse(t, null, out var modo) ? modo : Modo.Crudo(t);
    }

    /// <summary>Indica si el modo del servicio se corresponde con el del cuaderno.</summary>
    /// <param name="delCuaderno">Modo registrado en el cuaderno.</param>
    /// <param name="delServicio">Modo tal y como lo escribe el servicio.</param>
    /// <param name="admitirMismoGrupo">Dar por bueno que solo coincida el grupo.</param>
    public static bool Coinciden(Modo delCuaderno, string? delServicio, bool admitirMismoGrupo)
    {
        if (delCuaderno.EsVacio || string.IsNullOrWhiteSpace(delServicio)) return true;

        var otro = Interpretar(delServicio);
        if (otro.EsVacio) return true;

        // Exacto: mismo nombre usual (FT4 con FT4) o mismo par.
        if (string.Equals(delCuaderno.NombreUsual, otro.NombreUsual, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // El servicio dio el modo principal y el cuaderno el submodo, o al reves.
        if (string.Equals(delCuaderno.Principal, otro.Principal, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return admitirMismoGrupo
            && GrupoDe(delCuaderno) != GrupoDeModo.Desconocido
            && GrupoDe(delCuaderno) == GrupoDe(otro);
    }
}
