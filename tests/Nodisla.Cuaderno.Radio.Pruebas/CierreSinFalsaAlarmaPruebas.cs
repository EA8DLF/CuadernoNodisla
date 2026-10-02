using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Radio.Control;
using Nodisla.Cuaderno.Radio.Control.Ft710;
using Nodisla.Cuaderno.Radio.Control.Icom;
using Nodisla.Cuaderno.Radio.Control.Yaesu;
using Nodisla.Cuaderno.Radio.Ptt;
using Nodisla.Cuaderno.Radio.Pruebas.Dobles;
using Nodisla.Cuaderno.Radio.Pruebas.Icom;

namespace Nodisla.Cuaderno.Radio.Pruebas;

/// <summary>
/// Cerrar el Cuaderno no puede dar un «¡PTT PEGADO!» falso, y tampoco puede dejar de bajar un
/// PTT que de verdad esta arriba.
/// </summary>
/// <remarks>
/// <para>
/// El fallo (01-10-2026): en modo real, cerrar sin haber conectado nunca el equipo apuntaba
/// «¡PTT PEGADO! Han fallado todas las vías…» con una <see cref="ObjectDisposedException"/>
/// del semaforo del canal. El contenedor de servicios desecha el intermediario
/// (<see cref="ControlEquipoConmutable"/>) <b>antes</b> que el vigilante —lo tiene apuntado
/// tres veces, una por cada alias, y el ultimo alias que se pide queda el primero en cerrarse—,
/// asi que el vigilante, al cerrarse, mandaba bajar el PTT por un canal ya liberado.
/// </para>
/// <para>
/// Por eso cada prueba se hace en los dos ordenes de cierre: el intermediario antes que el
/// vigilante (lo que pasa en la aplicacion) y el vigilante antes (lo que deberia pasar).
/// </para>
/// </remarks>
public class CierreSinFalsaAlarmaPruebas
{
    private static OpcionesDelVigilante OpcionesDelVigilante() => new()
    {
        TiempoMaximo = TimeSpan.FromSeconds(60),
        TiempoSinLatido = TimeSpan.FromSeconds(60),
        PasoDeVigilancia = TimeSpan.FromMilliseconds(5),
        EsperaDeSuelta = TimeSpan.FromMilliseconds(500),
        EngancharseAlCierreDelProceso = false,
    };

    private static OpcionesFt710 OpcionesDelEquipo() => new()
    {
        IntervaloDeSondeo = TimeSpan.FromHours(1),
        EsperaDeOrden = TimeSpan.FromMilliseconds(500),
        EsperaDeReconexion = TimeSpan.FromMilliseconds(50),
    };

    /// <summary>Lo que se monta en la aplicacion: el control, el intermediario y el vigilante.</summary>
    private sealed class Montaje
    {
        internal Montaje(IControlEquipo control, RegistroDeMentira registro)
        {
            Registro = registro;
            Conmutable = new ControlEquipoConmutable(control, registro);
            Vigilante = new VigilantePtt(Conmutable, OpcionesDelVigilante(), registro);
            Vigilante.PttPegado += (_, pegado) => Pegados.Enqueue(pegado);
        }

        internal RegistroDeMentira Registro { get; }

        internal ControlEquipoConmutable Conmutable { get; }

        internal VigilantePtt Vigilante { get; }

        internal ConcurrentQueue<PttPegadoException> Pegados { get; } = new();

        /// <summary>Cierra como la aplicacion, en el orden que se pida.</summary>
        internal async Task CerrarAsync(bool conmutableAntes)
        {
            if (conmutableAntes)
            {
                await Conmutable.DisposeAsync();
                await Vigilante.DisposeAsync();
            }
            else
            {
                await Vigilante.DisposeAsync();
                await Conmutable.DisposeAsync();
            }
        }

        internal IReadOnlyList<string> Avisos() =>
            Registro.De(LogLevel.Warning).Concat(Registro.De(LogLevel.Error)).Concat(Registro.De(LogLevel.Critical)).ToList();
    }

