using System.Globalization;
using System.Text;
using System.Windows.Media.Imaging;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Impresion;
using Nodisla.Cuaderno.Impresion.Qsl;
using Nodisla.Cuaderno.Servicios.Correo;
using Nodisla.Cuaderno.Ui.Ajustes;
using Serilog;

namespace Nodisla.Cuaderno.Ui.Qsl;

/// <summary>De donde ha salido la direccion de correo del corresponsal.</summary>
public enum OrigenDelCorreo
{
    /// <summary>No hay: hay que escribirla o se salta.</summary>
    Ninguno = 0,

    /// <summary>Estaba apuntada en el contacto.</summary>
    DelContacto,

    /// <summary>La publica su ficha de QRZ.com (o HamQTH).</summary>
    DeLaFicha,

    /// <summary>La ha escrito el operador.</summary>
    Escrita,
}

/// <summary>
/// Todo lo de las QSL electronicas en un sitio: plantillas, dibujo, busqueda de la direccion,
/// envio y anotacion en el cuaderno. Las pantallas (editor, ventana de envio, ajustes) solo lo usan.
/// </summary>
public sealed class ServicioDeQsl
{
    private readonly string _carpetaDeDatos;
    private readonly IAlmacenDeCredenciales _credenciales;
    private readonly IRepositorioQso? _qsos;
    private readonly IRepositorioEstacion? _estaciones;
    private readonly CompletadorDeQso? _completador;

    /// <summary>Crea el servicio.</summary>
    /// <param name="carpetaDeDatos">Carpeta de datos del programa.</param>
    /// <param name="enviador">Quien manda los correos (el SMTP de verdad, o el buzon en disco con CUADERNO_SIMULADO).</param>
    /// <param name="credenciales">Almacen cifrado, de donde sale la contraseña justo al mandar.</param>
    /// <param name="qsos">Cuaderno, para apuntar el envio.</param>
    /// <param name="estaciones">Perfiles, para los datos de mi estacion.</param>
    /// <param name="completador">Ficha de QRZ, para buscar el correo del corresponsal.</param>
    public ServicioDeQsl(
        string carpetaDeDatos,
        IEnviadorDeCorreo enviador,
        IAlmacenDeCredenciales credenciales,
        IRepositorioQso? qsos = null,
        IRepositorioEstacion? estaciones = null,
        CompletadorDeQso? completador = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(carpetaDeDatos);
        _carpetaDeDatos = carpetaDeDatos;
        Enviador = enviador ?? throw new ArgumentNullException(nameof(enviador));
        _credenciales = credenciales ?? throw new ArgumentNullException(nameof(credenciales));
        _qsos = qsos;
        _estaciones = estaciones;
        _completador = completador;
        Disenos = new AlmacenDeDisenosDeQsl(carpetaDeDatos);
        Ajustes = AjustesDeCorreoQsl.Leer(carpetaDeDatos);
    }

    /// <summary>Se han mandado tarjetas y se han apuntado en estos contactos.</summary>
    public event EventHandler<IReadOnlyList<long>>? QslEnviadas;

    /// <summary>Las plantillas guardadas.</summary>
    public AlmacenDeDisenosDeQsl Disenos { get; }

    /// <summary>Servidor y texto del correo.</summary>
    public AjustesDeCorreoQsl Ajustes { get; }

    /// <summary>Quien manda los correos.</summary>
    public IEnviadorDeCorreo Enviador { get; }

    /// <summary>Identificador del perfil de estacion activo.</summary>
    public Func<long?> EstacionActiva { get; set; } = () => null;

    /// <summary>Hay contraseña de correo guardada.</summary>
    public bool HayContrasena => _credenciales.Existe(ClavesDeCredencial.SmtpContrasena);

    /// <summary>Hay cuaderno donde apuntar y buscar.</summary>
    public bool HayCuaderno => _qsos is not null;

    /// <summary>Guarda los ajustes de correo.</summary>
    public void GuardarAjustes() => Ajustes.Guardar(_carpetaDeDatos);

    /// <summary>Guarda la contraseña del correo cifrada.</summary>
    /// <param name="contrasena">La contraseña.</param>
    public void GuardarContrasena(string contrasena)
    {
        ArgumentException.ThrowIfNullOrEmpty(contrasena);
        _credenciales.Guardar(ClavesDeCredencial.SmtpContrasena, contrasena);
    }

