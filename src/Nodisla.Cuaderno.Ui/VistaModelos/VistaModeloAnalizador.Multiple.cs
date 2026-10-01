using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Ui.Recursos;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>
/// La tecla MULTI: el analizador en pequeño arriba y, debajo, el osciloscopio y el AF-FFT del
/// audio de recepcion, como la pantalla MULTI del FT-710 (manual de operacion, pag. 23).
/// </summary>
/// <remarks>
/// <para>
/// MULTI no tiene orden CAT, y no hace falta: es una vista de NUESTRA pantalla y no manda nada
/// a la radio. El osciloscopio y el AF-FFT salen del audio del codec USB, escuchando los
/// bloques de la entrada que ya abre el modem (WASAPI compartido); aqui no se abre ningun
/// dispositivo. Si esa entrada esta cerrada, los paneles lo dicen y ofrecen abrirla.
/// </para>
/// <para>
/// Lo que la radio deja tocar en esa pantalla (el atenuador del AF-FFT y el nivel y el barrido
/// del osciloscopio) no tiene orden CAT ni se ve en ninguna trama: aqui el nivel se ajusta solo
/// y el barrido es el de la foto del manual, 10 ms por division.
/// </para>
/// </remarks>
public sealed partial class VistaModeloAnalizador
{
    /// <summary>Lo menos que se espera entre dos pintados de los paneles de audio.</summary>
    private const int PausaEntrePintadosMs = 80;

    private readonly IEntradaDeAudio? _audio;
    private readonly PintorDelAudio _pintorDelAudio = new();
    private readonly object _cerrojoDelAudio = new();
    private float[] _ultimas = new float[PintorDelAudio.MuestrasNecesarias(48_000)];
    private int _llenas;
    private int _frecuenciaDelAudio = 48_000;
    private int _pintadoDelAudioPendiente;
    private long _ultimoPintadoDelAudio = long.MinValue / 2;
    private bool _escuchandoElAudio;

    /// <summary>La vista MULTI esta puesta (la tecla se ilumina).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RotuloEnPantalla))]
    private bool _multiple;

    /// <summary>La entrada de audio del codec esta abierta: hay osciloscopio y AF-FFT.</summary>
    [ObservableProperty]
    private bool _hayAudio;

    /// <summary>Bloques de audio que han llegado a los paneles de MULTI.</summary>
    public long BloquesDeAudio { get; private set; }

    /// <summary>El osciloscopio, 425 × 100 puntos.</summary>
    public WriteableBitmap? ImagenDelOsciloscopio { get; private set; }

    /// <summary>El AF-FFT, de 0 a 4 kHz, 425 × 100 puntos.</summary>
    public WriteableBitmap? ImagenDelAfFft { get; private set; }

    /// <summary>El pintor de los paneles de audio (para pruebas).</summary>
    public PintorDelAudio PintorDelAudio => _pintorDelAudio;

    /// <summary>El rotulo de encima tal como se ve: con «MULTI» si esta la vista multiple.</summary>
    public string RotuloEnPantalla => Multiple ? $"{Rotulo}  MULTI" : Rotulo;

    /// <summary>La tecla MULTI: pone o quita la vista multiple. No manda nada a la radio.</summary>
    [RelayCommand]
    public void AlternarMultiple() => Multiple = !Multiple;

    /// <summary>Pinta ya los paneles de audio con lo que haya (hilo de interfaz).</summary>
    public void PintarElAudio()
    {
        float[] muestras;
        int frecuencia;
        lock (_cerrojoDelAudio)
        {
            muestras = _ultimas[.._llenas];
            frecuencia = _frecuenciaDelAudio;
        }

        _ultimoPintadoDelAudio = _milisegundos();
        MirarElAudio();
        if (muestras.Length == 0) return;

        _pintorDelAudio.Pintar(muestras, frecuencia);
        if (ImagenDelOsciloscopio is null || ImagenDelAfFft is null)
        {
            ImagenDelOsciloscopio = new WriteableBitmap(PintorDelAudio.Ancho, PintorDelAudio.Alto, 96, 96, PixelFormats.Bgra32, null);
            ImagenDelAfFft = new WriteableBitmap(PintorDelAudio.Ancho, PintorDelAudio.Alto, 96, 96, PixelFormats.Bgra32, null);
            OnPropertyChanged(nameof(ImagenDelOsciloscopio));
            OnPropertyChanged(nameof(ImagenDelAfFft));
        }

        var rectangulo = new Int32Rect(0, 0, PintorDelAudio.Ancho, PintorDelAudio.Alto);
        ImagenDelOsciloscopio.WritePixels(rectangulo, _pintorDelAudio.Osciloscopio, PintorDelAudio.Ancho * 4, 0);
        ImagenDelAfFft.WritePixels(rectangulo, _pintorDelAudio.AfFft, PintorDelAudio.Ancho * 4, 0);
    }

    partial void OnMultipleChanged(bool value)
    {
        if (_audio is null)
        {
            MirarElAudio();
            return;
        }

        // Solo se escuchan los bloques mientras se ve MULTI.
        if (value && !_escuchandoElAudio)
        {
            _audio.BloqueCapturado += AlCapturarAudio;
            _escuchandoElAudio = true;
        }
        else if (!value && _escuchandoElAudio)
        {
            _audio.BloqueCapturado -= AlCapturarAudio;
            _escuchandoElAudio = false;
        }

        MirarElAudio();
    }

    /// <summary>Vuelve a mirar si la entrada del codec esta abierta.</summary>
    private void MirarElAudio() => HayAudio = _audio?.Abierto is not null;

    private void AlCapturarAudio(object? origen, BloqueDeAudio bloque)
    {
        var nuevas = bloque.Muestras.Span;
        lock (_cerrojoDelAudio)
        {
            if (bloque.FrecuenciaDeMuestreo > 0 && bloque.FrecuenciaDeMuestreo != _frecuenciaDelAudio)
            {
                _frecuenciaDelAudio = bloque.FrecuenciaDeMuestreo;
                _ultimas = new float[PintorDelAudio.MuestrasNecesarias(_frecuenciaDelAudio)];
                _llenas = 0;
            }

            var cabe = _ultimas.Length;
            if (nuevas.Length >= cabe)
            {
                nuevas[^cabe..].CopyTo(_ultimas);
                _llenas = cabe;
            }
            else
            {
                var sobran = Math.Max(0, _llenas + nuevas.Length - cabe);
                if (sobran > 0)
                {
                    Array.Copy(_ultimas, sobran, _ultimas, 0, _llenas - sobran);
                    _llenas -= sobran;
                }

                nuevas.CopyTo(_ultimas.AsSpan(_llenas));
                _llenas += nuevas.Length;
            }

            BloquesDeAudio++;
        }

        if (_milisegundos() - _ultimoPintadoDelAudio < PausaEntrePintadosMs) return;
        if (Interlocked.Exchange(ref _pintadoDelAudioPendiente, 1) == 0)
        {
            EnLaInterfaz(() =>
            {
                Interlocked.Exchange(ref _pintadoDelAudioPendiente, 0);
                PintarElAudio();
            });
        }
    }
}
