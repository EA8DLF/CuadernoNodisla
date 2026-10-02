using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Idiomas;
using Serilog;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>Una casilla de la rejilla de novedad, ya escrita para la pantalla.</summary>
/// <param name="Eje">Fila de la rejilla.</param>
/// <param name="Medio">Columna.</param>
/// <param name="Trabajado">Ya esta en el cuaderno.</param>
/// <param name="Confirmado">Ademas esta confirmado por esa via.</param>
/// <param name="Aplica">Se ha podido calcular. Falso = no se sabe, que no es lo mismo que nuevo.</param>
public sealed record CasillaVista(
    EjeDeNovedad Eje,
    MedioDeConfirmacion Medio,
    bool Trabajado,
    bool Confirmado,
    bool Aplica = true)
{
    /// <summary>
    /// Lo que dice la casilla: nueva, pendiente, confirmada o sin dato.
    /// </summary>
    /// <remarks>
    /// Cada estado lleva su <b>palabra</b>, no solo su color: un operador que no distingue el
    /// verde del ambar tiene que poder leer la rejilla igual de rapido.
    /// </remarks>
    public string Texto => !Aplica
        ? "—"
        : (Trabajado, Confirmado) switch
        {
            (false, _) => Textos.T("Libro.Retrato.Casilla.Nuevo"),
            (true, false) => Textos.T("Libro.Retrato.Casilla.SinQsl"),
            _ => Textos.T("Libro.Retrato.Casilla.Ok"),
        };

    /// <summary>Nombre para el lector de pantalla, que no ve los colores.</summary>
    public string NombreAccesible => Aplica
        ? Textos.F("Libro.Retrato.Casilla.Accesible", NombreDelEje, NombreDelMedio, Texto)
        : Textos.F("Libro.Retrato.Casilla.SinDato", NombreDelEje, NombreDelMedio);

    /// <summary>Nombre del eje en el idioma en uso.</summary>
    public string NombreDelEje => Eje switch
    {
        EjeDeNovedad.Pais => Textos.T("Comun.Pais"),
        EjeDeNovedad.Banda => Textos.T("Comun.Banda"),
        _ => Textos.T("Comun.Modo"),
    };

    /// <summary>Nombre de la via en el idioma en uso.</summary>
    public string NombreDelMedio => Medio switch
    {
        MedioDeConfirmacion.Papel => Textos.T("Libro.Retrato.Papel"),
        MedioDeConfirmacion.Eqsl => "eQSL",
        MedioDeConfirmacion.Lotw => "LoTW",
        MedioDeConfirmacion.QrzCom => "QRZ",
        _ => Medio.ToString(),
    };
}

/// <summary>Una casilla de la rejilla de banda por modo, ya escrita para la pantalla.</summary>
/// <param name="Banda">Banda de la columna.</param>
/// <param name="Familia">Fila.</param>
/// <param name="Contactos">Contactos en esa casilla.</param>
/// <param name="Confirmados">De ellos, confirmados.</param>
public sealed record CasillaDeRejilla(string Banda, FamiliaDeModo Familia, int Contactos, int Confirmados)
{
    /// <summary>Hay algun contacto en esa casilla.</summary>
    public bool Trabajado => Contactos > 0;

    /// <summary>Hay alguno confirmado.</summary>
    public bool Confirmado => Confirmados > 0;

    /// <summary>
    /// Lo que se escribe dentro: nada, el numero de contactos, o el visto de confirmado.
    /// </summary>
    /// <remarks>
    /// Los tres estados se distinguen <b>sin mirar el color</b>: casilla vacia es sin trabajar,
    /// un numero es trabajado sin confirmar, y el visto es confirmado. Un daltonico no separa
    /// el verde del ambar, y esto es informacion de operacion, no adorno. Cuantos contactos hay
    /// detras del visto lo dice el rotulo emergente.
    /// </remarks>
    public string Texto => (Contactos, Confirmados) switch
    {
        (0, _) => string.Empty,
        (_, > 0) => "✓",
        (< 10, _) => Contactos.ToString(Textos.Cultura),
        _ => "9+",
    };

    /// <summary>Lo que se dice al pasar el raton y al lector de pantalla.</summary>
    public string Detalle => Contactos switch
    {
        0 => Textos.F("Libro.Retrato.Rejilla.SinTrabajar", Banda, NombreDeLaFamilia),
        _ when Confirmados > 0 =>
            Textos.F("Libro.Retrato.Rejilla.Confirmados", Banda, NombreDeLaFamilia, Contactos, Confirmados),
        _ => Textos.F("Libro.Retrato.Rejilla.NingunoConfirmado", Banda, NombreDeLaFamilia, Contactos),
    };

