using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Radio.Control.Ft710;
using Nodisla.Cuaderno.Radio.Espectro;
using Serilog;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>
/// Los mandos del equipo por su nombre, para enlazarlos desde el frontal dibujado:
/// <c>{Binding Frontal[Bloqueo].Encendido}</c>. Un mando que el equipo no tiene da nulo.
/// </summary>
public sealed class MandosDelFrontal
{
    private readonly Func<MandoDeEquipo, VistaModeloMando?> _buscar;

    internal MandosDelFrontal(Func<MandoDeEquipo, VistaModeloMando?> buscar) => _buscar = buscar;

    /// <summary>El mando con ese nombre de <see cref="MandoDeEquipo"/>, o nulo.</summary>
    /// <param name="nombre">Nombre del mando.</param>
    public VistaModeloMando? this[string nombre] =>
        Enum.TryParse<MandoDeEquipo>(nombre, out var mando) ? _buscar(mando) : null;
}

/// <summary>
/// Como <see cref="MandosDelFrontal"/>, pero un mando que el equipo no tiene da un mando
/// «ausente» (apagado, sin valor) en vez de nulo. Es para los pilotos y rotulos del dibujo: un
/// enlace que pasa por un nulo queda roto aunque lleve valor de reserva.
/// </summary>
public sealed class PilotosDelFrontal
{
    private readonly MandosDelFrontal _mandos;

    internal PilotosDelFrontal(MandosDelFrontal mandos) => _mandos = mandos;

    /// <summary>El mando con ese nombre, o <see cref="MandoAusente.Instancia"/>.</summary>
    /// <param name="nombre">Nombre del mando.</param>
    public object this[string nombre] => (object?)_mandos[nombre] ?? MandoAusente.Instancia;
}

/// <summary>Un mando que el equipo no tiene: apagado, sin valor y sin texto.</summary>
public sealed class MandoAusente
{
    /// <summary>El unico mando ausente.</summary>
    public static MandoAusente Instancia { get; } = new();

    private MandoAusente()
    {
    }

    /// <summary>Siempre apagado.</summary>
    public bool Encendido => false;

    /// <summary>Sin valor.</summary>
    public string? ValorTexto => null;

    /// <summary>No esta disponible.</summary>
    public bool Disponible => false;
}

/// <summary>
/// Que ajusta cada posicion de los mandos FUNC y DSP del FT-710 (ordenes SF0 y SF1).
/// </summary>
public static class FuncionesDelFrontalFt710
{
    /// <summary>
    /// Mando que ajusta el FUNC en cada una de sus funciones (1…17, en el orden del manual CAT).
    /// M-GROUP (6) no tiene orden CAT: nulo.
    /// </summary>
    public static IReadOnlyList<MandoDeEquipo?> DelFunc { get; } =
    [
        null,
        MandoDeEquipo.EspectroNivel,
        MandoDeEquipo.EspectroPicos,
        MandoDeEquipo.EspectroColor,
        MandoDeEquipo.ContrastePantalla,
        MandoDeEquipo.BrilloPantalla,
        null,
        MandoDeEquipo.GananciaMicrofono,
        MandoDeEquipo.Compresor,
        MandoDeEquipo.NivelAmc,
        MandoDeEquipo.GananciaVox,
        MandoDeEquipo.RetardoVox,
        MandoDeEquipo.AntiVox,
        MandoDeEquipo.Potencia,
        MandoDeEquipo.Monitor,
        MandoDeEquipo.VelocidadKeyer,
        MandoDeEquipo.TonoCw,
        MandoDeEquipo.RetardoBreakIn,
    ];

    /// <summary>Mando que ajusta el DSP en cada funcion (1 SHIFT … 5 APF).</summary>
    public static IReadOnlyList<MandoDeEquipo?> DelDsp { get; } =
    [
        null,
        MandoDeEquipo.DesplazamientoFi,
        MandoDeEquipo.AnchoDeFiltro,
        MandoDeEquipo.FrecuenciaDeMuesca,
        MandoDeEquipo.FrecuenciaDeContorno,
        MandoDeEquipo.FrecuenciaApf,
    ];

    /// <summary>El mando que corresponde a una funcion, o nulo.</summary>
    /// <param name="tabla">Tabla de funciones.</param>
    /// <param name="funcion">Valor de SF0/SF1.</param>
    /// <returns>El mando, o nulo.</returns>
    public static MandoDeEquipo? Buscar(IReadOnlyList<MandoDeEquipo?> tabla, double? funcion)
    {
        ArgumentNullException.ThrowIfNull(tabla);
        if (funcion is not { } f) return null;
        var i = (int)Math.Round(f);
        return i >= 0 && i < tabla.Count ? tabla[i] : null;
    }
}

