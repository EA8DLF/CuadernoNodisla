using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Serilog;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>
/// Un secreto de un servicio, tal y como se maneja en la pantalla de ajustes.
/// </summary>
/// <remarks>
/// <b>Lo escrito no se vuelve a ensenar.</b> El almacen cifra con la proteccion de datos del
/// usuario de Windows y solo sabe decir si hay algo guardado, no que es. La pantalla ensena
/// «guardada» o «sin guardar» y el campo siempre sale vacio: un secreto que se vuelve a pintar
/// en pantalla es un secreto que alguien puede leer por encima del hombro.
/// </remarks>
public sealed partial class SecretoDeServicio : ObservableObject
{
    private readonly IAlmacenDeCredenciales _almacen;

    /// <summary>Monta la fila del secreto.</summary>
    /// <param name="almacen">Almacen cifrado.</param>
    /// <param name="clave">Clave con la que se guarda.</param>
    /// <param name="servicio">Servicio al que pertenece.</param>
    /// <param name="titulo">Como se llama en pantalla.</param>
    /// <param name="explicacion">Que es, para el operador.</param>
    public SecretoDeServicio(
        IAlmacenDeCredenciales almacen,
        string clave,
        string servicio,
        string titulo,
        string explicacion)
    {
        _almacen = almacen ?? throw new ArgumentNullException(nameof(almacen));
        Clave = clave;
        Servicio = servicio;
        Titulo = titulo;
        Explicacion = explicacion;

        Refrescar();
    }

    /// <summary>Clave con la que se guarda.</summary>
    public string Clave { get; }

    /// <summary>Servicio al que pertenece.</summary>
    public string Servicio { get; }

    /// <summary>Como se llama en pantalla.</summary>
    public string Titulo { get; }

    /// <summary>Que es, para el operador.</summary>
    public string Explicacion { get; }

    /// <summary>Lo que el operador acaba de teclear. Se vacia al guardar.</summary>
    /// <remarks>
    /// La casilla de la pantalla escucha este valor: cuando se vacia al guardar o al borrar,
    /// se vacian tambien sus puntos. Antes los puntos se quedaban y parecia que no se habia
    /// guardado nada.
    /// </remarks>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string _nuevo = string.Empty;

    /// <summary>Hay un secreto guardado con esa clave.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Estado))]
    [NotifyCanExecuteChangedFor(nameof(BorrarCommand))]
    private bool _guardado;

    /// <summary>Estado, escrito para el operador.</summary>
    public string Estado => Guardado ? "Guardada y cifrada" : "Sin guardar";

    /// <summary>Guarda lo tecleado y vacia el campo.</summary>
    [RelayCommand(CanExecute = nameof(HayAlgoTecleado))]
    public void Guardar()
    {
        if (string.IsNullOrWhiteSpace(Nuevo)) return;

        _almacen.Guardar(Clave, Nuevo);
        Nuevo = string.Empty;
        Refrescar();
    }

    /// <summary>Borra el secreto guardado.</summary>
    [RelayCommand(CanExecute = nameof(Guardado))]
    public void Borrar()
    {
        _almacen.Borrar(Clave);
        Nuevo = string.Empty;
        Refrescar();
    }

    private bool HayAlgoTecleado() => !string.IsNullOrWhiteSpace(Nuevo);

    private void Refrescar() => Guardado = _almacen.Existe(Clave);
}

/// <summary>
/// La pantalla de ajustes: credenciales, LoTW, equipo y cuaderno.
/// </summary>
/// <remarks>
/// <para>
/// Aqui va lo que se toca una vez y se olvida, y lo que hace falta para diagnosticar. Lo que
/// se mira operando —el estado de las conexiones— se queda en la pantalla de operar.
/// </para>
/// <para>
/// Las credenciales <b>se guardan cifradas y no se vuelven a ensenar</b>. El
/// <c>config.ini</c> del programa original guarda la clave de API de Club Log en claro; aqui
/// el almacen solo sabe decir si hay algo guardado.
/// </para>
/// </remarks>
public sealed partial class VistaModeloAjustes : ObservableObject
{
    private readonly ImportarAdif _importar;
    private readonly IRepositorioQso _cuaderno;
    private readonly Func<string?> _motivoDeNoPoderSubir;
    private readonly IEscritorAdif? _escritor;

