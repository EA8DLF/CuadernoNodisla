using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Radio.Espectro;
using Nodisla.Cuaderno.Ui.Ajustes;
using Nodisla.Cuaderno.Ui.Recursos;
using Nodisla.Cuaderno.Ui.VistaModelos;
using Nodisla.Cuaderno.Ui.Vistas;
using Xunit.Abstractions;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// El analizador de la radio como mando (docs\19-analisis-thetis.md, A2 y A3): spots en
/// carriles, clic y rueda para sintonizar, suelo de ruido, AGC de la cascada, paletas, picos y
/// desplazamiento de la cascada al resintonizar. Con tramas REALES del FT-710 de EA8DLF
/// (18.100.000, CENTER, span 200 kHz) y un control de equipo falso: no se toca ninguna radio.
/// </summary>
/// <remarks>
/// <c>CUADERNO_CAPTURAS_ANALIZADOR</c> (una carpeta) deja ademas las capturas de la ayuda.
/// </remarks>
public sealed class AnalizadorComoMandoPruebas(ITestOutputHelper salida)
{
    private static readonly EscalaDelEspectro Escala200k = new(18_000_000, 18_200_000);

    // ─────────────── Carriles ───────────────

    [Fact]
    public void Los_rotulos_de_un_mismo_carril_no_se_pisan()
    {
        // Doce estaciones apiñadas en 10 kHz (un pileup en 200 kHz de span).
        var spots = Enumerable.Range(0, 12)
            .Select(i => new SpotParaColocar(18_095_000 + (i * 900), 40))
            .ToList();

        var colocados = CarrilesDeSpots.Colocar(spots, Escala200k, 850, 3, out var sinSitio);

        colocados.Should().NotBeEmpty();
        foreach (var carril in colocados.GroupBy(c => c.Carril))
        {
            var ordenados = carril.OrderBy(c => c.Izquierda).ToList();
            for (var i = 1; i < ordenados.Count; i++)
            {
                ordenados[i].Izquierda.Should().BeGreaterThanOrEqualTo(
                    ordenados[i - 1].Izquierda + 40 + CarrilesDeSpots.Separacion, "dos rotulos del mismo carril no se tocan");
            }
        }

        colocados.Select(c => c.Carril).Should().OnlyContain(c => c >= 0 && c < 3);
        (colocados.Count + sinSitio).Should().Be(12, "cada spot o se coloca o se cuenta como sin sitio");
    }

    [Fact]
    public void Lo_que_aporta_se_coloca_antes_que_lo_trabajado()
    {
        // Tres estaciones en 2 kHz y un solo carril: caben dos (una a cada lado de su marca), y
        // la que se queda fuera es la ya trabajada.
        var spots = new List<SpotParaColocar>
        {
            new(18_100_000, 60, 0),
            new(18_101_000, 60, 2),
            new(18_099_000, 60, 1),
        };

        var colocados = CarrilesDeSpots.Colocar(spots, Escala200k, 850, 1, out var sinSitio);

        colocados.Should().HaveCount(2, "una a la derecha de su marca y otra a la izquierda");
        colocados.Select(c => c.Indice).Should().Contain(1).And.Contain(2).And.NotContain(0);
        sinSitio.Should().Be(1);
    }

    [Fact]
    public void Lo_que_no_se_ve_no_se_coloca_y_en_el_borde_el_rotulo_se_da_la_vuelta()
    {
        var spots = new List<SpotParaColocar>
        {
            new(17_990_000, 40),      // fuera, por la izquierda
            new(18_199_000, 40),      // pegado al borde derecho
        };

        var colocados = CarrilesDeSpots.Colocar(spots, Escala200k, 850, 2, out _);

        colocados.Should().ContainSingle();
        var borde = colocados[0];
        borde.Indice.Should().Be(1);
        (borde.Izquierda + 40).Should().BeLessThanOrEqualTo(850, "el rotulo no se sale de la pantalla");
        borde.RotuloALaIzquierda.Should().BeTrue();
    }

    // ─────────────── Escala: punto ↔ frecuencia ───────────────

    [Theory]
    [InlineData(0, 18_000_000)]
    [InlineData(425, 18_100_000)]
    [InlineData(850, 18_200_000)]
    [InlineData(212.5, 18_050_000)]
    public void Un_punto_es_una_frecuencia_y_al_reves(double x, double hz)
    {
        Escala200k.HzEn(x, 850).Should().BeApproximately(hz, 0.001);
        Escala200k.XDe(hz, 850).Should().BeApproximately(x, 0.001);
    }

    [Theory]
    [InlineData(200_000, 500)]
    [InlineData(100_000, 500)]
    [InlineData(50_000, 100)]
    [InlineData(20_000, 50)]
    [InlineData(1_000_000, 5_000)]
    public void El_paso_automatico_sigue_al_span(int span, long paso)
    {
        new EscalaDelEspectro(14_000_000, 14_000_000 + span).PasoAutomatico().Should().Be(paso);
    }

