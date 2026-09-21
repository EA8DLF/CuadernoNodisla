using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;
using DominioBanda = Nodisla.Cuaderno.Dominio.Valores.Banda;
using DominioFrecuencia = Nodisla.Cuaderno.Dominio.Valores.Frecuencia;
using DominioIndicativo = Nodisla.Cuaderno.Dominio.Valores.Indicativo;
using DominioModo = Nodisla.Cuaderno.Dominio.Valores.Modo;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>Tono del mensaje que se le ensena al operador bajo el formulario.</summary>
public enum TonoDeMensaje
{
    /// <summary>No hay nada que decir.</summary>
    Ninguno,
    /// <summary>Ha salido bien.</summary>
    Correcto,
    /// <summary>Hace falta una decision del operador.</summary>
    Aviso,
    /// <summary>No se ha podido guardar.</summary>
    Error,
}

/// <summary>
/// Formulario de entrada de contactos. Esta pensado para teclear deprisa durante un pileup:
/// Intro registra, Escape limpia y nada obliga a soltar el teclado.
/// </summary>
public sealed partial class VistaModeloEntradaQso : ObservableObject
{
    private readonly RegistrarQso _registrar;
    private readonly EditarQso _editar;
    private readonly ConsultarTrabajadoAntes _consultarTrabajadoAntes;

    private CancellationTokenSource? _consultaEnCurso;
    private bool _silencio;
    private bool _insistirConElDuplicado;
    private string _ultimoInformePorOmision = string.Empty;

    /// <summary>Crea el formulario con los casos de uso que necesita.</summary>
    public VistaModeloEntradaQso(
        RegistrarQso registrar,
        EditarQso editar,
        ConsultarTrabajadoAntes consultarTrabajadoAntes)
    {
        _registrar = registrar;
        _editar = editar;
        _consultarTrabajadoAntes = consultarTrabajadoAntes;

        Bandas = DominioBanda.Todas.Select(b => b.Nombre).ToArray();
        Modos = ModosHabituales();

        Limpiar();
    }

    /// <summary>Se dispara cuando el cuaderno ha cambiado y la rejilla debe refrescarse.</summary>
    public event EventHandler? CuadernoCambiado;

    /// <summary>Bandas que se ofrecen en la lista desplegable.</summary>
    public IReadOnlyList<string> Bandas { get; }

    /// <summary>Modos que se ofrecen en la lista desplegable, los habituales primero.</summary>
    public IReadOnlyList<string> Modos { get; }

    /// <summary>Contactos anteriores con el mismo indicativo, para verlos de un vistazo.</summary>
    public ObservableCollection<FilaDeQso> ContactosPrevios { get; } = [];

    /// <summary>Perfil de estacion con el que se registra. Lo fija la ventana principal.</summary>
    public long? EstacionId { get; set; }

    [ObservableProperty]
    private string _indicativo = string.Empty;

    [ObservableProperty]
    private string _banda = "20m";

    [ObservableProperty]
    private string _modo = "SSB";

    [ObservableProperty]
    private string _frecuencia = "14.200";

    [ObservableProperty]
    private string _informeEnviado = string.Empty;

    [ObservableProperty]
    private string _informeRecibido = string.Empty;

    [ObservableProperty]
    private string _fechaUtc = string.Empty;

    [ObservableProperty]
    private string _horaUtc = string.Empty;

    [ObservableProperty]
    private bool _horaAutomatica = true;

    [ObservableProperty]
    private string _nombre = string.Empty;

    [ObservableProperty]
    private string _qth = string.Empty;

    [ObservableProperty]
    private string _localizador = string.Empty;

    [ObservableProperty]
    private string _comentario = string.Empty;

    [ObservableProperty]
    private string _mensaje = string.Empty;

    [ObservableProperty]
    private TonoDeMensaje _tono = TonoDeMensaje.Ninguno;

    [ObservableProperty]
    private string _avisoTrabajadoAntes = string.Empty;

