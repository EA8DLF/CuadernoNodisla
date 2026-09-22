using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Aplicacion.CasosDeUso;

/// <summary>Las tres cosas que pueden ser nuevas de un contacto.</summary>
public enum EjeDeNovedad
{
    /// <summary>La entidad DXCC.</summary>
    Pais,

    /// <summary>La entidad en esa banda.</summary>
    Banda,

    /// <summary>La entidad en ese modo.</summary>
    Modo,
}

/// <summary>
/// Una casilla de la matriz de novedad: un eje cruzado con una via de confirmacion.
/// </summary>
/// <param name="Eje">Que se mira: el pais, la banda o el modo.</param>
/// <param name="Medio">Via de confirmacion.</param>
/// <param name="Trabajado">Ya hay contactos de ese eje en el cuaderno.</param>
/// <param name="Confirmado">Ademas, hay alguno confirmado por esa via.</param>
public sealed record CasillaDeNovedad(
    EjeDeNovedad Eje,
    MedioDeConfirmacion Medio,
    bool Trabajado,
    bool Confirmado)
{
    /// <summary>
    /// Vale la pena llamar por esta via: o no esta trabajado, o esta sin confirmar.
    /// </summary>
    /// <remarks>
    /// Confirmado por una via y no por otra es el caso mas comun del diploma a medias: la
    /// entidad esta en el cuaderno y en LoTW, pero falta la tarjeta. Por eso la casilla
    /// distingue tres estados y no dos.
    /// </remarks>
    public bool Aporta => !Confirmado;
}

/// <summary>Una casilla de la matriz de banda por familia de modo para un indicativo.</summary>
/// <param name="Banda">Banda de la columna.</param>
/// <param name="Familia">Fila: fonia, telegrafia o digitales.</param>
/// <param name="Contactos">Cuantos contactos hay en esa casilla.</param>
/// <param name="Confirmados">Cuantos de ellos tienen alguna confirmacion recibida.</param>
public sealed record CasillaDeBandaYModo(string Banda, FamiliaDeModo Familia, int Contactos, int Confirmados);

/// <summary>Las tres filas de la matriz de banda por modo.</summary>
public enum FamiliaDeModo
{
    /// <summary>Fonia: SSB, AM, FM.</summary>
    Fonia,

    /// <summary>Telegrafia.</summary>
    Telegrafia,

    /// <summary>Modos digitales.</summary>
    Digital,
}

/// <summary>Lo que se sabe de un indicativo de un vistazo.</summary>
/// <param name="Indicativo">Indicativo consultado.</param>
/// <param name="Novedad">Las doce casillas de eje por via de confirmacion.</param>
/// <param name="BandaYModo">Las casillas de banda por familia de modo con ese indicativo.</param>
/// <param name="ContactosConElIndicativo">Cuantas veces se ha trabajado ese indicativo.</param>
public sealed record Retrato(
    Indicativo Indicativo,
    IReadOnlyList<CasillaDeNovedad> Novedad,
    IReadOnlyList<CasillaDeBandaYModo> BandaYModo,
    int ContactosConElIndicativo);

/// <summary>
/// Responde, mientras se teclea un indicativo, a si merece la pena llamarle.
/// </summary>
/// <remarks>
/// <para>
/// Son dos rejillas. La primera cruza <b>lo que seria nuevo</b> —pais, banda, modo— con las
/// <b>vias de confirmacion</b> —papel, eQSL, LoTW, QRZ—: de un vistazo dice si esa entidad ya
/// la tienes y por donde la tienes confirmada. La segunda pone las bandas en columnas y las
/// tres familias de modo en filas, para ese indicativo en concreto: es el «trabajado antes»
/// condensado, que hasta ahora se decia con una frase.
/// </para>
/// <para>
/// Ninguno de los dos datos es nuevo: el cuaderno ya los tiene. Lo que faltaba era ensenarlos.
/// </para>
/// <para>
/// El coste esta pensado para que se pueda disparar en cada tecla. Las doce casillas de
/// novedad salen de doce <b>recuentos</b> en la base, no de traerse los contactos: cada uno
/// devuelve una fila y el numero total. La matriz de banda y modo si necesita los contactos
/// del indicativo, pero de un indicativo hay unos pocos.
/// </para>
/// </remarks>
public sealed class RetratoDelIndicativo
{
    /// <summary>Vias de confirmacion que se ensenan, en el orden de la rejilla.</summary>
    public static readonly IReadOnlyList<MedioDeConfirmacion> Vias =
    [
        MedioDeConfirmacion.Papel,
        MedioDeConfirmacion.Eqsl,
        MedioDeConfirmacion.Lotw,
        MedioDeConfirmacion.QrzCom,
    ];

    /// <summary>Ejes de la rejilla, en orden.</summary>
    public static readonly IReadOnlyList<EjeDeNovedad> Ejes =
        [EjeDeNovedad.Pais, EjeDeNovedad.Banda, EjeDeNovedad.Modo];

    /// <summary>Cuantos contactos del indicativo se traen para la matriz de banda y modo.</summary>
    private const int ContactosQueSeMiran = 200;

    private readonly IRepositorioQso _cuaderno;

    /// <summary>Monta el caso de uso.</summary>
    /// <param name="cuaderno">Repositorio de contactos.</param>
    public RetratoDelIndicativo(IRepositorioQso cuaderno) =>
        _cuaderno = cuaderno ?? throw new ArgumentNullException(nameof(cuaderno));

