using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Nodisla.Cuaderno.Ui.Telegrafia;

/// <summary>Una macro de telegrafia: la tecla F que la manda, su rotulo y su texto.</summary>
public sealed partial class MacroCw : ObservableObject
{
    /// <summary>Crea la macro.</summary>
    /// <param name="numero">De 1 a 12: la tecla F.</param>
    /// <param name="rotulo">Lo que pone en el boton.</param>
    /// <param name="texto">El texto, con variables.</param>
    public MacroCw(int numero, string rotulo, string texto)
    {
        Numero = numero;
        _rotulo = rotulo;
        _texto = texto;
    }

    /// <summary>De 1 a 12.</summary>
    public int Numero { get; }

    /// <summary>«F1»…«F12».</summary>
    public string Tecla => $"F{Numero}";

    /// <summary>Lo que pone en el boton.</summary>
    [ObservableProperty]
    private string _rotulo;

    /// <summary>El texto, con variables como <c>{CALL}</c> y cambios de velocidad como <c>&lt;+5&gt;</c>.</summary>
    [ObservableProperty]
    private string _texto;

    /// <summary>Es la que toca ahora en la secuencia (se resalta).</summary>
    [ObservableProperty]
    private bool _esLaQueToca;
}

/// <summary>El paso de un contacto en el que se esta.</summary>
public enum PasoCw
{
    /// <summary>Nada en marcha.</summary>
    Libre,

    /// <summary>Llamando CQ (o, buscando, el CQ del otro).</summary>
    Cq,

    /// <summary>La respuesta a una llamada: el otro a mi CQ, o yo al suyo.</summary>
    Respuesta,

    /// <summary>El informe (RST, y en concurso el numero).</summary>
    Informe,

    /// <summary>La confirmacion del informe recibido (R, TU).</summary>
    Confirmacion,

    /// <summary>La despedida: el contacto queda completo.</summary>
    SetentaYTres,
}

/// <summary>Que macros manda cada paso de la secuencia. Numeros de tecla (1 = F1).</summary>
public sealed class PasosDeMacros
{
    /// <summary>Llamar CQ.</summary>
    public int[] Cq { get; set; } = [];

    /// <summary>Contestar al CQ de otro (buscando).</summary>
    public int[] Respuesta { get; set; } = [];

    /// <summary>El informe cuando llamo yo (al que me ha contestado).</summary>
    public int[] InformeLlamando { get; set; } = [];

    /// <summary>El informe cuando busco (con la R del suyo).</summary>
    public int[] InformeBuscando { get; set; } = [];

    /// <summary>La confirmacion que cierra el contacto cuando llamo yo.</summary>
    public int[] Confirmacion { get; set; } = [];

    /// <summary>La despedida que cierra el contacto cuando busco. Vacio: se cierra con el informe.</summary>
    public int[] SetentaYTres { get; set; } = [];

    /// <summary>Copia.</summary>
    /// <returns>Otra igual.</returns>
    public PasosDeMacros Copiar() => new()
    {
        Cq = [.. Cq],
        Respuesta = [.. Respuesta],
        InformeLlamando = [.. InformeLlamando],
        InformeBuscando = [.. InformeBuscando],
        Confirmacion = [.. Confirmacion],
        SetentaYTres = [.. SetentaYTres],
    };
}

/// <summary>Una macro tal y como se guarda.</summary>
/// <param name="Rotulo">El rotulo.</param>
/// <param name="Texto">El texto.</param>
public sealed record MacroGuardada(string Rotulo, string Texto);

/// <summary>Un juego de doce macros con su secuencia: el de QSO normal o el de concurso.</summary>
public sealed class JuegoDeMacrosGuardado
{
    /// <summary>Las doce macros, de F1 a F12.</summary>
    public List<MacroGuardada> Macros { get; set; } = [];

    /// <summary>Que macros manda cada paso.</summary>
    public PasosDeMacros Pasos { get; set; } = new();

    /// <summary>Es de concurso: Intro hace lo de N1MM (ESM) y el contacto se cierra antes.</summary>
    public bool Concurso { get; set; }
}

/// <summary>
/// Lo que se guarda de la transmision de telegrafia: velocidad, macros y numero de serie.
/// </summary>
/// <remarks>
/// Va en su propio fichero, <c>transmision-cw.json</c> en la carpeta de datos, y no en los
/// ajustes generales: asi se exporta y se importa tal cual. <b>El pestillo de transmitir no se
/// guarda aqui ni en ningun sitio</b>, ni el modo automatico: cada arranque empiezan cerrados.
/// </remarks>
public sealed class AjustesDeTransmisionCw
{
    /// <summary>Nombre del fichero en la carpeta de datos.</summary>
    public const string Fichero = "transmision-cw.json";