    [Theory]
    [InlineData(14_074_260.0, 500, 14_074_500)]
    [InlineData(14_074_240.0, 500, 14_074_000)]
    [InlineData(14_074_250.0, 500, 14_074_500)]
    [InlineData(7_012_345.0, 10, 7_012_350)]
    [InlineData(7_012_345.0, 0, 7_012_345)]
    public void El_clic_se_ajusta_al_paso(double hz, long paso, long esperado)
    {
        EscalaDelEspectro.AjustarAlPaso(hz, paso).Should().Be(esperado);
    }

    // ─────────────── Clic, rueda y spot → orden a la radio ───────────────

    [Fact]
    public void Clic_en_el_espectro_manda_esa_frecuencia_ajustada_al_paso()
        => EnHiloDeInterfaz(() =>
        {
            var (modelo, control, _) = ModeloConControlFalso();

            // 3/4 del ancho de 18.000-18.200: 18.150.000; el paso de 200 kHz es 500 Hz.
            Esperar(modelo.ClicAsync(0.75)).Should().BeTrue();
            Esperar(modelo.ClicAsync(0.3001)).Should().BeTrue();

            control.Pedidas.Should().Equal(18_150_000, 18_060_000);
        });

    [Fact]
    public void La_rueda_mueve_por_pasos_y_suma_sobre_lo_ultimo_pedido()
        => EnHiloDeInterfaz(() =>
        {
            var (modelo, control, _) = ModeloConControlFalso();

            // VFO en 18.100.000 (la trama); tres muescas de 500 Hz y luego dos para abajo.
            Esperar(modelo.RuedaAsync(3)).Should().BeTrue();
            Esperar(modelo.RuedaAsync(-2)).Should().BeTrue();

            // Con Mayúsculas, la décima parte del paso.
            Esperar(modelo.RuedaAsync(1, fino: true)).Should().BeTrue();

            control.Pedidas.Should().Equal(18_101_500, 18_100_500, 18_100_550);
        });

    [Fact]
    public void Transmitiendo_ni_el_clic_ni_la_rueda_ni_el_spot_mandan_nada()
        => EnHiloDeInterfaz(() =>
        {
            var (modelo, control, estado) = ModeloConControlFalso();
            estado.Transmitiendo = true;

            Esperar(modelo.ClicAsync(0.75)).Should().BeFalse();
            Esperar(modelo.RuedaAsync(2)).Should().BeFalse();
            Esperar(modelo.ElegirSpotAsync(Fila("EA8DLF", 18_120_000))).Should().BeFalse();

            control.Pedidas.Should().BeEmpty();
            estado.SpotsElegidos.Should().BeEmpty();
        });

    [Fact]
    public void Sin_equipo_conectado_el_clic_no_manda_nada()
        => EnHiloDeInterfaz(() =>
        {
            var (modelo, control, estado) = ModeloConControlFalso();
            estado.Conectado = false;

            Esperar(modelo.ClicAsync(0.5)).Should().BeFalse();
            control.Pedidas.Should().BeEmpty();
        });

    [Fact]
    public void Con_el_clic_quitado_en_los_ajustes_no_se_sintoniza()
        => EnHiloDeInterfaz(() =>
        {
            var (modelo, control, _) = ModeloConControlFalso();
            modelo.Aplicar(new AjustesDelAnalizador { ClicParaSintonizar = false });

            Esperar(modelo.ClicAsync(0.75)).Should().BeFalse();
            Esperar(modelo.RuedaAsync(1)).Should().BeFalse();
            control.Pedidas.Should().BeEmpty();
        });

    [Fact]
    public void Clic_en_un_spot_va_por_el_camino_del_doble_clic_del_cluster()
        => EnHiloDeInterfaz(() =>
        {
            var (modelo, control, estado) = ModeloConControlFalso();
            var fila = Fila("ZD7X", 18_085_000, nueva: true);

            Esperar(modelo.ElegirSpotAsync(fila)).Should().BeTrue();

            estado.SpotsElegidos.Should().ContainSingle().Which.Should().BeSameAs(fila);
            control.Pedidas.Should().BeEmpty("la frecuencia y el modo los pone IrAlSpotAsync, como en la lista");

            // Y la rueda sigue desde el spot, no desde lo que diga la trama vieja.
            Esperar(modelo.RuedaAsync(1)).Should().BeTrue();
            control.Pedidas.Should().Equal(18_085_500);
        });

    [Fact]
    public void Muchas_muescas_seguidas_no_hacen_cola_solo_se_manda_la_ultima()
        => EnHiloDeInterfaz(() =>
        {
            var control = new ControlQueApunta { Retener = true };
            var sintonia = new SintoniaDelAnalizador(control, () => true, () => false, _ => Task.CompletedTask);

            var primera = sintonia.IrAAsync(14_000_000);
            _ = sintonia.IrAAsync(14_000_500);
            _ = sintonia.IrAAsync(14_001_000);
            _ = sintonia.IrAAsync(14_001_500);
            control.Soltar();
            Esperar(primera).Should().BeTrue();

            control.Pedidas.Should().Equal(14_000_000, 14_001_500);
        });

