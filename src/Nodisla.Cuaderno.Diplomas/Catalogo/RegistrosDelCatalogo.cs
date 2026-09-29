using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Diplomas.Catalogo;

/// <summary>
/// Campo del contacto que sirve de referencia en los diplomas que cuentan por campo.
/// </summary>
/// <remarks>
/// Es una enumeracion y no un texto libre: si el catalogo trae un campo que el motor no sabe
/// leer, el diploma se marca como no calculable y se dice por que, en vez de calcularlo mal.
/// </remarks>
public enum CampoDeQso
{
    /// <summary>El diploma no cuenta por campo.</summary>
    Ninguno,
    /// <summary>Entidad DXCC (<c>DXCC</c>).</summary>
    Dxcc,
    /// <summary>Division primaria: estado, provincia o canton (<c>STATE</c>).</summary>
    State,
    /// <summary>Zona CQ (<c>CQZ</c>).</summary>
    CqZone,
    /// <summary>Zona ITU (<c>ITUZ</c>).</summary>
    ItuZone,
    /// <summary>Continente (<c>CONT</c>).</summary>
    Continent,
    /// <summary>Prefijo WPX (<c>PFX</c>).</summary>
    Pfx,
    /// <summary>Los cuatro primeros caracteres del localizador (<c>GRIDSQUARE</c>).</summary>
    Gridsquare4,
    /// <summary>Division secundaria: condado o comarca (<c>CNTY</c>).</summary>
    Cnty,
    /// <summary>Localidad (<c>QTH</c>).</summary>
    Qth,
    /// <summary>Direccion postal (<c>ADDRESS</c>).</summary>
    Address,
    /// <summary>Referencia del programa declarado en <c>SIG</c> (<c>SIG_INFO</c>).</summary>
    SigInfo,
}

/// <summary>Un diploma del catalogo, con todo lo que el motor necesita para calcularlo.</summary>
/// <param name="Codigo">Clave del diploma, por ejemplo <c>DXCC</c>.</param>
/// <param name="Clase">Como cuenta: por indicativo, por campo o por referencia.</param>
/// <param name="Nombre">Nombre original, en el idioma en que lo publica su gestor.</param>
/// <param name="NombreEspanol">Nombre en espanol, si se ha traducido.</param>
/// <param name="Gestor">Entidad que lo otorga, si se conoce.</param>
/// <param name="Web">Pagina del diploma.</param>
/// <param name="WebDeReferencias">Pagina donde el gestor publica sus referencias.</param>
/// <param name="Exigencia">Que confirmacion hace falta.</param>
/// <param name="MediosValidos">Vias de confirmacion admitidas; vacio significa cualquiera.</param>
/// <param name="ValidaElGestor">
/// El diploma se valida contra la base de datos de su gestor, que el cuaderno no puede
/// consultar. Se cuenta lo confirmado por cualquier via, que es una cota inferior.
/// </param>
/// <param name="Campo">Campo del contacto del que sale la referencia.</param>
/// <param name="CampoLider">Campo que va delante del principal, por ejemplo el estado en <c>USA-CA</c>.</param>
/// <param name="Separador">Separador entre el campo lider y el principal.</param>
/// <param name="CampoExacto">La referencia tiene que estar en el catalogo, no vale cualquier valor.</param>
/// <param name="CadenaInicial">Texto que abre la referencia dentro del campo, por ejemplo <c>(</c>.</param>
/// <param name="CadenaFinal">Texto que la cierra.</param>
/// <param name="ReferenciaLibre">El universo de referencias es abierto: no hay lista que valga.</param>
/// <param name="ValidoDesde">Primera fecha que cuenta para el diploma.</param>
/// <param name="ValidoHasta">Ultima fecha que cuenta.</param>
/// <param name="BandasPermitidas">Bandas que admite el diploma; vacio, todas.</param>
/// <param name="ClasesDeModoPermitidas">Familias de modos que admite; vacio, todas.</param>
/// <param name="SoloEntidadesVigentes">Las entidades DXCC borradas no cuentan.</param>
/// <param name="Calculable">El motor sabe calcularlo.</param>
/// <param name="MotivoNoCalculable">Por que no lo sabe calcular.</param>
/// <param name="Descripcion">Reglamento resumido, tal y como lo trae el catalogo.</param>
public sealed record PremioDelCatalogo(
    string Codigo,
    ClaseDeDiploma Clase,
    string Nombre,
    string? NombreEspanol,
    string? Gestor,
    string? Web,
    string? WebDeReferencias,
    ExigenciaDeConfirmacion Exigencia,
    IReadOnlyList<MedioDeConfirmacion> MediosValidos,
    bool ValidaElGestor,
    CampoDeQso Campo,
    CampoDeQso CampoLider,
    string? Separador,
    bool CampoExacto,
    string? CadenaInicial,
    string? CadenaFinal,
    bool ReferenciaLibre,
    string? ValidoDesde,
    string? ValidoHasta,
    IReadOnlyList<string> BandasPermitidas,
    IReadOnlyList<ClaseDeModo> ClasesDeModoPermitidas,
    bool SoloEntidadesVigentes,
    bool Calculable,
    string? MotivoNoCalculable,
    string? Descripcion)
{
    /// <summary>Nombre preferido para la interfaz: el espanol si lo hay.</summary>
    public string NombreParaMostrar => string.IsNullOrWhiteSpace(NombreEspanol) ? Nombre : NombreEspanol;

    /// <summary>
    /// Las entidades DXCC borradas cuentan para este diploma si el contacto cae dentro de la
    /// ventana en que la entidad existia. Solo tiene sentido en los diplomas que cuentan por
    /// la entidad DXCC del contacto; en los demas, una referencia retirada no cuenta.
    /// </summary>
    public bool CuentanLasBorradas => Campo == CampoDeQso.Dxcc && !SoloEntidadesVigentes;
}

