using System.Net;
using FluentAssertions;

namespace Nodisla.Cuaderno.Servidores.Pruebas;

/// <summary>Quien entra: de fabrica, solo el propio PC y todo apagado.</summary>
public sealed class ControlDeAccesoPruebas
{
    [Fact]
    public void De_fabrica_todo_apagado_solo_loopback_y_sin_TX()
    {
        var o = new OpcionesDeServidores();
        o.RigctldActivo.Should().BeFalse();
        o.TciActivo.Should().BeFalse();
        o.PermitirTx.Should().BeFalse();
        o.AbrirALaRedLocal.Should().BeFalse();
        o.PuertoRigctld.Should().Be(4532);
        o.PuertoTci.Should().Be(40001);

        var acceso = new ControlDeAcceso(o);
        acceso.DireccionDeEscucha.Should().Be(IPAddress.Loopback);
        acceso.Admite(IPAddress.Loopback).Should().BeTrue();
        acceso.Admite(IPAddress.IPv6Loopback).Should().BeTrue();
        acceso.Admite(IPAddress.Parse("192.168.1.20")).Should().BeFalse();
    }

    [Fact]
    public void Abierto_a_la_red_solo_entran_las_IP_de_la_lista()
    {
        var o = new OpcionesDeServidores { AbrirALaRedLocal = true, IpsPermitidas = ["192.168.1.20", "10.0.5.0/24", "0.0.0.0/0", "basura"] }.Acotar();
        o.IpsPermitidas.Should().Equal("192.168.1.20", "10.0.5.0/24");

        var acceso = new ControlDeAcceso(o);
        acceso.DireccionDeEscucha.Should().Be(IPAddress.Any);
        acceso.Admite(IPAddress.Parse("192.168.1.20")).Should().BeTrue();
        acceso.Admite(IPAddress.Parse("::ffff:192.168.1.20")).Should().BeTrue();
        acceso.Admite(IPAddress.Parse("192.168.1.21")).Should().BeFalse();
        acceso.Admite(IPAddress.Parse("10.0.5.77")).Should().BeTrue();
        acceso.Admite(IPAddress.Parse("10.0.6.1")).Should().BeFalse();
        acceso.Admite(IPAddress.Loopback).Should().BeTrue();
    }

    [Fact]
    public void Se_guarda_y_se_lee()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "cuaderno-servidores-" + Guid.NewGuid().ToString("N"));
        try
        {
            new OpcionesDeServidores { RigctldActivo = true, PuertoTci = 50001, TokenTci = "  x  " }.Guardar(carpeta);
            var leidas = OpcionesDeServidores.Leer(carpeta);
            leidas.RigctldActivo.Should().BeTrue();
            leidas.PuertoTci.Should().Be(50001);
            leidas.TokenTci.Should().Be("x");
            OpcionesDeServidores.Leer(Path.Combine(carpeta, "no-existe")).RigctldActivo.Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(carpeta)) Directory.Delete(carpeta, true);
        }
    }

    [Fact]
    public async Task Los_servidores_escuchan_en_127_0_0_1_de_fabrica()
    {
        await using var banco = new Banco();
        await banco.ArrancarAsync(rigctld: true, tci: true);
        banco.Servidores.Rigctld.Punto!.Address.Should().Be(IPAddress.Loopback);
        banco.Servidores.Tci.Punto!.Address.Should().Be(IPAddress.Loopback);

        await banco.Servidores.AplicarAsync(new OpcionesDeServidores());
        banco.Servidores.Rigctld.Encendido.Should().BeFalse();
        banco.Servidores.Tci.Encendido.Should().BeFalse();
    }
}
