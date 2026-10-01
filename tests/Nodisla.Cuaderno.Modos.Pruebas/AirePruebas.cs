using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Modos.Ft8;
using Nodisla.Cuaderno.Modos.Ldpc;
using Nodisla.Cuaderno.Modos.Senal;

namespace Nodisla.Cuaderno.Modos.Pruebas;

/// <summary>
/// FT8 contra una grabacion REAL del aire, no contra el propio emisor.
/// </summary>
/// <remarks>
/// <para>
/// Grabada el 27-09-2026 a las 15:17:45 UTC en 20 m por el codec USB del FT-710 de EA8DLF.
/// Es la prueba que falto durante una semana: todas las demas generan la senal con nuestro
/// propio codificador, y un fallo compartido por emisor y receptor —el CRC-14 calculado sin los
/// catorce ceros— las dejaba pasar a todas mientras con la radio no salia ni una
/// decodificacion. Una grabacion del aire solo decodifica si hablamos el protocolo de verdad.
/// </para>
/// <para>
/// Los mensajes esperados son los que salieron de esta misma grabacion una vez corregido el
/// CRC; son indicativos reales y coherentes entre si (YO2CMI y N3XX se cruzan informes).
/// </para>
/// </remarks>
public sealed class AirePruebas
{
    private static readonly TablasDelProtocolo Tablas = TablasDelProtocolo.Cargar();

    [Fact]
    public void UnaVentanaRealDelAireDecodificaLasEstacionesQueHabia()
    {
        var ruta = Path.Combine(AppContext.BaseDirectory, "Aire", "ft8-20m-2026-09-27-151745.wav");
        var audio = LectorWav.Leer(ruta);
        var modo = new ModoFt8(ModoDelModem.Ft8, Tablas, new CatalogoDeIndicativos());

        var resultado = modo.DecodificarConCuentas(
            audio.Muestras, audio.FrecuenciaDeMuestreo, new DateTimeOffset(2026, 9, 27, 15, 17, 45, TimeSpan.Zero), 0);

        var textos = resultado.Decodificaciones.Select(d => d.Texto).ToList();
        textos.Should().Contain(new[]
        {
            "CQ HB9EFK JN46",
            // Hasta el 28-09-2026 salian «-06» y «-30»: el campo de 15 bits estaba corrido uno
            // (ver BaseDelInforme). Con el valor contrastado con ft8_lib salen -07 y 73, que es
            // lo que de verdad mandaron: un 73 al final del cruce de informes, no un -30.
            "M8KKH SV1BLD -07",
            "N3XX YO2CMI 73",
            "W9RJC F4BYA JN19",
            "A41DV F8VNU JN13",
        });
        textos.Count.Should().BeGreaterThanOrEqualTo(6);
    }

    [Fact]
    public void ElCrc14CoincideConElDelProtocolo()
    {
        // Vector de referencia: el mensaje de 77 bits todo a cero tiene CRC 0 en cualquier forma;
        // el de un solo 1 en el ultimo bit tiene que valer x^(5+14) mod P, que en la forma
        // correcta es distinto del que daba la forma sin ceros finales (x^5 mod P = 0x20).
        var ceros = new byte[Crc14.BitsDelMensaje];
        Crc14.Calcular(ceros).Should().Be(0);

        var uno = new byte[Crc14.BitsDelMensaje];
        uno[^1] = 1;
        Crc14.Calcular(uno).Should().NotBe(0x20, "eso es el resto sin los catorce ceros: el fallo que tumbaba FT8");
    }
}
