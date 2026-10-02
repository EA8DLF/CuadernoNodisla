using System.Globalization;
using System.Text;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Modos.Ft8;

/// <summary>Familia a la que pertenece un mensaje de 77 bits.</summary>
public enum TipoDeMensaje
{
    /// <summary>No se pudo reconocer; no vale para apuntar nada.</summary>
    NoSoportado,
    /// <summary>Mensaje corriente: a quien, quien y localizador o informe.</summary>
    Normal,
    /// <summary>Trece caracteres libres.</summary>
    TextoLibre,
    /// <summary>Lleva un indicativo que no cabe en el formato corriente.</summary>
    IndicativoNoEstandar,
    /// <summary>Telemetria: 71 bits en crudo.</summary>
    Telemetria,
}

/// <summary>Un mensaje ya desempaquetado y entendido.</summary>
/// <param name="Texto">Mensaje escrito como lo ensena el programa.</param>
/// <param name="Tipo">Familia del mensaje.</param>
public sealed record MensajeDescifrado(string Texto, TipoDeMensaje Tipo)
{
    /// <summary>Quien emite, si se pudo sacar con seguridad.</summary>
    public Indicativo Llamante { get; init; }

    /// <summary>A quien se llama. Una llamada general no tiene llamado.</summary>
    public Indicativo Llamado { get; init; }

    /// <summary>Localizador que viaja en el mensaje, si lo lleva.</summary>
    public Locator Locator { get; init; }

    /// <summary>Es una llamada general.</summary>
    public bool EsCq { get; init; }

    /// <summary>Informe de senal que lleva el mensaje, si lleva alguno.</summary>
    public int? Informe { get; init; }

    /// <summary>
    /// El mensaje traia un indicativo resumido que no consta en el catalogo.
    /// </summary>
    /// <remarks>
    /// Cuando esto es cierto, el mensaje se puede ensenar pero <b>no se puede apuntar</b>: el
    /// indicativo que falta es justo el dato del contacto.
    /// </remarks>
    public bool TieneIndicativoSinResolver { get; init; }
}

/// <summary>
/// Empaqueta y desempaqueta los 77 bits que van dentro de cada mensaje de FT8 y FT4.
/// </summary>
/// <remarks>
/// <para>
/// Setenta y siete bits para decir «CQ EA8DLF IL18» parece poco y sin embargo sobra, porque el
/// protocolo no manda texto: manda <b>numeros de catalogo</b>. El indicativo no viaja letra a
/// letra sino como el numero de orden que le toca entre todos los indicativos posibles con
/// forma corriente, que son unos doscientos sesenta millones y caben en 28 bits. El localizador
/// de cuatro caracteres es el numero de cuadro entre 32400, y cabe en 15. Asi es como entra un
/// contacto entero en menos de diez bytes.
/// </para>
/// <para>
/// El precio de esa compresion es que <b>cualquier cadena de 77 bits significa algo</b>. No hay
/// manera de mirar los bits y saber si son un mensaje o ruido: siempre saldra un indicativo con
/// buena pinta. Quien decide es el CRC, que se comprueba antes de llegar aqui. Este codigo da
/// por hecho que los bits ya pasaron el CRC, y aun asi rechaza lo que no entiende en vez de
/// aproximar.
/// </para>
/// <para>
/// Los formatos que se manejan son los que se usan en el dia a dia: el mensaje corriente, el
/// texto libre de trece caracteres, el de indicativos raros y la telemetria. Los de concurso
/// —campo de dia, vuelta de RTTY, VHF europeo, expediciones— se reconocen como no soportados y
/// se descartan, porque decodificarlos a medias seria peor que no decodificarlos.
/// </para>
/// </remarks>
public static class MensajeDe77Bits
{
    /// <summary>Bits que ocupa el mensaje.</summary>
    public const int Bits = 77;

    // Posiciones de los campos dentro de los 77 bits, contando desde el mas significativo.
    private const int PosicionDeI3 = 74;
    private const int PosicionDeN3 = 71;

    /// <summary>Cuadros de localizador de cuatro caracteres: 18 por 18 campos por 10 por 10.</summary>
    private const int CuadrosDeLocalizador = 32400;

    /// <summary>
    /// Desde donde se cuentan los informes dentro del campo de 15 bits.
    /// </summary>
    /// <remarks>
    /// Los valores 0 a 32399 son los localizadores. El 32400 no se usa; despues vienen los
    /// valores con nombre —32401 vacio, 32402 RRR, 32403 RR73, 32404 73— y a partir de ahi los
    /// informes: <c>32400 + 35 + dB</c>, de modo que −30 dB es 32405. <b>Contrastado bit a bit
    /// con los vectores de ft8_lib</b>: antes estaba corrido uno (vacio en 32400, informes desde
    /// 32434) y los demas programas leian RRR donde se mandaba RR73, RR73 donde se mandaba 73 y
    /// un decibelio menos en cada informe; y al reves en lo que se recibia.
    /// </remarks>
    private const int BaseDelInforme = CuadrosDeLocalizador + 35;

    /// <summary>Informe mas bajo que cabe en el campo.</summary>
    public const int InformeMinimo = -30;

    /// <summary>Informe mas alto que cabe en el campo.</summary>
    public const int InformeMaximo = 99;

    // Los tres numeros de la particion del campo de 28 bits que identifica a una estacion.
    // NumeroDeSenales + ResumenesDe22 + IndicativosCorrientes suman exactamente 2^28, y esa
    // suma es la comprobacion de que las tres constantes estan bien:
    //   2063592 + 4194304 + 37*36*10*27*27*27 = 268435456 = 2^28
    private const int NumeroDeSenales = 2063592;
    private const int ResumenesDe22 = 1 << 22;

