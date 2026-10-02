using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Radio;
using Nodisla.Cuaderno.Radio.Control;
using Nodisla.Cuaderno.Radio.Control.Ft710;
using Nodisla.Cuaderno.Radio.Modelos;
using Nodisla.Cuaderno.Radio.Ptt;
using Nodisla.Cuaderno.Ui.Ajustes;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>
/// Una opcion de desplegable cuyo texto sale de los textos del programa: avisa al cambiar de
/// idioma para que el desplegable (y la opcion elegida) se vuelvan a escribir.
/// </summary>
/// <remarks>
/// La igualdad sigue siendo la de los datos de la opcion: el aviso no cuenta, para que el
/// desplegable siga reconociendo la opcion elegida.
/// </remarks>
public abstract record OpcionTraducida : INotifyPropertyChanged
{
    /// <summary>Engancha la opcion al cambio de idioma.</summary>
    protected OpcionTraducida()
    {
        Textos.AlCambiar(this, static o => o.PropertyChanged?.Invoke(o, new PropertyChangedEventArgs(string.Empty)));
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Iguales si son del mismo tipo: los datos los compara cada opcion.</summary>
    /// <param name="other">La otra opcion.</param>
    /// <returns>Si son del mismo tipo.</returns>
    public virtual bool Equals(OpcionTraducida? other) => other is not null && EqualityContract == other.EqualityContract;

    /// <inheritdoc />
    public override int GetHashCode() => EqualityContract.GetHashCode();
}

/// <summary>Una via de control, escrita para el desplegable.</summary>
/// <param name="Via">Via de control.</param>
/// <param name="ClaveDelTitulo">Clave del nombre en pantalla.</param>
/// <param name="ClaveDeLaExplicacion">Clave de lo que hace falta para usarla.</param>
public sealed record ViaDeControlElegible(ViaDeControl Via, string ClaveDelTitulo, string ClaveDeLaExplicacion) : OpcionTraducida
{
    /// <summary>Como se llama en pantalla.</summary>
    public string Titulo => Textos.T(ClaveDelTitulo);

    /// <summary>Que hace falta para usarla.</summary>
    public string Explicacion => Textos.T(ClaveDeLaExplicacion);
}

/// <summary>Un fabricante del desplegable, o la deteccion automatica (nulo).</summary>
/// <param name="Fabricante">Fabricante, o nulo para buscar el equipo solo.</param>
/// <param name="Nombre">Nombre de la marca (no se traduce).</param>
public sealed record FabricanteElegible(Fabricante? Fabricante, string Nombre) : OpcionTraducida
{
    /// <summary>Como se llama en pantalla.</summary>
    public string Titulo => Fabricante is null ? Textos.T("Ajustes.Equipo.DeteccionAutomatica") : Nombre;
}

/// <summary>Un modelo del catalogo, para el desplegable.</summary>
/// <param name="Modelo">Modelo.</param>
public sealed record ModeloElegible(ModeloDeEquipo Modelo)
{
    /// <summary>Como se ve en el desplegable (dice si esta sin probar con radio).</summary>
    public string Titulo => Modelo.ParaElDesplegable;
}

/// <summary>Una velocidad de puerto serie, con aviso de si es de las habituales.</summary>
/// <param name="Baudios">Velocidad.</param>
/// <param name="EsHabitual">Es una de las dos que usa el FT-710.</param>
public sealed record VelocidadElegible(int Baudios, bool EsHabitual) : OpcionTraducida
{
    /// <summary>Como se escribe en el desplegable.</summary>
    public string Titulo => EsHabitual
        ? Textos.F("Ajustes.Equipo.VelocidadHabitual", Baudios)
        : Baudios.ToString("N0", Textos.Cultura);
}

/// <summary>Un control de equipo recien montado, con el rastro de donde ha aparecido.</summary>
/// <param name="Control">Control montado, listo para conectar.</param>
/// <param name="Donde">Donde ha aparecido el equipo, para escribirlo; vacio si no se buscó.</param>
public sealed record MontajeDeEquipo(IControlEquipo Control, string Donde)
{
    /// <summary>Puerto donde ha contestado el equipo, si se ha buscado.</summary>
    public string? Puerto { get; init; }