    /// <summary>Nombre de la familia de modo en el idioma en uso.</summary>
    public string NombreDeLaFamilia => Familia switch
    {
        FamiliaDeModo.Fonia => Textos.T("Libro.Retrato.Familia.Fonia"),
        FamiliaDeModo.Telegrafia => Textos.T("Libro.Retrato.Familia.Telegrafia"),
        _ => Textos.T("Libro.Retrato.Familia.Digitales"),
    };
}

/// <summary>
/// El retrato del indicativo que se esta tecleando: si es nuevo y por donde lo tienes.
/// </summary>
/// <remarks>
/// <para>
/// Responde de un vistazo a «¿le llamo o no?». Arriba, la rejilla de novedad: tres filas —pais,
/// banda, modo— por cuatro vias de confirmacion. Debajo, la rejilla de bandas por familia de
/// modo con ese indicativo, que es el «trabajado antes» sin leer una frase.
/// </para>
/// <para>
/// Se recalcula con retardo. Teclear un indicativo son seis o siete pulsaciones en dos
/// segundos, y lanzar doce recuentos por pulsacion seria castigar la base sin necesidad: se
/// espera a que la mano pare.
/// </para>
/// </remarks>
public sealed partial class VistaModeloRetrato : ObservableObject, IDisposable
{
    /// <summary>Lo que se espera desde la ultima tecla antes de preguntar al cuaderno.</summary>
    private static readonly TimeSpan Retardo = TimeSpan.FromMilliseconds(350);

    /// <summary>Bandas que salen en la rejilla, en el orden de siempre.</summary>
    public static readonly IReadOnlyList<string> BandasDeLaRejilla =
        ["160m", "80m", "60m", "40m", "30m", "20m", "17m", "15m", "12m", "10m", "6m", "4m", "2m", "70cm"];

    private readonly RetratoDelIndicativo _retrato;
    private readonly Dominio.Dxcc.IResolutorDxcc _dxcc;
    private readonly System.Windows.Threading.DispatcherTimer _espera;

    private string _pendiente = string.Empty;
    private Banda _banda = Banda.Vacia;
    private Modo _modo = Modo.Vacio;
    private CancellationTokenSource? _enCurso;
    private bool _liberado;

    /// <summary>Monta el retrato.</summary>
    /// <param name="retrato">Caso de uso que arma las dos rejillas.</param>
    /// <param name="dxcc">Resolutor de entidades, para saber de que pais es el indicativo.</param>
    public VistaModeloRetrato(RetratoDelIndicativo retrato, Dominio.Dxcc.IResolutorDxcc dxcc)
    {
        _retrato = retrato ?? throw new ArgumentNullException(nameof(retrato));
        _dxcc = dxcc ?? throw new ArgumentNullException(nameof(dxcc));

        _espera = new System.Windows.Threading.DispatcherTimer { Interval = Retardo };
        _espera.Tick += async (_, _) =>
        {
            _espera.Stop();
            await ArmarAsync().ConfigureAwait(true);
        };

        Textos.AlCambiar(this, static vm => vm.AlCambiarDeIdioma());
    }

    /// <summary>
    /// Las casillas escriben sus textos al pintarse: con el idioma nuevo se vuelve a armar el
    /// retrato (o el aviso de «escriba un indicativo» si no hay nada).
    /// </summary>
    private void AlCambiarDeIdioma()
    {
        if (_liberado) return;
        if (HayDatos && _pendiente.Length >= 3)
        {
            _espera.Stop();
            _espera.Start();
        }
        else
        {
            Resumen = Textos.T("Libro.Retrato.Resumen.Vacio");
        }
    }

    /// <summary>Perfil de estacion activo, para no mezclar cuadernos.</summary>
    public long? EstacionId { get; set; }

    /// <summary>Las doce casillas de la rejilla de novedad.</summary>
    public ObservableCollection<CasillaVista> Novedad { get; } = [];

    /// <summary>Las casillas de la rejilla de banda por familia de modo.</summary>
    public ObservableCollection<CasillaDeRejilla> Rejilla { get; } = [];

    /// <summary>Indicativo al que corresponde lo que se ensena.</summary>
    [ObservableProperty]
    private string _indicativo = string.Empty;

    /// <summary>Entidad DXCC del indicativo, escrita.</summary>
    [ObservableProperty]
    private string _pais = string.Empty;

