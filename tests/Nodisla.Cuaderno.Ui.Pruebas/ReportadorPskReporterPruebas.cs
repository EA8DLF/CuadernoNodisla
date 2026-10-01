using System.Buffers.Binary;
using FluentAssertions;
using Nodisla.Cuaderno.Ui.Digital;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>El paquete de PSK Reporter, sin abrir ningun socket.</summary>
public sealed class ReportadorPskReporterPruebas
{
    private static readonly DateTimeOffset Hora = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SinLocalizadorNoSeApunta()
    {
        var r = new ReportadorPskReporter("Prueba 1.0", 7);

        r.Anotar(new RecepcionParaInformar("EA5XYZ", "", 14_075_500, -7, "FT8", Hora)).Should().BeFalse();
        r.Anotar(new RecepcionParaInformar("EA5XYZ", "IM98", 14_075_500, -7, "FT8", Hora)).Should().BeTrue();
        r.Pendientes.Should().Be(1);
    }

    [Fact]
    public void ElMismoIndicativoNoSeRepiteEnCincoMinutos()
    {
        var r = new ReportadorPskReporter("Prueba 1.0", 7);

        r.Anotar(new RecepcionParaInformar("EA5XYZ", "IM98", 14_075_500, -7, "FT8", Hora)).Should().BeTrue();
        r.Anotar(new RecepcionParaInformar("EA5XYZ", "IM98", 14_075_500, -9, "FT8", Hora + TimeSpan.FromMinutes(2))).Should().BeFalse();
        r.Anotar(new RecepcionParaInformar("EA5XYZ", "IM98", 14_075_500, -9, "FT8", Hora + TimeSpan.FromMinutes(6))).Should().BeTrue();
    }

    [Fact]
    public void SinIndicativoPropioNoSaleNada()
    {
        var r = new ReportadorPskReporter("Prueba 1.0", 7);
        r.Anotar(new RecepcionParaInformar("EA5XYZ", "IM98", 14_075_500, -7, "FT8", Hora));

        r.Empaquetar(Hora).Should().BeNull();
    }

    [Fact]
    public void ElPaqueteTieneCabeceraIpfixPlantillasYDatos()
    {
        var r = new ReportadorPskReporter("Prueba 1.0", origen: 0x12345678)
        {
            MiIndicativo = "EA8DLF",
            MiLocalizador = "IL18",
        };
        r.Anotar(new RecepcionParaInformar("EA5XYZ", "IM98", 14_075_500, -7, "FT8", Hora));

        var p = r.Empaquetar(Hora + TimeSpan.FromSeconds(30))!;

        // Cabecera: version 10, longitud total, hora, secuencia 1, origen.
        BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(0)).Should().Be(0x000A);
        BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(2)).Should().Be((ushort)p.Length);
        BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(4)).Should().Be((uint)(Hora + TimeSpan.FromSeconds(30)).ToUnixTimeSeconds());
        BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(8)).Should().Be(1);
        BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(12)).Should().Be(0x12345678);
        p.Length.Should().Be(p.Length / 4 * 4, "los conjuntos van rellenos a cuatro bytes");

        // Plantilla del receptor (opciones, id 3) con la 0x9992; del emisor (id 2) con la 0x9993.
        BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(16)).Should().Be(3);
        BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(20)).Should().Be(0x9992);
        var largoReceptor = BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(18));
        var emisor = 16 + largoReceptor;
        BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(emisor)).Should().Be(2);
        BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(emisor + 4)).Should().Be(0x9993);
        BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(emisor + 6)).Should().Be(7, "siete campos");

        // El primer campo del emisor es senderCallsign (0x8001) de la empresa 30351.
        BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(emisor + 8)).Should().Be(0x8001);
        BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(emisor + 12)).Should().Be(30351);

        // Y los datos llevan lo que se apunto.
        var texto = System.Text.Encoding.ASCII.GetString(p);
        texto.Should().Contain("EA8DLF").And.Contain("IL18").And.Contain("Prueba 1.0").And.Contain("EA5XYZ").And.Contain("IM98").And.Contain("FT8");

        r.Pendientes.Should().Be(0, "empaquetar vacia la cola");
        r.Secuencia.Should().Be(1);
    }

    [Fact]
    public void LaFrecuenciaYLaSenalVanEnSuSitio()
    {
        var r = new ReportadorPskReporter("P", origen: 1) { MiIndicativo = "EA8DLF", MiLocalizador = "IL18" };
        r.Anotar(new RecepcionParaInformar("EA5XYZ", "IM98", 14_075_500, -7, "FT8", Hora));

        var p = r.Empaquetar(Hora)!;

        // El conjunto de datos 0x9993 es el ultimo (la ultima aparicion del identificador):
        // tras su cabecera va el indicativo (1 + 6 bytes), la frecuencia (4) y la señal (1).
        var i = Buscar(p, [0x99, 0x93], desde: 40);
        i.Should().BePositive();
        var callsign = i + 4;
        p[callsign].Should().Be(6);
        var frecuencia = callsign + 7;
        BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(frecuencia)).Should().Be(14_075_500);
        unchecked((sbyte)p[frecuencia + 4]).Should().Be(-7);
    }

    private static int Buscar(byte[] p, byte[] patron, int desde)
    {
        for (var i = p.Length - patron.Length; i >= desde; i--)
        {
            if (p[i] == patron[0] && p[i + 1] == patron[1]) return i;
        }

        return -1;
    }
}
