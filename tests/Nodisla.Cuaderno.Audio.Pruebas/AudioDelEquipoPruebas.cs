using FluentAssertions;
using Nodisla.Cuaderno.Audio.Dispositivos;
using Nodisla.Cuaderno.Radio.Control.Ft710;

namespace Nodisla.Cuaderno.Audio.Pruebas;

/// <summary>
/// Cual de todas las veces que Windows ha visto el codec del equipo es la de AHORA.
/// </summary>
/// <remarks>
/// Los datos son los del registro de la maquina de Jose el 2026-11-15: cinco instancias de
/// <c>VID_0D8C&amp;PID_0013</c> (otros puertos USB, el PC anterior) y solo una enchufada. El
/// programa se quedaba con la PRIMERA que encontraba, que era una vieja: sus extremos estaban
/// desenchufados, asi que ningun dispositivo activo salia marcado «del equipo», no se
/// preseleccionaba el codec y los avisos de dispositivos no avisaban de nada.
/// </remarks>
public class AudioDelEquipoPruebas
{
    private const string EntradaActiva = "{0.0.1.00000000}.{d6ccab5b-0b63-464b-9d58-3b3b15ae22be}";
    private const string SalidaActiva = "{0.0.0.00000000}.{b996ef85-520d-49ca-877e-56ab7a1be21a}";

    private static readonly Guid Viejo1 = Guid.Parse("e0a4ec82-c150-11f1-bbe4-8a884be2ae03");
    private static readonly Guid Enchufado = Guid.Parse("34b914e6-c17b-11f1-bbe4-8a884be2ae03");
    private static readonly Guid Viejo2 = Guid.Parse("e0a4e804-c150-11f1-bbe4-8a884be2ae03");
    private static readonly Guid Viejo3 = Guid.Parse("e0a4ee07-c150-11f1-bbe4-8a884be2ae03");
    private static readonly Guid Viejo4 = Guid.Parse("312cd976-bf13-11f1-bbe2-c7dc1626aa1a");
    private static readonly Guid Cascos = Guid.Parse("312cc927-bf13-11f1-bbe2-806e6f6e6963");

    /// <summary>En el orden en que los da el registro: el enchufado NO es el primero.</summary>
    private static readonly Guid[] Contenedores = [Viejo1, Enchufado, Viejo2, Viejo3, Viejo4];

    private static readonly ExtremoConContenedor[] Extremos =
    [
        Entrada(Cascos, "{1a058150-aa89-4aea-86f6-ea9971cd3df8}", "Micrófono", activo: true),
        Entrada(Viejo4, "{35c1465c-8185-4dee-bb30-c26f1dd6df3b}", "Micrófono", activo: false),
        Entrada(Viejo2, "{56b98644-a80a-4d15-a485-ec3ea7c465a8}", "Micrófono", activo: false),
        Entrada(Viejo1, "{7811563c-1bf7-4b2e-9679-f4ea82fb58fb}", "YAESU Micrófono", activo: false),
        Entrada(Viejo3, "{945b850c-2c20-4dcc-a5eb-448f64955a7e}", "Micrófono", activo: false),
        Entrada(Enchufado, "{d6ccab5b-0b63-464b-9d58-3b3b15ae22be}", "Micrófono", activo: true),
        Salida(Cascos, "{066c81a6-a737-4725-b234-6b25a69c73d9}", "Altavoces", activo: true),
        Salida(Viejo4, "{073e836b-8971-4004-9fc4-78981f822e90}", "Altavoces", activo: false),
        Salida(Viejo2, "{27066f5f-6dee-46a9-91a2-154a994e161b}", "Altavoces", activo: false),
        Salida(Viejo3, "{342d7ae3-8efa-4669-a7b9-e4252b96d5c6}", "Altavoces", activo: false),
        Salida(Enchufado, "{b996ef85-520d-49ca-877e-56ab7a1be21a}", "Altavoces", activo: true),
        Salida(Viejo1, "{c57ea43a-54ac-44bd-93d2-312ec82ae237}", "Altavoces", activo: false),
    ];

