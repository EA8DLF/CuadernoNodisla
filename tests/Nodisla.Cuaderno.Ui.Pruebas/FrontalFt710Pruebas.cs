using System.Collections.Concurrent;
using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Radio.Control;
using Nodisla.Cuaderno.Radio.Control.Ft710;
using Nodisla.Cuaderno.Radio.Ptt;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// Cada tecla, dial y mando del frontal dibujado, con el camino real del FT-710 y las
/// respuestas REALES de la radio de Jose (<c>Capturas/ft710-2026-09-28.tsv</c>).
/// </summary>
/// <remarks>
/// Las ordenes que se esperan son las que se mandaron a la radio el 28-09-2026 pulsando estos
/// mismos comandos (docs/15-botones-ft710-validados.md). El canal hace fallar la prueba si algo
/// que no sea MOX o TUNE pone el equipo en antena.
/// </remarks>
public sealed partial class FrontalFt710Pruebas
{
    private static readonly TimeSpan PlazoDeSeguridad = TimeSpan.FromSeconds(10);

    private static async Task<(CanalReal Canal, VistaModeloEquipo Equipo, ControlEquipoConmutable Conmutable)> MontarAsync(bool puedeTransmitir = false)
    {
        var canal = new CanalReal { PuedeTransmitir = puedeTransmitir };
        var control = new ControlFt710(canal, new OpcionesFt710
        {
            IntervaloDeSondeo = TimeSpan.FromHours(1),
            Esperar = (_, _) => Task.CompletedTask,
        });
        var conmutable = new ControlEquipoConmutable(control);
        var vigilante = new VigilantePtt(conmutable, new OpcionesDelVigilante { EngancharseAlCierreDelProceso = false });
        var equipo = new VistaModeloEquipo(conmutable, vigilante)
        {
            ConfirmarAccion = _ => true,
            ConfirmarQueVaATransmitir = _ => true,
        };

        await equipo.ConectarAsync();
        await EsperaALaVentana.DrenarAsync();
        canal.Mandadas.Clear();
        return (canal, equipo, conmutable);
    }

    [Fact]
    public async Task Al_conectar_estan_todos_los_mandos_del_frontal_con_lo_que_tenia_la_radio()
    {
        var (_, equipo, conmutable) = await MontarAsync();
        await using var _ = conmutable;

        equipo.Frontal["Bloqueo"]!.Encendido.Should().BeFalse();
        equipo.Frontal["Sintonizador"]!.Encendido.Should().BeTrue("AC001");
        equipo.Frontal["EspectroAncho"]!.ValorTexto.Should().Be("200 kHz");
        equipo.Frontal["EspectroModo"]!.ValorTexto.Should().Be("W/F CENTER");
        equipo.RotuloDelFunc.Should().Be("RF POWER");
        equipo.MandoDelFunc!.Mando.Should().Be(MandoDeEquipo.Potencia);
        equipo.RotuloDelDsp.Should().Be("SHIFT");
        equipo.MandoDelDsp!.Mando.Should().Be(MandoDeEquipo.DesplazamientoFi);
        equipo.Frontal["Silenciador"].Should().NotBeNull("es el centro del RF GAIN/SQL");
    }

    [Theory]
    [InlineData(MandoDeEquipo.Bloqueo, "LK1;")]
    [InlineData(MandoDeEquipo.Vox, "VX1;")]
    [InlineData(MandoDeEquipo.Split, "ST1;")]
    [InlineData(MandoDeEquipo.Rit, "CF00010000;")]
    [InlineData(MandoDeEquipo.SupresorDeRuido, "NB01;")]
    [InlineData(MandoDeEquipo.ReductorDeRuido, "NR01;")]
    [InlineData(MandoDeEquipo.FiltroEstrecho, "NA01;")]
    [InlineData(MandoDeEquipo.TonoDeReferenciaCw, "CS1;")]
    [InlineData(MandoDeEquipo.Sintonizador, "AC000;")]
    public async Task Cada_tecla_de_interruptor_manda_lo_que_se_mando_a_la_radio(MandoDeEquipo mando, string orden)
    {
        var (canal, equipo, conmutable) = await MontarAsync();
        await using var _ = conmutable;

        equipo.AlternarMandoCommand.CanExecute(mando).Should().BeTrue();
        await equipo.AlternarMandoCommand.ExecuteAsync(mando);
        await canal.EsperarAsync(orden);
    }

