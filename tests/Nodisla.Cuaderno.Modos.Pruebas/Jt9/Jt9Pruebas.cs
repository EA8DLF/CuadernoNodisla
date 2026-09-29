using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Modos.Banco;
using Nodisla.Cuaderno.Modos.Jt9;

namespace Nodisla.Cuaderno.Modos.Pruebas.Jt9;

/// <summary>Pruebas de las tablas, el codificador y la cadena completa de JT9.</summary>
public class Jt9Pruebas
{
    [Fact]
    public void ElSincronismoTiene16SimbolosYLosDatos69()
    {
        TablasJt9.PosicionesDeSincronismo.Should().HaveCount(16).And.OnlyHaveUniqueItems();
        TablasJt9.PosicionesDeDatos.Should().HaveCount(69);
        TablasJt9.PosicionesDeSincronismo.Concat(TablasJt9.PosicionesDeDatos).Should().OnlyHaveUniqueItems().And.HaveCount(85);
        TablasJt9.Sincronismo.Count(b => b == 1).Should().Be(16);
    }

    [Fact]
    public void ElEntrelazadoEsUnaPermutacionDe206()
    {
        TablasJt9.PosicionDeCadaBit.Should().HaveCount(206).And.OnlyHaveUniqueItems();
        TablasJt9.PosicionDeCadaBit.Should().OnlyContain(p => p >= 0 && p < 206);
        // El primer bit cae en la posicion 0 y el segundo en la 128 (inversion de 00000001).
        TablasJt9.PosicionDeCadaBit[0].Should().Be(0);
        TablasJt9.PosicionDeCadaBit[1].Should().Be(128);
    }

    [Fact]
    public void ElCodigoDeGrayDeTresBitsCambiaUnBitEntreVecinos()
    {
        for (var s = 1; s < 8; s++)
            System.Numerics.BitOperations.PopCount((uint)(TablasJt9.Gray[s] ^ TablasJt9.Gray[s - 1])).Should().Be(1);
        for (var s = 0; s < 8; s++) TablasJt9.GrayInverso[TablasJt9.Gray[s]].Should().Be((byte)s);
    }

    [Fact]
    public void LosTonosLlevanElSincronismoEnElTonoCeroYLosDatosDel1Al8()
    {
        var codificador = new CodificadorJt9();
        codificador.TryCodificar("CQ EA8DLF IL18", out var tonos, out var motivo).Should().BeTrue(motivo);
        tonos.Should().HaveCount(85);
        foreach (var p in TablasJt9.PosicionesDeSincronismo) tonos[p].Should().Be(0);
        foreach (var p in TablasJt9.PosicionesDeDatos) tonos[p].Should().BeInRange(1, 8);
    }

    [Fact]
    public void CodificarYDecodificarSinRuidoDevuelveElMensaje()
    {
        var modo = new ModoJt9();
        var senal = modo.Generar("CQ EA8DLF IL18", 1000, 12000);
        senal.Length.Should().Be(85 * 6912);
        var ventana = new float[ParametrosJt9.MuestrasDeLaVentana];
        senal.CopyTo(ventana, 12000);

        var resultado = modo.Decodificador.Decodificar(ventana, DateTimeOffset.UnixEpoch);
        resultado.Decodificaciones.Should().ContainSingle();
        var d = resultado.Decodificaciones[0];
        d.Texto.Should().Be("CQ EA8DLF IL18");
        d.TonoHz.Should().BeInRange(998, 1002);
        Math.Abs(d.DesfaseSegundos).Should().BeLessThan(0.05);
        d.Modo.Should().Be(ModoDelModem.Jt9);
        d.EsCq.Should().BeTrue();
        d.Llamante.Valor.Should().Be("EA8DLF");
    }

    [Fact]
    public void DecodificaConDesfaseYRuidoModerado()
    {
        var modo = new ModoJt9();
        modo.Modo.Should().Be(ModoDelModem.Jt9);
        modo.Periodo.Should().Be(TimeSpan.FromSeconds(60));
        modo.FrecuenciaDeAnalisis.Should().Be(12000);

        var senal = modo.Generar("EA1ABC EA8DLF -15", 1500, 12000);
        var ventana = new float[ParametrosJt9.MuestrasDeLaVentana];
        var comienzo = (int)Math.Round(1.6 * 12000);
        for (var i = 0; i < senal.Length; i++) ventana[comienzo + i] = senal[i];
        GeneradorDeSenal.AnadirRuido(ventana, GeneradorDeSenal.PotenciaMedia(senal), -18, 12000, new Random(5));

        var salida = modo.Decodificar(ventana, DateTimeOffset.UnixEpoch, CancellationToken.None);
        salida.Should().ContainSingle();
        salida[0].Texto.Should().Be("EA1ABC EA8DLF -15");
        salida[0].DesfaseSegundos.Should().BeApproximately(0.6, 0.05);
        salida[0].TonoHz.Should().BeInRange(1499, 1501);
    }

    [Fact]
    public void UnMensajeQueNoCabeLanzaFormatException()
    {
        var modo = new ModoJt9();
        var generar = () => modo.Generar("ESTO NO CABE EN TRECE", 1500, 48000);
        generar.Should().Throw<FormatException>();
    }

    [Fact]
    public void ElAudioGeneradoEmpiezaYAcabaEnSilencioSuave()
    {
        var senal = new ModoJt9().Generar("CQ EA8DLF IL18", 1500, 48000);
        senal.Length.Should().Be((int)Math.Round(85 * 6912.0 / 12000 * 48000));
        Math.Abs(senal[0]).Should().BeLessThan(0.01f);
        Math.Abs(senal[^1]).Should().BeLessThan(0.01f);
        senal.Max().Should().BeApproximately(0.5f, 0.01f);
    }
}
