using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>
/// Un texto de la configuración que puede venir ya escrito («QRZ.com») o como clave de los
/// recursos («Ajustes.Tarjeta.Cluster.Nombre»). Se resuelve cada vez que se pide, así que sigue
/// al idioma en uso.
/// </summary>
public static class TextoOClave
{
    /// <summary>El texto de la clave en el idioma en uso, o lo recibido tal cual si no es una clave.</summary>
    /// <param name="textoOClave">Texto ya escrito o clave con su apartado delante.</param>
    /// <returns>El texto que se enseña.</returns>
    public static string Resolver(string? textoOClave) =>
        string.IsNullOrEmpty(textoOClave)
            ? string.Empty
            : Textos.Buscar(textoOClave, Textos.Cultura) ?? textoOClave;
}

/// <summary>Como esta un servicio, para el color de su pastilla.</summary>
public enum EstadoDeServicio
{
    /// <summary>No hay nada puesto.</summary>
    SinConfigurar,

    /// <summary>Hay algo, pero falta lo necesario.</summary>
    AMedias,

    /// <summary>Tiene todo lo que necesita.</summary>
    Configurado,
}

/// <summary>
/// Un dato de la cuenta de un servicio que NO es secreto (el usuario, el apodo del QTH…), con
/// su rotulo. Lee y escribe en el modelo que lo guarda de verdad.
/// </summary>
public sealed class CampoDeCuenta : ObservableObject
{
    private readonly Func<string> _leer;
    private readonly Action<string> _escribir;
    private readonly string _rotulo;
    private readonly Func<string?>? _nota;

    /// <summary>Monta el campo.</summary>
    /// <param name="rotulo">Como se llama en pantalla: texto o clave de los recursos.</param>
    /// <param name="leer">De donde se lee.</param>
    /// <param name="escribir">Donde se escribe.</param>
    /// <param name="nota">
    /// Aclaracion corta (texto o clave), que se vuelve a pedir al cambiar de idioma; o nula.
    /// </param>
    public CampoDeCuenta(string rotulo, Func<string> leer, Action<string> escribir, Func<string?>? nota = null)
    {
        _rotulo = rotulo;
        _leer = leer ?? throw new ArgumentNullException(nameof(leer));
        _escribir = escribir ?? throw new ArgumentNullException(nameof(escribir));
        _nota = nota;

        Textos.AlCambiar(this, static c =>
        {
            c.OnPropertyChanged(nameof(Rotulo));
            c.OnPropertyChanged(nameof(Nota));
        });
    }

    /// <summary>Como se llama en pantalla.</summary>
    public string Rotulo => TextoOClave.Resolver(_rotulo);

    /// <summary>Aclaracion corta, para el globo de ayuda.</summary>
    public string Nota => TextoOClave.Resolver(_nota?.Invoke());

    /// <summary>Lo que vale ahora.</summary>
    public string Valor
    {
        get => _leer();
        set
        {
            if (string.Equals(value, _leer(), StringComparison.Ordinal)) return;
            _escribir(value ?? string.Empty);
            OnPropertyChanged();
        }
    }
}

/// <summary>
/// La tarjeta de un servicio en la configuración: sus datos de cuenta, sus secretos y su estado,
/// todo junto.
/// </summary>
/// <remarks>
/// <para>
/// Antes los secretos eran una lista larga hacia abajo —ocho casillas de contraseña seguidas—
/// y los usuarios de cada servicio estaban en otra lista, mucho más abajo. Para dejar LoTW
/// listo había que ir y venir tres veces. Ahora cada servicio es una tarjeta con todo lo suyo.
/// </para>
/// <para>
/// Los secretos siguen siendo <see cref="SecretoDeServicio"/>: se guardan igual, cifrados en
/// el mismo almacén, y no se vuelven a enseñar. La tarjeta solo los agrupa.
/// </para>
/// </remarks>
public sealed partial class TarjetaDeServicio : ObservableObject
{
    private readonly Func<(EstadoDeServicio Estado, string Texto)> _estado;
    private readonly Func<string>? _aviso;
    private readonly string _nombre;
    private readonly string _descripcion;
    private readonly string _textoDeProbar = "Ajustes.Tarjeta.ProbarLaConexion";
    private readonly string _nota = string.Empty;