    [Theory]
    [InlineData(TeclaDelEquipo.MemoriaAVfo, "MA;")]
    [InlineData(TeclaDelEquipo.VfoOMemoria, "VM;")]
    [InlineData(TeclaDelEquipo.AlternarVfo, "SV;")]
    [InlineData(TeclaDelEquipo.BandaArriba, "BU0;")]
    [InlineData(TeclaDelEquipo.BandaAbajo, "BD0;")]
    [InlineData(TeclaDelEquipo.RecuperarMemoriaRapida, "QR;")]
    [InlineData(TeclaDelEquipo.GuardarMemoriaRapida, "QI;")]
    [InlineData(TeclaDelEquipo.AjusteACero, "ZI0;")]
    [InlineData(TeclaDelEquipo.BorrarClarificador, "CF001+0000;")]
    [InlineData(TeclaDelEquipo.RestablecerDsp, "SH0000;")]
    public async Task Cada_tecla_de_accion_manda_su_orden(TeclaDelEquipo tecla, string orden)
    {
        var (canal, equipo, conmutable) = await MontarAsync();
        await using var _ = conmutable;

        equipo.PulsarTeclaCommand.CanExecute(tecla).Should().BeTrue();
        await equipo.PulsarTeclaCommand.ExecuteAsync(tecla);
        await canal.EsperarAsync(orden);
    }

    [Fact]
    public async Task Guardar_en_la_qmb_pide_confirmacion()
    {
        var (canal, equipo, conmutable) = await MontarAsync();
        await using var _ = conmutable;
        equipo.ConfirmarAccion = _ => false;

        await equipo.PulsarTeclaCommand.ExecuteAsync(TeclaDelEquipo.GuardarMemoriaRapida);

        canal.Mandadas.Should().NotContain("QI;");
    }

    [Fact]
    public async Task A_b_del_frontal_cambia_de_vfo_sin_preguntar_y_el_visor_lo_ve()
    {
        var (canal, equipo, conmutable) = await MontarAsync();
        await using var _ = conmutable;
        equipo.ConfirmarAccion = null;

        await equipo.PulsarTeclaCommand.ExecuteAsync(TeclaDelEquipo.AlternarVfo);
        await EsperaALaVentana.DrenarAsync();

        canal.Mandadas.Should().Contain("SV;");
        equipo.B.EsElActivo.Should().BeTrue("la radio contesta VS1 tras SV");
    }

    [Fact]
    public async Task Fine_fast_da_la_vuelta_normal_fine_fast()
    {
        var (canal, equipo, conmutable) = await MontarAsync();
        await using var _ = conmutable;

        foreach (var orden in new[] { "FN1;", "FN2;", "FN0;" })
        {
            equipo.SiguientePosicionCommand.Execute(MandoDeEquipo.SintoniaFinaRapida);
            await canal.EsperarAsync(orden);
        }
    }

    [Fact]
    public async Task El_dial_y_step_mch_mueven_la_frecuencia()
    {
        var (canal, equipo, conmutable) = await MontarAsync();
        await using var _ = conmutable;

        // 14.155.000 y paso de 20 Hz: una muesca son 200 Hz; STEP/MCH, 5 kHz a la rejilla.
        await equipo.GirarDialAsync(1);
        await canal.EsperarAsync("FA014155200;");

        await equipo.GirarPasosAsync(1);
        await canal.EsperarAsync("FA014160000;");
    }

