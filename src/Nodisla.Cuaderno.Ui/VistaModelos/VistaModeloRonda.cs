using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Ui.Conversores;
using Serilog;
using DominioBanda = Nodisla.Cuaderno.Dominio.Valores.Banda;
using DominioModo = Nodisla.Cuaderno.Dominio.Valores.Modo;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>Un participante de la ronda tal y como se ve y se edita en la lista.</summary>
public sealed partial class FilaDeParticipante : ObservableObject
{
    /// <summary>Monta la fila sobre el participante del dominio.</summary>
    public FilaDeParticipante(ParticipanteDeRonda participante)
    {
        ArgumentNullException.ThrowIfNull(participante);
        Participante = participante;
        Refrescar();
    }

    /// <summary>El participante del dominio, tal y como lo guarda el repositorio.</summary>
    public ParticipanteDeRonda Participante { get; }

    /// <summary>Indicativo, no editable una vez anadido.</summary>
    public string Indicativo => Participante.Call.Valor;

    /// <summary>Hora UTC de entrada en la ronda.</summary>
    public string HoraEntrada => Participante.EntradaUtc.UtcDateTime.ToString("HH:mm:ss");

    /// <summary>Pais resuelto, si lo hay.</summary>
    public string Pais => Participante.Pais ?? string.Empty;

    [ObservableProperty]
    private string _rstEnviadoTexto = string.Empty;

    [ObservableProperty]
    private string _rstRecibidoTexto = string.Empty;

    [ObservableProperty]
    private string _comentario = string.Empty;

    [ObservableProperty]
    private bool _trabajado;

    /// <summary>Vuelca en la fila lo que haya en el participante del dominio.</summary>
    public void Refrescar()
    {
        RstEnviadoTexto = Participante.RstEnviado.EsVacio ? string.Empty : Participante.RstEnviado.Texto;
        RstRecibidoTexto = Participante.RstRecibido.EsVacio ? string.Empty : Participante.RstRecibido.Texto;
        Comentario = Participante.Comentario ?? string.Empty;
        Trabajado = Participante.Trabajado;
    }

    /// <summary>Copia lo tecleado en la fila al participante del dominio, sin guardarlo todavia.</summary>
    public void VolcarEnElDominio()
    {
        Participante.RstEnviado = Informe.Parse(RstEnviadoTexto);
        Participante.RstRecibido = Informe.Parse(RstRecibidoTexto);
        Participante.Comentario = string.IsNullOrWhiteSpace(Comentario) ? null : Comentario.Trim();
    }
}

