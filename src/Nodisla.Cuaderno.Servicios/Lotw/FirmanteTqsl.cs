using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Nodisla.Cuaderno.Servicios.Lotw;

/// <summary>Como acabo una ejecucion de TQSL.</summary>
/// <param name="Codigo">Codigo de estado final que devuelve TQSL.</param>
/// <param name="Descripcion">Texto de la ultima linea de estado, tal cual lo dio TQSL.</param>
/// <param name="Salida">Todo lo que TQSL escribio en la salida de errores.</param>
public sealed record ResultadoDeTqsl(int Codigo, string Descripcion, string Salida)
{
    /// <summary>
    /// Todo lo enviado quedo firmado y subido. El codigo 9 tambien vale: significa que se
    /// subio lo nuevo y se dejo fuera lo que ya estaba subido, que es exactamente lo deseable.
    /// </summary>
    public bool EsExito => Codigo is 0 or 9;

    /// <summary>
    /// No se subio nada porque todo era repetido o estaba fuera del rango del certificado.
    /// No es un fallo del programa: no habia nada nuevo que mandar.
    /// </summary>
    public bool NadaQueSubir => Codigo is 8 or 14;
}

/// <summary>Firma un fichero ADIF con TQSL y lo sube a LoTW.</summary>
public interface IFirmanteTqsl
{
    /// <summary>Hay un TQSL utilizable en la maquina.</summary>
    bool EstaDisponible { get; }

    /// <summary>Firma el fichero y lo sube a LoTW.</summary>
    /// <param name="rutaDelAdif">Fichero ADI que hay que firmar.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    Task<ResultadoDeTqsl> FirmarYSubirAsync(string rutaDelAdif, CancellationToken ct = default);
}

/// <summary>
/// Llama a <c>tqsl.exe</c> por linea de ordenes para firmar y subir.
/// </summary>
/// <remarks>
/// <para>
/// La firma no se reimplementa a proposito. La ARRL documenta esta via precisamente para que
/// los programas de cuaderno la usen, y asi la renovacion anual del certificado, la custodia
/// de la clave privada y los cambios de formato del <c>.tq8</c> siguen siendo problema de TQSL
/// y no nuestro. Un cuaderno que se firmase sus propios contactos tendria que mantener una
/// implementacion criptografica que la ARRL puede cambiar cuando quiera.
/// </para>
/// <para>
/// La orden es la que documenta la propia ayuda de TQSL:
/// <c>tqsl -d -a compliant -u -x -l "Ubicacion" fichero.adi</c>. El <c>-x</c> hace que termine
/// en vez de abrir la ventana, el <c>-d</c> suprime el dialogo de rango de fechas y el
/// <c>-u</c> sube en lugar de guardar el <c>.tq8</c>. TQSL escribe su estado en la
/// <b>salida de errores</b>, no en la estandar, y la ultima linea tiene la forma
/// <c>Final Status: descripcion (codigo)</c>.
/// </para>
/// <para>
/// <b>Limitacion conocida:</b> si el certificado lleva frase de paso, TQSL solo la admite en
/// la linea de ordenes (<c>-p</c>), donde queda visible para quien mire la lista de procesos
/// mientras dura la llamada. No hay otra via; la frase se guarda cifrada y jamas se registra.
/// </para>
/// </remarks>
public sealed class FirmanteTqsl : IFirmanteTqsl
{
    private readonly OpcionesLotw _opciones;
    private readonly ILocalizadorDeTqsl _localizador;
    private readonly Func<string?> _fraseDePaso;
    private readonly ILogger _log;

    /// <summary>Crea el firmante.</summary>
    /// <param name="opciones">Ajustes de LoTW.</param>
    /// <param name="localizador">Como encontrar <c>tqsl.exe</c>.</param>
    /// <param name="fraseDePaso">
    /// De donde sacar la frase de paso del certificado. Se pide en el momento de usarla para
    /// no tenerla descifrada en memoria mas tiempo del necesario.
    /// </param>
    /// <param name="log">Registro de trazas.</param>
    public FirmanteTqsl(
        OpcionesLotw opciones,
        ILocalizadorDeTqsl? localizador = null,
        Func<string?>? fraseDePaso = null,
        ILogger<FirmanteTqsl>? log = null)
    {
        _opciones = opciones ?? throw new ArgumentNullException(nameof(opciones));
        _localizador = localizador ?? new LocalizadorDeTqsl();
        _fraseDePaso = fraseDePaso ?? (() => null);
        _log = log ?? NullLogger<FirmanteTqsl>.Instance;
    }

    /// <inheritdoc />
    public bool EstaDisponible => Ruta is not null;

    private string? Ruta => !string.IsNullOrWhiteSpace(_opciones.RutaDeTqsl)
        ? _opciones.RutaDeTqsl
        : _localizador.Localizar();

    /// <inheritdoc />
    public async Task<ResultadoDeTqsl> FirmarYSubirAsync(
        string rutaDelAdif, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rutaDelAdif);

        var ejecutable = Ruta
            ?? throw new TqslNoInstaladoException(
                "No se encontró TQSL (tqsl.exe) en este equipo. LoTW exige firmar los contactos "
                + "con el certificado de la ARRL, así que hay que instalar Trusted QSL desde "
                + "https://lotw.arrl.org/lotw-help/installation/ o indicar su ruta en la tarjeta de LoTW (Configuración › Cuentas y servicios).");

        if (string.IsNullOrWhiteSpace(_opciones.UbicacionDeEstacion))
        {
            throw new InvalidOperationException(
                "Falta el nombre de la ubicación de estación de TQSL. Sin ella, TQSL abre un "
                + "diálogo y la subida se queda esperando indefinidamente.");
        }

