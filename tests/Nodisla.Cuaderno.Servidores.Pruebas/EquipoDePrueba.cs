using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Radio.Ptt;

namespace Nodisla.Cuaderno.Servidores.Pruebas;

/// <summary>
/// Un equipo simulado con dos VFO, split y potencia. Nada sale al aire: el PTT es un booleano.
/// </summary>
internal sealed class EquipoDePrueba : IEquipoAvanzado, IEquipoConDosVfos
{
    private readonly object _candado = new();
    private EstadoDeLosVfos _vfos;
    private double _potencia = 100;

    public EquipoDePrueba(decimal mhz = 14.074m, string modo = "FT8")
    {
        var m = Modo.Parse(modo);
        var f = Frecuencia.DesdeMegahercios(mhz);
        _vfos = new EstadoDeLosVfos(
            new EstadoDeUnVfo(NombreDeVfo.A, f, m, true, true, true, 3000),
            new EstadoDeUnVfo(NombreDeVfo.B, Frecuencia.DesdeMegahercios(7.074m), m, false, false, false, 3000),
            false, false, 0, false, 0);
        Estado = EstadoDelEquipo.Desconectado with { Conectado = true, Frecuencia = f, Modo = m, Vfo = "A", PotenciaVatios = 100, SenalRecibida = 9 };
    }

    public bool EnAntena { get; private set; }

    public int Subidas { get; private set; }

    public List<string> Ordenes { get; } = [];

    public ViaDeControl Via => ViaDeControl.CatNativo;

    public EstadoDelEquipo Estado { get; private set; }

    public EstadoDeLosVfos Vfos
    {
        get
        {
            lock (_candado) return _vfos;
        }
    }

    public string NombreDelEquipo => "Equipo de prueba";

    public IReadOnlySet<MandoDeEquipo> Mandos { get; } = new HashSet<MandoDeEquipo> { MandoDeEquipo.Potencia, MandoDeEquipo.Split };

    public event EventHandler<EstadoDelEquipo>? EstadoCambiado;

    public Task ConectarAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task DesconectarAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task PonerFrecuenciaAsync(Frecuencia frecuencia, CancellationToken ct = default)
    {
        Apuntar($"F {frecuencia.Hercios}");
        var activo = Vfos.A.EsElActivo ? NombreDeVfo.A : NombreDeVfo.B;
        return PonerFrecuenciaDeAsync(activo, frecuencia, ct);
    }

    public Task PonerModoAsync(Modo modo, CancellationToken ct = default)
    {
        Apuntar($"M {modo}");
        var activo = Vfos.A.EsElActivo ? NombreDeVfo.A : NombreDeVfo.B;
        return PonerModoDeAsync(activo, modo, ct);
    }

    public Task PonerPttAsync(bool transmitir, CancellationToken ct = default)
    {
        Apuntar($"T {transmitir}");
        lock (_candado)
        {
            EnAntena = transmitir;
            if (transmitir) Subidas++;
        }

        Publicar();
        return Task.CompletedTask;
    }

    public Task<EstadoDeLosVfos> LeerVfosAsync(CancellationToken ct = default) => Task.FromResult(Vfos);

    public Task PonerVfoActivoAsync(NombreDeVfo vfo, CancellationToken ct = default)
    {
        Apuntar($"V {vfo}");
        lock (_candado)
        {
            _vfos = _vfos with
            {
                A = _vfos.A with { EsElActivo = vfo == NombreDeVfo.A, Recibe = vfo == NombreDeVfo.A, Transmite = _vfos.Split ? vfo != NombreDeVfo.A : vfo == NombreDeVfo.A },
                B = _vfos.B with { EsElActivo = vfo == NombreDeVfo.B, Recibe = vfo == NombreDeVfo.B, Transmite = _vfos.Split ? vfo != NombreDeVfo.B : vfo == NombreDeVfo.B },
            };
        }

        Publicar();
        return Task.CompletedTask;
    }

    public Task IntercambiarVfosAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task IgualarVfosAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task PonerFrecuenciaDeAsync(NombreDeVfo vfo, Frecuencia frecuencia, CancellationToken ct = default)
    {
        lock (_candado)
        {
            _vfos = vfo == NombreDeVfo.A ? _vfos with { A = _vfos.A with { Frecuencia = frecuencia } } : _vfos with { B = _vfos.B with { Frecuencia = frecuencia } };
        }

        Publicar();
        return Task.CompletedTask;
    }

    public Task PonerModoDeAsync(NombreDeVfo vfo, Modo modo, CancellationToken ct = default)
    {
        lock (_candado)
        {
            _vfos = vfo == NombreDeVfo.A ? _vfos with { A = _vfos.A with { Modo = modo } } : _vfos with { B = _vfos.B with { Modo = modo } };
        }

        Publicar();
        return Task.CompletedTask;
    }

    public RangoDeMando? Rango(MandoDeEquipo mando) => mando switch
    {
        MandoDeEquipo.Potencia => new RangoDeMando(mando, 5, 100, 1, "W"),
        MandoDeEquipo.Split => new RangoDeMando(mando, 0, 1, 1),
        _ => null,
    };