    /// <summary>Contactos que se piden de una vez al exportar.</summary>
    private const int PaginaDeExportacion = 1000;

    /// <summary>Monta la pantalla.</summary>
    /// <param name="credenciales">Almacen cifrado de secretos.</param>
    /// <param name="importar">Caso de uso de importacion de ADIF.</param>
    /// <param name="cuaderno">Repositorio, para las cifras del cuaderno.</param>
    /// <param name="avisoDeLaFraseDePaso">
    /// Aviso que hay que ensenar <b>antes</b> de pedir la frase de paso del certificado.
    /// </param>
    /// <param name="motivoDeNoPoderSubir">
    /// Por que no se puede subir a LoTW ahora mismo, o nulo si si se puede.
    /// </param>
    /// <param name="cat">
    /// El apartado del control del equipo. Nulo con los puertos simulados, donde no hay equipo
    /// de verdad que configurar.
    /// </param>
    /// <param name="cluster">El apartado de la conexion al cluster. Nulo con los simulados.</param>
    /// <param name="audio">
    /// El apartado de audio y modos digitales: por donde entra el sonido y quien decodifica.
    /// </param>
    /// <param name="escritor">
    /// Escritor de ADIF para exportar el cuaderno. Sin el, el boton sale apagado y lo dice.
    /// </param>
    public VistaModeloAjustes(
        IAlmacenDeCredenciales credenciales,
        ImportarAdif importar,
        IRepositorioQso cuaderno,
        string avisoDeLaFraseDePaso,
        Func<string?> motivoDeNoPoderSubir,
        VistaModeloAjustesCat? cat = null,
        VistaModeloAjustesCluster? cluster = null,
        VistaModeloAjustesAudio? audio = null,
        IEscritorAdif? escritor = null)
    {
        _escritor = escritor;
        Cat = cat;
        Cluster = cluster;
        Audio = audio;

        ArgumentNullException.ThrowIfNull(credenciales);

        _importar = importar ?? throw new ArgumentNullException(nameof(importar));
        _cuaderno = cuaderno ?? throw new ArgumentNullException(nameof(cuaderno));
        _motivoDeNoPoderSubir = motivoDeNoPoderSubir ?? throw new ArgumentNullException(nameof(motivoDeNoPoderSubir));

        AvisoDeLaFraseDePaso = avisoDeLaFraseDePaso;

        Secretos =
        [
            new(credenciales, ClavesDeCredencial.LotwContrasena, "LoTW", "Contraseña de LoTW",
                "La de su cuenta de la ARRL. Se usa para descargar el informe de confirmaciones."),
            new(credenciales, ClavesDeCredencial.TqslFraseDePaso, "LoTW", "Frase de paso del certificado",
                "Solo si su certificado la lleva. Lea el aviso de abajo antes de escribirla."),
            new(credenciales, ClavesDeCredencial.EqslContrasena, "eQSL", "Contraseña de eQSL.cc",
                "La de su cuenta de eQSL.cc."),
            new(credenciales, ClavesDeCredencial.ClubLogContrasena, "Club Log", "Contraseña de Club Log",
                "La de su cuenta, o una contraseña de aplicación si la tiene."),
            new(credenciales, ClavesDeCredencial.ClubLogApi, "Club Log", "Clave de API de Club Log",
                "Se pide al soporte de Club Log y no se comparte con nadie."),
            new(credenciales, ClavesDeCredencial.QrzContrasena, "QRZ.com", "Contraseña de QRZ.com",
                "Para la consulta de indicativos por XML."),
            new(credenciales, ClavesDeCredencial.QrzClaveDeCuaderno, "QRZ.com", "Clave del cuaderno de QRZ",
                "Es distinta de la contraseña de la cuenta: la da QRZ para subir contactos."),
            new(credenciales, ClavesDeCredencial.HamQthContrasena, "HamQTH", "Contraseña de HamQTH",
                "La de su cuenta de HamQTH."),
        ];

        RefrescarLotw();
    }

