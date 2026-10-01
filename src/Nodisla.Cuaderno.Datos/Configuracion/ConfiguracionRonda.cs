using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Datos.Configuracion;

/// <summary>Mapeo de la tabla <c>ronda_de_control</c>: las rondas de NET Control.</summary>
public sealed class ConfiguracionRonda : IEntityTypeConfiguration<RondaDeControl>
{
    /// <summary>Aplica el mapeo.</summary>
    public void Configure(EntityTypeBuilder<RondaDeControl> constructor)
    {
        ArgumentNullException.ThrowIfNull(constructor);

        constructor.HasKey(r => r.Id);
        constructor.Property(r => r.Id).ValueGeneratedOnAdd();
        constructor.Property(r => r.Nombre).IsRequired();

        constructor.HasOne<Estacion>()
            .WithMany()
            .HasForeignKey(r => r.EstacionId)
            .OnDelete(DeleteBehavior.SetNull);

        constructor.HasMany(r => r.Participantes)
            .WithOne()
            .HasForeignKey(p => p.RondaId)
            .OnDelete(DeleteBehavior.Cascade);

        constructor.Navigation(r => r.Participantes).AutoInclude(false);

        // Buscar la ronda abierta es «la que no tiene fin»: es la consulta que hace la
        // ventana en cada arranque para reabrirla sola.
        constructor.HasIndex(r => r.FinUtc)
            .HasDatabaseName("ix_ronda_abierta")
            .HasFilter("fin_utc IS NULL");

        constructor.HasIndex(r => r.InicioUtc)
            .HasDatabaseName("ix_ronda_inicio")
            .IsDescending(true);

        // Propiedades calculadas del dominio: no son columnas.
        constructor.Ignore(r => r.EstaAbierta);
        constructor.Ignore(r => r.Trabajados);
    }
}

/// <summary>Mapeo de la tabla <c>participante_de_ronda</c>: cada entrada de una ronda.</summary>
public sealed class ConfiguracionParticipanteDeRonda : IEntityTypeConfiguration<ParticipanteDeRonda>
{
    /// <summary>Aplica el mapeo.</summary>
    public void Configure(EntityTypeBuilder<ParticipanteDeRonda> constructor)
    {
        ArgumentNullException.ThrowIfNull(constructor);

        constructor.HasKey(p => p.Id);
        constructor.Property(p => p.Id).ValueGeneratedOnAdd();

        constructor.HasIndex(p => new { p.RondaId, p.EntradaUtc })
            .HasDatabaseName("ix_participante_ronda");
    }
}
