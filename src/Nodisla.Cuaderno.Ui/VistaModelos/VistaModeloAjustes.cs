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
    [ObservableProperty]
    private string _nuevo = string.Empty;

    /// <summary>Hay un secreto guardado con esa clave.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Estado))]
    private bool _guardado;

    /// <summary>Estado, escrito para el operador.</summary>
    public string Estado => Guardado ? "Guardada y cifrada" : "Sin guardar";

    /// <summary>Guarda lo tecleado y vacia el campo.</summary>
    [RelayCommand]
    public void Guardar()
    {
        if (string.IsNullOrWhiteSpace(Nuevo)) return;

        _almacen.Guardar(Clave, Nuevo);
        Nuevo = string.Empty;
        Refrescar();
    }

    /// <summary>Borra el secreto guardado.</summary>
    [RelayCommand]
    public void Borrar()
    {
        _almacen.Borrar(Clave);
        Nuevo = string.Empty;
        Refrescar();
    }

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
    public VistaModeloAjustes(
        IAlmacenDeCredenciales credenciales,
        ImportarAdif importar,
        IRepositorioQso cuaderno,
        string avisoDeLaFraseDePaso,
        Func<string?> motivoDeNoPoderSubir)
    {
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

    /// <summary>Los secretos de los servicios, por orden.</summary>
    public IReadOnlyList<SecretoDeServicio> Secretos { get; }

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

    /// <summary>Se esta importando.</summary>
    [ObservableProperty]
    private bool _ocupado;

    /// <summary>Trae las cifras del cuaderno y vuelve a mirar si LoTW esta listo.</summary>
    [RelayCommand]
    public async Task RefrescarAsync()
    {
        RefrescarLotw();

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
    /// que justifica todo el trabajo de fusion, y es lo primero que Jose querra mirar al meter
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
