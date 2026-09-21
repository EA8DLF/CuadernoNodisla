using System.Globalization;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

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
public sealed class RegistrarQso(IRepositorioQso repositorioQso, IRepositorioEstacion repositorioEstacion)
{
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

        var ahora = DateTimeOffset.UtcNow;
        qso.CreadoUtc = ahora;
        qso.ModificadoUtc = ahora;

        var idNuevo = await repositorioQso.AnadirAsync(qso, ct).ConfigureAwait(false);
        qso.Id = idNuevo;
        return new ResultadoDeRegistro { Id = idNuevo, Registrado = qso };
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
            errores.Add("Falta el indicativo del corresponsal.");
        }
        else if (!Indicativo.EsFormaValida(qso.Call.Valor))
        {
            errores.Add($"El indicativo «{qso.Call.Valor}» no tiene una forma válida.");
        }

        if (qso.Mode.EsVacio) errores.Add("Falta el modo.");

        if (qso.Band.EsVacia && qso.Freq.EsCero)
        {
            errores.Add("Falta la banda o la frecuencia.");
        }
        else if (!qso.Band.EsVacia && !qso.Freq.EsCero && !qso.Band.Contiene(qso.Freq))
        {
            errores.Add(string.Format(
                CultureInfo.CurrentCulture,
                "La frecuencia {0} MHz no está dentro de la banda {1}.",
                qso.Freq.AAdif(),
                qso.Band.Nombre));
        }

        if (qso.InicioUtc != default && qso.InicioUtc > DateTimeOffset.UtcNow + MargenDeFuturo)
        {
            errores.Add("La hora del contacto está en el futuro.");
        }

        if (qso.FinUtc is { } fin && fin < qso.InicioUtc)
        {
            errores.Add("La hora de fin es anterior a la de inicio.");
        }

        if (qso.StationCallsign.EsVacio)
        {
            errores.Add("Falta el indicativo de mi estación: elija un perfil de estación.");
        }

        return errores;
    }
}
