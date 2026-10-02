namespace Nodisla.Cuaderno.Aplicacion.Puertos;

/// <summary>
/// Guarda los secretos de los servicios: contrasenas, claves de API y frases de paso.
/// </summary>
/// <remarks>
/// <para>
/// El <c>config.ini</c> de Log4OM guarda la clave de API de Club Log en claro, legible por
/// cualquiera que abra el fichero. Aqui no: la implementacion de produccion cifra con la
/// proteccion de datos del usuario de Windows (DPAPI), de modo que el fichero solo lo puede
/// descifrar la cuenta que lo escribio.
/// </para>
/// <para>
/// Esta en los puertos, y no en la capa de servicios, porque quien escribe los secretos es la
/// interfaz —el operador los teclea una vez— y quien los lee son los servicios. Ningun secreto
/// se escribe en el registro de ajustes de la aplicacion ni se vuelve a mostrar en pantalla.
/// </para>
/// </remarks>
public interface IAlmacenDeCredenciales
{
    /// <summary>Devuelve el secreto guardado con esa clave, o nulo si no hay ninguno.</summary>
    /// <param name="clave">Clave del secreto, por ejemplo <c>lotw.contrasena</c>.</param>
    string? Leer(string clave);

    /// <summary>Guarda o reemplaza un secreto.</summary>
    /// <param name="clave">Clave del secreto.</param>
    /// <param name="secreto">Valor en claro; se cifra antes de tocar el disco.</param>
    void Guardar(string clave, string secreto);

    /// <summary>Borra un secreto. No falla si no existia.</summary>
    /// <param name="clave">Clave del secreto.</param>
    void Borrar(string clave);

    /// <summary>Indica si hay un secreto guardado con esa clave, sin descifrarlo.</summary>
    /// <param name="clave">Clave del secreto.</param>
    bool Existe(string clave);
}

/// <summary>Claves con las que cada servicio guarda sus secretos.</summary>
/// <remarks>
/// Estan centralizadas para que ningun servicio se invente la suya, para que la interfaz sepa
/// que tiene que pedirle al operador, y para poder auditar de un vistazo que secretos maneja el
/// programa.
/// </remarks>
public static class ClavesDeCredencial
{
    /// <summary>Contrasena de la cuenta de LoTW (ARRL), para descargar el informe.</summary>
    public const string LotwContrasena = "lotw.contrasena";

    /// <summary>
    /// Frase de paso del certificado de LoTW, si el certificado la lleva.
    /// </summary>
    /// <remarks>
    /// <b>Avise al operador antes de pedirla:</b> TQSL solo admite la frase de paso en la linea
    /// de ordenes, donde queda visible en la lista de procesos mientras dura la subida. Es
    /// decision del operador, pero tiene que tomarla informado.
    /// </remarks>
    public const string TqslFraseDePaso = "tqsl.frase";

    /// <summary>Contrasena de eQSL.cc.</summary>
    public const string EqslContrasena = "eqsl.contrasena";

    /// <summary>Contrasena (o contrasena de aplicacion) de la cuenta de Club Log.</summary>
    public const string ClubLogContrasena = "clublog.contrasena";

    /// <summary>Clave de API de Club Log. Se pide al soporte de Club Log; no se comparte.</summary>
    public const string ClubLogApi = "clublog.api";

    /// <summary>Contrasena de QRZ.com, para la consulta de indicativos por XML.</summary>
    public const string QrzContrasena = "qrz.contrasena";

    /// <summary>Clave del cuaderno de QRZ.com, distinta de la contrasena de la cuenta.</summary>
    public const string QrzClaveDeCuaderno = "qrz.cuaderno.clave";

    /// <summary>Contrasena de HamQTH.</summary>
    public const string HamQthContrasena = "hamqth.contrasena";

    /// <summary>
    /// Contrasena del nodo de cluster, para los que la piden.
    /// </summary>
    /// <remarks>
    /// La mayoria de los nodos no pide contrasena y basta con el indicativo, pero los que la
    /// piden la mandan en claro por Telnet. Que viaje en claro por el aire no es motivo para
    /// guardarla en claro en el disco: va aqui, cifrada, y en el fichero de ajustes no se
    /// escribe nunca.
    /// </remarks>
    public const string ClusterContrasena = "cluster.contrasena";

    /// <summary>
    /// Identificador del nodo de cluster que habia antes de poder tener varios.
    /// </summary>
    /// <remarks>
    /// Su contrasena se sigue guardando con <see cref="ClusterContrasena"/>: asi la que ya
    /// estaba guardada vale sin moverla de sitio.
    /// </remarks>
    public const string NodoDeClusterPrincipal = "principal";

    /// <summary>Clave de la contrasena de un nodo de cluster concreto.</summary>
    /// <param name="idDelNodo">Identificador del nodo en los ajustes.</param>
    /// <returns>La clave con la que se guarda en el almacen cifrado.</returns>
    public static string ContrasenaDeNodoDeCluster(string idDelNodo) =>
        string.IsNullOrWhiteSpace(idDelNodo) || idDelNodo == NodoDeClusterPrincipal
            ? ClusterContrasena
            : $"{ClusterContrasena}.{idDelNodo}";

    /// <summary>Contraseña del servidor de correo saliente con el que se mandan las QSL.</summary>
    public const string SmtpContrasena = "smtp.contrasena";
}