    [Fact]
    public async Task Func_cambia_de_funcion_y_su_mando_ajusta_la_nueva()
    {
        var (canal, equipo, conmutable) = await MontarAsync();
        await using var _ = conmutable;

        equipo.CambiarFuncionCommand.Execute("FUNC+");
        await canal.EsperarAsync("SF0E;");
        equipo.RotuloDelFunc.Should().Be("MONI LEVEL");
        equipo.MandoDelFunc!.Mando.Should().Be(MandoDeEquipo.Monitor);

        equipo.MandoDelFunc.Valor += 1;
        await canal.EsperarAsync("ML1001;");

        equipo.CambiarFuncionCommand.Execute("DSP+");
        await canal.EsperarAsync("SF12;");
        equipo.MandoDelDsp!.Mando.Should().Be(MandoDeEquipo.AnchoDeFiltro);
    }

    [Fact]
    public async Task Func_se_salta_m_group_porque_no_tiene_orden_cat()
    {
        var (canal, equipo, conmutable) = await MontarAsync();
        await using var _ = conmutable;
        equipo.Frontal["FuncionDelMandoFunc"]!.Valor = 5; // DIMMER
        await canal.EsperarAsync("SF05;");

        equipo.CambiarFuncionCommand.Execute("FUNC+");

        await canal.EsperarAsync("SF07;");
        canal.Mandadas.Should().NotContain("SF06;");
    }

    [Theory]
    [InlineData("CENTER", "SS0670000;")]
    [InlineData("EXPAND", "SS0630000;")]
    [InlineData("3DSS", "SS0600000;")]
    [InlineData("SPAN+", "SS0580000;")]
    [InlineData("SPAN-", "SS0560000;")]
    [InlineData("SPEED+", "SS0030000;")]
    [InlineData("SPEED-", "SS0010000;")]
    public async Task Las_teclas_de_la_pantalla_mandan_su_orden_del_analizador(string tecla, string orden)
    {
        var (canal, equipo, conmutable) = await MontarAsync();
        await using var _ = conmutable;

        equipo.TeclaDelAnalizadorCommand.CanExecute(tecla).Should().BeTrue();
        equipo.TeclaDelAnalizadorCommand.Execute(tecla);

        await canal.EsperarAsync(orden);
    }

    [Fact]
    public async Task Expand_se_lee_ampliado_aunque_la_radio_conteste_otro_codigo_y_desde_3dss_pasa_a_la_cascada_ampliada()
    {
        var (canal, equipo, conmutable) = await MontarAsync();
        await using var _ = conmutable;

        equipo.TeclaDelAnalizadorCommand.Execute("EXPAND");
        await canal.EsperarAsync("SS0630000;");
        await equipo.Frontal["EspectroModo"]!.RecogerAsync();
        equipo.AnalizadorAmpliado.Should().BeTrue("la radio contesta SS0650000 y es W/F CENTER EXPAND");

        equipo.TeclaDelAnalizadorCommand.Execute("3DSS");
        await canal.EsperarAsync("SS0600000;");
        equipo.AnalizadorEnTresD.Should().BeTrue();

        // El FT-710 no tiene 3DSS ampliado por CAT: EXPAND lleva a la cascada ampliada.
        canal.Mandadas.Clear();
        equipo.TeclaDelAnalizadorCommand.CanExecute("EXPAND").Should().BeTrue();
        equipo.TeclaDelAnalizadorCommand.Execute("EXPAND");
        await canal.EsperarAsync("SS0630000;");
        equipo.AnalizadorEnTresD.Should().BeFalse();
        equipo.AnalizadorAmpliado.Should().BeTrue();
    }

