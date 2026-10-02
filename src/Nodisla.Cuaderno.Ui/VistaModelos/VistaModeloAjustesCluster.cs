using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Dxcc;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Integraciones.Cluster;
using Nodisla.Cuaderno.Ui.Ajustes;
using Serilog;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>
/// El apartado del cluster en la pantalla de ajustes: a que nodo se entra y con que indicativo.
/// </summary>
/// <remarks>
/// <para>
/// Antes de esto, el nodo y el indicativo estaban <b>escritos en el codigo</b> del registro de
/// servicios, sin contrasena y sin pantalla donde cambiarlos. Aqui se eligen, se guardan y se
/// aplican sin cerrar el programa.
/// </para>
/// <para>
/// <b>La contrasena no se guarda con lo demas.</b> Va al almacen cifrado, igual que las de los
/// servicios de confirmacion, y una vez escrita no se vuelve a enseñar. En el fichero de
/// ajustes no aparece nunca.
/// </para>
/// <para>
/// El indicativo de partida sale del <b>perfil de estacion activo</b>, no de una constante:
/// quien opera como EA8DLF/P desde el Teide no tiene por que acordarse de cambiarlo aqui.
/// </para>
/// </remarks>
public sealed partial class VistaModeloAjustesCluster : ObservableObject
{
    private readonly AjustesDelPrograma _ajustes;
    private readonly string _carpetaDeDatos;
    private readonly IAlmacenDeCredenciales _credenciales;
    private readonly IRepositorioEstacion _estaciones;
    private readonly IResolutorDxcc _dxcc;
    private readonly FuenteSpotsConmutable _conmutable;
    private readonly Action? _alCambiarLaFuente;

    /// <summary>Monta el apartado.</summary>
    /// <param name="ajustes">Ajustes del programa, ya leidos del disco.</param>
    /// <param name="carpetaDeDatos">Carpeta donde se guardan.</param>
    /// <param name="credenciales">Almacen cifrado, donde va la contrasena del nodo.</param>
    /// <param name="estaciones">Perfiles de estacion, de donde sale el indicativo de partida.</param>
    /// <param name="dxcc">Resolutor de entidades, que necesita la fuente de anuncios.</param>
    /// <param name="conmutable">Intermediario que sabe cambiar la fuente de anuncios.</param>
    /// <param name="alCambiarLaFuente">
    /// Que hacer cuando se cambia de nodo, para que el panel del cluster se entere del nombre
    /// nuevo. Puede ser nulo en pruebas.
    /// </param>
    public VistaModeloAjustesCluster(
        AjustesDelPrograma ajustes,
        string carpetaDeDatos,
        IAlmacenDeCredenciales credenciales,
        IRepositorioEstacion estaciones,
        IResolutorDxcc dxcc,
        FuenteSpotsConmutable conmutable,
        Action? alCambiarLaFuente = null)
    {
        ArgumentNullException.ThrowIfNull(ajustes);
        ArgumentException.ThrowIfNullOrWhiteSpace(carpetaDeDatos);

        _ajustes = ajustes;
        _carpetaDeDatos = carpetaDeDatos;
        _credenciales = credenciales ?? throw new ArgumentNullException(nameof(credenciales));
        _estaciones = estaciones ?? throw new ArgumentNullException(nameof(estaciones));
        _dxcc = dxcc ?? throw new ArgumentNullException(nameof(dxcc));
        _conmutable = conmutable ?? throw new ArgumentNullException(nameof(conmutable));
        _alCambiarLaFuente = alCambiarLaFuente;

        Nodos = NodosConocidos.Todos;

        RecogerDeLosAjustes();
        RefrescarLaContrasena();

        // El estado de la contraseña se escribe en el idioma nuevo.
        Textos.AlCambiar(this, static vm => vm.OnPropertyChanged(nameof(EstadoDeLaContrasena)));
    }

