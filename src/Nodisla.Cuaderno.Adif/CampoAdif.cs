namespace Nodisla.Cuaderno.Adif;

/// <summary>Un campo ADIF suelto, tal y como viaja por el fichero.</summary>
/// <param name="Nombre">Nombre del campo en mayusculas, por ejemplo <c>MY_GRIDSQUARE</c>.</param>
/// <param name="Valor">Valor ya decodificado a texto, que es lo que vale el campo.</param>
/// <param name="TipoAdif">Indicador de tipo si el fichero lo traia: <c>D</c>, <c>N</c>, <c>S</c>…</param>
/// <param name="Literal">
/// Texto que ocupaba el campo en el fichero cuando no coincide con <paramref name="Valor"/>.
/// </param>
/// <remarks>
/// <paramref name="Literal"/> existe por un caso muy concreto y muy real: Log4OM escribe
/// <c>&lt;CNTY:10&gt;CA,VENTURA // Ventura</c>, declarando bien los diez bytes del codigo del
/// condado y dejando detras un rotulo de adorno. El dato bueno es <c>CA,VENTURA</c> —es lo que
/// cuenta para los diplomas— pero el fichero ha de salir igual que entro. Por eso se guardan
/// los dos: el valor manda en el modelo y el literal manda al escribir el ADI, con la longitud
/// declarada del valor.
/// </remarks>
public readonly record struct CampoAdif(
    string Nombre,
    string Valor,
    string? TipoAdif = null,
    string? Literal = null)
{
    /// <summary>Texto que hay que volcar al fichero ADI: el literal si lo hay, o el valor.</summary>
    public string TextoParaEscribir => Literal ?? Valor;

    /// <summary>Representacion legible, util al depurar y en los mensajes de aviso.</summary>
    public override string ToString() => $"{Nombre}={Valor}";
}

/// <summary>Que clase de elemento se ha encontrado al recorrer el fichero.</summary>
internal enum ClaseDeToken
{
    /// <summary>Un campo con su valor.</summary>
    Campo,

    /// <summary>La etiqueta <c>&lt;EOH&gt;</c>, que cierra la cabecera.</summary>
    FinDeCabecera,

    /// <summary>La etiqueta <c>&lt;EOR&gt;</c>, que cierra un registro.</summary>
    FinDeRegistro,
}

/// <summary>Elemento devuelto por el analizador de ADI.</summary>
internal readonly record struct TokenAdi(ClaseDeToken Clase, CampoAdif Campo);
