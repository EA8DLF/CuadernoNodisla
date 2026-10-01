using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Servicios.Actualizaciones;
using Nodisla.Cuaderno.Ui.Ajustes;
using Nodisla.Cuaderno.Ui.Soporte;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>
/// El aviso de versiones nuevas: la barra discreta de la ventana principal y el apartado de
/// Ajustes con la casilla y el boton «Buscar actualizaciones».
/// </summary>
/// <remarks>
/// <para>
/// <b>Nunca roba el foco.</b> Al arrancar se pregunta a GitHub en segundo plano, como mucho una
/// vez al dia; si hay version nueva se enciende <see cref="AvisoVisible"/> y nada mas. No se
/// abre ninguna ventana hasta que el operador pulsa «Descargar e instalar», y entonces solo la
/// confirmacion.
/// </para>
/// <para>
/// Si GitHub no contesta, contesta 404 (repositorio privado) o se agota el tiempo, al arrancar
/// no se dice nada. Solo la busqueda a mano cuenta lo que ha pasado, en una linea de texto.
/// </para>
/// </remarks>
public sealed partial class VistaModeloActualizaciones : ObservableObject
{
    private readonly ComprobadorDeVersiones _comprobador;
    private readonly DescargadorDeInstalador _descargador;
    private readonly AjustesDeActualizaciones _ajustes;
    private readonly string _carpeta;
    private readonly TimeProvider _reloj;
    private readonly AccionesDelSistema _acciones;
    private readonly ILogger _log;
    private CancellationTokenSource? _cancelacionDeDescarga;
    private bool _arranqueHecho;

    /// <summary>Crea el modelo de vista.</summary>
    public VistaModeloActualizaciones(
        ComprobadorDeVersiones comprobador,
        DescargadorDeInstalador descargador,
        AjustesDeActualizaciones ajustes,
        string carpetaDeDatos,
        TimeProvider? reloj = null,
        AccionesDelSistema? acciones = null,
        ILogger<VistaModeloActualizaciones>? log = null)
    {
        _comprobador = comprobador ?? throw new ArgumentNullException(nameof(comprobador));
        _descargador = descargador ?? throw new ArgumentNullException(nameof(descargador));
        _ajustes = ajustes ?? throw new ArgumentNullException(nameof(ajustes));
        _carpeta = carpetaDeDatos;
        _reloj = reloj ?? TimeProvider.System;
        _acciones = acciones ?? new AccionesDelSistema();
        _log = (ILogger?)log ?? NullLogger.Instance;
        _comprobarAlArrancar = ajustes.ComprobarAlArrancar;
    }

    /// <summary>
    /// No sale a la red al arrancar aunque la casilla este marcada. Se pone con los puertos
    /// simulados: una sesion de pruebas no tiene por que hablar con GitHub. La busqueda a mano
    /// sigue funcionando.
    /// </summary>
    public bool SinComprobacionAlArrancar { get; init; }

    /// <summary>Version que esta corriendo, para el apartado de Ajustes.</summary>
    public string VersionActual => _comprobador.Instalada.ToString();

    /// <summary>Buscar versiones al arrancar. Se guarda al cambiar.</summary>
    [ObservableProperty]
    private bool _comprobarAlArrancar;

    /// <summary>La version nueva encontrada, o nula.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TituloDelAviso), nameof(Notas), nameof(SePuedeInstalar))]
    [NotifyCanExecuteChangedFor(nameof(DescargarEInstalarCommand), nameof(VerEnGitHubCommand))]
    private VersionPublicada? _nueva;

    /// <summary>Se ensena la barra del aviso.</summary>
    [ObservableProperty]
    private bool _avisoVisible;

    /// <summary>Hay una busqueda en marcha.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuscarActualizacionesCommand))]
    private bool _buscando;

    /// <summary>Hay una descarga en marcha.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DescargarEInstalarCommand), nameof(CancelarDescargaCommand))]
    private bool _descargando;

    /// <summary>Fraccion descargada, de 0 a 1.</summary>
    [ObservableProperty]
    private double _progreso;

    /// <summary>Linea de estado: resultado de la busqueda manual o de la descarga.</summary>
    [ObservableProperty]
    private string _estado = string.Empty;

    /// <summary>Texto de la barra.</summary>
    public string TituloDelAviso => Nueva is null
        ? string.Empty
        : $"Hay una versión nueva de Cuaderno NODISLA: {Nueva.Version} (tiene la {VersionActual}).";

    /// <summary>Notas de la version nueva.</summary>
    public string Notas => Nueva is null
        ? string.Empty
        : string.IsNullOrWhiteSpace(Nueva.Notas) ? "Esta versión no trae notas." : Nueva.Notas.Trim();

    /// <summary>La version nueva trae instalador y suma: se puede instalar desde aqui.</summary>
    public bool SePuedeInstalar => Nueva?.SePuedeInstalar == true;

    partial void OnComprobarAlArrancarChanged(bool value)
    {
        _ajustes.ComprobarAlArrancar = value;
        _ajustes.Guardar(_carpeta);
    }

