using System.IO;
using System.Net;
using System.Net.Http;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Dxcc;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Impresion;
using Nodisla.Cuaderno.Impresion.Modelo;
using Nodisla.Cuaderno.Satelites.Catalogo;
using Nodisla.Cuaderno.Satelites.Seguimiento;
using Nodisla.Cuaderno.Ui.Ajustes;
using Nodisla.Cuaderno.Ui.Desarrollo;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>Almacen de credenciales de mentira: un diccionario, sin cifrar ni tocar disco.</summary>
internal sealed class AlmacenDeMentira : IAlmacenDeCredenciales
{
    public Dictionary<string, string> Guardadas { get; } = [];

    public string? Leer(string clave) => Guardadas.GetValueOrDefault(clave);

    public void Guardar(string clave, string secreto) => Guardadas[clave] = secreto;

    public void Borrar(string clave) => Guardadas.Remove(clave);

    public bool Existe(string clave) => Guardadas.ContainsKey(clave);
}

/// <summary>Una hora que no corre: las pruebas no miran el reloj de pared.</summary>
internal sealed class HoraFija(DateTimeOffset ahora) : TimeProvider
{
    public DateTimeOffset Ahora { get; set; } = ahora;

    public override DateTimeOffset GetUtcNow() => Ahora;
}

/// <summary>
/// Fabrica de clientes HTTP que no sale a la red: a cualquier pregunta contesta el mismo texto
/// y apunta a donde se le pregunto.
/// </summary>
internal sealed class RedDeMentira(string respuesta) : IHttpClientFactory
{
    public List<Uri> Preguntas { get; } = [];

    public string Respuesta { get; } = respuesta;

    public HttpClient CreateClient(string name) => new(new Manejador(this));

    private sealed class Manejador(RedDeMentira red) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage peticion, CancellationToken ct)
        {
            red.Preguntas.Add(peticion.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(red.Respuesta) });
        }
    }
}

/// <summary>Equipo de mentira con dos VFO: apunta lo que le mandan y no habla con nada.</summary>
internal sealed class EquipoDePapelConDosVfos : IControlEquipo, IEquipoConDosVfos
{
    public List<(NombreDeVfo Vfo, Frecuencia Frecuencia)> Escrituras { get; } = [];

    public int PttsPedidos { get; private set; }

    public ViaDeControl Via => ViaDeControl.CatNativo;

    public EstadoDelEquipo Estado => EstadoDelEquipo.Desconectado;

    public EstadoDeLosVfos Vfos => EstadoDeLosVfos.SinDatos;

    public event EventHandler<EstadoDelEquipo>? EstadoCambiado
    {
        add { }
        remove { }
    }

    public Task ConectarAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task DesconectarAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task PonerFrecuenciaAsync(Frecuencia frecuencia, CancellationToken ct = default) => Task.CompletedTask;

    public Task PonerModoAsync(Modo modo, CancellationToken ct = default) => Task.CompletedTask;

    public Task PonerPttAsync(bool transmitir, CancellationToken ct = default)
    {
        PttsPedidos++;
        return Task.CompletedTask;
    }

    public Task<EstadoDeLosVfos> LeerVfosAsync(CancellationToken ct = default) => Task.FromResult(Vfos);

    public Task PonerVfoActivoAsync(NombreDeVfo vfo, CancellationToken ct = default) => Task.CompletedTask;

    public Task IntercambiarVfosAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task IgualarVfosAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task PonerFrecuenciaDeAsync(NombreDeVfo vfo, Frecuencia frecuencia, CancellationToken ct = default)
    {
        Escrituras.Add((vfo, frecuencia));
        return Task.CompletedTask;
    }

    public Task PonerModoDeAsync(NombreDeVfo vfo, Modo modo, CancellationToken ct = default) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Generador de impresos de mentira: apunta lo que le piden y devuelve un PDF de juguete.</summary>
internal sealed class GeneradorDeMentira : IGeneradorDeImpresos
{
    public List<IReadOnlyList<EtiquetaDeQsl>> Pedidos { get; } = [];

    public OpcionesDeImpresion? UltimasOpciones { get; private set; }

    public Impreso Etiquetas(
        IReadOnlyList<EtiquetaDeQsl> etiquetas,
        PlantillaDeEtiquetas plantilla,
        OpcionesDeImpresion? opciones = null)
    {
        Pedidos.Add(etiquetas);
        UltimasOpciones = opciones;
        return new Impreso("%PDF-1.4 de mentira"u8.ToArray(), "application/pdf", 1, "etiquetas.pdf");
    }

