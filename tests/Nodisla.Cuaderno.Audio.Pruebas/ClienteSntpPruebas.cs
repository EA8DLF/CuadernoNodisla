using System.Buffers.Binary;
using FluentAssertions;
using Nodisla.Cuaderno.Audio.Reloj;

namespace Nodisla.Cuaderno.Audio.Pruebas;

/// <summary>
/// La cuenta del desvio de SNTP, comprobada con paquetes armados a mano.
/// </summary>
/// <remarks>
/// No se habla con ningun servidor: se fabrica la respuesta que habria dado uno y se comprueba
/// que de ella sale el desvio que tiene que salir. Asi la formula queda probada aunque no haya
/// red, y las pruebas no dependen de que internet vaya bien hoy.
/// </remarks>
public class ClienteSntpPruebas
{
    private static readonly DateTime OrigenNtp = new(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static void EscribirMarca(Span<byte> destino, DateTime instante)
    {
        var desdeElOrigen = instante - OrigenNtp;
        var segundos = (uint)desdeElOrigen.TotalSeconds;
        var resto = desdeElOrigen.TotalSeconds - segundos;

        BinaryPrimitives.WriteUInt32BigEndian(destino, segundos);
        BinaryPrimitives.WriteUInt32BigEndian(destino[4..], (uint)(resto * 4294967296.0));
    }

    private static byte[] Respuesta(DateTime recibido, DateTime enviado, byte primerByte = 0x24, byte estrato = 2)
    {
        var paquete = new byte[48];
        paquete[0] = primerByte;
        paquete[1] = estrato;
        EscribirMarca(paquete.AsSpan(32, 8), recibido);
        EscribirMarca(paquete.AsSpan(40, 8), enviado);
        return paquete;
    }

    [Fact]
    public void UnRelojAdelantadoSaleConDesvioPositivo()
    {
        var cliente = new ClienteSntp("servidor de prueba");

        // La hora de verdad son las 12:00:00,000; el ordenador cree que son las 12:00:01,000.
        var enElServidor = new DateTime(2026, 9, 22, 12, 0, 0, 50, DateTimeKind.Utc);
        var salida = new DateTimeOffset(2026, 9, 22, 12, 0, 1, 0, TimeSpan.Zero);
        var vuelta = new DateTimeOffset(2026, 9, 22, 12, 0, 1, 100, TimeSpan.Zero);

        var medida = cliente.Interpretar(Respuesta(enElServidor, enElServidor), salida, vuelta);

        medida.Should().NotBeNull();
        medida!.DesvioMs.Should().BeApproximately(1000.0, 1.0);
        medida.IdaYVueltaMs.Should().BeApproximately(100.0, 1.0);
        medida.Fuente.Should().Be("servidor de prueba");
    }

    [Fact]
    public void UnRelojAtrasadoSaleConDesvioNegativo()
    {
        var cliente = new ClienteSntp("servidor de prueba");

        // La hora de verdad son las 12:00:02,000; el ordenador cree que son las 12:00:00,000.
        var enElServidor = new DateTime(2026, 9, 22, 12, 0, 2, 50, DateTimeKind.Utc);
        var salida = new DateTimeOffset(2026, 9, 22, 12, 0, 0, 0, TimeSpan.Zero);
        var vuelta = new DateTimeOffset(2026, 9, 22, 12, 0, 0, 100, TimeSpan.Zero);

        var medida = cliente.Interpretar(Respuesta(enElServidor, enElServidor), salida, vuelta);

        medida.Should().NotBeNull();
        medida!.DesvioMs.Should().BeApproximately(-2000.0, 1.0);
    }

    [Fact]
    public void UnServidorQueDiceQueNoEstaSincronizadoNoVale()
    {
        var cliente = new ClienteSntp("servidor de prueba");
        var instante = new DateTime(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);

        // Aviso 3 en los dos bits de arriba: «no estoy sincronizado».
        var paquete = Respuesta(instante, instante, primerByte: 0xE4);

        cliente.Interpretar(paquete, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow).Should().BeNull();
    }

    [Fact]
    public void UnPaqueteQueNoEsDeServidorNoVale()
    {
        var cliente = new ClienteSntp("servidor de prueba");
        var instante = new DateTime(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);

        // Modo 3 es «cliente», no «servidor».
        var paquete = Respuesta(instante, instante, primerByte: 0x23);

        cliente.Interpretar(paquete, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow).Should().BeNull();
    }

    [Fact]
    public void UnPaqueteSinMarcasDeTiempoNoVale()
    {
        var cliente = new ClienteSntp("servidor de prueba");
        var paquete = new byte[48];
        paquete[0] = 0x24;
        paquete[1] = 2;

        cliente.Interpretar(paquete, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow).Should().BeNull();
    }

    [Fact]
    public void UnPaqueteCortoNoVale()
    {
        var cliente = new ClienteSntp("servidor de prueba");
        cliente.Interpretar(new byte[10], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow).Should().BeNull();
    }

    [Fact]
    public void HayQueDecirAQuienSeLePreguntaLaHora()
    {
        var fallo = () => new ClienteSntp("  ");
        fallo.Should().Throw<ArgumentException>();
    }
}
