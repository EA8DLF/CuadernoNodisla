using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Datos.Configuracion;

/// <summary>
/// Mapeo de <c>qso_confirmacion</c>. Log4OM guarda esto como un JSON dentro de una columna;
/// aqui es una tabla indexada para que los diplomas se calculen con SQL.
/// </summary>
public sealed class ConfiguracionQsoConfirmacion : IEntityTypeConfiguration<QsoConfirmacion>
{
    /// <summary>Aplica el mapeo.</summary>
    public void Configure(EntityTypeBuilder<QsoConfirmacion> constructor)
    {
        ArgumentNullException.ThrowIfNull(constructor);

        constructor.ToTable("qso_confirmacion");
        constructor.HasKey(c => c.Id);
        constructor.Property(c => c.Id).ValueGeneratedOnAdd();

        // Una sola fila por contacto y servicio: el modelo de datos lo pide como clave primaria
        // compuesta, pero la entidad del dominio ya trae identificador propio, asi que la misma
        // garantia se da con un indice unico.
        constructor.HasIndex(c => new { c.QsoId, c.Medio })
            .HasDatabaseName("ux_conf_qso_servicio")
            .IsUnique();

        constructor.HasIndex(c => new { c.Medio, c.Recibido, c.QsoId })
            .HasDatabaseName("ix_conf_servicio_rec");

        constructor.HasIndex(c => new { c.Medio, c.Enviado, c.QsoId })
            .HasDatabaseName("ix_conf_servicio_env");

        constructor.Ignore(c => c.EstaConfirmada);
    }
}

/// <summary>Mapeo de <c>qso_referencia</c>: IOTA, SOTA, POTA, WWFF y demas programas.</summary>
public sealed class ConfiguracionQsoReferencia : IEntityTypeConfiguration<QsoReferencia>
{
    /// <summary>Aplica el mapeo.</summary>
    public void Configure(EntityTypeBuilder<QsoReferencia> constructor)
    {
        ArgumentNullException.ThrowIfNull(constructor);

        constructor.ToTable("qso_referencia");
        constructor.HasKey(r => r.Id);
        constructor.Property(r => r.Id).ValueGeneratedOnAdd();
        constructor.Property(r => r.Codigo).IsRequired();

        constructor.HasIndex(r => new { r.Tipo, r.Codigo, r.Lado })
            .HasDatabaseName("ix_ref_award_ref");

        constructor.HasIndex(r => r.QsoId)
            .HasDatabaseName("ix_ref_qso");

        constructor.HasIndex(r => new { r.Lado, r.Tipo, r.Codigo })
            .HasDatabaseName("ix_ref_propia");

        // Los programas que ADIF no modela se guardan como award_code OTRA con el nombre en
        // «programa»: sin este indice, cada consulta de un diploma de esos tiene que recorrer
        // todas las referencias OTRA del cuaderno.
        constructor.HasIndex(r => new { r.NombrePrograma, r.Codigo })
            .HasDatabaseName("ix_ref_programa")
            .HasFilter("programa IS NOT NULL");
    }
}

/// <summary>
/// Mapeo de <c>qso_campo_extra</c>: los campos ADIF que el modelo no representa y los
/// <c>APP_*</c> de otros programas, para que importar y exportar no pierda ni un dato.
/// </summary>
public sealed class ConfiguracionQsoCampoExtra : IEntityTypeConfiguration<QsoCampoExtra>
{
    /// <summary>Aplica el mapeo.</summary>
    public void Configure(EntityTypeBuilder<QsoCampoExtra> constructor)
    {
        ArgumentNullException.ThrowIfNull(constructor);

        constructor.ToTable("qso_campo_extra");
        constructor.HasKey(c => c.Id);
        constructor.Property(c => c.Id).ValueGeneratedOnAdd();
        constructor.Property(c => c.Nombre).IsRequired();
        constructor.Property(c => c.Valor).IsRequired();

        constructor.HasIndex(c => new { c.QsoId, c.Nombre })
            .HasDatabaseName("ux_campo_extra")
            .IsUnique();
    }
}
