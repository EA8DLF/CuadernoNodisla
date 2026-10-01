using Microsoft.Extensions.DependencyInjection;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Ui.Datos;

/// <summary>
/// Envoltorios que abren un ambito nuevo en cada llamada al cuaderno.
/// </summary>
/// <remarks>
/// <para>
/// La capa de datos registra sus repositorios <b>por ambito</b>, porque cada uno lleva dentro
/// un contexto de Entity Framework y ese contexto no se puede compartir entre hilos ni durar
/// para siempre. Los modelos de vista de esta interfaz, en cambio, son <b>unicos</b>: viven
/// mientras vive la ventana. Pedir un servicio por ambito desde uno unico es justo lo que el
/// contenedor prohibe, y con razon.
/// </para>
/// <para>
/// La salida es esta: los modelos de vista reciben un envoltorio unico que, en cada llamada,
/// abre su ambito, hace el trabajo y lo cierra. Asi cada consulta tiene su propio contexto
/// —dos pantallas que consultan a la vez no se pisan— y nadie tiene que enterarse de ambitos.
/// </para>
/// <para>
/// El precio es un ambito por llamada, que en una base local es despreciable: lo caro de una
/// consulta es leer el disco, no construir el contexto.
/// </para>
/// </remarks>
public static class UnAmbitoPorLlamada
{
    /// <summary>Registra los envoltorios sobre los repositorios por ambito de la capa de datos.</summary>
    /// <param name="servicios">Coleccion de servicios.</param>
    /// <returns>La misma coleccion, para poder encadenar.</returns>
    /// <remarks>
    /// Los envoltorios piden dentro del ambito las <b>clases concretas</b> de la capa de datos,
    /// no sus interfaces. Tiene que ser asi: al registrar el envoltorio como unico, el suyo es
    /// el ultimo registro de la interfaz, de modo que pedir la interfaz dentro del ambito
    /// devolveria otra vez el envoltorio y se llamaria a si mismo hasta reventar la pila. Paso,
    /// y el sintoma fue justo ese: la aplicacion moria al arrancar con un desbordamiento de
    /// pila y sin una linea en el registro.
    /// </remarks>
    public static IServiceCollection AnadirPuentesDelCuaderno(this IServiceCollection servicios)
    {
        ArgumentNullException.ThrowIfNull(servicios);

        servicios.AddScoped<Cuaderno.Datos.Repositorios.RepositorioQso>();
        servicios.AddScoped<Cuaderno.Datos.Repositorios.RepositorioEstacion>();
        servicios.AddScoped<Cuaderno.Datos.Informes.ConsultasDeInforme>();
        servicios.AddScoped<Cuaderno.Datos.Repositorios.RepositorioRondas>();
        servicios.AddScoped<Cuaderno.Datos.Repositorios.RepositorioDiplomasEmitidos>();

        servicios.AddSingleton<IRepositorioQso>(
            p => new RepositorioQsoConAmbito(p.GetRequiredService<IServiceScopeFactory>()));
        servicios.AddSingleton<IRepositorioEstacion>(
            p => new RepositorioEstacionConAmbito(p.GetRequiredService<IServiceScopeFactory>()));
        servicios.AddSingleton<IConsultasDeInforme>(
            p => new ConsultasDeInformeConAmbito(p.GetRequiredService<IServiceScopeFactory>()));
        servicios.AddSingleton<IRepositorioRondas>(
            p => new RepositorioRondasConAmbito(p.GetRequiredService<IServiceScopeFactory>()));
        servicios.AddSingleton<IRepositorioDiplomasEmitidos>(
            p => new RepositorioDiplomasEmitidosConAmbito(p.GetRequiredService<IServiceScopeFactory>()));

        return servicios;
    }

    /// <summary>Hace el trabajo dentro de un ambito recien abierto y lo cierra al salir.</summary>
    private static async Task<T> EnUnAmbitoAsync<TServicio, T>(
        IServiceScopeFactory fabrica,
        Func<TServicio, Task<T>> trabajo)
        where TServicio : notnull
    {
        using var ambito = fabrica.CreateScope();
        return await trabajo(ambito.ServiceProvider.GetRequiredService<TServicio>()).ConfigureAwait(false);
    }