    /// <summary>Borra la contraseña del correo.</summary>
    public void BorrarContrasena() => _credenciales.Borrar(ClavesDeCredencial.SmtpContrasena);

    /// <summary>Conecta con el servidor y se identifica, sin mandar nada.</summary>
    /// <param name="ct">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    public Task ProbarAsync(CancellationToken ct = default) =>
        Enviador.ProbarAsync(Ajustes.Smtp, _credenciales.Leer(ClavesDeCredencial.SmtpContrasena), ct);

    /// <summary>Los datos de mi estacion segun el perfil activo.</summary>
    /// <param name="ct">Cancelacion.</param>
    /// <returns>Los datos; vacios si no hay perfil.</returns>
    public async Task<DatosDeMiEstacion> MiEstacionAsync(CancellationToken ct = default)
    {
        if (_estaciones is null) return DatosDeMiEstacion.Vacios;
        try
        {
            var id = EstacionActiva();
            var estacion = id is { } i
                ? await _estaciones.ObtenerAsync(i, ct).ConfigureAwait(true)
                : await _estaciones.PredeterminadaAsync(ct).ConfigureAwait(true);
            return DatosDeMiEstacion.Desde(estacion);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warning(ex, "No se ha podido leer el perfil de estación para la QSL.");
            return DatosDeMiEstacion.Vacios;
        }
    }

    /// <summary>Los ultimos contactos del cuaderno, para la vista previa.</summary>
    /// <param name="cuantos">Cuantos.</param>
    /// <param name="ct">Cancelacion.</param>
    /// <returns>Los contactos, del mas reciente al mas antiguo.</returns>
    public async Task<IReadOnlyList<Qso>> UltimosContactosAsync(int cuantos, CancellationToken ct = default)
    {
        if (_qsos is null) return [];
        var pagina = await _qsos.BuscarAsync(new CriterioQso { OrdenarPor = CampoDeOrden.Fecha, Descendente = true }, 0, cuantos, ct).ConfigureAwait(true);
        return pagina.Elementos;
    }

    /// <summary>Trae contactos del cuaderno por su identificador, con sus confirmaciones.</summary>
    /// <param name="ids">Los identificadores.</param>
    /// <param name="ct">Cancelacion.</param>
    /// <returns>Los que existen, en el mismo orden.</returns>
    public async Task<IReadOnlyList<Qso>> TraerAsync(IEnumerable<long> ids, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (_qsos is null) return [];
        var lista = new List<Qso>();
        foreach (var id in ids.Distinct())
        {
            if (await _qsos.ObtenerAsync(id, ct).ConfigureAwait(true) is { } qso) lista.Add(qso);
        }

        return lista;
    }

    /// <summary>Dibuja la tarjeta de un contacto.</summary>
    /// <param name="diseno">La plantilla.</param>
    /// <param name="qso">El contacto; nulo deja en blanco sus campos.</param>
    /// <param name="yo">Mi estacion.</param>
    /// <param name="ppp">Resolucion.</param>
    /// <param name="soloMisDatos">Sin los campos del contacto (para eQSL).</param>
    /// <returns>La tarjeta.</returns>
    public BitmapSource Dibujar(DisenoDeQsl diseno, Qso? qso, DatosDeMiEstacion yo, double ppp, bool soloMisDatos = false) =>
        DibujanteDeQsl.Dibujar(diseno, VariablesDeQsl.Para(qso, yo), Disenos.RutaDelFondo(diseno), ppp, soloMisDatos);

    /// <summary>La tarjeta de un contacto como fichero adjunto.</summary>
    /// <param name="diseno">La plantilla.</param>
    /// <param name="qso">El contacto.</param>
    /// <param name="yo">Mi estacion.</param>
    /// <param name="formato">PNG o JPG.</param>
    /// <returns>El adjunto.</returns>
    public AdjuntoDeCorreo Adjunto(DisenoDeQsl diseno, Qso qso, DatosDeMiEstacion yo, FormatoDeImagen formato)
    {
        ArgumentNullException.ThrowIfNull(qso);
        var (tipo, extension) = DibujanteDeQsl.Tipo(formato);
        var bytes = DibujanteDeQsl.Codificar(Dibujar(diseno, qso, yo, DibujanteDeQsl.PppDeCorreo), formato);
        return new AdjuntoDeCorreo(NombreDeFichero(qso, yo) + extension, tipo, bytes);
    }

