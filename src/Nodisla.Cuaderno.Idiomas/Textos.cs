using System.Globalization;
using System.Resources;

namespace Nodisla.Cuaderno.Idiomas;

/// <summary>Un idioma en que puede hablar el programa.</summary>
/// <param name="Codigo">Código de dos letras («es», «en»...).</param>
/// <param name="Nombre">Nombre del idioma en su propio idioma («Deutsch»), que es como lo busca quien lo habla.</param>
/// <param name="CulturaDeFabrica">
/// Cultura de fechas y números cuando el sistema no habla ese idioma (si lo habla, se usa la del sistema).
/// </param>
public sealed record IdiomaDelPrograma(string Codigo, string Nombre, string CulturaDeFabrica)
{
    /// <inheritdoc />
    public override string ToString() => Nombre;
}

/// <summary>
/// Los textos que ve el operador, en el idioma elegido.
/// </summary>
/// <remarks>
/// <para>
/// Las claves llevan delante su apartado: <c>Cabina.Conectar</c> está en <c>Recursos\Cabina.resx</c>
/// (español, la referencia) y en <c>Cabina.en.resx</c>, <c>Cabina.pt.resx</c>... Una clave sin
/// traducir cae al español, y una clave que no existe se devuelve tal cual, para que se vea en
/// pantalla y la caza la prueba de claves.
/// </para>
/// <para>
/// El idioma se elige con <see cref="Cambiar"/> y se puede cambiar con el programa abierto: quien
/// enseña textos fijos se entera por <see cref="IdiomaCambiado"/> o con <see cref="AlCambiar{T}"/>.
/// Mientras nadie elige, el programa habla español: así las pruebas no dependen del Windows en que
/// se ejecuten.
/// </para>
/// <para>
/// <b>Lo que NO se traduce</b>: los registros (siguen en español), los rótulos que imitan el panel
/// de la radio (MODE, SPLIT, CLAR...) y el formato técnico de frecuencias y horas UTC.
/// </para>
/// </remarks>
public static class Textos
{
    /// <summary>Los apartados que tienen fichero de textos, en el orden en que se cargan.</summary>
    public static IReadOnlyList<string> Apartados { get; } =
    [
        "Comun", "Principal", "Cabina", "Digital", "Libro", "Qsl", "Ajustes", "Ayuda", "Dialogos", "Servicios",
    ];

    /// <summary>Los seis idiomas; el primero, el español, es la referencia.</summary>
    public static IReadOnlyList<IdiomaDelPrograma> Idiomas { get; } =
    [
        new("es", "Español", "es-ES"),
        new("en", "English", "en-GB"),
        new("pt", "Português", "pt-PT"),
        new("fr", "Français", "fr-FR"),
        new("it", "Italiano", "it-IT"),
        new("de", "Deutsch", "de-DE"),
    ];

    /// <summary>El idioma de quien no habla ninguno de los seis.</summary>
    public const string IdiomaDeReserva = "en";

    private static readonly Dictionary<string, ResourceManager> Gestores = Apartados.ToDictionary(
        a => a,
        a => new ResourceManager($"Nodisla.Cuaderno.Idiomas.Recursos.{a}", typeof(Textos).Assembly),
        StringComparer.Ordinal);

    /// <summary>Quien se ha suscrito, en qué hilo y con qué contexto, para avisarle en el suyo.</summary>
    private sealed record Oyente(WeakReference Dueno, Action<object> Accion, int Hilo, SynchronizationContext? Contexto);

    private static readonly List<Oyente> Oyentes = [];

    /// <summary>La cultura del Windows en que arrancó el programa, antes de tocar nada.</summary>
    public static CultureInfo CulturaDelSistema { get; } = CultureInfo.CurrentUICulture;

    /// <summary>La cultura de formatos del sistema (puede no coincidir con la de su idioma).</summary>
    public static CultureInfo FormatosDelSistema { get; } = CultureInfo.CurrentCulture;

    /// <summary>Cultura de los textos, las fechas y los números.</summary>
    public static CultureInfo Cultura { get; private set; } = new("es-ES", useUserOverride: false);