    [Fact]
    public void Entre_varias_instancias_del_codec_se_elige_la_que_esta_enchufada()
    {
        var audio = AudioDelFt710.Elegir(Contenedores, Extremos);

        audio.Should().NotBeNull();
        audio!.ContenedorUsb.Should().Be(Enchufado, "la primera instancia del registro es de otro puerto USB y esta desenchufada");
        audio.Entradas.Should().ContainSingle().Which.Identificador.Should().Be(EntradaActiva);
        audio.Salidas.Should().ContainSingle().Which.Identificador.Should().Be(SalidaActiva);
        audio.Entradas.Concat(audio.Salidas).Should().OnlyContain(e => e.Activo);
    }

    [Fact]
    public void Los_dispositivos_activos_del_codec_salen_marcados_del_equipo()
    {
        var audio = AudioDelFt710.Elegir(Contenedores, Extremos)!;
        var delEquipo = new HashSet<string>(audio.Entradas.Select(e => e.Identificador), StringComparer.OrdinalIgnoreCase);

        // Lo que enumera WASAPI: solo los activos.
        var lista = CatalogoDeDispositivos.Componer(
            [
                ("{0.0.1.00000000}.{1a058150-aa89-4aea-86f6-ea9971cd3df8}", "Micrófono (Lync USB Headset)"),
                (EntradaActiva, "Micrófono (5- USB Audio Device)"),
            ],
            delEquipo,
            esDeEntrada: true);

        lista[0].Nombre.Should().Be("Micrófono (5- USB Audio Device)", "el del equipo va el primero");
        lista[0].EsDelEquipo.Should().BeTrue();
        lista[1].EsDelEquipo.Should().BeFalse();
    }

    [Fact]
    public void Con_la_radio_apagada_se_da_una_instancia_conocida_sin_extremos_activos()
    {
        var apagados = Extremos.Select(e => e with { Extremo = e.Extremo with { Activo = false } }).ToArray();

        var audio = AudioDelFt710.Elegir(Contenedores, apagados);

        audio.Should().NotBeNull("el codec se conoce aunque ahora no este");
        audio!.Entradas.Concat(audio.Salidas).Should().NotContain(e => e.Activo);
    }

    [Fact]
    public void Sin_ninguna_instancia_del_codec_no_hay_audio_del_equipo() =>
        AudioDelFt710.Elegir([], Extremos).Should().BeNull();

    [Fact]
    public void Si_hay_dos_enchufadas_gana_la_que_tiene_entrada_y_salida()
    {
        // Una instancia a medias (solo la salida activa) no le quita el sitio a la completa.
        ExtremoConContenedor[] extremos =
        [
            Salida(Viejo1, "{c57ea43a-54ac-44bd-93d2-312ec82ae237}", "Altavoces", activo: true),
            Entrada(Enchufado, "{d6ccab5b-0b63-464b-9d58-3b3b15ae22be}", "Micrófono", activo: true),
            Salida(Enchufado, "{b996ef85-520d-49ca-877e-56ab7a1be21a}", "Altavoces", activo: true),
        ];

        AudioDelFt710.Elegir(Contenedores, extremos)!.ContenedorUsb.Should().Be(Enchufado);
    }

    private static ExtremoConContenedor Entrada(Guid contenedor, string guid, string nombre, bool activo) =>
        new(contenedor, new ExtremoDeAudio("{0.0.1.00000000}." + guid, nombre, "USB Audio Device", EsEntrada: true, activo));

    private static ExtremoConContenedor Salida(Guid contenedor, string guid, string nombre, bool activo) =>
        new(contenedor, new ExtremoDeAudio("{0.0.0.00000000}." + guid, nombre, "USB Audio Device", EsEntrada: false, activo));
}
