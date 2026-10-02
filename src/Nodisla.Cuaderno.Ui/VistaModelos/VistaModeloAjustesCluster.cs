using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Integraciones.Cluster;
using Nodisla.Cuaderno.Ui.Ajustes;
using Nodisla.Cuaderno.Ui.Conversores;
using Serilog;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>
/// El apartado del cluster en la pantalla de ajustes: a que nodos se entra, con que
/// indicativo y como van ahora mismo.
/// </summary>
/// <remarks>
/// <para>
/// Se puede estar en <b>varios nodos a la vez</b>: cada uno trae lo que le llega por su red, y
/// juntos traen mas. Los anuncios repetidos se juntan en una sola fila (lo hace
/// <see cref="SeguirElCluster"/>), y por eso aqui se ajustan tambien la tolerancia y la
/// ventana con las que se juntan.
/// </para>
/// <para>
/// <b>Las contrasenas no se guardan con lo demas.</b> Cada nodo tiene la suya en el almacen
/// cifrado, y una vez escrita no se vuelve a ensenar. En el fichero de ajustes no aparece nunca.
/// </para>
/// <para>
/// El indicativo de partida sale del <b>perfil de estacion activo</b>, no de una constante:
/// quien opera como EA8DLF/P desde el Teide no tiene por que acordarse de cambiarlo aqui.
/// </para>
/// </remarks>
public sealed partial class VistaModeloAjustesCluster : ObservableObject
{
    /// <summary>Cada cuanto se repasa el ritmo de anuncios aunque no avise la fuente.</summary>
    private static readonly TimeSpan Repaso = TimeSpan.FromSeconds(5);

    private readonly AjustesDelPrograma _ajustes;
    private readonly string? _carpetaDeDatos;
    private readonly IAlmacenDeCredenciales _credenciales;
    private readonly IRepositorioEstacion _estaciones;
    private readonly FuenteDeVariosNodos _fuente;
    private readonly SeguirElCluster? _seguimiento;
    private readonly Func<string, int, CancellationToken, Task<PruebaDeNodo>> _probar;
    private readonly Action? _alCambiarLaFuente;
    private readonly HashSet<string> _quitados = new(StringComparer.Ordinal);
    private readonly ITimer _repaso;

    /// <summary>Monta el apartado.</summary>
    /// <param name="ajustes">Ajustes del programa, ya leidos del disco.</param>
    /// <param name="carpetaDeDatos">Carpeta donde se guardan; nula, no se guardan (simulado).</param>
    /// <param name="credenciales">Almacen cifrado, donde va la contrasena de cada nodo.</param>
    /// <param name="estaciones">Perfiles de estacion, de donde sale el indicativo de partida.</param>
    /// <param name="fuente">Fuente de varios nodos que se reconfigura al aplicar.</param>
    /// <param name="seguimiento">Seguimiento del cluster, para cambiarle la junta de repetidos.</param>
    /// <param name="probar">
    /// Como se prueba un nodo. Por omision se abre el puerto sin entrar; con los simulados, nada.
    /// </param>
    /// <param name="alCambiarLaFuente">
    /// Que hacer al aplicar, para que el panel del cluster se entere. Puede ser nulo en pruebas.
    /// </param>
    public VistaModeloAjustesCluster(
        AjustesDelPrograma ajustes,
        string? carpetaDeDatos,
        IAlmacenDeCredenciales credenciales,
        IRepositorioEstacion estaciones,
        FuenteDeVariosNodos fuente,
        SeguirElCluster? seguimiento = null,
        Func<string, int, CancellationToken, Task<PruebaDeNodo>>? probar = null,
        Action? alCambiarLaFuente = null)
    {
        ArgumentNullException.ThrowIfNull(ajustes);

        _ajustes = ajustes;
        _carpetaDeDatos = string.IsNullOrWhiteSpace(carpetaDeDatos) ? null : carpetaDeDatos;
        _credenciales = credenciales ?? throw new ArgumentNullException(nameof(credenciales));
        _estaciones = estaciones ?? throw new ArgumentNullException(nameof(estaciones));
        _fuente = fuente ?? throw new ArgumentNullException(nameof(fuente));
        _seguimiento = seguimiento;
        _probar = probar ?? ((servidor, puerto, ct) => ProbadorDeNodo.ProbarAsync(servidor, puerto, null, ct));
        _alCambiarLaFuente = alCambiarLaFuente;

        NodosConocidos = Integraciones.Cluster.NodosConocidos.Todos;

        RecogerDeLosAjustes();
        RefrescarEstados();

        _fuente.NodosCambiaron += AlCambiarLosNodos;
        _repaso = TimeProvider.System.CreateTimer(_ => Hilo.EnLaVentana(RefrescarEstados), null, Repaso, Repaso);

        // Los estados y el resumen se escriben en el idioma nuevo.
        Textos.AlCambiar(this, static vm =>
        {
            vm.OnPropertyChanged(nameof(EstadoDeLaContrasena));
            vm.OnPropertyChanged(nameof(ResumenDeNodos));
            foreach (var nodo in vm.Nodos) nodo.RefrescarTextos();
        });
    }

