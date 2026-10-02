using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Radio.Control.Ft710;
using Nodisla.Cuaderno.Radio.Espectro;

namespace Nodisla.Cuaderno.Ui.Desarrollo;

/// <summary>
/// El analizador de la radio con los puertos simulados: hace lo que haria el FT-710.
/// </summary>
/// <remarks>
/// Manda unas once trazas por segundo (lo que mide la radio de verdad) con el SPAN, SPEED,
/// CENTER/CURSOR/FIX, 3DSS y EXPAND que tenga puestos el <see cref="EquipoSimulado"/>, como
/// hace la radio en el bloque de estado de cada trama. Asi se ve, sin radio, que las teclas de
/// la pantalla cambian el dibujo. Ruido, un par de señales fijas y una que se mueve.
/// </remarks>
public sealed class AnalizadorSimulado : IAnalizadorDeEspectro
{
    private readonly EquipoSimulado _equipo;
    private readonly Random _azar = new(7_10);
    private Timer? _reloj;
    private int _pasada;

    /// <summary>Monta el analizador simulado.</summary>
    /// <param name="equipo">El equipo simulado del que toma lo que tiene puesto.</param>
    public AnalizadorSimulado(EquipoSimulado equipo) => _equipo = equipo;

    /// <inheritdoc />
    public event EventHandler<TrazaDeEspectro>? TrazaRecibida;

    /// <inheritdoc />
    public event EventHandler? EstadoCambiado;

    /// <inheritdoc />
    public string Origen => Textos.T("Dialogos.Simulado.Analizador");

    /// <inheritdoc />
    public EstadoDelAnalizador Estado { get; private set; } = EstadoDelAnalizador.Parado;

    /// <inheritdoc />
    public string Motivo => string.Empty;

    /// <inheritdoc />
    public void Iniciar()
    {
        if (_reloj is not null) return;
        _reloj = new Timer(_ => Pasar(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(90));
        Estado = EstadoDelAnalizador.Recibiendo;
        EstadoCambiado?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public async Task DetenerAsync()
    {
        if (_reloj is { } reloj)
        {
            _reloj = null;
            await reloj.DisposeAsync().ConfigureAwait(false);
        }

        Estado = EstadoDelAnalizador.Parado;
        EstadoCambiado?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => new(DetenerAsync());

    private int Valor(MandoDeEquipo mando, int siNo) =>
        _equipo.LeerMandoAsync(mando).GetAwaiter().GetResult() is { } v ? (int)v : siNo;

    private void Pasar()
    {
        var indiceDeSpan = Math.Clamp(Valor(MandoDeEquipo.EspectroAncho, 7), 0, TramaDelAnalizadorFt710.Spans.Count - 1);
        var span = TramaDelAnalizadorFt710.Spans[indiceDeSpan];
        var vista = ModosDelAnalizadorFt710.Todos[Math.Clamp(Valor(MandoDeEquipo.EspectroModo, 4), 0, ModosDelAnalizadorFt710.Todos.Count - 1)];
        var posicion = vista.Posicion switch
        {
            1 => ModoDelAnalizador.Cursor,
            2 => ModoDelAnalizador.Fijo,
            _ => ModoDelAnalizador.Centro,
        };

        var vfo = _equipo.Estado.Frecuencia.Hercios;
        if (vfo <= 0) vfo = 14_210_000;
        var inicio = posicion == ModoDelAnalizador.Fijo ? vfo / 100_000 * 100_000 - span / 4 : vfo - span / 2;

        _pasada++;
        var niveles = new byte[TramaDelAnalizadorFt710.Puntos];
        for (var i = 0; i < niveles.Length; i++)
        {
            var hz = inicio + ((long)i * span / niveles.Length);
            double nivel = 40 + _azar.Next(0, 14);
            nivel += Senal(hz, vfo + 1_500, 900, 70);
            nivel += Senal(hz, vfo - 23_000, 1_500, 55);
            nivel += Senal(hz, vfo + 41_000 + ((_pasada % 200) * 60), 1_200, 60);
            niveles[i] = (byte)Math.Clamp(nivel, 0, 255);
        }

        TrazaRecibida?.Invoke(
            this,
            new TrazaDeEspectro(
                niveles, inicio, inicio + span, vfo, span, posicion,
                Valor(MandoDeEquipo.EspectroVelocidad, 2), vista.EsTresD, vista.Ampliado));
    }

    private static double Senal(long hz, long centro, double ancho, double alto)
    {
        var d = (hz - centro) / ancho;
        return alto * Math.Exp(-d * d);
    }
}
