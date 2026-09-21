using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Datos.Conversores;

/// <summary>
/// Utilidades comunes a los conversores: formato de fecha y lecturas tolerantes.
/// </summary>
/// <remarks>
/// Los objetos de valor del dominio son <c>readonly record struct</c> sin constructor publico,
/// asi que EF Core no puede mapearlos solo. Cada conversor escribe exactamente lo que espera
/// ADIF: texto normalizado para indicativos, localizadores, bandas e informes, y megahercios en
/// coma flotante para la frecuencia. Las fechas van en texto ISO-8601 UTC al segundo, que es lo
/// unico que garantiza que el orden lexicografico coincida con el cronologico.
/// </remarks>
public static class ConversoresDeValor
{
    /// <summary>Formato con el que se guarda cualquier fecha: ISO-8601 en UTC, al segundo.</summary>
    public const string FormatoFecha = "yyyy-MM-dd HH:mm:ss";

    /// <summary>Formatos aceptados al leer, por si una base antigua trae otra variante.</summary>
    private static readonly string[] FormatosAceptados =
    [
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-dd HH:mm:ss.FFFFFFF",
        "yyyy-MM-ddTHH:mm:ss.FFFFFFF",
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-dd",
    ];

    /// <summary>Escribe el instante en UTC con el formato del cuaderno.</summary>
    public static string ATexto(DateTimeOffset instante) =>
        instante.UtcDateTime.ToString(FormatoFecha, CultureInfo.InvariantCulture);

    /// <summary>Lee una fecha guardada. Siempre se interpreta como UTC, nunca como hora local.</summary>
    public static DateTimeOffset AFecha(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return default;
        if (DateTime.TryParseExact(texto, FormatosAceptados, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var fecha))
        {
            return new DateTimeOffset(DateTime.SpecifyKind(fecha, DateTimeKind.Utc));
        }
        return DateTimeOffset.Parse(texto, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }

    /// <summary>Localizador tolerante: lo que no sea un Maidenhead valido se lee como vacio.</summary>
    public static Locator ALocator(string texto) =>
        Locator.TryParse(texto, out var l) ? l : Locator.Vacio;

    /// <summary>Banda tolerante: lo que no este en la tabla ADIF se lee como vacia.</summary>
    public static Banda ABanda(string texto) =>
        Banda.TryParse(texto, out var b) ? b : Banda.Vacia;

    /// <summary>Frecuencia en megahercios; el cero y los negativos se leen como frecuencia cero.</summary>
    public static Frecuencia AFrecuencia(double megahercios) =>
        megahercios <= 0 ? Frecuencia.Cero : Frecuencia.DesdeMegahercios((decimal)megahercios);
}

/// <summary>Indicativo del corresponsal o propio, guardado ya normalizado.</summary>
public sealed class ConversorDeIndicativo : ValueConverter<Indicativo, string>
{
    /// <summary>Crea el conversor.</summary>
    public ConversorDeIndicativo()
        : base(v => v.Valor ?? string.Empty, v => Indicativo.Crudo(v))
    {
    }
}

/// <summary>Localizador Maidenhead. El vacio se guarda como cadena vacia, nunca como nulo.</summary>
public sealed class ConversorDeLocator : ValueConverter<Locator, string>
{
    /// <summary>Crea el conversor.</summary>
    public ConversorDeLocator()
        : base(v => v.Valor ?? string.Empty, v => ConversoresDeValor.ALocator(v))
    {
    }
}

/// <summary>Banda ADIF en minusculas.</summary>
public sealed class ConversorDeBanda : ValueConverter<Banda, string>
{
    /// <summary>Crea el conversor.</summary>
    public ConversorDeBanda()
        : base(v => v.Nombre ?? string.Empty, v => ConversoresDeValor.ABanda(v))
    {
    }
}

/// <summary>
/// Modo principal ADIF. El submodo no va aqui: viaja en la columna <c>submode</c> a traves de
/// una propiedad sombra, porque la clave natural del cuaderno se define sobre el modo principal
/// y no debe ensuciarse con el submodo.
/// </summary>
public sealed class ConversorDeModo : ValueConverter<Modo, string>
{
    /// <summary>Crea el conversor.</summary>
    public ConversorDeModo()
        : base(v => v.Principal ?? string.Empty, v => Modo.Crudo(v, null))
    {
    }
}

/// <summary>Frecuencia en megahercios, en columna REAL como manda el modelo de datos.</summary>
public sealed class ConversorDeFrecuencia : ValueConverter<Frecuencia, double>
{
    /// <summary>Crea el conversor.</summary>
    public ConversorDeFrecuencia()
        : base(v => (double)v.Megahercios, v => ConversoresDeValor.AFrecuencia(v))
    {
    }
}

/// <summary>Informe de senal, tal cual se escribe en ADIF.</summary>
public sealed class ConversorDeInforme : ValueConverter<Informe, string>
{
    /// <summary>Crea el conversor.</summary>
    public ConversorDeInforme()
        : base(v => v.Texto ?? string.Empty, v => Informe.Parse(v))
    {
    }
}

/// <summary>Instante en UTC, en texto ISO-8601 al segundo.</summary>
public sealed class ConversorDeFechaUtc : ValueConverter<DateTimeOffset, string>
{
    /// <summary>Crea el conversor.</summary>
    public ConversorDeFechaUtc()
        : base(v => ConversoresDeValor.ATexto(v), v => ConversoresDeValor.AFecha(v))
    {
    }
}

/// <summary>Identificador estable del contacto, en texto.</summary>
public sealed class ConversorDeGuid : ValueConverter<Guid, string>
{
    /// <summary>Crea el conversor.</summary>
    public ConversorDeGuid()
        : base(v => v.ToString("D"), v => Guid.Parse(v))
    {
    }
}

/// <summary>Servicio de confirmacion, con los codigos que usa ADIF para cada via.</summary>
public sealed class ConversorDeMedio : ValueConverter<MedioDeConfirmacion, string>
{
    /// <summary>Crea el conversor.</summary>
    public ConversorDeMedio()
        : base(v => ATexto(v), v => AMedio(v))
    {
    }