    /// <summary>Velocidad a la que ha contestado, si se ha buscado.</summary>
    public int Baudios { get; init; }
}

/// <summary>
/// Monta el control del equipo que piden unos ajustes.
/// </summary>
/// <remarks>
/// Es el hueco por donde las pruebas meten un montaje de mentira: montar de verdad abre puertos
/// serie de la maquina, y eso no se hace en una bateria de pruebas.
/// </remarks>
/// <param name="equipo">Ajustes elegidos.</param>
/// <param name="aviso">Por donde se va contando lo que se esta haciendo.</param>
/// <param name="ct">Testigo de cancelacion; salta con el tope de tiempo.</param>
/// <returns>El control montado.</returns>
public delegate Task<MontajeDeEquipo> MontadorDeEquipo(
    AjustesDeEquipo equipo,
    IProgress<string> aviso,
    CancellationToken ct);

/// <summary>Como fue la ultima prueba de conexion.</summary>
public enum ResultadoDePrueba
{
    /// <summary>Todavia no se ha probado.</summary>
    SinProbar,

    /// <summary>Se esta probando.</summary>
    Probando,

    /// <summary>El equipo ha contestado.</summary>
    Correcto,

    /// <summary>No ha contestado, o ha fallado algo.</summary>
    Fallido,
}

/// <summary>
/// El apartado CAT de la pantalla de ajustes: por donde se habla con el equipo.
/// </summary>
/// <remarks>
/// <para>
/// Esta pantalla existe porque faltaba lo mas basico: <b>la via de control estaba escrita en el
/// codigo</b> y venia en «Ninguna», asi que el boton Conectar del panel del equipo no abria
/// nada. Aqui se elige la via, el puerto y la velocidad, se prueba y se aplica sin cerrar el
/// programa.
/// </para>
/// <para>
/// <b>Probar no transmite.</b> Se abre la conexion, se lee la identificacion, la frecuencia y
/// el modo, y se cierra. Ni una sola orden que ponga el equipo en antena o que le mueva los
/// VFO: el FT-710 puede estar encendido y en mitad de un contacto.
/// </para>
/// </remarks>
public sealed partial class VistaModeloAjustesCat : ObservableObject
{
    private readonly AjustesDelPrograma _ajustes;
    private readonly string _carpetaDeDatos;
    private readonly IControlEquipoConmutable _conmutable;
    private readonly IVigilantePtt _vigilante;
    private readonly ILogger _registro;
    private readonly MontadorDeEquipo _montador;
    private readonly TimeSpan _topeDeBusqueda;

    /// <summary>
    /// Lo que como mucho se pasa buscando el equipo antes de rendirse.
    /// </summary>
    /// <remarks>
    /// Medido en la maquina de Jose: barrer sus cuatro puertos serie a las cinco velocidades,
    /// sin que conteste nadie, tarda 18 segundos. Un minuto deja sitio de sobra para una maquina
    /// con mas puertos y, sobre todo, <b>pone un final</b>: antes esto no terminaba nunca.
    /// </remarks>
    public static readonly TimeSpan TopeDeBusquedaPorOmision = TimeSpan.FromMinutes(1);

