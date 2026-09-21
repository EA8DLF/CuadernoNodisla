using System.IO;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Ui.Desarrollo;

/// <summary>
/// Consulta de indicativos que todavia no existe. Se declara no disponible, asi la ventana
/// sabe que no puede rellenar la ficha del corresponsal y no promete lo que no puede dar.
/// </summary>
public sealed class ConsultaIndicativoNoDisponible : IConsultaIndicativo
{
    /// <inheritdoc />
    public string Nombre => "Sin servicio de consulta";

    /// <inheritdoc />
    public bool EstaDisponible => false;

    /// <inheritdoc />
    public Task<FichaIndicativo?> ConsultarAsync(Indicativo indicativo, CancellationToken ct = default) =>
        Task.FromResult<FichaIndicativo?>(null);
}

/// <summary>Lector ADIF provisional: no lee nada hasta que llegue el de verdad.</summary>
public sealed class LectorAdifNoDisponible : ILectorAdif
{
    /// <inheritdoc />
    public Task<LecturaAdif> LeerAsync(Stream origen, CancellationToken ct = default) =>
        Task.FromResult(new LecturaAdif
        {
            Qsos = [],
            Avisos =
            [
                new AvisoAdif(0, null, "La importación ADIF todavía no está disponible.", NivelDeAviso.Error),
            ],
        });
}

/// <summary>Escritor ADIF provisional: avisa de que la exportacion aun no esta lista.</summary>
public sealed class EscritorAdifNoDisponible : IEscritorAdif
{
    /// <inheritdoc />
    public Task EscribirAsync(
        IAsyncEnumerable<Qso> qsos,
        Stream destino,
        OpcionesAdif? opciones = null,
        CancellationToken ct = default) =>
        Task.FromException(new NotSupportedException("La exportación ADIF todavía no está disponible."));
}