/// <summary>Lo que hay detras de las teclas, diales y mandos del frontal dibujado.</summary>
public sealed partial class VistaModeloEquipo
{
    /// <summary>
    /// Mandos que se vuelven a leer en cada vuelta de la medicion, porque se ven en el frontal y
    /// el operador los puede cambiar desde la radio.
    /// </summary>
    private static readonly MandoDeEquipo[] DelFrontal =
    [
        MandoDeEquipo.SupresorDeRuido, MandoDeEquipo.ReductorDeRuido, MandoDeEquipo.FiltroEstrecho,
        MandoDeEquipo.Split, MandoDeEquipo.Vox, MandoDeEquipo.Sintonizador, MandoDeEquipo.Bloqueo,
        MandoDeEquipo.SintoniaFinaRapida, MandoDeEquipo.Rit, MandoDeEquipo.GananciaRf,
        MandoDeEquipo.Silenciador, MandoDeEquipo.Volumen, MandoDeEquipo.EspectroModo,
        MandoDeEquipo.EspectroAncho, MandoDeEquipo.EspectroVelocidad, MandoDeEquipo.FuncionDelMandoFunc,
        MandoDeEquipo.FuncionDelMandoDsp, MandoDeEquipo.Atenuador, MandoDeEquipo.Preamplificador,
        MandoDeEquipo.MuescaAutomatica, MandoDeEquipo.Agc,
    ];

    /// <summary>Cuantos mandos del resto de la lista se releen en cada vuelta.</summary>
    private const int DelRestoPorVuelta = 3;

    private MandosDelFrontal? _frontal;
    private ITransmisionEnCurso? _mox;
    private int _vueltaDelResto;
    private bool _refrescando;

    /// <summary>Los mandos por nombre, para el frontal dibujado.</summary>
    public MandosDelFrontal Frontal => _frontal ??= new MandosDelFrontal(MandoDe);

    /// <summary>Los mandos por nombre, sin nulos: para pilotos y rotulos del dibujo.</summary>
    public PilotosDelFrontal Pilotos => new(Frontal);

    /// <summary>El mando que ajusta ahora el FUNC (lo que diga SF0).</summary>
    public VistaModeloMando? MandoDelFunc =>
        FuncionesDelFrontalFt710.Buscar(FuncionesDelFrontalFt710.DelFunc, MandoDe(MandoDeEquipo.FuncionDelMandoFunc)?.Valor) is { } m
            ? MandoDe(m)
            : null;

    /// <summary>El mando que ajusta ahora el DSP (lo que diga SF1).</summary>
    public VistaModeloMando? MandoDelDsp =>
        FuncionesDelFrontalFt710.Buscar(FuncionesDelFrontalFt710.DelDsp, MandoDe(MandoDeEquipo.FuncionDelMandoDsp)?.Valor) is { } m
            ? MandoDe(m)
            : null;

    /// <summary>Rotulo de la funcion del FUNC, como en la pantalla del equipo.</summary>
    public string RotuloDelFunc => MandoDe(MandoDeEquipo.FuncionDelMandoFunc)?.ValorTexto ?? "FUNC";

    /// <summary>Valor del mando que ajusta ahora el FUNC (p. ej. «50» con RF POWER). Vacio si no se sabe.</summary>
    public string ValorDelFunc => MandoDelFunc?.ValorTexto ?? string.Empty;

    /// <summary>Rotulo de la funcion del DSP.</summary>
    public string RotuloDelDsp => MandoDe(MandoDeEquipo.FuncionDelMandoDsp)?.ValorTexto ?? "DSP";

    /// <summary>El analizador del equipo esta en 3DSS.</summary>
    public bool AnalizadorEnTresD => ModoDelAnalizador is { EsTresD: true };

    /// <summary>La cascada del analizador esta ampliada (EXPAND).</summary>
    public bool AnalizadorAmpliado => ModoDelAnalizador is { Ampliado: true };

    /// <summary>CENTER, CURSOR o FIX, lo que tenga puesto el analizador.</summary>
    public string RotuloDeCenter => ModoDelAnalizador?.Posicion switch
    {
        1 => "CURSOR",
        2 => "FIX",
        _ => "CENTER",
    };

