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
/// La pestaña Operar con el camino REAL del FT-710: <see cref="ControlFt710"/> detras de
/// <see cref="ControlEquipoConmutable"/>, como en el programa instalado, y un canal CAT de mentira
/// que contesta lo que contesto el equipo del operador en las capturas de docs/04-ft710-cat.md.
/// </summary>
/// <remarks>
/// Hasta ahora todo se comprobaba contra el equipo simulado, que implementa las interfaces que el
/// FT-710 real no implementaba. Aqui cada tecla del frontal se pulsa y se mira la orden CAT que
/// sale. Ninguna prueba transmite: el canal falla la prueba si alguien manda TX1, MX1 o PS0.
/// </remarks>
public sealed class OperarFt710Pruebas
{
    private static async Task<(CanalFt710DeMentira Canal, VistaModeloEquipo Equipo, ControlEquipoConmutable Conmutable)> MontarAsync()
    {
        var canal = new CanalFt710DeMentira();

        // El sondeo no llega a dar ni una vuelta: nada de reloj de pared en las pruebas.
        var control = new ControlFt710(canal, new OpcionesFt710 { IntervaloDeSondeo = TimeSpan.FromHours(1) });
        var conmutable = new ControlEquipoConmutable(control);
        var vigilante = new VigilantePtt(conmutable, new OpcionesDelVigilante { EngancharseAlCierreDelProceso = false });
        var equipo = new VistaModeloEquipo(conmutable, vigilante);

        await equipo.ConectarAsync();
        await EsperaALaVentana.DrenarAsync();
        canal.Mandadas.Clear();
        return (canal, equipo, conmutable);
    }

    private static long Hz(string texto) => TextoDeFrecuencia.Leer(texto).Hercios;

    [Fact]
    public async Task Al_conectar_el_visor_tiene_las_frecuencias_de_los_dos_vfo()
    {
        var (_, equipo, conmutable) = await MontarAsync();
        await using var _ = conmutable;

        equipo.Conectado.Should().BeTrue();
        equipo.TieneDosVfos.Should().BeTrue("el FT-710 real tiene que informar de A y B, no solo el simulado");
        Hz(equipo.A.Frecuencia).Should().Be(27555000);
        Hz(equipo.B.Frecuencia).Should().Be(7000000);
        equipo.A.FrecuenciaDelVisor.Should().NotBe("—");
        equipo.B.FrecuenciaDelVisor.Should().NotBe("—");
        equipo.A.Modo.Should().NotBe("—");
        equipo.B.Modo.Should().NotBe("—");
    }

    [Fact]
    public async Task Al_conectar_los_mandos_del_frontal_existen()
    {
        var (_, equipo, conmutable) = await MontarAsync();
        await using var _ = conmutable;

        equipo.Mandos.Should().NotBeEmpty("antes se construian con el equipo sin conectar y salian cero");
        equipo.MandoDeVolumen.Should().NotBeNull();
        equipo.MandoDeGananciaRf.Should().NotBeNull();
        equipo.MandoDeVolumen!.ValorTexto.Should().Be("89");
    }

    [Theory]
    [InlineData(MandoDeEquipo.Split, "ST1;")]
    [InlineData(MandoDeEquipo.SupresorDeRuido, "NB01;")]
    [InlineData(MandoDeEquipo.ReductorDeRuido, "NR01;")]
    [InlineData(MandoDeEquipo.Vox, "VX1;")]
    [InlineData(MandoDeEquipo.Sintonizador, "AC001;")]
    public async Task Cada_tecla_del_frontal_manda_su_orden_cat(MandoDeEquipo mando, string orden)
    {
        var (canal, equipo, conmutable) = await MontarAsync();
        await using var _ = conmutable;

        equipo.AlternarMandoCommand.CanExecute(mando).Should().BeTrue();
        await equipo.AlternarMandoCommand.ExecuteAsync(mando);
        await EsperaALaVentana.DrenarAsync();

        canal.Mandadas.Should().Contain(orden);
        canal.Mandadas.Should().NotContain("TX1;", "encender el acoplador no emite y no sube el PTT");
    }

    [Fact]
    public async Task Clar_sale_apagada_porque_el_ft710_no_admite_rt()
    {
        var (_, equipo, conmutable) = await MontarAsync();
        await using var _ = conmutable;

        equipo.AlternarMandoCommand.CanExecute(MandoDeEquipo.Rit).Should().BeFalse(
            "una tecla que no hace nada tiene que verse apagada");
    }