    /// <summary>Monta el apartado sobre los ajustes guardados.</summary>
    /// <param name="ajustes">Ajustes del programa, ya leidos del disco.</param>
    /// <param name="carpetaDeDatos">Carpeta donde se guardan.</param>
    /// <param name="conmutable">Intermediario que sabe cambiar el control del equipo.</param>
    /// <param name="vigilante">Vigilante del PTT, para soltarlo antes de cambiar de control.</param>
    /// <param name="registro">Donde anotar lo que pasa con el CAT. Sin el, no se anota nada.</param>
    /// <param name="montador">Como se monta el control; nulo para montarlo de verdad.</param>
    /// <param name="topeDeBusqueda">Tope de tiempo de la busqueda; nulo para el de costumbre.</param>
    public VistaModeloAjustesCat(
        AjustesDelPrograma ajustes,
        string carpetaDeDatos,
        IControlEquipoConmutable conmutable,
        IVigilantePtt vigilante,
        ILogger? registro = null,
        MontadorDeEquipo? montador = null,
        TimeSpan? topeDeBusqueda = null)
    {
        ArgumentNullException.ThrowIfNull(ajustes);
        ArgumentException.ThrowIfNullOrWhiteSpace(carpetaDeDatos);

        _ajustes = ajustes;
        _carpetaDeDatos = carpetaDeDatos;
        _conmutable = conmutable ?? throw new ArgumentNullException(nameof(conmutable));
        _vigilante = vigilante ?? throw new ArgumentNullException(nameof(vigilante));
        _registro = registro ?? NullLogger.Instance;
        _montador = montador ?? MontarDeVerdadAsync;
        _topeDeBusqueda = topeDeBusqueda ?? TopeDeBusquedaPorOmision;

        Vias =
        [
            new(ViaDeControl.Ninguna, "Ajustes.Equipo.Via.Ninguna", "Ajustes.Equipo.Via.Ninguna.Explicacion"),
            new(ViaDeControl.CatNativo, "Ajustes.Equipo.Via.CatNativo", "Ajustes.Equipo.Via.CatNativo.Explicacion"),
            new(ViaDeControl.Rigctld, "Ajustes.Equipo.Via.Rigctld", "Ajustes.Equipo.Via.Rigctld.Explicacion"),
            new(ViaDeControl.OmniRig, "Ajustes.Equipo.Via.OmniRig", "Ajustes.Equipo.Via.OmniRig.Explicacion"),
        ];

        Velocidades =
        [
            new(4800, false),
            new(9600, false),
            new(19200, false),
            new(38400, true),
            new(115200, true),
        ];

        ViasDePtt =
        [
            new(ViaDePtt.Cat, "Ajustes.Equipo.Ptt.Cat", "Ajustes.Equipo.Ptt.Cat.Explicacion"),
            new(ViaDePtt.Rts, "Ajustes.Equipo.Ptt.Rts", "Ajustes.Equipo.Ptt.Rts.Explicacion"),
            new(ViaDePtt.Dtr, "Ajustes.Equipo.Ptt.Dtr", "Ajustes.Equipo.Ptt.Dtr.Explicacion"),
        ];

        Fabricantes =
        [
            new(null, string.Empty),
            .. CatalogoDeModelos.Fabricantes.Select(f => new FabricanteElegible(f, f == Radio.Modelos.Fabricante.Icom ? "ICOM" : f.ToString())),
        ];

        RecogerDeLosAjustes();
        RefrescarPuertos();

        // La pastilla de la via puesta y el aviso del modelo se escriben en el idioma nuevo.
        Textos.AlCambiar(this, static vm =>
        {
            vm.RefrescarViaPuesta();
            vm.OnPropertyChanged(nameof(AvisoDelModelo));
        });
    }

    /// <summary>Fabricantes del desplegable, con la deteccion automatica la primera.</summary>
    public IReadOnlyList<FabricanteElegible> Fabricantes { get; }

    /// <summary>Modelos del fabricante elegido.</summary>
    public ObservableCollection<ModeloElegible> ModelosDelFabricante { get; } = [];

    /// <summary>Fabricante elegido (o la deteccion automatica).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SeElijeModelo))]
    [NotifyPropertyChangedFor(nameof(EsIcom))]
    private FabricanteElegible? _fabricante;

    /// <summary>Modelo elegido.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AvisoDelModelo))]
    private ModeloElegible? _modelo;

    /// <summary>Direccion CI-V en hexadecimal (ICOM), vacia para la de fabrica.</summary>
    [ObservableProperty]
    private string _direccionCiv = string.Empty;

    /// <summary>Se ha elegido fabricante: hay que enseñar el desplegable de modelos.</summary>
    public bool SeElijeModelo => Fabricante?.Fabricante is not null;

    /// <summary>Es un ICOM: se enseña la direccion CI-V.</summary>
    public bool EsIcom => Fabricante?.Fabricante == Radio.Modelos.Fabricante.Icom;

    /// <summary>Aviso honesto del modelo elegido: si esta sin probar con radio, y lo que falta confirmar.</summary>
    public string AvisoDelModelo => Modelo?.Modelo is { ProbadoConRadio: false } m
        ? Textos.T("Ajustes.Equipo.SinProbarConRadio") + " " + (m.Notas ?? string.Empty)
        : string.Empty;