    private static string ATexto(MedioDeConfirmacion medio) => medio switch
    {
        MedioDeConfirmacion.Papel => "QSL",
        MedioDeConfirmacion.Lotw => "LOTW",
        MedioDeConfirmacion.Eqsl => "EQSL",
        MedioDeConfirmacion.ClubLog => "CLUBLOG",
        MedioDeConfirmacion.QrzCom => "QRZCOM",
        MedioDeConfirmacion.HamQth => "HAMQTH",
        MedioDeConfirmacion.HrdLog => "HRDLOG",
        MedioDeConfirmacion.QrzCq => "QRZCQ",
        _ => medio.ToString().ToUpperInvariant(),
    };

    private static MedioDeConfirmacion AMedio(string texto) => texto.ToUpperInvariant() switch
    {
        "QSL" => MedioDeConfirmacion.Papel,
        "LOTW" => MedioDeConfirmacion.Lotw,
        "EQSL" => MedioDeConfirmacion.Eqsl,
        "CLUBLOG" => MedioDeConfirmacion.ClubLog,
        "QRZCOM" => MedioDeConfirmacion.QrzCom,
        "HAMQTH" => MedioDeConfirmacion.HamQth,
        "HRDLOG" => MedioDeConfirmacion.HrdLog,
        "QRZCQ" => MedioDeConfirmacion.QrzCq,
        _ => MedioDeConfirmacion.Papel,
    };
}

/// <summary>
/// Estado de una confirmacion. Se guardan codigos de una letra parecidos a los de ADIF pero
/// propios: ADIF no distingue «rechazado» de «devuelto» y el dominio si.
/// </summary>
public sealed class ConversorDeEstado : ValueConverter<EstadoDeConfirmacion, string>
{
    /// <summary>Crea el conversor.</summary>
    public ConversorDeEstado()
        : base(v => ATexto(v), v => AEstado(v))
    {
    }

    private static string ATexto(EstadoDeConfirmacion estado) => estado switch
    {
        EstadoDeConfirmacion.Ninguno => "N",
        EstadoDeConfirmacion.Pendiente => "Q",
        EstadoDeConfirmacion.Confirmado => "Y",
        EstadoDeConfirmacion.Solicitado => "R",
        EstadoDeConfirmacion.Rechazado => "X",
        EstadoDeConfirmacion.Devuelto => "D",
        EstadoDeConfirmacion.Invalido => "I",
        _ => "N",
    };

    private static EstadoDeConfirmacion AEstado(string texto) => texto.ToUpperInvariant() switch
    {
        "Q" => EstadoDeConfirmacion.Pendiente,
        "Y" => EstadoDeConfirmacion.Confirmado,
        "R" => EstadoDeConfirmacion.Solicitado,
        "X" => EstadoDeConfirmacion.Rechazado,
        "D" => EstadoDeConfirmacion.Devuelto,
        "I" => EstadoDeConfirmacion.Invalido,
        _ => EstadoDeConfirmacion.Ninguno,
    };
}

/// <summary>Via de envio de la tarjeta en papel (<c>QSL_SENT_VIA</c>).</summary>
public sealed class ConversorDeVia : ValueConverter<ViaDeEnvio, string>
{
    /// <summary>Crea el conversor.</summary>
    public ConversorDeVia()
        : base(v => ATexto(v), v => AVia(v))
    {
    }

    private static string ATexto(ViaDeEnvio via) => via switch
    {
        ViaDeEnvio.Buro => "B",
        ViaDeEnvio.Directo => "D",
        ViaDeEnvio.Gestor => "M",
        ViaDeEnvio.Electronico => "E",
        _ => string.Empty,
    };

    private static ViaDeEnvio AVia(string texto) => texto.ToUpperInvariant() switch
    {
        "B" => ViaDeEnvio.Buro,
        "D" => ViaDeEnvio.Directo,
        "M" => ViaDeEnvio.Gestor,
        "E" => ViaDeEnvio.Electronico,
        _ => ViaDeEnvio.Ninguna,
    };
}

/// <summary>Programa de la referencia, con el mismo codigo que usan los diplomas.</summary>
public sealed class ConversorDeTipoDeReferencia : ValueConverter<TipoDeReferencia, string>
{
    /// <summary>Crea el conversor.</summary>
    public ConversorDeTipoDeReferencia()
        : base(v => v.ToString().ToUpper(), v => ATipo(v))
    {
    }

    private static TipoDeReferencia ATipo(string texto) =>
        Enum.TryParse<TipoDeReferencia>(texto, ignoreCase: true, out var t) ? t : TipoDeReferencia.Otra;
}

/// <summary>Lado de la referencia: falso el corresponsal, cierto la estacion propia.</summary>
public sealed class ConversorDeLado : ValueConverter<LadoDeReferencia, bool>
{
    /// <summary>Crea el conversor.</summary>
    public ConversorDeLado()
        : base(v => v == LadoDeReferencia.Propia,
               v => v ? LadoDeReferencia.Propia : LadoDeReferencia.Corresponsal)
    {
    }
}