    [Fact]
    public async Task Lo_que_se_toca_en_la_radio_se_ve_en_el_frontal()
    {
        var (canal, equipo, conmutable) = await MontarAsync();
        await using var _ = conmutable;

        // Como si Jose pulsara las teclas del equipo: cambia lo que la radio contesta.
        canal.Poner("NB0;", "NB01;");
        canal.Poner("LK;", "LK1;");
        canal.Poner("NA0;", "NA01;");
        canal.Poner("SS05;", "SS0560000;");
        canal.Poner("SS06;", "SS0600000;");
        canal.Poner("SF0;", "SF07;");
        canal.Poner("FN;", "FN2;");
        canal.Poner("IF;", "IF000014155000+005010200000;");
        canal.Poner("CF000;", "CF00010000;");

        await ((ControlFt710)conmutable.Actual).LeerEstadoAsync();
        await equipo.RefrescarElFrontalAsync();
        await EsperaALaVentana.DrenarAsync();

        equipo.Frontal["SupresorDeRuido"]!.Encendido.Should().BeTrue();
        equipo.Frontal["Bloqueo"]!.Encendido.Should().BeTrue();
        equipo.Frontal["FiltroEstrecho"]!.Encendido.Should().BeTrue();
        equipo.Frontal["EspectroAncho"]!.ValorTexto.Should().Be("100 kHz");
        equipo.AnalizadorEnTresD.Should().BeTrue();
        equipo.AjusteDelAnalizador.Should().Be(new AjusteDelAnalizador(100_000, 2, ModoDelAnalizador.Centro, TresD: true, Ampliado: false));
        equipo.RotuloDelFunc.Should().Be("MIC GAIN");
        equipo.Frontal["SintoniaFinaRapida"]!.ValorTexto.Should().Be("Rápida (FAST)");
        equipo.Frontal["Rit"]!.Encendido.Should().BeTrue();
        equipo.TextoDeRit.Should().Be("RIT +50 Hz");
    }

    [Fact]
    public async Task El_visor_pone_en_grande_el_vfo_activo_y_sigue_al_boton_ab_de_la_radio()
    {
        var (canal, equipo, conmutable) = await MontarAsync();
        await using var _ = conmutable;
        equipo.Principal.Should().BeSameAs(equipo.A);

        // A/B pulsado en la radio: el siguiente sondeo lo trae (respuestas reales, 29-09-2026).
        canal.Poner("VS;", "VS1;");
        canal.Poner("FB;", "FB027555000;");
        canal.Poner("MD0;", "MD02;");
        canal.Poner("MD1;", "MD12;");
        canal.Poner("FT;", "FT0;");
        await ((ControlFt710)conmutable.Actual).LeerEstadoAsync();
        await EsperaALaVentana.DrenarAsync();

        equipo.Principal.Should().BeSameAs(equipo.B);
        equipo.Secundario.Should().BeSameAs(equipo.A);
        equipo.B.EsElActivo.Should().BeTrue();
        equipo.Vfo.Should().Be("VFO B");
        equipo.Frecuencia.Should().Be(TextoDeFrecuencia.Escribir(Dominio.Valores.Frecuencia.DesdeHercios(27_555_000)));
    }

    [Fact]
    public async Task La_rueda_sobre_clar_mueve_el_desplazamiento()
    {
        var (canal, equipo, conmutable) = await MontarAsync();
        await using var _ = conmutable;

        equipo.MoverClarificador(3);

        await canal.EsperarAsync("CF001+0030;");
    }