    /// <summary>
    /// Comprobacion del arranque: en segundo plano, una sola vez por sesion y como mucho una vez
    /// al dia. No lanza nunca.
    /// </summary>
    /// <remarks>La llama la barra del aviso al cargarse; se puede llamar mas veces sin efecto.</remarks>
    public async Task ComprobarAlArrancarAsync()
    {
        if (_arranqueHecho) return;
        _arranqueHecho = true;

        if (SinComprobacionAlArrancar || !_ajustes.ComprobarAlArrancar) return;
        var ahora = _reloj.GetUtcNow();
        if (!PlanDeComprobacion.Toca(_ajustes.UltimaComprobacion, ahora)) return;

        try
        {
            var resultado = await Task.Run(() => _comprobador.ComprobarAsync());

            // Solo cuenta como hecha si se llego a saber algo; sin red se reintenta en el
            // siguiente arranque en vez de esperar un dia entero.
            if (resultado.Estado != EstadoDeComprobacion.NoDisponible)
            {
                _ajustes.UltimaComprobacion = ahora;
                _ajustes.Guardar(_carpeta);
            }

            if (resultado.Estado == EstadoDeComprobacion.HayVersionNueva
                && !string.Equals(_ajustes.VersionDescartada, resultado.Nueva!.Version.ToString(), StringComparison.Ordinal))
            {
                Nueva = resultado.Nueva;
                AvisoVisible = true;
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "La comprobación de versiones del arranque no ha terminado.");
        }
    }

    private bool PuedeBuscar() => !Buscando;

    /// <summary>Busqueda a mano: sin limite diario y contando el resultado.</summary>
    [RelayCommand(CanExecute = nameof(PuedeBuscar))]
    private async Task BuscarActualizacionesAsync()
    {
        Buscando = true;
        Estado = "Buscando…";
        try
        {
            var resultado = await Task.Run(() => _comprobador.ComprobarAsync());
            switch (resultado.Estado)
            {
                case EstadoDeComprobacion.HayVersionNueva:
                    Nueva = resultado.Nueva;
                    AvisoVisible = true;
                    Estado = $"Hay una versión nueva: {resultado.Nueva!.Version}.";
                    break;
                case EstadoDeComprobacion.AlDia:
                    Estado = $"Está al día (versión {VersionActual}).";
                    break;
                default:
                    Estado = $"No se ha podido comprobar ahora. {resultado.Motivo}".TrimEnd();
                    break;
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "La búsqueda de versiones no ha terminado.");
            Estado = "No se ha podido comprobar ahora.";
        }
        finally
        {
            Buscando = false;
        }
    }

    private bool PuedeDescargar() => Nueva is not null && !Descargando;

    /// <summary>
    /// Descarga el instalador, comprueba su SHA-256 y, si casa y el operador confirma, lo lanza y
    /// cierra el programa.
    /// </summary>
    [RelayCommand(CanExecute = nameof(PuedeDescargar))]
    private async Task DescargarEInstalarAsync()
    {
        var nueva = Nueva!;
        if (!nueva.SePuedeInstalar)
        {
            Estado = "Esta versión no trae instalador comprobable. Descárguela desde la página de GitHub.";
            return;
        }

        Descargando = true;
        Progreso = 0;
        Estado = "Descargando el instalador…";
        _cancelacionDeDescarga = new CancellationTokenSource();
        try
        {
            var progreso = new Progress<double>(p => Progreso = p);
            var token = _cancelacionDeDescarga.Token;
            var resultado = await Task.Run(() => _descargador.DescargarAsync(nueva, progreso, token), token);

            if (resultado.Estado != EstadoDeDescarga.Verificado)
            {
                Estado = resultado.Motivo ?? "No se ha podido descargar el instalador.";
                return;
            }

            Estado = "Instalador descargado y comprobado (SHA-256 correcta).";
            var seguir = _acciones.Confirmar(
                $"Instalar la versión {nueva.Version}",
                $"Se va a cerrar Cuaderno NODISLA y abrir el instalador de la versión {nueva.Version}.\n\n" +
                "Sus contactos y ajustes no se tocan. Si está transmitiendo o a mitad de un contacto, " +
                "cancele y termine primero.",
                "Cerrar e instalar");
            if (!seguir)
            {
                Estado = "Instalación aplazada. El instalador ya está descargado.";
                return;
            }

            _log.LogInformation("Se lanza el instalador {Ruta} y se cierra el programa.", resultado.Ruta);
            _acciones.LanzarInstalador(resultado.Ruta!);
            _acciones.CerrarPrograma();
        }
        catch (OperationCanceledException)
        {
            Estado = "Descarga cancelada.";
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "No se ha podido instalar la versión nueva.");
            Estado = "No se ha podido abrir el instalador.";
        }
        finally
        {
            _cancelacionDeDescarga?.Dispose();
            _cancelacionDeDescarga = null;
            Descargando = false;
        }
    }

    private bool PuedeCancelar() => Descargando;

    /// <summary>Corta la descarga en marcha.</summary>
    [RelayCommand(CanExecute = nameof(PuedeCancelar))]
    private void CancelarDescarga() => _cancelacionDeDescarga?.Cancel();

    private bool HayNueva() => Nueva is not null;

    /// <summary>Abre la pagina de la version en GitHub.</summary>
    [RelayCommand(CanExecute = nameof(HayNueva))]
    private void VerEnGitHub()
    {
        try
        {
            _acciones.AbrirEnNavegador(Nueva!.Pagina);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "No se ha podido abrir el navegador.");
            Estado = $"No se ha podido abrir el navegador. La página es {Nueva!.Pagina}";
        }
    }

    /// <summary>Oculta la barra; esa version ya no se vuelve a avisar al arrancar.</summary>
    [RelayCommand]
    private void Descartar()
    {
        AvisoVisible = false;
        if (Nueva is null) return;
        _ajustes.VersionDescartada = Nueva.Version.ToString();
        _ajustes.Guardar(_carpeta);
    }
}
