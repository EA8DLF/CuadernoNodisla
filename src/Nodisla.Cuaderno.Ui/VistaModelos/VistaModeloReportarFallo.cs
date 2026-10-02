using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Servicios.Actualizaciones;
using Nodisla.Cuaderno.Servicios.Informes;
using Nodisla.Cuaderno.Ui.Soporte;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>
/// «Reportar un fallo»: el formulario, lo que se adjunta y el envio a GitHub.
/// </summary>
/// <remarks>
/// <para>
/// <b>El operador ve todo lo que sale antes de que salga.</b> Lo adjunto (version, Windows,
/// radio, modo CAT y, si quiere, el final del registro) se rellena ya limpio en cuadros que se
/// pueden editar, y <see cref="VistaPrevia"/> es exactamente el cuerpo que se va a mandar.
/// </para>
/// <para>
/// Enviar no publica nada: abre en el navegador la pagina de nueva incidencia de GitHub con el
/// titulo y el cuerpo ya puestos, y el operador la manda con su cuenta. En el programa no hay
/// ninguna ficha de acceso a GitHub.
/// </para>
/// <para>
/// <b>Idioma.</b> Lo que ve el operador (rotulos, avisos, estado) va en su idioma, pero el
/// cuerpo de la incidencia (<see cref="InformeDeFallo"/>, con sus encabezados y los datos del
/// entorno) sigue en espanol: va dirigido al desarrollador y conviene que todas las incidencias
/// se lean igual. El operador puede escribir en su idioma lo que cuenta.
/// </para>
/// </remarks>
public sealed partial class VistaModeloReportarFallo : ObservableObject
{
    private readonly Func<string> _describirEntorno;
    private readonly Func<string> _leerRegistro;
    private readonly AccionesDelSistema _acciones;
    private readonly RepositorioPublico _repositorio;
    private readonly ILogger _log;

    /// <summary>Crea el formulario.</summary>
    /// <param name="describirEntorno">Devuelve el texto del entorno, ya limpio.</param>
    /// <param name="leerRegistro">Devuelve las ultimas lineas del registro, ya limpias.</param>
    /// <param name="acciones">Navegador y portapapeles.</param>
    /// <param name="repositorio">Repositorio; por omision el de Cuaderno NODISLA.</param>
    /// <param name="log">Registro.</param>
    public VistaModeloReportarFallo(
        Func<string> describirEntorno,
        Func<string> leerRegistro,
        AccionesDelSistema? acciones = null,
        RepositorioPublico? repositorio = null,
        ILogger<VistaModeloReportarFallo>? log = null)
    {
        _describirEntorno = describirEntorno ?? throw new ArgumentNullException(nameof(describirEntorno));
        _leerRegistro = leerRegistro ?? throw new ArgumentNullException(nameof(leerRegistro));
        _acciones = acciones ?? new AccionesDelSistema();
        _repositorio = repositorio ?? RepositorioPublico.CuadernoNodisla;
        _log = (ILogger?)log ?? NullLogger.Instance;
        _entorno = describirEntorno();
    }

    /// <summary>Titulo de la incidencia.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VistaPrevia))]
    [NotifyCanExecuteChangedFor(nameof(EnviarCommand))]
    private string _titulo = string.Empty;

    /// <summary>Que paso.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VistaPrevia))]
    [NotifyCanExecuteChangedFor(nameof(EnviarCommand))]
    private string _quePaso = string.Empty;

    /// <summary>Que esperaba que pasara.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VistaPrevia))]
    private string _queEsperaba = string.Empty;

    /// <summary>Pasos para reproducirlo.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VistaPrevia))]
    private string _pasos = string.Empty;

    /// <summary>Version, Windows, radio y CAT: rellenado y editable.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VistaPrevia))]
    private string _entorno;

    /// <summary>Adjuntar el final del registro.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VistaPrevia))]
    private bool _incluirRegistro;

    /// <summary>Final del registro: rellenado al marcar la casilla y editable.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VistaPrevia))]
    private string _registro = string.Empty;

    /// <summary>Linea de estado tras enviar.</summary>
    [ObservableProperty]
    private string _estado = string.Empty;

    /// <summary>El cuerpo tal cual se va a mandar, ya limpio.</summary>
    public string VistaPrevia => InformeDeFallo.Cuerpo(Datos());

    partial void OnIncluirRegistroChanged(bool value)
    {
        if (value && string.IsNullOrWhiteSpace(Registro)) Registro = _leerRegistro();
    }

    private DatosDeInforme Datos() => new()
    {
        Titulo = Titulo,
        QuePaso = QuePaso,
        QueEsperaba = QueEsperaba,
        Pasos = Pasos,
        Entorno = Entorno,
        Registro = IncluirRegistro ? Registro : string.Empty,
    };

    /// <summary>Vuelve a rellenar el entorno y el registro (deshace las ediciones).</summary>
    [RelayCommand]
    private void RecargarAdjuntos()
    {
        Entorno = _describirEntorno();
        if (IncluirRegistro) Registro = _leerRegistro();
    }

    private bool PuedeEnviar() => !string.IsNullOrWhiteSpace(Titulo) && !string.IsNullOrWhiteSpace(QuePaso);

    /// <summary>Abre la incidencia en el navegador (y copia el cuerpo si no cabe en el enlace).</summary>
    [RelayCommand(CanExecute = nameof(PuedeEnviar))]
    private void Enviar()
    {
        var envio = InformeDeFallo.Preparar(Datos(), _repositorio);
        try
        {
            if (envio.CuerpoEnElPortapapeles)
            {
                _acciones.CopiarAlPortapapeles(envio.CuerpoCompleto);
            }

            _acciones.AbrirEnNavegador(envio.Direccion);
            Estado = envio.CuerpoEnElPortapapeles
                ? Textos.T("Ayuda.Fallo.EstadoPortapapeles")
                : Textos.T("Ayuda.Fallo.EstadoAbierto");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "No se ha podido abrir el informe de fallo en el navegador.");
            try
            {
                _acciones.CopiarAlPortapapeles(envio.CuerpoCompleto);
                Estado = Textos.F("Ayuda.Fallo.EstadoSinNavegador", _repositorio.NuevaIncidencia);
            }
            catch (Exception ex2)
            {
                _log.LogWarning(ex2, "Tampoco se ha podido copiar al portapapeles.");
                Estado = Textos.F("Ayuda.Fallo.EstadoSinNada", _repositorio.NuevaIncidencia);
            }
        }
    }

    /// <summary>Deja el formulario en blanco (el entorno se vuelve a rellenar).</summary>
    [RelayCommand]
    private void Vaciar()
    {
        Titulo = QuePaso = QueEsperaba = Pasos = Registro = Estado = string.Empty;
        IncluirRegistro = false;
        Entorno = _describirEntorno();
    }
}