    private const string AlfabetoLibre = " 0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ+-./?";
    private const string AlfabetoPrimero = " 0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string AlfabetoSegundo = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string AlfabetoDigito = "0123456789";
    private const string AlfabetoSufijo = " ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string AlfabetoDeIndicativoLargo = " 0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ/";

    /// <summary>Caracteres de texto libre que caben en un mensaje.</summary>
    public const int CaracteresDeTextoLibre = 13;

    /// <summary>Caracteres que ocupa un indicativo raro dentro del campo de 58 bits.</summary>
    private const int CaracteresDeIndicativoLargo = 11;

    // ---------------------------------------------------------------------------------------
    // Empaquetado
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Convierte un mensaje escrito en los 77 bits que hay que emitir.
    /// </summary>
    /// <param name="texto">Mensaje con la gramatica del modo, por ejemplo <c>CQ EA8DLF IL18</c>.</param>
    /// <param name="bits">Los 77 bits, si se pudo.</param>
    /// <param name="motivo">Por que no se pudo, para poder decirselo al operador.</param>
    /// <returns>Cierto si el mensaje cabe en el formato.</returns>
    /// <remarks>
    /// Si el mensaje no encaja en ningun formato corriente se intenta como texto libre, que es
    /// lo que hace tambien el programa de referencia. Si tampoco cabe ahi, se rechaza: es mucho
    /// mejor negarse a transmitir que emitir un mensaje recortado que diga otra cosa.
    /// </remarks>
    public static bool TryEmpaquetar(string? texto, out byte[] bits, out string motivo)
    {
        bits = [];
        motivo = string.Empty;
        if (string.IsNullOrWhiteSpace(texto))
        {
            motivo = Textos.T("Servicios.Modos.MensajeVacio");
            return false;
        }

        var limpio = NormalizarTexto(texto);
        // El orden es el del protocolo: primero el mensaje corriente (con los indicativos entre
        // angulos resumidos en 22 bits), luego el de indicativo raro en claro (tipo 4), y solo
        // si nada de eso vale, el mensaje corriente resumiendo por su cuenta el indicativo raro
        // que venga sin angulos. El texto libre, al final.
        if (TryEmpaquetarNormal(limpio, resumirRaros: false, out bits)) return true;
        if (TryEmpaquetarNoEstandar(limpio, out bits)) return true;
        if (TryEmpaquetarNormal(limpio, resumirRaros: true, out bits)) return true;
        if (TryEmpaquetarTextoLibre(limpio, out bits)) return true;

        motivo = ExplicarElRechazo(limpio);
        return false;
    }

    /// <summary>
    /// Los indicativos que lleva un mensaje que se va a emitir, sin angulos.
    /// </summary>
    /// <remarks>
    /// Sirve para que el receptor aprenda lo que el propio operador manda: si se emite
    /// <c>&lt;HB10GBT&gt; EA8DLF IL18</c>, luego hay que poder resolver el resumen de HB10GBT y el
    /// de EA8DLF en lo que contesten. Solo cuenta si el mensaje sale como mensaje de indicativos
    /// (tipos 1 y 4); del texto libre no se saca nada.
    /// </remarks>
    public static IReadOnlyList<string> IndicativosQueViajan(string? texto)
    {
        if (!TryEmpaquetar(texto, out var bits, out _)) return [];
        var i3 = EmpaquetadoDeBits.Leer(bits, PosicionDeI3, 3);
        if (i3 is not (1 or 4)) return [];

        var lista = new List<string>(2);
        foreach (var palabra in NormalizarTexto(texto!).Split(' '))
        {
            var v = TryQuitarAngulos(palabra, out var dentro) ? dentro : palabra;
            if (v is "CQ" or "DE" or "QRZ" or "RR73" || EsLocalizadorDeCuatro(v)) continue;
            if (EsIndicativoResumible(v) && !lista.Contains(v)) lista.Add(v);
        }
        return lista;
    }

    /// <summary>Dice en palabras llanas por que un mensaje no se puede emitir.</summary>
    private static string ExplicarElRechazo(string limpio)
    {
        var palabras = limpio.Split(' ');
        if (palabras.Any(p => p.StartsWith('<') && p.EndsWith('>') && !EsIndicativoResumible(p[1..^1])))
        {
            return Textos.F("Servicios.Modos.Ft8.Angulos", limpio);
        }

        var largo = palabras.FirstOrDefault(p => p.Length > CaracteresDeIndicativoLargo
                                                 && p.Any(char.IsAsciiDigit) && p.Any(char.IsAsciiLetter));
        if (largo is not null)
        {
            return Textos.F("Servicios.Modos.Ft8.IndicativoLargo", largo, largo.Length, CaracteresDeIndicativoLargo);
        }

        var raros = limpio.Where(c => c != ' ' && !AlfabetoLibre.Contains(c, StringComparison.Ordinal)).Distinct().ToArray();
        return raros.Length > 0
            ? Textos.F("Servicios.Modos.Ft8.NoCabeRaros", limpio, CaracteresDeTextoLibre, string.Join(' ', raros))
            : Textos.F("Servicios.Modos.Ft8.NoCabeLargo", limpio, CaracteresDeTextoLibre, limpio.Length);
    }

    private static string NormalizarTexto(string texto)
    {
        // Los angulos se conservan: son como se pide que un indicativo viaje resumido, y eso
        // cambia los bits que se emiten.
        var partes = texto.Trim().ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', partes);
    }

