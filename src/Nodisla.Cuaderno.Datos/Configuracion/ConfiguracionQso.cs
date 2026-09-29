using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Datos.Configuracion;

/// <summary>Mapeo de la tabla <c>qso</c>, el corazon del cuaderno.</summary>
public sealed class ConfiguracionQso : IEntityTypeConfiguration<Qso>
{
    /// <summary>Aplica el mapeo.</summary>
    public void Configure(EntityTypeBuilder<Qso> constructor)
    {
        ArgumentNullException.ThrowIfNull(constructor);

        constructor.ToTable("qso");
        constructor.HasKey(q => q.Id);
        constructor.Property(q => q.Id).ValueGeneratedOnAdd();

        // El submodo ADIF se guarda aparte del modo principal: la clave natural del cuaderno
        // se define sobre el modo, y meter el submodo dentro romperia la deteccion de duplicados.
        constructor.Property<string?>(NombresDeColumna.PropiedadSubmodo);

        constructor.Property(q => q.Dxcc).HasDefaultValue(0);
        constructor.Property(q => q.SilentKey).HasDefaultValue(false);

        constructor.HasOne<Estacion>()
            .WithMany()
            .HasForeignKey(q => q.EstacionId)
            .OnDelete(DeleteBehavior.SetNull);

        constructor.HasMany(q => q.Confirmaciones)
            .WithOne()
            .HasForeignKey(c => c.QsoId)
            .OnDelete(DeleteBehavior.Cascade);

        constructor.HasMany(q => q.Referencias)
            .WithOne()
            .HasForeignKey(r => r.QsoId)
            .OnDelete(DeleteBehavior.Cascade);

        constructor.HasMany(q => q.CamposExtra)
            .WithOne()
            .HasForeignKey(c => c.QsoId)
            .OnDelete(DeleteBehavior.Cascade);

        constructor.Navigation(q => q.Confirmaciones).AutoInclude(false);
        constructor.Navigation(q => q.Referencias).AutoInclude(false);
        constructor.Navigation(q => q.CamposExtra).AutoInclude(false);

        // Clave natural compatible con Log4OM: es lo que evita duplicar el cuaderno entero
        // cada vez que se reimporta un ADIF. Va con la fecha descendente para que sirva
        // tambien de indice del «trabajado antes en esta banda y modo».
        constructor.HasIndex(q => new { q.Call, q.Band, q.Mode, q.InicioUtc })
            .HasDatabaseName("ux_qso_natural")
            .IsUnique()
            .IsDescending(false, false, false, true);

        constructor.HasIndex(q => q.Uuid)
            .HasDatabaseName("ux_qso_uuid")
            .IsUnique();

        // «Trabajado antes»: la consulta mas frecuente de todo el programa.
        constructor.HasIndex(q => new { q.Call, q.InicioUtc })
            .HasDatabaseName("ix_qso_call")
            .IsDescending(false, true);

        constructor.HasIndex(q => new { q.Dxcc, q.Band, q.Mode })
            .HasDatabaseName("ix_qso_dxcc_band_mode");

        constructor.HasIndex(q => new { q.Dxcc, q.InicioUtc })
            .HasDatabaseName("ix_qso_dxcc_fecha");

        constructor.HasIndex(q => q.InicioUtc)
            .HasDatabaseName("ix_qso_fecha")
            .IsDescending(true);

        constructor.HasIndex(q => new { q.Band, q.Mode, q.InicioUtc })
            .HasDatabaseName("ix_qso_band_mode_fecha")
            .IsDescending(false, false, true);

        // El localizador vacio se guarda como cadena vacia, no como nulo: el indice parcial
        // filtra por cadena vacia en vez de por IS NULL.
        constructor.HasIndex(q => q.Gridsquare)
            .HasDatabaseName("ix_qso_grid")
            .HasFilter("gridsquare <> ''");

        constructor.HasIndex(q => new { q.Dxcc, q.State })
            .HasDatabaseName("ix_qso_estado")
            .HasFilter("state IS NOT NULL");

        constructor.HasIndex(q => new { q.Cqz, q.Band, q.Mode })
            .HasDatabaseName("ix_qso_cq")
            .HasFilter("cqz IS NOT NULL");

        constructor.HasIndex(q => new { q.Ituz, q.Band, q.Mode })
            .HasDatabaseName("ix_qso_itu")
            .HasFilter("ituz IS NOT NULL");

        constructor.HasIndex(q => new { q.ContestId, q.InicioUtc })
            .HasDatabaseName("ix_qso_contest")
            .HasFilter("contest_id IS NOT NULL");

        constructor.HasIndex(q => new { q.EstacionId, q.InicioUtc })
            .HasDatabaseName("ix_qso_estacion")
            .IsDescending(false, true);

        // Propiedades calculadas del dominio: no son columnas.
        constructor.Ignore(q => q.Duracion);
        constructor.Ignore(q => q.CoordenadaCorresponsal);
        constructor.Ignore(q => q.CoordenadaPropia);
        constructor.Ignore(q => q.DistanciaKm);
        constructor.Ignore(q => q.RumboGrados);
        constructor.Ignore(q => q.ClaveNatural);
    }
}
