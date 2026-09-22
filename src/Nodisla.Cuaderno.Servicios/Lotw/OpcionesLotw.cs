namespace Nodisla.Cuaderno.Servicios.Lotw;

/// <summary>
/// Ajustes de LoTW (Logbook of the World, de la ARRL).
/// </summary>
/// <remarks>
/// Aqui no hay ni una contrasena: los secretos viven cifrados en el almacen de credenciales.
/// </remarks>
public sealed record OpcionesLotw
{
    /// <summary>Usuario de LoTW, que es el indicativo con el que se registro la cuenta.</summary>
    public string Usuario { get; init; } = string.Empty;

    /// <summary>
    /// Nombre de la «Station Location» de TQSL con la que firmar. Es el nombre que el operador
    /// le puso en TQSL, no el indicativo; si no se indica, TQSL abriria un dialogo y la subida
    /// se quedaria colgada esperando a nadie.
    /// </summary>
    public string UbicacionDeEstacion { get; init; } = string.Empty;

    /// <summary>
    /// Ruta completa a <c>tqsl.exe</c>. Vacia para que el programa la busque solo.
    /// </summary>
    public string? RutaDeTqsl { get; init; }

    /// <summary>Direccion del informe de confirmaciones.</summary>
    /// <remarks>
    /// Es la que usa el propio Log4OM y la que documenta la ARRL para programas de terceros.
    /// </remarks>
    public Uri UrlDelInforme { get; init; } = new("https://lotw.arrl.org/lotwuser/lotwreport.adi");

    /// <summary>
    /// Pedir el detalle de la confirmacion (<c>qso_qsldetail=yes</c>): DXCC, zonas, estado,
    /// condado y localizador del corresponsal tal y como los declaro el otro operador.
    /// </summary>
    public bool PedirDetalleDeQsl { get; init; } = true;

    /// <summary>
    /// Filtrar el informe por el indicativo con el que se transmitio (<c>qso_owncall</c>).
    /// Vacio para traer los de todos los indicativos de la cuenta.
    /// </summary>
    public string? FiltrarPorIndicativoPropio { get; init; }

    /// <summary>
    /// Que hacer con los contactos que TQSL ve repetidos o fuera del rango de fechas del
    /// certificado. <c>compliant</c> firma los demas y deja fuera los problematicos, que es lo
    /// unico razonable sin nadie delante de la pantalla.
    /// </summary>
    public string AccionDeTqsl { get; init; } = "compliant";

    /// <summary>Tiempo maximo que se le concede a TQSL para firmar y subir.</summary>
    public TimeSpan EsperaDeTqsl { get; init; } = TimeSpan.FromMinutes(10);
}