    /// <summary>
    /// La subida automatica y el completado con QRZ: cuentas, casillas y cola. Nulo si no se
    /// ha montado (pruebas).
    /// </summary>
    public VistaModeloSubidas? Subidas { get; init; }

    /// <summary>Los secretos de los servicios, por orden.</summary>
    public IReadOnlyList<SecretoDeServicio> Secretos { get; }

    /// <summary>
    /// El apartado CAT: por donde se habla con el equipo.
    /// </summary>
    /// <remarks>
    /// Es lo que faltaba y por lo que el boton Conectar no conectaba: la via de control estaba
    /// escrita en el codigo y venia en «Ninguna».
    /// </remarks>
    public VistaModeloAjustesCat? Cat { get; }

    /// <summary>El apartado del cluster: a que nodo se entra y con que indicativo.</summary>
    public VistaModeloAjustesCluster? Cluster { get; }

    /// <summary>
    /// El apartado de audio y modos digitales: por donde entra el sonido y quien decodifica.
    /// </summary>
    /// <remarks>
    /// Es donde se elige entre el modem propio y el puente con WSJT-X, y donde se ve el nivel
    /// de entrada con su aviso de saturacion.
    /// </remarks>
    public VistaModeloAjustesAudio? Audio { get; }

    /// <summary>Apartado de fonía por el PC, o nulo si no se registró.</summary>
    public VistaModeloAjustesFonia? Fonia { get; init; }

    /// <summary>Apartado del correo con el que se mandan las QSL, o nulo si no se registró.</summary>
    public VistaModeloCorreoQsl? CorreoQsl { get; init; }

    /// <summary>Hay apartado de audio que enseñar.</summary>
    public bool HayAudio => Audio is not null;

    /// <summary>Hay apartado de equipo que enseñar.</summary>
    public bool HayCat => Cat is not null;

    /// <summary>Hay apartado de cluster que enseñar.</summary>
    public bool HayCluster => Cluster is not null;

    /// <summary>
    /// Aviso sobre la frase de paso, que se ensena <b>antes</b> de pedirla.
    /// </summary>
    /// <remarks>
    /// TQSL solo admite la frase por linea de ordenes, donde queda visible en la lista de
    /// procesos mientras dura la subida. No se puede evitar; lo que si se puede es decirlo
    /// antes y no despues.
    /// </remarks>
    public string AvisoDeLaFraseDePaso { get; }

    /// <summary>Por que no se puede subir a LoTW. Vacio cuando si se puede.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SePuedeSubirALotw))]
    private string _motivoDeNoPoderSubirALotw = string.Empty;

    /// <summary>Se puede subir a LoTW ahora mismo.</summary>
    public bool SePuedeSubirALotw => MotivoDeNoPoderSubirALotw.Length == 0;

    /// <summary>Contactos que hay en el cuaderno.</summary>
    [ObservableProperty]
    private string _contactosDelCuaderno = "—";

    /// <summary>Donde esta el cuaderno.</summary>
    [ObservableProperty]
    private string _rutaDelCuaderno = string.Empty;

    /// <summary>Lo que ha pasado con la ultima importacion o exportacion.</summary>
    [ObservableProperty]
    private string _parteDeLaImportacion = string.Empty;

    /// <summary>La ultima importacion dejo choques que conviene revisar.</summary>
    [ObservableProperty]
    private bool _hayChoques;

    /// <summary>Choques de la ultima importacion, escritos.</summary>
    public System.Collections.ObjectModel.ObservableCollection<string> Choques { get; } = [];

    /// <summary>Se esta importando o exportando.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportarAdifCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportarAdifCommand))]
    private bool _ocupado;

    /// <summary>
    /// Pregunta que fichero importar. La pone la pantalla (es un dialogo de Windows); las
    /// pruebas ponen otra que no abre nada. Nula, o respuesta nula: no se hace nada.
    /// </summary>
    public Func<string?>? ElegirFicheroParaImportar { get; set; }