    private static readonly JsonSerializerOptions Formato = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Velocidad de partida (WPM).</summary>
    public int Wpm { get; set; } = 22;

    /// <summary>Juego en uso: falso el de QSO normal, verdadero el de concurso.</summary>
    public bool UsarConcurso { get; set; }

    /// <summary>El juego de QSO normal.</summary>
    public JuegoDeMacrosGuardado Normal { get; set; } = JuegosDeFabrica.Normal();

    /// <summary>El juego de concurso.</summary>
    public JuegoDeMacrosGuardado Concurso { get; set; } = JuegosDeFabrica.Concurso();

    /// <summary>Nombre del operador, para <c>{MINOMBRE}</c>.</summary>
    public string MiNombre { get; set; } = string.Empty;

    /// <summary>QTH propio, para <c>{MIQTH}</c>.</summary>
    public string MiQth { get; set; } = string.Empty;

    /// <summary>Siguiente numero de serie de concurso (<c>{NR}</c>).</summary>
    public int SiguienteNumero { get; set; } = 1;

    /// <summary>Tope de una transmision de telegrafia, en segundos.</summary>
    public int TiempoMaximoSegundos { get; set; } = 120;

    /// <summary>Lo escrito sale palabra a palabra mientras se teclea.</summary>
    public bool EscribirMientrasSeEnvia { get; set; }

    /// <summary>Segundos de silencio tras los que el automatico repite lo ultimo.</summary>
    public int SegundosSinRespuesta { get; set; } = 12;

    /// <summary>Repeticiones del automatico antes de rendirse.</summary>
    public int RepeticionesMaximas { get; set; } = 3;

    /// <summary>Lo deja todo dentro de limites con sentido.</summary>
    /// <returns>Los mismos ajustes.</returns>
    public AjustesDeTransmisionCw Acotar()
    {
        Wpm = Math.Clamp(Wpm, 5, 60);
        SiguienteNumero = Math.Clamp(SiguienteNumero, 1, 9999);
        TiempoMaximoSegundos = Math.Clamp(TiempoMaximoSegundos, 10, 180);
        SegundosSinRespuesta = Math.Clamp(SegundosSinRespuesta, 4, 60);
        RepeticionesMaximas = Math.Clamp(RepeticionesMaximas, 1, 10);
        Normal = Completar(Normal, JuegosDeFabrica.Normal());
        Concurso = Completar(Concurso, JuegosDeFabrica.Concurso());
        MiNombre ??= string.Empty;
        MiQth ??= string.Empty;
        return this;
    }

    /// <summary>Lee los ajustes de la carpeta de datos; sin fichero o con uno roto, los de fabrica.</summary>
    /// <param name="carpeta">Carpeta de datos.</param>
    /// <returns>Los ajustes.</returns>
    public static AjustesDeTransmisionCw Leer(string? carpeta)
    {
        if (string.IsNullOrEmpty(carpeta)) return new AjustesDeTransmisionCw().Acotar();
        var ruta = Path.Combine(carpeta, Fichero);
        try
        {
            return File.Exists(ruta) ? Desde(File.ReadAllText(ruta, Encoding.UTF8)) : new AjustesDeTransmisionCw().Acotar();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Serilog.Log.Warning(ex, "No se han podido leer los ajustes de la telegrafía; se usan los de fábrica.");
            return new AjustesDeTransmisionCw().Acotar();
        }
    }

    /// <summary>Lee unos ajustes de un texto JSON.</summary>
    /// <param name="json">El texto.</param>
    /// <returns>Los ajustes, ya acotados.</returns>
    /// <exception cref="JsonException">Si el texto no es un fichero de macros.</exception>
    public static AjustesDeTransmisionCw Desde(string json) =>
        (JsonSerializer.Deserialize<AjustesDeTransmisionCw>(json, Formato) ?? throw new JsonException("Fichero vacío.")).Acotar();

    /// <summary>Lo deja en JSON.</summary>
    /// <returns>El texto.</returns>
    public string AJson() => JsonSerializer.Serialize(this, Formato);

    /// <summary>Guarda en la carpeta de datos.</summary>
    /// <param name="carpeta">Carpeta de datos.</param>
    public void Guardar(string carpeta)
    {
        Directory.CreateDirectory(carpeta);
        var ruta = Path.Combine(carpeta, Fichero);
        var temporal = ruta + ".tmp";
        File.WriteAllText(temporal, AJson(), new UTF8Encoding(false));
        File.Move(temporal, ruta, overwrite: true);
    }