    /// <summary>Monta la tarjeta.</summary>
    /// <param name="nombre">Nombre del servicio: nombre propio o clave de los recursos.</param>
    /// <param name="inicial">Letra de la chapa.</param>
    /// <param name="descripcion">Para qué sirve, en una línea (texto o clave).</param>
    /// <param name="estado">Cómo está: se vuelve a preguntar cuando cambia algo.</param>
    /// <param name="secretos">Sus secretos, o vacío.</param>
    /// <param name="campos">Sus datos de cuenta, o vacío.</param>
    /// <param name="aviso">Aviso vivo (por ejemplo, por qué no se puede subir), o nulo.</param>
    /// <param name="origenes">Modelos que, al cambiar, cambian el estado.</param>
    public TarjetaDeServicio(
        string nombre,
        string inicial,
        string descripcion,
        Func<(EstadoDeServicio Estado, string Texto)> estado,
        IReadOnlyList<SecretoDeServicio>? secretos = null,
        IReadOnlyList<CampoDeCuenta>? campos = null,
        Func<string>? aviso = null,
        params INotifyPropertyChanged?[] origenes)
    {
        _nombre = nombre;
        Inicial = inicial;
        _descripcion = descripcion;
        _estado = estado ?? throw new ArgumentNullException(nameof(estado));
        _aviso = aviso;
        Secretos = secretos ?? [];
        Campos = campos ?? [];

        foreach (var s in Secretos) s.PropertyChanged += (_, _) => Refrescar();
        foreach (var o in origenes)
        {
            if (o is not null) o.PropertyChanged += (_, _) => Refrescar();
        }

        // Nombre, descripción, estado y avisos se vuelven a escribir en el idioma nuevo.
        Textos.AlCambiar(this, static t => t.OnPropertyChanged(string.Empty));
    }

    /// <summary>Nombre del servicio.</summary>
    public string Nombre => TextoOClave.Resolver(_nombre);

    /// <summary>Letra de la chapa, al estilo de la marca.</summary>
    public string Inicial { get; }

    /// <summary>Para qué sirve.</summary>
    public string Descripcion => TextoOClave.Resolver(_descripcion);

    /// <summary>Sus secretos.</summary>
    public IReadOnlyList<SecretoDeServicio> Secretos { get; }

    /// <summary>Sus datos de cuenta, que no son secretos.</summary>
    public IReadOnlyList<CampoDeCuenta> Campos { get; }

    /// <summary>Hay datos de cuenta que enseñar.</summary>
    public bool HayCampos => Campos.Count > 0;

    /// <summary>Guarda los datos de cuenta. Nula si la tarjeta no tiene.</summary>
    public ICommand? GuardarCuenta { get; init; }

    /// <summary>Hay botón de guardar la cuenta.</summary>
    public bool SePuedeGuardarLaCuenta => GuardarCuenta is not null && HayCampos;

    /// <summary>Prueba la conexión o vuelve a comprobar. Nula si el servicio no tiene prueba.</summary>
    public ICommand? Probar { get; init; }

    /// <summary>Rótulo del botón de probar.</summary>
    public string TextoDeProbar
    {
        get => TextoOClave.Resolver(_textoDeProbar);
        init => _textoDeProbar = value;
    }

    /// <summary>Hay botón de probar.</summary>
    public bool HayPrueba => Probar is not null;

    /// <summary>Lleva al apartado donde se configura entero. Nula si todo cabe en la tarjeta.</summary>
    public ICommand? Configurar { get; init; }

    /// <summary>Hay botón de ir al apartado.</summary>
    public bool HayConfigurar => Configurar is not null;

    /// <summary>Aclaración fija que se lee ANTES de escribir (por ejemplo, la de TQSL).</summary>
    public string Nota
    {
        get => TextoOClave.Resolver(_nota);
        init => _nota = value;
    }

    /// <summary>Cómo está, para el color.</summary>
    public EstadoDeServicio Estado => _estado().Estado;

    /// <summary>Cómo está, escrito.</summary>
    public string EstadoTexto => _estado().Texto;

    /// <summary>Aviso vivo, o vacío.</summary>
    public string Aviso => _aviso?.Invoke() ?? string.Empty;

    /// <summary>Vuelve a mirar el estado y el aviso.</summary>
    public void Refrescar()
    {
        OnPropertyChanged(nameof(Estado));
        OnPropertyChanged(nameof(EstadoTexto));
        OnPropertyChanged(nameof(Aviso));
    }

    /// <summary>
    /// Estado a partir de los secretos: todos los necesarios guardados, alguno, o ninguno.
    /// </summary>
    /// <param name="necesarios">Los que hacen falta para que el servicio funcione.</param>
    /// <param name="todos">Todos los de la tarjeta.</param>
    /// <returns>El estado y su texto.</returns>
    public static (EstadoDeServicio, string) PorSecretos(
        IReadOnlyList<SecretoDeServicio> necesarios,
        IReadOnlyList<SecretoDeServicio> todos)
    {
        if (necesarios.Count > 0 && necesarios.All(s => s.Guardado)) return (EstadoDeServicio.Configurado, Textos.T("Ajustes.Estado.Configurada"));
        if (todos.Any(s => s.Guardado)) return (EstadoDeServicio.AMedias, Textos.T("Ajustes.Estado.Incompleta"));
        return (EstadoDeServicio.SinConfigurar, Textos.T("Ajustes.Estado.SinConfigurar"));
    }
}