    public Impreso Tarjetas(
        IReadOnlyList<TarjetaDeQsl> tarjetas,
        PlantillaDeTarjetas plantilla,
        OpcionesDeImpresion? opciones = null) => throw new NotSupportedException();
}

/// <summary>
/// Monta los modelos de las pestañas Ajustes, Satélites, Imprimir y Ronda con dobles de
/// mentira: ni red, ni radio, ni tarjeta de sonido, ni el cuaderno ni los ajustes de Jose.
/// </summary>
internal sealed class BancoDeAjustes : IDisposable
{
    /// <summary>Un paso de SO-50 comprobado contra Heavens-Above: culmina a las 13:45:47 UTC.</summary>
    public static readonly DateTimeOffset EnMitadDeUnPaso = new(2026, 9, 26, 13, 45, 0, TimeSpan.Zero);

    public const string TleSo50 = """
        SAUDISAT 1C (SO-50)
        1 27607U 02058C   26269.22993978  .00000723  00000+0  10290-3 0  9997
        2 27607  64.5524 176.3163 0071304 243.8576 115.5184 14.83229784279446
        """;

    public BancoDeAjustes()
    {
        Directory.CreateDirectory(Carpeta);
        Estaciones = new RepositorioEstacionEnMemoria(conPerfilesDeEjemplo: true);
        Cuaderno = new RepositorioQsoEnMemoria(CuadernoDeDemostracion.Generar(300, 7));
    }

    public string Carpeta { get; } = Path.Combine(Path.GetTempPath(), "cuaderno-ajustes-" + Guid.NewGuid().ToString("N"));

    public AjustesDelPrograma Ajustes { get; } = new();

    public AlmacenDeMentira Almacen { get; } = new();

    public RepositorioQsoEnMemoria Cuaderno { get; }

    public RepositorioEstacionEnMemoria Estaciones { get; }

    public IResolutorDxcc Dxcc { get; } = ResolutorDxcc.Predeterminado;

    public HoraFija Hora { get; } = new(EnMitadDeUnPaso);

    public RedDeMentira Red { get; } = new(TleSo50);

    public EquipoDePapelConDosVfos EquipoConDosVfos { get; } = new();

    public GeneradorDeMentira Generador { get; } = new();

    public List<string> DocumentosAbiertos { get; } = [];

    public VistaModeloAjustes Configuracion(
        VistaModeloAjustesCat? cat = null,
        VistaModeloAjustesCluster? cluster = null,
        VistaModeloAjustesAudio? audio = null,
        bool conEscritor = true) =>
        new(Almacen,
            new ImportarAdif(new Nodisla.Cuaderno.Adif.LectorAdif(), Cuaderno),
            Cuaderno,
            "La frase de paso queda a la vista en la lista de procesos mientras dura la subida.",
            () => "Falta TQSL en este equipo.",
            cat,
            cluster,
            audio,
            conEscritor ? new Nodisla.Cuaderno.Adif.EscritorAdif() : null);

    public VistaModeloAjustesCat Cat(MontadorDeEquipo? montador = null) =>
        new(Ajustes,
            Carpeta,
            new ConmutableDePapel(),
            new VigilanteDePapel(),
            registro: null,
            montador ?? ((_, _, _) => Task.FromResult(new MontajeDeEquipo(new ControlDePapel(ViaDeControl.CatNativo), " en COM99 a 38400 baudios"))),
            TimeSpan.FromSeconds(5));

    public VistaModeloAjustesCluster Cluster() =>
        new(Ajustes,
            Carpeta,
            Almacen,
            Estaciones,
            Dxcc,
            new Integraciones.Cluster.FuenteSpotsConmutable(new FuenteSpotsSimulada()));

    public VistaModeloAjustesAudio Audio() =>
        new(Ajustes, Carpeta, new EntradaDeAudioSimulada(), new SalidaDeAudioSimulada());

    public VistaModeloSatelites Satelites(IControlEquipo? equipo = null) =>
        new(CatalogoDeSatelites.Instancia,
            new SeguidorDeSatelites(new OpcionesDeSatelites
            {
                Observador = Nodisla.Cuaderno.Satelites.Orbital.Observador.DesdeLocator(Locator.Parse("IL27HX")),
            }),
            new OpcionesDeSatelites(),
            equipo ?? EquipoConDosVfos,
            Red,
            Ajustes,
            Carpeta,
            new VistaModeloMapa(new PuntosDelCuaderno(Cuaderno, Dxcc), Dxcc),
            Hora);

    public VistaModeloImpresion Impresion() =>
        new(new BuscarEnCuaderno(Cuaderno), Generador, Ajustes, Carpeta)
        {
            AbrirDocumento = DocumentosAbiertos.Add,
            MiIndicativo = Indicativo.Parse("EA8DLF"),
        };

    public VistaModeloRonda Ronda() =>
        new(new GestionarRonda(new RepositorioRondasEnMemoria(), new RegistrarQso(Cuaderno, Estaciones), Dxcc),
            Estaciones);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Carpeta)) Directory.Delete(Carpeta, recursive: true);
        }
        catch (IOException)
        {
            // Una carpeta temporal que no se deja borrar no invalida la prueba.
        }
    }
}