    /// <summary>
    /// Arma el retrato de un indicativo en una banda y un modo.
    /// </summary>
    /// <param name="indicativo">Indicativo que se esta tecleando.</param>
    /// <param name="dxcc">Entidad DXCC a la que pertenece, o cero si no se ha resuelto.</param>
    /// <param name="banda">Banda en la que se trabajaria.</param>
    /// <param name="modo">Modo en el que se trabajaria.</param>
    /// <param name="estacionId">Perfil de estacion activo, o nulo para todos.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>Las dos rejillas.</returns>
    public async Task<Retrato> ArmarAsync(
        Indicativo indicativo,
        int dxcc,
        Banda banda,
        Modo modo,
        long? estacionId = null,
        CancellationToken ct = default)
    {
        var novedad = dxcc > 0
            ? await NovedadAsync(dxcc, banda, modo, estacionId, ct).ConfigureAwait(false)
            : SinDatos();

        var (bandaYModo, cuantos) = await BandaYModoAsync(indicativo, estacionId, ct).ConfigureAwait(false);

        return new Retrato(indicativo, novedad, bandaYModo, cuantos);
    }

    /// <summary>Las doce casillas, cada una de un recuento.</summary>
    private async Task<IReadOnlyList<CasillaDeNovedad>> NovedadAsync(
        int dxcc,
        Banda banda,
        Modo modo,
        long? estacionId,
        CancellationToken ct)
    {
        var casillas = new List<CasillaDeNovedad>(Ejes.Count * Vias.Count);

        foreach (var eje in Ejes)
        {
            var criterio = CriterioDe(eje, dxcc, banda, modo, estacionId);

            var trabajados = await CuantosAsync(criterio, ct).ConfigureAwait(false);

            foreach (var via in Vias)
            {
                var confirmados = trabajados == 0
                    ? 0
                    : await CuantosAsync(criterio with { ConfirmadoPor = via }, ct).ConfigureAwait(false);

                casillas.Add(new CasillaDeNovedad(eje, via, trabajados > 0, confirmados > 0));
            }
        }

        return casillas;
    }

    private static CriterioQso CriterioDe(
        EjeDeNovedad eje,
        int dxcc,
        Banda banda,
        Modo modo,
        long? estacionId) => eje switch
        {
            EjeDeNovedad.Banda => new CriterioQso { Dxcc = dxcc, Band = banda, EstacionId = estacionId },
            EjeDeNovedad.Modo => new CriterioQso { Dxcc = dxcc, Mode = modo.Principal, EstacionId = estacionId },
            _ => new CriterioQso { Dxcc = dxcc, EstacionId = estacionId },
        };

    private async Task<int> CuantosAsync(CriterioQso criterio, CancellationToken ct)
    {
        // Se pide una sola fila: lo que interesa es el total, que la base resuelve con un
        // recuento. Traerse los contactos para contarlos seria recorrer el cuaderno entero en
        // cada tecla.
        var pagina = await _cuaderno.BuscarAsync(criterio, 0, 1, ct).ConfigureAwait(false);
        return pagina.TotalFiltrado;
    }

    /// <summary>Las casillas de banda por familia de modo con ese indicativo.</summary>
    private async Task<(IReadOnlyList<CasillaDeBandaYModo> Casillas, int Contactos)> BandaYModoAsync(
        Indicativo indicativo,
        long? estacionId,
        CancellationToken ct)
    {
        var pagina = await _cuaderno.BuscarAsync(
            new CriterioQso { Call = indicativo.Valor, EstacionId = estacionId },
            0,
            ContactosQueSeMiran,
            ct).ConfigureAwait(false);

        var cuentas = new Dictionary<(string Banda, FamiliaDeModo Familia), (int Contactos, int Confirmados)>();

        foreach (var qso in pagina.Elementos)
        {
            var clave = (qso.Band.Nombre, FamiliaDe(qso.Mode));
            cuentas.TryGetValue(clave, out var actual);

            cuentas[clave] = (actual.Contactos + 1, actual.Confirmados + (EstaConfirmado(qso) ? 1 : 0));
        }

        var casillas = cuentas
            .Select(c => new CasillaDeBandaYModo(c.Key.Banda, c.Key.Familia, c.Value.Contactos, c.Value.Confirmados))
            .ToList();

        return (casillas, pagina.TotalFiltrado);
    }

    /// <summary>El contacto tiene alguna confirmacion recibida que cuente.</summary>
    private static bool EstaConfirmado(Qso qso) => qso.Confirmaciones.Any(c => c.EstaConfirmada);

    /// <summary>
    /// A que fila de la matriz va cada modo.
    /// </summary>
    /// <remarks>
    /// Son las tres familias con las que se opera de verdad: se habla, se telegrafia o se
    /// teclea. Un modo desconocido cae en digitales, que es donde aparecen los modos nuevos.
    /// </remarks>
    public static FamiliaDeModo FamiliaDe(Modo modo) => modo.Principal switch
    {
        "SSB" or "AM" or "FM" or "DIGITALVOICE" => FamiliaDeModo.Fonia,
        "CW" => FamiliaDeModo.Telegrafia,
        _ => FamiliaDeModo.Digital,
    };

    private static IReadOnlyList<CasillaDeNovedad> SinDatos() =>
        [.. from eje in Ejes from via in Vias select new CasillaDeNovedad(eje, via, false, false)];
}
