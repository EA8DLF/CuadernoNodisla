using FluentAssertions;
using Nodisla.Cuaderno.Audio.Cascada;

namespace Nodisla.Cuaderno.Audio.Pruebas;

/// <summary>
/// El troceado con solape, comprobado con una rampa.
/// </summary>
/// <remarks>
/// Se usa una rampa —0, 1, 2, 3...— porque cada muestra lleva escrito su propio numero: si el
/// troceador se salta una o la repite, se ve a simple vista. Perder muestras aqui seria perder
/// decodificaciones sin que nadie se entere.
/// </remarks>
public class TroceadorConSolapePruebas
{
    private static float[] Rampa(int cuantas, int desde = 0)
    {
        var datos = new float[cuantas];
        for (var i = 0; i < cuantas; i++)
        {
            datos[i] = desde + i;
        }

        return datos;
    }

    [Fact]
    public void ElSolapeNiPierdeNiDuplicaMuestras()
    {
        var troceador = new TroceadorConSolape(tamano: 8, salto: 4);
        var inicios = new List<long>();
        var contenidos = new List<float[]>();

        // Se mete en trozos de tamano raro a proposito: asi se comprueba que da igual como
        // llegue el audio, que lo que sale es siempre lo mismo.
        var entrada = Rampa(40);
        var puesto = 0;
        foreach (var cuantas in new[] { 3, 1, 10, 7, 19 })
        {
            troceador.Anadir(entrada.AsSpan(puesto, cuantas), (inicio, tramo) =>
            {
                inicios.Add(inicio);
                contenidos.Add(tramo.ToArray());
            });

            puesto += cuantas;
        }

        troceador.MuestrasRecibidas.Should().Be(40);

        // De 40 muestras, con tramos de 8 y saltos de 4, salen tramos en 0, 4, 8... 32.
        inicios.Should().Equal(0, 4, 8, 12, 16, 20, 24, 28, 32);

        for (var i = 0; i < contenidos.Count; i++)
        {
            contenidos[i].Should().Equal(Rampa(8, (int)inicios[i]));
        }
    }

    [Fact]
    public void CadaTramoEmpiezaUnSaltoDespuesDelAnterior()
    {
        var troceador = new TroceadorConSolape(tamano: 16, salto: 16);
        var inicios = new List<long>();

        troceador.Anadir(Rampa(64), (inicio, _) => inicios.Add(inicio));

        // Sin solape los tramos van pegados y no se repite ni una muestra.
        inicios.Should().Equal(0, 16, 32, 48);
    }

    [Fact]
    public void ReiniciarOlvidaLoQueHabiaAMedias()
    {
        var troceador = new TroceadorConSolape(tamano: 8, salto: 8);
        troceador.Anadir(Rampa(5), (_, _) => { });
        troceador.Reiniciar();

        var contenidos = new List<float[]>();
        troceador.Anadir(Rampa(8, 100), (_, tramo) => contenidos.Add(tramo.ToArray()));

        troceador.MuestrasRecibidas.Should().Be(8);
        contenidos.Should().ContainSingle();
        contenidos[0].Should().Equal(Rampa(8, 100));
    }

    [Fact]
    public void ElSaltoNoPuedePasarDelTamano()
    {
        var fallo = () => new TroceadorConSolape(tamano: 8, salto: 9);
        fallo.Should().Throw<ArgumentOutOfRangeException>();
    }
}