    /// <summary>Las tarjetas de varios contactos en un PDF para imprimir.</summary>
    /// <param name="diseno">La plantilla.</param>
    /// <param name="qsos">Los contactos.</param>
    /// <param name="yo">Mi estacion.</param>
    /// <param name="unaPorPagina">Cada tarjeta en su pagina (imprenta) o repartidas en A4 (casa).</param>
    /// <returns>El PDF.</returns>
    public Impreso Pdf(DisenoDeQsl diseno, IReadOnlyList<Qso> qsos, DatosDeMiEstacion yo, bool unaPorPagina)
    {
        ArgumentNullException.ThrowIfNull(diseno);
        ArgumentNullException.ThrowIfNull(qsos);
        var imagenes = qsos
            .Select(q => DibujanteDeQsl.Codificar(Dibujar(diseno, q, yo, DibujanteDeQsl.PppDeImprenta), FormatoDeImagen.Jpg))
            .ToList();
        var nombre = qsos.Count == 1 ? NombreDeFichero(qsos[0], yo) + ".pdf" : "tarjetas-qsl.pdf";
        return unaPorPagina
            ? PdfDeTarjetasQsl.UnaPorPagina(imagenes, diseno.AnchoMm, diseno.AltoMm, nombre)
            : PdfDeTarjetasQsl.EnHojaA4(imagenes, diseno.AnchoMm, diseno.AltoMm, nombre);
    }

    /// <summary>
    /// Busca la direccion del corresponsal: la del contacto si la tiene y, si no, la de su ficha de QRZ.
    /// </summary>
    /// <param name="qso">Un contacto con el.</param>
    /// <param name="ct">Cancelacion.</param>
    /// <returns>La direccion y de donde sale.</returns>
    public async Task<(string? Correo, OrigenDelCorreo Origen)> BuscarCorreoAsync(Qso qso, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(qso);
        if (DireccionDeCorreo.EsValida(qso.Email)) return (qso.Email!.Trim(), OrigenDelCorreo.DelContacto);
        if (_completador is not { EstaDisponible: true }) return (null, OrigenDelCorreo.Ninguno);

        try
        {
            var ficha = await _completador.ConsultarAsync(qso.Call, ct).ConfigureAwait(true);
            return DireccionDeCorreo.EsValida(ficha?.CorreoElectronico)
                ? (ficha!.CorreoElectronico!.Trim(), OrigenDelCorreo.DeLaFicha)
                : (null, OrigenDelCorreo.Ninguno);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warning(ex, "No se ha podido consultar la ficha de {Indicativo} para buscar su correo.", qso.Call.Valor);
            return (null, OrigenDelCorreo.Ninguno);
        }
    }

    /// <summary>
    /// Manda un correo con las tarjetas de varios contactos con la misma estacion y los apunta
    /// como enviados.
    /// </summary>
    /// <param name="para">Direccion del corresponsal.</param>
    /// <param name="contactos">Los contactos con el.</param>
    /// <param name="diseno">La plantilla.</param>
    /// <param name="yo">Mi estacion.</param>
    /// <param name="asunto">Asunto con variables (se rellenan con el primer contacto).</param>
    /// <param name="texto">Texto con variables.</param>
    /// <param name="ct">Cancelacion.</param>
    /// <returns>Tarea que acaba con el correo aceptado por el servidor y los contactos apuntados.</returns>
    /// <exception cref="ErrorDeCorreo">No se ha podido mandar; no se apunta nada.</exception>
    public async Task EnviarAsync(
        string para,
        IReadOnlyList<Qso> contactos,
        DisenoDeQsl diseno,
        DatosDeMiEstacion yo,
        string asunto,
        string texto,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(contactos);
        ArgumentNullException.ThrowIfNull(diseno);
        if (contactos.Count == 0) throw new ArgumentException("No hay contactos que mandar.", nameof(contactos));
        ComprobarEnvio(para);

        var variables = new Dictionary<string, string>(VariablesDeQsl.Para(contactos[0], yo), StringComparer.OrdinalIgnoreCase)
        {
            ["contactos"] = ListaDeContactos(contactos, yo),
        };

        var adjuntos = contactos.Select(q => Adjunto(diseno, q, yo, Ajustes.Formato)).ToList();
        var mensaje = new MensajeDeCorreo(
            para.Trim(),
            VariablesDeQsl.Sustituir(asunto, variables),
            VariablesDeQsl.Sustituir(texto, variables),
            adjuntos);

        await EnviarCorreoAsync(mensaje, ct).ConfigureAwait(true);

        var ahora = DateTimeOffset.UtcNow;
        var apuntados = new List<long>();
        foreach (var qso in contactos)
        {
            MarcaDeQslEnviada.Marcar(qso, ahora);
            if (_qsos is null || qso.Id <= 0) continue;
            try
            {
                // Se relee de la base: el objeto de la pantalla puede ser una copia vieja.
                var guardado = await _qsos.ObtenerAsync(qso.Id, CancellationToken.None).ConfigureAwait(true);
                if (guardado is null) continue;
                MarcaDeQslEnviada.Marcar(guardado, ahora);
                guardado.ModificadoUtc = ahora;
                await _qsos.ActualizarAsync(guardado, CancellationToken.None).ConfigureAwait(true);
                apuntados.Add(qso.Id);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "La QSL de {Indicativo} ha salido pero no se ha podido apuntar en el contacto {Id}.", qso.Call.Valor, qso.Id);
            }
        }