    [Fact]
    public async Task Los_mandos_giratorios_mandan_su_orden()
    {
        var (canal, equipo, conmutable) = await MontarAsync();
        await using var _ = conmutable;

        equipo.MandoDeVolumen!.Valor = 100;
        equipo.MandoDeGananciaRf!.Valor = 200;
        await canal.EsperarAsync("RG0200;");

        canal.Mandadas.Should().Contain("AG0100;").And.Contain("RG0200;");
    }

    [Fact]
    public async Task Mode_cambia_el_modo_del_vfo_activo()
    {
        var (canal, equipo, conmutable) = await MontarAsync();
        await using var _ = conmutable;

        await equipo.SiguienteModoCommand.ExecuteAsync(null);
        await EsperaALaVentana.DrenarAsync();
        canal.Mandadas.Should().Contain(o => o.StartsWith("MD0", StringComparison.Ordinal));

        // Con el VFO B activo, MODE toca el B. En el FT-710 MD0 es la banda PRINCIPAL, que con
        // VS1 es el B (comprobado en la radio el 29-09-2026): MD1 cambiaria el A.
        canal.Poner("VS;", "VS1;");
        await ((ControlFt710)conmutable.Actual).LeerEstadoAsync();
        await EsperaALaVentana.DrenarAsync();
        canal.Mandadas.Clear();
        await equipo.SiguienteModoCommand.ExecuteAsync(null);
        await EsperaALaVentana.DrenarAsync();
        canal.Mandadas.Should().Contain(o => o.StartsWith("MD0", StringComparison.Ordinal));
        canal.Mandadas.Should().NotContain(o => o.StartsWith("MD1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Band_lleva_el_vfo_a_la_banda_siguiente()
    {
        var (canal, equipo, conmutable) = await MontarAsync();
        await using var _ = conmutable;

        await equipo.SiguienteBandaCommand.ExecuteAsync(null);
        await EsperaALaVentana.DrenarAsync();

        canal.Mandadas.Should().Contain(o => o.StartsWith("FA", StringComparison.Ordinal) && o.Length == 12);
    }

    [Fact]
    public async Task Ab_no_intercambia_nada_sin_confirmacion_del_operador()
    {
        var (canal, equipo, conmutable) = await MontarAsync();
        await using var _ = conmutable;

        equipo.IntercambiarVfosCommand.CanExecute(null).Should().BeTrue();
        await equipo.IntercambiarVfosCommand.ExecuteAsync(null);
        await EsperaALaVentana.DrenarAsync();
        canal.Mandadas.Should().NotContain("SV;");

        equipo.ConfirmarAccion = _ => false;
        await equipo.IntercambiarVfosCommand.ExecuteAsync(null);
        await EsperaALaVentana.DrenarAsync();
        canal.Mandadas.Should().NotContain("SV;");
    }

    [Fact]
    public async Task Ab_confirmado_intercambia_y_el_visor_lo_refleja()
    {
        var (canal, equipo, conmutable) = await MontarAsync();
        await using var _ = conmutable;
        equipo.ConfirmarAccion = _ => true;

        await equipo.IntercambiarVfosCommand.ExecuteAsync(null);
        await EsperaALaVentana.DrenarAsync();

        // En el FT-710 SV no intercambia (solo cambia de VFO: visto en la radio el 28-09-2026),
        // asi que intercambiar es escribir cada VFO en el otro.
        canal.Mandadas.Should().NotContain("SV;");
        canal.Mandadas.Should().Contain(["FA007000000;", "FB027555000;"]);
        Hz(equipo.A.Frecuencia).Should().Be(7000000);
        Hz(equipo.B.Frecuencia).Should().Be(27555000);
    }

    [Fact]
    public async Task Poner_frecuencia_de_un_vfo_concreto_usa_fa_o_fb()
    {
        var (canal, equipo, conmutable) = await MontarAsync();
        await using var _ = conmutable;
        var dos = (IEquipoConDosVfos)conmutable.Actual;

        await dos.PonerFrecuenciaDeAsync(NombreDeVfo.B, Dominio.Valores.Frecuencia.DesdeHercios(14_074_000));
        await EsperaALaVentana.DrenarAsync();

        canal.Mandadas.Should().Contain("FB014074000;");
        Hz(equipo.B.Frecuencia).Should().Be(14074000);
    }

    /// <summary>
    /// Canal CAT en memoria que contesta como el FT-710 del operador y aplica las ordenes de escritura.
    /// </summary>
    private sealed class CanalFt710DeMentira : ICanalCat
    {
        private readonly ConcurrentDictionary<string, string> _respuestas = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _esperas = new(StringComparer.Ordinal);

        public CanalFt710DeMentira()
        {
            // Primera y segunda captura (docs/04-ft710-cat.md). El VFO B se pone en 7 MHz para
            // distinguirlo del A.
            foreach (var r in new[]
                     {
                         "ID0800;", "PS1;", "FA027555000;", "FB007000000;", "VS0;", "MD02;", "MD11;",
                         "SH0020;", "NA00;", "NB00;", "NB10;", "NL0000;", "NR00;", "RL001;", "BP00000;",
                         "BP01150;", "CO000000;", "CO011500;", "AG0089;", "AG1000;", "RG0255;", "SQ0000;",
                         "MG080;", "PC100;", "AC000;", "VX0;", "VG070;", "VD08;", "GT06;", "PA00;", "RA00;",
                         "KS020;", "KP40;", "BI0;", "BC00;", "SM0041;", "FT0;", "ST0;", "MC001;",
                     })
            {
                Poner(Clave(r), r);
            }

            // Respuestas cuyo prefijo no coincide con la orden (el equipo contesta con el indice 0).
            Poner("GT0;", "GT06;");
            Poner("PA0;", "PA00;");
            Poner("PA1;", "PA00;");
            Poner("RA0;", "RA00;");
            Poner("SQ0;", "SQ0000;");
            Poner("SQ1;", "SQ0000;");
            Poner("IS0;", "IS00+0000;");
            Poner("IS1;", "IS00+0000;");
            Poner("BC0;", "BC00;");
        }

        public List<string> Mandadas { get; } = [];

        public bool Abierto { get; private set; }

        public string Descripcion => "FT-710 de mentira";

        public void Poner(string consulta, string respuesta) => _respuestas[consulta] = respuesta;

        public Task AbrirAsync(CancellationToken ct = default)
        {
            Abierto = true;
            return Task.CompletedTask;
        }

        public Task<string?> PreguntarAsync(string orden, CancellationToken ct = default)
        {
            Comprobar(orden);
            // Como el canal serie de verdad: la respuesta llega sin el punto y coma final.
            return Task.FromResult<string?>(_respuestas.TryGetValue(orden, out var r) ? r.TrimEnd(';') : "?");
        }

        public Task MandarAsync(string orden, CancellationToken ct = default)
        {
            Comprobar(orden);
            lock (Mandadas) Mandadas.Add(orden);
            Aplicar(orden);
            if (_esperas.TryRemove(orden, out var espera)) espera.TrySetResult();
            return Task.CompletedTask;
        }

        public void MandarSincrono(string orden) => MandarAsync(orden).GetAwaiter().GetResult();

        public void Cerrar() => Abierto = false;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task EsperarAsync(string orden)
        {
            lock (Mandadas)
            {
                if (Mandadas.Contains(orden)) return Task.CompletedTask;
            }

            return _esperas.GetOrAdd(orden, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        }

        private static string Clave(string respuesta)
        {
            // «AG0089;» -> «AG0;»: letras y, si la orden lleva indice de VFO, ese digito.
            var letras = new string(respuesta.TakeWhile(char.IsAsciiLetter).ToArray());
            var conIndice = letras is "MD" or "SH" or "NA" or "NB" or "NL" or "NR" or "RL" or "AG" or "RG" or "SM";
            var conDosIndices = letras is "BP" or "CO";
            var largo = letras.Length + (conDosIndices ? 2 : conIndice ? 1 : 0);
            return respuesta[..largo] + ";";
        }

        private static void Comprobar(string orden)
        {
            // Ninguna prueba de la pestaña Operar puede poner el equipo en el aire ni apagarlo.
            new[] { "TX1;", "TX2;", "MX1;", "PS0;" }.Should().NotContain(orden);
        }

        private void Aplicar(string orden)
        {
            switch (orden)
            {
                case "SV;":
                    (_respuestas["FA;"], _respuestas["FB;"]) =
                        ("FA" + _respuestas["FB;"][2..], "FB" + _respuestas["FA;"][2..]);
                    return;
                case "AB;":
                    _respuestas["FB;"] = "FB" + _respuestas["FA;"][2..];
                    return;
                case "BA;":
                    _respuestas["FA;"] = "FA" + _respuestas["FB;"][2..];
                    return;
            }

            // Una orden de escritura deja su valor como respuesta a la consulta mas larga que
            // le sirve de prefijo: «AG0100;» -> «AG0;» contesta «AG0100;».
            var cuerpo = orden.TrimEnd(';');
            var consulta = _respuestas.Keys
                .Select(k => k.TrimEnd(';'))
                .Where(k => cuerpo.StartsWith(k, StringComparison.Ordinal) && cuerpo.Length > k.Length)
                .OrderByDescending(k => k.Length)
                .FirstOrDefault();

            if (consulta is not null) _respuestas[consulta + ";"] = orden;
        }
    }
}
