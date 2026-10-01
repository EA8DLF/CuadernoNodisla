using FluentAssertions;
using Nodisla.Cuaderno.Concursos.Telegrafia;

namespace Nodisla.Cuaderno.Concursos.Pruebas;

/// <summary>El entrenador genera texto; no toca la radio ni sabe que existe.</summary>
public sealed class EntrenadorPruebas
{
    private static EntrenadorDeTelegrafia Con(EjercicioDeTelegrafia ejercicio, int semilla = 42) =>
        new(new OpcionesDeEntrenamiento { Ejercicio = ejercicio, Semilla = semilla, Grupos = 6 });

    [Fact]
    public void ConLaMismaSemillaSaleLaMismaTanda()
    {
        // Sirve para repetir un ejercicio que se ha fallado, y para que las pruebas no bailen.
        Con(EjercicioDeTelegrafia.GruposAlAzar).Siguiente().Texto
            .Should().Be(Con(EjercicioDeTelegrafia.GruposAlAzar).Siguiente().Texto);
    }

    [Fact]
    public void LosGruposSonDeCincoYVienenSeparados()
    {
        var tanda = Con(EjercicioDeTelegrafia.Letras).Siguiente();

        tanda.Grupos.Should().HaveCount(6);
        tanda.Grupos.Should().OnlyContain(g => g.Length == 5);
        tanda.Texto.Should().MatchRegex("^[A-Z ]+$");
    }

    [Fact]
    public void LosNumerosSonNumerosYLasLetrasLetras()
    {
        Con(EjercicioDeTelegrafia.Numeros).Siguiente().Texto.Should().MatchRegex("^[0-9 ]+$");
        Con(EjercicioDeTelegrafia.Letras).Siguiente().Texto.Should().MatchRegex("^[A-Z ]+$");
    }

    [Fact]
    public void LosIndicativosTienenFormaDeIndicativo()
    {
        var tanda = Con(EjercicioDeTelegrafia.Indicativos).Siguiente();

        tanda.Grupos.Should().OnlyContain(g => g.Any(char.IsAsciiDigit));
        tanda.Grupos.Should().OnlyContain(g => g.Any(char.IsAsciiLetter));
        // Un indicativo NO tiene por que empezar por letra: 9A3C (Croacia), 4X4ABC (Israel) o
        // 3DA0RU son indicativos de verdad, y el entrenador tiene que mandarlos igual que los
        // demas. Lo que sí se comprueba es que sea alfanumerico y de longitud creible.
        tanda.Grupos.Should().OnlyContain(g => char.IsAsciiLetterOrDigit(g[0]));
        tanda.Grupos.Should().OnlyContain(g => g.All(char.IsAsciiLetterOrDigit));
        tanda.Grupos.Should().OnlyContain(g => g.Length >= 3 && g.Length <= 8);
    }

    [Fact]
    public void ElIntercambioDeConcursoTraeInformeYNumero()
    {
        var tanda = Con(EjercicioDeTelegrafia.Concurso).Siguiente();

        tanda.Texto.Should().Contain("599");
        tanda.Grupos.Should().HaveCount(6 * 3, "indicativo, informe y numero por cada uno");
    }

    [Fact]
    public void TodoLoQueGeneraSeSabeManipular()
    {
        foreach (var ejercicio in Enum.GetValues<EjercicioDeTelegrafia>())
        {
            var tanda = Con(ejercicio).Siguiente();
            foreach (var letra in tanda.Texto.Where(c => c != ' '))
            {
                AlfabetoMorse.TryCodigo(letra, out _).Should()
                    .BeTrue($"el ejercicio {ejercicio} ha generado «{letra}», que no se sabe manipular");
            }
        }
    }

    [Fact]
    public void KochEmpiezaConDosCaracteresYVaAnadiendoDeUnoEnUno()
    {
        var entrenador = Con(EjercicioDeTelegrafia.Koch);
        var primera = entrenador.Siguiente();

        primera.Texto.Where(c => c != ' ').Distinct().Should().BeSubsetOf(new[] { 'K', 'M' });

        entrenador.Corregir(primera, primera.Texto);
        entrenador.CaracteresDeKoch.Should().Be(3);
        entrenador.Siguiente().Texto.Where(c => c != ' ').Distinct()
            .Should().BeSubsetOf(new[] { 'K', 'M', 'R' });
    }

    [Fact]
    public void EnKochLasLetrasVanRapidasYLoQueSeEstiraSonLosHuecos()
    {
        var entrenador = new EntrenadorDeTelegrafia(new OpcionesDeEntrenamiento
        {
            Ejercicio = EjercicioDeTelegrafia.Koch,
            PpmInicial = 8,
            PpmMaximas = 25,
            Semilla = 1,
        });
        var tanda = entrenador.Siguiente();

        tanda.Ppm.Should().Be(25, "en Koch los caracteres van siempre a velocidad final");
        tanda.PpmEfectivas.Should().Be(8);
        new Manipulador(tanda.Ppm, tanda.PpmEfectivas).EsFarnsworth.Should().BeTrue();
    }

    [Fact]
    public void LaVelocidadSubeSoloSiSeAciertaLoSuficiente()
    {
        var entrenador = new EntrenadorDeTelegrafia(new OpcionesDeEntrenamiento
        {
            Ejercicio = EjercicioDeTelegrafia.Letras,
            PpmInicial = 15,
            PpmMaximas = 25,
            PasoDePpm = 2,
            Semilla = 7,
            Grupos = 4,
        });
        var tanda = entrenador.Siguiente();

        var mal = entrenador.Corregir(tanda, "XXXXX XXXXX XXXXX XXXXX");
        mal.Sube.Should().BeFalse();
        mal.PpmSiguiente.Should().Be(15);

        var bien = entrenador.Corregir(tanda, tanda.Texto);
        bien.Sube.Should().BeTrue();
        bien.Acierto.Should().Be(1.0);
        bien.PpmSiguiente.Should().Be(17);
    }

    [Fact]
    public void LaVelocidadNoPasaDelTecho()
    {
        var entrenador = new EntrenadorDeTelegrafia(new OpcionesDeEntrenamiento
        {
            Ejercicio = EjercicioDeTelegrafia.Letras,
            PpmInicial = 24,
            PpmMaximas = 25,
            PasoDePpm = 5,
            Semilla = 3,
        });
        var tanda = entrenador.Siguiente();

        entrenador.Corregir(tanda, tanda.Texto).PpmSiguiente.Should().Be(25);
        entrenador.Corregir(tanda, tanda.Texto).PpmSiguiente.Should().Be(25);
    }

    [Fact]
    public void CorregirNoDistingueMayusculasNiEspacios()
    {
        var entrenador = Con(EjercicioDeTelegrafia.Letras);
        var tanda = entrenador.Siguiente();

        var correccion = entrenador.Corregir(tanda, tanda.Texto.Replace(" ", string.Empty).ToLowerInvariant());
        correccion.Acierto.Should().Be(1.0);
    }

    [Fact]
    public void CopiarDeMenosNoInventaAciertos()
    {
        var entrenador = Con(EjercicioDeTelegrafia.Letras);
        var tanda = entrenador.Siguiente();

        var correccion = entrenador.Corregir(tanda, tanda.Grupos[0]);
        correccion.Aciertos.Should().Be(5);
        correccion.Total.Should().Be(30);
        correccion.Sube.Should().BeFalse();
    }
}
