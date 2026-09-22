using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Audio.Ventanas;

namespace Nodisla.Cuaderno.Audio.Pruebas;

/// <summary>
/// El reparto del audio en ventanas de FT8 y FT4.
/// </summary>
/// <remarks>
/// Las ventanas van alineadas al reloj universal, iguales para todo el mundo. Lo que se prueba
/// aqui es que cada muestra acaba en la ventana que le toca por su hora, y no en la que tocaria
/// por el orden en que llego: si esto se tuerce, el decodificador mira quince segundos que no
/// son y no saca nada.
/// </remarks>
public class TroceadorDeVentanasPruebas
{
    private const int Frecuencia = 8000;

    private static BloqueDeAudio Bloque(DateTimeOffset cuando, int muestras, float valor = 1f)
    {
        var datos = new float[muestras];
        Array.Fill(datos, valor);
        return new BloqueDeAudio(datos, Frecuencia, cuando);
    }

    [Fact]
    public void LasVentanasEmpiezanEnLosCuartosDeMinuto()
    {
        var instante = new DateTimeOffset(2026, 9, 22, 12, 0, 22, 300, TimeSpan.Zero);
        TroceadorDeVentanas.InicioDeLaVentana(instante, TimeSpan.FromSeconds(15))
            .Should().Be(new DateTimeOffset(2026, 9, 22, 12, 0, 15, TimeSpan.Zero));
    }

    [Fact]
    public void UnaVentanaSeEntregaCuandoLlegaLaMuestraDeLaSiguiente()
    {
        var troceador = new TroceadorDeVentanas(TimeSpan.FromSeconds(15), Frecuencia);
        troceador.MuestrasPorVentana.Should().Be(15 * Frecuencia);

        // Se empieza justo en el borde y se mete la ventana entera de una vez.
        var inicio = new DateTimeOffset(2026, 9, 22, 12, 0, 15, TimeSpan.Zero);
        var terminadas = troceador.Anadir(Bloque(inicio, 15 * Frecuencia));
        terminadas.Should().BeEmpty();

        // La primera muestra de la siguiente ventana es la que cierra la anterior.
        terminadas = troceador.Anadir(Bloque(inicio.AddSeconds(15), 100));

        terminadas.Should().ContainSingle();
        terminadas[0].InicioUtc.Should().Be(inicio);
        terminadas[0].EstaCompleta.Should().BeTrue();
        terminadas[0].Muestras.Length.Should().Be(15 * Frecuencia);
    }

    [Fact]
    public void UnBloqueQueCruzaElBordeSeReparteEntreLasDosVentanas()
    {
        var troceador = new TroceadorDeVentanas(TimeSpan.FromSeconds(15), Frecuencia);
        var inicio = new DateTimeOffset(2026, 9, 22, 12, 0, 15, TimeSpan.Zero);

        // Se llena hasta un cuarto de segundo antes del borde.
        troceador.Anadir(Bloque(inicio, (15 * Frecuencia) - 2000, valor: 1f));

        // Y ahora un bloque de medio segundo que cae a caballo: 2000 muestras de la ventana
        // vieja y 2000 de la nueva.
        var terminadas = troceador.Anadir(
            Bloque(inicio.AddSeconds(15) - TimeSpan.FromMilliseconds(250), 4000, valor: 2f));

        terminadas.Should().ContainSingle();
        terminadas[0].EstaCompleta.Should().BeTrue();

        var muestras = terminadas[0].Muestras.Span;
        muestras[0].Should().Be(1f);
        muestras[(15 * Frecuencia) - 2001].Should().Be(1f);
        muestras[(15 * Frecuencia) - 2000].Should().Be(2f);
        muestras[^1].Should().Be(2f);

        // Y lo que se quedo de la ventana nueva sigue dentro, esperando.
        troceador.VentanaEnCurso.Should().Be(inicio.AddSeconds(15));
    }

    [Fact]
    public void LoQueNoLlegaSeCuentaComoFaltante()
    {
        var troceador = new TroceadorDeVentanas(TimeSpan.FromSeconds(15), Frecuencia);
        var inicio = new DateTimeOffset(2026, 9, 22, 12, 0, 15, TimeSpan.Zero);

        troceador.Anadir(Bloque(inicio, 1000));

        // Un salto: lo siguiente llega cinco segundos despues y solo dura cinco segundos, asi
        // que la ventana se queda con dos agujeros.
        troceador.Anadir(Bloque(inicio.AddSeconds(5), 5 * Frecuencia));
        var terminadas = troceador.Anadir(Bloque(inicio.AddSeconds(15), 10));

        terminadas.Should().ContainSingle();
        terminadas[0].EstaCompleta.Should().BeFalse();

        // De quince segundos solo llegaron mil muestras y cinco segundos.
        terminadas[0].MuestrasFaltantes.Should().Be((15 * Frecuencia) - 1000 - (5 * Frecuencia));
    }

    [Fact]
    public void LasVentanasVaciasNoSeEntregan()
    {
        var troceador = new TroceadorDeVentanas(TimeSpan.FromSeconds(15), Frecuencia);
        var inicio = new DateTimeOffset(2026, 9, 22, 12, 0, 15, TimeSpan.Zero);

        troceador.Anadir(Bloque(inicio, 100));

        // Se pierde un minuto entero de audio: no tiene sentido entregar cuatro ventanas vacias.
        var terminadas = troceador.Anadir(Bloque(inicio.AddSeconds(60), 100));

        terminadas.Should().ContainSingle();
        terminadas[0].InicioUtc.Should().Be(inicio);
        troceador.VentanaEnCurso.Should().Be(inicio.AddSeconds(60));
    }

    [Fact]
    public void CerrarEntregaLoQueHubieraAMedias()
    {
        var troceador = new TroceadorDeVentanas(TimeSpan.FromSeconds(7.5), Frecuencia);
        var inicio = new DateTimeOffset(2026, 9, 22, 12, 0, 7, 500, TimeSpan.Zero);

        troceador.Anadir(Bloque(inicio, 500));
        var aMedias = troceador.Cerrar();

        aMedias.Should().NotBeNull();
        aMedias!.InicioUtc.Should().Be(inicio);
        aMedias.EstaCompleta.Should().BeFalse();
        troceador.Cerrar().Should().BeNull();
    }

    [Fact]
    public void UnBloqueAOtraFrecuenciaNoSeAdmite()
    {
        var troceador = new TroceadorDeVentanas(TimeSpan.FromSeconds(15), Frecuencia);
        var bloque = new BloqueDeAudio(new float[10], 48000, DateTimeOffset.UtcNow);

        var fallo = () => troceador.Anadir(bloque);
        fallo.Should().Throw<ArgumentException>();
    }
}
