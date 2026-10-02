using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Aplicacion.CasosDeUso;

/// <summary>
/// Completa los contactos con la ficha del corresponsal en QRZ.com (o HamQTH).
/// </summary>
/// <remarks>
/// <para>
/// <b>Solo rellena lo vacio.</b> Lo que el operador ha escrito, o lo que el cuaderno ya sabia,
/// manda sobre lo que diga el servicio: QRZ.com tiene fichas viejas, y un nombre corregido a
/// mano no se puede pisar con el de la base.
/// </para>
/// <para>
/// <b>Nunca falla hacia fuera.</b> Sin red, sin credenciales o con el servicio caido, la
/// consulta devuelve nulo y deja el motivo en <see cref="Problema"/> para el aviso discreto de
/// la barra de estado. Un contacto no se deja de guardar porque QRZ.com no conteste.
/// </para>
/// </remarks>
public sealed class CompletadorDeQso
{
    /// <summary>Lo que se espera a QRZ antes de guardar sin sus datos.</summary>
    public static readonly TimeSpan EsperaPorOmision = TimeSpan.FromSeconds(1.5);

    private readonly IConsultaIndicativo _consulta;
    private readonly Func<TimeSpan, CancellationToken, Task> _esperar;

    /// <summary>Crea el completador.</summary>
    /// <param name="consulta">Consulta de indicativos, normalmente la cadena con cache.</param>
    /// <param name="esperar">
    /// Como esperar. Sustituible en las pruebas: ninguna prueba mira el reloj de pared.
    /// </param>
    /// <param name="espera">Cuanto se espera a la consulta antes de guardar sin ella.</param>
    /// <param name="activo">Si el operador tiene activado completar con QRZ.</param>
    public CompletadorDeQso(
        IConsultaIndicativo consulta,
        Func<TimeSpan, CancellationToken, Task>? esperar = null,
        TimeSpan? espera = null,
        Func<bool>? activo = null)
    {
        _consulta = consulta ?? throw new ArgumentNullException(nameof(consulta));
        _esperar = esperar ?? Task.Delay;
        Espera = espera ?? EsperaPorOmision;
        _activo = activo ?? (() => true);
    }

    private readonly Func<bool> _activo;

    /// <summary>Cuanto se espera a la consulta antes de guardar sin ella.</summary>
    public TimeSpan Espera { get; }