    /// <summary>Resumen del trabajado antes: cuantas veces y si hay algo nuevo.</summary>
    [ObservableProperty]
    private string _resumen = Textos.T("Libro.Retrato.Resumen.Vacio");

    /// <summary>Hay algo que el contacto aportaria: entidad, banda o modo nuevos.</summary>
    [ObservableProperty]
    private bool _aportaAlgo;

    /// <summary>Hay datos que ensenar.</summary>
    [ObservableProperty]
    private bool _hayDatos;

    /// <summary>
    /// Dice que indicativo, banda y modo se estan tecleando.
    /// </summary>
    /// <param name="indicativo">Indicativo, tal y como va escrito.</param>
    /// <param name="banda">Banda elegida.</param>
    /// <param name="modo">Modo elegido.</param>
    public void Mirar(string? indicativo, Banda banda, Modo modo)
    {
        var limpio = (indicativo ?? string.Empty).Trim().ToUpperInvariant();

        _pendiente = limpio;
        _banda = banda;
        _modo = modo;

        // Con menos de tres letras no hay indicativo que valga: el prefijo mas corto del mundo
        // tiene dos y siempre lleva numero y sufijo detras.
        if (limpio.Length < 3)
        {
            Vaciar();
            return;
        }

        _espera.Stop();
        _espera.Start();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_liberado) return;
        _liberado = true;

        _espera.Stop();
        _enCurso?.Cancel();
        _enCurso?.Dispose();
    }

    private void Vaciar()
    {
        Novedad.Clear();
        Rejilla.Clear();
        HayDatos = false;
        AportaAlgo = false;
        Indicativo = string.Empty;
        Pais = string.Empty;
        Resumen = Textos.T("Libro.Retrato.Resumen.Vacio");
    }

    private async Task ArmarAsync()
    {
        if (_liberado) return;

        var indicativo = _pendiente;
        if (indicativo.Length < 3) return;

        _enCurso?.Cancel();
        _enCurso?.Dispose();
        _enCurso = new CancellationTokenSource();
        var ct = _enCurso.Token;

        try
        {
            var valor = Dominio.Valores.Indicativo.Parse(indicativo);
            var entidad = _dxcc.Resolver(valor, DateOnly.FromDateTime(DateTime.UtcNow)).Entidad;

            var retrato = await _retrato.ArmarAsync(
                valor,
                entidad?.Numero ?? 0,
                _banda,
                _modo,
                EstacionId,
                ct).ConfigureAwait(true);

            if (ct.IsCancellationRequested || _liberado) return;

            Indicativo = valor.Valor;
            Pais = entidad?.NombreParaMostrar ?? Textos.T("Libro.Retrato.SinEntidad");
            Poner(retrato);
        }
        catch (OperationCanceledException)
        {
            // Otra tecla llego antes: lo que valia es la consulta nueva.
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "No se ha podido armar el retrato de {Indicativo}.", indicativo);
            Vaciar();
        }
    }

    private void Poner(Retrato retrato)
    {
        Novedad.Clear();
        foreach (var casilla in retrato.Novedad)
        {
            Novedad.Add(new CasillaVista(
                casilla.Eje, casilla.Medio, casilla.Trabajado, casilla.Confirmado, casilla.Aplica));
        }

        var porClave = retrato.BandaYModo.ToDictionary(c => (c.Banda, c.Familia));

        Rejilla.Clear();
        foreach (var familia in (FamiliaDeModo[])[FamiliaDeModo.Fonia, FamiliaDeModo.Telegrafia, FamiliaDeModo.Digital])
        {
            foreach (var banda in BandasDeLaRejilla)
            {
                var casilla = porClave.TryGetValue((banda, familia), out var hay)
                    ? new CasillaDeRejilla(banda, familia, hay.Contactos, hay.Confirmados)
                    : new CasillaDeRejilla(banda, familia, 0, 0);

                Rejilla.Add(casilla);
            }
        }

        HayDatos = true;

        // Aporta si hay alguna casilla CALCULADA que salga nueva. Las que no se han podido
        // calcular no cuentan: no saber no es lo mismo que ser nuevo.
        AportaAlgo = retrato.Novedad.Any(c => c.Aplica && !c.Trabajado);

        Resumen = retrato.ContactosConElIndicativo switch
        {
            0 => Textos.T("Libro.Retrato.Resumen.Nunca"),
            1 => Textos.T("Libro.Retrato.Resumen.UnaVez"),
            var cuantos => Textos.F("Libro.Retrato.Resumen.Veces", cuantos),
        };
    }
}
