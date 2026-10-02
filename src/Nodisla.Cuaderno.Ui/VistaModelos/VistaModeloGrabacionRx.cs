using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodisla.Cuaderno.Audio.Fonia;
using Nodisla.Cuaderno.Audio.Procesado;
using Nodisla.Cuaderno.Idiomas;
using Serilog;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>
/// El grabador continuo de la recepcion en pantalla: cuanto lleva, «Guardar lo ultimo» y el
/// adjunto al contacto.
/// </summary>
/// <remarks>
/// Lo guardado va a la carpeta <c>audio</c> de los datos, en WAV. Si esta pedido adjuntarlo, el
/// nombre del fichero pasa al contacto que se esta escribiendo (o modificando) y se guarda con
/// el. Escuchar el de un contacto es local: va a los altavoces del PC.
/// </remarks>
public sealed partial class VistaModeloGrabacionRx : ObservableObject
{
    private readonly ControlDeFonia _control;
    private readonly VistaModeloAjustesFonia _ajustes;
    private readonly IReproductorLocal _reproductor;
    private readonly string? _carpeta;
    private readonly Func<DateTimeOffset> _ahora;

    /// <summary>Monta el grabador en pantalla.</summary>
    /// <param name="control">La fonia, con su grabador.</param>
    /// <param name="ajustes">Ajustes de fonia: minutos, adjuntar y altavoces.</param>
    /// <param name="reproductor">Para escuchar en local.</param>
    /// <param name="carpetaDeDatos">Carpeta de datos; nula, no se guarda nada (pruebas).</param>
    /// <param name="entrada">El contacto que se esta escribiendo; puede faltar.</param>
    /// <param name="ahora">Reloj; nulo, el del sistema.</param>
    public VistaModeloGrabacionRx(
        ControlDeFonia control,
        VistaModeloAjustesFonia ajustes,
        IReproductorLocal reproductor,
        string? carpetaDeDatos,
        VistaModeloEntradaQso? entrada = null,
        Func<DateTimeOffset>? ahora = null)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(ajustes);
        ArgumentNullException.ThrowIfNull(reproductor);
        _control = control;
        _ajustes = ajustes;
        _reproductor = reproductor;
        _carpeta = string.IsNullOrWhiteSpace(carpetaDeDatos) ? null : Path.Combine(carpetaDeDatos, "audio");
        _ahora = ahora ?? (() => DateTimeOffset.UtcNow);
        Entrada = entrada;

        if (entrada is not null) entrada.EscucharAudio = EscucharFicheroAsync;
        Textos.AlCambiar(this, static vm => vm.OnPropertyChanged(nameof(TextoDelBoton)));
    }

    /// <summary>El formulario del contacto, para adjuntar.</summary>
    public VistaModeloEntradaQso? Entrada { get; }

    /// <summary>Carpeta del audio guardado, o nula.</summary>
    public string? Carpeta => _carpeta;

    /// <summary>Rotulo del boton, con los minutos.</summary>
    public string TextoDelBoton => Textos.F("Cabina.Fonia.Grabador.Guardar", _ajustes.MinutosDeGrabacion);

    [ObservableProperty]
    private string _grabado = string.Empty;

    [ObservableProperty]
    private string _aviso = string.Empty;

    [ObservableProperty]
    private string? _ultimoGuardado;

    /// <summary>Cuanto hay en memoria; lo llama el refresco del panel.</summary>
    public void Refrescar()
    {
        var hay = _control.Grabador.Grabado;
        Grabado = _control.Grabador.Activo && hay > TimeSpan.Zero
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)hay.TotalMinutes}:{hay.Seconds:00}")
            : string.Empty;
        OnPropertyChanged(nameof(TextoDelBoton));
    }

    /// <summary>Guarda en un WAV lo que haya en memoria y, si toca, lo adjunta al contacto.</summary>
    /// <returns>Ruta del fichero, o nula si no habia nada o no se pudo.</returns>
    public string? GuardarLoUltimo()
    {
        var audio = _control.Grabador.Instantanea();
        if (audio is null)
        {
            Aviso = Textos.T("Cabina.Fonia.Grabador.Nada");
            return null;
        }

        if (_carpeta is null)
        {
            Aviso = Textos.T("Cabina.Fonia.Grabador.SinCarpeta");
            return null;
        }

        var ahora = _ahora().UtcDateTime;
        var indicativo = Entrada is { Indicativo.Length: > 0 } e ? "-" + Limpio(e.Indicativo) : string.Empty;
        var nombre = string.Create(CultureInfo.InvariantCulture, $"rx-{ahora:yyyyMMdd-HHmmss}{indicativo}.wav");
        var ruta = Path.Combine(_carpeta, nombre);

        try
        {
            ArchivoWav.Guardar(ruta, audio);
        }
        catch (Exception fallo) when (fallo is IOException or UnauthorizedAccessException)
        {
            Log.Warning(fallo, "No se ha podido guardar la grabación de la recepción.");
            Aviso = Textos.F("Cabina.Fonia.Voz.NoGuarda", fallo.Message);
            return null;
        }

        UltimoGuardado = nombre;
        var duracion = string.Create(CultureInfo.InvariantCulture, $"{(int)audio.Duracion.TotalMinutes}:{audio.Duracion.Seconds:00}");
        if (_ajustes.AdjuntarAlQso && Entrada is not null)
        {
            Entrada.AudioAdjunto = nombre;
            Aviso = Textos.F("Cabina.Fonia.Grabador.Adjuntado", duracion, nombre);
        }
        else
        {
            Aviso = Textos.F("Cabina.Fonia.Grabador.Guardado", duracion, nombre);
        }

        Log.Information("Grabación de la recepción guardada en {Ruta}.", ruta);
        return ruta;
    }

    /// <summary>El boton «Guardar lo ultimo».</summary>
    [RelayCommand]
    private void Guardar() => GuardarLoUltimo();

    /// <summary>Suena un audio guardado por los altavoces del PC.</summary>
    /// <param name="nombre">Nombre del fichero dentro de la carpeta de audio.</param>
    public async Task EscucharFicheroAsync(string nombre)
    {
        if (_reproductor.Sonando)
        {
            _reproductor.Parar();
            return;
        }

        if (_carpeta is null || string.IsNullOrWhiteSpace(nombre)) return;

        // Solo el nombre: nada de rutas que salgan de la carpeta de audio.
        var ruta = Path.Combine(_carpeta, Path.GetFileName(nombre));
        if (!File.Exists(ruta))
        {
            if (Entrada is not null) Entrada.Mensaje = Textos.F("Cabina.Fonia.Grabador.NoEsta", nombre);
            return;
        }

        if (_ajustes.Altavoces is not { } altavoces)
        {
            if (Entrada is not null) Entrada.Mensaje = Textos.T("Cabina.Fonia.ElijaEscucha");
            return;
        }

        try
        {
            await _reproductor.ReproducirAsync(ArchivoWav.Leer(ruta), altavoces.Id).ConfigureAwait(true);
        }
        catch (Exception fallo)
        {
            Log.Warning(fallo, "No se ha podido escuchar el audio del contacto.");
            if (Entrada is not null) Entrada.Mensaje = Textos.F("Cabina.Fonia.Voz.NoSuena", fallo.Message);
        }
    }

    private static string Limpio(string indicativo) =>
        new(indicativo.Trim().ToUpperInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
}
