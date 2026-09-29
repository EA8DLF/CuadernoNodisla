using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Satelites.Catalogo;

namespace Nodisla.Cuaderno.Satelites.Doppler;

/// <summary>
/// El par de frecuencias nominales con el que se trabaja un satelite: por donde se sube y por
/// donde baja, dentro del transpondedor elegido.
/// </summary>
/// <param name="Satelite">Abreviatura del satelite, la que va en <c>SAT_NAME</c>.</param>
/// <param name="Transpondedor">Transpondedor del catalogo con el que se opera.</param>
/// <param name="SubidaNominal">Frecuencia a la que el satelite tiene que recibir.</param>
/// <param name="BajadaNominal">Frecuencia a la que el satelite transmite.</param>
/// <remarks>
/// <para>
/// <b>Nominal quiere decir sin Doppler.</b> Estas son las frecuencias del satelite, las que hay
/// que anotar en el cuaderno. Lo que se pone en el dial es otra cosa y la calcula
/// <see cref="CorreccionDoppler"/>. Confundir las dos deja un cuaderno lleno de frecuencias que
/// no existen y contactos que LoTW no casa.
/// </para>
/// </remarks>
public sealed record EnlaceDeSatelite(
    string Satelite,
    Transpondedor Transpondedor,
    Frecuencia SubidaNominal,
    Frecuencia BajadaNominal)
{
    /// <summary>El enlace tiene subida: se puede transmitir por el.</summary>
    public bool TieneSubida => !SubidaNominal.EsCero;

    /// <summary>El enlace tiene bajada: se puede escuchar.</summary>
    public bool TieneBajada => !BajadaNominal.EsCero;

    /// <summary>
    /// Enlace centrado en el transpondedor, que es por donde se empieza a buscar.
    /// </summary>
    /// <param name="transpondedor">Transpondedor del catalogo.</param>
    /// <returns>El enlace con las dos frecuencias centrales.</returns>
    /// <exception cref="ArgumentNullException">No se ha dado transpondedor.</exception>
    public static EnlaceDeSatelite Centrado(Transpondedor transpondedor)
    {
        ArgumentNullException.ThrowIfNull(transpondedor);
        return new EnlaceDeSatelite(
            transpondedor.Abreviatura,
            transpondedor,
            transpondedor.SubidaCentral ?? Frecuencia.Cero,
            transpondedor.BajadaCentral ?? Frecuencia.Cero);
    }

    /// <summary>
    /// Enlace que sale de elegir una frecuencia concreta dentro del margen de bajada.
    /// </summary>
    /// <param name="transpondedor">Transpondedor del catalogo.</param>
    /// <param name="bajada">Frecuencia de bajada elegida, dentro del margen.</param>
    /// <returns>El enlace con la subida que le corresponde a esa bajada.</returns>
    /// <remarks>
    /// <para>
    /// <b>Aqui es donde se decide si uno contesta en el sitio correcto.</b> En un transpondedor
    /// lineal, moverse por la banda de bajada obliga a moverse por la de subida, y en los
    /// <see cref="Catalogo.Transpondedor.Invertido">invertidos</see> se mueve <b>al reves</b>:
    /// bajar un kilohercio en la bajada es subir un kilohercio en la subida. La mayoria de los
    /// lineales de aficionado son invertidos, asi que equivocarse en el signo es la norma y no
    /// la excepcion.
    /// </para>
    /// <para>
    /// En un repetidor de FM no hay margen que recorrer: la subida es fija y se devuelve tal
    /// cual, se pida la bajada que se pida.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">No se ha dado transpondedor.</exception>
    public static EnlaceDeSatelite ConBajada(Transpondedor transpondedor, Frecuencia bajada)
    {
        ArgumentNullException.ThrowIfNull(transpondedor);

        var subida = SubidaPara(transpondedor, bajada);
        return new EnlaceDeSatelite(transpondedor.Abreviatura, transpondedor, subida, bajada);
    }

    /// <summary>Frecuencia de subida que corresponde a una bajada dada.</summary>
    /// <param name="transpondedor">Transpondedor del catalogo.</param>
    /// <param name="bajada">Frecuencia de bajada.</param>
    /// <returns>La subida que le toca, ya recortada al margen del transpondedor.</returns>
    /// <exception cref="ArgumentNullException">No se ha dado transpondedor.</exception>
    public static Frecuencia SubidaPara(Transpondedor transpondedor, Frecuencia bajada)
    {
        ArgumentNullException.ThrowIfNull(transpondedor);

        if (transpondedor.SubidaInicio is not { } subInicio)
        {
            return Frecuencia.Cero;
        }

        var subFin = transpondedor.SubidaFin ?? subInicio;

        // Sin margen de bajada —repetidor de FM o baliza— no hay nada que recorrer.
        if (transpondedor.BajadaInicio is not { } bajInicio
            || transpondedor.BajadaFin is not { } bajFin
            || bajFin.Megahercios <= bajInicio.Megahercios)
        {
            return subInicio;
        }

        var dentro = Math.Clamp(bajada.Megahercios, bajInicio.Megahercios, bajFin.Megahercios);
        var desplazamiento = transpondedor.Invertido
            ? bajFin.Megahercios - dentro
            : dentro - bajInicio.Megahercios;

        var ancho = subFin.Megahercios - subInicio.Megahercios;
        if (ancho <= 0)
        {
            return subInicio;
        }

        var bruto = subInicio.Megahercios + Math.Min(desplazamiento, ancho);
        return Frecuencia.DesdeMegahercios(bruto);
    }

    /// <summary>Las dos frecuencias ya corregidas de Doppler para un instante.</summary>
    /// <param name="vista">Lo que se ve del satelite en ese instante.</param>
    /// <returns>Donde hay que poner los diales.</returns>
    public SintoniaCorregida Sintonia(Orbital.VistaDesdeTierra vista) =>
        CorreccionDoppler.Corregir(SubidaNominal, BajadaNominal, vista);
}
