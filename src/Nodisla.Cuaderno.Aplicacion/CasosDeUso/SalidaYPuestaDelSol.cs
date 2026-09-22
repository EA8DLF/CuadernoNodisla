using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Aplicacion.CasosDeUso;

/// <summary>
/// Las horas de orto y ocaso en un punto, que es lo que se ensena junto a los indices solares.
/// </summary>
/// <remarks>
/// <para>
/// Se resuelve buscando el cambio de signo de la altura del Sol a lo largo del dia, con la
/// misma cuenta que usa el paso gris. No hace falta mas precision: un minuto arriba o abajo no
/// cambia ninguna decision de operacion, y en cambio una formula cerrada se equivoca de hora
/// entera cerca de los polos.
/// </para>
/// <para>
/// <b>Hay dias sin orto y sin ocaso.</b> Por encima del circulo polar el Sol no sale en
/// invierno ni se pone en verano, y entonces no hay hora que dar: se devuelve nulo, y quien lo
/// ensene tiene que decir «no sale» en vez de inventarse las doce de la noche.
/// </para>
/// </remarks>
public static class SalidaYPuestaDelSol
{
    /// <summary>
    /// Altura del centro del Sol a la que se considera que sale o se pone.
    /// </summary>
    /// <remarks>
    /// No es cero: el disco solar mide medio grado y la atmosfera lo levanta otro tanto, asi
    /// que el borde superior asoma cuando el centro esta todavia 50 minutos de arco por
    /// debajo del horizonte. Es el mismo valor que usan los almanaques.
    /// </remarks>
    public const double AlturaDelHorizonte = -0.833;

    /// <summary>Paso con el que se recorre el dia buscando el cambio de signo.</summary>
    private static readonly TimeSpan Paso = TimeSpan.FromMinutes(2);

    /// <summary>Afinado final, en segundos, por biseccion.</summary>
    private const int VueltasDeAfinado = 8;

    /// <summary>
    /// Calcula a que hora sale y se pone el Sol en un punto, para el dia del instante dado.
    /// </summary>
    /// <param name="donde">Punto de la Tierra.</param>
    /// <param name="instanteUtc">Cualquier instante del dia que interesa.</param>
    /// <returns>
    /// Las dos horas en UTC. Cualquiera de las dos es nula si ese dia el Sol no cruza el
    /// horizonte en ese punto: noche polar o sol de medianoche.
    /// </returns>
    public static (DateTimeOffset? Orto, DateTimeOffset? Ocaso) Calcular(
        Coordenada donde,
        DateTimeOffset instanteUtc)
    {
        var arranque = new DateTimeOffset(instanteUtc.UtcDateTime.Date, TimeSpan.Zero);

        DateTimeOffset? orto = null;
        DateTimeOffset? ocaso = null;

        var anterior = arranque;
        var alturaAnterior = PasoGris.AlturaDelSol(donde, anterior) - AlturaDelHorizonte;

        for (var t = arranque + Paso; t <= arranque.AddDays(1); t += Paso)
        {
            var altura = PasoGris.AlturaDelSol(donde, t) - AlturaDelHorizonte;

            if (alturaAnterior <= 0 && altura > 0)
            {
                orto ??= Afinar(donde, anterior, t);
            }
            else if (alturaAnterior >= 0 && altura < 0)
            {
                ocaso ??= Afinar(donde, anterior, t);
            }

            anterior = t;
            alturaAnterior = altura;
        }

        return (orto, ocaso);
    }

    /// <summary>Parte el intervalo por la mitad hasta dar con el cruce al segundo.</summary>
    private static DateTimeOffset Afinar(Coordenada donde, DateTimeOffset antes, DateTimeOffset despues)
    {
        var signoAntes = Math.Sign(PasoGris.AlturaDelSol(donde, antes) - AlturaDelHorizonte);

        for (var i = 0; i < VueltasDeAfinado; i++)
        {
            var medio = antes + ((despues - antes) / 2);
            var signoMedio = Math.Sign(PasoGris.AlturaDelSol(donde, medio) - AlturaDelHorizonte);

            if (signoMedio == signoAntes)
            {
                antes = medio;
            }
            else
            {
                despues = medio;
            }
        }

        return antes + ((despues - antes) / 2);
    }
}
