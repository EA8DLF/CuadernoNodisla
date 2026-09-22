using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Aplicacion.Pruebas;

/// <summary>
/// El retrato del indicativo es lo que responde a «¿le llamo o no?» mientras se teclea.
/// </summary>
/// <remarks>
/// Lo que se fija aquí es que no mienta en ninguno de los tres estados que distingue: nuevo,
/// trabajado pero sin confirmar por esa vía, y cerrado. Confundir el segundo con el tercero
/// haría que el operador dejara pasar justo los contactos que le faltan para cerrar el diploma.
/// </remarks>
public sealed class RetratoDelIndicativoPruebas
{
    private static readonly Banda Veinte = Banda.Parse("20m");
    private static readonly Modo Ssb = Modo.Parse("SSB");

    [Fact]
    public async Task Una_entidad_que_no_esta_sale_nueva_por_las_cuatro_vias()
    {
        var retrato = await Armar([], 291, "K1ABC");

        retrato.Novedad.Should().HaveCount(12);
        retrato.Novedad.Should().OnlyContain(c => !c.Trabajado && !c.Confirmado);
        retrato.Novedad.Should().OnlyContain(c => c.Aporta);
        retrato.ContactosConElIndicativo.Should().Be(0);
    }

    [Fact]
    public async Task Trabajada_y_sin_confirmar_no_es_lo_mismo_que_cerrada()
    {
        // Un contacto con la entidad, sin ninguna confirmación recibida.
        var qso = Contacto("K1ABC", 291, Veinte, Ssb);

        var retrato = await Armar([qso], 291, "K1ABC");

        var pais = retrato.Novedad.Where(c => c.Eje == EjeDeNovedad.Pais).ToList();

        pais.Should().OnlyContain(c => c.Trabajado);
        pais.Should().OnlyContain(c => !c.Confirmado);

        // Sigue aportando: está en el cuaderno pero no cuenta para ningún diploma todavía.
        pais.Should().OnlyContain(c => c.Aporta);
    }

    [Fact]
    public async Task Confirmada_por_una_via_sigue_faltando_por_las_otras()
    {
        var qso = Contacto("K1ABC", 291, Veinte, Ssb);
        qso.Confirmaciones.Add(new QsoConfirmacion
        {
            Medio = MedioDeConfirmacion.Lotw,
            Recibido = EstadoDeConfirmacion.Confirmado,
        });

        var retrato = await Armar([qso], 291, "K1ABC");

        var pais = retrato.Novedad.Where(c => c.Eje == EjeDeNovedad.Pais).ToList();

        pais.Single(c => c.Medio == MedioDeConfirmacion.Lotw).Confirmado.Should().BeTrue();
        pais.Single(c => c.Medio == MedioDeConfirmacion.Lotw).Aporta.Should().BeFalse();

        pais.Single(c => c.Medio == MedioDeConfirmacion.Papel).Confirmado.Should().BeFalse();
        pais.Single(c => c.Medio == MedioDeConfirmacion.Papel).Aporta.Should().BeTrue();
    }

    [Fact]
    public async Task La_entidad_en_otra_banda_deja_la_banda_nueva()
    {
        // La entidad está trabajada, pero en 40 metros: en 20 sigue siendo nueva.
        var qso = Contacto("K1ABC", 291, Banda.Parse("40m"), Ssb);

        var retrato = await Armar([qso], 291, "K1ABC");

        retrato.Novedad.Where(c => c.Eje == EjeDeNovedad.Pais).Should().OnlyContain(c => c.Trabajado);
        retrato.Novedad.Where(c => c.Eje == EjeDeNovedad.Banda).Should().OnlyContain(c => !c.Trabajado);
    }

    [Fact]
    public async Task La_entidad_en_otro_modo_deja_el_modo_nuevo()
    {
        var qso = Contacto("K1ABC", 291, Veinte, Modo.Parse("CW"));

        var retrato = await Armar([qso], 291, "K1ABC");

        retrato.Novedad.Where(c => c.Eje == EjeDeNovedad.Modo).Should().OnlyContain(c => !c.Trabajado);
    }

    [Fact]
    public async Task La_rejilla_pone_cada_contacto_en_su_banda_y_su_familia_de_modo()
    {
        List<Qso> cuaderno =
        [
            Contacto("K1ABC", 291, Veinte, Ssb),
            Contacto("K1ABC", 291, Veinte, Ssb),
            Contacto("K1ABC", 291, Banda.Parse("40m"), Modo.Parse("CW")),
            Contacto("K1ABC", 291, Veinte, Modo.Parse("FT8")),
        ];

        var retrato = await Armar(cuaderno, 291, "K1ABC");

        retrato.ContactosConElIndicativo.Should().Be(4);

        Casilla(retrato, "20m", FamiliaDeModo.Fonia).Contactos.Should().Be(2);
        Casilla(retrato, "40m", FamiliaDeModo.Telegrafia).Contactos.Should().Be(1);
        Casilla(retrato, "20m", FamiliaDeModo.Digital).Contactos.Should().Be(1);
    }

    [Fact]
    public async Task La_rejilla_distingue_lo_confirmado_de_lo_solo_trabajado()
    {
        var sinQsl = Contacto("K1ABC", 291, Veinte, Ssb);
        var conQsl = Contacto("K1ABC", 291, Veinte, Ssb);
        conQsl.Confirmaciones.Add(new QsoConfirmacion
        {
            Medio = MedioDeConfirmacion.Papel,
            Recibido = EstadoDeConfirmacion.Confirmado,
        });

        var retrato = await Armar([sinQsl, conQsl], 291, "K1ABC");

        var casilla = Casilla(retrato, "20m", FamiliaDeModo.Fonia);
        casilla.Contactos.Should().Be(2);
        casilla.Confirmados.Should().Be(1);
    }