    /// <summary>Igual, para lo que no devuelve nada.</summary>
    private static async Task EnUnAmbitoAsync<TServicio>(
        IServiceScopeFactory fabrica,
        Func<TServicio, Task> trabajo)
        where TServicio : notnull
    {
        using var ambito = fabrica.CreateScope();
        await trabajo(ambito.ServiceProvider.GetRequiredService<TServicio>()).ConfigureAwait(false);
    }

    private sealed class RepositorioQsoConAmbito(IServiceScopeFactory fabrica) : IRepositorioQso
    {
        /// <inheritdoc />
        public Task<Qso?> ObtenerAsync(long id, CancellationToken ct = default) =>
            EnUnAmbitoAsync<Cuaderno.Datos.Repositorios.RepositorioQso, Qso?>(fabrica, r => r.ObtenerAsync(id, ct));

        /// <inheritdoc />
        public Task<Qso?> ObtenerPorUuidAsync(Guid uuid, CancellationToken ct = default) =>
            EnUnAmbitoAsync<Cuaderno.Datos.Repositorios.RepositorioQso, Qso?>(fabrica, r => r.ObtenerPorUuidAsync(uuid, ct));

        /// <inheritdoc />
        public Task<Pagina<Qso>> BuscarAsync(
            CriterioQso criterio,
            int desplazamiento,
            int limite,
            CancellationToken ct = default) =>
            EnUnAmbitoAsync<Cuaderno.Datos.Repositorios.RepositorioQso, Pagina<Qso>>(
                fabrica,
                r => r.BuscarAsync(criterio, desplazamiento, limite, ct));

        /// <inheritdoc />
        public Task<long> AnadirAsync(Qso qso, CancellationToken ct = default) =>
            EnUnAmbitoAsync<Cuaderno.Datos.Repositorios.RepositorioQso, long>(fabrica, r => r.AnadirAsync(qso, ct));

        /// <inheritdoc />
        public Task<ResultadoDeLote> AnadirLoteAsync(
            IEnumerable<Qso> qsos,
            bool omitirDuplicados = true,
            CancellationToken ct = default) =>
            EnUnAmbitoAsync<Cuaderno.Datos.Repositorios.RepositorioQso, ResultadoDeLote>(
                fabrica,
                r => r.AnadirLoteAsync(qsos, omitirDuplicados, ct));

        /// <inheritdoc />
        public Task ActualizarAsync(Qso qso, CancellationToken ct = default) =>
            EnUnAmbitoAsync<Cuaderno.Datos.Repositorios.RepositorioQso>(fabrica, r => r.ActualizarAsync(qso, ct));

        /// <inheritdoc />
        public Task EliminarAsync(long id, CancellationToken ct = default) =>
            EnUnAmbitoAsync<Cuaderno.Datos.Repositorios.RepositorioQso>(fabrica, r => r.EliminarAsync(id, ct));

        /// <inheritdoc />
        public Task<Qso?> BuscarDuplicadoAsync(Qso candidato, CancellationToken ct = default) =>
            EnUnAmbitoAsync<Cuaderno.Datos.Repositorios.RepositorioQso, Qso?>(fabrica, r => r.BuscarDuplicadoAsync(candidato, ct));

        /// <inheritdoc />
        public Task<IReadOnlyList<Qso>> TrabajadoAntesAsync(
            Indicativo indicativo,
            int maximo = 50,
            CancellationToken ct = default) =>
            EnUnAmbitoAsync<Cuaderno.Datos.Repositorios.RepositorioQso, IReadOnlyList<Qso>>(
                fabrica,
                r => r.TrabajadoAntesAsync(indicativo, maximo, ct));

        /// <inheritdoc />
        public Task<int> ContarAsync(CancellationToken ct = default) =>
            EnUnAmbitoAsync<Cuaderno.Datos.Repositorios.RepositorioQso, int>(fabrica, r => r.ContarAsync(ct));
    }

    private sealed class RepositorioEstacionConAmbito(IServiceScopeFactory fabrica) : IRepositorioEstacion
    {
        /// <inheritdoc />
        public Task<IReadOnlyList<Estacion>> TodasAsync(bool soloActivas = true, CancellationToken ct = default) =>
            EnUnAmbitoAsync<Cuaderno.Datos.Repositorios.RepositorioEstacion, IReadOnlyList<Estacion>>(
                fabrica,
                r => r.TodasAsync(soloActivas, ct));

