using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Datos.Configuracion;

/// <summary>Mapeo de la tabla <c>diploma_emitido</c>: el historial de diplomas emitidos.</summary>
public sealed class ConfiguracionDiplomaEmitido : IEntityTypeConfiguration<DiplomaEmitido>
{
    /// <summary>Aplica el mapeo.</summary>
    public void Configure(EntityTypeBuilder<DiplomaEmitido> constructor)
    {
        ArgumentNullException.ThrowIfNull(constructor);

        constructor.ToTable("diploma_emitido");
        constructor.HasKey(d => d.Id);
        constructor.Property(d => d.Id).ValueGeneratedOnAdd();
        constructor.Property(d => d.Serie).IsRequired().UseCollation("NOCASE");
        constructor.Property(d => d.Indicativo).IsRequired().UseCollation("NOCASE");
        constructor.Property(d => d.NombreDelDiploma).IsRequired();

        // Texto y no numero: el cuaderno se puede abrir con cualquier visor de SQLite y
        // «Emitido» se entiende solo.
        constructor.Property(d => d.Origen).HasConversion<string>();

        // El numero no se repite dentro de su serie: es la garantia de la numeracion correlativa.
        constructor.HasIndex(d => new { d.Serie, d.Numero })
            .IsUnique()
            .HasDatabaseName("ux_diploma_serie_numero");

        constructor.HasIndex(d => d.EmitidoUtc)
            .HasDatabaseName("ix_diploma_emitido")
            .IsDescending(true);

        constructor.HasIndex(d => d.Indicativo)
            .HasDatabaseName("ix_diploma_indicativo");
    }
}