    private static JuegoDeMacrosGuardado Completar(JuegoDeMacrosGuardado? juego, JuegoDeMacrosGuardado fabrica)
    {
        if (juego is null) return fabrica;
        juego.Macros ??= [];
        for (var i = juego.Macros.Count; i < 12; i++) juego.Macros.Add(fabrica.Macros[i]);
        if (juego.Macros.Count > 12) juego.Macros = juego.Macros.Take(12).ToList();
        juego.Macros = juego.Macros.Select(m => new MacroGuardada(m?.Rotulo ?? string.Empty, m?.Texto ?? string.Empty)).ToList();
        juego.Pasos ??= fabrica.Pasos.Copiar();
        static int[] Validas(int[]? teclas) => (teclas ?? []).Where(t => t is >= 1 and <= 12).ToArray();
        juego.Pasos.Cq = Validas(juego.Pasos.Cq);
        juego.Pasos.Respuesta = Validas(juego.Pasos.Respuesta);
        juego.Pasos.InformeLlamando = Validas(juego.Pasos.InformeLlamando);
        juego.Pasos.InformeBuscando = Validas(juego.Pasos.InformeBuscando);
        juego.Pasos.Confirmacion = Validas(juego.Pasos.Confirmacion);
        juego.Pasos.SetentaYTres = Validas(juego.Pasos.SetentaYTres);
        return juego;
    }
}

/// <summary>Los dos juegos de macros de fabrica.</summary>
/// <remarks>
/// Los rotulos de fabrica son los de la telegrafia, que no se traducen: CQ, TU, 73, AGN… Los
/// textos siguen la costumbre de la banda; se pueden cambiar todos.
/// </remarks>
public static class JuegosDeFabrica
{
    /// <summary>QSO normal, el de siempre: CQ, respuesta, informe con nombre y QTH, confirmacion y 73.</summary>
    /// <returns>El juego.</returns>
    public static JuegoDeMacrosGuardado Normal() => new()
    {
        Concurso = false,
        Macros =
        [
            new("CQ", "CQ CQ CQ DE {MICALL} {MICALL} {MICALL} K"),
            new("DE", "{CALL} DE {MICALL} {MICALL} K"),
            new("RST", "{CALL} DE {MICALL} TNX FER CALL UR RST {RST} {RST} NAME {MINOMBRE} {MINOMBRE} QTH {MIQTH} HW? {CALL} DE {MICALL} K"),
            new("R TU", "{CALL} DE {MICALL} R R TNX {NOMBRE} UR {RST} FB QSO 73 {CALL} DE {MICALL} <SK>"),
            new("73", "R TNX {NOMBRE} 73 GL {CALL} DE {MICALL} <SK> E E"),
            new("R RST", "{CALL} DE {MICALL} R TNX FER RPRT UR RST {RST} {RST} NAME {MINOMBRE} QTH {MIQTH} HW? {CALL} DE {MICALL} K"),
            new("?", "{?}"),
            new("AGN", "{AGN} {AGN}"),
            new("QRZ?", "QRZ? DE {MICALL} K"),
            new("MI", "{MICALL}"),
            new("QRS", "<-5>PSE QRS {MICALL}"),
            new("QRL?", "QRL?"),
        ],
        Pasos = new PasosDeMacros
        {
            Cq = [1],
            Respuesta = [2],
            InformeLlamando = [3],
            InformeBuscando = [6],
            Confirmacion = [4],
            SetentaYTres = [5],
        },
    };

    /// <summary>Concurso, al estilo de N1MM: CQ TEST, intercambio con 5NN y numero, TU.</summary>
    /// <returns>El juego.</returns>
    public static JuegoDeMacrosGuardado Concurso() => new()
    {
        Concurso = true,
        Macros =
        [
            new("CQ", "{CQ} {MICALL} {MICALL} TEST"),
            new("EXCH", "{RST} {NR}"),
            new("TU", "{TU} {MICALL}"),
            new("MI", "{MICALL}"),
            new("CALL", "{CALL}"),
            new("RPT", "{RST} {NR} {NR}"),
            new("?", "{?}"),
            new("AGN", "{AGN}"),
            new("NR?", "NR?"),
            new("CL?", "CL?"),
            new("QRL?", "QRL?"),
            new("QRS", "<WPM 18>{MICALL} {MICALL}"),
        ],
        Pasos = new PasosDeMacros
        {
            Cq = [1],
            Respuesta = [4],
            InformeLlamando = [5, 2],
            InformeBuscando = [2],
            Confirmacion = [3],
            SetentaYTres = [],
        },
    };
}