    [Fact]
    public async Task Ningun_boton_del_frontal_transmite()
    {
        // El canal hace fallar la prueba en cuanto sale TX1, MX1 o AC003.
        var (canal, equipo, conmutable) = await MontarAsync();
        await using var _ = conmutable;

        foreach (var mando in new[]
                 {
                     MandoDeEquipo.Bloqueo, MandoDeEquipo.Vox, MandoDeEquipo.Split, MandoDeEquipo.Rit,
                     MandoDeEquipo.SupresorDeRuido, MandoDeEquipo.ReductorDeRuido, MandoDeEquipo.FiltroEstrecho,
                     MandoDeEquipo.TonoDeReferenciaCw, MandoDeEquipo.Sintonizador,
                 })
        {
            await equipo.AlternarMandoCommand.ExecuteAsync(mando);
            await equipo.AlternarMandoCommand.ExecuteAsync(mando);
        }

        foreach (var tecla in Enum.GetValues<TeclaDelEquipo>())
        {
            await equipo.PulsarTeclaCommand.ExecuteAsync(tecla);
        }

        foreach (var t in new[] { "CENTER", "3DSS", "EXPAND", "SPAN+", "SPAN-", "SPEED+", "SPEED-" })
        {
            if (equipo.TeclaDelAnalizadorCommand.CanExecute(t)) equipo.TeclaDelAnalizadorCommand.Execute(t);
        }

        equipo.CambiarFuncionCommand.Execute("FUNC+");
        equipo.CambiarFuncionCommand.Execute("DSP+");
        equipo.SiguientePosicionCommand.Execute(MandoDeEquipo.SintoniaFinaRapida);
        await equipo.GirarDialAsync(2);
        await equipo.GirarPasosAsync(-1);
        await equipo.SiguienteModoCommand.ExecuteAsync(null);
        await EsperaALaVentana.DrenarAsync();

        canal.Mandadas.Should().NotBeEmpty();
        canal.Mandadas.Should().NotContain(o => o.StartsWith("TX1", StringComparison.Ordinal)
                                                || o.StartsWith("MX1", StringComparison.Ordinal)
                                                || o.StartsWith("AC003", StringComparison.Ordinal));
        equipo.Transmitiendo.Should().BeFalse();
    }

    [Fact]
    public async Task Mox_sube_y_baja_el_ptt_por_el_vigilante()
    {
        var (canal, equipo, conmutable) = await MontarAsync(puedeTransmitir: true);
        await using var _ = conmutable;

        await equipo.MoxCommand.ExecuteAsync(null);
        equipo.EnMox.Should().BeTrue();
        await canal.EsperarAsync("TX1;");

        await equipo.MoxCommand.ExecuteAsync(null);
        equipo.EnMox.Should().BeFalse();
        await canal.EsperarAsync("TX0;");
        canal.Mandadas.Last().Should().Be("TX0;", "lo ultimo que sale tras quitar MOX es bajar el PTT");
    }

    [Fact]
    public async Task Mox_sin_confirmacion_no_transmite()
    {
        var (canal, equipo, conmutable) = await MontarAsync();
        await using var _ = conmutable;
        equipo.ConfirmarQueVaATransmitir = _ => false;

        await equipo.MoxCommand.ExecuteAsync(null);

        equipo.EnMox.Should().BeFalse();
        canal.Mandadas.Should().BeEmpty();
    }

    [Fact]
    public async Task Tune_mantenida_sintoniza_por_el_vigilante_y_deja_el_ptt_abajo()
    {
        var (canal, equipo, conmutable) = await MontarAsync(puedeTransmitir: true);
        await using var _ = conmutable;

        // Lo que contesto RI0; en la radio mientras sintonizaba, y al final ceros.
        canal.PonerSecuencia("RI0;", ["RI00010100;", "RI01010100;", "RI00010100;", "RI00000000;"]);

        var termino = await equipo.SintonizarAsync(TimeSpan.FromSeconds(10));

        termino.Should().BeTrue();
        canal.Mandadas.Should().ContainInOrder("AC003;", "TX0;", "AC001;");
        canal.Mandadas.Should().NotContain("TX1;", "TUNE la hace el acoplador, no el CAT");
    }

    /// <summary>
    /// Canal en memoria con las respuestas reales del 28-09-2026 y las reglas vistas en la radio.
    /// </summary>
    private sealed class CanalReal : ICanalCat
    {
        private readonly ConcurrentDictionary<string, string> _respuestas = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _esperas = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, Queue<string>> _secuencias = new(StringComparer.Ordinal);

        public CanalReal()
        {
            var ruta = System.IO.Path.Combine(AppContext.BaseDirectory, "Capturas", "ft710-2026-09-28.tsv");
            foreach (var linea in System.IO.File.ReadAllLines(ruta))
            {
                if (linea.Length == 0 || linea.StartsWith('#')) continue;
                var partes = linea.Split('\t');
                _respuestas[partes[0]] = partes[1];
            }

            _respuestas["ID;"] = "ID0800;";
        }

