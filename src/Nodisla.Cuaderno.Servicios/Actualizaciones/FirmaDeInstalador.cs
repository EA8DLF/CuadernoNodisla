namespace Nodisla.Cuaderno.Servicios.Actualizaciones;

/// <summary>Que se ha visto al mirar la firma Authenticode de un fichero.</summary>
public enum EstadoDeFirma
{
    /// <summary>
    /// El fichero lleva firma y no se ha tocado desde que se firmo. <b>No dice quien firmo</b>:
    /// eso se decide comparando la huella con <see cref="FirmantesDeConfianza"/>.
    /// </summary>
    Integra,

    /// <summary>El fichero no lleva firma.</summary>
    SinFirma,

    /// <summary>
    /// Lleva firma pero no vale: el fichero se ha alterado despues de firmarlo, la firma esta
    /// rota o Windows ha dado un error que no se sabe interpretar. Ante la duda, no vale.
    /// </summary>
    NoValida,
}

/// <summary>Resultado de mirar la firma de un fichero.</summary>
/// <param name="Estado">Que se ha visto.</param>
/// <param name="HuellaSha256">
/// SHA-256 del certificado firmante (DER completo), en hexadecimal mayusculas. Solo viene
/// cuando el estado es <see cref="EstadoDeFirma.Integra"/>.
/// </param>
/// <param name="Sujeto">Sujeto del certificado firmante, para el registro.</param>
/// <param name="Codigo">Codigo que devolvio Windows (0 si la cadena ademas es de confianza).</param>
public sealed record ResultadoDeFirma(
    EstadoDeFirma Estado,
    string? HuellaSha256 = null,
    string? Sujeto = null,
    int Codigo = 0);

/// <summary>Mira la firma Authenticode de un fichero.</summary>
/// <remarks>
/// Es una interfaz para que la decision (instalar o no) se pruebe con dobles, sin firmar nada.
/// La de verdad es <see cref="VerificadorDeFirmaAuthenticode"/>.
/// </remarks>
public interface IVerificadorDeFirma
{
    /// <summary>Mira la firma del fichero. No lanza: lo que no se entiende es <see cref="EstadoDeFirma.NoValida"/>.</summary>
    /// <param name="ruta">Ruta del fichero.</param>
    ResultadoDeFirma Verificar(string ruta);
}

/// <summary>
/// Los certificados con los que NODISLA firma sus instaladores, fijados por huella.
/// </summary>
/// <remarks>
/// <para>
/// El certificado de NODISLA es <b>autofirmado</b>: Windows no confia en su cadena en el PC de
/// nadie. Por eso no se pregunta «¿confia Windows en el firmante?», sino «¿es exactamente
/// nuestro certificado?»: la huella SHA-256 del certificado va fijada aqui, dentro del programa,
/// y quien cambie la release de GitHub no puede cambiarla.
/// </para>
/// <para>
/// Es una <b>lista</b> para poder renovar el certificado sin dejar tirado a nadie: antes de
/// empezar a firmar con uno nuevo se publica una version (firmada con el viejo) que ya lleva las
/// dos huellas. Ver <c>docs/20-firma-de-codigo.md</c>.
/// </para>
/// </remarks>
public sealed class FirmantesDeConfianza
{
    /// <summary>
    /// «CN=Jose M. EA8DLF (SDR Nodisla)», RSA 2048, valido del 04/09/2026 al 04/09/2036.
    /// Huella SHA-1 (la que ensena Windows): 919A06FFBDF618A0D1CE38426547328C1FB82E74.
    /// </summary>
    public const string CertificadoNodisla2026 = "EED2089F0BBDEA84445C022C8DC00C3D21A563A5EFEE3FC6742A7397FFE5552A";

    private readonly HashSet<string> _huellas;

    /// <summary>Los firmantes que acepta el programa publicado.</summary>
    public static FirmantesDeConfianza Nodisla { get; } = new([CertificadoNodisla2026]);

    /// <summary>Crea la lista.</summary>
    /// <param name="huellasSha256">
    /// Huellas SHA-256 de los certificados, en hexadecimal (valen minusculas, espacios y dos puntos).
    /// </param>
    /// <exception cref="ArgumentException">Alguna huella no son 64 digitos hexadecimales.</exception>
    public FirmantesDeConfianza(IEnumerable<string> huellasSha256)
    {
        ArgumentNullException.ThrowIfNull(huellasSha256);
        _huellas = new HashSet<string>(StringComparer.Ordinal);
        foreach (var huella in huellasSha256)
        {
            var limpia = Normalizar(huella);
            if (limpia.Length != 64 || !limpia.All(Uri.IsHexDigit))
            {
                throw new ArgumentException("Una huella SHA-256 son 64 dígitos hexadecimales.", nameof(huellasSha256));
            }

            _huellas.Add(limpia);
        }
    }

    /// <summary>Las huellas aceptadas, normalizadas.</summary>
    public IReadOnlyCollection<string> Huellas => _huellas;

    /// <summary>
    /// Cierto solo si la firma esta integra <b>y</b> el firmante es uno de los fijados.
    /// </summary>
    /// <param name="firma">Lo que dijo el verificador.</param>
    public bool Acepta(ResultadoDeFirma? firma) =>
        firma is { Estado: EstadoDeFirma.Integra, HuellaSha256: { } huella }
        && _huellas.Contains(Normalizar(huella));

    private static string Normalizar(string? huella) =>
        new string((huella ?? string.Empty).Where(c => !char.IsWhiteSpace(c) && c != ':').ToArray()).ToUpperInvariant();
}