        /// <inheritdoc />
        public Task<Estacion?> ObtenerAsync(long id, CancellationToken ct = default) =>
            EnUnAmbitoAsync<Cuaderno.Datos.Repositorios.RepositorioEstacion, Estacion?>(fabrica, r => r.ObtenerAsync(id, ct));

        /// <inheritdoc />
        public Task<Estacion?> PredeterminadaAsync(CancellationToken ct = default) =>
            EnUnAmbitoAsync<Cuaderno.Datos.Repositorios.RepositorioEstacion, Estacion?>(fabrica, r => r.PredeterminadaAsync(ct));

        /// <inheritdoc />
        public Task<long> AnadirAsync(Estacion estacion, CancellationToken ct = default) =>
            EnUnAmbitoAsync<Cuaderno.Datos.Repositorios.RepositorioEstacion, long>(fabrica, r => r.AnadirAsync(estacion, ct));

        /// <inheritdoc />
        public Task ActualizarAsync(Estacion estacion, CancellationToken ct = default) =>
            EnUnAmbitoAsync<Cuaderno.Datos.Repositorios.RepositorioEstacion>(fabrica, r => r.ActualizarAsync(estacion, ct));

        /// <inheritdoc />
        public Task EstablecerPredeterminadaAsync(long id, CancellationToken ct = default) =>
            EnUnAmbitoAsync<Cuaderno.Datos.Repositorios.RepositorioEstacion>(fabrica, r => r.EstablecerPredeterminadaAsync(id, ct));
    }

    private sealed class ConsultasDeInformeConAmbito(IServiceScopeFactory fabrica) : IConsultasDeInforme
    {
        /// <inheritdoc />
        public Task<IReadOnlyList<ResumenPorBanda>> PorBandaAsync(CancellationToken ct = default) =>
            EnUnAmbitoAsync<Cuaderno.Datos.Informes.ConsultasDeInforme, IReadOnlyList<ResumenPorBanda>>(fabrica, c => c.PorBandaAsync(ct));

        /// <inheritdoc />
        public Task<IReadOnlyList<ResumenPorModo>> PorModoAsync(CancellationToken ct = default) =>
            EnUnAmbitoAsync<Cuaderno.Datos.Informes.ConsultasDeInforme, IReadOnlyList<ResumenPorModo>>(fabrica, c => c.PorModoAsync(ct));

        /// <inheritdoc />
        public Task<IReadOnlyList<CasillaDxcc>> MatrizDxccPorBandaAsync(
            MedioDeConfirmacion medio = MedioDeConfirmacion.Lotw,
            CancellationToken ct = default) =>
            EnUnAmbitoAsync<Cuaderno.Datos.Informes.ConsultasDeInforme, IReadOnlyList<CasillaDxcc>>(
                fabrica,
                c => c.MatrizDxccPorBandaAsync(medio, ct));

        /// <inheritdoc />
        public Task<Novedad> ConsultarNovedadAsync(
            int dxcc,
            Banda banda,
            Modo modo,
            CancellationToken ct = default) =>
            EnUnAmbitoAsync<Cuaderno.Datos.Informes.ConsultasDeInforme, Novedad>(
                fabrica,
                c => c.ConsultarNovedadAsync(dxcc, banda, modo, ct));

        /// <inheritdoc />
        public Task<TotalesDelCuaderno> TotalesAsync(CancellationToken ct = default) =>
            EnUnAmbitoAsync<Cuaderno.Datos.Informes.ConsultasDeInforme, TotalesDelCuaderno>(fabrica, c => c.TotalesAsync(ct));
    }

    private sealed class RepositorioRondasConAmbito(IServiceScopeFactory fabrica) : IRepositorioRondas
    {
        /// <inheritdoc />
        public Task<long> AbrirAsync(RondaDeControl ronda, CancellationToken ct = default) =>
            EnUnAmbitoAsync<Cuaderno.Datos.Repositorios.RepositorioRondas, long>(fabrica, r => r.AbrirAsync(ronda, ct));