    partial void OnFabricanteChanged(FabricanteElegible? value)
    {
        var anterior = Modelo?.Modelo.Clave;
        ModelosDelFabricante.Clear();
        if (value?.Fabricante is { } f)
        {
            foreach (var m in CatalogoDeModelos.De(f)) ModelosDelFabricante.Add(new ModeloElegible(m));
        }

        Modelo = ModelosDelFabricante.FirstOrDefault(m => m.Modelo.Clave == anterior) ?? ModelosDelFabricante.FirstOrDefault();
    }

    /// <summary>Vias de control que se ofrecen.</summary>
    public IReadOnlyList<ViaDeControlElegible> Vias { get; }

    /// <summary>Velocidades de puerto serie que se ofrecen.</summary>
    public IReadOnlyList<VelocidadElegible> Velocidades { get; }

    /// <summary>Formas de subir el PTT que se ofrecen.</summary>
    public IReadOnlyList<ViaDePttElegible> ViasDePtt { get; }

    /// <summary>Puertos serie que hay ahora mismo en la maquina, con su descripcion.</summary>
    public ObservableCollection<PuertoSerieDeLaMaquina> Puertos { get; } = [];

    /// <summary>Via de control elegida.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EsCatNativo))]
    [NotifyPropertyChangedFor(nameof(EsRigctld))]
    [NotifyPropertyChangedFor(nameof(EsOmniRig))]
    [NotifyPropertyChangedFor(nameof(HayEquipo))]
    private ViaDeControlElegible? _via;

    /// <summary>Buscar el equipo por todos los puertos en vez de usar el elegido.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SeElijePuerto))]
    private bool _detectarElPuerto = true;

    /// <summary>Puerto serie elegido.</summary>
    [ObservableProperty]
    private PuertoSerieDeLaMaquina? _puerto;

    /// <summary>Velocidad elegida.</summary>
    [ObservableProperty]
    private VelocidadElegible? _velocidad;

    /// <summary>Cada cuanto se le pregunta al equipo como esta, en milisegundos.</summary>
    [ObservableProperty]
    private int _sondeoMs = 500;

    /// <summary>Lo que se espera a que el equipo conteste, en milisegundos.</summary>
    [ObservableProperty]
    private int _esperaDeOrdenMs = 350;

    /// <summary>Forma de subir el PTT elegida.</summary>
    [ObservableProperty]
    private ViaDePttElegible? _pttPor;

    /// <summary>Maquina donde escucha <c>rigctld</c>.</summary>
    [ObservableProperty]
    private string _maquinaDeRigctld = "127.0.0.1";

    /// <summary>Puerto TCP donde escucha <c>rigctld</c>.</summary>
    [ObservableProperty]
    private int _puertoDeRigctld = 4532;

    /// <summary>Cual de los dos equipos de OmniRig se usa.</summary>
    [ObservableProperty]
    private int _equipoDeOmniRig = 1;

    /// <summary>Tiempo maximo en antena de una vez, en segundos.</summary>
    [ObservableProperty]
    private int _tiempoMaximoSegundos = 180;

    /// <summary>Tiempo sin latido tras el cual se baja el PTT, en segundos.</summary>
    [ObservableProperty]
    private int _tiempoSinLatidoSegundos = 15;

    /// <summary>Lo que ha dicho la ultima prueba o la ultima aplicacion.</summary>
    [ObservableProperty]
    private string _parte = string.Empty;

    /// <summary>Como fue la ultima prueba.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayParte))]
    private ResultadoDePrueba _resultado = ResultadoDePrueba.SinProbar;

    /// <summary>Se esta probando o aplicando.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ProbarCommand))]
    [NotifyCanExecuteChangedFor(nameof(AplicarCommand))]
    private bool _ocupado;

    /// <summary>Via que hay puesta ahora mismo, escrita.</summary>
    [ObservableProperty]
    private string _viaPuesta = string.Empty;

    /// <summary>Hay algo que contar de la ultima prueba.</summary>
    public bool HayParte => Resultado != ResultadoDePrueba.SinProbar;

