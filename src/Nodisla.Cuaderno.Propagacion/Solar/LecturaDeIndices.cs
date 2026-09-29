using System.Globalization;
using Nodisla.Cuaderno.Aplicacion.Puertos;

namespace Nodisla.Cuaderno.Propagacion.Solar;

/// <summary>De donde han salido los indices que se estan ensenando.</summary>
public enum OrigenDeLosIndices
{
    /// <summary>Recien traidos de la red.</summary>
    Red = 0,

    /// <summary>Leidos del fichero de la ultima vez que hubo red.</summary>
    Cache,
}

/// <summary>
/// Unos indices solares con su procedencia, para poder decir siempre de cuando son.
/// </summary>
/// <param name="Indices">Los valores.</param>
/// <param name="ObtenidoUtc">Cuando se trajeron de la red por ultima vez.</param>
/// <param name="Origen">Si vienen de la red o del fichero guardado.</param>
/// <param name="Fuente">Quien los publica, para poder citarlo en pantalla.</param>
public sealed record LecturaDeIndices(
    IndicesSolares Indices,
    DateTimeOffset ObtenidoUtc,
    OrigenDeLosIndices Origen,
    string Fuente)
{
    /// <summary>Cuanto ha pasado desde la medida mas antigua que compone estos indices.</summary>
    /// <param name="ahoraUtc">Momento actual.</param>
    public TimeSpan Antiguedad(DateTimeOffset ahoraUtc)
    {
        var edad = ahoraUtc - Indices.MedidoUtc;
        return edad < TimeSpan.Zero ? TimeSpan.Zero : edad;
    }

    /// <summary>Cuanto hace que se consiguio hablar con la fuente.</summary>
    /// <param name="ahoraUtc">Momento actual.</param>
    public TimeSpan DesdeLaUltimaDescarga(DateTimeOffset ahoraUtc)
    {
        var edad = ahoraUtc - ObtenidoUtc;
        return edad < TimeSpan.Zero ? TimeSpan.Zero : edad;
    }

    /// <summary>Los indices son lo bastante recientes como para darlos por buenos sin avisar.</summary>
    /// <param name="ahoraUtc">Momento actual.</param>
    /// <param name="frescura">Plazo dentro del cual se consideran frescos.</param>
    /// <remarks>
    /// Se mira cuanto hace que se hablo con el NOAA, no la edad de la medida. El flujo solar se
    /// publica una vez al dia: con el otro criterio el aviso saltaria siempre y dejaria de
    /// significar nada. La edad de la medida se ensena igualmente, la haya o no.
    /// </remarks>
    public bool EstanFrescos(DateTimeOffset ahoraUtc, TimeSpan frescura) =>
        Origen == OrigenDeLosIndices.Red && DesdeLaUltimaDescarga(ahoraUtc) <= frescura;

    /// <summary>
    /// Frase para el operador que dice de cuando son los indices y de donde salieron.
    /// </summary>
    /// <param name="ahoraUtc">Momento actual.</param>
    /// <param name="frescura">Plazo dentro del cual se consideran frescos.</param>
    /// <remarks>
    /// Nunca se calla la edad. Un valor viejo ensenado como si fuera de ahora hace que el
    /// operador llame en la banda equivocada.
    /// </remarks>
    public string Describir(DateTimeOffset ahoraUtc, TimeSpan frescura)
    {
        var medido = Indices.MedidoUtc.ToUniversalTime()
            .ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("es-ES"));
        var edad = TextoDeEdad(Antiguedad(ahoraUtc));

        if (Origen == OrigenDeLosIndices.Cache)
        {
            return $"Sin red: índices guardados del {medido} UTC, hace {edad} ({Fuente}).";
        }

        if (EstanFrescos(ahoraUtc, frescura))
        {
            return $"Índices del {medido} UTC, hace {edad} ({Fuente}).";
        }

        var sinRefrescar = TextoDeEdad(DesdeLaUltimaDescarga(ahoraUtc));
        return $"Índices sin refrescar desde hace {sinRefrescar}: los últimos son del {medido} UTC, "
               + $"hace {edad} ({Fuente}).";
    }

    /// <summary>Pone una duracion en palabras cortas: "35 min", "2 h 10 min", "3 días".</summary>
    /// <param name="edad">Duracion que se quiere describir.</param>
    public static string TextoDeEdad(TimeSpan edad)
    {
        if (edad < TimeSpan.FromMinutes(1))
        {
            return "menos de un minuto";
        }

        if (edad < TimeSpan.FromHours(1))
        {
            return $"{(int)edad.TotalMinutes} min";
        }

        if (edad < TimeSpan.FromDays(1))
        {
            var horas = (int)edad.TotalHours;
            var minutos = edad.Minutes;
            return minutos == 0 ? $"{horas} h" : $"{horas} h {minutos} min";
        }

        var dias = (int)edad.TotalDays;
        return dias == 1 ? "1 día" : $"{dias} días";
    }
}
