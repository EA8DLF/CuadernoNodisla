using System.Globalization;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Impresion.Modelo;

/// <summary>
/// Una linea de contacto dentro de una etiqueta: lo que hay que confirmar de un QSO.
/// </summary>
/// <param name="Fecha">Fecha del contacto, en UTC.</param>
/// <param name="HoraUtc">Hora del contacto, en UTC.</param>
/// <param name="Banda">Banda.</param>
/// <param name="Modo">Modo.</param>
/// <param name="Informe">Informe enviado, que es el que confirma quien manda la tarjeta.</param>
/// <param name="Satelite">Satelite, si el contacto fue por satelite.</param>
/// <param name="ModoDelSatelite">Modo del transpondedor, si lo hubo.</param>
public readonly record struct LineaDeContacto(
    DateOnly Fecha,
    TimeOnly HoraUtc,
    string Banda,
    string Modo,
    string Informe,
    string? Satelite,
    string? ModoDelSatelite)
{
    /// <summary>El contacto fue por satelite.</summary>
    public bool EsPorSatelite => !string.IsNullOrEmpty(Satelite);

    /// <summary>Saca la linea de un contacto del cuaderno.</summary>
    /// <param name="qso">El contacto.</param>
    /// <returns>La linea lista para imprimir.</returns>
    /// <remarks>
    /// <b>Se imprime el informe enviado, no el recibido.</b> La tarjeta confirma lo que yo oi
    /// de la otra estacion; lo que ella oyo de mi lo confirmara su tarjeta. Imprimir el
    /// recibido es un error corriente y deja al corresponsal con dos informes distintos del
    /// mismo contacto.
    /// </remarks>
    /// <exception cref="ArgumentNullException">No se ha dado contacto.</exception>
    public static LineaDeContacto Desde(Qso qso)
    {
        ArgumentNullException.ThrowIfNull(qso);

        var utc = qso.InicioUtc.ToUniversalTime();
        return new LineaDeContacto(
            DateOnly.FromDateTime(utc.Date),
            TimeOnly.FromTimeSpan(utc.TimeOfDay),
            // Se cualifica el tipo: el parametro «Banda» de este mismo registro tapa al tipo.
            qso.Band.EsVacia ? Dominio.Valores.Banda.DesdeFrecuencia(qso.Freq).Nombre : qso.Band.Nombre,
            qso.Mode.NombreUsual,
            qso.RstSent.EsVacio ? string.Empty : qso.RstSent.Texto,
            qso.SatName,
            qso.SatMode);
    }
}

/// <summary>
/// Una etiqueta de QSL: a quien va y que contactos confirma.
/// </summary>
/// <param name="Destino">Indicativo del corresponsal.</param>
/// <param name="MiIndicativo">Indicativo con el que se trabajo.</param>
/// <param name="Contactos">Contactos que confirma la etiqueta, en orden.</param>
/// <param name="Gestor">Gestor de QSL del corresponsal, si lo tiene.</param>
/// <param name="Via">Por donde va la tarjeta.</param>
/// <param name="MiLocalizador">Mi localizador, para que el corresponsal lo apunte.</param>
/// <param name="Mensaje">Frase al pie: agradecimiento, peticion de tarjeta o lo que se quiera.</param>
/// <remarks>
/// <b>Una etiqueta es un corresponsal, no un contacto.</b> Si con la misma estacion hay cinco
/// contactos, van los cinco en la misma etiqueta y se pega una sola tarjeta. Imprimir una
/// etiqueta por contacto multiplica el gasto de papel y de buro sin confirmar nada mas.
/// </remarks>
public sealed record EtiquetaDeQsl(
    Indicativo Destino,
    Indicativo MiIndicativo,
    IReadOnlyList<LineaDeContacto> Contactos,
    string? Gestor,
    ViaDeEnvio Via,
    Locator MiLocalizador,
    string? Mensaje)
{
    /// <summary>Encabezado de la etiqueta: a quien va y por donde.</summary>
    public string Encabezado => string.IsNullOrWhiteSpace(Gestor)
        ? Destino.Valor
        : Textos.F("Servicios.Impresion.Etiqueta.Via", Destino.Valor, Gestor);

    /// <summary>Texto de la via, para el pie.</summary>
    public string TextoDeLaVia => Via switch
    {
        ViaDeEnvio.Buro => Textos.T("Servicios.Impresion.Via.Buro"),
        ViaDeEnvio.Directo => Textos.T("Servicios.Impresion.Via.Directo"),
        ViaDeEnvio.Gestor => Textos.T("Servicios.Impresion.Via.Gestor"),
        ViaDeEnvio.Electronico => Textos.T("Servicios.Impresion.Via.Electronico"),
        _ => string.Empty,
    };

    /// <summary>Las columnas de una linea de contacto, ya formateadas.</summary>
    /// <param name="linea">Linea de contacto.</param>
    /// <returns>Fecha, hora, banda, modo e informe como textos.</returns>
    /// <remarks>
    /// La fecha va en el orden internacional —ano, mes y dia— y no en el espanol. No es un
    /// descuido: la tarjeta la lee alguien que puede estar en cualquier parte, y <c>03/04/26</c>
    /// significa cosas distintas a un lado y a otro del Atlantico. En <c>2026-04-03</c> no hay
    /// ambiguedad posible.
    /// </remarks>
    public static string[] Columnas(LineaDeContacto linea)
    {
        var ci = CultureInfo.InvariantCulture;
        return
        [
            linea.Fecha.ToString("yyyy-MM-dd", ci),
            linea.HoraUtc.ToString("HH:mm", ci),
            linea.Banda,
            linea.Modo,
            linea.Informe,
        ];
    }
}
