using System.Text.RegularExpressions;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Ui.Digital;

namespace Nodisla.Cuaderno.Ui.Telegrafia;

/// <summary>Quien llama: yo (CQ, «run») o el otro (yo busco y contesto, «S&amp;P»).</summary>
public enum PapelCw
{
    /// <summary>Llamo yo CQ.</summary>
    Llamo,

    /// <summary>Busco y contesto al CQ de otro.</summary>
    Busco,
}

/// <summary>Lo que queda de un contacto de telegrafia al completarlo.</summary>
/// <param name="Indicativo">El corresponsal.</param>
/// <param name="RstRecibido">El RST que me dio (vacio si no se leyo).</param>
/// <param name="NumeroRecibido">Su numero de serie, en concurso.</param>
/// <param name="Nombre">Su nombre, si lo dijo.</param>
/// <param name="Qth">Su QTH, si lo dijo.</param>
/// <param name="Localizador">Su localizador, si lo dijo.</param>
public sealed record DatosDelQsoCw(
    string Indicativo,
    string RstRecibido,
    string NumeroRecibido,
    string Nombre,
    string Qth,
    string Localizador);

/// <summary>
/// La secuencia de un contacto de telegrafia, como la del FT8 del modem propio:
/// CQ → respuesta → informe → confirmacion → 73.
/// </summary>
/// <remarks>
/// <para>
/// <b>No transmite nada.</b> Decide que toca mandar y avanza con lo que lee el decodificador:
/// el modelo de la pantalla es quien manda, y siempre por el pestillo, la pregunta y el
/// vigilante.
/// </para>
/// <para>
/// Lo que se oye se le da palabra a palabra con <see cref="Oir"/>; al acabar la pasada del otro
/// (un <c>K</c>, <c>KN</c>, <c>&lt;AR&gt;</c>… o un silencio) se llama a
/// <see cref="TerminarPasada"/>, que la mira entera y decide. Asi nunca se contesta a media
/// pasada, encima del otro.
/// </para>
/// <list type="bullet">
/// <item><b>Llamo</b>: CQ → (me contesta X: «EA8DLF DE X», «DE X» o «X») → informe →
/// (su informe: RST, nombre, QTH; en concurso RST y numero) → confirmacion, y completo.</item>
/// <item><b>Busco</b>: (su CQ) → respuesta → (su informe a mi) → informe con R →
/// (su R/TU/73) → 73, y completo. En concurso se completa con el informe.</item>
/// </list>
/// <para>Un «?», «AGN», «RPT», «NR?»… hace repetir lo ultimo.</para>
/// </remarks>
public sealed partial class SecuenciadorCw
{
    private static readonly HashSet<string> FinesDePasada = new(StringComparer.Ordinal)
    {
        "K", "KN", "<KN>", "BK", "<BK>", "AR", "<AR>", "+", "SK", "<SK>", "<VA>",
    };

    private static readonly HashSet<string> Repetir = new(StringComparer.Ordinal)
    {
        "?", "AGN", "AGN?", "RPT", "RPT?", "NR?", "CL?", "CALL?", "QRZ?",
    };

    private static readonly HashSet<string> Despedidas = new(StringComparer.Ordinal)
    {
        "TU", "73", "R", "RR", "QSL", "SK", "<SK>", "<VA>", "EE", "GL", "TNX", "FB",
    };

    private static readonly HashSet<string> Claves = new(StringComparer.Ordinal)
    {
        "NAME", "OP", "NM", "QTH", "RST", "UR", "HW", "HW?", "DE", "K", "KN", "<KN>", "BK", "<AR>", "AR", "LOC", "QRA",
        "GRID", "WWL", "TNX", "TU", "73", "ES", "=", "<BT>", "BT", "IS", "R", "FER", "CALL", "RIG", "ANT", "WX", "PWR",
    };

    private readonly List<string> _pasada = [];

    /// <summary>El indicativo propio.</summary>
    public string MiIndicativo { get; set; } = string.Empty;

    /// <summary>
    /// El contacto se cierra con el informe cuando busco (concurso: no hay 73).
    /// </summary>
    public bool CierraConElInforme { get; set; }