        /// <inheritdoc />
        public Task<RondaDeControl?> ObtenerAbiertaAsync(CancellationToken ct = default) =>
            EnUnAmbitoAsync<Cuaderno.Datos.Repositorios.RepositorioRondas, RondaDeControl?>(
                fabrica,
                r => r.ObtenerAbiertaAsync(ct));

        /// <inheritdoc />
        public Task<RondaDeControl?> ObtenerAsync(long id, CancellationToken ct = default) =>
            EnUnAmbitoAsync<Cuaderno.Datos.Repositorios.RepositorioRondas, RondaDeControl?>(
                fabrica,
                r => r.ObtenerAsync(id, ct));

        /// <inheritdoc />
        public Task<IReadOnlyList<RondaDeControl>> ListarAsync(CancellationToken ct = default) =>
            EnUnAmbitoAsync<Cuaderno.Datos.Repositorios.RepositorioRondas, IReadOnlyList<RondaDeControl>>(
                fabrica,
                r => r.ListarAsync(ct));

        /// <inheritdoc />
        public Task CerrarAsync(long rondaId, DateTimeOffset finUtc, CancellationToken ct = default) =>
            EnUnAmbitoAsync<Cuaderno.Datos.Repositorios.RepositorioRondas>(
                fabrica,
                r => r.CerrarAsync(rondaId, finUtc, ct));

        /// <inheritdoc />
        public Task<long> AnadirParticipanteAsync(ParticipanteDeRonda participante, CancellationToken ct = default) =>
            EnUnAmbitoAsync<Cuaderno.Datos.Repositorios.RepositorioRondas, long>(
                fabrica,
                r => r.AnadirParticipanteAsync(participante, ct));

        /// <inheritdoc />
        public Task ActualizarParticipanteAsync(ParticipanteDeRonda participante, CancellationToken ct = default) =>
            EnUnAmbitoAsync<Cuaderno.Datos.Repositorios.RepositorioRondas>(
                fabrica,
                r => r.ActualizarParticipanteAsync(participante, ct));

        /// <inheritdoc />
        public Task EliminarParticipanteAsync(long participanteId, CancellationToken ct = default) =>
            EnUnAmbitoAsync<Cuaderno.Datos.Repositorios.RepositorioRondas>(
                fabrica,
                r => r.EliminarParticipanteAsync(participanteId, ct));
    }

    private sealed class RepositorioDiplomasEmitidosConAmbito(IServiceScopeFactory fabrica) : IRepositorioDiplomasEmitidos
    {
        /// <inheritdoc />
        public Task<int> SiguienteNumeroAsync(string serie, CancellationToken ct = default) =>
            EnUnAmbitoAsync<Cuaderno.Datos.Repositorios.RepositorioDiplomasEmitidos, int>(fabrica, r => r.SiguienteNumeroAsync(serie, ct));

        /// <inheritdoc />
        public Task<DiplomaEmitido> EmitirAsync(DiplomaEmitido diploma, CancellationToken ct = default) =>
            EnUnAmbitoAsync<Cuaderno.Datos.Repositorios.RepositorioDiplomasEmitidos, DiplomaEmitido>(fabrica, r => r.EmitirAsync(diploma, ct));

        /// <inheritdoc />
        public Task<IReadOnlyList<DiplomaEmitido>> ListarAsync(int limite = 500, CancellationToken ct = default) =>
            EnUnAmbitoAsync<Cuaderno.Datos.Repositorios.RepositorioDiplomasEmitidos, IReadOnlyList<DiplomaEmitido>>(fabrica, r => r.ListarAsync(limite, ct));

        /// <inheritdoc />
        public Task<DiplomaEmitido?> ObtenerAsync(long id, CancellationToken ct = default) =>
            EnUnAmbitoAsync<Cuaderno.Datos.Repositorios.RepositorioDiplomasEmitidos, DiplomaEmitido?>(fabrica, r => r.ObtenerAsync(id, ct));

        /// <inheritdoc />
        public Task MarcarEnviadoAsync(long id, string correo, DateTimeOffset cuando, CancellationToken ct = default) =>
            EnUnAmbitoAsync<Cuaderno.Datos.Repositorios.RepositorioDiplomasEmitidos>(fabrica, r => r.MarcarEnviadoAsync(id, correo, cuando, ct));
    }
}