    /// <summary>La via elegida es el CAT nativo del FT-710.</summary>
    public bool EsCatNativo => Via?.Via == ViaDeControl.CatNativo;

    /// <summary>La via elegida es <c>rigctld</c>.</summary>
    public bool EsRigctld => Via?.Via == ViaDeControl.Rigctld;

    /// <summary>La via elegida es OmniRig.</summary>
    public bool EsOmniRig => Via?.Via == ViaDeControl.OmniRig;

    /// <summary>Se ha elegido alguna via con equipo detras.</summary>
    public bool HayEquipo => Via is not null && Via.Via != ViaDeControl.Ninguna;

    /// <summary>Hay que enseñar el desplegable de puertos.</summary>
    public bool SeElijePuerto => !DetectarElPuerto;

    /// <summary>Vuelve a mirar que puertos serie hay en la maquina.</summary>
    [RelayCommand]
    public void RefrescarPuertos()
    {
        var elegido = Puerto?.Nombre ?? _ajustes.Equipo.Puerto;

        Puertos.Clear();
        try
        {
            foreach (var puerto in PuertosSerieDelSistema.Listar()) Puertos.Add(puerto);
        }
        catch (Exception ex)
        {
            _registro.LogWarning(ex, "No se han podido listar los puertos serie.");
        }

        Puerto = Puertos.FirstOrDefault(p => string.Equals(p.Nombre, elegido, StringComparison.OrdinalIgnoreCase))
                 ?? Puertos.FirstOrDefault();
    }

    /// <summary>
    /// Abre la conexion, lee lo que hay y la cierra.
    /// </summary>
    /// <remarks>
    /// Se prueba sobre un control <b>aparte</b>, no sobre el que esta usando el programa: probar
    /// no puede tirar la conexion que el operador tenga abierta. Y se lee, solo se lee.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(SePuedeTrabajar))]
    public async Task ProbarAsync()
    {
        var equipo = Recoger();

        if (equipo.Via == ViaDeControl.Ninguna)
        {
            Resultado = ResultadoDePrueba.Correcto;
            Parte = Textos.T("Ajustes.Equipo.NadaQueProbar");
            return;
        }

        await TrabajarAsync(equipo, aplicar: false).ConfigureAwait(true);
    }

    /// <summary>
    /// Guarda los ajustes y cambia el control del equipo sin cerrar el programa.
    /// </summary>
    /// <remarks>
    /// <b>Se guarda antes de buscar.</b> Lo que el operador ha elegido es suyo y no puede
    /// perderse porque la radio este apagada; si al buscar aparece en otro puerto o a otra
    /// velocidad, se vuelve a guardar con lo encontrado.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(SePuedeTrabajar))]
    public async Task AplicarAsync()
    {
        var equipo = Recoger();

        _ajustes.Equipo = equipo;
        _ajustes.Guardar(_carpetaDeDatos);

        await TrabajarAsync(equipo, aplicar: true).ConfigureAwait(true);
    }