    /// <summary>
    /// Como tiene la radio su analizador segun el CAT (SPAN, SPEED, CENTER/CURSOR/FIX, 3DSS y
    /// EXPAND), para que el dibujo cambie en cuanto se toca una tecla de la pantalla o el
    /// sondeo ve un cambio hecho en la propia radio. Nulo si el equipo no lo da.
    /// </summary>
    public AjusteDelAnalizador? AjusteDelAnalizador
    {
        get
        {
            if (ModoDelAnalizador is not { } modo) return null;
            int? span = MandoDe(MandoDeEquipo.EspectroAncho) is { Disponible: true } ancho
                && (int)ancho.Valor is var i && i >= 0 && i < TramaDelAnalizadorFt710.Spans.Count
                    ? TramaDelAnalizadorFt710.Spans[i]
                    : null;
            int? velocidad = MandoDe(MandoDeEquipo.EspectroVelocidad) is { Disponible: true } v ? (int)v.Valor : null;
            var posicion = modo.Posicion switch
            {
                1 => Nodisla.Cuaderno.Aplicacion.Puertos.ModoDelAnalizador.Cursor,
                2 => Nodisla.Cuaderno.Aplicacion.Puertos.ModoDelAnalizador.Fijo,
                _ => Nodisla.Cuaderno.Aplicacion.Puertos.ModoDelAnalizador.Centro,
            };
            return new AjusteDelAnalizador(span, velocidad, posicion, modo.EsTresD, modo.Ampliado);
        }
    }

    /// <summary>El equipo esta en memorias.</summary>
    public bool EnMemoria => Real is IEquipoConDosVfos dos && dos.Vfos.EnMemoria;

    /// <summary>Se esta transmitiendo con MOX desde el frontal.</summary>
    public bool EnMox => _mox is not null;

    private ModoDelAnalizadorFt710? ModoDelAnalizador =>
        MandoDe(MandoDeEquipo.EspectroModo) is { Disponible: true } m
        && (int)m.Valor is var i && i >= 0 && i < ModosDelAnalizadorFt710.Todos.Count
            ? ModosDelAnalizadorFt710.Todos[i]
            : null;

    /// <summary>Pulsa una tecla del equipo (M▶V, V/M, QMB, BAND, ZIN, DSP RESET, A/B…).</summary>
    /// <param name="tecla">Tecla pulsada.</param>
    [RelayCommand(CanExecute = nameof(SePuedePulsar))]
    public async Task PulsarTeclaAsync(TeclaDelEquipo tecla)
    {
        if (Real is not IEquipoConTeclas conTeclas) return;

        // Guardar en la QMB empuja la pila de memorias rapidas del equipo: no tiene vuelta atras.
        if (tecla == TeclaDelEquipo.GuardarMemoriaRapida
            && !Confirmar("Guardar la frecuencia actual en la memoria rápida (QMB) del equipo."))
        {
            return;
        }

        try
        {
            await conTeclas.PulsarAsync(tecla).ConfigureAwait(true);
            await RefrescarElFrontalAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido pulsar {Tecla}.", tecla);
            Aviso = $"No se ha podido pulsar {tecla}: {ex.Message}";
        }
    }