/// <summary>
/// Panel de control de una ronda (NET Control): se abre con una banda, un modo y una
/// frecuencia, los participantes van entrando por su indicativo, y cada uno que se confirma se
/// convierte en un contacto real del cuaderno con la hora en que entro.
/// </summary>
/// <remarks>
/// Solo hay una ronda abierta a la vez, y se reabre sola al arrancar el programa: es el
/// repositorio quien sabe si hay una sin cerrar, no un fichero de estado aparte. Ver
/// <see cref="GestionarRonda"/> para el porque.
/// </remarks>
public sealed partial class VistaModeloRonda(
    GestionarRonda gestionar,
    IRepositorioEstacion estaciones) : ObservableObject
{
    /// <summary>Se dispara cuando un participante se confirma y entra en el cuaderno.</summary>
    public event EventHandler? CuadernoCambiado;

    /// <summary>Perfiles de estacion para elegir con quien se dirige la ronda.</summary>
    public ObservableCollection<Estacion> Estaciones { get; } = [];

    /// <summary>Participantes de la ronda abierta, en el orden en que fueron entrando.</summary>
    public ObservableCollection<FilaDeParticipante> Participantes { get; } = [];

    /// <summary>Historial de rondas, la mas reciente primero.</summary>
    public ObservableCollection<RondaDeControl> Historial { get; } = [];

    /// <summary>Bandas que se ofrecen al abrir una ronda.</summary>
    public IReadOnlyList<string> Bandas { get; } = DominioBanda.Todas.Select(b => b.Nombre).ToArray();

    /// <summary>Modos habituales de una red, delante del resto del catalogo.</summary>
    public IReadOnlyList<string> Modos { get; } = ModosDeRed();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayRondaAbierta))]
    [NotifyPropertyChangedFor(nameof(TituloDeLaRonda))]
    [NotifyCanExecuteChangedFor(nameof(AbrirCommand))]
    [NotifyCanExecuteChangedFor(nameof(CerrarCommand))]
    [NotifyCanExecuteChangedFor(nameof(AnadirParticipanteCommand))]
    private RondaDeControl? _rondaAbierta;

    [ObservableProperty]
    private Estacion? _estacionElegida;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AbrirCommand))]
    private string _nombreRonda = string.Empty;

    [ObservableProperty]
    private string _clubOEvento = string.Empty;

    [ObservableProperty]
    private string _banda = "40m";

    [ObservableProperty]
    private string _modo = "SSB";

    [ObservableProperty]
    private string _frecuencia = string.Empty;

    [ObservableProperty]
    private string _notas = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnadirParticipanteCommand))]
    private string _indicativoNuevo = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarParticipanteCommand))]
    [NotifyCanExecuteChangedFor(nameof(MarcarTrabajadoCommand))]
    [NotifyCanExecuteChangedFor(nameof(EliminarParticipanteCommand))]
    private FilaDeParticipante? _participanteElegido;

    [ObservableProperty]
    private string _mensaje = string.Empty;

    [ObservableProperty]
    private TonoDeMensaje _tono;

    /// <summary>Hay una ronda abierta ahora mismo.</summary>
    public bool HayRondaAbierta => RondaAbierta is not null;

    /// <summary>Cabecera de la ronda abierta, para la pantalla.</summary>
    public string TituloDeLaRonda => RondaAbierta is { } r
        ? string.Join(" · ", new[] { r.Nombre, r.Band.Nombre, r.Mode.NombreUsual, TextoDeFrecuencia.Escribir(r.Freq) is { Length: > 0 } mhz ? $"{mhz} MHz" : string.Empty }.Where(t => !string.IsNullOrWhiteSpace(t)))
        : "Sin ronda abierta";

    /// <summary>
    /// Carga los perfiles de estacion, el historial y reabre la ronda si quedo una abierta.
    /// </summary>
    public async Task CargarAsync(CancellationToken ct = default)
    {
        try
        {
            var perfiles = await estaciones.TodasAsync(ct: ct).ConfigureAwait(true);
            Estaciones.Clear();
            foreach (var e in perfiles) Estaciones.Add(e);

            var predeterminada = await estaciones.PredeterminadaAsync(ct).ConfigureAwait(true);
            EstacionElegida = predeterminada is null
                ? Estaciones.FirstOrDefault()
                : Estaciones.FirstOrDefault(e => e.Id == predeterminada.Id) ?? predeterminada;

            var abierta = await gestionar.ObtenerAbiertaAsync(ct).ConfigureAwait(true);
            if (abierta is not null) CargarRonda(abierta);

            await RefrescarHistorialAsync(ct).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido cargar la ronda de control.");
            Mensaje = $"No se ha podido cargar la ronda: {ex.Message}";
            Tono = TonoDeMensaje.Error;
        }
    }

    /// <summary>Abre una ronda nueva con los datos del formulario.</summary>
    [RelayCommand(CanExecute = nameof(SePuedeAbrir))]
    public async Task AbrirAsync()
    {
        if (string.IsNullOrWhiteSpace(NombreRonda))
        {
            Mensaje = "Falta el nombre de la ronda.";
            Tono = TonoDeMensaje.Error;
            return;
        }

        if (!DominioModo.TryParse(Modo, null, out var modo))
        {
            Mensaje = $"El modo «{Modo}» no está en la tabla de ADIF.";
            Tono = TonoDeMensaje.Error;
            return;
        }

        DominioBanda.TryParse(Banda, out var banda);

        // Una frecuencia mal tecleada no se tira a cero en silencio: cada contacto confirmado
        // en la ronda saldria sin frecuencia.
        var freq = Dominio.Valores.Frecuencia.Cero;
        if (!string.IsNullOrWhiteSpace(Frecuencia) && !TextoDeFrecuencia.TryLeer(Frecuencia, out freq))
        {
            Mensaje = $"La frecuencia «{Frecuencia}» no se entiende. Escríbala en MHz, por ejemplo 7.150.";
            Tono = TonoDeMensaje.Error;
            return;
        }

        try
        {
            var ronda = await gestionar
                .AbrirAsync(new PeticionDeRonda
                {
                    Nombre = NombreRonda.Trim(),
                    ClubOEvento = ClubOEvento,
                    Band = banda,
                    Mode = modo,
                    Freq = freq,
                    EstacionId = EstacionElegida?.Id,
                    Notas = Notas,
                })
                .ConfigureAwait(true);

            CargarRonda(ronda);
            Mensaje = $"Ronda «{ronda.Nombre}» abierta.";
            Tono = TonoDeMensaje.Correcto;
            await RefrescarHistorialAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido abrir la ronda.");
            Mensaje = $"No se ha podido abrir la ronda: {ex.Message}";
            Tono = TonoDeMensaje.Error;
        }
    }

    /// <summary>Cierra la ronda abierta. Los participantes ya anadidos quedan como estaban.</summary>
    [RelayCommand(CanExecute = nameof(HayRondaAbierta))]
    public async Task CerrarAsync()
    {
        if (RondaAbierta is not { } ronda) return;

        try
        {
            await gestionar.CerrarAsync(ronda.Id).ConfigureAwait(true);
            RondaAbierta = null;
            Participantes.Clear();
            ParticipanteElegido = null;
            Mensaje = $"Ronda «{ronda.Nombre}» cerrada.";
            Tono = TonoDeMensaje.Correcto;
            await RefrescarHistorialAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido cerrar la ronda.");
            Mensaje = $"No se ha podido cerrar la ronda: {ex.Message}";
            Tono = TonoDeMensaje.Error;
        }
    }

    /// <summary>Anade el indicativo tecleado como participante nuevo.</summary>
    [RelayCommand(CanExecute = nameof(SePuedeAnadirParticipante))]
    public async Task AnadirParticipanteAsync()
    {
        if (RondaAbierta is not { } ronda) return;

        try
        {
            var resultado = await gestionar.AnadirParticipanteAsync(ronda.Id, IndicativoNuevo).ConfigureAwait(true);
            if (!resultado.Correcto)
            {
                Mensaje = resultado.Error ?? "No se ha podido añadir el participante.";
                Tono = TonoDeMensaje.Error;
                return;
            }

            var fila = new FilaDeParticipante(resultado.Participante!);
            Participantes.Add(fila);
            ParticipanteElegido = fila;
            IndicativoNuevo = string.Empty;
            Mensaje = string.IsNullOrEmpty(fila.Pais)
                ? $"{fila.Indicativo} entra en la ronda."
                : $"{fila.Indicativo} ({fila.Pais}) entra en la ronda.";
            Tono = TonoDeMensaje.Correcto;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido anadir un participante a la ronda.");
            Mensaje = $"No se ha podido añadir el participante: {ex.Message}";
            Tono = TonoDeMensaje.Error;
        }
    }

    /// <summary>Guarda el RST y el comentario tecleados para el participante elegido.</summary>
    [RelayCommand(CanExecute = nameof(HayParticipanteElegido))]
    public async Task GuardarParticipanteAsync()
    {
        if (ParticipanteElegido is not { } fila) return;

        try
        {
            fila.VolcarEnElDominio();
            await gestionar.ActualizarParticipanteAsync(fila.Participante).ConfigureAwait(true);
            Mensaje = $"Guardado el RST y el comentario de {fila.Indicativo}.";
            Tono = TonoDeMensaje.Correcto;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido guardar el participante de la ronda.");
            Mensaje = $"No se ha podido guardar: {ex.Message}";
            Tono = TonoDeMensaje.Error;
        }
    }

    /// <summary>
    /// Confirma el participante elegido como contacto trabajado: guarda primero lo tecleado en
    /// la fila y despues lo convierte en un QSO real del cuaderno.
    /// </summary>
    [RelayCommand(CanExecute = nameof(SePuedeMarcarTrabajado))]
    public async Task MarcarTrabajadoAsync()
    {
        if (RondaAbierta is not { } ronda || ParticipanteElegido is not { } fila) return;

        try
        {
            fila.VolcarEnElDominio();
            var resultado = await gestionar.MarcarTrabajadoAsync(ronda, fila.Participante).ConfigureAwait(true);

            if (!resultado.Correcto)
            {
                Mensaje = resultado.Duplicado is not null
                    ? $"{fila.Indicativo} ya está en el cuaderno con esa hora, banda y modo."
                    : $"No se ha podido guardar: {string.Join("; ", resultado.Errores)}";
                Tono = TonoDeMensaje.Error;
                return;
            }

            fila.Refrescar();

            // Ya confirmado: el boton se apaga. Antes seguia encendido y un segundo clic metia
            // el mismo contacto otra vez en el cuaderno.
            MarcarTrabajadoCommand.NotifyCanExecuteChanged();
            Mensaje = $"{fila.Indicativo} añadido al cuaderno.";
            Tono = TonoDeMensaje.Correcto;
            CuadernoCambiado?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido confirmar un participante de la ronda.");
            Mensaje = $"No se ha podido confirmar el contacto: {ex.Message}";
            Tono = TonoDeMensaje.Error;
        }
    }

    /// <summary>Quita de la ronda al participante elegido, sin tocar el cuaderno.</summary>
    [RelayCommand(CanExecute = nameof(HayParticipanteElegido))]
    public async Task EliminarParticipanteAsync()
    {
        if (ParticipanteElegido is not { } fila) return;

        try
        {
            await gestionar.EliminarParticipanteAsync(fila.Participante.Id).ConfigureAwait(true);
            Participantes.Remove(fila);
            ParticipanteElegido = null;
            Mensaje = fila.Trabajado
                ? $"{fila.Indicativo} quitado de la ronda. Su contacto sigue en el cuaderno."
                : $"{fila.Indicativo} quitado de la ronda.";
            Tono = TonoDeMensaje.Correcto;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido quitar un participante de la ronda.");
            Mensaje = $"No se ha podido quitar el participante: {ex.Message}";
            Tono = TonoDeMensaje.Error;
        }
    }

    private bool SePuedeAbrir() => !HayRondaAbierta && !string.IsNullOrWhiteSpace(NombreRonda);

    private bool SePuedeAnadirParticipante() => HayRondaAbierta && !string.IsNullOrWhiteSpace(IndicativoNuevo);

    private bool HayParticipanteElegido() => ParticipanteElegido is not null;

    private bool SePuedeMarcarTrabajado() => ParticipanteElegido is { Trabajado: false };

    private void CargarRonda(RondaDeControl ronda)
    {
        RondaAbierta = ronda;
        Participantes.Clear();
        foreach (var p in ronda.Participantes.OrderBy(p => p.EntradaUtc))
        {
            Participantes.Add(new FilaDeParticipante(p));
        }

        NombreRonda = ronda.Nombre;
        ClubOEvento = ronda.ClubOEvento ?? string.Empty;
        Banda = ronda.Band.EsVacia ? Banda : ronda.Band.Nombre;
        Modo = ronda.Mode.EsVacio ? Modo : ronda.Mode.NombreUsual;
        Frecuencia = ronda.Freq.EsCero ? Frecuencia : TextoDeFrecuencia.Escribir(ronda.Freq);
        Notas = ronda.Notas ?? string.Empty;
    }

    private async Task RefrescarHistorialAsync(CancellationToken ct = default)
    {
        var todas = await gestionar.ListarAsync(ct).ConfigureAwait(true);
        Historial.Clear();
        foreach (var r in todas) Historial.Add(r);
    }

    /// <summary>Modos habituales de una red de radioaficionados, delante del resto del catalogo.</summary>
    private static IReadOnlyList<string> ModosDeRed()
    {
        string[] primeros = ["SSB", "USB", "LSB", "FM", "CW", "AM", "DMR", "DSTAR", "C4FM", "M17"];

        var resto = DominioModo.ModosPrincipales
            .Where(m => !primeros.Contains(m, StringComparer.OrdinalIgnoreCase))
            .OrderBy(m => m, StringComparer.OrdinalIgnoreCase);

        return [.. primeros, .. resto];
    }
}