    /// <summary>
    /// El trabajo de verdad de Probar y de Aplicar, que es casi el mismo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Esto no puede quedarse callado ni colgado, y por algo.</b> Antes se llamaba a la
    /// busqueda de equipos directamente desde el hilo de la ventana y sin decir nada: buscar por
    /// los cuatro puertos serie de esta maquina a las cinco velocidades tarda <b>hasta 18
    /// segundos</b> —medido— cuando no contesta nadie, y si el puerto se colgaba no terminaba
    /// nunca. El operador pulsaba Aplicar, no veia un solo mensaje, los botones se le quedaban
    /// grises y nada se guardaba. Ahora: el trabajo va fuera del hilo de la ventana, con tope de
    /// tiempo, y va contando por donde va.
    /// </para>
    /// </remarks>
    /// <param name="equipo">Ajustes tal y como estan en pantalla.</param>
    /// <param name="aplicar">Cierto para cambiar el control del programa; falso para solo probar.</param>
    private async Task TrabajarAsync(AjustesDeEquipo equipo, bool aplicar)
    {
        Ocupado = true;
        Resultado = ResultadoDePrueba.Probando;
        Parte = Textos.T(aplicar ? "Ajustes.Equipo.Aplicando" : "Ajustes.Equipo.Probando");

        using var corte = new CancellationTokenSource(_topeDeBusqueda);
        var aviso = new Progress<string>(texto => Parte = texto);

        try
        {
            var montaje = await _montador(equipo, aviso, corte.Token).ConfigureAwait(true);

            // Lo encontrado se lleva a la pantalla y a los ajustes: el operador tiene que ver
            // donde ha aparecido, y la proxima vez hay que acertar a la primera.
            if (montaje.Puerto is { Length: > 0 })
            {
                equipo.Puerto = montaje.Puerto;
                equipo.Baudios = montaje.Baudios;
                LlevarALaPantalla(montaje.Puerto, montaje.Baudios);

                if (aplicar)
                {
                    _ajustes.Equipo = equipo;
                    _ajustes.Guardar(_carpetaDeDatos);
                }
            }

            if (aplicar)
            {
                await AplicarElControlAsync(equipo, montaje).ConfigureAwait(true);
                return;
            }

            await ProbarElControlAsync(montaje).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            _registro.LogWarning("Se ha agotado el tiempo buscando el equipo ({Tope}).", _topeDeBusqueda);
            Resultado = ResultadoDePrueba.Fallido;
            Parte = Textos.F("Ajustes.Equipo.TiempoAgotado", _topeDeBusqueda.TotalSeconds);
        }
        catch (Exception ex)
        {
            _registro.LogError(ex, "Ha fallado la conexión con el equipo.");
            Resultado = ResultadoDePrueba.Fallido;
            Parte = Textos.F("Ajustes.Equipo.NoSeHaPodidoConectar", ex.Message);
        }
        finally
        {
            Ocupado = false;
        }
    }

    /// <summary>Abre, lee y cierra, sin tocar el control que esta usando el programa.</summary>
    private async Task ProbarElControlAsync(MontajeDeEquipo montaje)
    {
        await using var control = montaje.Control;

        if (control.Via == ViaDeControl.Ninguna)
        {
            Resultado = ResultadoDePrueba.Fallido;
            Parte = NoApareceElEquipo;
            _registro.LogWarning("Prueba de conexión: {Parte}", Parte);
            return;
        }

        Parte = Textos.T("Ajustes.Equipo.Abriendo");
        await control.ConectarAsync().ConfigureAwait(true);

        var estado = control.Estado;
        var nombre = control is IEquipoAvanzado avanzado ? avanzado.NombreDelEquipo : Textos.T("Ajustes.Equipo.Generico");

        Resultado = estado.Conectado ? ResultadoDePrueba.Correcto : ResultadoDePrueba.Fallido;
        Parte = estado.Conectado
            ? Textos.F(
                "Ajustes.Equipo.Contesta",
                nombre,
                montaje.Donde,
                TextoDeFrecuencia.Escribir(estado.Frecuencia),
                estado.Modo.EsVacio ? "—" : estado.Modo.NombreUsual)
            : Textos.T("Ajustes.Equipo.NoContesta");

        _registro.LogInformation("Prueba de conexión: {Parte}", Parte);
        await control.DesconectarAsync().ConfigureAwait(true);
    }

    /// <summary>Pone el control nuevo en el intermediario, soltando antes el PTT.</summary>
    private async Task AplicarElControlAsync(AjustesDeEquipo equipo, MontajeDeEquipo montaje)
    {
        if (_vigilante.EnAntena)
        {
            await _vigilante.SoltarYaAsync(MotivoDeSuelta.Cierre).ConfigureAwait(true);
        }

        await _conmutable.SustituirAsync(montaje.Control).ConfigureAwait(true);
        RefrescarViaPuesta();

        var sinEquipo = equipo.Via != ViaDeControl.Ninguna && montaje.Control.Via == ViaDeControl.Ninguna;
        Resultado = sinEquipo ? ResultadoDePrueba.Fallido : ResultadoDePrueba.Correcto;

        Parte = equipo.Via switch
        {
            ViaDeControl.Ninguna => Textos.T("Ajustes.Equipo.GuardadoSinEquipo"),
            _ when sinEquipo => Textos.F("Ajustes.Equipo.GuardadoPero", NoApareceElEquipo),
            _ => Textos.F("Ajustes.Equipo.GuardadoYAplicado", Titulo(_conmutable.Actual.Via), montaje.Donde),
        };

        _registro.LogInformation("Ajustes del equipo aplicados: {Parte}", Parte);
    }