        Log.Information("QSL electrónica enviada a {Indicativo} ({Contactos} contacto(s)).", contactos[0].Call.Valor, contactos.Count);
        if (apuntados.Count > 0) QslEnviadas?.Invoke(this, apuntados);
    }

    /// <summary>
    /// Manda un correo ya compuesto con el servidor configurado para las QSL. Lo usan las
    /// tarjetas y los diplomas: un solo sitio que lee la contraseña y habla con el servidor.
    /// </summary>
    /// <param name="mensaje">El correo.</param>
    /// <param name="ct">Cancelacion.</param>
    /// <returns>Tarea que acaba con el correo aceptado por el servidor.</returns>
    /// <exception cref="ErrorDeCorreo">Direccion mala, correo sin configurar o el servidor lo rechaza.</exception>
    public async Task EnviarCorreoAsync(MensajeDeCorreo mensaje, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(mensaje);
        ComprobarEnvio(mensaje.Para);

        // La contraseña se lee aqui, justo al mandar, y no se guarda en ningun sitio mas.
        await Enviador.EnviarAsync(mensaje, Ajustes.Smtp, _credenciales.Leer(ClavesDeCredencial.SmtpContrasena), ct).ConfigureAwait(true);
    }

    /// <summary>Nombre de fichero de la tarjeta: <c>QSL_EA8DLF_EA1ABC_20260929_1432</c>.</summary>
    /// <param name="qso">El contacto.</param>
    /// <param name="yo">Mi estacion.</param>
    /// <returns>El nombre, sin extension.</returns>
    public static string NombreDeFichero(Qso qso, DatosDeMiEstacion yo)
    {
        ArgumentNullException.ThrowIfNull(qso);
        var v = VariablesDeQsl.Para(qso, yo);
        var utc = qso.InicioUtc.ToUniversalTime();
        static string Limpio(string s) => new(s.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray());
        var mio = Limpio(v["miindicativo"]);
        return string.Create(CultureInfo.InvariantCulture, $"QSL_{(mio.Length == 0 ? "NODISLA" : mio)}_{Limpio(v["indicativo"])}_{utc:yyyyMMdd_HHmm}");
    }

    private void ComprobarEnvio(string para)
    {
        if (!DireccionDeCorreo.EsValida(para)) throw new ErrorDeCorreo(Textos.F("Qsl.Envio.DireccionNoValida", para)) { EsDelDestinatario = true };
        if (!Ajustes.Smtp.EstaCompleta && Enviador is ClienteSmtp)
        {
            throw new ErrorDeCorreo(Textos.T("Qsl.Envio.FaltaConfigurar"));
        }
    }

    private static string ListaDeContactos(IReadOnlyList<Qso> contactos, DatosDeMiEstacion yo)
    {
        var s = new StringBuilder();
        foreach (var q in contactos)
        {
            var v = VariablesDeQsl.Para(q, yo);
            s.Append(v["fecha"]).Append(' ').Append(v["hora"]).Append(" UTC  ")
                .Append(v["banda"]).Append(' ').Append(v["modo"]);
            if (v["rst"].Length > 0) s.Append("  RST ").Append(v["rst"]);
            s.Append('\n');
        }

        return s.ToString().TrimEnd('\n');
    }
}
