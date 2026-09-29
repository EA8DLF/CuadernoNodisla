using Nodisla.Cuaderno.Concursos.Catalogo;
using Nodisla.Cuaderno.Concursos.Sesion;

namespace Nodisla.Cuaderno.Concursos.Calculo;

/// <summary>
/// Cuanto vale un contacto segun las reglas del concurso.
/// </summary>
/// <remarks>
/// Las reglas se miran <b>en el orden en que vienen</b> y gana la primera que case. Ese
/// detalle no es un capricho: en el CQ WW un contacto dentro de Norteamerica vale dos puntos
/// y uno del mismo continente vale uno, y el primero es un caso particular del segundo. Si
/// se evaluaran en otro orden, o se buscara «la mejor», saldrian marcadores distintos.
/// </remarks>
public static class CalculadoraDePuntos
{
    /// <summary>Puntos de un contacto.</summary>
    /// <param name="concurso">Reglas del concurso.</param>
    /// <param name="miEstacion">Datos de mi estacion.</param>
    /// <param name="apunte">Contacto que se valora.</param>
    /// <returns>Los puntos. Cero si el concurso no trae reglas de puntuacion.</returns>
    public static int De(ReglaDeConcurso concurso, DatosDeMiEstacion miEstacion, ApunteDeConcurso apunte)
    {
        ArgumentNullException.ThrowIfNull(concurso);
        ArgumentNullException.ThrowIfNull(miEstacion);
        ArgumentNullException.ThrowIfNull(apunte);

        var clase = ClaseDeModo.De(apunte.Modo);
        foreach (var regla in concurso.Puntuacion)
        {
            if (regla.Bandas.Count > 0 && !regla.Bandas.Contains(apunte.Banda)) continue;
            if (regla.Modos.Count > 0 && !regla.Modos.Any(m => ClaseDeModo.Equivale(m, clase))) continue;
            if (Casa(regla.Ambito, miEstacion, apunte)) return regla.Puntos;
        }
        return 0;
    }

    private static bool Casa(AmbitoDePuntos ambito, DatosDeMiEstacion mia, ApunteDeConcurso apunte) => ambito switch
    {
        AmbitoDePuntos.Cualquiera => true,
        AmbitoDePuntos.MismoPais => apunte.Dxcc is { } d && d == mia.Dxcc,
        AmbitoDePuntos.MismoContinente => MismoContinente(mia, apunte) && !MismoPais(mia, apunte),
        AmbitoDePuntos.MismoContinenteNa =>
            MismoContinente(mia, apunte) && !MismoPais(mia, apunte)
            && string.Equals(mia.Continente, "NA", StringComparison.OrdinalIgnoreCase),
        AmbitoDePuntos.OtroContinente => Conocidos(mia, apunte) && !MismoContinente(mia, apunte),
        AmbitoDePuntos.MismaZonaCq => apunte.ZonaCq is { } z && mia.ZonaCq is { } m && z == m,
        AmbitoDePuntos.MismaZonaItu => apunte.ZonaItu is { } z && mia.ZonaItu is { } m && z == m,
        _ => false,
    };

    private static bool MismoPais(DatosDeMiEstacion mia, ApunteDeConcurso apunte) =>
        apunte.Dxcc is { } d && d == mia.Dxcc;

    /// <summary>
    /// Se conocen los dos continentes. Sin ellos no se puede decir «otro continente»: se
    /// preferiria fallar a favor del operador, pero eso infla el marcador y engana.
    /// </summary>
    private static bool Conocidos(DatosDeMiEstacion mia, ApunteDeConcurso apunte) =>
        !string.IsNullOrWhiteSpace(mia.Continente) && !string.IsNullOrWhiteSpace(apunte.Continente);

    private static bool MismoContinente(DatosDeMiEstacion mia, ApunteDeConcurso apunte) =>
        Conocidos(mia, apunte)
        && string.Equals(mia.Continente, apunte.Continente, StringComparison.OrdinalIgnoreCase);
}