    /// <summary>
    /// Lo que se dice cuando no aparece la radio por ningun puerto.
    /// </summary>
    /// <remarks>
    /// Empieza en mayuscula porque esto se lee en PANTALLA, donde es la frase entera. En el
    /// registro va detras de «Prueba de conexión:», y ahi una mayuscula no molesta a nadie; al
    /// reves si: una frase que empieza en minuscula dentro de un recuadro rojo parece cortada.
    /// </remarks>
    private static string NoApareceElEquipo => Textos.T("Ajustes.Equipo.NoAparece");

    /// <summary>Lleva a los desplegables el puerto y la velocidad donde ha aparecido el equipo.</summary>
    private void LlevarALaPantalla(string puerto, int baudios)
    {
        if (Puertos.All(p => !string.Equals(p.Nombre, puerto, StringComparison.OrdinalIgnoreCase)))
        {
            RefrescarPuertos();
        }

        Puerto = Puertos.FirstOrDefault(p => string.Equals(p.Nombre, puerto, StringComparison.OrdinalIgnoreCase))
                 ?? Puerto;
        Velocidad = Velocidades.FirstOrDefault(v => v.Baudios == baudios) ?? Velocidad;
    }

    /// <summary>Vuelve a poner en pantalla lo que hay guardado, deshaciendo lo tecleado.</summary>
    [RelayCommand]
    public void Descartar()
    {
        RecogerDeLosAjustes();
        Resultado = ResultadoDePrueba.SinProbar;
        Parte = string.Empty;
    }

    /// <summary>Deja escrita la via que hay puesta ahora mismo.</summary>
    public void RefrescarViaPuesta() => ViaPuesta = Titulo(_conmutable.Actual.Via);

    /// <summary>
    /// Monta el control que pide la pantalla, buscando el equipo si se ha pedido detectarlo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Buscar es leer.</b> Se abre cada puerto serie, se manda <c>ID;</c> y se mira si
    /// contesta un identificador Yaesu de cuatro cifras. Ni una orden que mueva nada: en esta
    /// maquina hay un puerto que no es la radio y que escupe su propio registro, y el equipo se
    /// reconoce por lo que contesta, nunca por donde estaba.
    /// </para>
    /// <para>
    /// Todo esto pasa <b>fuera del hilo de la ventana</b>: abrir puertos serie es lento y la
    /// pantalla tiene que poder seguir contando por donde va.
    /// </para>
    /// </remarks>
    private async Task<MontajeDeEquipo> MontarDeVerdadAsync(
        AjustesDeEquipo equipo,
        IProgress<string> aviso,
        CancellationToken ct)
    {
        if (equipo.Via != ViaDeControl.CatNativo || !equipo.DetectarElPuerto)
        {
            return new MontajeDeEquipo(
                FabricaDeControlEquipo.Crear(equipo.AOpcionesDeRadio(), _registro),
                string.Empty);
        }

        // Con varios modelos, la busqueda la hace la fabrica: Yaesu por ID; e ICOM por CI-V.
        // Solo lee. El puerto y la velocidad donde aparezca se guardan como antes.
        return await Task.Run(async () =>
        {
            var opciones = equipo.AOpcionesDeRadio();
            var visto = await FabricaDeControlEquipo.BuscarEquipoAsync(opciones, aviso, _registro, ct).ConfigureAwait(false);
            if (visto is null) return new MontajeDeEquipo(new ControlNulo(_registro), string.Empty);

            opciones.Ft710.Puerto = visto.Puerto;
            opciones.Ft710.Baudios = visto.Baudios;
            return new MontajeDeEquipo(
                FabricaDeControlEquipo.CrearPara(visto, opciones, _registro),
                " " + Textos.F("Ajustes.Equipo.EnPuerto", visto.Puerto, visto.Baudios))
            {
                Puerto = visto.Puerto,
                Baudios = visto.Baudios,
            };
        }, ct).ConfigureAwait(true);
    }

