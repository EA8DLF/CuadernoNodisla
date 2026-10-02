using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Idiomas;
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
    private readonly CompletadorDeQso? _completador;

    /// <summary>
    /// El contacto que se esta modificando, tal y como vino del cuaderno. Los cambios del
    /// formulario se ponen ENCIMA de el: construir uno nuevo con solo los campos del formulario
    /// borraba al guardar el pais, las zonas, las confirmaciones y el estado de envio.
    /// </summary>
    private Qso? _qsoEnEdicion;

    /// <summary>Lo que se relleno solo con la ficha de QRZ, para quitarlo si cambia el indicativo.</summary>
    private (string Nombre, string Qth, string Localizador) _rellenadoConFicha = (string.Empty, string.Empty, string.Empty);

    private CancellationTokenSource? _consultaEnCurso;
    private bool _silencio;

    /// <summary>Ultima frecuencia de transmision que puso el dial.</summary>
    private DominioFrecuencia? _txDelDial;

    /// <summary>Frecuencia de recepcion que puso el dial con split; nula sin split.</summary>
    private DominioFrecuencia? _rxDelDial;
    private bool _insistirConElDuplicado;
    private string _ultimoInformePorOmision = string.Empty;

    /// <summary>Crea el formulario con los casos de uso que necesita.</summary>
    public VistaModeloEntradaQso(
        RegistrarQso registrar,
        EditarQso editar,
        ConsultarTrabajadoAntes consultarTrabajadoAntes,
        CompletadorDeQso? completador = null)
    {
        _registrar = registrar;
        _editar = editar;
        _consultarTrabajadoAntes = consultarTrabajadoAntes;
        _completador = completador;

        Bandas = DominioBanda.Todas.Select(b => b.Nombre).ToArray();
        Modos = ModosHabituales();

        Limpiar();

        Textos.AlCambiar(this, static vm => vm.AlCambiarDeIdioma());
    }

    /// <summary>Vuelve a escribir en el idioma nuevo lo fijo del formulario.</summary>
    private void AlCambiarDeIdioma()
    {
        OnPropertyChanged(nameof(OrigenDeLaSintonia));
        OnPropertyChanged(nameof(TituloDelPanel));
        OnPropertyChanged(nameof(TextoDelBotonGuardar));
        for (var i = 0; i < ContactosPrevios.Count; i++) ContactosPrevios[i] = new FilaDeQso(ContactosPrevios[i].Qso);
    }

    /// <summary>Se dispara cuando el cuaderno ha cambiado y la rejilla debe refrescarse.</summary>
    public event EventHandler? CuadernoCambiado;

    /// <summary>
    /// Como esperar el respiro antes de consultar. Sustituible en las pruebas, que no miran el
    /// reloj de pared.
    /// </summary>
    public Func<TimeSpan, CancellationToken, Task> Esperar { get; set; } = Task.Delay;

    /// <summary>
    /// La ultima consulta lanzada (trabajado antes y ficha). Solo para las pruebas: la interfaz
    /// no espera nunca por ella.
    /// </summary>
    public Task ConsultaEnCurso { get; private set; } = Task.CompletedTask;

    /// <summary>Lo que dice la ficha de QRZ del indicativo tecleado, en una linea.</summary>
    [ObservableProperty]
    private string _resumenDeFicha = string.Empty;

    /// <summary>Bandas que se ofrecen en la lista desplegable.</summary>
    public IReadOnlyList<string> Bandas { get; }

    /// <summary>Modos que se ofrecen en la lista desplegable, los habituales primero.</summary>
    public IReadOnlyList<string> Modos { get; }

    /// <summary>Contactos anteriores con el mismo indicativo, para verlos de un vistazo.</summary>
    public ObservableCollection<FilaDeQso> ContactosPrevios { get; } = [];

    /// <summary>Perfil de estacion con el que se registra. Lo fija la ventana principal.</summary>
    public long? EstacionId { get; set; }

    /// <summary>
    /// La frecuencia, la banda y el modo los esta poniendo el equipo, no el operador.
    /// </summary>
    /// <remarks>
    /// Se ensena en el formulario a proposito. El error mas caro del cuaderno es registrar
    /// veinte contactos en la frecuencia equivocada creyendo que el programa seguia al dial
    /// cuando el equipo llevaba un rato desconectado.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OrigenDeLaSintonia))]
    private bool _siguiendoAlEquipo;

    /// <summary>De donde salen la frecuencia y el modo, escrito para el operador.</summary>
    public string OrigenDeLaSintonia => SiguiendoAlEquipo
        ? Textos.T("Libro.Entrada.SiguiendoAlEquipo")
        : Textos.T("Libro.Entrada.LosPoneUsted");

    /// <summary>
    /// Prepara el formulario para trabajar a una estacion anunciada en el cluster.
    /// </summary>
    /// <param name="indicativo">Indicativo anunciado.</param>
    /// <param name="frecuencia">Frecuencia del anuncio.</param>
    /// <param name="modo">Modo del anuncio, si lo trae.</param>
    public void PonerDesdeElSpot(string indicativo, DominioFrecuencia frecuencia, string? modo)
    {
        _silencio = true;
        Indicativo = DominioIndicativo.Normalizar(indicativo);

        if (!frecuencia.EsCero)
        {
            Frecuencia = TextoDeFrecuencia.Escribir(frecuencia);
            var banda = DominioBanda.DesdeFrecuencia(frecuencia);
            Banda = banda.EsVacia ? string.Empty : banda.Nombre;
        }

        if (modo is { Length: > 0 } && DominioModo.TryParse(modo, null, out var m)) Modo = m.NombreUsual;
        _silencio = false;

        PonerInformesPorOmision(forzar: false);
        LanzarConsultaDeTrabajadoAntes(Indicativo);
    }

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
    public string TituloDelPanel => Textos.T(EnEdicion ? "Libro.Entrada.TituloModificar" : "Libro.Entrada.TituloNuevo");

    /// <summary>Texto del boton principal.</summary>
    public string TextoDelBotonGuardar => Textos.T(EnEdicion ? "Libro.Entrada.GuardarCambios" : "Libro.Entrada.Registrar");

    /// <summary>Pone la hora en los campos mientras el operador no la haya fijado a mano.</summary>
    public void ActualizarReloj(DateTimeOffset utc)
    {
        if (!HoraAutomatica || EnEdicion) return;
        _silencio = true;
        FechaUtc = utc.UtcDateTime.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
        HoraUtc = utc.UtcDateTime.ToString("HH:mm", CultureInfo.InvariantCulture);
        _silencio = false;
    }

    /// <summary>
    /// Sigue al dial del equipo: pone en el formulario la frecuencia, la banda y el modo que
    /// tiene puestos la radio.
    /// </summary>
    /// <remarks>
    /// <b>No toda frecuencia tiene banda.</b> El dia que se capturo el CAT del FT-710 de Jose,
    /// el dial estaba en 27.555 MHz, que no esta en la tabla de bandas de ADIF. Cuando pasa
    /// eso, la banda se deja vacia a proposito —no se conserva la anterior, que seria mentira—
    /// y el contacto se registra igual: el nucleo admite banda vacia mientras haya frecuencia.
    ///
    /// Mientras se esta modificando un contacto del cuaderno, el dial no toca nada: lo que hay
    /// en el formulario es lo que se grabo aquel dia, no lo que la radio tiene ahora.
    /// </remarks>
    /// <param name="frecuencia">Frecuencia del VFO activo.</param>
    /// <param name="modo">Modo que tiene puesto el equipo.</param>
    public void SeguirAlDial(DominioFrecuencia frecuencia, DominioModo modo) => SeguirAlDial(frecuencia, modo, null);

    /// <summary>
    /// Sigue al dial con split: <paramref name="frecuencia"/> es la de transmision (FREQ) y
    /// <paramref name="frecuenciaRx"/> la de recepcion (FREQ_RX), como las apunta Log4OM.
    /// </summary>
    /// <param name="frecuencia">Frecuencia de transmision.</param>
    /// <param name="modo">Modo que tiene puesto el equipo.</param>
    /// <param name="frecuenciaRx">Frecuencia de recepcion, o nulo sin split.</param>
    public void SeguirAlDial(DominioFrecuencia frecuencia, DominioModo modo, DominioFrecuencia? frecuenciaRx)
    {
        if (EnEdicion || frecuencia.EsCero) return;

        _txDelDial = frecuencia;
        _rxDelDial = frecuenciaRx is { EsCero: false } rx && rx != frecuencia ? rx : null;
        _silencio = true;
        Frecuencia = TextoDeFrecuencia.Escribir(frecuencia);

        var banda = DominioBanda.DesdeFrecuencia(frecuencia);
        Banda = banda.EsVacia ? string.Empty : banda.Nombre;

        var cambiaElModo = !modo.EsVacio && !string.Equals(Modo, modo.NombreUsual, StringComparison.Ordinal);
        if (cambiaElModo) Modo = modo.NombreUsual;
        _silencio = false;

        if (cambiaElModo) PonerInformesPorOmision(forzar: false);
    }

    /// <summary>
    /// Se han mandado las QSL de estos contactos por correo: si uno es el que se esta
    /// modificando, se le pone la misma marca para que «Guardar cambios» no la borre.
    /// </summary>
    /// <param name="ids">Los contactos a los que se les ha mandado la tarjeta.</param>
    public void AnotarQslEnviadas(IReadOnlyList<long> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (_qsoEnEdicion is { } qso && ids.Contains(qso.Id))
        {
            Impresion.Qsl.MarcaDeQslEnviada.Marcar(qso, DateTimeOffset.UtcNow);
        }
    }

    /// <summary>Id del contacto que se esta modificando, o nulo.</summary>
    public long? IdDelContactoEnEdicion => EnEdicion ? IdEnEdicion : null;

    /// <summary>Carga un contacto del cuaderno en el formulario para modificarlo.</summary>
    public void CargarParaEditar(Qso qso)
    {
        ArgumentNullException.ThrowIfNull(qso);

        _silencio = true;
        _qsoEnEdicion = qso;
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

        Mensaje = Textos.F("Libro.Entrada.Modificando", qso.Call.Valor);
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
            var lotwYaSubido = false;
            if (_qsoEnEdicion is { } original && original.Id == IdEnEdicion)
            {
                lotwYaSubido = original.Confirmaciones.Any(c => c.Medio == MedioDeConfirmacion.Lotw
                    && c.Enviado is EstadoDeConfirmacion.Confirmado or EstadoDeConfirmacion.Verificado);
                PonerEncima(qso, original);
                qso = original;
            }

            qso.Id = IdEnEdicion;
            var edicion = await _editar.EjecutarAsync(
                new PeticionDeEdicion { Qso = qso, EstacionId = EstacionId }).ConfigureAwait(true);

            if (edicion.NoEncontrado)
            {
                Mensaje = Textos.T("Libro.Entrada.YaNoEsta");
                Tono = TonoDeMensaje.Error;
                return;
            }
            if (!edicion.Correcto)
            {
                Mensaje = string.Join("  ", edicion.Errores);
                Tono = TonoDeMensaje.Error;
                return;
            }

            Mensaje = Textos.F("Libro.Entrada.CambiosGuardados", qso.Call.Valor)
                + (lotwYaSubido
                    ? " " + Textos.T("Libro.Entrada.LotwNoAdmite")
                    : string.Empty);
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
            var instante = duplicado.InicioUtc.UtcDateTime;
            var cuando = instante.ToString(Textos.T("Comun.FormatoDeFecha"), Textos.Cultura) + " "
                + instante.ToString("HH:mm", CultureInfo.InvariantCulture);
            Mensaje = Textos.F(
                "Libro.Entrada.Duplicado",
                duplicado.Call.Valor,
                duplicado.Band.Nombre,
                duplicado.Mode.NombreUsual,
                cuando);
            Tono = TonoDeMensaje.Aviso;
            return;
        }

        if (!registro.Correcto)
        {
            Mensaje = string.Join("  ", registro.Errores);
            Tono = TonoDeMensaje.Error;
            return;
        }

        Mensaje = Textos.F("Libro.Entrada.Registrado", qso.Call.Valor, qso.Band.Nombre, qso.Mode.NombreUsual);
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
        _qsoEnEdicion = null;
        _rellenadoConFicha = (string.Empty, string.Empty, string.Empty);
        ResumenDeFicha = string.Empty;
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

    /// <summary>Pone lo del formulario encima del contacto que vino del cuaderno.</summary>
    private static void PonerEncima(Qso delFormulario, Qso original)
    {
        original.Call = delFormulario.Call;
        original.Band = delFormulario.Band;
        original.Mode = delFormulario.Mode;
        original.Freq = delFormulario.Freq;
        original.InicioUtc = delFormulario.InicioUtc;
        original.RstSent = delFormulario.RstSent;
        original.RstRcvd = delFormulario.RstRcvd;
        original.Name = delFormulario.Name;
        original.Qth = delFormulario.Qth;
        original.Gridsquare = delFormulario.Gridsquare;
        original.Comentario = delFormulario.Comentario;
    }

    private Qso? ConstruirQso()
    {
        if (!DominioIndicativo.TryParse(Indicativo, out var call))
        {
            Mensaje = string.IsNullOrWhiteSpace(Indicativo)
                ? Textos.T("Servicios.Aplicacion.FaltaIndicativo")
                : Textos.F("Servicios.Aplicacion.IndicativoNoValido", Indicativo);
            Tono = TonoDeMensaje.Error;
            return null;
        }

        if (!DominioModo.TryParse(Modo, null, out var modo))
        {
            Mensaje = Textos.F("Libro.Entrada.ModoNoAdif", Modo);
            Tono = TonoDeMensaje.Error;
            return null;
        }

        DominioBanda.TryParse(Banda, out var banda);
        var frecuencia = LeerFrecuencia();

        if (!LeerInstante(out var inicio))
        {
            Mensaje = Textos.T("Libro.Entrada.FechaNoValida");
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

            // Con split, la de recepcion; solo si la frecuencia sigue siendo la que puso el dial
            // (si el operador la ha tecleado a mano, la de recepcion ya no casa con ella).
            FreqRx = _rxDelDial is { } rx && _txDelDial == frecuencia ? rx : null,
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

        ConsultaEnCurso = ConsultarAsync(texto, cts.Token);
    }

    private async Task ConsultarAsync(string texto, CancellationToken ct)
    {
        try
        {
            // Un respiro antes de preguntar: durante un pileup se teclea mas rapido que esto.
            await Esperar(TimeSpan.FromMilliseconds(220), ct).ConfigureAwait(true);
            QuitarLoRellenadoConFicha();
            var resultado = await _consultarTrabajadoAntes.EjecutarAsync(texto, ct).ConfigureAwait(true);
            if (ct.IsCancellationRequested) return;
            AplicarTrabajadoAntes(resultado);
            await RellenarConFichaAsync(texto, ct).ConfigureAwait(true);
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
        if (resultado.EsNuevoEnBanda(banda)) novedad.Add(Textos.T("Libro.Entrada.NuevoEnBanda"));
        if (resultado.EsNuevoEnModo(modo)) novedad.Add(Textos.T("Libro.Entrada.NuevoEnModo"));

        AvisoTrabajadoAntes = novedad.Count > 0
            ? Textos.F("Libro.Entrada.Seria", resultado.Resumen, string.Join(" " + Textos.T("Libro.Entrada.Y") + " ", novedad))
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

    /// <summary>
    /// Pide la ficha a QRZ.com y rellena Nombre, QTH y Localizador <b>si estan vacios</b>. Lo
    /// que haya escrito el operador no se toca. Sin red o sin credenciales no dice nada aqui:
    /// el aviso discreto va en la pastilla «Ficha» de la barra de estado.
    /// </summary>
    private async Task RellenarConFichaAsync(string texto, CancellationToken ct)
    {
        if (_completador is not { EstaDisponible: true }) return;
        if (!DominioIndicativo.TryParse(texto, out var indicativo)) return;

        var ficha = await _completador.ConsultarAsync(indicativo, ct).ConfigureAwait(true);
        if (ct.IsCancellationRequested || ficha is null) return;
        if (!string.Equals(DominioIndicativo.Normalizar(Indicativo), indicativo.Valor, StringComparison.OrdinalIgnoreCase)) return;

        _silencio = true;
        if (string.IsNullOrWhiteSpace(Nombre) && !string.IsNullOrWhiteSpace(ficha.Nombre))
        {
            Nombre = ficha.Nombre.Trim();
            _rellenadoConFicha.Nombre = Nombre;
        }
        if (string.IsNullOrWhiteSpace(Qth) && !string.IsNullOrWhiteSpace(ficha.Localidad))
        {
            Qth = ficha.Localidad.Trim();
            _rellenadoConFicha.Qth = Qth;
        }
        if (string.IsNullOrWhiteSpace(Localizador) && !ficha.Localizador.EsVacio)
        {
            Localizador = ficha.Localizador.Valor;
            _rellenadoConFicha.Localizador = Localizador;
        }
        _silencio = false;

        var partes = new List<string> { ficha.Fuente };
        if (ficha.Pais is { Length: > 0 } pais) partes.Add(pais);
        if (ficha.GestorQsl is { Length: > 0 } via) partes.Add(Textos.F("Libro.Entrada.QslVia", via));
        if (ficha.UsaLotw == true) partes.Add(Textos.F("Libro.Entrada.Usa", "LoTW"));
        if (ficha.UsaEqsl == true) partes.Add(Textos.F("Libro.Entrada.Usa", "eQSL"));
        ResumenDeFicha = string.Join(" · ", partes);
    }

    /// <summary>
    /// Si cambia el indicativo, se quita lo que puso la ficha del anterior y el operador no ha
    /// tocado. Lo tecleado a mano se queda.
    /// </summary>
    private void QuitarLoRellenadoConFicha()
    {
        _silencio = true;
        if (_rellenadoConFicha.Nombre.Length > 0 && Nombre == _rellenadoConFicha.Nombre) Nombre = string.Empty;
        if (_rellenadoConFicha.Qth.Length > 0 && Qth == _rellenadoConFicha.Qth) Qth = string.Empty;
        if (_rellenadoConFicha.Localizador.Length > 0 && Localizador == _rellenadoConFicha.Localizador) Localizador = string.Empty;
        _silencio = false;
        _rellenadoConFicha = (string.Empty, string.Empty, string.Empty);
        ResumenDeFicha = string.Empty;
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
