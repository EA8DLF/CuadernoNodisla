using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Idiomas;
using Serilog;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>
/// La bienvenida con el cuaderno recien creado: donde vive y como traerse lo que ya se tiene.
/// </summary>
/// <remarks>
/// <para>
/// Una rejilla vacia sin explicacion parece un programa roto. Y la primera pregunta de
/// cualquiera que estrena un cuaderno es «¿y lo que ya tengo?».
/// </para>
/// <para>
/// El parte de la importacion se ensena entero, y las <b>confirmaciones rescatadas</b>
/// aparte y destacadas: es la cifra que justifica fundir las copias del fichero en vez de
/// saltarlas, y cada una de ellas es un diploma que no se pierde.
/// </para>
/// </remarks>
public sealed partial class VistaModeloCuadernoVacio : ObservableObject
{
    private readonly ImportarAdif _importar;

    /// <summary>Monta la bienvenida.</summary>
    /// <param name="importar">Caso de uso de importacion.</param>
    /// <param name="rutaDelCuaderno">Fichero del cuaderno, para poder decir donde esta.</param>
    public VistaModeloCuadernoVacio(ImportarAdif importar, string rutaDelCuaderno)
    {
        _importar = importar ?? throw new ArgumentNullException(nameof(importar));
        RutaDelCuaderno = rutaDelCuaderno;
    }

    /// <summary>Donde esta el fichero del cuaderno.</summary>
    public string RutaDelCuaderno { get; }

    /// <summary>
    /// Donde Log4OM deja sus respaldos, que es lo mas probable que quiera traerse.
    /// </summary>
    /// <remarks>
    /// Abrir el dialogo ahi no importa nada por su cuenta: solo ahorra buscar la carpeta. Si no
    /// existe, el dialogo abre donde abriria normalmente.
    /// </remarks>
    public static string CarpetaDeRespaldosDeLog4Om => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Log4OM2",
        "backup");

    /// <summary>Donde se abre el dialogo de importar.</summary>
    public string CarpetaDeRespaldos => CarpetaDeRespaldosDeLog4Om;

    /// <summary>Cuantos contactos han entrado. Cero si no se ha importado nada.</summary>
    public int Anadidos { get; private set; }

    /// <summary>El parte de la importacion, escrito.</summary>
    [ObservableProperty]
    private string _parte = string.Empty;

    /// <summary>Las confirmaciones rescatadas, aparte porque es la cifra que importa.</summary>
    [ObservableProperty]
    private string _rescatadas = string.Empty;

    /// <summary>Lo que ha ido mal, si algo.</summary>
    [ObservableProperty]
    private string _aviso = string.Empty;

    /// <summary>Se esta importando.</summary>
    [ObservableProperty]
    private bool _ocupado;

    /// <summary>Trae un fichero ADIF al cuaderno y ensena el parte.</summary>
    /// <param name="ruta">Fichero elegido.</param>
    public async Task ImportarAsync(string ruta)
    {
        if (string.IsNullOrWhiteSpace(ruta)) return;

        Ocupado = true;
        Aviso = string.Empty;
        Parte = Textos.T("Libro.Vacio.Importando");
        Rescatadas = string.Empty;

        try
        {
            await using var fichero = File.OpenRead(ruta);
            var parte = await _importar.EjecutarAsync(fichero).ConfigureAwait(true);

            Anadidos = parte.Anadidos;
            Parte = Escribir(parte, ruta);

            // Al registro tambien: el parte de la primera importacion es lo que hay que poder
            // mirar despues si algo no cuadra.
            Log.Information(
                "Importado {Fichero}: {Leidos} leidos, {Anadidos} nuevos, {Fundidos} fundidos, "
                + "{YaEstaban} ya estaban, {Rescatadas} confirmaciones rescatadas, "
                + "{Choques} choques, cuadra={Cuadra}.",
                Path.GetFileName(ruta),
                parte.RegistrosLeidos,
                parte.Anadidos,
                parte.Fundidos,
                parte.YaEstaban,
                parte.ConfirmacionesRecuperadas,
                parte.Choques.Count,
                parte.NoSePierdeNada);

            Rescatadas = parte.ConfirmacionesRecuperadas > 0
                ? Textos.F("Libro.Vacio.Rescatadas", parte.ConfirmacionesRecuperadas)
                : string.Empty;

            if (!parte.NoSePierdeNada)
            {
                Aviso = Textos.T("Libro.Vacio.NoCuadra");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido importar el ADIF {Ruta}.", ruta);
            Parte = string.Empty;
            Aviso = Textos.F("Libro.Vacio.NoSeHaPodido", ex.Message);
        }
        finally
        {
            Ocupado = false;
        }
    }

    private static string Escribir(ResultadoDeImportacion parte, string ruta)
    {
        var texto = new System.Text.StringBuilder();

        texto.Append(Path.GetFileName(ruta)).Append(": ");
        texto.Append(Textos.F(
            "Libro.Vacio.Parte",
            parte.RegistrosLeidos,
            parte.Anadidos,
            parte.FundidosEnElFichero,
            parte.FundidosConElCuaderno,
            parte.YaEstaban));

        if (parte.ProgramaOrigen is { Length: > 0 } programa)
        {
            texto.Append(' ').Append(Textos.F("Libro.Vacio.Programa", programa));
        }

        texto.Append(' ').Append(Textos.F("Libro.Vacio.Duracion", parte.Duracion.TotalSeconds));

        if (parte.Choques.Count > 0)
        {
            texto.Append(' ').Append(Textos.F(
                "Libro.Vacio.Choques", parte.Choques.Count, Textos.T("Principal.Nav.Configuracion")));
        }

        if (parte.Avisos.Count > 0)
        {
            texto.Append(' ').Append(Textos.F("Libro.Vacio.Avisos", parte.Avisos.Count));
        }

        return texto.ToString();
    }
}
