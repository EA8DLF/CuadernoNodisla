namespace Nodisla.Cuaderno.Adif;

/// <summary>Un campo ADIF suelto, tal y como viaja por el fichero.</summary>
/// <param name="Nombre">Nombre del campo en mayusculas, por ejemplo <c>MY_GRIDSQUARE</c>.</param>
/// <param name="Valor">Valor ya decodificado a texto.</param>
/// <param name="TipoAdif">Indicador de tipo si el fichero lo traia: <c>D</c>, <c>N</c>, <c>S</c>…</param>
public readonly record struct CampoAdif(string Nombre, string Valor, string? TipoAdif = null)
{
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