    /// <summary>Hay servicio configurado y el operador quiere completar.</summary>
    public bool EstaDisponible
    {
        get
        {
            try
            {
                return _activo() && _consulta.EstaDisponible;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>Nombre del servicio, para la barra.</summary>
    public string Servicio => _consulta.Nombre;

    /// <summary>Por que fallo la ultima consulta; nulo si fue bien.</summary>
    public string? Problema { get; private set; }

    /// <summary>Aviso del servicio en la ultima ficha (suscripcion parcial, por ejemplo).</summary>
    public string? Aviso { get; private set; }

    /// <summary>Cambio el estado de la consulta: para refrescar el aviso de la barra.</summary>
    public event EventHandler? EstadoCambiado;

    /// <summary>
    /// Consulta el indicativo. Nunca lanza: si algo falla, devuelve nulo y deja el motivo.
    /// </summary>
    /// <param name="indicativo">Indicativo del corresponsal.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    public async Task<FichaIndicativo?> ConsultarAsync(Indicativo indicativo, CancellationToken ct = default)
    {
        if (indicativo.EsVacio || !EstaDisponible) return null;
        try
        {
            var ficha = await _consulta.ConsultarAsync(indicativo, ct).ConfigureAwait(false);
            Cambiar(problema: null, aviso: ficha?.Aviso);
            return ficha;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            Cambiar(Textos.F("Servicios.Aplicacion.NoHaContestado", _consulta.Nombre, ex.Message), Aviso);
            return null;
        }
    }

    /// <summary>
    /// Espera a una consulta ya lanzada como mucho <see cref="Espera"/>.
    /// </summary>
    /// <returns>Verdadero si termino a tiempo.</returns>
    public async Task<bool> EsperarAsync(Task consulta, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(consulta);
        if (consulta.IsCompleted) return true;

        using var cancelar = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var plazo = _esperar(Espera, cancelar.Token);
        var primera = await Task.WhenAny(consulta, plazo).ConfigureAwait(false);
        await cancelar.CancelAsync().ConfigureAwait(false);
        return primera == consulta;
    }

    /// <summary>
    /// Rellena los campos vacios del contacto con la ficha. No pisa nada que ya tenga valor.
    /// </summary>
    /// <param name="qso">Contacto a completar.</param>
    /// <param name="ficha">Ficha del corresponsal.</param>
    /// <returns>Verdadero si ha cambiado algun campo.</returns>
    public static bool Completar(Qso qso, FichaIndicativo? ficha)
    {
        ArgumentNullException.ThrowIfNull(qso);
        if (ficha is null) return false;

        // Una ficha de otro indicativo (QRZ devuelve la del titular si se consulta EA8DLF/P)
        // vale igual para el nombre; lo que no se hace es completar un contacto con la ficha de
        // una consulta que ya no le corresponde.
        if (!Coincide(qso.Call, ficha.Indicativo)) return false;

        var cambios = false;
        qso.Name = Poner(qso.Name, ficha.Nombre, ref cambios);
        qso.Qth = Poner(qso.Qth, ficha.Localidad, ref cambios);
        qso.Address = Poner(qso.Address, ficha.Direccion, ref cambios);
        qso.Country = Poner(qso.Country, ficha.Pais, ref cambios);
        qso.State = Poner(qso.State, ficha.DivisionPrimaria, ref cambios);
        qso.Cnty = Poner(qso.Cnty, ficha.DivisionSecundaria, ref cambios);
        qso.Email = Poner(qso.Email, ficha.CorreoElectronico, ref cambios);
        qso.Web = Poner(qso.Web, ficha.Web?.Trim(), ref cambios);
        qso.QslVia = Poner(qso.QslVia, ficha.GestorQsl, ref cambios);
        qso.IotaIslandId = Poner(qso.IotaIslandId, ficha.Iota, ref cambios);

        if (qso.Gridsquare.EsVacio && !ficha.Localizador.EsVacio)
        {
            qso.Gridsquare = ficha.Localizador;
            cambios = true;
        }

        if (qso.Lat is null && qso.Lon is null && ficha.Latitud is { } lat && ficha.Longitud is { } lon)
        {
            qso.Lat = lat;
            qso.Lon = lon;
            cambios = true;
        }

        if (qso.Dxcc == 0 && ficha.Dxcc is { } dxcc && dxcc > 0)
        {
            qso.Dxcc = dxcc;
            cambios = true;
        }

        if (qso.Cqz is null && ficha.ZonaCq is > 0)
        {
            qso.Cqz = ficha.ZonaCq;
            cambios = true;
        }

        if (qso.Ituz is null && ficha.ZonaItu is > 0)
        {
            qso.Ituz = ficha.ZonaItu;
            cambios = true;
        }

        return cambios;
    }

    /// <summary>
    /// La ficha es del mismo indicativo, o del indicativo base (sin prefijo ni sufijo de
    /// portable), que es como contestan los servicios.
    /// </summary>
    private static bool Coincide(Indicativo delQso, Indicativo deLaFicha)
    {
        if (deLaFicha.EsVacio) return true;
        if (string.Equals(delQso.Valor, deLaFicha.Valor, StringComparison.OrdinalIgnoreCase)) return true;
        return delQso.Valor.Split('/').Any(parte =>
            string.Equals(parte, deLaFicha.Valor, StringComparison.OrdinalIgnoreCase));
    }

    private static string? Poner(string? actual, string? nuevo, ref bool cambios)
    {
        if (!string.IsNullOrWhiteSpace(actual) || string.IsNullOrWhiteSpace(nuevo)) return actual;
        cambios = true;
        return nuevo.Trim();
    }

    private void Cambiar(string? problema, string? aviso)
    {
        if (problema == Problema && aviso == Aviso) return;
        Problema = problema;
        Aviso = aviso;
        EstadoCambiado?.Invoke(this, EventArgs.Empty);
    }
}