    private static bool TryEmpaquetarNormal(string texto, bool resumirRaros, out byte[] bits)
    {
        bits = [];
        var campos = texto.Split(' ');
        if (campos.Length < 2) return false;

        var indice = 0;
        if (!TryLeerCampoDeEstacion(campos, ref indice, resumirRaros, out var n28Destino, out var roverDestino)) return false;
        if (indice >= campos.Length) return false;
        if (!TryEstacionA28(campos[indice], resumirRaros, out var n28Origen, out var roverOrigen)) return false;
        indice++;

        var acuse = false;
        var resto = string.Join(' ', campos.Skip(indice));
        if (resto.StartsWith("R ", StringComparison.Ordinal))
        {
            acuse = true;
            resto = resto[2..];
        }
        else if (resto.Length > 1 && resto[0] == 'R' && (resto[1] == '+' || resto[1] == '-'))
        {
            acuse = true;
            resto = resto[1..];
        }

        if (!TryCampoDeQuinceA(resto, out var g15)) return false;

        bits = new byte[Bits];
        EmpaquetadoDeBits.Escribir(bits, 0, 28, n28Destino);
        EmpaquetadoDeBits.Escribir(bits, 28, 1, roverDestino ? 1 : 0);
        EmpaquetadoDeBits.Escribir(bits, 29, 28, n28Origen);
        EmpaquetadoDeBits.Escribir(bits, 57, 1, roverOrigen ? 1 : 0);
        EmpaquetadoDeBits.Escribir(bits, 58, 1, acuse ? 1 : 0);
        EmpaquetadoDeBits.Escribir(bits, 59, 15, g15);
        EmpaquetadoDeBits.Escribir(bits, PosicionDeI3, 3, 1);
        return true;
    }

    /// <summary>
    /// Lee el primer campo, que puede ser una llamada general con cola o un indicativo.
    /// </summary>
    /// <remarks>
    /// <c>CQ DX</c> son dos palabras y una sola estacion; <c>CQ EA8DLF</c> son dos estaciones.
    /// Se distinguen porque la cola de una llamada general es de una a cuatro letras sin
    /// numeros, o tres cifras, y un indicativo corriente siempre lleva un numero en medio.
    /// </remarks>
    private static bool TryLeerCampoDeEstacion(string[] campos, ref int indice, bool resumirRaros, out long n28, out bool rover)
    {
        rover = false;
        n28 = 0;
        var primero = campos[indice];

        switch (primero)
        {
            case "DE":
                indice++;
                n28 = 0;
                return true;
            case "QRZ":
                indice++;
                n28 = 1;
                return true;
            case "CQ":
                indice++;
                if (indice < campos.Length && campos.Length - indice >= 2 && EsColaDeLlamadaGeneral(campos[indice], out var cola))
                {
                    n28 = cola;
                    indice++;
                }
                else
                {
                    n28 = 2;
                }
                return true;
            default:
                if (!TryEstacionA28(primero, resumirRaros, out n28, out rover)) return false;
                indice++;
                return true;
        }
    }

    /// <summary>
    /// Pasa una estacion al campo de 28 bits: en claro si es de forma corriente, o resumida en
    /// 22 bits si va entre angulos (o si es rara y se ha pedido resumirla).
    /// </summary>
    /// <remarks>
    /// El campo de 28 bits reserva, justo despues de las senales (<c>DE</c>, <c>QRZ</c>,
    /// <c>CQ</c>…), 2^22 valores para indicativos resumidos: el valor es
    /// <c>NumeroDeSenales + resumen de 22 bits</c>. Asi es como <c>&lt;HB10GBT&gt; EA8DLF IL18</c>
    /// cabe en un mensaje corriente aunque HB10GBT no quepa en la plantilla de seis huecos.
    /// </remarks>
    private static bool TryEstacionA28(string campo, bool resumirRaros, out long n28, out bool rover)
    {
        rover = false;
        n28 = 0;
        if (TryQuitarAngulos(campo, out var resumido))
        {
            if (!EsIndicativoResumible(resumido)) return false;
            n28 = NumeroDeSenales + CatalogoDeIndicativos.Resumir(resumido, 22);
            return true;
        }

        if (TryIndicativoCorrienteA28(campo, out n28, out rover)) return true;

        if (resumirRaros && EsIndicativoNoEstandar(campo))
        {
            n28 = NumeroDeSenales + CatalogoDeIndicativos.Resumir(campo, 22);
            return true;
        }
        return false;
    }

    /// <summary>Si la palabra va entre angulos, devuelve lo de dentro.</summary>
    private static bool TryQuitarAngulos(string campo, out string dentro)
    {
        dentro = string.Empty;
        if (campo.Length < 3 || campo[0] != '<' || campo[^1] != '>') return false;
        dentro = campo[1..^1];
        return true;
    }

    /// <summary>
    /// Dice si algo puede viajar como indicativo: resumido, o en claro en el campo de 58 bits.
    /// </summary>
    /// <remarks>
    /// Tres a once caracteres del alfabeto de 38, con al menos una letra y una cifra y sin barra
    /// en los extremos. La cifra obligatoria es lo que impide que un hueco sin rellenar de la
    /// gramatica —<c>&lt;DX&gt;</c>, <c>&lt;YO&gt;</c>— o el <c>&lt;...&gt;</c> de un resumen sin
    /// resolver acaben emitiendose como si fueran una estacion.
    /// </remarks>
    private static bool EsIndicativoResumible(string v)
    {
        if (v.Length is < 3 or > CaracteresDeIndicativoLargo) return false;
        if (!v.All(c => c != ' ' && AlfabetoDeIndicativoLargo.Contains(c, StringComparison.Ordinal))) return false;
        if (v[0] == '/' || v[^1] == '/') return false;
        return v.Any(char.IsAsciiDigit) && v.Any(char.IsAsciiLetter);
    }

    /// <summary>
    /// Dice si un indicativo cabe en el formato corriente de 28 bits, que es el que no necesita
    /// resumirse para ir con localizador o informe.
    /// </summary>
    /// <remarks>
    /// Hasta dos caracteres de prefijo, una cifra y hasta tres letras, con <c>/R</c> opcional.
    /// <c>HB10GBT</c> o <c>EA8/G5LSI</c> no caben y se tienen que mandar resumidos entre angulos,
    /// o en claro con el formato de indicativo raro.
    /// </remarks>
    public static bool EsIndicativoEstandar(string? indicativo) =>
        !string.IsNullOrWhiteSpace(indicativo) && TryIndicativoCorrienteA28(indicativo.Trim().ToUpperInvariant(), out _, out _);

