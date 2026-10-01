using Microsoft.EntityFrameworkCore;
using Nodisla.Cuaderno.Datos.Configuracion;
using Nodisla.Cuaderno.Datos.Conversores;
using Nodisla.Cuaderno.Datos.Interceptores;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Datos;

/// <summary>
/// El cuaderno en SQLite: un solo fichero con los contactos, los perfiles de estacion y todo
/// lo que cuelga de ellos.
/// </summary>
public sealed class ContextoCuaderno : DbContext
{
    /// <summary>Crea el contexto con las opciones dadas.</summary>
    /// <param name="opciones">Opciones de EF Core, normalmente con el proveedor de SQLite.</param>
    public ContextoCuaderno(DbContextOptions<ContextoCuaderno> opciones)
        : base(opciones)
    {
    }

    /// <summary>Los contactos.</summary>
    public DbSet<Qso> Qsos => Set<Qso>();

    /// <summary>Los perfiles de estacion.</summary>
    public DbSet<Estacion> Estaciones => Set<Estacion>();

    /// <summary>Confirmaciones por servicio de cada contacto.</summary>
    public DbSet<QsoConfirmacion> Confirmaciones => Set<QsoConfirmacion>();

    /// <summary>Referencias de programas de activacion de cada contacto.</summary>
    public DbSet<QsoReferencia> Referencias => Set<QsoReferencia>();

    /// <summary>Campos ADIF no modelados de cada contacto.</summary>
    public DbSet<QsoCampoExtra> CamposExtra => Set<QsoCampoExtra>();

    /// <summary>Las rondas de control (NET Control).</summary>
    public DbSet<RondaDeControl> Rondas => Set<RondaDeControl>();

    /// <summary>Los participantes de cada ronda.</summary>
    public DbSet<ParticipanteDeRonda> ParticipantesDeRonda => Set<ParticipanteDeRonda>();

    /// <summary>El historial de diplomas emitidos.</summary>
    public DbSet<DiplomaEmitido> DiplomasEmitidos => Set<DiplomaEmitido>();

    /// <inheritdoc/>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        optionsBuilder.AddInterceptors(InterceptorDePragmas.Instancia, InterceptorDeSubmodo.Instancia);
        base.OnConfiguring(optionsBuilder);
    }

    /// <inheritdoc/>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        // Los conversores se declaran por tipo, no por propiedad: asi cualquier campo nuevo
        // del dominio queda mapeado sin tener que acordarse de nada.
        configurationBuilder.Properties<Indicativo>().HaveConversion<ConversorDeIndicativo>();
        configurationBuilder.Properties<Locator>().HaveConversion<ConversorDeLocator>();
        configurationBuilder.Properties<Banda>().HaveConversion<ConversorDeBanda>();
        configurationBuilder.Properties<Modo>().HaveConversion<ConversorDeModo>();
        configurationBuilder.Properties<Frecuencia>().HaveConversion<ConversorDeFrecuencia>();
        configurationBuilder.Properties<Informe>().HaveConversion<ConversorDeInforme>();
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<ConversorDeFechaUtc>();
        configurationBuilder.Properties<Guid>().HaveConversion<ConversorDeGuid>();
        configurationBuilder.Properties<MedioDeConfirmacion>().HaveConversion<ConversorDeMedio>();
        configurationBuilder.Properties<EstadoDeConfirmacion>().HaveConversion<ConversorDeEstado>();
        configurationBuilder.Properties<ViaDeEnvio>().HaveConversion<ConversorDeVia>();
        configurationBuilder.Properties<TipoDeReferencia>().HaveConversion<ConversorDeTipoDeReferencia>();
        configurationBuilder.Properties<LadoDeReferencia>().HaveConversion<ConversorDeLado>();

        base.ConfigureConventions(configurationBuilder);
    }

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new ConfiguracionEstacion());
        modelBuilder.ApplyConfiguration(new ConfiguracionQso());
        modelBuilder.ApplyConfiguration(new ConfiguracionQsoConfirmacion());
        modelBuilder.ApplyConfiguration(new ConfiguracionQsoReferencia());
        modelBuilder.ApplyConfiguration(new ConfiguracionQsoCampoExtra());
        modelBuilder.ApplyConfiguration(new ConfiguracionRonda());
        modelBuilder.ApplyConfiguration(new ConfiguracionParticipanteDeRonda());
        modelBuilder.ApplyConfiguration(new ConfiguracionDiplomaEmitido());

        NombresDeColumna.Aplicar(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }

    /// <inheritdoc/>
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        VolcarSubmodo();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <inheritdoc/>
    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        VolcarSubmodo();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Copia el submodo de cada contacto a su propiedad sombra antes de escribir. Es la otra
    /// mitad de lo que hace <see cref="InterceptorDeSubmodo"/> al leer.
    /// </summary>
    private void VolcarSubmodo()
    {
        foreach (var entrada in ChangeTracker.Entries<Qso>())
        {
            if (entrada.State is EntityState.Added or EntityState.Modified)
            {
                entrada.Property<string?>(NombresDeColumna.PropiedadSubmodo).CurrentValue =
                    entrada.Entity.Mode.Submodo;
            }
        }
    }
}
