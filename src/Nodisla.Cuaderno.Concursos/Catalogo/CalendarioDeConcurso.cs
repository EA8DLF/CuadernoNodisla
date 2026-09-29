namespace Nodisla.Cuaderno.Concursos.Catalogo;

/// <summary>Una edicion concreta de un concurso.</summary>
/// <param name="Codigo">Identificador del concurso.</param>
/// <param name="InicioUtc">Cuando empieza.</param>
/// <param name="FinUtc">Cuando termina.</param>
public sealed record EdicionDeConcurso(string Codigo, DateTimeOffset InicioUtc, DateTimeOffset FinUtc)
{
    /// <summary>El instante cae dentro de la ventana del concurso.</summary>
    /// <param name="instanteUtc">Momento que se pregunta.</param>
    public bool Contiene(DateTimeOffset instanteUtc) => instanteUtc >= InicioUtc && instanteUtc < FinUtc;
}

/// <summary>
/// Traduce el «ultimo fin de semana completo de noviembre» a una fecha de verdad.
/// </summary>
/// <remarks>
/// Parece una tonteria y no lo es: «el ultimo fin de semana completo» no es «el ultimo
/// sabado». En un mes cuyo ultimo sabado sea dia 30, ese fin de semana se sale del mes y el
/// concurso es el anterior. Esa cuenta mal hecha manda al operador a la radio una semana
/// tarde, que es el peor error posible de un calendario de concursos.
/// </remarks>
public static class CalendarioDeConcurso
{
    /// <summary>Calcula la edicion de un ano concreto.</summary>
    /// <param name="concurso">Concurso con su fecha de celebracion.</param>
    /// <param name="anio">Ano de la edicion.</param>
    /// <returns>La edicion, o nulo si el recurso no fija la fecha de ese concurso.</returns>
    public static EdicionDeConcurso? Edicion(ReglaDeConcurso concurso, int anio)
    {
        ArgumentNullException.ThrowIfNull(concurso);
        if (concurso.Celebracion is not { } cuando || cuando.EsDesconocida) return null;

        var dia = DiaDelMes(anio, cuando.Mes, cuando.Dia, cuando.Ordinal, cuando.SemanaCompleta);
        var inicio = new DateTimeOffset(
            dia.Year, dia.Month, dia.Day, cuando.HoraInicioUtc.Hour, cuando.HoraInicioUtc.Minute, 0, TimeSpan.Zero);
        return new EdicionDeConcurso(concurso.Codigo, inicio, inicio + cuando.Duracion);
    }

    /// <summary>La siguiente edicion que empieza despues del instante dado.</summary>
    /// <param name="concurso">Concurso con su fecha de celebracion.</param>
    /// <param name="desdeUtc">A partir de cuando se busca.</param>
    /// <returns>La proxima edicion, o nulo si el recurso no fija la fecha.</returns>
    public static EdicionDeConcurso? Proxima(ReglaDeConcurso concurso, DateTimeOffset desdeUtc)
    {
        ArgumentNullException.ThrowIfNull(concurso);
        var deEsteAnio = Edicion(concurso, desdeUtc.Year);
        if (deEsteAnio is null) return null;
        // Si ya termino la de este ano, la siguiente es la del que viene.
        return deEsteAnio.FinUtc > desdeUtc ? deEsteAnio : Edicion(concurso, desdeUtc.Year + 1);
    }

    /// <summary>Concursos del catalogo que estan en marcha en ese instante.</summary>
    /// <param name="catalogo">Catalogo a mirar.</param>
    /// <param name="instanteUtc">Momento que se pregunta.</param>
    /// <returns>Las ediciones vivas, de la que antes empezo a la que despues.</returns>
    public static IReadOnlyList<EdicionDeConcurso> EnMarcha(CatalogoDeConcursos catalogo, DateTimeOffset instanteUtc)
    {
        ArgumentNullException.ThrowIfNull(catalogo);
        var vivas = new List<EdicionDeConcurso>();
        foreach (var concurso in catalogo.ConReglas)
        {
            // Se miran dos anos porque un concurso que empieza el 31 de diciembre termina en enero.
            foreach (var anio in new[] { instanteUtc.Year - 1, instanteUtc.Year })
            {
                if (Edicion(concurso, anio) is { } edicion && edicion.Contiene(instanteUtc)) vivas.Add(edicion);
            }
        }
        return vivas.OrderBy(e => e.InicioUtc).ToArray();
    }

    /// <summary>El n-esimo dia de la semana de un mes, contando o no las semanas que se salen.</summary>
    /// <param name="anio">Ano.</param>
    /// <param name="mes">Mes, de 1 a 12.</param>
    /// <param name="dia">Dia de la semana buscado.</param>
    /// <param name="ordinal">Cual: de 1 a 4, o 5 para el ultimo.</param>
    /// <param name="semanaCompleta">
    /// La semana que empieza ese dia tiene que caber entera en el mes. Es lo que significa
    /// «fin de semana completo» y lo que separa al CQ WW de un calendario mal hecho.
    /// </param>
    /// <returns>La fecha resultante.</returns>
    public static DateOnly DiaDelMes(int anio, int mes, DayOfWeek dia, int ordinal, bool semanaCompleta)
    {
        var primero = new DateOnly(anio, mes, 1);
        var diasHasta = ((int)dia - (int)primero.DayOfWeek + 7) % 7;
        var candidatos = new List<DateOnly>();
        for (var fecha = primero.AddDays(diasHasta); fecha.Month == mes; fecha = fecha.AddDays(7))
        {
            // El fin de semana es el sabado y el domingo: hace falta que quepa tambien el dia siguiente.
            if (semanaCompleta && fecha.AddDays(1).Month != mes) continue;
            candidatos.Add(fecha);
        }

        if (candidatos.Count == 0) return primero.AddDays(diasHasta);
        return ordinal >= 5 || ordinal > candidatos.Count
            ? candidatos[^1]
            : candidatos[Math.Max(ordinal, 1) - 1];
    }
}