    /// <summary>Es concurso: del informe se lee tambien el numero de serie.</summary>
    public bool Concurso { get; set; }

    /// <summary>Quien llama en este contacto.</summary>
    public PapelCw Papel { get; private set; } = PapelCw.Llamo;

    /// <summary>Lo ultimo que se mando.</summary>
    public PasoCw Paso { get; private set; } = PasoCw.Libre;

    /// <summary>Lo que toca mandar ahora.</summary>
    public PasoCw Siguiente { get; private set; } = PasoCw.Cq;

    /// <summary>El corresponsal, en cuanto se sabe.</summary>
    public string Corresponsal { get; private set; } = string.Empty;

    /// <summary>El RST que me ha dado.</summary>
    public string RstRecibido { get; private set; } = string.Empty;

    /// <summary>Su numero de serie (concurso).</summary>
    public string NumeroRecibido { get; private set; } = string.Empty;

    /// <summary>Su nombre.</summary>
    public string Nombre { get; private set; } = string.Empty;

    /// <summary>Su QTH.</summary>
    public string Qth { get; private set; } = string.Empty;

    /// <summary>Su localizador.</summary>
    public string Localizador { get; private set; } = string.Empty;

    /// <summary>Veces seguidas que se ha repetido lo mismo sin respuesta.</summary>
    public int Repeticiones { get; private set; }

    /// <summary>El contacto en curso esta completo.</summary>
    public bool Completo { get; private set; }

    /// <summary>Se ha sabido quien es el corresponsal.</summary>
    public event EventHandler<string>? CorresponsalEncontrado;

    /// <summary>Han llegado datos suyos (RST, nombre, QTH…).</summary>
    public event EventHandler? DatosRecibidos;

    /// <summary>El contacto queda completo: listo para el cuaderno.</summary>
    public event EventHandler<DatosDelQsoCw>? ContactoCompleto;

    /// <summary>Empieza un contacto nuevo.</summary>
    /// <param name="papel">Llamo yo o busco.</param>
    /// <param name="indicativo">El corresponsal, si ya se sabe (buscando, el del CQ).</param>
    public void Empezar(PapelCw papel, string? indicativo = null)
    {
        Papel = papel;
        Paso = PasoCw.Libre;
        Corresponsal = Normalizar(indicativo);
        RstRecibido = NumeroRecibido = Nombre = Qth = Localizador = string.Empty;
        Repeticiones = 0;
        Completo = false;
        _pasada.Clear();
        Siguiente = papel == PapelCw.Llamo ? PasoCw.Cq : PasoCw.Respuesta;
    }

    /// <summary>Pone el corresponsal a mano (el operador lo ha escrito o pulsado).</summary>
    /// <param name="indicativo">El indicativo.</param>
    public void PonerCorresponsal(string? indicativo)
    {
        var nuevo = Normalizar(indicativo);
        if (nuevo == Corresponsal) return;
        Corresponsal = nuevo;
        if (Paso is PasoCw.Libre or PasoCw.Cq && nuevo.Length > 0)
        {
            // Con el indicativo puesto, llamando CQ toca el informe; sin nada en marcha y
            // buscando, la respuesta.
            Siguiente = Paso == PasoCw.Cq || Papel == PapelCw.Llamo ? PasoCw.Informe : PasoCw.Respuesta;
        }
    }

    /// <summary>Se acaba de mandar lo de un paso.</summary>
    /// <param name="paso">El paso que se mando.</param>
    public void AlEnviar(PasoCw paso)
    {
        Repeticiones = paso == Paso && !Completo ? Repeticiones + 1 : 0;
        Paso = paso;
        _pasada.Clear();

        switch (paso)
        {
            case PasoCw.Cq:
                Papel = PapelCw.Llamo;
                Completo = false;
                Siguiente = PasoCw.Cq;
                break;
            case PasoCw.Respuesta:
                Papel = PapelCw.Busco;
                Siguiente = PasoCw.Respuesta;
                break;
            case PasoCw.Informe:
                if (Papel == PapelCw.Busco && CierraConElInforme) Completar();
                else Siguiente = PasoCw.Informe;
                break;
            case PasoCw.Confirmacion:
            case PasoCw.SetentaYTres:
                Completar();
                break;
        }
    }

