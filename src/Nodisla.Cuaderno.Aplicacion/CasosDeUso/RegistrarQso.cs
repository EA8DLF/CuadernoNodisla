using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Aplicacion.CasosDeUso;

/// <summary>Datos con los que se pide registrar un contacto en el cuaderno.</summary>
public sealed record PeticionDeRegistro
{
    /// <summary>Contacto a registrar, tal y como lo ha compuesto la interfaz.</summary>
    public required Qso Qso { get; init; }

    /// <summary>Perfil de estacion a aplicar. Si es nulo se usa el predeterminado.</summary>
    public long? EstacionId { get; init; }

    /// <summary>Registrar aunque ya exista otro contacto con la misma clave natural.</summary>
    public bool AdmitirDuplicado { get; init; }
}

/// <summary>Resultado de intentar registrar un contacto.</summary>
public sealed record ResultadoDeRegistro
{
    /// <summary>El contacto se ha guardado.</summary>
    public bool Correcto => Errores.Count == 0 && Duplicado is null;

    /// <summary>Identificador asignado al contacto guardado.</summary>
    public long Id { get; init; }

    /// <summary>Motivos por los que no se ha podido guardar, en espanol y para el operador.</summary>
    public IReadOnlyList<string> Errores { get; init; } = [];

    /// <summary>Contacto ya existente con la misma clave natural, si se encontro uno.</summary>
    public Qso? Duplicado { get; init; }

    /// <summary>Contacto guardado, ya con los valores por omision aplicados.</summary>
    public Qso? Registrado { get; init; }
}

