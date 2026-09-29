using System.Globalization;
using FluentAssertions;
using Nodisla.Cuaderno.Satelites.Orbital;

namespace Nodisla.Cuaderno.Satelites.Pruebas;

/// <summary>
/// Contrasta el propagador contra los vectores oficiales de verificacion de SGP4.
/// </summary>
/// <remarks>
/// Es la unica prueba que convierte «predice bien» en un numero. Los ficheros
/// <c>SGP4-VER.TLE</c> y <c>tcppver.out</c> son la bateria de verificacion publicada con
/// «Revisiting Spacetrack Report #3» (Vallado, Crawford, Hujsak y Kelso, AIAA 2006): el
/// primero trae los elementos y el segundo la posicion y la velocidad que tiene que salir,
/// calculadas con la implementacion de referencia. Si nuestro codigo se separa de ahi, es
/// nuestro codigo el que esta mal.
/// <para>
/// Solo se comprueban los casos de orbita baja. Los de espacio profundo necesitan SDP4, que
/// este modulo no implementa y rechaza explicitamente.
/// </para>
/// </remarks>
public sealed class VerificacionOficialSgp4(Xunit.Abstractions.ITestOutputHelper salida)
{
    /// <summary>Tolerancia de posicion, en kilometros.</summary>
    /// <remarks>
    /// Un metro. El objetivo no es «parecerse»: es reproducir el algoritmo. Lo que quede por
    /// debajo de esto es redondeo de coma flotante, no modelo.
    /// </remarks>
    private const double ToleranciaPosicionKm = 0.001;

    /// <summary>Tolerancia de velocidad, en kilometros por segundo.</summary>
    private const double ToleranciaVelocidadKmS = 0.000001;

    [Fact]
    public void ReproduceLosVectoresOficialesDeOrbitaBaja()
    {
        var casos = CasosDeVerificacion.Cargar();
        casos.Should().NotBeEmpty("los ficheros de verificación tienen que estar junto a las pruebas");

        var comprobados = 0;
        var peorPosicion = 0.0;
        var peorVelocidad = 0.0;
        var fallos = new List<string>();

        foreach (var caso in casos)
        {
            var propagador = new Sgp4(caso.Elementos);

            foreach (var punto in caso.Puntos)
            {
                if (!propagador.TryPropagarMinutos(punto.Minutos, out var r, out var v, out _))
                {
                    continue;
                }

                var errorPosicion = (r - punto.Posicion).Modulo;
                var errorVelocidad = (v - punto.Velocidad).Modulo;
                peorPosicion = Math.Max(peorPosicion, errorPosicion);
                peorVelocidad = Math.Max(peorVelocidad, errorVelocidad);
                comprobados++;

                if (errorPosicion > ToleranciaPosicionKm || errorVelocidad > ToleranciaVelocidadKmS)
                {
                    fallos.Add(
                        $"{caso.Elementos.NumeroCatalogo} a {punto.Minutos:F2} min: "
                        + $"{errorPosicion * 1000:F3} m y {errorVelocidad * 1000:F6} m/s");
                }
            }
        }

        // Se deja escrito el peor caso: es el numero que se enseña cuando alguien pregunta
        // cuanto se aparta la predicción de la referencia.
        salida.WriteLine(
            $"{casos.Count} satélites de órbita baja, {comprobados} puntos comprobados. "
            + $"Peor desviación: {peorPosicion * 1_000_000:F3} mm y {peorVelocidad * 1_000_000:F3} mm/s.");

        comprobados.Should().BeGreaterThan(50, "hacen falta bastantes puntos para que esto signifique algo");
        fallos.Should().BeEmpty();
        peorPosicion.Should().BeLessThan(ToleranciaPosicionKm);
        peorVelocidad.Should().BeLessThan(ToleranciaVelocidadKmS);
    }

    [Fact]
    public void RechazaDeFormaExplicitaLasOrbitasDeEspacioProfundo()
    {
        // AO-10 y compania: periodo largo, hace falta SDP4. Es mejor negarse que inventar.
        var elementos = LectorDeElementos.LeerPar(
            "OBJETO EN ORBITA ALTA",
            "1 04632U 70093B   04031.91070959 -.00000084  00000-0  10000-3 0  9955",
            "2 04632  11.4628 273.1101 1450506 207.6000 143.9350  1.20231981148599");

        elementos.EsEspacioProfundo.Should().BeTrue();
        var accion = () => new Sgp4(elementos);
        accion.Should().Throw<NotSupportedException>().WithMessage("*espacio profundo*");
    }
}

/// <summary>Lee la bateria oficial de verificacion que acompana a las pruebas.</summary>
internal static class CasosDeVerificacion
{
    internal sealed record Punto(double Minutos, Vector3 Posicion, Vector3 Velocidad);

    internal sealed record Caso(ElementosOrbitales Elementos, IReadOnlyList<Punto> Puntos);

    /// <summary>Carga los casos de orbita baja con sus vectores esperados.</summary>
    internal static IReadOnlyList<Caso> Cargar()
    {
        var carpeta = Path.Combine(AppContext.BaseDirectory, "Datos");
        var elementos = LectorDeElementos.Leer(File.ReadAllText(Path.Combine(carpeta, "SGP4-VER.TLE")))
            .Where(e => !e.EsEspacioProfundo)
            .ToDictionary(e => e.NumeroCatalogo);

        var esperados = LeerSalidaDeReferencia(Path.Combine(carpeta, "tcppver.out"));

        return elementos.Values
            .Where(e => esperados.ContainsKey(e.NumeroCatalogo))
            .Select(e => new Caso(e, esperados[e.NumeroCatalogo]))
            .ToList();
    }

    /// <summary>
    /// Lee <c>tcppver.out</c>: una cabecera «numero xx» por satelite y despues una linea por
    /// instante con los minutos desde la epoca, la posicion y la velocidad.
    /// </summary>
    private static Dictionary<int, List<Punto>> LeerSalidaDeReferencia(string ruta)
    {
        var resultado = new Dictionary<int, List<Punto>>();
        List<Punto>? actual = null;

        foreach (var cruda in File.ReadLines(ruta))
        {
            var linea = cruda.Trim();
            if (linea.Length == 0)
            {
                continue;
            }

            var campos = linea.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (campos.Length == 2 && campos[1] == "xx"
                && int.TryParse(campos[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var catalogo))
            {
                actual = [];
                resultado[catalogo] = actual;
                continue;
            }

            if (actual is null || campos.Length < 7)
            {
                continue;
            }

            var numeros = new double[7];
            var todos = true;
            for (var i = 0; i < 7; i++)
            {
                if (!double.TryParse(campos[i], NumberStyles.Float, CultureInfo.InvariantCulture, out numeros[i]))
                {
                    todos = false;
                    break;
                }
            }

            if (todos)
            {
                actual.Add(new Punto(
                    numeros[0],
                    new Vector3(numeros[1], numeros[2], numeros[3]),
                    new Vector3(numeros[4], numeros[5], numeros[6])));
            }
        }

        return resultado;
    }
}
