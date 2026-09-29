namespace Nodisla.Cuaderno.Servicios.Eqsl;

/// <summary>Ajustes de eQSL.cc.</summary>
/// <remarks>Sin contrasenas: los secretos viven cifrados en el almacen de credenciales.</remarks>
public sealed record OpcionesEqsl
{
    /// <summary>Indicativo con el que se entra en eQSL.</summary>
    public string Usuario { get; init; } = string.Empty;

    /// <summary>
    /// Apodo de la cuenta (<c>QTHNickname</c>) cuando el mismo indicativo tiene varias.
    /// </summary>
    /// <remarks>
    /// eQSL admite varias cuentas por indicativo, una por ubicacion. Si hay mas de una y no se
    /// indica cual, eQSL elige por fechas y puede acertar o no; con el apodo no hay duda.
    /// </remarks>
    public string? ApodoDeEstacion { get; init; }

    /// <summary>Direccion de subida de ADIF.</summary>
    public Uri UrlDeSubida { get; init; } = new("https://www.eqsl.cc/qslcard/ImportADIF.cfm");

    /// <summary>Direccion de peticion del buzon de entrada.</summary>
    public Uri UrlDelBuzon { get; init; } = new("https://www.eqsl.cc/qslcard/DownloadInBox.cfm");

    /// <summary>Raiz del sitio, para resolver el enlace relativo que devuelve el buzon.</summary>
    /// <remarks>
    /// Desde octubre de 2019 los ficheros generados no cuelgan de <c>/qslcard/</c> sino de una
    /// carpeta por encima, asi que el enlace hay que resolverlo contra la raiz.
    /// </remarks>
    public Uri RaizDelSitio { get; init; } = new("https://www.eqsl.cc/");

    /// <summary>Traer solo las confirmaciones, sin los contactos todavia sin confirmar.</summary>
    public bool SoloConfirmados { get; init; } = true;
}