    /// <summary>Una palabra mas de la pasada del otro.</summary>
    /// <param name="palabra">La palabra, tal cual la da el decodificador.</param>
    /// <returns>Verdadero si la palabra cierra la pasada (<c>K</c>, <c>KN</c>, <c>&lt;AR&gt;</c>…).</returns>
    public bool Oir(string palabra)
    {
        ArgumentNullException.ThrowIfNull(palabra);
        var p = palabra.Trim().ToUpperInvariant();
        if (p.Length == 0) return false;
        _pasada.Add(p);
        return FinesDePasada.Contains(p);
    }

    /// <summary>Lo que se lleva oido de la pasada.</summary>
    public string PasadaEnCurso => string.Join(' ', _pasada);

    /// <summary>
    /// Mira la pasada del otro entera y avanza.
    /// </summary>
    /// <returns>Verdadero si ahora toca mandar algo por lo que se ha oido.</returns>
    public bool TerminarPasada()
    {
        var pasada = _pasada.ToList();
        _pasada.Clear();
        if (pasada.Count == 0 || Paso == PasoCw.Libre && Papel == PapelCw.Llamo) return false;
        if (Completo && Paso is not PasoCw.Cq) return false;

        var mio = Normalizar(MiIndicativo);
        if (OtroContacto(pasada, mio)) return false;

        if (pasada.Any(EsPeticionDeRepetir) && Paso != PasoCw.Cq)
        {
            Siguiente = Paso;
            return true;
        }

        switch (Paso)
        {
            case PasoCw.Cq:
                return OirRespuestaAMiCq(pasada, mio);

            case PasoCw.Respuesta:
                // Buscando: su informe, dirigido a mi.
                if (!pasada.Any(p => EsElMio(p, mio))) return false;
                if (!LeerInforme(pasada)) return false;
                Siguiente = PasoCw.Informe;
                return true;

            case PasoCw.Informe when Papel == PapelCw.Llamo:
                if (!LeerInforme(pasada)) return false;
                Siguiente = PasoCw.Confirmacion;
                return true;

            case PasoCw.Informe:
                LeerInforme(pasada);
                if (!pasada.Any(Despedidas.Contains)) return false;
                Siguiente = PasoCw.SetentaYTres;
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Ha pasado el tiempo sin respuesta: toca repetir lo ultimo, o rendirse.
    /// </summary>
    /// <param name="maximo">Repeticiones que se permiten.</param>
    /// <returns>Verdadero si hay que repetir; falso si ya se ha repetido bastante.</returns>
    public bool Silencio(int maximo)
    {
        if (Paso == PasoCw.Libre || Completo && Paso != PasoCw.Cq) return false;
        if (Repeticiones >= maximo) return false;
        Siguiente = Paso;
        return true;
    }

    private bool OirRespuestaAMiCq(List<string> pasada, string mio)
    {
        // El CQ de otro no es una respuesta al mio.
        if (pasada.Contains("CQ") || pasada.Contains("TEST")) return false;

        var indicativos = pasada.Select(Normalizar).Where(p => p.Length > 0 && p != mio && PalabrasCw.EsIndicativo(p)).ToList();
        if (indicativos.Count == 0) return false;

        // «… DE X»: el que va detras del DE. Si no, el primero que se oyo.
        var de = pasada.LastIndexOf("DE");
        var elegido = de >= 0 && de + 1 < pasada.Count && PalabrasCw.EsIndicativo(Normalizar(pasada[de + 1])) && Normalizar(pasada[de + 1]) != mio
            ? Normalizar(pasada[de + 1])
            : indicativos[0];

        Completo = false;
        RstRecibido = NumeroRecibido = Nombre = Qth = Localizador = string.Empty;
        Corresponsal = elegido;
        Papel = PapelCw.Llamo;
        Repeticiones = 0;
        Siguiente = PasoCw.Informe;
        CorresponsalEncontrado?.Invoke(this, elegido);
        return true;
    }

    /// <summary>
    /// Lee de una pasada el RST, el numero (en concurso), el nombre, el QTH y el localizador.
    /// </summary>
    /// <returns>Verdadero si traia RST.</returns>
    private bool LeerInforme(List<string> pasada)
    {
        var hubo = false;
        for (var i = 0; i < pasada.Count; i++)
        {
            var p = pasada[i];
            if (RstRecibido.Length == 0 && Rst().IsMatch(p))
            {
                RstRecibido = p.Replace('N', '9').Replace('T', '0');
                hubo = true;
                if (Concurso && i + 1 < pasada.Count && NumeroDeSerie().IsMatch(pasada[i + 1]))
                {
                    NumeroRecibido = Cortados(pasada[i + 1]);
                }

                continue;
            }

            if (Rst().IsMatch(p) && RstRecibido.Length > 0)
            {
                // Repetido («599 599»): ya se tiene.
                hubo = true;
                continue;
            }

            if (p is "NAME" or "OP" or "NM" && Tras(pasada, i) is { } nombre)
            {
                Nombre = nombre;
            }
            else if (p == "QTH")
            {
                var qth = pasada.Skip(i + 1).SkipWhile(x => x == "IS").TakeWhile(x => !Claves.Contains(x)).Take(3).ToList();
                if (qth.Count > 0) Qth = string.Join(' ', qth);
            }
            else if (p is "LOC" or "QRA" or "GRID" or "WWL" && Tras(pasada, i) is { } loc && Locator.TryParse(loc, out _))
            {
                Localizador = loc;
            }
        }

        if (hubo || Nombre.Length > 0 || Qth.Length > 0) DatosRecibidos?.Invoke(this, EventArgs.Empty);
        return hubo;

        static string? Tras(List<string> pasada, int i)
        {
            var resto = pasada.Skip(i + 1).SkipWhile(x => x == "IS").FirstOrDefault();
            return resto is null || Claves.Contains(resto) ? null : resto;
        }
    }

    private void Completar()
    {
        if (Completo) return;
        Completo = true;
        Siguiente = Papel == PapelCw.Llamo ? PasoCw.Cq : PasoCw.Libre;
        Repeticiones = 0;
        if (Corresponsal.Length == 0) return;
        ContactoCompleto?.Invoke(this, new DatosDelQsoCw(Corresponsal, RstRecibido, NumeroRecibido, Nombre, Qth, Localizador));
    }

    /// <summary>«Y DE X» con Y que no soy yo: el otro esta con otro.</summary>
    private bool OtroContacto(List<string> pasada, string mio)
    {
        var de = pasada.IndexOf("DE");
        if (de <= 0) return false;
        var destinatario = Normalizar(pasada[de - 1]);
        if (!PalabrasCw.EsIndicativo(destinatario) || EsElMio(destinatario, mio)) return false;

        // Llamando CQ, «Y DE X» con Y = X es el que se repite: no es otro contacto.
        var emisor = de + 1 < pasada.Count ? Normalizar(pasada[de + 1]) : string.Empty;
        return destinatario != emisor;
    }

    private static bool EsElMio(string palabra, string mio)
    {
        if (mio.Length == 0) return false;
        var p = Normalizar(palabra);
        return p == mio || p.Split('/').Contains(mio, StringComparer.Ordinal);
    }

    private static bool EsPeticionDeRepetir(string p) =>
        Repetir.Contains(p) || (p.Length > 1 && p.EndsWith('?') && p is not "HW?" and not "QRL?");

    private static string Normalizar(string? indicativo) =>
        string.IsNullOrWhiteSpace(indicativo) ? string.Empty : indicativo.Trim().ToUpperInvariant();

    /// <summary>Numeros cortados de concurso: T = 0, N = 9, O = 0, A = 1, E = 5.</summary>
    private static string Cortados(string p) =>
        p.Replace('T', '0').Replace('O', '0').Replace('N', '9').Replace('A', '1').Replace('E', '5');

    [GeneratedRegex(@"^[1-5][1-9N][1-9N]$", RegexOptions.CultureInvariant)]
    private static partial Regex Rst();

    [GeneratedRegex(@"^(?=.*[0-9T])[0-9TNOAE]{1,4}$", RegexOptions.CultureInvariant)]
    private static partial Regex NumeroDeSerie();
}