/// <summary>Una variante de un diploma tal y como viene del catalogo.</summary>
/// <param name="Codigo">Diploma al que pertenece.</param>
/// <param name="Variante">Nombre de la variante.</param>
/// <param name="Descripcion">Reglamento de la variante.</param>
/// <param name="Modos">Modos o submodos exigidos; vacio, cualquiera.</param>
/// <param name="Bandas">Bandas exigidas; vacio, cualquiera.</param>
/// <param name="Clase">Familia de modos exigida: telegrafia, fonia, digitales o cualquiera.</param>
/// <param name="Continentes">Continentes exigidos; vacio, cualquiera.</param>
/// <param name="Anual">Solo cuenta lo trabajado dentro del ano en curso.</param>
/// <param name="ExigeSatelite">Solo cuentan los contactos por satelite.</param>
/// <param name="ExcluyeSatelite">Los contactos por satelite no cuentan.</param>
/// <param name="ValidoDesde">Primera fecha que cuenta para la variante.</param>
/// <param name="ValidoHasta">Ultima fecha que cuenta.</param>
/// <param name="Objetivo">Cuantas referencias distintas hacen falta, si el reglamento lo fija.</param>
/// <param name="Sintetica">
/// La variante no existe en el original: se anade porque el diploma no declara ninguna clase y
/// sin ella no habria nada que calcular.
/// </param>
public sealed record VarianteDelCatalogo(
    string Codigo,
    string Variante,
    string? Descripcion,
    IReadOnlyList<string> Modos,
    IReadOnlyList<string> Bandas,
    ClaseDeModo Clase,
    IReadOnlyList<string> Continentes,
    bool Anual,
    bool ExigeSatelite,
    bool ExcluyeSatelite,
    string? ValidoDesde,
    string? ValidoHasta,
    int? Objetivo,
    bool Sintetica);

/// <summary>Una referencia del catalogo mundial.</summary>
/// <param name="Codigo">Diploma al que pertenece.</param>
/// <param name="Referencia">Codigo de la referencia.</param>
/// <param name="Alias">Codigo alternativo con el que tambien se conoce.</param>
/// <param name="ValidoDesde">Desde cuando cuenta.</param>
/// <param name="ValidoHasta">Hasta cuando cuenta.</param>
/// <param name="Descripcion">Nombre para mostrar.</param>
/// <param name="Grupo">Agrupacion principal.</param>
/// <param name="Subgrupo">Agrupacion secundaria.</param>
/// <param name="Gridsquare">Localizador de la referencia.</param>
/// <param name="Puntuacion">Puntos que otorga.</param>
/// <param name="Bonus">Puntos extra.</param>
/// <param name="Valida">La referencia sigue vigente.</param>
/// <param name="DxccPermitidos">
/// Entidades DXCC desde las que se puede activar. En el original es una lista de texto de hasta
/// 3.000 caracteres; aqui ya viene partida en numeros.
/// </param>
public sealed record ReferenciaDelCatalogo(
    string Codigo,
    string Referencia,
    string? Alias,
    string? ValidoDesde,
    string? ValidoHasta,
    string Descripcion,
    string? Grupo,
    string? Subgrupo,
    string? Gridsquare,
    double Puntuacion,
    double Bonus,
    bool Valida,
    IReadOnlyList<int> DxccPermitidos);

/// <summary>Un informe del catalogo, de los que el original define en XML.</summary>
/// <param name="Identificador">Clave del informe.</param>
/// <param name="Titulo">Titulo para mostrar.</param>
/// <param name="Descripcion">Que calcula.</param>
/// <param name="Proveedor">Quien lo publica.</param>
/// <param name="Habilitado">Esta activo.</param>
public sealed record InformeDelCatalogo(
    string Identificador,
    string? Titulo,
    string? Descripcion,
    string? Proveedor,
    bool Habilitado);

/// <summary>Cabecera del recurso: version y recuentos declarados.</summary>
/// <param name="Version">Version del formato del recurso.</param>
/// <param name="FechaDeLosDatos">Fecha de la ultima actualizacion del catalogo original.</param>
/// <param name="FechaDeGeneracion">Cuando se genero el recurso.</param>
/// <param name="Diplomas">Cuantos diplomas declara.</param>
/// <param name="Variantes">Cuantas variantes declara.</param>
/// <param name="Referencias">Cuantas referencias declara.</param>
public sealed record CabeceraDelCatalogo(
    int Version,
    string FechaDeLosDatos,
    string FechaDeGeneracion,
    int Diplomas,
    int Variantes,
    int Referencias)
{
    /// <summary>Huella con la que se decide si hay que reconstruir la base del catalogo.</summary>
    public string Huella =>
        $"{Version}|{FechaDeLosDatos}|{FechaDeGeneracion}|{Diplomas}|{Variantes}|{Referencias}";
}

/// <summary>La parte pequena del catalogo, la que cabe en memoria.</summary>
/// <param name="Cabecera">Version y recuentos.</param>
/// <param name="Diplomas">Los diplomas, por codigo.</param>
/// <param name="Variantes">Las variantes de cada diploma, por codigo.</param>
/// <param name="Informes">Los informes definidos.</param>
public sealed record CatalogoDeDiplomas(
    CabeceraDelCatalogo Cabecera,
    IReadOnlyDictionary<string, PremioDelCatalogo> Diplomas,
    IReadOnlyDictionary<string, IReadOnlyList<VarianteDelCatalogo>> Variantes,
    IReadOnlyList<InformeDelCatalogo> Informes);