    /// <summary>Pregunta donde exportar. Igual que <see cref="ElegirFicheroParaImportar"/>.</summary>
    public Func<string?>? ElegirFicheroParaExportar { get; set; }

    /// <summary>Hay escritor de ADIF con el que exportar.</summary>
    public bool SePuedeExportar => _escritor is not null;

    /// <summary>Pide el fichero e importa.</summary>
    [RelayCommand(CanExecute = nameof(EstaLibre))]
    private async Task ImportarAdifAsync()
    {
        if (ElegirFicheroParaImportar?.Invoke() is not { Length: > 0 } ruta) return;
        await ImportarAsync(ruta).ConfigureAwait(true);
    }

    /// <summary>Pide donde y exporta el cuaderno entero.</summary>
    [RelayCommand(CanExecute = nameof(SePuedeExportarAhora))]
    private async Task ExportarAdifAsync()
    {
        if (ElegirFicheroParaExportar?.Invoke() is not { Length: > 0 } ruta) return;
        await ExportarAsync(ruta).ConfigureAwait(true);
    }

    private bool EstaLibre() => !Ocupado;

    private bool SePuedeExportarAhora() => !Ocupado && _escritor is not null;

    /// <summary>
    /// Exporta el cuaderno entero a un fichero ADIF, del contacto mas antiguo al mas nuevo.
    /// </summary>
    /// <param name="ruta">Fichero de destino.</param>
    /// <remarks>
    /// Se escribe primero en un fichero de al lado y solo al terminar bien se pone en su sitio:
    /// una exportacion que falla a medias no puede dejar destrozado un respaldo que ya
    /// existiera con ese nombre. Antes este boton decia «no se ha escrito» y no hacia nada.
    /// </remarks>
    /// <returns>La tarea de la exportacion.</returns>
    public async Task ExportarAsync(string ruta)
    {
        if (string.IsNullOrWhiteSpace(ruta)) return;

        if (_escritor is null)
        {
            ParteDeLaImportacion = "No hay escritor de ADIF en esta sesión: el fichero no se ha escrito.";
            return;
        }

        Ocupado = true;
        Choques.Clear();
        HayChoques = false;
        ParteDeLaImportacion = "Exportando…";

        var temporal = ruta + ".escribiendo";
        var escritos = 0;

        async IAsyncEnumerable<Dominio.Entidades.Qso> Todos()
        {
            var desplazamiento = 0;
            while (true)
            {
                var pagina = await _cuaderno
                    .BuscarAsync(new CriterioQso { Descendente = false }, desplazamiento, PaginaDeExportacion)
                    .ConfigureAwait(false);

                foreach (var qso in pagina.Elementos)
                {
                    escritos++;
                    yield return qso;
                }

                if (pagina.Elementos.Count < PaginaDeExportacion) yield break;
                desplazamiento += pagina.Elementos.Count;
            }
        }

        try
        {
            await using (var fichero = File.Create(temporal))
            {
                await _escritor.EscribirAsync(Todos(), fichero).ConfigureAwait(true);
            }

            File.Move(temporal, ruta, overwrite: true);
            ParteDeLaImportacion = string.Create(
                CultureInfo.CurrentCulture,
                $"{Path.GetFileName(ruta)}: {escritos:N0} contactos exportados a ADIF.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido exportar el cuaderno a {Ruta}.", ruta);
            ParteDeLaImportacion = $"No se ha podido exportar: {ex.Message} El fichero no se ha escrito.";
            try
            {
                if (File.Exists(temporal)) File.Delete(temporal);
            }
            catch (IOException)
            {
                // Un temporal que no se deja borrar no tapa el fallo de verdad.
            }
        }
        finally
        {
            Ocupado = false;
        }
    }

    /// <summary>Trae las cifras del cuaderno y vuelve a mirar si LoTW esta listo.</summary>
    [RelayCommand]
    public async Task RefrescarAsync()
    {
        RefrescarLotw();

        // El indicativo de partida del cluster sale del perfil de estacion activo, y el perfil
        // se puede haber cambiado desde que se monto la pantalla.
        if (Cluster is not null) await Cluster.CargarElPerfilAsync().ConfigureAwait(true);
        Cat?.RefrescarViaPuesta();

        try
        {
            var total = await _cuaderno.ContarAsync().ConfigureAwait(true);
            ContactosDelCuaderno = total.ToString("N0", CultureInfo.CurrentCulture);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se han podido contar los contactos del cuaderno.");
            ContactosDelCuaderno = "no se ha podido contar";
        }
    }

