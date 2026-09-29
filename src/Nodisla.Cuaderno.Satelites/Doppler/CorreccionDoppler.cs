using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Satelites.Orbital;

namespace Nodisla.Cuaderno.Satelites.Doppler;

/// <summary>
/// A donde hay que llevar el dial para oir al satelite y para que el satelite nos oiga.
/// </summary>
/// <param name="Bajada">Frecuencia a la que hay que escuchar.</param>
/// <param name="Subida">Frecuencia a la que hay que transmitir.</param>
/// <param name="DesplazamientoBajadaHz">Cuanto se ha corrido la bajada, en hercios.</param>
/// <param name="DesplazamientoSubidaHz">Cuanto se ha corrido la subida, en hercios.</param>
/// <param name="VelocidadRadialKmS">Velocidad de alejamiento con la que se ha calculado.</param>
public readonly record struct SintoniaCorregida(
    Frecuencia Bajada,
    Frecuencia Subida,
    double DesplazamientoBajadaHz,
    double DesplazamientoSubidaHz,
    double VelocidadRadialKmS)
{
    /// <summary>El satelite se acerca: la bajada sube de frecuencia y la subida hay que bajarla.</summary>
    public bool SeAcerca => VelocidadRadialKmS < 0;
}

/// <summary>
/// Correccion Doppler de las frecuencias de subida y de bajada.
/// </summary>
/// <remarks>
/// <para>
/// Es lo que hace la diferencia entre oirse y no oirse. En 70 cm un paso de orbita baja
/// arrastra la senal unos diez kilohercios de punta a punta: sin corregir, el satelite se pasa
/// de largo por el filtro de SSB en menos de un minuto.
/// </para>
/// <para>
/// <b>Los dos signos no son el mismo.</b> Lo que se recibe de un emisor que se mueve vale
/// <c>f·(1 − ṙ/c)</c>: para escuchar se aplica esa correccion a la frecuencia de bajada. Pero
/// para transmitir hay que deshacerla, no repetirla: el satelite tiene que recibir su
/// frecuencia nominal, asi que se transmite <c>f/(1 − ṙ/c)</c>. Acercandose, la bajada se oye
/// mas arriba y hay que transmitir mas abajo. Aplicar el mismo signo a las dos es el error
/// clasico, y deja fuera de banda al que llama.
/// </para>
/// <para>
/// No se usa la formula relativista. A ocho kilometros por segundo la correccion de segundo
/// orden vale tres partes en diez mil millones: 0,1 Hz en 435 MHz, muy por debajo de la
/// resolucion del dial de cualquier equipo.
/// </para>
/// </remarks>
public static class CorreccionDoppler
{
    /// <summary>Velocidad de la luz en el vacio, en kilometros por segundo.</summary>
    public const double VelocidadDeLaLuzKmS = 299792.458;

    /// <summary>Frecuencia a la que se recibe una emision del satelite.</summary>
    /// <param name="nominal">Frecuencia con la que transmite el satelite.</param>
    /// <param name="velocidadRadialKmS">Velocidad de alejamiento; negativa si se acerca.</param>
    /// <returns>Donde hay que poner el dial de recepcion.</returns>
    public static Frecuencia Bajada(Frecuencia nominal, double velocidadRadialKmS)
    {
        var factor = 1.0 - (velocidadRadialKmS / VelocidadDeLaLuzKmS);
        return Frecuencia.DesdeHercios((long)Math.Round(nominal.Hercios * factor));
    }

    /// <summary>Frecuencia a la que hay que transmitir para que el satelite reciba la suya.</summary>
    /// <param name="nominal">Frecuencia que el satelite espera recibir.</param>
    /// <param name="velocidadRadialKmS">Velocidad de alejamiento; negativa si se acerca.</param>
    /// <returns>Donde hay que poner el dial de transmision.</returns>
    public static Frecuencia Subida(Frecuencia nominal, double velocidadRadialKmS)
    {
        var factor = 1.0 - (velocidadRadialKmS / VelocidadDeLaLuzKmS);
        return Frecuencia.DesdeHercios((long)Math.Round(nominal.Hercios / factor));
    }

    /// <summary>Corrige las dos frecuencias de un enlace a la vez.</summary>
    /// <param name="subidaNominal">Frecuencia de subida nominal.</param>
    /// <param name="bajadaNominal">Frecuencia de bajada nominal.</param>
    /// <param name="vista">Lo que se ve del satelite en ese instante.</param>
    /// <returns>Las dos frecuencias corregidas y cuanto se han movido.</returns>
    public static SintoniaCorregida Corregir(
        Frecuencia subidaNominal,
        Frecuencia bajadaNominal,
        VistaDesdeTierra vista)
    {
        var bajada = Bajada(bajadaNominal, vista.VelocidadRadialKmS);
        var subida = Subida(subidaNominal, vista.VelocidadRadialKmS);

        return new SintoniaCorregida(
            bajada,
            subida,
            bajada.Hercios - bajadaNominal.Hercios,
            subida.Hercios - subidaNominal.Hercios,
            vista.VelocidadRadialKmS);
    }
}