    // ─────────────── Ir a un spot (lista, bandmap, analizador): un solo camino ───────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Ir_al_spot_transmitiendo_no_hace_nada_y_dice_por_que(bool conectado)
    {
        var control = new ControlQueApunta();
        var preparados = new List<FilaDeSpot>();
        var avisos = new List<string>();
        var llevar = new LlevarAlSpot(control, () => conectado, () => true, preparados.Add, avisos.Add);

        (await llevar.IrAsync(Fila("ZD7X", 18_085_000))).Should().BeFalse();

        control.Pedidas.Should().BeEmpty("no se manda ninguna orden a la radio");
        control.Modos.Should().BeEmpty();
        preparados.Should().BeEmpty("tampoco se toca el contacto nuevo");
        avisos.Should().ContainSingle().Which.Should().Be(Nodisla.Cuaderno.Idiomas.Textos.T("Cabina.Spot.NoTransmitiendo")).And.NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Ir_al_spot_sin_equipo_rellena_el_contacto_sin_tocar_la_radio_y_avisa_suave()
    {
        var control = new ControlQueApunta();
        var preparados = new List<FilaDeSpot>();
        var avisos = new List<string>();
        var llevar = new LlevarAlSpot(control, () => false, () => false, preparados.Add, avisos.Add);
        var fila = Fila("ZD7X", 18_085_000);

        (await llevar.IrAsync(fila)).Should().BeFalse("la radio no se ha movido");

        preparados.Should().ContainSingle().Which.Should().BeSameAs(fila, "quien apunta a mano sin CAT sigue teniendo el contacto relleno");
        control.Pedidas.Should().BeEmpty();
        control.Modos.Should().BeEmpty();
        avisos.Should().ContainSingle().Which.Should().Be(Nodisla.Cuaderno.Idiomas.Textos.T("Cabina.Spot.SinEquipo"));
    }

    [Fact]
    public async Task Ir_al_spot_con_equipo_y_en_recepcion_pone_frecuencia_modo_y_contacto()
    {
        var control = new ControlQueApunta();
        var preparados = new List<FilaDeSpot>();
        var avisos = new List<string>();
        var llevar = new LlevarAlSpot(control, () => true, () => false, preparados.Add, avisos.Add);
        var fila = Fila("ZD7X", 18_085_000);

        (await llevar.IrAsync(fila)).Should().BeTrue();

        // Frecuencia, modo y otra vez frecuencia (cambiar de modo corre el dial del FT-710).
        control.Pedidas.Should().Equal(18_085_000, 18_085_000);
        control.Modos.Should().ContainSingle();
        preparados.Should().ContainSingle().Which.Should().BeSameAs(fila);
        avisos.Should().BeEmpty();
    }

    // ─────────────── Spots a la vista ───────────────

    [Fact]
    public void Solo_salen_los_spots_que_caen_en_lo_que_se_ve_y_uno_por_indicativo()
        => EnHiloDeInterfaz(() =>
        {
            var (modelo, _, _) = ModeloConControlFalso();
            modelo.PonerSpots(
            [
                Fila("ZD7X", 18_085_000),
                Fila("ZD7X", 18_086_000),    // repetido, mas viejo: fuera
                Fila("JA1ABC", 14_074_000),  // otra banda: fuera
                Fila("VK9DX", 18_199_000),
            ]);

            modelo.SpotsALaVista.Select(f => f.Indicativo).Should().Equal("ZD7X", "VK9DX");
            modelo.SpotsALaVista[0].Spot.Frecuencia.Hercios.Should().Be(18_085_000);

            modelo.Aplicar(new AjustesDelAnalizador { SpotsEncima = false });
            modelo.SpotsALaVista.Should().BeEmpty();
        });

    [Fact]
    public void El_color_del_spot_dice_su_novedad()
    {
        var nueva = AnalizadorDelEquipo.ColorDelSpot(Fila("ZD7X", 18_085_000, nueva: true));
        var hueco = AnalizadorDelEquipo.ColorDelSpot(Fila("EA1AA", 18_085_000, hueco: true));
        var vista = AnalizadorDelEquipo.ColorDelSpot(Fila("EA1AA", 18_085_000));

        new[] { nueva, hueco, vista }.Distinct().Should().HaveCount(3);
    }

    [Fact]
    public void En_pantalla_los_rotulos_salen_en_carriles_sin_pisarse()
        => EnHiloDeInterfaz(() =>
        {
            var (modelo, _, _) = ModeloConControlFalso();
            modelo.PonerSpots(SpotsDeUnPileup());

            var (control, ventana) = Montar(modelo, 640, 220);
            try
            {
                var rotulos = control.RotulosVisibles.ToList();
                rotulos.Should().HaveCountGreaterThan(5);
                foreach (var carril in rotulos.GroupBy(r => r.Carril))
                {
                    var ordenados = carril.OrderBy(r => r.Izquierda).ToList();
                    for (var i = 1; i < ordenados.Count; i++)
                    {
                        var anterior = ordenados[i - 1];
                        ordenados[i].Izquierda.Should().BeGreaterThan(
                            anterior.Izquierda + AnalizadorDelEquipo.AnchoDeRotulo(anterior.Fila.Indicativo));
                    }
                }

                // Las entidades nuevas siempre caben.
                rotulos.Select(r => r.Fila.Indicativo).Should().Contain("ZD7X").And.Contain("3Y0J");
            }
            finally
            {
                ventana.Close();
            }
        });

    // ─────────────── Suelo de ruido y AGC de la cascada ───────────────

    [Fact]
    public void El_suelo_se_queda_en_el_ruido_aunque_la_banda_este_llena()
    {
        var suelo = new SueloDeRuido();
        var azar = new Random(1);
        var niveles = new byte[850];
        for (var pasada = 0; pasada < 200; pasada++)
        {
            for (var i = 0; i < niveles.Length; i++)
            {
                // Ruido de 60 a 74 y un 40 % de la banda con señales de 90 a 140 (un concurso).
                niveles[i] = (byte)(i % 5 < 2 ? azar.Next(90, 140) : azar.Next(60, 75));
            }

            suelo.Seguir(niveles, pasada * 90);
        }

        suelo.Valor.Should().BeInRange(60, 68, "el suelo es el ruido, no la mediana de una banda llena");
    }

    [Fact]
    public void Al_cambiar_de_banda_el_suelo_llega_deprisa_y_sin_cambio_va_despacio()
    {
        var suelo = new SueloDeRuido();
        var bajo = Enumerable.Repeat((byte)60, 850).ToArray();
        var alto = Enumerable.Repeat((byte)100, 850).ToArray();
        for (var i = 0; i < 50; i++) suelo.Seguir(bajo, i * 90);
        suelo.Valor.Should().BeApproximately(60, 1.5);

        // Sin aviso (p. ej. se pone el preamplificador): sube despacio, sin saltos.
        var lento = new SueloDeRuido();
        for (var i = 0; i < 50; i++) lento.Seguir(bajo, i * 90);
        for (var i = 0; i < 5; i++) lento.Seguir(alto, 5000 + (i * 90));
        lento.Valor.Should().BeLessThan(70);

        // Con aviso (otra banda u otro span): en un segundo ya esta.
        suelo.AtaqueRapido(5000);
        for (var i = 0; i < 11; i++) suelo.Seguir(alto, 5000 + (i * 90));
        suelo.Valor.Should().BeApproximately(100, 3);
    }

    [Fact]
    public void Con_las_tramas_reales_el_suelo_cae_en_el_ruido_de_la_radio()
    {
        var pintor = new PintorDelAnalizador();
        var trazas = TrazasReales();
        var t = 0L;
        foreach (var traza in Enumerable.Repeat(trazas, 30).SelectMany(x => x))
        {
            pintor.Pintar(traza.Niveles, traza.InicioHz, traza.FinHz, t += 90);
        }

        // docs\14: «suelo ~68, señales hasta ~100» en 17 m tranquila (la mediana de esas tramas
        // es 68-74; el suelo queda algo por debajo, en la parte baja del ruido).
        pintor.Suelo.Should().BeInRange(60, 72);

        // El ruido sale oscuro y lo que asoma 40 puntos, claro.
        Brillo(pintor.ColorDeCascada((byte)pintor.Suelo)).Should().BeLessThan(0.2);
        Brillo(pintor.ColorDeCascada((byte)(pintor.Suelo + 40))).Should().BeGreaterThan(0.35);
    }

    [Fact]
    public void El_suelo_se_recuerda_por_banda()
    {
        var pintor = new PintorDelAnalizador();
        var en17 = Enumerable.Repeat((byte)68, 850).ToArray();
        var en40 = Enumerable.Repeat((byte)110, 850).ToArray();
        var t = 0L;
        for (var i = 0; i < 40; i++) pintor.Pintar(en17, 18_000_000, 18_200_000, t += 90);
        for (var i = 0; i < 40; i++) pintor.Pintar(en40, 7_000_000, 7_200_000, t += 90);
        pintor.Suelo.Should().BeApproximately(110, 3);

        // De vuelta a 17 m: el suelo de 17 m sale en la primera pasada, sin esperar.
        pintor.Pintar(en17, 18_000_000, 18_200_000, t += 90);
        pintor.Suelo.Should().BeApproximately(68, 3);
    }

    [Fact]
    public void Sin_agc_el_negro_es_el_nivel_fijado()
    {
        var pintor = new PintorDelAnalizador { SueloAutomatico = false, NivelBajo = 100, Contraste = 50 };
        foreach (var traza in TrazasReales()) pintor.Pintar(traza.Niveles);

        pintor.ColorDeCascada(100).Should().Be(PaletasDelAnalizador.Tabla(PaletaDelAnalizador.Radio)[0]);
        pintor.ColorDeCascada(150).Should().Be(PaletasDelAnalizador.Tabla(PaletaDelAnalizador.Radio)[255]);
    }

    [Theory]
    [InlineData(PaletaDelAnalizador.Radio)]
    [InlineData(PaletaDelAnalizador.Nodisla)]
    [InlineData(PaletaDelAnalizador.Arcoiris)]
    [InlineData(PaletaDelAnalizador.Fuego)]
    [InlineData(PaletaDelAnalizador.Hielo)]
    [InlineData(PaletaDelAnalizador.Gris)]
    public void Cada_paleta_va_de_oscuro_a_claro(PaletaDelAnalizador paleta)
    {
        var tabla = PaletasDelAnalizador.Tabla(paleta);
        tabla.Should().HaveCount(256);
        Brillo(tabla[0]).Should().BeLessThan(0.1);
        Brillo(tabla[255]).Should().BeGreaterThan(0.8);
        tabla.Distinct().Count().Should().BeGreaterThan(100);
        PaletasDelAnalizador.DesdeNombre(paleta.ToString()).Should().Be(paleta);
    }

    [Fact]
    public void Los_picos_se_marcan_con_lo_que_asoman_sobre_el_ruido()
    {
        var pintor = new PintorDelAnalizador { MarcarPicos = true };
        var niveles = Enumerable.Repeat((byte)60, 850).ToArray();
        Pico(niveles, 200, 100);   // +40
        Pico(niveles, 202, 96);    // pegado al anterior, sin bajar 8 entre los dos: es el mismo
        Pico(niveles, 600, 84);    // +24
        Pico(niveles, 615, 82);    // a 15 puntos de otro mas alto: no se marca (las cifras se pisarian)
        Pico(niveles, 700, 75);    // +15: es lo que asoma el ruido, no llega
        for (var i = 0; i < 20; i++) pintor.Pintar(niveles);

        pintor.Picos.Should().HaveCount(2);
        pintor.Picos[0].Posicion.Should().BeApproximately(200.5 / 850, 0.002);
        pintor.Picos[0].SobreElSuelo.Should().BeApproximately(40, 2);
        pintor.Picos[1].Posicion.Should().BeApproximately(600.5 / 850, 0.002);
        pintor.Picos[1].SobreElSuelo.Should().BeApproximately(24, 2);

        pintor.MarcarPicos = false;
        pintor.Pintar(niveles);
        pintor.Picos.Should().BeEmpty();
    }

    // ─────────────── Desplazamiento de la cascada ───────────────

    [Fact]
    public void Al_resintonizar_la_cascada_se_corre_y_la_señal_sigue_en_su_frecuencia()
    {
        var pintor = new PintorDelAnalizador();
        var t = 0L;
        const long senal = 18_100_000;

        // Diez pasadas con el VFO en 18.100 (la señal en el centro)…
        for (var i = 0; i < 10; i++) pintor.Pintar(ConSenal(18_000_000, 200_000, senal), 18_000_000, 18_200_000, t += 90);
        var columnaAntes = ColumnaMasClara(pintor, 5);
        columnaAntes.Should().BeInRange(423, 427);

        // …y se sube el VFO 10 kHz: 42,5 puntos. Lo pintado se va a la izquierda.
        pintor.Pintar(ConSenal(18_010_000, 200_000, senal), 18_010_000, 18_210_000, t += 90);
        pintor.UltimoDesplazamiento.Should().Be(-42);
        ColumnaMasClara(pintor, 5).Should().BeInRange(columnaAntes - 43, columnaAntes - 41, "la fila vieja sigue debajo de la señal");
        ColumnaMasClara(pintor, 0).Should().BeInRange(columnaAntes - 44, columnaAntes - 40, "la fila nueva cae en el mismo sitio");
    }

    [Fact]
    public void Muchos_saltos_pequeños_no_pierden_el_resto()
    {
        var pintor = new PintorDelAnalizador();
        var t = 0L;
        var niveles = Enumerable.Repeat((byte)60, 850).ToArray();
        pintor.Pintar(niveles, 18_000_000, 18_200_000, t += 90);

        // Diez saltos de 100 Hz (0,425 puntos cada uno): 4,25 puntos en total.
        var total = 0;
        for (var i = 1; i <= 10; i++)
        {
            pintor.Pintar(niveles, 18_000_000 + (i * 100), 18_200_000 + (i * 100), t += 90);
            total += pintor.UltimoDesplazamiento;
        }

        total.Should().Be(-4);
    }

    [Fact]
    public void Con_otro_span_o_en_otra_banda_no_se_corre()
    {
        var pintor = new PintorDelAnalizador();
        var niveles = Enumerable.Repeat((byte)60, 850).ToArray();
        pintor.Pintar(niveles, 18_000_000, 18_200_000, 90);
        pintor.Pintar(niveles, 18_050_000, 18_150_000, 180); // otro span
        pintor.UltimoDesplazamiento.Should().Be(0);
        pintor.Pintar(niveles, 7_000_000, 7_100_000, 270);   // otra banda: se limpia
        pintor.UltimoDesplazamiento.Should().Be(0);
    }

    [Fact]
    public void Sin_desplazar_la_cascada_no_se_corre()
    {
        var pintor = new PintorDelAnalizador { Desplazar = false };
        const long senal = 18_100_000;
        for (var i = 0; i < 5; i++) pintor.Pintar(ConSenal(18_000_000, 200_000, senal), 18_000_000, 18_200_000, i * 90);
        var antes = ColumnaMasClara(pintor, 3);
        pintor.Pintar(ConSenal(18_010_000, 200_000, senal), 18_010_000, 18_210_000, 900);
        ColumnaMasClara(pintor, 3).Should().Be(antes);
    }

    // ─────────────── Ajustes ───────────────

    [Fact]
    public void Los_ajustes_se_guardan_y_avisan_y_se_acotan()
    {
        var programa = new AjustesDelPrograma();
        var vm = new VistaModeloAjustesAnalizador(programa, carpeta: null);
        var avisos = 0;
        vm.Cambiado += (_, _) => avisos++;

        vm.Paleta = vm.Paletas.Single(p => p.Valor == PaletaDelAnalizador.Arcoiris);
        vm.MarcarPicos = true;
        vm.Contraste = 999;

        avisos.Should().Be(3);
        programa.Analizador.Paleta.Should().Be("Arcoiris");
        programa.Analizador.MarcarPicos.Should().BeTrue();
        programa.Analizador.Contraste.Should().Be(150);

        new AjustesDelAnalizador { PasoHz = 7, CarrilesDeSpots = 9, Paleta = "nada" }.Acotar()
            .Should().BeEquivalentTo(new { PasoHz = 0, CarrilesDeSpots = 4, Paleta = "Radio" });

        vm.DeFabricaCommand.Execute(null);
        programa.Analizador.Paleta.Should().Be("Radio");
    }

    // ─────────────── Rendimiento ───────────────

    [Fact]
    public void Pintar_una_pasada_cuesta_poco()
    {
        var trazas = TrazasReales();
        var pintor = new PintorDelAnalizador { MarcarPicos = true };
        var t = 0L;
        for (var i = 0; i < 200; i++) pintor.Pintar(trazas[i % trazas.Count].Niveles, 18_000_000 + ((i % 4) * 1000), 18_200_000 + ((i % 4) * 1000), t += 90);

        const int vueltas = 3000;
        var reloj = Stopwatch.StartNew();
        for (var i = 0; i < vueltas; i++)
        {
            var corrimiento = (i % 4) * 1000;
            pintor.Pintar(trazas[i % trazas.Count].Niveles, 18_000_000 + corrimiento, 18_200_000 + corrimiento, t += 90);
        }

        reloj.Stop();
        var porPasada = reloj.Elapsed.TotalMilliseconds / vueltas;
        salida.WriteLine($"Pintor (FAST1, picos, resintonizando cada pasada): {porPasada:0.000} ms por pasada; a 11 pasadas/s, {porPasada * 11 / 10:0.00} % de un nucleo.");
        porPasada.Should().BeLessThan(3, "a once pasadas por segundo tiene que sobrar CPU");
    }

    [Fact]
    public void Retratos_para_la_ayuda()
    {
        if (Environment.GetEnvironmentVariable("CUADERNO_CAPTURAS_ANALIZADOR") is not { Length: > 0 } carpeta) return;
        Directory.CreateDirectory(carpeta);

        EnHiloDeInterfaz(() =>
        {
            foreach (var (nombre, paleta, picos) in new[]
                     {
                         ("analizador-spots", PaletaDelAnalizador.Radio, false),
                         ("analizador-arcoiris-picos", PaletaDelAnalizador.Arcoiris, true),
                     })
            {
                var (modelo, _, _) = ModeloConControlFalso();
                modelo.Aplicar(new AjustesDelAnalizador { Paleta = paleta.ToString(), MarcarPicos = picos });
                modelo.PonerSpots(SpotsDeUnPileup());
                var tirada = Environment.GetEnvironmentVariable("CUADERNO_TRAMAS_ANALIZADOR") is { Length: > 0 } tramas
                    ? Directory.GetFiles(tramas, "*.bin").Order(StringComparer.Ordinal)
                        .Select(f => TramaDelAnalizadorFt710.IntentarDescifrar(File.ReadAllBytes(f), out var tr) ? tr : null)
                        .OfType<TrazaDeEspectro>().ToList()
                    : [.. Enumerable.Repeat(TrazasReales(), 50).SelectMany(x => x)];
                foreach (var traza in tirada) modelo.Pintar(traza);
                Guardar(modelo, Path.Combine(carpeta, nombre + ".png"), 640, 220, raton: 0.62);
            }

            // Antes y despues de resintonizar 25 kHz con la cascada corriéndose.
            {
                var (modelo, _, _) = ModeloConControlFalso();
                foreach (var traza in Enumerable.Repeat(TrazasReales(), 25).SelectMany(x => x)) modelo.Pintar(traza);
                foreach (var traza in Enumerable.Repeat(TrazasReales(), 6).SelectMany(x => x)) modelo.Pintar(Correr(traza, 25_000));

                Guardar(modelo, Path.Combine(carpeta, "analizador-resintonizado.png"), 640, 220, raton: null);
            }
        });
    }

    // ─────────────── Ayudantes ───────────────

    private static IReadOnlyList<TrazaDeEspectro> TrazasReales()
    {
        var carpeta = Path.Combine(AppContext.BaseDirectory, "Capturas");
        return Directory.GetFiles(carpeta, "espectro-ft710-*.bin")
            .Order(StringComparer.Ordinal)
            .Select(f => TramaDelAnalizadorFt710.IntentarDescifrar(File.ReadAllBytes(f), out var t) ? t! : null)
            .OfType<TrazaDeEspectro>()
            .ToList();
    }

    /// <summary>La misma traza vista con el VFO subido: lo que habria mandado la radio.</summary>
    private static TrazaDeEspectro Correr(TrazaDeEspectro traza, long hz)
    {
        var puntos = (int)(hz * traza.Niveles.Length / traza.SpanHz);
        var niveles = new byte[traza.Niveles.Length];
        for (var i = 0; i < niveles.Length; i++)
        {
            var j = i + puntos;
            niveles[i] = j < niveles.Length ? traza.Niveles[j] : traza.Niveles[i % 40];
        }

        return traza with { Niveles = niveles, VfoHz = traza.VfoHz + hz, InicioHz = traza.InicioHz + hz, FinHz = traza.FinHz + hz };
    }

    private static List<FilaDeSpot> SpotsDeUnPileup() =>
    [
        Fila("ZD7X", 18_082_000, nueva: true),
        Fila("3Y0J", 18_145_000, nueva: true),
        Fila("EA8DLF", 18_100_000),
        Fila("DL1ABC", 18_083_500),
        Fila("F5XYZ", 18_084_200, hueco: true),
        Fila("G4ABC", 18_085_100),
        Fila("I2XYZ", 18_086_000, automatico: true),
        Fila("OH2BH", 18_110_000, hueco: true),
        Fila("JA1XYZ", 18_068_500),
        Fila("K1ABC", 18_120_000),
        Fila("VK2XY", 18_160_000, automatico: true),
        Fila("PY2AA", 18_030_000),
    ];

    private static FilaDeSpot Fila(string indicativo, long hz, bool nueva = false, bool hueco = false, bool automatico = false)
    {
        var ahora = DateTimeOffset.UtcNow;
        var spot = new Spot(Indicativo.Crudo(indicativo), Frecuencia.DesdeHercios(hz), Indicativo.Crudo("EA8BFK"), null, ahora, "prueba")
        {
            ModoAnunciado = Modo.Crudo("CW"),
            EsEntidadNueva = nueva,
            EsNuevoEnBandaYModo = nueva || hueco,
            EsDeEscuchaAutomatica = automatico,
        };
        return new FilaDeSpot(new AnuncioDelCluster(spot, [new QuienLoOye(Indicativo.Crudo("EA8BFK"), "AF", null, automatico, ahora)], ahora));
    }

    private static (VistaModeloAnalizador Modelo, ControlQueApunta Control, EstadoFalso Estado) ModeloConControlFalso()
    {
        var control = new ControlQueApunta();
        var estado = new EstadoFalso();
        long ahora = 1_000;
        var modelo = new VistaModeloAnalizador(null, () => ahora += 10);
        modelo.Sintonia = new SintoniaDelAnalizador(
            control,
            () => estado.Conectado,
            () => estado.Transmitiendo,
            fila =>
            {
                estado.SpotsElegidos.Add(fila);
                return Task.CompletedTask;
            },
            () => ahora);
        modelo.Pintar(TrazasReales()[0]);
        modelo.Escala.Should().Be(new EscalaDelEspectro(18_000_000, 18_200_000));
        return (modelo, control, estado);
    }

    private static void Pico(byte[] niveles, int x, byte alto)
    {
        for (var d = -2; d <= 2; d++) niveles[x + d] = (byte)Math.Max(niveles[x + d], alto - (Math.Abs(d) * 4));
    }

    private static byte[] ConSenal(long inicio, long span, long senal)
    {
        var niveles = new byte[850];
        for (var i = 0; i < niveles.Length; i++)
        {
            var hz = inicio + ((double)i * span / niveles.Length);
            niveles[i] = (byte)(60 + (Math.Abs(hz - senal) < 300 ? 80 : 0));
        }

        return niveles;
    }

    private static int ColumnaMasClara(PintorDelAnalizador pintor, int fila)
    {
        var mejor = 0;
        var brillo = -1.0;
        for (var x = 0; x < PintorDelAnalizador.Ancho; x++)
        {
            var b = Brillo(pintor.Cascada[(fila * PintorDelAnalizador.Ancho) + x]);
            if (b > brillo) (mejor, brillo) = (x, b);
        }

        return mejor;
    }

    private static double Brillo(int color) =>
        (((color >> 16) & 0xFF) + ((color >> 8) & 0xFF) + (color & 0xFF)) / (3 * 255.0);

    private static T Esperar<T>(Task<T> tarea)
    {
        var marco = new DispatcherFrame();
        tarea.ContinueWith(_ => marco.Continue = false, TaskScheduler.Default);
        if (!tarea.IsCompleted) Dispatcher.PushFrame(marco);
        return tarea.GetAwaiter().GetResult();
    }

    private static (AnalizadorDelEquipo Control, Window Ventana) Montar(VistaModeloAnalizador modelo, double ancho, double alto)
    {
        var control = new AnalizadorDelEquipo { DataContext = modelo, Width = ancho, Height = alto };
        var marco = new Border { Background = Brushes.Black, Child = control, Width = ancho, Height = alto };
        var ventana = new Window
        {
            Content = marco,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStyle = WindowStyle.None,
            ShowActivated = false,
            ShowInTaskbar = false,
            Left = -6000,
            Top = 0,
        };
        ventana.Show();
        marco.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
        return (control, ventana);
    }

    private static void Guardar(VistaModeloAnalizador modelo, string ruta, double ancho, double alto, double? raton)
    {
        var (control, ventana) = Montar(modelo, ancho, alto);
        try
        {
            if (raton is { } x)
            {
                // La linea y la cifra que siguen al raton, como si estuviera encima.
                var metodo = typeof(AnalizadorDelEquipo).GetMethod("SeguirAlRaton", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                metodo!.Invoke(control, [x * ancho]);
            }

            var marco = (FrameworkElement)ventana.Content;
            marco.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
            const double escala = 2.0;
            var mapa = new RenderTargetBitmap((int)(ancho * escala), (int)(alto * escala), 96 * escala, 96 * escala, PixelFormats.Pbgra32);
            mapa.Render(marco);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(mapa));
            using var fichero = File.Create(ruta);
            png.Save(fichero);
        }
        finally
        {
            ventana.Close();
        }
    }

    private static void EnHiloDeInterfaz(Action accion)
    {
        Exception? fallo = null;
        var hilo = new Thread(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                accion();
            }
            catch (Exception ex)
            {
                fallo = ex;
            }
        });
        hilo.SetApartmentState(ApartmentState.STA);
        hilo.Start();
        hilo.Join();
        if (fallo is not null) throw new InvalidOperationException(fallo.Message, fallo);
    }

    /// <summary>Lo que hace el resto del programa: conectado o no, transmitiendo o no, spots elegidos.</summary>
    private sealed class EstadoFalso
    {
        public bool Conectado { get; set; } = true;

        public bool Transmitiendo { get; set; }

        public List<FilaDeSpot> SpotsElegidos { get; } = [];
    }

    /// <summary>Control de equipo falso: apunta las frecuencias que le piden y no toca ningun puerto.</summary>
    private sealed class ControlQueApunta : IControlEquipo
    {
        private TaskCompletionSource? _retenida;

        public bool Retener { get; set; }

        public List<long> Pedidas { get; } = [];

        public ViaDeControl Via => ViaDeControl.Ninguna;

        public EstadoDelEquipo Estado => EstadoDelEquipo.Desconectado;

        public event EventHandler<EstadoDelEquipo>? EstadoCambiado
        {
            add { }
            remove { }
        }

        public void Soltar() => _retenida?.TrySetResult();

        public Task ConectarAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task DesconectarAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task PonerFrecuenciaAsync(Frecuencia frecuencia, CancellationToken ct = default)
        {
            Pedidas.Add(frecuencia.Hercios);
            if (!Retener) return Task.CompletedTask;
            Retener = false;
            _retenida = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return _retenida.Task;
        }

        public List<Modo> Modos { get; } = [];

        public Task PonerModoAsync(Modo modo, CancellationToken ct = default)
        {
            Modos.Add(modo);
            return Task.CompletedTask;
        }

        public Task PonerPttAsync(bool transmitir, CancellationToken ct = default) =>
            throw new InvalidOperationException("El analizador nunca transmite.");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