        var arranque = new ProcessStartInfo(ejecutable)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argumento in ArgumentosDe(rutaDelAdif)) arranque.ArgumentList.Add(argumento);

        var frase = _fraseDePaso();
        if (!string.IsNullOrEmpty(frase))
        {
            arranque.ArgumentList.Add("-p");
            arranque.ArgumentList.Add(frase);
        }

        // Nunca se traza la lista de argumentos: puede llevar la frase de paso.
        _log.LogInformation(
            "Firmando y subiendo {Fichero} con TQSL desde {Ejecutable}.", rutaDelAdif, ejecutable);

        using var proceso = new Process { StartInfo = arranque };
        var errores = new StringBuilder();
        proceso.ErrorDataReceived += (_, e) => { if (e.Data is not null) errores.AppendLine(e.Data); };

        if (!proceso.Start())
        {
            throw new TqslNoInstaladoException($"No se pudo arrancar TQSL desde «{ejecutable}».");
        }
        proceso.BeginErrorReadLine();
        _ = await proceso.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);

        using var espera = CancellationTokenSource.CreateLinkedTokenSource(ct);
        espera.CancelAfter(_opciones.EsperaDeTqsl);
        try
        {
            await proceso.WaitForExitAsync(espera.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            MatarConCuidado(proceso);
            throw new TimeoutException(
                $"TQSL no terminó en {_opciones.EsperaDeTqsl}. Se ha cancelado la subida.");
        }

        var salida = errores.ToString();
        var (codigo, descripcion) = InterpretarEstado(salida, proceso.ExitCode);
        _log.LogInformation("TQSL terminó con el código {Codigo}: {Descripcion}", codigo, descripcion);
        return new ResultadoDeTqsl(codigo, descripcion, salida);
    }

    /// <summary>Argumentos con los que se llama a TQSL, sin la frase de paso.</summary>
    /// <param name="rutaDelAdif">Fichero a firmar.</param>
    public IReadOnlyList<string> ArgumentosDe(string rutaDelAdif) =>
    [
        "-d",                               // sin diálogo de rango de fechas
        "-a", _opciones.AccionDeTqsl,       // qué hacer con repetidos y fuera de rango
        "-u",                               // subir en vez de guardar el .tq8
        "-x",                               // modo por lotes: termina en vez de abrir la ventana
        "-l", _opciones.UbicacionDeEstacion,
        rutaDelAdif,
    ];

    /// <summary>
    /// Saca el codigo de estado de la salida de TQSL. Se prefiere la ultima linea
    /// <c>Final Status: ... (n)</c> al codigo de salida del proceso porque es la que la propia
    /// ayuda de TQSL declara como contrato.
    /// </summary>
    /// <param name="salida">Todo lo escrito por TQSL en la salida de errores.</param>
    /// <param name="codigoDeSalida">Codigo de salida del proceso, como respaldo.</param>
    public static (int Codigo, string Descripcion) InterpretarEstado(string salida, int codigoDeSalida)
    {
        if (!string.IsNullOrEmpty(salida))
        {
            const string marca = "Final Status:";
            var i = salida.LastIndexOf(marca, StringComparison.OrdinalIgnoreCase);
            if (i >= 0)
            {
                var linea = salida[(i + marca.Length)..].Trim();
                var abre = linea.LastIndexOf('(');
                var cierra = linea.LastIndexOf(')');
                if (abre >= 0 && cierra > abre
                    && int.TryParse(linea[(abre + 1)..cierra], out var codigo))
                {
                    return (codigo, linea[..abre].Trim());
                }
                return (codigoDeSalida, linea);
            }
        }
        return (codigoDeSalida, DescripcionDe(codigoDeSalida));
    }

    /// <summary>Traduce al español el codigo de estado de TQSL.</summary>
    /// <param name="codigo">Codigo devuelto por TQSL.</param>
    public static string DescripcionDe(int codigo) => codigo switch
    {
        0 => "Todos los contactos se firmaron y se subieron.",
        1 => "El operador canceló la firma.",
        2 => "LoTW rechazó el fichero.",
        3 => "Respuesta inesperada del servidor de TQSL.",
        4 => "Error interno de TQSL.",
        5 => "Error de la biblioteca de TQSL.",
        6 => "TQSL no pudo abrir el fichero de entrada.",
        7 => "TQSL no pudo escribir el fichero de salida.",
        8 => "No se subió nada: todo estaba ya subido o fuera del rango de fechas del certificado.",
        9 => "Se subió lo nuevo; lo ya subido o fuera de rango se dejó fuera.",
        10 => "Error de sintaxis en la llamada a TQSL.",
        11 => "No se pudo conectar con LoTW.",
        12 => "Error desconocido de TQSL.",
        13 => "La base de datos de subidas de TQSL estaba bloqueada.",
        14 => "Todos los contactos del fichero estaban ya subidos.",
        15 => "La frase de paso del certificado no es válida.",
        _ => $"TQSL devolvió el código {codigo}.",
    };

    private static void MatarConCuidado(Process proceso)
    {
        try
        {
            if (!proceso.HasExited) proceso.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // El proceso ya habia muerto por su cuenta.
        }
    }
}

/// <summary>No hay un TQSL utilizable en la maquina.</summary>
public sealed class TqslNoInstaladoException : Exception
{
    /// <summary>Crea la excepcion.</summary>
    /// <param name="mensaje">Explicacion para el operador.</param>
    public TqslNoInstaladoException(string mensaje) : base(mensaje) { }
}
