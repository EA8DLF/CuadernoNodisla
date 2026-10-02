using System.Globalization;
using System.Text.Json;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Impresion;
using Nodisla.Cuaderno.Impresion.Diplomas;
using Nodisla.Cuaderno.Impresion.Qsl;
using Nodisla.Cuaderno.Servicios.Correo;
using Serilog;

namespace Nodisla.Cuaderno.Ui.Qsl;

/// <summary>Un diploma del modulo de diplomas que ya tiene confirmaciones, para imprimir su certificado.</summary>
/// <param name="Diploma">El diploma del catalogo.</param>
/// <param name="Progreso">Su progreso.</param>
public sealed record DiplomaConseguido(Diploma Diploma, ProgresoDeDiploma Progreso)
{
    /// <summary>Llega al objetivo (o no tiene objetivo y hay alguna confirmada).</summary>
    public bool Completo => Progreso.Objetivo is not > 0 || Progreso.Confirmadas >= Progreso.Objetivo;

    /// <summary>Como se ve en la lista.</summary>
    public string Texto => Textos.F(
        "Qsl.Disenador.ConseguidoTexto",
        (Completo ? "✓ " : string.Empty) + Diploma.Codigo,
        Progreso.Variante,
        string.Create(CultureInfo.InvariantCulture, $"{Progreso.Confirmadas}{(Progreso.Objetivo is > 0 ? "/" + Progreso.Objetivo : string.Empty)}"));

    /// <inheritdoc />
    public override string ToString() => Texto;
}

/// <summary>
/// Todo lo de los diplomas impresos: plantillas, rellenado desde el cuaderno o desde el modulo de
/// diplomas, numeracion, PDF, PNG, correo e historial. El correo sale por el mismo servicio que
/// las QSL (<see cref="ServicioDeQsl.EnviarCorreoAsync"/>).
/// </summary>
public sealed class ServicioDeDiplomas
{
    private const int MaximoDeReferencias = 5000;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly IRepositorioDiplomasEmitidos _emitidos;
    private readonly IRepositorioQso? _qsos;
    private readonly IDiplomas? _diplomas;

    /// <summary>Crea el servicio.</summary>
    /// <param name="carpetaDeDatos">Carpeta de datos del programa.</param>
    /// <param name="qsl">El servicio de las QSL: mi estacion y el correo.</param>
    /// <param name="emitidos">Historial y numeracion.</param>
    /// <param name="qsos">Cuaderno, para rellenar desde los contactos.</param>
    /// <param name="diplomas">Modulo de diplomas, para los certificados de los conseguidos.</param>
    public ServicioDeDiplomas(
        string carpetaDeDatos,
        ServicioDeQsl qsl,
        IRepositorioDiplomasEmitidos emitidos,
        IRepositorioQso? qsos = null,
        IDiplomas? diplomas = null)
    {
        Qsl = qsl ?? throw new ArgumentNullException(nameof(qsl));
        _emitidos = emitidos ?? throw new ArgumentNullException(nameof(emitidos));
        _qsos = qsos;
        _diplomas = diplomas;
        Disenos = new AlmacenDeDisenosDeDiploma(carpetaDeDatos);
    }

    /// <summary>Se ha emitido o enviado un diploma.</summary>
    public event EventHandler<DiplomaEmitido>? HistorialCambiado;

    /// <summary>Las plantillas.</summary>
    public AlmacenDeDisenosDeDiploma Disenos { get; }

    /// <summary>Las QSL: mi estacion y el correo.</summary>
    public ServicioDeQsl Qsl { get; }

    /// <summary>Hay cuaderno donde buscar contactos.</summary>
    public bool HayCuaderno => _qsos is not null;

    /// <summary>Hay modulo de diplomas.</summary>
    public bool HayDiplomas => _diplomas is not null;