    /// <summary>Nodos que se ofrecen hechos, para anadir con un clic.</summary>
    public IReadOnlyList<NodoConocido> NodosConocidos { get; }

    /// <summary>Nodos de la lista, en el orden en que se ensenan.</summary>
    public ObservableCollection<NodoDeClusterEnAjustes> Nodos { get; } = [];

    /// <summary>Nodo conocido elegido en el desplegable para anadirlo.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotaDelNodo))]
    [NotifyCanExecuteChangedFor(nameof(AnadirConocidoCommand))]
    private NodoConocido? _nodoConocido;

    /// <summary>Nodo de la lista que se esta editando.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayNodoSeleccionado))]
    [NotifyPropertyChangedFor(nameof(ContrasenaGuardada))]
    [NotifyPropertyChangedFor(nameof(EstadoDeLaContrasena))]
    [NotifyCanExecuteChangedFor(nameof(GuardarLaContrasenaCommand))]
    [NotifyCanExecuteChangedFor(nameof(BorrarLaContrasenaCommand))]
    private NodoDeClusterEnAjustes? _nodoSeleccionado;

    /// <summary>
    /// Indicativo comun con el que se entra. Vacio quiere decir «el del perfil de estacion activo».
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IndicativoDeAcceso))]
    private string _indicativo = string.Empty;

    /// <summary>Sufijo comun del indicativo: <c>1</c> se manda como <c>EA8DLF-1</c>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IndicativoDeAcceso))]
    private string _sufijo = string.Empty;

    /// <summary>Indicativo del perfil de estacion activo.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IndicativoDeAcceso))]
    private string _indicativoDelPerfil = string.Empty;

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

    /// <summary>Diferencia de frecuencia que aun es la misma estacion, en kilohercios.</summary>
    [ObservableProperty]
    private decimal _toleranciaDeRepetidosKhz = 1.0m;

    /// <summary>Minutos durante los que se juntan los repetidos.</summary>
    [ObservableProperty]
    private int _ventanaDeRepetidosMinutos = 10;

    /// <summary>Lo que el operador acaba de teclear como contrasena del nodo elegido.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarLaContrasenaCommand))]
    private string _contrasenaNueva = string.Empty;

    /// <summary>Lo que ha pasado con la ultima accion.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayParte))]
    private string _parte = string.Empty;

    /// <summary>La ultima accion fallo.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayParte))]
    private bool _fallo;

    /// <summary>Se esta aplicando.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AplicarCommand))]
    private bool _ocupado;

    /// <summary>Hay algo que contar de la ultima accion.</summary>
    public bool HayParte => Parte.Length > 0;

    /// <summary>Hay un nodo elegido para editar.</summary>
    public bool HayNodoSeleccionado => NodoSeleccionado is not null;

    /// <summary>Que tiene de particular el nodo conocido elegido.</summary>
    public string NotaDelNodo => NodoConocido?.Nota ?? string.Empty;

    /// <summary>Hay una contrasena guardada para el nodo elegido.</summary>
    public bool ContrasenaGuardada => NodoSeleccionado?.ContrasenaGuardada ?? false;

    /// <summary>Estado de la contrasena del nodo elegido, escrito para el operador.</summary>
    public string EstadoDeLaContrasena =>
        Textos.T(ContrasenaGuardada ? "Ajustes.Secreto.Guardada" : "Ajustes.Secreto.SinGuardar");

    /// <summary>Algun nodo tiene contrasena guardada.</summary>
    public bool HayContrasenas => Nodos.Any(n => n.ContrasenaGuardada);

    /// <summary>Cuantos nodos estan activos.</summary>
    public int NodosActivos => Nodos.Count(n => n.Activo);

    /// <summary>Cuantos nodos estan conectados ahora mismo.</summary>
    public int NodosConectados => Nodos.Count(n => n.Estado == EstadoDeConexion.Conectado);

    /// <summary>«5 nodos · 3 conectados».</summary>
    public string ResumenDeNodos => Textos.F("Principal.Cluster.ResumenNodos", Nodos.Count, NodosConectados);

    /// <summary>Con que indicativo se entra de verdad, sufijo incluido.</summary>
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

    /// <summary>Anade a la lista el nodo conocido elegido, listo para aplicar.</summary>
    [RelayCommand(CanExecute = nameof(HayNodoConocido))]
    public void AnadirConocido()
    {
        if (NodoConocido is not { } conocido) return;

        var repetido = Nodos.FirstOrDefault(n =>
            string.Equals(n.Servidor.Trim(), conocido.Servidor, StringComparison.OrdinalIgnoreCase)
            && n.Puerto == conocido.Puerto);
        if (repetido is not null)
        {
            NodoSeleccionado = repetido;
            Avisar(Textos.F("Ajustes.Cluster.YaEnLaLista", repetido.Nombre), fallo: true);
            return;
        }

        var nodo = new NodoDeClusterEnAjustes(new AjustesDeNodoDeCluster
        {
            Nombre = NombreLibre(conocido.Nombre),
            Servidor = conocido.Servidor,
            Puerto = conocido.Puerto,
            EsSkimmer = conocido.EsSkimmer,
            GuionDeArranque = [.. conocido.GuionRecomendado],
        });
        Anadir(nodo);
    }

    /// <summary>Anade un nodo en blanco, para escribir uno que no esta en la lista.</summary>
    [RelayCommand]
    public void AnadirNodo()
    {
        var nodo = new NodoDeClusterEnAjustes(new AjustesDeNodoDeCluster
        {
            Nombre = NombreLibre(Textos.T("Ajustes.Cluster.NodoNuevo")),
        });
        Anadir(nodo);
    }

    /// <summary>Quita un nodo de la lista. Se cierra al aplicar.</summary>
    /// <param name="nodo">Nodo que se quita; si es nulo, el elegido.</param>
    [RelayCommand]
    public void QuitarNodo(NodoDeClusterEnAjustes? nodo)
    {
        var cual = nodo ?? NodoSeleccionado;
        if (cual is null) return;

        var indice = Nodos.IndexOf(cual);
        cual.PropertyChanged -= AlCambiarUnNodo;
        Nodos.Remove(cual);
        _quitados.Add(cual.Id);
        NodoSeleccionado = Nodos.Count == 0 ? null : Nodos[Math.Clamp(indice, 0, Nodos.Count - 1)];
        AvisarDeLaLista();
    }

    /// <summary>Conecta un nodo, sin tocar los demas.</summary>
    /// <param name="nodo">Nodo que se conecta.</param>
    [RelayCommand]
    public async Task ConectarNodoAsync(NodoDeClusterEnAjustes? nodo)
    {
        if (nodo is null) return;
        if (!nodo.Aplicado)
        {
            Avisar(Textos.F("Ajustes.Cluster.AplicaPrimero", nodo.Nombre), fallo: true);
            return;
        }

        try
        {
            await _fuente.ConectarNodoAsync(nodo.Id).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "No se ha podido conectar el nodo {Nodo}.", nodo.Nombre);
            Avisar(Textos.F("Principal.Cluster.NoSePudoConectar", ex.Message), fallo: true);
        }
        RefrescarEstados();
    }

    /// <summary>Desconecta un nodo, sin tocar los demas.</summary>
    /// <param name="nodo">Nodo que se desconecta.</param>
    [RelayCommand]
    public async Task DesconectarNodoAsync(NodoDeClusterEnAjustes? nodo)
    {
        if (nodo is null || !nodo.Aplicado) return;

        try
        {
            await _fuente.DesconectarNodoAsync(nodo.Id).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Fallo al desconectar el nodo {Nodo}.", nodo.Nombre);
            Avisar(Textos.F("Principal.Cluster.FalloAlDesconectar", ex.Message), fallo: true);
        }
        RefrescarEstados();
    }

    /// <summary>
    /// Comprueba si el nodo contesta, sin entrar: abre el puerto, lee el saludo y cierra.
    /// </summary>
    /// <param name="nodo">Nodo que se prueba.</param>
    [RelayCommand]
    public async Task ProbarNodoAsync(NodoDeClusterEnAjustes? nodo)
    {
        if (nodo is null) return;
        if (string.IsNullOrWhiteSpace(nodo.Servidor) || nodo.Puerto is < 1 or > 65535)
        {
            nodo.PruebaBien = false;
            nodo.Prueba = Textos.F("Ajustes.Cluster.NodoSinServidor", nodo.Nombre);
            return;
        }

        nodo.PruebaBien = false;
        nodo.Prueba = Textos.T("Ajustes.Cluster.Probando");
        try
        {
            var prueba = await _probar(nodo.Servidor.Trim(), nodo.Puerto, CancellationToken.None).ConfigureAwait(true);
            var milisegundos = Math.Round(prueba.Tiempo.TotalMilliseconds);
            nodo.PruebaBien = prueba.Responde;
            nodo.Prueba = !prueba.Responde
                ? Textos.F("Ajustes.Cluster.NoResponde", prueba.Error ?? string.Empty)
                : prueba.Saludo is { Length: > 0 } saludo
                    ? Textos.F("Ajustes.Cluster.RespondeConSaludo", milisegundos, saludo)
                    : Textos.F("Ajustes.Cluster.Responde", milisegundos);
        }
        catch (Exception ex)
        {
            nodo.PruebaBien = false;
            nodo.Prueba = Textos.F("Ajustes.Cluster.NoResponde", ex.Message);
        }
    }

    /// <summary>Guarda la contrasena del nodo elegido en el almacen cifrado.</summary>
    [RelayCommand(CanExecute = nameof(SePuedeGuardarLaContrasena))]
    public void GuardarLaContrasena()
    {
        if (NodoSeleccionado is null || string.IsNullOrWhiteSpace(ContrasenaNueva)) return;

        _credenciales.Guardar(NodoSeleccionado.ClaveDeContrasena, ContrasenaNueva);
        ContrasenaNueva = string.Empty;
        RefrescarLasContrasenas();
    }

    /// <summary>Borra la contrasena guardada del nodo elegido.</summary>
    [RelayCommand(CanExecute = nameof(ContrasenaGuardada))]
    public void BorrarLaContrasena()
    {
        if (NodoSeleccionado is null) return;

        _credenciales.Borrar(NodoSeleccionado.ClaveDeContrasena);
        ContrasenaNueva = string.Empty;
        RefrescarLasContrasenas();
    }

    /// <summary>
    /// Guarda los ajustes y pone la lista nueva en marcha sin cerrar el programa.
    /// </summary>
    /// <remarks>
    /// Los nodos que no han cambiado siguen conectados; solo se cierran y se abren los que
    /// han cambiado de maquina, de acceso o de guion, y se cierran los quitados.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(SePuedeTrabajar))]
    public async Task AplicarAsync()
    {
        Ocupado = true;

        try
        {
            foreach (var nodo in Nodos)
            {
                if (string.IsNullOrWhiteSpace(nodo.Servidor))
                {
                    NodoSeleccionado = nodo;
                    Avisar(Textos.F("Ajustes.Cluster.NodoSinServidor", nodo.Nombre), fallo: true);
                    return;
                }

                if (nodo.Puerto is < 1 or > 65535)
                {
                    NodoSeleccionado = nodo;
                    Avisar(Textos.F("Ajustes.Cluster.PuertoNoVale", nodo.Nombre), fallo: true);
                    return;
                }
            }

            var texto = string.IsNullOrWhiteSpace(Indicativo) ? IndicativoDelPerfil : Indicativo.Trim();
            if (string.IsNullOrWhiteSpace(texto))
            {
                Avisar(Textos.T("Ajustes.Cluster.SinIndicativo"), fallo: true);
                return;
            }

            Dominio.Valores.Indicativo indicativo;
            try
            {
                indicativo = Dominio.Valores.Indicativo.Parse(texto);
            }
            catch (Exception ex)
            {
                Avisar(Textos.F("Ajustes.Cluster.IndicativoNoVale", texto, ex.Message), fallo: true);
                return;
            }

            var cluster = Recoger();
            _ajustes.Cluster = cluster;
            if (_carpetaDeDatos is not null) _ajustes.Guardar(_carpetaDeDatos);

            // Las contrasenas de los nodos quitados no se quedan huerfanas en el almacen.
            foreach (var id in _quitados.Where(id => cluster.Nodos.TrueForAll(n => n.Id != id)))
            {
                _credenciales.Borrar(ClavesDeCredencial.ContrasenaDeNodoDeCluster(id));
            }
            _quitados.Clear();

            var opciones = cluster.Nodos
                .Select(n => cluster.AOpcionesDeNodo(n, indicativo, _credenciales.Leer(n.ClaveDeContrasena)))
                .ToList();
            await _fuente.AplicarAsync(opciones).ConfigureAwait(true);
            _seguimiento?.CambiarCriterioDeRepetidos(cluster.VentanaAcotada, cluster.ToleranciaAcotada);
            _alCambiarLaFuente?.Invoke();

            RefrescarEstados();
            Avisar(
                Textos.F("Ajustes.Cluster.AplicadoVarios", cluster.Nodos.Count, NodosActivos, IndicativoDeAcceso),
                fallo: false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se han podido aplicar los ajustes del cluster.");
            Avisar(Textos.F("Ajustes.NoSeHanPodidoAplicar", ex.Message), fallo: true);
        }
        finally
        {
            Ocupado = false;
        }
    }

    /// <summary>Vuelve a poner en pantalla lo que hay guardado.</summary>
    [RelayCommand]
    public void Descartar()
    {
        _quitados.Clear();
        RecogerDeLosAjustes();
        RefrescarEstados();
        Parte = string.Empty;
        Fallo = false;
    }

    /// <summary>Repasa el estado en vivo de cada nodo.</summary>
    public void RefrescarEstados()
    {
        var vivos = _fuente.Nodos;
        foreach (var nodo in Nodos)
        {
            nodo.PonerEstado(vivos.FirstOrDefault(v => v.Id == nodo.Id));
        }

        OnPropertyChanged(nameof(NodosConectados));
        OnPropertyChanged(nameof(ResumenDeNodos));
    }

    /// <summary>Deja de escuchar a la fuente. Lo llama la ventana al cerrarse.</summary>
    public void Detener()
    {
        _fuente.NodosCambiaron -= AlCambiarLosNodos;
        _repaso.Dispose();
    }

    partial void OnNodoSeleccionadoChanged(NodoDeClusterEnAjustes? value) => ContrasenaNueva = string.Empty;

    private bool HayNodoConocido() => NodoConocido is not null;

    private bool SePuedeTrabajar() => !Ocupado;

    private bool SePuedeGuardarLaContrasena() =>
        NodoSeleccionado is not null && !string.IsNullOrWhiteSpace(ContrasenaNueva);

    private void AlCambiarLosNodos(object? origen, EventArgs e) => Hilo.EnLaVentana(RefrescarEstados);

    private void Anadir(NodoDeClusterEnAjustes nodo)
    {
        nodo.ContrasenaGuardada = _credenciales.Existe(nodo.ClaveDeContrasena);
        nodo.PropertyChanged += AlCambiarUnNodo;
        Nodos.Add(nodo);
        NodoSeleccionado = nodo;
        AvisarDeLaLista();
    }

    private void AlCambiarUnNodo(object? origen, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NodoDeClusterEnAjustes.Activo) or nameof(NodoDeClusterEnAjustes.ContrasenaGuardada))
        {
            AvisarDeLaLista();
        }
    }

    private void AvisarDeLaLista()
    {
        OnPropertyChanged(nameof(NodosActivos));
        OnPropertyChanged(nameof(NodosConectados));
        OnPropertyChanged(nameof(ResumenDeNodos));
        OnPropertyChanged(nameof(HayContrasenas));
    }

    private void Avisar(string texto, bool fallo)
    {
        Fallo = fallo;
        Parte = texto;
    }

    /// <summary>Un nombre que no este ya en la lista: «DXFun», «DXFun (2)»...</summary>
    private string NombreLibre(string nombre)
    {
        var candidato = nombre;
        for (var n = 2; Nodos.Any(x => string.Equals(x.Nombre, candidato, StringComparison.OrdinalIgnoreCase)); n++)
        {
            candidato = $"{nombre} ({n})";
        }
        return candidato;
    }

    private void RefrescarLasContrasenas()
    {
        foreach (var nodo in Nodos) nodo.ContrasenaGuardada = _credenciales.Existe(nodo.ClaveDeContrasena);

        OnPropertyChanged(nameof(ContrasenaGuardada));
        OnPropertyChanged(nameof(EstadoDeLaContrasena));
        BorrarLaContrasenaCommand.NotifyCanExecuteChanged();
    }

    private AjustesDeCluster Recoger() => new()
    {
        Nodos = [.. Nodos.Select(n => n.AGuardar())],
        Indicativo = string.IsNullOrWhiteSpace(Indicativo) ? null : Indicativo.Trim().ToUpperInvariant(),
        Sufijo = string.IsNullOrWhiteSpace(Sufijo) ? null : Sufijo.Trim().TrimStart('-'),
        EsperaDeConexionSegundos = EsperaDeConexionSegundos,
        PrimerReintentoSegundos = PrimerReintentoSegundos,
        ReintentoMaximoSegundos = ReintentoMaximoSegundos,
        SilencioMaximoMinutos = SilencioMaximoMinutos,
        ToleranciaDeRepetidosKhz = ToleranciaDeRepetidosKhz,
        VentanaDeRepetidosMinutos = VentanaDeRepetidosMinutos,
    };

    private void RecogerDeLosAjustes()
    {
        var cluster = _ajustes.Cluster;

        foreach (var nodo in Nodos) nodo.PropertyChanged -= AlCambiarUnNodo;
        Nodos.Clear();
        foreach (var guardado in cluster.Nodos)
        {
            var nodo = new NodoDeClusterEnAjustes(guardado)
            {
                ContrasenaGuardada = _credenciales.Existe(guardado.ClaveDeContrasena),
            };
            nodo.PropertyChanged += AlCambiarUnNodo;
            Nodos.Add(nodo);
        }

        Indicativo = cluster.Indicativo ?? string.Empty;
        Sufijo = cluster.Sufijo ?? string.Empty;
        EsperaDeConexionSegundos = cluster.EsperaDeConexionSegundos;
        PrimerReintentoSegundos = cluster.PrimerReintentoSegundos;
        ReintentoMaximoSegundos = cluster.ReintentoMaximoSegundos;
        SilencioMaximoMinutos = cluster.SilencioMaximoMinutos;
        ToleranciaDeRepetidosKhz = cluster.ToleranciaDeRepetidosKhz;
        VentanaDeRepetidosMinutos = cluster.VentanaDeRepetidosMinutos;

        NodoSeleccionado = Nodos.FirstOrDefault();
        AvisarDeLaLista();
    }
}
