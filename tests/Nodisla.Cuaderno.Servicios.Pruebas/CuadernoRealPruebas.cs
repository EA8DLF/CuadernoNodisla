using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Servicios.Adif;
using Nodisla.Cuaderno.Servicios.Emparejamiento;

namespace Nodisla.Cuaderno.Servicios.Pruebas;

/// <summary>
/// Emparejamiento contra el cuaderno real del operador.
/// </summary>
/// <remarks>
/// <para>
/// Los contactos de juguete de las demas pruebas no tienen las rarezas de un cuaderno de
/// verdad: indicativos con barra, modos con submodo, contactos seguidos con el mismo
/// corresponsal y horas repetidas al segundo. Esta prueba lee el respaldo ADIF del operador y
/// comprueba el emparejamiento contra esos datos.
/// </para>
/// <para>
/// El respaldo <b>no se copia al repositorio</b>: es el cuaderno del operador. Si no esta en la
/// maquina, la prueba se salta sola en vez de fallar.
/// </para>
/// </remarks>
public class CuadernoRealPruebas
{
    private static readonly EmparejadorDeConfirmaciones Emparejador = new();

    private static string? RutaDelRespaldo()
    {
        var carpeta = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Log4OM2", "backup");
        if (!Directory.Exists(carpeta)) return null;
        return Directory.EnumerateFiles(carpeta, "*.adi")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static List<Qso> LeerCuaderno(string ruta)
    {
        var qsos = new List<Qso>();
        long id = 0;
        foreach (var registro in AdifLigero.LeerRegistros(File.ReadAllText(ruta)))
        {
            if (!LectorDeConfirmaciones.TryLeerClave(registro, out var clave)) continue;
            Modo.TryParse(
                LectorDeConfirmaciones.Campo(registro, "MODE"),
                LectorDeConfirmaciones.Campo(registro, "SUBMODE"),
                out var modo);
            qsos.Add(new Qso
            {
                Id = ++id,
                Call = clave.Call,
                Band = clave.Band,
                Mode = modo.EsVacio ? Modo.Crudo(clave.Mode) : modo,
                InicioUtc = clave.InicioUtc,
            });
        }
        return qsos;
    }

    private static ConfirmacionDescargada Confirmacion(
        Qso qso, TimeSpan desfase = default, string? modo = null, Banda? banda = null) =>
        new(qso.Call,
            banda ?? qso.Band,
            modo ?? qso.Mode.NombreUsual,
            qso.InicioUtc + desfase,
            MedioDeConfirmacion.Lotw,
            null,
            Verificada: true);

    [Fact]
    public void El_respaldo_real_se_lee_entero()
    {
        var ruta = RutaDelRespaldo();
        if (ruta is null) return; // No hay respaldo en esta maquina.

        var qsos = LeerCuaderno(ruta);

        qsos.Should().HaveCountGreaterThan(1000);
        qsos.Should().OnlyContain(q => !q.Call.EsVacio);
        qsos.Should().OnlyContain(q => q.InicioUtc.Year > 1990);
    }

    [Fact]
    public void Con_los_mismos_datos_empareja_todo_el_cuaderno()
    {
        var ruta = RutaDelRespaldo();
        if (ruta is null) return;

        var qsos = LeerCuaderno(ruta);
        var confirmaciones = qsos.Select(q => Confirmacion(q)).ToList();

        var resultado = Emparejador.Emparejar(qsos, confirmaciones);

        resultado.SinPareja.Should().BeEmpty();
        resultado.Parejas.Should().HaveCount(qsos.Count);
    }

    [Fact]
    public void Un_desfase_de_veinte_minutos_sigue_emparejando_en_lotw()
    {
        var ruta = RutaDelRespaldo();
        if (ruta is null) return;

        var qsos = LeerCuaderno(ruta);
        var confirmaciones = qsos.Select(q => Confirmacion(q, TimeSpan.FromMinutes(20))).ToList();

        var resultado = Emparejador.Emparejar(qsos, confirmaciones);

        // Con un cuaderno real puede haber dos contactos con el mismo corresponsal dentro de la
        // ventana; lo que se comprueba es que el desfase no rompe el emparejamiento en masa.
        resultado.Parejas.Should().HaveCountGreaterThan((int)(qsos.Count * 0.98));
    }

    [Fact]
    public void Con_la_banda_cambiada_nada_casa_y_todo_acaba_sin_pareja()
    {
        var ruta = RutaDelRespaldo();
        if (ruta is null) return;

        var qsos = LeerCuaderno(ruta)
            .Where(q => q.Band.Nombre is "20m")
            .Take(200)
            .ToList();
        if (qsos.Count == 0) return;

        var confirmaciones = qsos.Select(q => Confirmacion(q, banda: Banda.Parse("6mm"))).ToList();

        var resultado = Emparejador.Emparejar(qsos, confirmaciones);

        resultado.Parejas.Should().BeEmpty();
        resultado.SinPareja.Should().HaveCount(confirmaciones.Count);
    }

    [Fact]
    public void Los_modos_con_submodo_del_cuaderno_real_casan_con_el_nombre_que_usa_el_servicio()
    {
        var ruta = RutaDelRespaldo();
        if (ruta is null) return;

        var qsos = LeerCuaderno(ruta)
            .Where(q => q.Mode.Submodo is not null)
            .Take(200)
            .ToList();
        if (qsos.Count == 0) return;

        // El servicio manda el modo principal (MFSK) donde el cuaderno guarda el submodo (FT4).
        var confirmaciones = qsos.Select(q => Confirmacion(q, modo: q.Mode.Principal)).ToList();

        var resultado = Emparejador.Emparejar(qsos, confirmaciones);

        resultado.SinPareja.Should().BeEmpty();
    }
}
