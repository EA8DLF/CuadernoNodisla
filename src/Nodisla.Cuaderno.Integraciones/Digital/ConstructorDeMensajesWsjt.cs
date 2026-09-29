using Nodisla.Cuaderno.Aplicacion.Puertos;

namespace Nodisla.Cuaderno.Integraciones.Digital;

/// <summary>
/// Arma los datagramas que se envian al programa de modos digitales.
/// </summary>
/// <remarks>
/// Son pocos y casi todos comunes, salvo dos detalles que hay que respetar o no pasa nada de
/// nada: la respuesta de JTDX termina en el mensaje, sin los dos campos que WSJT-X anadio
/// despues, y el resaltado de indicativos no existe fuera de WSJT-X.
/// </remarks>
public static class ConstructorDeMensajesWsjt
{
    /// <summary>Esquema con el que se escribe, que es el que entienden todos desde hace anos.</summary>
    public const uint Esquema = 3;

    private static EscritorQDataStream Cabecera(TipoMensajeWsjt tipo, string identificador)
    {
        var escritor = new EscritorQDataStream();
        escritor.UInt32(AnalizadorWsjt.Magia);
        escritor.UInt32(Esquema);
        escritor.UInt32((uint)tipo);
        escritor.Texto(identificador);
        return escritor;
    }

    /// <summary>Latido con el que el cuaderno se presenta ante el programa.</summary>
    /// <param name="identificador">Nombre con el que se identifica el cuaderno.</param>
    /// <param name="version">Version del cuaderno.</param>
    /// <param name="revision">Revision del cuaderno.</param>
    public static byte[] Latido(string identificador, string version, string revision)
    {
        var escritor = Cabecera(TipoMensajeWsjt.Latido, identificador);
        escritor.UInt32(Esquema);
        escritor.Texto(version);
        escritor.Texto(revision);
        return escritor.ABytes();
    }

    /// <summary>
    /// Pide que se llame a la estacion de una decodificacion, que es lo mismo que hace el
    /// operador al pinchar dos veces sobre ella en la ventana del programa.
    /// </summary>
    /// <remarks>
    /// Este es el unico mensaje divergente que <b>enviamos</b> nosotros, y por eso el unico en
    /// el que equivocarse tiene consecuencias: el Reply de JTDX se quedo en la version que no
    /// llevaba ni el logico de baja confianza ni el byte de modificadores, asi que mandarle el
    /// de WSJT-X seria anadirle dos bytes de basura al final.
    /// </remarks>
    /// <param name="decodificacion">Decodificacion a la que se contesta.</param>
    /// <param name="dialecto">Dialecto de la instancia, que decide como termina el mensaje.</param>
    /// <param name="bajaConfianza">Marca de decodificacion dudosa. JTDX no lee este campo.</param>
    /// <param name="modificadores">
    /// Teclas de modificacion que se simulan, con el mismo significado que en la ventana del
    /// programa. JTDX tampoco lee este campo.
    /// </param>
    public static byte[] Respuesta(
        DecodificacionDigital decodificacion,
        DialectoDigital dialecto,
        bool bajaConfianza = false,
        byte modificadores = 0)
    {
        ArgumentNullException.ThrowIfNull(decodificacion);

        var escritor = Cabecera(TipoMensajeWsjt.Respuesta, decodificacion.Identificador);
        escritor.Hora(decodificacion.InstanteUtc.ToUniversalTime().TimeOfDay);
        escritor.Int32(decodificacion.Decibelios);
        escritor.Doble(decodificacion.DesfaseSegundos);
        escritor.UInt32((uint)Math.Max(0, decodificacion.TonoHz));

        // Aqui va el indicador tal y como vino, no el modo: el otro extremo reconstruye con el
        // la linea de su ventana para saber a que decodificacion nos referimos. Mandarle el
        // nombre del modo cuando el habia dicho «~» seria no encontrar nada.
        escritor.Texto(decodificacion.IndicadorDelPrograma ?? string.Empty);
        escritor.Texto(decodificacion.Texto);

        if (dialecto != DialectoDigital.Jtdx)
        {
            escritor.Logico(bajaConfianza);
            escritor.Byte(modificadores);
        }

        return escritor.ABytes();
    }

    /// <summary>
    /// Resalta un indicativo en la ventana del programa. Solo WSJT-X lo entiende.
    /// </summary>
    /// <param name="identificador">Instancia a la que se le pide.</param>
    /// <param name="indicativo">Indicativo que hay que resaltar.</param>
    /// <param name="esNuevo">
    /// Cierto para pintarlo como estacion nueva; falso para quitarle el resaltado, que se pide
    /// enviando colores invalidos.
    /// </param>
    public static byte[] Resaltar(string identificador, string indicativo, bool esNuevo)
    {
        var escritor = Cabecera(TipoMensajeWsjt.ResaltarIndicativo, identificador);
        escritor.Texto(indicativo);
        if (esNuevo)
        {
            escritor.Color(255, 90, 90);   // fondo
            escritor.Color(0, 0, 0);       // texto
        }
        else
        {
            escritor.ColorInvalido();
            escritor.ColorInvalido();
        }
        escritor.Logico(true); // resaltar tambien las lineas siguientes
        return escritor.ABytes();
    }

    /// <summary>
    /// Cambia la configuracion activa del programa por su nombre. Mensaje 14, solo de WSJT-X.
    /// </summary>
    /// <remarks>
    /// JTDX dejo este numero reservado y nunca lo implemento, asi que mandarselo no da error:
    /// lo ignora en silencio.
    /// </remarks>
    /// <param name="identificador">Instancia a la que se le pide.</param>
    /// <param name="configuracion">Nombre de la configuracion, tal y como el programa la llama.</param>
    public static byte[] CambiarConfiguracion(string identificador, string configuracion)
    {
        var escritor = Cabecera(TipoMensajeWsjt.CambiarConfiguracion, identificador);
        escritor.Texto(configuracion);
        return escritor.ABytes();
    }

    /// <summary>Fija el desplazamiento de transmision en hercios. Mensaje 50, solo de JTDX.</summary>
    /// <param name="identificador">Instancia a la que se le pide.</param>
    /// <param name="hercios">Desplazamiento dentro del ancho de banda de audio.</param>
    public static byte[] FijarTxDeltaFreq(string identificador, uint hercios)
    {
        var escritor = Cabecera(TipoMensajeWsjt.FijarTxDeltaFreq, identificador);
        escritor.UInt32(hercios);
        return escritor.ABytes();
    }

    /// <summary>
    /// Fija la direccion y el periodo de la llamada general y, si se quiere, la dispara.
    /// Mensaje 51, solo de JTDX.
    /// </summary>
    /// <param name="identificador">Instancia a la que se le pide.</param>
    /// <param name="direccion">Direccion de la llamada: <c>DX</c>, <c>EU</c>, <c>NA</c>…</param>
    /// <param name="periodoTx">Periodo en el que transmitir.</param>
    /// <param name="enviar">Cierto para que empiece a llamar ya.</param>
    public static byte[] DispararCq(string identificador, string direccion, bool periodoTx, bool enviar)
    {
        var escritor = Cabecera(TipoMensajeWsjt.DispararCq, identificador);
        escritor.Texto(direccion);
        escritor.Logico(periodoTx);
        escritor.Logico(enviar);
        return escritor.ABytes();
    }
}