    /// <summary>Nombre de fichero: <c>Diploma_NODISLA_0007_EA1ABC</c>.</summary>
    /// <param name="diseno">La plantilla.</param>
    /// <param name="datos">Los datos.</param>
    /// <returns>El nombre, sin extension.</returns>
    public static string NombreDeFichero(DisenoDeDiploma diseno, DatosDeDiploma datos)
    {
        ArgumentNullException.ThrowIfNull(diseno);
        ArgumentNullException.ThrowIfNull(datos);
        static string Limpio(string s) => new(s.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray());
        var numero = datos.Numero is null ? "borrador" : Limpio(diseno.FormatearNumero(datos.Numero));
        var indicativo = Limpio(datos.Indicativo.Trim().ToUpperInvariant());
        return $"Diploma_{Limpio(diseno.Serie)}_{numero}_{(indicativo.Length == 0 ? "SIN" : indicativo)}";
    }

    /// <summary>Los datos de un diploma emitido a otra estacion, a partir de sus contactos.</summary>
    /// <param name="indicativo">La estacion.</param>
    /// <param name="contactos">Los contactos elegidos que lo justifican.</param>
    /// <param name="diseno">La plantilla (nombre y categoria propuestos).</param>
    /// <returns>Los datos, sin numero.</returns>
    public static DatosDeDiploma DesdeContactos(string indicativo, IReadOnlyList<Qso> contactos, DisenoDeDiploma diseno)
    {
        ArgumentNullException.ThrowIfNull(contactos);
        ArgumentNullException.ThrowIfNull(diseno);
        var filas = contactos.OrderBy(q => q.InicioUtc).Select(Fila).ToList();
        var conReferencia = filas.Count(f => f.Referencia.Length > 0);
        return new DatosDeDiploma
        {
            Indicativo = (indicativo ?? string.Empty).Trim().ToUpperInvariant(),
            Nombre = contactos.Select(q => q.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))?.Trim(),
            Correo = contactos.Select(q => q.Email).FirstOrDefault(DireccionDeCorreo.EsValida)?.Trim(),
            NombreDelDiploma = diseno.DiplomaPorOmision,
            Categoria = diseno.CategoriaPorOmision,
            Fecha = DateTimeOffset.Now,

            // Un diploma de club suele justificarse por contactos, sin referencias: se cuentan ellos.
            Referencias = conReferencia > 0 ? null : filas.Count,
            Qsos = filas.Count,
            Filas = filas,
        };
    }

    /// <summary>Los contactos del cuaderno con una estacion, del primero al ultimo.</summary>
    /// <param name="indicativo">La estacion.</param>
    /// <param name="ct">Cancelacion.</param>
    /// <returns>Los contactos.</returns>
    public async Task<IReadOnlyList<Qso>> ContactosConAsync(string indicativo, CancellationToken ct = default)
    {
        if (_qsos is null || string.IsNullOrWhiteSpace(indicativo)) return [];
        var pagina = await _qsos.BuscarAsync(
            new CriterioQso { Call = indicativo.Trim().ToUpperInvariant(), OrdenarPor = CampoDeOrden.Fecha, Descendente = false },
            0,
            1000,
            ct).ConfigureAwait(true);
        return pagina.Elementos;
    }

    /// <summary>Los diplomas del modulo de diplomas que ya tienen alguna referencia confirmada.</summary>
    /// <param name="ct">Cancelacion.</param>
    /// <returns>Los diplomas, los completos primero.</returns>
    public async Task<IReadOnlyList<DiplomaConseguido>> ConseguidosAsync(CancellationToken ct = default)
    {
        if (_diplomas is null) return [];
        var catalogo = (await _diplomas.CatalogoAsync(ct).ConfigureAwait(true)).ToDictionary(d => d.Codigo, StringComparer.OrdinalIgnoreCase);
        var progresos = await _diplomas.ProgresoDeMisDiplomasAsync(ct).ConfigureAwait(true);
        return progresos
            .Where(p => p.Confirmadas > 0 && catalogo.ContainsKey(p.Codigo))
            .Select(p => new DiplomaConseguido(catalogo[p.Codigo], p))
            .OrderByDescending(d => d.Completo)
            .ThenBy(d => d.Diploma.Codigo, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Progreso.Variante, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Los datos del certificado de un diploma conseguido: mi indicativo, el diploma, la variante
    /// como categoria y las referencias confirmadas con su primer contacto.
    /// </summary>
    /// <param name="conseguido">El diploma.</param>
    /// <param name="yo">Mi estacion.</param>
    /// <param name="ct">Cancelacion.</param>
    /// <returns>Los datos, sin numero.</returns>
    public async Task<DatosDeDiploma> DesdeDiplomaAsync(DiplomaConseguido conseguido, DatosDeMiEstacion yo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(conseguido);
        ArgumentNullException.ThrowIfNull(yo);
        var filas = new List<FilaDeJustificante>();
        if (_diplomas is not null)
        {
            const int Pagina = 500;
            for (var desde = 0; desde < MaximoDeReferencias; desde += Pagina)
            {
                var pagina = await _diplomas.DetalleAsync(conseguido.Diploma.Codigo, conseguido.Progreso.Variante, desde, Pagina, ct).ConfigureAwait(true);
                foreach (var r in pagina.Elementos.Where(r => r.Confirmada))
                {
                    var fila = new FilaDeJustificante { Referencia = r.Referencia, NombreDeReferencia = r.Nombre };
                    if (r.PrimerQsoId is { } id && _qsos is not null && await _qsos.ObtenerAsync(id, ct).ConfigureAwait(true) is { } qso)
                    {
                        var deQso = Fila(qso);
                        fila.Indicativo = deQso.Indicativo;
                        fila.FechaUtc = deQso.FechaUtc;
                        fila.Banda = deQso.Banda;
                        fila.Modo = deQso.Modo;
                        fila.Rst = deQso.Rst;
                    }

                    filas.Add(fila);
                }

                if (pagina.Elementos.Count < Pagina || desde + Pagina >= pagina.TotalFiltrado) break;
            }
        }

        return new DatosDeDiploma
        {
            Indicativo = yo.Indicativo,
            Nombre = yo.Nombre,
            NombreDelDiploma = conseguido.Diploma.Nombre,
            Categoria = conseguido.Progreso.Variante,
            Entidad = conseguido.Diploma.Gestor,
            Fecha = DateTimeOffset.Now,
            Referencias = conseguido.Progreso.Confirmadas,
            Qsos = filas.Count(f => f.FechaUtc is not null),
            Filas = filas,
        };
    }

    /// <summary>El numero que le tocaria al siguiente diploma de la serie.</summary>
    /// <param name="serie">La serie.</param>
    /// <param name="ct">Cancelacion.</param>
    /// <returns>El numero.</returns>
    public Task<int> SiguienteNumeroAsync(string serie, CancellationToken ct = default) =>
        _emitidos.SiguienteNumeroAsync(serie, ct);

    /// <summary>El historial de emitidos.</summary>
    /// <param name="ct">Cancelacion.</param>
    /// <returns>Del mas reciente al mas antiguo.</returns>
    public Task<IReadOnlyList<DiplomaEmitido>> HistorialAsync(CancellationToken ct = default) =>
        _emitidos.ListarAsync(500, ct);

    /// <summary>Emite el diploma: le da el siguiente numero de su serie y lo apunta en el historial.</summary>
    /// <param name="diseno">La plantilla.</param>
    /// <param name="datos">Los datos; se les pone el numero.</param>
    /// <param name="origen">Emitido a otra estacion o certificado propio.</param>
    /// <param name="ct">Cancelacion.</param>
    /// <returns>El apunte del historial.</returns>
    public async Task<DiplomaEmitido> EmitirAsync(DisenoDeDiploma diseno, DatosDeDiploma datos, OrigenDeDiplomaEmitido origen, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(diseno);
        ArgumentNullException.ThrowIfNull(datos);
        if (string.IsNullOrWhiteSpace(datos.Indicativo)) throw new ArgumentException(Textos.T("Qsl.Disenador.FaltaIndicativo"), nameof(datos));
        if (string.IsNullOrWhiteSpace(datos.NombreDelDiploma)) throw new ArgumentException(Textos.T("Qsl.Disenador.FaltaNombre"), nameof(datos));

        var v = VariablesDeDiploma.Para(datos, diseno, null);
        datos.Numero = null;
        var apunte = new DiplomaEmitido
        {
            Serie = diseno.Serie,
            Origen = origen,
            Indicativo = v["indicativo"],
            Nombre = string.IsNullOrWhiteSpace(datos.Nombre) ? null : datos.Nombre.Trim(),
            NombreDelDiploma = datos.NombreDelDiploma.Trim(),
            Categoria = string.IsNullOrWhiteSpace(datos.Categoria) ? null : datos.Categoria.Trim(),
            EmitidoUtc = DateTimeOffset.UtcNow,
            PlantillaId = diseno.Id,
            PlantillaNombre = diseno.Nombre,
            Referencias = int.Parse(v["referencias"], CultureInfo.InvariantCulture),
            Qsos = int.Parse(v["qsos"], CultureInfo.InvariantCulture),
            Datos = JsonSerializer.Serialize(datos, Json),
        };

        apunte = await _emitidos.EmitirAsync(apunte, ct).ConfigureAwait(true);
        datos.Numero = apunte.Numero;
        Log.Information("Diploma {Serie} nº {Numero} emitido a {Indicativo}.", apunte.Serie, apunte.Numero, apunte.Indicativo);
        HistorialCambiado?.Invoke(this, apunte);
        return apunte;
    }

    /// <summary>Lo que se imprimio en un diploma del historial, con su numero.</summary>
    /// <param name="emitido">El apunte.</param>
    /// <returns>Los datos; nulos si el apunte no los guarda o estan dañados.</returns>
    public static DatosDeDiploma? DatosDe(DiplomaEmitido emitido)
    {
        ArgumentNullException.ThrowIfNull(emitido);
        if (string.IsNullOrWhiteSpace(emitido.Datos)) return null;
        try
        {
            var datos = JsonSerializer.Deserialize<DatosDeDiploma>(emitido.Datos, Json);
            if (datos is null) return null;
            datos.Filas ??= [];
            datos.Numero = emitido.Numero;
            return datos;
        }
        catch (JsonException ex)
        {
            Log.Warning(ex, "Los datos guardados del diploma {Id} están dañados.", emitido.Id);
            return null;
        }
    }

    /// <summary>Las hojas del diploma.</summary>
    /// <param name="diseno">La plantilla.</param>
    /// <param name="datos">Los datos.</param>
    /// <param name="yo">Mi estacion.</param>
    /// <param name="ppp">Resolucion.</param>
    /// <param name="conAnexos">Con las hojas de anexo.</param>
    /// <returns>Las hojas.</returns>
    public IReadOnlyList<HojaDeDiploma> Hojas(DisenoDeDiploma diseno, DatosDeDiploma datos, DatosDeMiEstacion yo, double ppp, bool conAnexos = true) =>
        DibujanteDeDiplomas.Hojas(diseno, VariablesDeDiploma.Para(datos, diseno, yo), datos.Filas, Disenos.RutaDeImagen, ppp, conAnexos);

    /// <summary>El diploma en PNG (solo la primera hoja).</summary>
    /// <param name="diseno">La plantilla.</param>
    /// <param name="datos">Los datos.</param>
    /// <param name="yo">Mi estacion.</param>
    /// <returns>Los bytes del PNG.</returns>
    public byte[] Png(DisenoDeDiploma diseno, DatosDeDiploma datos, DatosDeMiEstacion yo) =>
        DibujanteDeQsl.Codificar(Hojas(diseno, datos, yo, DibujanteDeDiplomas.PppDeImprenta, conAnexos: false)[0].Imagen, FormatoDeImagen.Png);

    /// <summary>El diploma en PDF: una hoja por pagina, del tamaño del papel, con texto buscable.</summary>
    /// <param name="diseno">La plantilla.</param>
    /// <param name="datos">Los datos.</param>
    /// <param name="yo">Mi estacion.</param>
    /// <returns>El PDF.</returns>
    public Impreso Pdf(DisenoDeDiploma diseno, DatosDeDiploma datos, DatosDeMiEstacion yo)
    {
        ArgumentNullException.ThrowIfNull(diseno);
        ArgumentNullException.ThrowIfNull(datos);
        var v = VariablesDeDiploma.Para(datos, diseno, yo);
        var paginas = Hojas(diseno, datos, yo, DibujanteDeDiplomas.PppDeImprenta)
            .Select(h => new PaginaDeImagen(DibujanteDeQsl.Codificar(h.Imagen, FormatoDeImagen.Jpg), diseno.AnchoMm, diseno.AltoMm, h.Textos))
            .ToList();
        var titulo = VariablesDeDiploma.Sustituir("{diploma} nº {numero} — {indicativo}", v);
        return PdfDeTarjetasQsl.Paginas(
            paginas,
            NombreDeFichero(diseno, datos) + ".pdf",
            titulo,
            VariablesDeDiploma.Sustituir("{diploma} · {categoria}", v),
            VariablesDeDiploma.Sustituir("diploma, {indicativo}, {serie}, {numero}", v));
    }

    /// <summary>Manda el diploma en PDF por correo y lo apunta en el historial.</summary>
    /// <param name="diseno">La plantilla.</param>
    /// <param name="datos">Los datos, ya emitidos (con numero).</param>
    /// <param name="emitido">Su apunte del historial.</param>
    /// <param name="para">Direccion.</param>
    /// <param name="ct">Cancelacion.</param>
    /// <returns>Tarea que acaba con el correo aceptado y apuntado.</returns>
    /// <exception cref="ErrorDeCorreo">No se ha podido mandar; no se apunta nada.</exception>
    public async Task EnviarAsync(DisenoDeDiploma diseno, DatosDeDiploma datos, DiplomaEmitido emitido, string para, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(diseno);
        ArgumentNullException.ThrowIfNull(datos);
        ArgumentNullException.ThrowIfNull(emitido);
        var yo = await Qsl.MiEstacionAsync(ct).ConfigureAwait(true);
        var v = VariablesDeDiploma.Para(datos, diseno, yo);
        var pdf = Pdf(diseno, datos, yo);
        var mensaje = new MensajeDeCorreo(
            (para ?? string.Empty).Trim(),
            VariablesDeDiploma.Sustituir(diseno.Asunto, v),
            VariablesDeDiploma.Sustituir(diseno.TextoDelCorreo, v),
            [new AdjuntoDeCorreo(pdf.NombreSugerido, pdf.TipoDeMedio, pdf.Bytes)]);
        await Qsl.EnviarCorreoAsync(mensaje, ct).ConfigureAwait(true);

        var ahora = DateTimeOffset.UtcNow;
        await _emitidos.MarcarEnviadoAsync(emitido.Id, mensaje.Para, ahora, CancellationToken.None).ConfigureAwait(true);
        emitido.Correo = mensaje.Para;
        emitido.EnviadoUtc = ahora;
        Log.Information("Diploma {Serie} nº {Numero} enviado por correo a {Indicativo}.", emitido.Serie, emitido.Numero, emitido.Indicativo);
        HistorialCambiado?.Invoke(this, emitido);
    }

    private static FilaDeJustificante Fila(Qso q)
    {
        var referencia = q.Referencias.FirstOrDefault(r => r.Lado == LadoDeReferencia.Corresponsal);
        var banda = q.Band.EsVacia
            ? (q.Freq.EsCero ? string.Empty : Dominio.Valores.Banda.DesdeFrecuencia(q.Freq).Nombre)
            : q.Band.Nombre;
        return new FilaDeJustificante
        {
            Referencia = referencia?.Codigo ?? string.Empty,
            NombreDeReferencia = referencia?.Descripcion,
            Indicativo = q.Call.Valor ?? string.Empty,
            FechaUtc = q.InicioUtc.ToUniversalTime(),
            Banda = banda ?? string.Empty,
            Modo = q.Mode.EsVacio ? string.Empty : q.Mode.NombreUsual,
            Rst = q.RstSent.EsVacio ? string.Empty : q.RstSent.Texto,
        };
    }
}