    /// <summary>
    /// Importa un fichero ADIF y ensena el parte completo.
    /// </summary>
    /// <param name="ruta">Fichero que se importa.</param>
    /// <remarks>
    /// El parte no es un adorno: dice cuantos registros se fundieron y <b>cuantas
    /// confirmaciones se han rescatado</b> al fundir en vez de saltar la copia. Esa cifra es la
    /// que justifica todo el trabajo de fusion, y es lo primero que el operador querra mirar al meter
    /// su respaldo.
    /// </remarks>
    public async Task ImportarAsync(string ruta)
    {
        if (string.IsNullOrWhiteSpace(ruta)) return;

        Ocupado = true;
        Choques.Clear();
        HayChoques = false;
        ParteDeLaImportacion = "Importando…";

        try
        {
            await using var fichero = File.OpenRead(ruta);
            var parte = await _importar.EjecutarAsync(fichero).ConfigureAwait(true);

            ParteDeLaImportacion = Escribir(parte, ruta);

            foreach (var choque in parte.Choques.Take(50))
            {
                Choques.Add(choque.ToString() ?? string.Empty);
            }

            HayChoques = Choques.Count > 0;

            await RefrescarAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido importar el ADIF {Ruta}.", ruta);
            ParteDeLaImportacion = $"No se ha podido importar: {ex.Message}";
        }
        finally
        {
            Ocupado = false;
        }
    }

    private static string Escribir(ResultadoDeImportacion parte, string ruta)
    {
        var texto = new System.Text.StringBuilder();

        texto.Append(CultureInfo.CurrentCulture, $"{Path.GetFileName(ruta)}: ");
        texto.Append(CultureInfo.CurrentCulture, $"{parte.RegistrosLeidos:N0} registros leídos, ");
        texto.Append(CultureInfo.CurrentCulture, $"{parte.Anadidos:N0} nuevos, ");
        texto.Append(CultureInfo.CurrentCulture, $"{parte.Fundidos:N0} fundidos ");
        texto.Append(CultureInfo.CurrentCulture, $"({parte.FundidosEnElFichero:N0} dentro del fichero y ");
        texto.Append(CultureInfo.CurrentCulture, $"{parte.FundidosConElCuaderno:N0} contra el cuaderno), ");
        texto.Append(CultureInfo.CurrentCulture, $"{parte.YaEstaban:N0} ya estaban.");

        if (parte.ConfirmacionesRecuperadas > 0)
        {
            texto.Append(CultureInfo.CurrentCulture,
                $" Se han rescatado {parte.ConfirmacionesRecuperadas:N0} confirmaciones que se habrían perdido descartando las copias.");
        }

        if (parte.ProgramaOrigen is { Length: > 0 } programa)
        {
            texto.Append(CultureInfo.CurrentCulture, $" Lo generó {programa}.");
        }

        texto.Append(CultureInfo.CurrentCulture, $" Ha tardado {parte.Duracion.TotalSeconds:N1} s.");

        if (!parte.NoSePierdeNada)
        {
            texto.Append(" AVISO: las cuentas no cuadran, hay registros que no se pueden explicar. Revise el fichero.");
        }

        if (parte.Avisos.Count > 0)
        {
            texto.Append(CultureInfo.CurrentCulture, $" Con {parte.Avisos.Count:N0} aviso(s) de lectura.");
        }

        return texto.ToString();
    }

    private void RefrescarLotw()
    {
        try
        {
            MotivoDeNoPoderSubirALotw = _motivoDeNoPoderSubir() ?? string.Empty;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido comprobar si LoTW está listo.");
            MotivoDeNoPoderSubirALotw = $"No se ha podido comprobar el estado de LoTW: {ex.Message}";
        }
    }
}