    public Task<double?> LeerMandoAsync(MandoDeEquipo mando, CancellationToken ct = default)
    {
        lock (_candado)
        {
            return Task.FromResult<double?>(mando switch
            {
                MandoDeEquipo.Potencia => _potencia,
                MandoDeEquipo.Split => _vfos.Split ? 1 : 0,
                _ => null,
            });
        }
    }

    public Task EscribirMandoAsync(MandoDeEquipo mando, double valor, CancellationToken ct = default)
    {
        Apuntar($"{mando} {valor}");
        lock (_candado)
        {
            if (mando == MandoDeEquipo.Potencia) _potencia = valor;
            if (mando == MandoDeEquipo.Split)
            {
                var split = valor >= 0.5;
                var aActivo = _vfos.A.EsElActivo;
                _vfos = _vfos with
                {
                    Split = split,
                    A = _vfos.A with { Transmite = split ? !aActivo : aActivo },
                    B = _vfos.B with { Transmite = split ? aActivo : !aActivo },
                };
            }
        }

        Publicar();
        return Task.CompletedTask;
    }

    public Task<LecturaDeMedidores> LeerMedidoresAsync(CancellationToken ct = default) =>
        Task.FromException<LecturaDeMedidores>(new NotSupportedException());

    public Task<IReadOnlyList<MemoriaDeEquipo>> LeerMemoriasAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<MemoriaDeEquipo>>([]);

    public Task IrAMemoriaAsync(int numero, CancellationToken ct = default) => Task.CompletedTask;

    public Task<string?> OrdenEnCrudoAsync(string orden, CancellationToken ct = default)
    {
        Apuntar("CRUDO " + orden);
        return Task.FromResult<string?>(null);
    }

    public double Potencia
    {
        get
        {
            lock (_candado) return _potencia;
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private void Apuntar(string orden)
    {
        lock (_candado) Ordenes.Add(orden);
    }

    private void Publicar()
    {
        EstadoDelEquipo estado;
        lock (_candado)
        {
            var activo = _vfos.A.EsElActivo ? _vfos.A : _vfos.B;
            Estado = Estado with { Frecuencia = activo.Frecuencia, Modo = activo.Modo, Vfo = activo.Nombre.ToString(), Transmitiendo = EnAntena, PotenciaVatios = _potencia };
            estado = Estado;
        }

        EstadoCambiado?.Invoke(this, estado);
    }
}

/// <summary>Monta la radio compartida sobre un vigilante de verdad y el equipo de prueba.</summary>
internal sealed class Banco : IAsyncDisposable
{
    public Banco(decimal mhz = 14.074m, string modo = "FT8", int? potenciaMaximaDeBanda = null)
    {
        Equipo = new EquipoDePrueba(mhz, modo);
        Vigilante = new VigilantePtt(
            Equipo,
            new OpcionesDelVigilante
            {
                TiempoMaximo = TimeSpan.FromSeconds(30),
                TiempoSinLatido = TimeSpan.FromSeconds(5),
                PasoDeVigilancia = TimeSpan.FromMilliseconds(5),
                EsperaDeSuelta = TimeSpan.FromMilliseconds(500),
                EngancharseAlCierreDelProceso = false,
                Seguridad = new OpcionesDeSeguridadDeTx { VigilarRoe = false, PausaTrasUnCorte = TimeSpan.FromMilliseconds(50) },
            });
        Radio = new RadioCompartida(Equipo, Vigilante, () => Pestillo, _ => potenciaMaximaDeBanda);
        Servidores = new ServidoresParaOtrosProgramas(Radio);
    }

    public EquipoDePrueba Equipo { get; }

    public VigilantePtt Vigilante { get; }

    public RadioCompartida Radio { get; }

    public ServidoresParaOtrosProgramas Servidores { get; }

    public bool Pestillo { get; set; } = true;

    /// <summary>Arranca en puertos libres de 127.0.0.1 (puerto 0).</summary>
    public async Task ArrancarAsync(bool rigctld = true, bool tci = false, bool tx = true, string? token = null)
    {
        await Servidores.AplicarAsync(new OpcionesDeServidores
        {
            RigctldActivo = rigctld,
            PuertoRigctld = 0,
            TciActivo = tci,
            PuertoTci = 0,
            PermitirTx = tx,
            TokenTci = token,
        });
    }

    public async ValueTask DisposeAsync()
    {
        await Servidores.DisposeAsync();
        await Vigilante.DisposeAsync();
    }
}

/// <summary>Esperas con tope, sin dormir a ciegas.</summary>
internal static class Esperar
{
    public static async Task<bool> HastaAsync(Func<bool> condicion, int milisegundos = 3000)
    {
        var tope = DateTime.UtcNow.AddMilliseconds(milisegundos);
        while (DateTime.UtcNow < tope)
        {
            if (condicion()) return true;
            await Task.Delay(10);
        }

        return condicion();
    }
}