    /// <summary>Código de dos letras del idioma en uso.</summary>
    public static string Codigo => Cultura.TwoLetterISOLanguageName;

    /// <summary>Salta cada vez que cambia el idioma, en el hilo de quien lo cambió.</summary>
    public static event EventHandler? IdiomaCambiado;

    /// <summary>El idioma que toca si el operador no ha elegido: el del sistema si es de los seis; si no, inglés.</summary>
    /// <param name="cultura">Cultura del sistema (por omisión, la del Windows en que se arrancó).</param>
    /// <returns>Código de dos letras.</returns>
    public static string IdiomaDelSistema(CultureInfo? cultura = null)
    {
        var codigo = (cultura ?? CulturaDelSistema).TwoLetterISOLanguageName;
        return Idiomas.Any(i => i.Codigo == codigo) ? codigo : IdiomaDeReserva;
    }

    /// <summary>Normaliza lo guardado en los ajustes: nulo, vacío, «auto» o un idioma que no está, al del sistema.</summary>
    /// <param name="elegido">Lo que guardó el operador.</param>
    /// <returns>Código de dos letras de uno de los seis.</returns>
    public static string Resolver(string? elegido)
    {
        if (string.IsNullOrWhiteSpace(elegido)) return IdiomaDelSistema();
        var codigo = elegido.Trim().ToLowerInvariant();
        if (codigo.Length > 2 && codigo[2] == '-') codigo = codigo[..2];
        return Idiomas.Any(i => i.Codigo == codigo) ? codigo : IdiomaDelSistema();
    }

    /// <summary>
    /// La cultura de fechas y números para un idioma: la del sistema si habla ese idioma (un
    /// brasileño sigue viendo sus fechas), y si no la de fábrica del idioma.
    /// </summary>
    /// <param name="codigo">Código de dos letras.</param>
    /// <returns>La cultura, sin las personalizaciones de «Región» del usuario.</returns>
    public static CultureInfo CulturaPara(string codigo)
    {
        var idioma = Idiomas.FirstOrDefault(i => i.Codigo == codigo) ?? Idiomas[0];
        var nombre = FormatosDelSistema.TwoLetterISOLanguageName == idioma.Codigo && !FormatosDelSistema.IsNeutralCulture
            ? FormatosDelSistema.Name
            : idioma.CulturaDeFabrica;

        // useUserOverride en falso a propósito, como hasta ahora: si alguien puso el punto como
        // separador decimal en «Región», el cuaderno no tiene que escribir «20,000 contactos».
        return new CultureInfo(nombre, useUserOverride: false);
    }

    /// <summary>Cambia el idioma del programa y avisa a quien enseñe textos.</summary>
    /// <param name="elegido">Código («de»), nulo o «auto» para el del sistema.</param>
    public static void Cambiar(string? elegido) => Cambiar(CulturaPara(Resolver(elegido)));

    /// <summary>Cambia a una cultura concreta (pruebas y arranque).</summary>
    /// <param name="cultura">La cultura nueva.</param>
    public static void Cambiar(CultureInfo cultura)
    {
        ArgumentNullException.ThrowIfNull(cultura);
        if (Cultura.Name == cultura.Name) return;

        Cultura = cultura;
        IdiomaCambiado?.Invoke(null, EventArgs.Empty);

        Oyente[] copia;
        lock (Oyentes)
        {
            Oyentes.RemoveAll(o => !o.Dueno.IsAlive);
            copia = [.. Oyentes];
        }

        var hilo = Environment.CurrentManagedThreadId;
        foreach (var oyente in copia)
        {
            if (oyente.Dueno.Target is not { } vivo) continue;

            // A cada uno se le avisa en su hilo: un modelo de vista que rehace una colección
            // enlazada solo puede hacerlo desde el hilo de su ventana. Si se suscribió en este
            // mismo hilo, o en uno sin contexto, en el acto.
            if (oyente.Hilo == hilo || oyente.Contexto is null)
            {
                oyente.Accion(vivo);
            }
            else
            {
                var accion = oyente.Accion;
                oyente.Contexto.Post(static estado =>
                {
                    var (a, v) = ((Action<object>, object))estado!;
                    a(v);
                }, (accion, vivo));
            }
        }
    }