    [Fact]
    public async Task Una_confirmacion_solo_enviada_no_cuenta_como_recibida()
    {
        // Mandar la tarjeta no es tenerla. Contarlo al revés inflaría los diplomas.
        var qso = Contacto("K1ABC", 291, Veinte, Ssb);
        qso.Confirmaciones.Add(new QsoConfirmacion
        {
            Medio = MedioDeConfirmacion.Papel,
            Enviado = EstadoDeConfirmacion.Confirmado,
            Recibido = EstadoDeConfirmacion.Ninguno,
        });

        var retrato = await Armar([qso], 291, "K1ABC");

        Casilla(retrato, "20m", FamiliaDeModo.Fonia).Confirmados.Should().Be(0);
    }

    [Fact]
    public async Task Sin_entidad_resuelta_no_se_inventa_nada()
    {
        // Un indicativo que el resolutor no sabe de dónde es: las doce casillas salen vacías,
        // que es la verdad, en vez de decir «nuevo» y mandar a llamar a ciegas.
        var retrato = await Armar([Contacto("K1ABC", 291, Veinte, Ssb)], 0, "XX0XXX");

        retrato.Novedad.Should().HaveCount(12);
        retrato.Novedad.Should().OnlyContain(c => !c.Trabajado && !c.Confirmado);
    }

    [Theory]
    [InlineData("SSB", FamiliaDeModo.Fonia)]
    [InlineData("AM", FamiliaDeModo.Fonia)]
    [InlineData("FM", FamiliaDeModo.Fonia)]
    [InlineData("CW", FamiliaDeModo.Telegrafia)]
    [InlineData("FT8", FamiliaDeModo.Digital)]
    [InlineData("RTTY", FamiliaDeModo.Digital)]
    public void Cada_modo_cae_en_su_familia(string modo, FamiliaDeModo esperada) =>
        RetratoDelIndicativo.FamiliaDe(Modo.Parse(modo)).Should().Be(esperada);

    private static CasillaDeBandaYModo Casilla(Retrato retrato, string banda, FamiliaDeModo familia) =>
        retrato.BandaYModo.Single(c => c.Banda == banda && c.Familia == familia);

    private static async Task<Retrato> Armar(IReadOnlyList<Qso> cuaderno, int dxcc, string indicativo)
    {
        var caso = new RetratoDelIndicativo(new CuadernoDePrueba(cuaderno));
        return await caso.ArmarAsync(Indicativo.Parse(indicativo), dxcc, Veinte, Ssb);
    }

    private static Qso Contacto(string indicativo, int dxcc, Banda banda, Modo modo) => new()
    {
        Call = Indicativo.Parse(indicativo),
        Dxcc = dxcc,
        Band = banda,
        Mode = modo,
        InicioUtc = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero),
    };

    /// <summary>
    /// Cuaderno de mentira que filtra en memoria lo mismo que la base filtra en SQL.
    /// </summary>
    /// <remarks>
    /// Solo entiende los criterios que usa el retrato: indicativo, entidad, banda, modo y via
    /// de confirmacion. Cualquier otro se ignora a proposito, para que una prueba que dependa
    /// de un criterio no cubierto falle en vez de pasar por casualidad.
    /// </remarks>
    private sealed class CuadernoDePrueba(IReadOnlyList<Qso> contactos) : IRepositorioQso
    {
        public Task<Pagina<Qso>> BuscarAsync(
            CriterioQso criterio,
            int desplazamiento,
            int limite,
            CancellationToken ct = default)
        {
            var filtrados = contactos.Where(q =>
                (criterio.Call is null || string.Equals(q.Call.Valor, criterio.Call, StringComparison.OrdinalIgnoreCase))
                && (criterio.Dxcc is null || q.Dxcc == criterio.Dxcc)
                && (criterio.Band is null || q.Band == criterio.Band.Value)
                && (criterio.Mode is null || string.Equals(q.Mode.Principal, criterio.Mode, StringComparison.OrdinalIgnoreCase))
                && (criterio.ConfirmadoPor is null
                    || q.Confirmaciones.Any(c => c.Medio == criterio.ConfirmadoPor && c.EstaConfirmada)))
                .ToList();

            var pagina = filtrados.Skip(desplazamiento).Take(limite).ToList();
            return Task.FromResult(new Pagina<Qso>(pagina, filtrados.Count, desplazamiento));
        }

        // Lo demas del puerto no lo usa el retrato: si alguna prueba llegara aqui, es que el
        // caso de uso ha empezado a hacer algo que nadie ha revisado.
        public Task<Qso?> ObtenerAsync(long id, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Qso?> ObtenerPorUuidAsync(Guid uuid, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<long> AnadirAsync(Qso qso, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ResultadoDeLote> AnadirLoteAsync(
            IEnumerable<Qso> qsos,
            bool omitirDuplicados = true,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task ActualizarAsync(Qso qso, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task EliminarAsync(long id, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Qso?> BuscarDuplicadoAsync(Qso candidato, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Qso>> TrabajadoAntesAsync(
            Indicativo indicativo,
            int maximo = 50,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<int> ContarAsync(CancellationToken ct = default) =>
            Task.FromResult(contactos.Count);
    }
}