    [ObservableProperty]
    private bool _hayTrabajadoAntes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EnEdicion))]
    [NotifyPropertyChangedFor(nameof(TituloDelPanel))]
    [NotifyPropertyChangedFor(nameof(TextoDelBotonGuardar))]
    private long _idEnEdicion;

    /// <summary>Se esta modificando un contacto que ya estaba en el cuaderno.</summary>
    public bool EnEdicion => IdEnEdicion > 0;

    /// <summary>Titulo del panel, que cambia segun se este dando de alta o modificando.</summary>
    public string TituloDelPanel => EnEdicion ? "Modificar contacto" : "Contacto nuevo";

    /// <summary>Texto del boton principal.</summary>
    public string TextoDelBotonGuardar => EnEdicion ? "Guardar cambios (Intro)" : "Registrar contacto (Intro)";

    /// <summary>Pone la hora en los campos mientras el operador no la haya fijado a mano.</summary>
    public void ActualizarReloj(DateTimeOffset utc)
    {
        if (!HoraAutomatica || EnEdicion) return;
        _silencio = true;
        FechaUtc = utc.UtcDateTime.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
        HoraUtc = utc.UtcDateTime.ToString("HH:mm", CultureInfo.InvariantCulture);
        _silencio = false;
    }

    /// <summary>Carga un contacto del cuaderno en el formulario para modificarlo.</summary>
    public void CargarParaEditar(Qso qso)
    {
        ArgumentNullException.ThrowIfNull(qso);

        _silencio = true;
        IdEnEdicion = qso.Id;
        HoraAutomatica = false;
        Indicativo = qso.Call.Valor;
        Banda = qso.Band.Nombre;
        Modo = qso.Mode.NombreUsual;
        Frecuencia = TextoDeFrecuencia.Escribir(qso.Freq);
        InformeEnviado = qso.RstSent.Texto;
        InformeRecibido = qso.RstRcvd.Texto;
        FechaUtc = qso.InicioUtc.UtcDateTime.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
        HoraUtc = qso.InicioUtc.UtcDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        Nombre = qso.Name ?? string.Empty;
        Qth = qso.Qth ?? string.Empty;
        Localizador = qso.Gridsquare.Valor;
        Comentario = qso.Comentario ?? string.Empty;
        _silencio = false;

        Mensaje = $"Modificando el contacto con {qso.Call.Valor}. Escape cancela.";
        Tono = TonoDeMensaje.Aviso;
        LanzarConsultaDeTrabajadoAntes(Indicativo);
    }

    /// <summary>Registra el contacto, o guarda los cambios si se estaba modificando uno.</summary>
    [RelayCommand]
    private async Task GuardarAsync()
    {
        var qso = ConstruirQso();
        if (qso is null) return;

        if (EnEdicion)
        {
            qso.Id = IdEnEdicion;
            var edicion = await _editar.EjecutarAsync(
                new PeticionDeEdicion { Qso = qso, EstacionId = EstacionId }).ConfigureAwait(true);

            if (edicion.NoEncontrado)
            {
                Mensaje = "Ese contacto ya no está en el cuaderno.";
                Tono = TonoDeMensaje.Error;
                return;
            }
            if (!edicion.Correcto)
            {
                Mensaje = string.Join("  ", edicion.Errores);
                Tono = TonoDeMensaje.Error;
                return;
            }

            Mensaje = $"Cambios guardados en el contacto con {qso.Call.Valor}.";
            Tono = TonoDeMensaje.Correcto;
            Vaciar(conservarMensaje: true);
            CuadernoCambiado?.Invoke(this, EventArgs.Empty);
            return;
        }

        var registro = await _registrar.EjecutarAsync(new PeticionDeRegistro
        {
            Qso = qso,
            EstacionId = EstacionId,
            AdmitirDuplicado = _insistirConElDuplicado,
        }).ConfigureAwait(true);

        if (registro.Duplicado is { } duplicado)
        {
            _insistirConElDuplicado = true;
            var cuando = duplicado.InicioUtc.UtcDateTime.ToString("dd-MM-yyyy HH:mm", CultureInfo.InvariantCulture);
            Mensaje = $"Ya hay un contacto con {duplicado.Call.Valor} en {duplicado.Band.Nombre} " +
                      $"{duplicado.Mode.NombreUsual} el {cuando} UTC. Pulse Intro otra vez para registrarlo igualmente.";
            Tono = TonoDeMensaje.Aviso;
            return;
        }

        if (!registro.Correcto)
        {
            Mensaje = string.Join("  ", registro.Errores);
            Tono = TonoDeMensaje.Error;
            return;
        }

        Mensaje = $"Contacto con {qso.Call.Valor} registrado en {qso.Band.Nombre} {qso.Mode.NombreUsual}.";
        Tono = TonoDeMensaje.Correcto;
        Vaciar(conservarMensaje: true);
        CuadernoCambiado?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Vacia el formulario y cancela la modificacion en curso.</summary>
    [RelayCommand]
    private void Limpiar() => Vaciar(conservarMensaje: false);

    /// <summary>Pone en los campos de fecha y hora el instante actual en UTC.</summary>
    [RelayCommand]
    private void PonerHoraActual()
    {
        HoraAutomatica = true;
        ActualizarReloj(DateTimeOffset.UtcNow);
    }

    /// <summary>Deja el formulario listo para el siguiente corresponsal sin perder banda ni modo.</summary>
    private void Vaciar(bool conservarMensaje)
    {
        _silencio = true;
        IdEnEdicion = 0;
        _insistirConElDuplicado = false;
        Indicativo = string.Empty;
        Nombre = string.Empty;
        Qth = string.Empty;
        Localizador = string.Empty;
        Comentario = string.Empty;
        HoraAutomatica = true;
        _silencio = false;

        PonerInformesPorOmision(forzar: true);
        ActualizarReloj(DateTimeOffset.UtcNow);
        LimpiarTrabajadoAntes();

        if (conservarMensaje) return;
        Mensaje = string.Empty;
        Tono = TonoDeMensaje.Ninguno;
    }

    private Qso? ConstruirQso()
    {
        if (!DominioIndicativo.TryParse(Indicativo, out var call))
        {
            Mensaje = string.IsNullOrWhiteSpace(Indicativo)
                ? "Falta el indicativo del corresponsal."
                : $"El indicativo «{Indicativo}» no tiene una forma válida.";
            Tono = TonoDeMensaje.Error;
            return null;
        }

        if (!DominioModo.TryParse(Modo, null, out var modo))
        {
            Mensaje = $"El modo «{Modo}» no está en la tabla de ADIF.";
            Tono = TonoDeMensaje.Error;
            return null;
        }

        DominioBanda.TryParse(Banda, out var banda);
        var frecuencia = LeerFrecuencia();

        if (!LeerInstante(out var inicio))
        {
            Mensaje = "La fecha o la hora no se entienden. Use dd-mm-aaaa y hh:mm en UTC.";
            Tono = TonoDeMensaje.Error;
            return null;
        }

        Locator.TryParse(Localizador, out var localizador);

        return new Qso
        {
            Call = call,
            Band = banda,
            Mode = modo,
            Freq = frecuencia,
            InicioUtc = inicio,
            RstSent = Informe.Parse(InformeEnviado),
            RstRcvd = Informe.Parse(InformeRecibido),
            Name = Vacio(Nombre),
            Qth = Vacio(Qth),
            Gridsquare = localizador,
            Comentario = Vacio(Comentario),
        };
    }

    /// <summary>
    /// Lee la frecuencia tecleada. Siempre con punto decimal, aunque el programa hable espanol;
    /// la regla y el porque estan en <see cref="TextoDeFrecuencia"/>.
    /// </summary>
    private DominioFrecuencia LeerFrecuencia() => TextoDeFrecuencia.Leer(Frecuencia);

    private bool LeerInstante(out DateTimeOffset instante)
    {
        if (HoraAutomatica)
        {
            var ahora = DateTimeOffset.UtcNow;
            instante = new DateTimeOffset(
                ahora.UtcDateTime.Year, ahora.UtcDateTime.Month, ahora.UtcDateTime.Day,
                ahora.UtcDateTime.Hour, ahora.UtcDateTime.Minute, ahora.UtcDateTime.Second, TimeSpan.Zero);
            return true;
        }

        instante = default;
        var formatosFecha = new[] { "dd-MM-yyyy", "dd/MM/yyyy", "yyyy-MM-dd", "ddMMyyyy" };
        var formatosHora = new[] { "HH:mm:ss", "HH:mm", "HHmmss", "HHmm" };

        if (!DateTime.TryParseExact(FechaUtc?.Trim(), formatosFecha,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha))
        {
            return false;
        }

        if (!DateTime.TryParseExact(HoraUtc?.Trim(), formatosHora,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var hora))
        {
            return false;
        }

        instante = new DateTimeOffset(
            fecha.Year, fecha.Month, fecha.Day, hora.Hour, hora.Minute, hora.Second, TimeSpan.Zero);
        return true;
    }

    partial void OnIndicativoChanged(string value)
    {
        _insistirConElDuplicado = false;
        if (_silencio) return;
        LanzarConsultaDeTrabajadoAntes(value);
    }

    partial void OnModoChanged(string value)
    {
        if (_silencio) return;
        PonerInformesPorOmision(forzar: false);
    }

    partial void OnBandaChanged(string value)
    {
        if (_silencio) return;
        if (!DominioBanda.TryParse(value, out var banda)) return;

        var actual = LeerFrecuencia();
        if (!actual.EsCero && banda.Contiene(actual)) return;

        _silencio = true;
        Frecuencia = TextoDeFrecuencia.EscribirMegahercios(banda.Limite.Inferior);
        _silencio = false;
    }

    partial void OnFrecuenciaChanged(string value)
    {
        if (_silencio) return;

        var f = LeerFrecuencia();
        if (f.EsCero) return;

        var banda = DominioBanda.DesdeFrecuencia(f);
        if (banda.EsVacia || string.Equals(banda.Nombre, Banda, StringComparison.Ordinal)) return;

        _silencio = true;
        Banda = banda.Nombre;
        _silencio = false;
    }

    private void PonerInformesPorOmision(bool forzar)
    {
        if (!DominioModo.TryParse(Modo, null, out var modo)) return;

        var porOmision = Informe.PorOmisionPara(modo).Texto;

        _silencio = true;
        if (forzar || string.IsNullOrWhiteSpace(InformeEnviado) || InformeEnviado == _ultimoInformePorOmision)
        {
            InformeEnviado = porOmision;
        }
        if (forzar || string.IsNullOrWhiteSpace(InformeRecibido) || InformeRecibido == _ultimoInformePorOmision)
        {
            InformeRecibido = porOmision;
        }
        _silencio = false;

        _ultimoInformePorOmision = porOmision;
    }

    private void LanzarConsultaDeTrabajadoAntes(string texto)
    {
        _consultaEnCurso?.Cancel();
        _consultaEnCurso?.Dispose();
        var cts = new CancellationTokenSource();
        _consultaEnCurso = cts;

        _ = ConsultarAsync(texto, cts.Token);
    }

    private async Task ConsultarAsync(string texto, CancellationToken ct)
    {
        try
        {
            // Un respiro antes de preguntar: durante un pileup se teclea mas rapido que esto.
            await Task.Delay(220, ct).ConfigureAwait(true);
            var resultado = await _consultarTrabajadoAntes.EjecutarAsync(texto, ct).ConfigureAwait(true);
            if (ct.IsCancellationRequested) return;
            AplicarTrabajadoAntes(resultado);
        }
        catch (OperationCanceledException)
        {
            // El operador ha seguido tecleando; esta consulta ya no interesa.
        }
    }

    private void AplicarTrabajadoAntes(ResultadoTrabajadoAntes resultado)
    {
        ContactosPrevios.Clear();

        if (!resultado.TrabajadoAntes)
        {
            HayTrabajadoAntes = false;
            AvisoTrabajadoAntes = string.Empty;
            return;
        }

        HayTrabajadoAntes = true;

        DominioBanda.TryParse(Banda, out var banda);
        DominioModo.TryParse(Modo, null, out var modo);

        var novedad = new List<string>();
        if (resultado.EsNuevoEnBanda(banda)) novedad.Add("nuevo en esta banda");
        if (resultado.EsNuevoEnModo(modo)) novedad.Add("nuevo en este modo");

        AvisoTrabajadoAntes = novedad.Count > 0
            ? $"{resultado.Resumen} · Sería {string.Join(" y ", novedad)}."
            : resultado.Resumen;

        // Cuatro caben en una linea a lo ancho de la ventana; mas obligarian a robarle
        // otra linea al cuaderno, y el cuaderno tiene prioridad.
        foreach (var qso in resultado.Contactos.Take(4)) ContactosPrevios.Add(new FilaDeQso(qso));

        RellenarDesdeElUltimoContacto(resultado.Contactos[0]);
    }

    /// <summary>Copia del ultimo contacto lo que el operador aun no ha tecleado.</summary>
    private void RellenarDesdeElUltimoContacto(Qso ultimo)
    {
        _silencio = true;
        if (string.IsNullOrWhiteSpace(Nombre) && !string.IsNullOrWhiteSpace(ultimo.Name)) Nombre = ultimo.Name;
        if (string.IsNullOrWhiteSpace(Qth) && !string.IsNullOrWhiteSpace(ultimo.Qth)) Qth = ultimo.Qth;
        if (string.IsNullOrWhiteSpace(Localizador) && !ultimo.Gridsquare.EsVacio) Localizador = ultimo.Gridsquare.Valor;
        _silencio = false;
    }

    private void LimpiarTrabajadoAntes()
    {
        HayTrabajadoAntes = false;
        AvisoTrabajadoAntes = string.Empty;
        ContactosPrevios.Clear();
    }

    private static string? Vacio(string texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    /// <summary>Modos que se usan todos los dias, delante del resto del catalogo de ADIF.</summary>
    private static IReadOnlyList<string> ModosHabituales()
    {
        string[] primeros =
        [
            "SSB", "USB", "LSB", "CW", "FM", "AM", "FT8", "FT4", "RTTY", "PSK31",
            "JS8", "MSK144", "Q65", "JT65", "SSTV", "DMR", "DSTAR", "C4FM", "M17",
        ];

        var resto = DominioModo.ModosPrincipales
            .Where(m => !primeros.Contains(m, StringComparer.OrdinalIgnoreCase))
            .OrderBy(m => m, StringComparer.OrdinalIgnoreCase);

        return [.. primeros, .. resto];
    }
}