    // ── FT-710 ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cerrar_sin_haber_conectado_no_da_alarma_ni_toca_el_equipo(bool conmutableAntes)
    {
        await using var equipo = new Ft710DeMentira();
        var canal = new CanalTcpCat("127.0.0.1", equipo.Puerto, TimeSpan.FromMilliseconds(500));
        var montaje = new Montaje(new ControlFt710(canal, OpcionesDelEquipo()), new RegistroDeMentira());

        await montaje.CerrarAsync(conmutableAntes);

        montaje.Pegados.Should().BeEmpty("sin canal abierto ni PTT pedido no hay nada que se haya podido quedar pegado");
        montaje.Avisos().Should().BeEmpty();
        equipo.Recibidas.Should().BeEmpty("no se abre un puerto que nadie abrió solo para mandarle TX0");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task Cerrar_con_el_canal_abierto_y_el_ptt_abajo_no_da_alarma(bool conmutableAntes, bool habiaTransmitido)
    {
        await using var equipo = new Ft710DeMentira();
        var canal = new CanalTcpCat("127.0.0.1", equipo.Puerto, TimeSpan.FromMilliseconds(500));
        var control = new ControlFt710(canal, OpcionesDelEquipo());
        var montaje = new Montaje(control, new RegistroDeMentira());
        await montaje.Conmutable.ConectarAsync();

        if (habiaTransmitido)
        {
            await using (await montaje.Vigilante.PedirAntenaAsync("prueba"))
            {
                await equipo.EsperarAntenaAsync(true, TimeSpan.FromSeconds(5));
            }

            await equipo.EsperarAntenaAsync(false, TimeSpan.FromSeconds(5));
        }

        await montaje.CerrarAsync(conmutableAntes);

        montaje.Pegados.Should().BeEmpty();
        montaje.Registro.De(LogLevel.Error).Should().BeEmpty();
        equipo.EnAntena.Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cerrar_con_el_ptt_arriba_lo_baja(bool conmutableAntes)
    {
        await using var equipo = new Ft710DeMentira();
        var canal = new CanalTcpCat("127.0.0.1", equipo.Puerto, TimeSpan.FromMilliseconds(500));
        var montaje = new Montaje(new ControlFt710(canal, OpcionesDelEquipo()), new RegistroDeMentira());
        await montaje.Conmutable.ConectarAsync();

        _ = await montaje.Vigilante.PedirAntenaAsync("prueba");
        await equipo.EsperarAntenaAsync(true, TimeSpan.FromSeconds(5));
        equipo.EnAntena.Should().BeTrue();

        await montaje.CerrarAsync(conmutableAntes);

        await equipo.EsperarAntenaAsync(false, TimeSpan.FromSeconds(5));
        equipo.EnAntena.Should().BeFalse("cerrar con el equipo en antena es justo lo que no puede pasar");
        if (!conmutableAntes)
        {
            montaje.Pegados.Should().BeEmpty("la bajada ha funcionado");
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cerrar_con_el_ptt_arriba_y_el_canal_roto_avisa_de_ptt_pegado(bool conmutableAntes)
    {
        var canal = new CanalCatRompible();
        await canal.AbrirAsync();
        var montaje = new Montaje(new ControlFt710(canal, OpcionesDelEquipo()), new RegistroDeMentira());

        _ = await montaje.Vigilante.PedirAntenaAsync("prueba");
        canal.Mandadas.Should().Contain("TX1;");
        canal.Roto = true;

        await montaje.CerrarAsync(conmutableAntes);

        // Con el PTT de verdad arriba, que el canal acabe liberado (ObjectDisposedException) no
        // puede tapar la alarma: hay que apagar el equipo a mano.
        montaje.Pegados.Should().NotBeEmpty();
        montaje.Registro.De(LogLevel.Error).Should()
            .Contain(mensaje => mensaje.Contains("PTT PEGADO", StringComparison.Ordinal));
    }

    // ── El vigilante solo ───────────────────────────────────────────────────

    [Fact]
    public async Task Un_control_cerrado_por_el_que_nunca_se_transmitio_no_es_un_ptt_pegado()
    {
        var control = new ControlYaCerrado { Cerrado = true };
        var registro = new RegistroDeMentira();
        var vigilante = new VigilantePtt(control, OpcionesDelVigilante(), registro);
        var pegados = 0;
        vigilante.PttPegado += (_, _) => Interlocked.Increment(ref pegados);

        await vigilante.DisposeAsync();

        pegados.Should().Be(0);
        registro.De(LogLevel.Error).Should().BeEmpty();
        registro.De(LogLevel.Warning).Should().BeEmpty();
    }

    [Fact]
    public async Task Si_se_transmitio_un_control_cerrado_sigue_siendo_ptt_pegado()
    {
        var control = new ControlYaCerrado();
        var registro = new RegistroDeMentira();
        await using var vigilante = new VigilantePtt(control, OpcionesDelVigilante(), registro);
        var pegados = 0;
        vigilante.PttPegado += (_, _) => Interlocked.Increment(ref pegados);

        var transmision = await vigilante.PedirAntenaAsync("prueba");
        control.PttArriba.Should().BeTrue();
        control.Cerrado = true;

        var soltar = async () => await transmision.DisposeAsync();

        await soltar.Should().ThrowAsync<PttPegadoException>();
        pegados.Should().BeGreaterThan(0);
    }

    // ── Icom y Yaesu binario: el mismo camino ───────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Icom_cerrar_sin_haber_conectado_no_da_alarma_ni_abre_el_puerto(bool conmutableAntes)
    {
        var radio = new IcomDeMentira(ModelosIcom.Todos.First());
        var canal = new CanalCivDeMentira(radio);
        var opciones = new OpcionesFt710
        {
            IntervaloDeSondeo = TimeSpan.FromHours(1),
            EsperaDeOrden = TimeSpan.FromMilliseconds(300),
            Esperar = (_, _) => Task.CompletedTask,
        };
        var montaje = new Montaje(new ControlIcom(canal, radio.Perfil, opciones), new RegistroDeMentira());

        await montaje.CerrarAsync(conmutableAntes);

        montaje.Pegados.Should().BeEmpty();
        montaje.Avisos().Should().BeEmpty();
        canal.Escrito.Should().BeEmpty();
        canal.Abierto.Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Yaesu_binario_cerrar_sin_haber_conectado_no_da_alarma_ni_abre_el_puerto(bool conmutableAntes)
    {
        var canal = new CanalBinarioConCandado();
        var control = new ControlYaesuBinario(
            canal,
            ProveedorYaesuBinario.Ft817,
            new OpcionesFt710 { IntervaloDeSondeo = TimeSpan.FromHours(1) });
        var montaje = new Montaje(control, new RegistroDeMentira());

        await montaje.CerrarAsync(conmutableAntes);

        montaje.Pegados.Should().BeEmpty();
        montaje.Avisos().Should().BeEmpty();
        canal.Aperturas.Should().Be(0);
        canal.Mandados.Should().Be(0);
    }

    // ── Dobles ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Canal CAT que se porta como el serie de verdad: semaforo que se libera al desecharlo,
    /// nada sale por un canal cerrado y, roto, todo falla.
    /// </summary>
    private sealed class CanalCatRompible : ICanalCat
    {
        private readonly SemaphoreSlim _puerta = new(1, 1);
        private readonly ConcurrentQueue<string> _mandadas = new();

        internal bool Roto { get; set; }

        internal IReadOnlyCollection<string> Mandadas => _mandadas;

        public bool Abierto { get; private set; }

        public string Descripcion => "canal rompible";

        public async Task AbrirAsync(CancellationToken ct = default)
        {
            await _puerta.WaitAsync(ct);
            try
            {
                if (Roto) throw new IOException("El cable se ha soltado.");
                Abierto = true;
            }
            finally
            {
                _puerta.Release();
            }
        }

        public async Task<string?> PreguntarAsync(string orden, CancellationToken ct = default)
        {
            await MandarAsync(orden, ct);
            return null;
        }

        public async Task MandarAsync(string orden, CancellationToken ct = default)
        {
            await _puerta.WaitAsync(ct);
            try
            {
                MandarSincrono(orden);
            }
            finally
            {
                _puerta.Release();
            }
        }

        public void MandarSincrono(string orden)
        {
            if (!Abierto) throw new InvalidOperationException("El canal no está abierto.");
            if (Roto) throw new IOException("El cable se ha soltado.");
            _mandadas.Enqueue(orden);
        }

        public void Cerrar() => Abierto = false;

        public ValueTask DisposeAsync()
        {
            Cerrar();
            _puerta.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Canal binario con semaforo, como <see cref="CanalSerieBinario"/>.</summary>
    private sealed class CanalBinarioConCandado : ICanalBinario
    {
        private readonly SemaphoreSlim _turno = new(1, 1);
        private int _aperturas;
        private int _mandados;

        internal int Aperturas => Volatile.Read(ref _aperturas);

        internal int Mandados => Volatile.Read(ref _mandados);

        public bool Abierto { get; private set; }

        public string Descripcion => "FT-817 con candado";

        public async Task AbrirAsync(CancellationToken ct = default)
        {
            await _turno.WaitAsync(ct);
            try
            {
                Interlocked.Increment(ref _aperturas);
                Abierto = true;
            }
            finally
            {
                _turno.Release();
            }
        }

        public async Task<byte[]?> PreguntarAsync(byte[] bloque, int bytes, CancellationToken ct = default)
        {
            await MandarAsync(bloque, ct);
            return null;
        }

        public async Task MandarAsync(byte[] bloque, CancellationToken ct = default)
        {
            await _turno.WaitAsync(ct);
            try
            {
                MandarSincrono(bloque);
            }
            finally
            {
                _turno.Release();
            }
        }

        public void MandarSincrono(byte[] bloque)
        {
            if (!Abierto) throw new InvalidOperationException("El canal no está abierto.");
            Interlocked.Increment(ref _mandados);
        }

        public void Cerrar() => Abierto = false;

        public ValueTask DisposeAsync()
        {
            Cerrar();
            _turno.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Control que, cerrado, contesta a todo con <see cref="ObjectDisposedException"/>.</summary>
    private sealed class ControlYaCerrado : IControlEquipo, IPttDirecto, ISueltaDeEmergenciaPtt
    {
        private int _pttArriba;

        internal ControlYaCerrado()
        {
            ViasDeSuelta =
            [
                new ViaDeSuelta(
                    "cerrado: vía de emergencia",
                    _ =>
                    {
                        Comprobar();
                        Volatile.Write(ref _pttArriba, 0);
                        return Task.CompletedTask;
                    },
                    () =>
                    {
                        Comprobar();
                        Volatile.Write(ref _pttArriba, 0);
                    }),
            ];
        }

        internal bool Cerrado { get; set; }

        internal bool PttArriba => Volatile.Read(ref _pttArriba) != 0;

        public ViaDeControl Via => ViaDeControl.Ninguna;

        public EstadoDelEquipo Estado => EstadoDelEquipo.Desconectado;

        public IReadOnlyList<ViaDeSuelta> ViasDeSuelta { get; }

#pragma warning disable CS0067 // El doble no cambia de estado.
        public event EventHandler<EstadoDelEquipo>? EstadoCambiado;
#pragma warning restore CS0067

        public Task ConectarAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task DesconectarAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task PonerFrecuenciaAsync(Frecuencia frecuencia, CancellationToken ct = default) => Task.CompletedTask;

        public Task PonerModoAsync(Modo modo, CancellationToken ct = default) => Task.CompletedTask;

        Task IControlEquipo.PonerPttAsync(bool transmitir, CancellationToken ct) =>
            GuardiaDelPtt.Rechazar(transmitir, () => ((IPttDirecto)this).PonerPttDirectoAsync(false, ct));

        Task IPttDirecto.PonerPttDirectoAsync(bool transmitir, CancellationToken ct)
        {
            Comprobar();
            Volatile.Write(ref _pttArriba, transmitir ? 1 : 0);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private void Comprobar()
        {
            if (Cerrado) throw new ObjectDisposedException(nameof(SemaphoreSlim));
        }
    }
}