    /// <summary>Nodos que se ofrecen hechos.</summary>
    public IReadOnlyList<NodoConocido> Nodos { get; }

    /// <summary>Nodo elegido de la lista. Elegir uno rellena servidor y puerto.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotaDelNodo))]
    private NodoConocido? _nodo;

    /// <summary>Nombre del nodo, el que se ve en pantalla.</summary>
    [ObservableProperty]
    private string _nombre = "Cluster de DX";

    /// <summary>Maquina a la que conectarse.</summary>
    [ObservableProperty]
    private string _servidor = string.Empty;

    /// <summary>Puerto de Telnet.</summary>
    [ObservableProperty]
    private int _puerto = 7300;

    /// <summary>
    /// Indicativo con el que se entra. Vacio quiere decir «el del perfil de estacion activo».
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IndicativoDeAcceso))]
    private string _indicativo = string.Empty;

    /// <summary>Sufijo del indicativo: <c>1</c> se manda como <c>EA8DLF-1</c>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IndicativoDeAcceso))]
    private string _sufijo = string.Empty;

    /// <summary>Indicativo del perfil de estacion activo.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IndicativoDeAcceso))]
    private string _indicativoDelPerfil = string.Empty;

    /// <summary>Ordenes que se mandan al conectar, una por linea.</summary>
    [ObservableProperty]
    private string _guionDeArranque = string.Empty;

    /// <summary>Volver a conectar solo cuando se cae la conexion.</summary>
    [ObservableProperty]
    private bool _reconectarSolo = true;

    /// <summary>Lo que se espera a que el socket abra, en segundos.</summary>
    [ObservableProperty]
    private int _esperaDeConexionSegundos = 20;

    /// <summary>Espera antes del primer reintento, en segundos.</summary>
    [ObservableProperty]
    private int _primerReintentoSegundos = 5;

    /// <summary>Tope de la espera entre reintentos, en segundos.</summary>
    [ObservableProperty]
    private int _reintentoMaximoSegundos = 300;

    /// <summary>Tiempo sin recibir nada tras el cual se da la conexion por muerta, en minutos.</summary>
    [ObservableProperty]
    private int _silencioMaximoMinutos = 15;

    /// <summary>Lo que el operador acaba de teclear como contrasena. Se vacia al guardar.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarLaContrasenaCommand))]
    private string _contrasenaNueva = string.Empty;

    /// <summary>Hay una contrasena guardada para el cluster.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstadoDeLaContrasena))]
    [NotifyCanExecuteChangedFor(nameof(BorrarLaContrasenaCommand))]
    private bool _contrasenaGuardada;

    /// <summary>Lo que ha pasado con la ultima aplicacion.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayParte))]
    private string _parte = string.Empty;

    /// <summary>La ultima aplicacion fallo.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayParte))]
    private bool _fallo;

    /// <summary>Se esta aplicando.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AplicarCommand))]
    private bool _ocupado;

    /// <summary>Hay algo que contar de la ultima aplicacion.</summary>
    public bool HayParte => Parte.Length > 0;

    /// <summary>Estado de la contrasena, escrito para el operador.</summary>
    public string EstadoDeLaContrasena => Textos.T(ContrasenaGuardada ? "Ajustes.Secreto.Guardada" : "Ajustes.Secreto.SinGuardar");

    /// <summary>Que tiene de particular el nodo elegido.</summary>
    public string NotaDelNodo => Nodo?.Nota ?? string.Empty;

    /// <summary>Con que indicativo se va a entrar de verdad, sufijo incluido.</summary>
    public string IndicativoDeAcceso
    {
        get
        {
            var indicativo = string.IsNullOrWhiteSpace(Indicativo) ? IndicativoDelPerfil : Indicativo.Trim();
            if (indicativo.Length == 0) return "—";

            var sufijo = Sufijo.Trim().TrimStart('-');
            return sufijo.Length == 0 ? indicativo.ToUpperInvariant() : $"{indicativo.ToUpperInvariant()}-{sufijo}";
        }
    }

    /// <summary>Trae el indicativo del perfil de estacion activo.</summary>
    /// <returns>La tarea de la consulta.</returns>
    public async Task CargarElPerfilAsync()
    {
        try
        {
            var perfil = await _estaciones.PredeterminadaAsync().ConfigureAwait(true);
            IndicativoDelPerfil = perfil?.StationCallsign.Valor ?? string.Empty;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "No se ha podido leer el perfil de estación para el cluster.");
        }
    }

    private bool _rellenandoDesdeElNodo;

    /// <summary>Rellena servidor, puerto y nombre con los del nodo elegido.</summary>
    partial void OnNodoChanged(NodoConocido? value)
    {
        if (value is null) return;

        _rellenandoDesdeElNodo = true;
        try
        {
            Nombre = value.Nombre;
            Servidor = value.Servidor;
            Puerto = value.Puerto;
        }
        finally
        {
            _rellenandoDesdeElNodo = false;
        }
    }

    partial void OnServidorChanged(string value) => SoltarElNodoSiYaNoEs();

    partial void OnPuertoChanged(int value) => SoltarElNodoSiYaNoEs();

    /// <summary>
    /// Si se teclea otro servidor u otro puerto, el desplegable deja de decir que es un nodo
    /// conocido.
    /// </summary>
    /// <remarks>
    /// Antes el desplegable se quedaba en el nodo de antes aunque se hubiera borrado el servidor,
    /// y volver a elegir ese mismo nodo no hacia nada: no cambiaba la eleccion y no rellenaba.
    /// </remarks>
    private void SoltarElNodoSiYaNoEs()
    {
        if (_rellenandoDesdeElNodo || Nodo is null) return;

        if (!string.Equals(Nodo.Servidor, Servidor?.Trim(), StringComparison.OrdinalIgnoreCase) || Nodo.Puerto != Puerto)
        {
            Nodo = Nodos.FirstOrDefault(n =>
                string.Equals(n.Servidor, Servidor?.Trim(), StringComparison.OrdinalIgnoreCase) && n.Puerto == Puerto);
        }
    }

    /// <summary>Guarda la contrasena del nodo en el almacen cifrado.</summary>
    [RelayCommand(CanExecute = nameof(HayContrasenaTecleada))]
    public void GuardarLaContrasena()
    {
        if (string.IsNullOrWhiteSpace(ContrasenaNueva)) return;

        _credenciales.Guardar(ClavesDeCredencial.ClusterContrasena, ContrasenaNueva);
        ContrasenaNueva = string.Empty;
        RefrescarLaContrasena();
    }

    /// <summary>Borra la contrasena guardada.</summary>
    [RelayCommand(CanExecute = nameof(ContrasenaGuardada))]
    public void BorrarLaContrasena()
    {
        _credenciales.Borrar(ClavesDeCredencial.ClusterContrasena);
        ContrasenaNueva = string.Empty;
        RefrescarLaContrasena();
    }

    /// <summary>
    /// Guarda los ajustes y cambia la fuente de anuncios sin cerrar el programa.
    /// </summary>
    [RelayCommand(CanExecute = nameof(SePuedeTrabajar))]
    public async Task AplicarAsync()
    {
        Ocupado = true;

        try
        {
            if (string.IsNullOrWhiteSpace(Servidor))
            {
                Fallo = true;
                Parte = Textos.T("Ajustes.Cluster.FaltaServidor");
                return;
            }

            var texto = string.IsNullOrWhiteSpace(Indicativo) ? IndicativoDelPerfil : Indicativo.Trim();
            if (string.IsNullOrWhiteSpace(texto))
            {
                Fallo = true;
                Parte = Textos.T("Ajustes.Cluster.SinIndicativo");
                return;
            }

            Dominio.Valores.Indicativo indicativo;
            try
            {
                indicativo = Dominio.Valores.Indicativo.Parse(texto);
            }
            catch (Exception ex)
            {
                Fallo = true;
                Parte = Textos.F("Ajustes.Cluster.IndicativoNoVale", texto, ex.Message);
                return;
            }

            var cluster = Recoger();
            _ajustes.Cluster = cluster;
            _ajustes.Guardar(_carpetaDeDatos);

            var contrasena = _credenciales.Leer(ClavesDeCredencial.ClusterContrasena);
            var opciones = cluster.AOpcionesDeCluster(indicativo, contrasena);

            await _conmutable.SustituirAsync(new ClusterTelnet(opciones, _dxcc)).ConfigureAwait(true);
            _alCambiarLaFuente?.Invoke();

            Fallo = false;
            Parte = Textos.F("Ajustes.Cluster.Aplicado", opciones.Nombre, opciones.Servidor, opciones.Puerto, opciones.IndicativoDeAcceso);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se han podido aplicar los ajustes del cluster.");
            Fallo = true;
            Parte = Textos.F("Ajustes.NoSeHanPodidoAplicar", ex.Message);
        }
        finally
        {
            Ocupado = false;
            OnPropertyChanged(nameof(HayParte));
        }
    }

    /// <summary>Vuelve a poner en pantalla lo que hay guardado.</summary>
    [RelayCommand]
    public void Descartar()
    {
        RecogerDeLosAjustes();
        Parte = string.Empty;
        Fallo = false;
        OnPropertyChanged(nameof(HayParte));
    }

    private bool SePuedeTrabajar() => !Ocupado;

    private bool HayContrasenaTecleada() => !string.IsNullOrWhiteSpace(ContrasenaNueva);

    private void RefrescarLaContrasena() =>
        ContrasenaGuardada = _credenciales.Existe(ClavesDeCredencial.ClusterContrasena);

    private AjustesDeCluster Recoger() => new()
    {
        Nombre = Nombre,
        Servidor = Servidor.Trim(),
        Puerto = Puerto,
        Indicativo = string.IsNullOrWhiteSpace(Indicativo) ? null : Indicativo.Trim().ToUpperInvariant(),
        Sufijo = string.IsNullOrWhiteSpace(Sufijo) ? null : Sufijo.Trim().TrimStart('-'),
        GuionDeArranque = [.. GuionDeArranque
            .Split('\n')
            .Select(linea => linea.Trim())
            .Where(linea => linea.Length > 0)],
        ReconectarSolo = ReconectarSolo,
        EsperaDeConexionSegundos = EsperaDeConexionSegundos,
        PrimerReintentoSegundos = PrimerReintentoSegundos,
        ReintentoMaximoSegundos = ReintentoMaximoSegundos,
        SilencioMaximoMinutos = SilencioMaximoMinutos,
    };

    private void RecogerDeLosAjustes()
    {
        var cluster = _ajustes.Cluster;

        Nombre = cluster.Nombre;
        Servidor = cluster.Servidor;
        Puerto = cluster.Puerto;
        Indicativo = cluster.Indicativo ?? string.Empty;
        Sufijo = cluster.Sufijo ?? string.Empty;
        GuionDeArranque = string.Join(Environment.NewLine, cluster.GuionDeArranque);
        ReconectarSolo = cluster.ReconectarSolo;
        EsperaDeConexionSegundos = cluster.EsperaDeConexionSegundos;
        PrimerReintentoSegundos = cluster.PrimerReintentoSegundos;
        ReintentoMaximoSegundos = cluster.ReintentoMaximoSegundos;
        SilencioMaximoMinutos = cluster.SilencioMaximoMinutos;

        Nodo = Nodos.FirstOrDefault(n =>
            string.Equals(n.Servidor, cluster.Servidor, StringComparison.OrdinalIgnoreCase)
            && n.Puerto == cluster.Puerto);
    }
}