    /// <summary>
    /// Pide que se llame a <paramref name="accion"/> cada vez que cambie el idioma, sin retener a
    /// <paramref name="dueno"/>: cuando el dueño se recoge, la suscripción se va con él.
    /// </summary>
    /// <remarks>
    /// La acción tiene que ser <c>static</c> y trabajar sobre el dueño que recibe; si capturase al
    /// dueño, lo mantendría vivo para siempre. Se ejecuta en el hilo en que se suscribió (por su
    /// contexto de sincronización), así que puede tocar colecciones enlazadas a su ventana. Lo típico en un modelo de vista:
    /// <c>Textos.AlCambiar(this, static vm =&gt; vm.OnPropertyChanged(string.Empty));</c>
    /// </remarks>
    /// <typeparam name="T">Tipo del dueño.</typeparam>
    /// <param name="dueno">Quien enseña los textos.</param>
    /// <param name="accion">Qué hacer con él al cambiar de idioma.</param>
    public static void AlCambiar<T>(T dueno, Action<T> accion)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(dueno);
        ArgumentNullException.ThrowIfNull(accion);
        lock (Oyentes)
        {
            Oyentes.RemoveAll(o => !o.Dueno.IsAlive);
            Oyentes.Add(new Oyente(
                new WeakReference(dueno),
                o => accion((T)o),
                Environment.CurrentManagedThreadId,
                SynchronizationContext.Current));
        }
    }

    /// <summary>El texto de una clave en el idioma en uso.</summary>
    /// <param name="clave">Clave con su apartado delante, p. ej. <c>Comun.Aceptar</c>.</param>
    /// <returns>El texto; la propia clave si no existe.</returns>
    public static string T(string clave) => Buscar(clave, Cultura) ?? clave;

    /// <summary>Un texto con huecos (<c>{0}</c>, <c>{1:N0}</c>...) rellenos con la cultura en uso.</summary>
    /// <param name="clave">Clave del texto.</param>
    /// <param name="valores">Lo que va en los huecos.</param>
    /// <returns>El texto compuesto.</returns>
    public static string F(string clave, params object?[] valores) =>
        string.Format(Cultura, T(clave), valores);

    /// <summary>El texto de una clave en una cultura concreta, sin caer en la clave.</summary>
    /// <param name="clave">Clave con su apartado.</param>
    /// <param name="cultura">Cultura (cae al español si no hay traducción).</param>
    /// <returns>El texto, o nulo si la clave no existe.</returns>
    public static string? Buscar(string clave, CultureInfo cultura)
    {
        if (string.IsNullOrEmpty(clave)) return null;
        var punto = clave.IndexOf('.', StringComparison.Ordinal);
        if (punto <= 0 || !Gestores.TryGetValue(clave[..punto], out var gestor)) return null;

        try
        {
            return gestor.GetString(clave, cultura);
        }
        catch (MissingManifestResourceException)
        {
            return null;
        }
    }

    /// <summary>
    /// Las claves y textos de un apartado en un idioma, SIN caer al español (para las pruebas de
    /// que no falta nada).
    /// </summary>
    /// <param name="apartado">Nombre del apartado («Cabina»).</param>
    /// <param name="codigo">Código del idioma; «es» es el fichero de referencia.</param>
    /// <returns>Clave → texto; vacío si el fichero no existe.</returns>
    public static IReadOnlyDictionary<string, string> TextosDe(string apartado, string codigo)
    {
        var resultado = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!Gestores.TryGetValue(apartado, out var gestor)) return resultado;

        var cultura = codigo == "es" ? CultureInfo.InvariantCulture : new CultureInfo(codigo);
        ResourceSet? conjunto;
        try
        {
            conjunto = gestor.GetResourceSet(cultura, createIfNotExists: true, tryParents: false);
        }
        catch (MissingManifestResourceException)
        {
            return resultado;
        }

        if (conjunto is null) return resultado;
        foreach (System.Collections.DictionaryEntry entrada in conjunto)
        {
            if (entrada.Key is string clave && entrada.Value is string texto) resultado[clave] = texto;
        }

        return resultado;
    }
}