        public bool PuedeTransmitir { get; init; }

        public List<string> Mandadas { get; } = [];

        public bool Abierto { get; private set; }

        public string Descripcion => "FT-710 con respuestas reales";

        public void Poner(string consulta, string respuesta) => _respuestas[consulta] = respuesta;

        public void PonerSecuencia(string consulta, IEnumerable<string> respuestas) =>
            _secuencias[consulta] = new Queue<string>(respuestas);

        public Task AbrirAsync(CancellationToken ct = default)
        {
            Abierto = true;
            return Task.CompletedTask;
        }

        public Task<string?> PreguntarAsync(string orden, CancellationToken ct = default)
        {
            Comprobar(orden);
            if (_secuencias.TryGetValue(orden, out var cola) && cola.TryDequeue(out var siguiente))
            {
                if (cola.Count == 0) _respuestas[orden] = siguiente;
                return Task.FromResult<string?>(siguiente.TrimEnd(';'));
            }

            return Task.FromResult<string?>(_respuestas.TryGetValue(orden, out var r) ? r.TrimEnd(';') : "?");
        }

        public Task MandarAsync(string orden, CancellationToken ct = default)
        {
            // Se apunta ANTES de comprobar: si el modelo se traga la excepcion, la prueba la ve igual.
            lock (Mandadas) Mandadas.Add(orden);
            Comprobar(orden);
            Aplicar(orden);
            if (_esperas.TryRemove(orden, out var espera)) espera.TrySetResult();
            return Task.CompletedTask;
        }

        public void MandarSincrono(string orden) => MandarAsync(orden).GetAwaiter().GetResult();

        public void Cerrar() => Abierto = false;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public async Task EsperarAsync(string orden)
        {
            Task espera;
            lock (Mandadas)
            {
                if (Mandadas.Contains(orden)) return;
                espera = _esperas.GetOrAdd(orden, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
            }

            await espera.WaitAsync(PlazoDeSeguridad);
        }

        private void Comprobar(string orden)
        {
            if (orden.StartsWith("PS0", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("PS0 apagaria la radio.");
            }

            if (!PuedeTransmitir
                && (orden.StartsWith("TX1", StringComparison.Ordinal)
                    || orden.StartsWith("TX2", StringComparison.Ordinal)
                    || orden.StartsWith("MX1", StringComparison.Ordinal)
                    || orden.StartsWith("AC003", StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"«{orden}» pone el equipo en antena y esta prueba no transmite.");
            }
        }

        private void Aplicar(string orden)
        {
            var cuerpo = orden.TrimEnd(';');
            switch (cuerpo)
            {
                case "SV":
                    // En la radio real, SV cambia el VFO con el que se opera (VS0 <-> VS1).
                    _respuestas["VS;"] = _respuestas["VS;"] == "VS1;" ? "VS0;" : "VS1;";
                    return;
                case ['S', 'S', '0', '6', var modo, ..]:
                    // Los modos ampliados se leen 5/8/B aunque se escriban 3/6/9 (visto en la radio).
                    var leido = modo switch { '3' => '5', '6' => '8', '9' => 'B', _ => modo };
                    _respuestas["SS06;"] = $"SS06{leido}0000;";
                    return;
                case ['F', 'A' or 'B', ..] when cuerpo.Length == 11:
                    _respuestas[cuerpo[..2] + ";"] = orden;
                    if (cuerpo[1] == 'A') _respuestas["IF;"] = $"IF000{cuerpo[2..]}+000000200000;";
                    return;
            }

            if (cuerpo.StartsWith("TX", StringComparison.Ordinal)) return;

            var consulta = _respuestas.Keys
                .Select(k => k.TrimEnd(';'))
                .Where(k => cuerpo.StartsWith(k, StringComparison.Ordinal) && cuerpo.Length > k.Length)
                .OrderByDescending(k => k.Length)
                .FirstOrDefault();

            if (consulta is not null) _respuestas[consulta + ";"] = orden;
        }
    }
}