/// <summary>
/// Registra un contacto nuevo: aplica el perfil de estacion, rellena lo que falte con los
/// valores por omision, valida y comprueba que no sea un duplicado.
/// </summary>
/// <remarks>
/// Si hay <see cref="CompletadorDeQso"/>, antes de guardar se piden a QRZ.com los datos que
/// falten (nombre, QTH, localizador…). Se espera un momento; si la consulta tarda mas, el
/// contacto se guarda sin ellos y se completa en cuanto llegue la respuesta. Todo contacto
/// guardado se anuncia por <see cref="AvisosDeQsos"/>, que es donde escucha la cola de subidas.
/// </remarks>
public sealed class RegistrarQso(
    IRepositorioQso repositorioQso,
    IRepositorioEstacion repositorioEstacion,
    CompletadorDeQso? completador = null,
    AvisosDeQsos? avisos = null)
{
    /// <summary>
    /// Ultima tarea de completado en segundo plano. Solo para las pruebas: la interfaz no
    /// espera nunca por ella.
    /// </summary>
    public Task CompletadoPendiente { get; private set; } = Task.CompletedTask;

    /// <summary>Margen que se tolera al comprobar que la hora del contacto no es futura.</summary>
    private static readonly TimeSpan MargenDeFuturo = TimeSpan.FromMinutes(2);

    /// <summary>Registra el contacto de la peticion.</summary>
    public async Task<ResultadoDeRegistro> EjecutarAsync(PeticionDeRegistro peticion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(peticion);
        var qso = peticion.Qso;

        var estacion = peticion.EstacionId is { } id
            ? await repositorioEstacion.ObtenerAsync(id, ct).ConfigureAwait(false)
            : await repositorioEstacion.PredeterminadaAsync(ct).ConfigureAwait(false);
        estacion?.AplicarA(qso);

        AplicarValoresPorOmision(qso);

        var errores = Validar(qso);
        if (errores.Count > 0) return new ResultadoDeRegistro { Errores = errores };

        if (!peticion.AdmitirDuplicado)
        {
            var duplicado = await repositorioQso.BuscarDuplicadoAsync(qso, ct).ConfigureAwait(false);
            if (duplicado is not null) return new ResultadoDeRegistro { Duplicado = duplicado };
        }

        // La consulta a QRZ se lanza sin el testigo de la peticion: si tarda, sigue despues
        // de guardar y completa el contacto ya guardado.
        Task<FichaIndicativo?>? consulta = null;
        if (completador is { EstaDisponible: true })
        {
            consulta = completador.ConsultarAsync(qso.Call);
            if (await completador.EsperarAsync(consulta, ct).ConfigureAwait(false))
            {
                CompletadorDeQso.Completar(qso, await consulta.ConfigureAwait(false));
                consulta = null;
            }
        }

        var ahora = DateTimeOffset.UtcNow;
        qso.CreadoUtc = ahora;
        qso.ModificadoUtc = ahora;

        var idNuevo = await repositorioQso.AnadirAsync(qso, ct).ConfigureAwait(false);
        qso.Id = idNuevo;

        if (consulta is null) avisos?.Avisar(qso, TipoDeGuardado.Nuevo);
        else CompletadoPendiente = CompletarDespuesAsync(qso, consulta);

        return new ResultadoDeRegistro { Id = idNuevo, Registrado = qso };
    }

    /// <summary>
    /// Completa el contacto ya guardado cuando llega la ficha, y solo entonces lo anuncia: asi
    /// la cola de subidas manda el contacto ya con el nombre y el localizador.
    /// </summary>
    private async Task CompletarDespuesAsync(Qso qso, Task<FichaIndicativo?> consulta)
    {
        try
        {
            var ficha = await consulta.ConfigureAwait(false);
            if (CompletadorDeQso.Completar(qso, ficha))
            {
                qso.ModificadoUtc = DateTimeOffset.UtcNow;
                await repositorioQso.ActualizarAsync(qso).ConfigureAwait(false);
                avisos?.Avisar(qso, TipoDeGuardado.Completado);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning(
                $"No se ha podido completar el contacto con {qso.Call.Valor}: {ex.Message}");
        }
        finally
        {
            avisos?.Avisar(qso, TipoDeGuardado.Nuevo);
        }
    }

    /// <summary>
    /// Rellena lo que el operador no ha tecleado: la hora, la banda deducida de la frecuencia
    /// y los informes por omision del modo.
    /// </summary>
    public static void AplicarValoresPorOmision(Qso qso)
    {
        ArgumentNullException.ThrowIfNull(qso);

        if (qso.InicioUtc == default) qso.InicioUtc = DateTimeOffset.UtcNow;
        if (qso.Band.EsVacia && !qso.Freq.EsCero) qso.Band = Banda.DesdeFrecuencia(qso.Freq);
        if (qso.BandRx.EsVacia && qso.FreqRx is { } rx && !rx.EsCero) qso.BandRx = Banda.DesdeFrecuencia(rx);
        if (qso.RstSent.EsVacio) qso.RstSent = Informe.PorOmisionPara(qso.Mode);
        if (qso.RstRcvd.EsVacio) qso.RstRcvd = Informe.PorOmisionPara(qso.Mode);
        qso.Origen ??= "teclado";
    }

    /// <summary>
    /// Comprueba lo minimo imprescindible para que el contacto sea valido y exportable a ADIF.
    /// Devuelve la lista vacia si todo esta bien.
    /// </summary>
    public static IReadOnlyList<string> Validar(Qso qso)
    {
        ArgumentNullException.ThrowIfNull(qso);
        var errores = new List<string>();

        if (qso.Call.EsVacio)
        {
            errores.Add(Textos.T("Servicios.Aplicacion.FaltaIndicativo"));
        }
        else if (!Indicativo.EsFormaValida(qso.Call.Valor))
        {
            errores.Add(Textos.F("Servicios.Aplicacion.IndicativoNoValido", qso.Call.Valor));
        }

        if (qso.Mode.EsVacio) errores.Add(Textos.T("Servicios.Aplicacion.FaltaModo"));

        if (qso.Band.EsVacia && qso.Freq.EsCero)
        {
            errores.Add(Textos.T("Servicios.Aplicacion.FaltaBanda"));
        }
        else if (!qso.Band.EsVacia && !qso.Freq.EsCero && !qso.Band.Contiene(qso.Freq))
        {
            errores.Add(Textos.F(
                "Servicios.Aplicacion.FrecuenciaFueraDeBanda",
                qso.Freq.AAdif(),
                qso.Band.Nombre));
        }

        if (qso.InicioUtc != default && qso.InicioUtc > DateTimeOffset.UtcNow + MargenDeFuturo)
        {
            errores.Add(Textos.T("Servicios.Aplicacion.HoraEnElFuturo"));
        }

        if (qso.FinUtc is { } fin && fin < qso.InicioUtc)
        {
            errores.Add(Textos.T("Servicios.Aplicacion.FinAntesDeInicio"));
        }

        if (qso.StationCallsign.EsVacio)
        {
            errores.Add(Textos.T("Servicios.Aplicacion.FaltaMiIndicativo"));
        }

        return errores;
    }
}
