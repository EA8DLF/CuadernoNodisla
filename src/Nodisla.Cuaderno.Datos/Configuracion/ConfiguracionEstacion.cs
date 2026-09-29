using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Datos.Configuracion;

/// <summary>Mapeo de la tabla <c>estacion</c>, los perfiles de estacion.</summary>
public sealed class ConfiguracionEstacion : IEntityTypeConfiguration<Estacion>
{
    /// <summary>Aplica el mapeo.</summary>
    public void Configure(EntityTypeBuilder<Estacion> constructor)
    {
        ArgumentNullException.ThrowIfNull(constructor);

        constructor.ToTable("estacion");
        constructor.HasKey(e => e.Id);
        constructor.Property(e => e.Id).ValueGeneratedOnAdd();
        constructor.Property(e => e.NombrePerfil).IsRequired();
        constructor.Property(e => e.Activo).HasDefaultValue(true);

        constructor.HasIndex(e => e.NombrePerfil)
            .HasDatabaseName("ux_estacion_nombre")
            .IsUnique();

        constructor.Ignore(e => e.Coordenada);
    }
}
