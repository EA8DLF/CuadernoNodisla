using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Audio.Captura;

namespace Nodisla.Cuaderno.Audio.Pruebas;

/// <summary>
/// El colchon y el ensamblador de bloques, que es donde se gana o se pierde el audio.
/// </summary>
/// <remarks>
/// Todo esto se prueba sin tarjeta de sonido: son las piezas que estan a proposito separadas de
/// Windows. Lo que se comprueba es lo unico que de verdad importa de la captura: que el flujo
/// sale entero, que cuando se pierde algo se cuenta, y que la hora de cada bloque sigue siendo
/// la buena despues de un hueco.
/// </remarks>
public class CapturaSinTarjetaPruebas
{
    private static readonly DateTimeOffset Arranque = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

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
    public void ElFlujoSaleEnteroYEnOrden()
    {
        var ensamblador = new EnsambladorDeBloques(48000, 100, Arranque);
        var bloques = new List<BloqueDeAudio>();
        var entrada = Rampa(1000);

        var puesto = 0;
        foreach (var cuantas in new[] { 7, 93, 300, 1, 599 })
        {
            ensamblador.Anadir(entrada.AsSpan(puesto, cuantas), bloques);
            puesto += cuantas;
        }

        bloques.Should().HaveCount(10);

        var salida = bloques.SelectMany(bloque => bloque.Muestras.ToArray()).ToArray();
        salida.Should().Equal(entrada);
    }

    [Fact]
    public void CadaBloqueVaFechadoPorSuPosicionEnElFlujo()
    {
        var ensamblador = new EnsambladorDeBloques(48000, 4800, Arranque);
        var bloques = new List<BloqueDeAudio>();

        ensamblador.Anadir(new float[4800 * 3], bloques);

        bloques.Should().HaveCount(3);
        bloques[0].InstanteUtc.Should().Be(Arranque);
        bloques[1].InstanteUtc.Should().Be(Arranque + TimeSpan.FromMilliseconds(100));
        bloques[2].InstanteUtc.Should().Be(Arranque + TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public void SaltarMuestrasCorreLaHoraParaQueLoSiguienteSigaEnSuVentana()
    {
        var ensamblador = new EnsambladorDeBloques(48000, 4800, Arranque);
        var bloques = new List<BloqueDeAudio>();

        ensamblador.Anadir(new float[4800], bloques);

        // Se pierde medio segundo de audio.
        ensamblador.Saltar(24000);
        ensamblador.Anadir(new float[4800], bloques);

        bloques.Should().HaveCount(2);
        bloques[1].InstanteUtc.Should().Be(Arranque + TimeSpan.FromMilliseconds(600));
    }

    [Fact]
    public void ElColchonDevuelveLoMismoQueSeMetio()
    {
        var colchon = new AmortiguadorCircular(16);
        var destino = new float[16];

        colchon.Escribir(Rampa(10)).Should().Be(0);
        colchon.Leer(destino.AsSpan(0, 10)).Should().Be(10);
        destino.AsSpan(0, 10).ToArray().Should().Equal(Rampa(10));

        // Y sigue valiendo cuando la escritura da la vuelta al final del colchon.
        colchon.Escribir(Rampa(12, 100)).Should().Be(0);
        colchon.Leer(destino.AsSpan(0, 12)).Should().Be(12);
        destino.AsSpan(0, 12).ToArray().Should().Equal(Rampa(12, 100));
        colchon.MuestrasPerdidas.Should().Be(0);
    }

    [Fact]
    public void CuandoElColchonSeLlenaSeCuentanLasMuestrasPerdidas()
    {
        var colchon = new AmortiguadorCircular(8);

        colchon.Escribir(Rampa(8)).Should().Be(0);

        // No cabe nada mas: para meter cuatro hay que tirar las cuatro mas viejas.
        colchon.Escribir(Rampa(4, 100)).Should().Be(4);
        colchon.MuestrasPerdidas.Should().Be(4);

        var destino = new float[8];
        colchon.Leer(destino).Should().Be(8);

        // Lo que queda es lo mas nuevo: se tira lo viejo para que el hueco quede justo donde el
        // lector va a leer, y asi la hora de lo que viene detras no se corre.
        destino.Should().Equal(4f, 5f, 6f, 7f, 100f, 101f, 102f, 103f);
    }

    [Fact]
    public void UnaTacadaMayorQueElColchonSeQuedaConLoMasNuevo()
    {
        var colchon = new AmortiguadorCircular(4);
        colchon.Escribir(Rampa(10)).Should().Be(6);

        var destino = new float[4];
        colchon.Leer(destino).Should().Be(4);
        destino.Should().Equal(6f, 7f, 8f, 9f);
    }
}