    /// <summary>Dice si un indicativo se puede emitir de alguna manera (corriente, resumido o en claro).</summary>
    public static bool EsIndicativoEmitible(string? indicativo) =>
        !string.IsNullOrWhiteSpace(indicativo) && EsIndicativoResumible(indicativo.Trim().ToUpperInvariant());

    private static bool EsColaDeLlamadaGeneral(string cola, out long n28)
    {
        n28 = 0;
        if (cola.Length is < 1 or > 4) return false;

        if (cola.Length == 3 && cola.All(char.IsAsciiDigit))
        {
            // CQ 000 a CQ 999 ocupan los valores 3 a 1002.
            n28 = 3 + int.Parse(cola, CultureInfo.InvariantCulture);
            return true;
        }

        if (!cola.All(c => c is >= 'A' and <= 'Z')) return false;

        // De una a cuatro letras, alineadas a la derecha en cuatro huecos de 27 valores.
        long n = 0;
        var relleno = new string(' ', 4 - cola.Length) + cola;
        foreach (var c in relleno)
        {
            var i = AlfabetoSufijo.IndexOf(c, StringComparison.Ordinal);
            if (i < 0) return false;
            n = (n * 27) + i;
        }
        n28 = 1003 + n;
        return n28 <= 532443;
    }

    /// <summary>
    /// Pasa un indicativo de forma corriente al numero de 28 bits que le toca.
    /// </summary>
    /// <remarks>
    /// Un indicativo corriente es hasta dos caracteres de prefijo, una cifra y hasta tres
    /// letras de sufijo. Para numerarlo se encaja en una plantilla de seis huecos con la cifra
    /// siempre en el tercero; ahi es donde se decide si <c>K1ABC</c> lleva un espacio delante o
    /// si <c>A45XR</c> no lo lleva, que es el detalle donde mas facil es equivocarse.
    /// </remarks>
    private static bool TryIndicativoCorrienteA28(string indicativo, out long n28, out bool rover)
    {
        n28 = 0;
        rover = false;
        var v = indicativo;

        if (v.EndsWith("/R", StringComparison.Ordinal))
        {
            rover = true;
            v = v[..^2];
        }

        if (!TryPlantillaDeSeis(v, out var plantilla)) return false;

        long n = AlfabetoPrimero.IndexOf(plantilla[0], StringComparison.Ordinal);
        var i2 = AlfabetoSegundo.IndexOf(plantilla[1], StringComparison.Ordinal);
        var i3 = AlfabetoDigito.IndexOf(plantilla[2], StringComparison.Ordinal);
        var i4 = AlfabetoSufijo.IndexOf(plantilla[3], StringComparison.Ordinal);
        var i5 = AlfabetoSufijo.IndexOf(plantilla[4], StringComparison.Ordinal);
        var i6 = AlfabetoSufijo.IndexOf(plantilla[5], StringComparison.Ordinal);
        if (n < 0 || i2 < 0 || i3 < 0 || i4 < 0 || i5 < 0 || i6 < 0) return false;

        n = (((((n * 36) + i2) * 10 + i3) * 27 + i4) * 27 + i5) * 27 + i6;
        n28 = n + NumeroDeSenales + ResumenesDe22;
        return true;
    }

    /// <summary>Encaja el indicativo en seis huecos con la cifra separadora en el tercero.</summary>
    private static bool TryPlantillaDeSeis(string indicativo, out string plantilla)
    {
        plantilla = string.Empty;
        if (indicativo.Length is < 3 or > 6) return false;
        if (!indicativo.All(char.IsAsciiLetterOrDigit)) return false;

        // La cifra que separa el prefijo del sufijo es la ultima cifra a la que solo le siguen
        // letras. Asi A45XR separa por el 5 y no por el 4, y 2E0ABC separa por el 0 y no por
        // el 2, que es lo que haria una busqueda ingenua de la primera cifra.
        var separador = -1;
        for (var i = indicativo.Length - 1; i >= 0; i--)
        {
            if (!char.IsAsciiDigit(indicativo[i])) continue;
            var soloLetrasDetras = true;
            for (var j = i + 1; j < indicativo.Length; j++)
                if (!char.IsAsciiLetter(indicativo[j])) { soloLetrasDetras = false; break; }
            if (soloLetrasDetras) { separador = i; break; }
        }
        if (separador is < 1 or > 2) return false;

        var hueco = 2 - separador;
        if (hueco + indicativo.Length > 6) return false;

        var trozos = new char[6];
        Array.Fill(trozos, ' ');
        for (var i = 0; i < indicativo.Length; i++) trozos[hueco + i] = indicativo[i];

        // El sufijo solo admite letras o hueco; si aqui queda una cifra, no es un indicativo
        // de forma corriente y hay que mandarlo por el formato de indicativos raros.
        for (var i = 3; i < 6; i++)
            if (trozos[i] != ' ' && !char.IsAsciiLetter(trozos[i])) return false;
        if (!char.IsAsciiDigit(trozos[2])) return false;

        plantilla = new string(trozos);
        return true;
    }