    private bool SePuedeTrabajar() => !Ocupado;

    private string Titulo(ViaDeControl via) =>
        Vias.FirstOrDefault(v => v.Via == via)?.Titulo ?? via.ToString();

    /// <summary>Pasa lo que hay en pantalla a unos ajustes.</summary>
    private AjustesDeEquipo Recoger() => new()
    {
        Via = Via?.Via ?? ViaDeControl.Ninguna,
        DetectarElPuerto = DetectarElPuerto,
        Modelo = Fabricante?.Fabricante is null ? CatalogoDeModelos.Automatico : Modelo?.Modelo.Clave ?? CatalogoDeModelos.Automatico,
        DireccionCiv = byte.TryParse(DireccionCiv.Trim().Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var civ) ? civ : null,
        Puerto = Puerto?.Nombre,
        Baudios = Velocidad?.Baudios ?? 38400,
        SondeoMs = SondeoMs,
        EsperaDeOrdenMs = EsperaDeOrdenMs,
        PttPor = PttPor?.Via ?? ViaDePtt.Cat,
        MaquinaDeRigctld = MaquinaDeRigctld,
        PuertoDeRigctld = PuertoDeRigctld,
        EquipoDeOmniRig = EquipoDeOmniRig,
        TiempoMaximoSegundos = TiempoMaximoSegundos,
        TiempoSinLatidoSegundos = TiempoSinLatidoSegundos,
    };

    /// <summary>Pasa a la pantalla lo que hay guardado.</summary>
    private void RecogerDeLosAjustes()
    {
        var equipo = _ajustes.Equipo;

        Via = Vias.FirstOrDefault(v => v.Via == equipo.Via) ?? Vias[0];
        DetectarElPuerto = equipo.DetectarElPuerto;
        var modelo = CatalogoDeModelos.Buscar(equipo.Modelo);
        Fabricante = Fabricantes.FirstOrDefault(f => f.Fabricante == modelo?.Fabricante) ?? Fabricantes[0];
        Modelo = ModelosDelFabricante.FirstOrDefault(m => m.Modelo == modelo) ?? ModelosDelFabricante.FirstOrDefault();
        DireccionCiv = equipo.DireccionCiv is { } d ? d.ToString("X2", CultureInfo.InvariantCulture) : string.Empty;
        Velocidad = Velocidades.FirstOrDefault(v => v.Baudios == equipo.Baudios) ?? Velocidades[^1];

        // Tambien el puerto: Descartar lo dejaba en el ultimo que se hubiera tocado.
        Puerto = Puertos.FirstOrDefault(p => string.Equals(p.Nombre, equipo.Puerto, StringComparison.OrdinalIgnoreCase))
                 ?? Puerto;
        SondeoMs = equipo.SondeoMs;
        EsperaDeOrdenMs = equipo.EsperaDeOrdenMs;
        PttPor = ViasDePtt.FirstOrDefault(v => v.Via == equipo.PttPor) ?? ViasDePtt[0];
        MaquinaDeRigctld = equipo.MaquinaDeRigctld;
        PuertoDeRigctld = equipo.PuertoDeRigctld;
        EquipoDeOmniRig = equipo.EquipoDeOmniRig;
        TiempoMaximoSegundos = equipo.TiempoMaximoSegundos;
        TiempoSinLatidoSegundos = equipo.TiempoSinLatidoSegundos;

        RefrescarViaPuesta();
    }
}

/// <summary>Una forma de subir el PTT, escrita para el desplegable.</summary>
/// <param name="Via">Forma de subir el PTT.</param>
/// <param name="Titulo">Como se llama en pantalla.</param>
/// <param name="Explicacion">Cuando se usa.</param>
public sealed record ViaDePttElegible(ViaDePtt Via, string ClaveDelTitulo, string ClaveDeLaExplicacion) : OpcionTraducida
{
    /// <summary>Como se llama en pantalla.</summary>
    public string Titulo => Textos.T(ClaveDelTitulo);

    /// <summary>Cuando se usa.</summary>
    public string Explicacion => Textos.T(ClaveDeLaExplicacion);
}