    /// <summary>Gira el dial principal.</summary>
    /// <param name="muescas">Muescas: positivas suben.</param>
    public async Task GirarDialAsync(int muescas)
    {
        if (!Conectado || Transmitiendo || Real is not IEquipoConTeclas conTeclas) return;

        try
        {
            await conTeclas.GirarDialAsync(muescas).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "No se ha podido girar el dial.");
            Aviso = $"No se ha podido girar el dial: {ex.Message}";
        }
    }

    /// <summary>Gira el anillo STEP/MCH.</summary>
    /// <param name="muescas">Muescas: positivas suben.</param>
    public async Task GirarPasosAsync(int muescas)
    {
        if (!Conectado || Transmitiendo || Real is not IEquipoConTeclas conTeclas) return;

        try
        {
            await conTeclas.GirarPasosAsync(muescas).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "No se ha podido girar STEP/MCH.");
            Aviso = $"No se ha podido girar STEP/MCH: {ex.Message}";
        }
    }

    /// <summary>
    /// Pulsa el FUNC o el DSP: pasa a la funcion siguiente (<c>+</c>) o anterior (<c>-</c>).
    /// </summary>
    /// <param name="que">«FUNC+», «FUNC-», «DSP+» o «DSP-».</param>
    [RelayCommand(CanExecute = nameof(SePuedeAccionar))]
    public void CambiarFuncion(string? que)
    {
        if (que is null || que.Length < 2) return;
        var mando = que.StartsWith("DSP", StringComparison.Ordinal)
            ? MandoDeEquipo.FuncionDelMandoDsp
            : MandoDeEquipo.FuncionDelMandoFunc;
        if (MandoDe(mando) is not { Disponible: true } vista) return;

        var sentido = que[^1] == '-' ? -1 : 1;
        var tabla = mando == MandoDeEquipo.FuncionDelMandoDsp ? FuncionesDelFrontalFt710.DelDsp : FuncionesDelFrontalFt710.DelFunc;
        var siguiente = (int)vista.Valor;
        for (var i = 0; i < tabla.Count; i++)
        {
            siguiente += sentido;
            if (siguiente > vista.Maximo) siguiente = (int)vista.Minimo;
            if (siguiente < vista.Minimo) siguiente = (int)vista.Maximo;

            // Se saltan las funciones sin orden CAT (M-GROUP): girar el FUNC en ellas no haria nada.
            if (tabla[siguiente] is { } m && MandoDe(m) is not null) break;
        }

        vista.Valor = siguiente;
    }

    /// <summary>Pasa un mando de posiciones a la siguiente, dando la vuelta (FINE/FAST).</summary>
    /// <param name="mando">Mando que se mueve.</param>
    [RelayCommand(CanExecute = nameof(SePuedeAlternar))]
    public void SiguientePosicion(MandoDeEquipo mando)
    {
        if (MandoDe(mando) is not { Disponible: true, SoloLectura: false } vista) return;
        var n = vista.Valor + vista.Paso;
        vista.Valor = n > vista.Maximo ? vista.Minimo : n;
    }

    /// <summary>Teclas tactiles de la pantalla: CENTER, 3DSS, EXPAND, SPAN y SPEED.</summary>
    /// <param name="tecla">«CENTER», «3DSS», «EXPAND», «SPAN+», «SPAN-», «SPEED+» o «SPEED-».</param>
    [RelayCommand(CanExecute = nameof(SePuedeTocarElAnalizador))]
    public void TeclaDelAnalizador(string? tecla)
    {
        switch (tecla)
        {
            case "CENTER" or "3DSS" or "EXPAND":
                if (MandoDe(MandoDeEquipo.EspectroModo) is not { Disponible: true } modo) return;
                var actual = Math.Clamp((int)modo.Valor, 0, ModosDelAnalizadorFt710.Todos.Count - 1);
                var puesto = ModosDelAnalizadorFt710.Todos[actual];
                modo.Valor = tecla switch
                {
                    "CENTER" => ModosDelAnalizadorFt710.TrasCenter(actual),
                    "3DSS" => ModosDelAnalizadorFt710.TrasTresD(actual),

                    // En 3DSS el FT-710 no tiene ampliado por CAT (probado el 29-09-2026: SS06C,
                    // D y E no hacen nada): EXPAND pasa a la cascada ampliada, que si la admite.
                    _ when puesto.EsTresD => ModosDelAnalizadorFt710.Indice(tresD: false, puesto.Posicion, ampliado: true),
                    _ => ModosDelAnalizadorFt710.TrasExpand(actual),
                };
                return;
            case "SPAN+" or "SPAN-" or "SPEED+" or "SPEED-":
                var mando = tecla.StartsWith("SPAN", StringComparison.Ordinal)
                    ? MandoDeEquipo.EspectroAncho
                    : MandoDeEquipo.EspectroVelocidad;
                if (MandoDe(mando) is not { Disponible: true } vista) return;
                var n = vista.Valor + (tecla[^1] == '-' ? -1 : 1);
                vista.Valor = n > vista.Maximo ? vista.Minimo : n < vista.Minimo ? vista.Maximo : n;
                return;
        }
    }

    /// <summary>
    /// La rueda sobre CLAR: mueve el desplazamiento del clarificador 10 Hz por muesca.
    /// </summary>
    /// <param name="muescas">Muescas: positivas suben.</param>
    public void MoverClarificador(int muescas)
    {
        if (!Conectado || MandoDe(MandoDeEquipo.DesplazamientoRit) is not { Disponible: true } vista) return;
        vista.Valor = Math.Clamp(vista.Valor + (muescas * vista.Paso), vista.Minimo, vista.Maximo);
    }

    /// <summary>
    /// MOX: pone el equipo en antena por el vigilante del PTT; volver a pulsar lo quita.
    /// </summary>
    /// <remarks>
    /// Es la misma transmision vigilada que usa todo el programa: tope de tiempo, latido (lo da
    /// la medicion cada medio segundo) y suelta por todas las vias si algo se tuerce.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(SePuedeAccionar))]
    public async Task MoxAsync()
    {
        if (_mox is { } enCurso)
        {
            _mox = null;
            OnPropertyChanged(nameof(EnMox));
            await enCurso.DisposeAsync().ConfigureAwait(true);
            Aviso = "MOX quitado: PTT abajo.";
            return;
        }

        if (ConfirmarQueVaATransmitir is { } preguntar && !preguntar("MOX (transmitir)")) return;

        try
        {
            _mox = await _vigilante.PedirAntenaAsync("MOX desde el frontal").ConfigureAwait(true);
            OnPropertyChanged(nameof(EnMox));
            Aviso = "MOX: el equipo está en antena. Vuelva a pulsar para quitarlo.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido poner MOX.");
            Aviso = $"No se ha podido poner MOX: {ex.Message}";
        }
    }

    /// <summary>
    /// TUNE mantenida: el acoplador sintoniza (emite portadora) dentro de una transmision vigilada.
    /// </summary>
    /// <param name="tope">Lo mas que se deja sintonizando.</param>
    /// <returns>Verdadero si el acoplador empezo y termino dentro del tope.</returns>
    public async Task<bool> SintonizarAsync(TimeSpan tope)
    {
        if (!Conectado || _mox is not null || Real is not Radio.Control.IEquipoConSintonia ft710) return false;
        if (ConfirmarQueVaATransmitir is { } preguntar && !preguntar("Sintonizar el acoplador (emite portadora)")) return false;

        try
        {
            ft710.PrepararSintonia();
            bool termino;
            await using (var antena = await _vigilante.PedirAntenaAsync("TUNE: sintonizar el acoplador").ConfigureAwait(true))
            {
                termino = await ft710.EsperarFinDeSintoniaAsync(antena.Latir, tope).ConfigureAwait(true);
            }

            Aviso = termino
                ? "Acoplador sintonizado."
                : !ft710.VeElFinDeLaSintonia
                    ? $"Sintonía lanzada. El {NombreDelEquipo} no dice por CAT cuándo termina: se ha soltado a los "
                      + $"{Math.Min(tope.TotalSeconds, ControlFt710.SintoniaSinIndicador.TotalSeconds):N0} s. Mire la ROE en la radio."
                    : $"El acoplador no terminó en {tope.TotalSeconds:N0} s: se ha cortado.";
            await RefrescarElFrontalAsync().ConfigureAwait(true);
            return termino;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Fallo sintonizando el acoplador.");
            Aviso = $"No se ha podido sintonizar: {ex.Message}";
            return false;
        }
    }

    /// <summary>TUNE mantenida desde el frontal (clic derecho), con el tope de diez segundos.</summary>
    [RelayCommand(CanExecute = nameof(SePuedeSintonizar))]
    public Task SintonizarAcopladorAsync() => SintonizarAsync(TimeSpan.FromSeconds(10));

    /// <summary>
    /// Vuelve a leer los mandos del frontal y unos pocos del resto, para que la pantalla ensene
    /// lo que se toca en la propia radio.
    /// </summary>
    public async Task RefrescarElFrontalAsync()
    {
        if (_refrescando || !Conectado || Transmitiendo || Mandos.Count == 0) return;
        _refrescando = true;
        try
        {
            foreach (var mando in DelFrontal)
            {
                if (MandoDe(mando) is { } vista) await vista.RecogerSiNoSeEstaMoviendoAsync().ConfigureAwait(true);
            }

            foreach (var extra in new[] { MandoDelFunc, MandoDelDsp })
            {
                if (extra is not null) await extra.RecogerSiNoSeEstaMoviendoAsync().ConfigureAwait(true);
            }

            var resto = Mandos.Where(m => Array.IndexOf(DelFrontal, m.Mando) < 0).ToList();
            for (var i = 0; i < DelRestoPorVuelta && resto.Count > 0; i++)
            {
                _vueltaDelResto = (_vueltaDelResto + 1) % resto.Count;
                await resto[_vueltaDelResto].RecogerSiNoSeEstaMoviendoAsync().ConfigureAwait(true);
            }

            RecogerLosIndicadores();
            OnPropertyChanged(nameof(EnMemoria));
        }
        finally
        {
            _refrescando = false;
        }
    }

    private bool SePuedePulsar(TeclaDelEquipo tecla) =>
        Conectado && Real is IEquipoConTeclas conTeclas && conTeclas.Teclas.Contains(tecla);

    private bool SePuedeTocarElAnalizador(string? tecla) =>
        Conectado && tecla switch
        {
            "CENTER" or "3DSS" or "EXPAND" => MandoDe(MandoDeEquipo.EspectroModo) is { Disponible: true },
            "SPAN+" or "SPAN-" => MandoDe(MandoDeEquipo.EspectroAncho) is { Disponible: true },
            "SPEED+" or "SPEED-" => MandoDe(MandoDeEquipo.EspectroVelocidad) is { Disponible: true },
            _ => false,
        };

    private bool SePuedeSintonizar() =>
        Conectado && Real is Radio.Control.IEquipoConSintonia && MandoDe(MandoDeEquipo.Sintonizador) is { Disponible: true };

    /// <summary>Se engancha a los mandos que cambian lo que ensena el frontal.</summary>
    private void EscucharLosMandosDelFrontal()
    {
        _frontal = null;
        foreach (var mando in Mandos)
        {
            mando.PropertyChanged += AlCambiarUnMandoDelFrontal;
        }

        AvisarDelFrontal();
    }

    private void AlCambiarUnMandoDelFrontal(object? origen, PropertyChangedEventArgs e)
    {
        if (origen is not VistaModeloMando vista || e.PropertyName is not (nameof(VistaModeloMando.Valor) or nameof(VistaModeloMando.Disponible)))
        {
            return;
        }

        switch (vista.Mando)
        {
            case MandoDeEquipo.FuncionDelMandoFunc or MandoDeEquipo.FuncionDelMandoDsp:
                OnPropertyChanged(nameof(MandoDelFunc));
                OnPropertyChanged(nameof(MandoDelDsp));
                OnPropertyChanged(nameof(RotuloDelFunc));
                OnPropertyChanged(nameof(ValorDelFunc));
                OnPropertyChanged(nameof(RotuloDelDsp));
                break;
            case MandoDeEquipo.EspectroModo:
                OnPropertyChanged(nameof(AnalizadorEnTresD));
                OnPropertyChanged(nameof(AnalizadorAmpliado));
                OnPropertyChanged(nameof(RotuloDeCenter));
                OnPropertyChanged(nameof(AjusteDelAnalizador));
                TeclaDelAnalizadorCommand.NotifyCanExecuteChanged();
                break;
            case MandoDeEquipo.EspectroAncho or MandoDeEquipo.EspectroVelocidad:
                OnPropertyChanged(nameof(AjusteDelAnalizador));
                TeclaDelAnalizadorCommand.NotifyCanExecuteChanged();
                break;
        }

        // El valor del mando que lleva ahora el FUNC, para la etiqueta ámbar del visor.
        if (ReferenceEquals(vista, MandoDelFunc))
        {
            OnPropertyChanged(nameof(ValorDelFunc));
        }
    }

    private void AvisarDelFrontal()
    {
        OnPropertyChanged(nameof(Frontal));
        OnPropertyChanged(nameof(Pilotos));
        OnPropertyChanged(nameof(MandoDelFunc));
        OnPropertyChanged(nameof(MandoDelDsp));
        OnPropertyChanged(nameof(RotuloDelFunc));
        OnPropertyChanged(nameof(ValorDelFunc));
        OnPropertyChanged(nameof(RotuloDelDsp));
        OnPropertyChanged(nameof(AnalizadorEnTresD));
        OnPropertyChanged(nameof(AnalizadorAmpliado));
        OnPropertyChanged(nameof(RotuloDeCenter));
        OnPropertyChanged(nameof(AjusteDelAnalizador));
        OnPropertyChanged(nameof(EnMemoria));
        PulsarTeclaCommand.NotifyCanExecuteChanged();
        SiguientePosicionCommand.NotifyCanExecuteChanged();
        AlternarMandoCommand.NotifyCanExecuteChanged();
        CambiarFuncionCommand.NotifyCanExecuteChanged();
        TeclaDelAnalizadorCommand.NotifyCanExecuteChanged();
        MoxCommand.NotifyCanExecuteChanged();
        SintonizarAcopladorCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Latido del MOX: lo da la medicion mientras el operador lo tenga puesto.</summary>
    private void LatirElMox() => _mox?.Latir();
}