    private static bool TryCampoDeQuinceA(string campo, out long g15)
    {
        g15 = CuadrosDeLocalizador + 1;
        if (string.IsNullOrEmpty(campo)) return true;

        switch (campo)
        {
            case "RRR": g15 = CuadrosDeLocalizador + 2; return true;
            case "RR73": g15 = CuadrosDeLocalizador + 3; return true;
            case "73": g15 = CuadrosDeLocalizador + 4; return true;
        }

        if (campo.Length == 4
            && campo[0] is >= 'A' and <= 'R' && campo[1] is >= 'A' and <= 'R'
            && char.IsAsciiDigit(campo[2]) && char.IsAsciiDigit(campo[3]))
        {
            g15 = ((((campo[0] - 'A') * 18) + (campo[1] - 'A')) * 10 + (campo[2] - '0')) * 10 + (campo[3] - '0');
            return true;
        }

        if (campo.Length >= 2 && (campo[0] == '+' || campo[0] == '-')
            && int.TryParse(campo, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var informe))
        {
            if (informe < InformeMinimo || informe > InformeMaximo) return false;
            g15 = BaseDelInforme + informe;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Empaqueta un mensaje de tipo 4: un indicativo en claro de hasta once caracteres y el
    /// otro resumido en 12 bits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Campos, de izquierda a derecha: resumen de 12 bits, indicativo en claro de 58, un bit
    /// que dice cual de los dos va primero, dos bits para <c>RRR</c>/<c>RR73</c>/<c>73</c>, un
    /// bit de llamada general y el <c>i3 = 4</c>. No hay sitio para localizador ni informe.
    /// </para>
    /// <para>
    /// Se aceptan dos escrituras. La explicita, con el resumido entre angulos:
    /// <c>HB10GBT &lt;EA8DLF&gt;</c>, <c>&lt;EA8DLF&gt; HB10GBT RRR</c>. Y la antigua sin
    /// angulos, cuando uno es raro y el otro corriente (<c>EA8DLF EA1ABC/P 73</c>), en la que se
    /// resume el corriente.
    /// </para>
    /// <para>
    /// La comprobacion es deliberadamente estricta. Si no lo fuera, un texto cualquiera de dos
    /// palabras se colaria por aqui y se emitiria como si fueran dos indicativos: el receptor
    /// leeria «HOLA» como una estacion. Antes que eso, que caiga al texto libre, que dice lo que
    /// dice.
    /// </para>
    /// </remarks>
    private static bool TryEmpaquetarNoEstandar(string texto, out byte[] bits)
    {
        bits = [];
        var campos = texto.Split(' ');
        if (campos.Length < 2) return false;

        if (campos[0] == "CQ") return TryEmpaquetarCqNoEstandar(campos, out bits);
        if (campos.Length > 3) return false;

        var cola = campos.Length == 3 ? campos[2] : string.Empty;
        var r2 = cola switch
        {
            "" => 0L,
            "RRR" => 1L,
            "RR73" => 2L,
            "73" => 3L,
            _ => -1L,
        };
        if (r2 < 0) return false;

        string enClaro, resumido;
        bool elEnClaroVaPrimero;

        var primeroResumido = TryQuitarAngulos(campos[0], out var dentroPrimero);
        var segundoResumido = TryQuitarAngulos(campos[1], out var dentroSegundo);

        if (primeroResumido && !segundoResumido)
        {
            resumido = dentroPrimero;
            enClaro = campos[1];
            elEnClaroVaPrimero = false;
        }
        else if (segundoResumido && !primeroResumido)
        {
            resumido = dentroSegundo;
            enClaro = campos[0];
            elEnClaroVaPrimero = true;
        }
        else if (primeroResumido || segundoResumido)
        {
            return false;
        }
        else if (EsIndicativoNoEstandar(campos[0]) && EsIndicativoCorriente(campos[1]))
        {
            enClaro = campos[0];
            resumido = campos[1];
            elEnClaroVaPrimero = true;
        }
        else if (EsIndicativoNoEstandar(campos[1]) && EsIndicativoCorriente(campos[0]))
        {
            enClaro = campos[1];
            resumido = campos[0];
            elEnClaroVaPrimero = false;
        }
        else
        {
            return false;
        }

        if (!EsIndicativoResumible(resumido) || !EsIndicativoResumible(enClaro)) return false;
        if (!TryIndicativoLargoA58(enClaro, out var c58)) return false;
        var h12 = CatalogoDeIndicativos.Resumir(resumido, 12);

        bits = EscribirTipoCuatro(h12, c58, elEnClaroVaPrimero, r2, esCq: false);
        return true;
    }

    /// <summary>
    /// <c>CQ HB10GBT</c>: la llamada general de un indicativo raro, en claro.
    /// </summary>
    /// <remarks>
    /// El formato no tiene sitio para localizador ni para la cola del CQ (<c>DX</c>,
    /// <c>POTA</c>…). Si vienen, se quitan, como hacen los demas programas: lo que importa es que
    /// el indicativo llegue entero para que los demas lo aprendan. Solo se hace si el
    /// indicativo es de verdad raro; uno corriente va por el mensaje normal, con localizador.
    /// </remarks>
    private static bool TryEmpaquetarCqNoEstandar(string[] campos, out byte[] bits)
    {
        bits = [];
        var i = 1;
        if (campos.Length >= 3 && !EsIndicativoNoEstandar(campos[1]) && EsColaDeLlamadaGeneral(campos[1], out _)) i = 2;
        if (i >= campos.Length) return false;

        var raro = campos[i];
        if (!EsIndicativoNoEstandar(raro)) return false;
        var sobra = campos.Length - i - 1;
        if (sobra > 1) return false;
        if (sobra == 1 && !EsLocalizadorDeCuatro(campos[i + 1])) return false;

        if (!TryIndicativoLargoA58(raro, out var c58)) return false;
        bits = EscribirTipoCuatro(0, c58, elEnClaroVaPrimero: false, r2: 0, esCq: true);
        return true;
    }

    private static bool EsLocalizadorDeCuatro(string campo) =>
        campo.Length == 4 && campo[0] is >= 'A' and <= 'R' && campo[1] is >= 'A' and <= 'R'
        && char.IsAsciiDigit(campo[2]) && char.IsAsciiDigit(campo[3]);

    private static byte[] EscribirTipoCuatro(long h12, long c58, bool elEnClaroVaPrimero, long r2, bool esCq)
    {
        var bits = new byte[Bits];
        EmpaquetadoDeBits.Escribir(bits, 0, 12, h12);
        EmpaquetadoDeBits.Escribir(bits, 12, 58, c58);
        EmpaquetadoDeBits.Escribir(bits, 70, 1, elEnClaroVaPrimero ? 1 : 0);
        EmpaquetadoDeBits.Escribir(bits, 71, 2, r2);
        EmpaquetadoDeBits.Escribir(bits, 73, 1, esCq ? 1 : 0);
        EmpaquetadoDeBits.Escribir(bits, PosicionDeI3, 3, 4);
        return bits;
    }

    /// <summary>Dice si algo tiene pinta de indicativo pero no cabe en el formato corriente.</summary>
    /// <remarks>
    /// Se exige que lleve al menos una cifra y al menos una letra, que es lo que distingue a
    /// <c>EA1ABC/P</c> o <c>VP2E/K1ABC</c> de una palabra suelta. Sin ese filtro, cualquier par
    /// de palabras acabaria emitiendose como si fueran estaciones.
    /// </remarks>
    private static bool EsIndicativoNoEstandar(string v) =>
        EsIndicativoResumible(v) && !TryIndicativoCorrienteA28(v, out _, out _);

    /// <summary>Dice si algo es un indicativo del formato corriente.</summary>
    private static bool EsIndicativoCorriente(string v) => TryIndicativoCorrienteA28(v, out _, out _);

    /// <summary>
    /// Numera un indicativo raro en base 38 para que quepa en 58 bits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Once caracteres de un alfabeto de 38 —hueco, cifras, letras y barra— dan un numero de
    /// 57,7 bits, que cabe justo en los 58 que reserva el formato. Ese es el motivo de que un
    /// indicativo de mas de once caracteres no se pueda emitir de ninguna manera.
    /// </para>
    /// <para>
    /// <b>Alineado a la derecha</b>: los huecos sobrantes van delante y valen cero, asi que el
    /// numero es el de los caracteres del indicativo sin mas. Es como lo emiten los demas
    /// programas (contrastado con los vectores de ft8_lib); alineado a la izquierda se
    /// decodificaria igual, pero los bits no serian los mismos.
    /// </para>
    /// </remarks>
    private static bool TryIndicativoLargoA58(string indicativo, out long c58)
    {
        c58 = 0;
        if (indicativo.Length > CaracteresDeIndicativoLargo) return false;
        foreach (var c in indicativo)
        {
            var i = AlfabetoDeIndicativoLargo.IndexOf(c, StringComparison.Ordinal);
            if (i < 0) return false;
            c58 = (c58 * 38) + i;
        }
        return c58 >= 0;
    }

    private static bool TryEmpaquetarTextoLibre(string texto, out byte[] bits)
    {
        bits = [];
        if (texto.Length > CaracteresDeTextoLibre) return false;

        // Se alinea a la derecha, que es como lo hace el protocolo: los espacios sobrantes
        // quedan delante y valen cero, asi que no gastan valor.
        UInt128 n = 0;
        foreach (var c in texto.PadLeft(CaracteresDeTextoLibre))
        {
            var i = AlfabetoLibre.IndexOf(c, StringComparison.Ordinal);
            if (i < 0) return false;
            n = (n * 42) + (UInt128)i;
        }

        bits = new byte[Bits];
        EmpaquetadoDeBits.EscribirGrande(bits, 0, 71, n);
        EmpaquetadoDeBits.Escribir(bits, PosicionDeN3, 3, 0);
        EmpaquetadoDeBits.Escribir(bits, PosicionDeI3, 3, 0);
        return true;
    }

    // ---------------------------------------------------------------------------------------
    // Desempaquetado
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Convierte 77 bits ya comprobados por el CRC en un mensaje entendible.
    /// </summary>
    /// <param name="bits">Los 77 bits.</param>
    /// <param name="catalogo">Catalogo donde buscar los indicativos que viajan resumidos.</param>
    /// <param name="mensaje">El mensaje, si el formato se reconoce.</param>
    /// <returns>
    /// Falso si el formato no se reconoce. <b>Falso es la respuesta correcta</b> cuando el
    /// mensaje es de un formato de concurso que no se maneja: mas vale perder una decodificacion
    /// que ensenar un indicativo sacado del sitio equivocado de los bits.
    /// </returns>
    public static bool TryDesempaquetar(ReadOnlySpan<byte> bits, CatalogoDeIndicativos catalogo, out MensajeDescifrado mensaje)
    {
        ArgumentNullException.ThrowIfNull(catalogo);
        mensaje = new MensajeDescifrado(string.Empty, TipoDeMensaje.NoSoportado);
        if (bits.Length != Bits) return false;
        if (EsTodoCeros(bits)) return false;

        var i3 = (int)EmpaquetadoDeBits.Leer(bits, PosicionDeI3, 3);
        return i3 switch
        {
            0 => TryDesempaquetarDeTipoCero(bits, out mensaje),
            1 => TryDesempaquetarNormal(bits, catalogo, out mensaje),
            4 => TryDesempaquetarNoEstandar(bits, catalogo, out mensaje),
            _ => false,
        };
    }

    /// <summary>
    /// Dice si los 77 bits son todo ceros.
    /// </summary>
    /// <remarks>
    /// <b>Esta comprobacion vale por si sola de aviso.</b> El mensaje de todo ceros tiene un CRC
    /// de todo ceros —el CRC de nada es nada—, asi que la palabra de codigo de todo ceros pasa
    /// el sello sin problemas. Y resulta que es justo la palabra a la que el corrector de errores
    /// tiende a irse cuando solo le dan ruido, porque es la mas «facil» de todas. Sin este
    /// rechazo explicito, cada pocas ventanas de banda vacía aparecería un mensaje fantasma.
    /// </remarks>
    private static bool EsTodoCeros(ReadOnlySpan<byte> bits)
    {
        foreach (var b in bits) if (b != 0) return false;
        return true;
    }

    private static bool TryDesempaquetarDeTipoCero(ReadOnlySpan<byte> bits, out MensajeDescifrado mensaje)
    {
        mensaje = new MensajeDescifrado(string.Empty, TipoDeMensaje.NoSoportado);
        var n3 = (int)EmpaquetadoDeBits.Leer(bits, PosicionDeN3, 3);

        if (n3 == 0)
        {
            var n = EmpaquetadoDeBits.LeerGrande(bits, 0, 71);
            var trozos = new char[CaracteresDeTextoLibre];
            for (var i = CaracteresDeTextoLibre - 1; i >= 0; i--)
            {
                trozos[i] = AlfabetoLibre[(int)(n % 42)];
                n /= 42;
            }
            var texto = new string(trozos).Trim();
            // Un texto libre vacío es lo que sale del mensaje de todo ceros, que es la palabra
            // falsa mas habitual. Se rechaza aqui ademas de en la comprobacion general.
            if (texto.Length == 0) return false;
            mensaje = new MensajeDescifrado(texto, TipoDeMensaje.TextoLibre);
            return true;
        }

        if (n3 == 5)
        {
            var n = EmpaquetadoDeBits.LeerGrande(bits, 0, 71);
            mensaje = new MensajeDescifrado(n.ToString("X18", CultureInfo.InvariantCulture), TipoDeMensaje.Telemetria);
            return true;
        }

        // Expediciones y concursos: formatos que no se manejan.
        return false;
    }

    private static bool TryDesempaquetarNormal(ReadOnlySpan<byte> bits, CatalogoDeIndicativos catalogo, out MensajeDescifrado mensaje)
    {
        mensaje = new MensajeDescifrado(string.Empty, TipoDeMensaje.NoSoportado);

        var n28Destino = EmpaquetadoDeBits.Leer(bits, 0, 28);
        var roverDestino = EmpaquetadoDeBits.Leer(bits, 28, 1) != 0;
        var n28Origen = EmpaquetadoDeBits.Leer(bits, 29, 28);
        var roverOrigen = EmpaquetadoDeBits.Leer(bits, 57, 1) != 0;
        var acuse = EmpaquetadoDeBits.Leer(bits, 58, 1) != 0;
        var g15 = (int)EmpaquetadoDeBits.Leer(bits, 59, 15);

        if (!TryDe28ATexto(n28Destino, roverDestino, catalogo, out var destino, out var destinoEnClaro, out var esCqDestino, out var destinoSinResolver)) return false;
        if (!TryDe28ATexto(n28Origen, roverOrigen, catalogo, out var origen, out var origenEnClaro, out var esCqOrigen, out var origenSinResolver)) return false;
        // La segunda estacion es siempre quien emite; si ahi sale una llamada general, los bits
        // no son un mensaje de este tipo por mucho que el CRC cuadrara.
        if (esCqOrigen) return false;

        if (!TryDeQuinceATexto(g15, out var cola, out var locator, out var informe)) return false;

        var partes = new StringBuilder(destino);
        partes.Append(' ').Append(origen);
        if (acuse && cola.Length > 0 && (cola[0] == '+' || cola[0] == '-'))
        {
            // El acuse de recibo se pega al informe sin espacio: «R-12», no «R -12».
            partes.Append(" R").Append(cola);
        }
        else
        {
            if (acuse) partes.Append(" R");
            if (cola.Length > 0) partes.Append(' ').Append(cola);
        }

        // El indicativo que sale es el de verdad, sin angulos: <HB10GBT> y HB10GBT son la misma
        // estacion para el secuenciador y para el cuaderno.
        var llamante = Indicativo.Vacio;
        if (origenEnClaro.Length > 0) Indicativo.TryParse(origenEnClaro, out llamante);
        var llamado = Indicativo.Vacio;
        if (!esCqDestino && destinoEnClaro.Length > 0) Indicativo.TryParse(destinoEnClaro, out llamado);

        // Solo se apunta en el catalogo lo que se ha visto entero, y en claro: un resumen
        // resuelto ya esta en el catalogo, y uno sin resolver no dice nada.
        if (!origenSinResolver && !origen.StartsWith('<')) catalogo.Recordar(origen);
        if (!esCqDestino && !destinoSinResolver && !destino.StartsWith('<')) catalogo.Recordar(destino);

        mensaje = new MensajeDescifrado(partes.ToString(), TipoDeMensaje.Normal)
        {
            Llamante = llamante,
            Llamado = llamado,
            Locator = locator,
            EsCq = esCqDestino,
            Informe = informe,
            TieneIndicativoSinResolver = destinoSinResolver || origenSinResolver,
        };
        return true;
    }

    private static bool TryDe28ATexto(long n28, bool rover, CatalogoDeIndicativos catalogo, out string texto, out string enClaro, out bool esLlamadaGeneral, out bool sinResolver)
    {
        var ok = TryDe28ATexto(n28, rover, catalogo, out texto, out esLlamadaGeneral, out sinResolver);
        enClaro = !ok || esLlamadaGeneral || sinResolver ? string.Empty : texto.Trim('<', '>');
        return ok;
    }

    private static bool TryDe28ATexto(long n28, bool rover, CatalogoDeIndicativos catalogo, out string texto, out bool esLlamadaGeneral, out bool sinResolver)
    {
        texto = string.Empty;
        esLlamadaGeneral = false;
        sinResolver = false;

        if (n28 < NumeroDeSenales)
        {
            esLlamadaGeneral = true;
            switch (n28)
            {
                case 0: texto = "DE"; return true;
                case 1: texto = "QRZ"; return true;
                case 2: texto = "CQ"; return true;
            }
            if (n28 <= 1002)
            {
                texto = "CQ " + (n28 - 3).ToString("D3", CultureInfo.InvariantCulture);
                return true;
            }
            if (n28 <= 532443)
            {
                var n = n28 - 1003;
                var trozos = new char[4];
                for (var i = 3; i >= 0; i--)
                {
                    trozos[i] = AlfabetoSufijo[(int)(n % 27)];
                    n /= 27;
                }
                texto = "CQ " + new string(trozos).Trim();
                return true;
            }
            // Hueco reservado del protocolo: no significa nada conocido.
            return false;
        }

        var resto = n28 - NumeroDeSenales;
        if (resto < ResumenesDe22)
        {
            // Viaja resumido. Si no consta, se dice que no consta y punto.
            var conocido = catalogo.Resolver((int)resto, 22);
            if (conocido is null)
            {
                texto = CatalogoDeIndicativos.TextoSinResolver();
                sinResolver = true;
                return true;
            }
            texto = "<" + conocido + ">";
            return true;
        }

        var indice = resto - ResumenesDe22;
        var c6 = new char[6];
        c6[5] = AlfabetoSufijo[(int)(indice % 27)]; indice /= 27;
        c6[4] = AlfabetoSufijo[(int)(indice % 27)]; indice /= 27;
        c6[3] = AlfabetoSufijo[(int)(indice % 27)]; indice /= 27;
        c6[2] = AlfabetoDigito[(int)(indice % 10)]; indice /= 10;
        c6[1] = AlfabetoSegundo[(int)(indice % 36)]; indice /= 36;
        c6[0] = AlfabetoPrimero[(int)(indice % 37)];

        texto = new string(c6).Trim();
        if (texto.Length < 3) return false;
        if (rover) texto += "/R";
        return true;
    }

    private static bool TryDeQuinceATexto(int g15, out string cola, out Locator locator, out int? informe)
    {
        cola = string.Empty;
        locator = Locator.Vacio;
        informe = null;

        if (g15 < CuadrosDeLocalizador)
        {
            var n = g15;
            var d2 = n % 10; n /= 10;
            var d1 = n % 10; n /= 10;
            var l2 = n % 18; n /= 18;
            var l1 = n % 18;
            var texto = $"{(char)('A' + l1)}{(char)('A' + l2)}{d1}{d2}";
            if (!Locator.TryParse(texto, out locator)) return false;
            cola = texto;
            return true;
        }

        switch (g15 - CuadrosDeLocalizador)
        {
            // El 32400 no lo emite nadie: no es ni localizador ni valor con nombre.
            case 0: return false;
            case 1: cola = string.Empty; return true;
            case 2: cola = "RRR"; return true;
            case 3: cola = "RR73"; return true;
            case 4: cola = "73"; return true;
        }

        var valor = g15 - BaseDelInforme;
        if (valor < InformeMinimo || valor > InformeMaximo) return false;
        informe = valor;
        cola = valor.ToString("+00;-00", CultureInfo.InvariantCulture);
        return true;
    }

    private static bool TryDesempaquetarNoEstandar(ReadOnlySpan<byte> bits, CatalogoDeIndicativos catalogo, out MensajeDescifrado mensaje)
    {
        mensaje = new MensajeDescifrado(string.Empty, TipoDeMensaje.NoSoportado);

        var h12 = (int)EmpaquetadoDeBits.Leer(bits, 0, 12);
        var c58 = EmpaquetadoDeBits.Leer(bits, 12, 58);
        var elRaroVaPrimero = EmpaquetadoDeBits.Leer(bits, 70, 1) != 0;
        var r2 = (int)EmpaquetadoDeBits.Leer(bits, 71, 2);
        var esCq = EmpaquetadoDeBits.Leer(bits, 73, 1) != 0;

        var trozos = new char[CaracteresDeIndicativoLargo];
        var n = c58;
        for (var i = CaracteresDeIndicativoLargo - 1; i >= 0; i--)
        {
            trozos[i] = AlfabetoDeIndicativoLargo[(int)(n % 38)];
            n /= 38;
        }
        var raro = new string(trozos).Trim();
        if (raro.Length < 3) return false;

        var conocido = catalogo.Resolver(h12, 12);
        var sinResolver = conocido is null;
        var corriente = conocido is null ? CatalogoDeIndicativos.TextoSinResolver() : "<" + conocido + ">";

        var cola = r2 switch
        {
            0 => string.Empty,
            1 => "RRR",
            2 => "RR73",
            3 => "73",
            _ => string.Empty,
        };

        string texto;
        Indicativo llamante, llamado = Indicativo.Vacio;
        if (esCq)
        {
            texto = $"CQ {raro}";
            Indicativo.TryParse(raro, out llamante);
        }
        else if (elRaroVaPrimero)
        {
            // El raro es a quien se llama; quien emite es el resumido, que puede no constar.
            texto = $"{raro} {corriente}";
            llamante = Indicativo.Vacio;
            Indicativo.TryParse(raro, out llamado);
            if (conocido is not null) Indicativo.TryParse(conocido, out llamante);
        }
        else
        {
            texto = $"{corriente} {raro}";
            Indicativo.TryParse(raro, out llamante);
            if (conocido is not null) Indicativo.TryParse(conocido, out llamado);
        }
        if (cola.Length > 0) texto += " " + cola;

        catalogo.Recordar(raro);

        mensaje = new MensajeDescifrado(texto, TipoDeMensaje.IndicativoNoEstandar)
        {
            Llamante = llamante,
            Llamado = llamado,
            EsCq = esCq,
            TieneIndicativoSinResolver = sinResolver && !esCq,
        };
        return true;
    }
}
