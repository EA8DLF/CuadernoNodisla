using Nodisla.Cuaderno.Aplicacion.Puertos;

namespace Nodisla.Cuaderno.Modos.Marco;

/// <summary>
/// Un modo digital del modem propio: sabe decodificar una ventana de audio y generar la senal
/// de un mensaje.
/// </summary>
/// <remarks>
/// <para>
/// Es el contrato con el que se anaden modos al modem sin tocar los que ya hay: cada uno vive
/// en su carpeta (<c>Wspr/</c>, <c>Jt65/</c>, <c>Msk144/</c>...), con su decodificador, su
/// generador, sus tablas y su banco de medida, y se registra en <see cref="RegistroDeModos"/>.
/// FT8 y FT4 llegaron antes que este marco y siguen en <c>Ft8/</c>; se adaptan a el, no al reves.
/// </para>
/// <para>
/// Las dos reglas de <see cref="IModemPropio"/> mandan aqui igual: <b>no inventar</b>
/// (cero decodificaciones falsas, medidas en el banco de cada modo) y <b>transmitir solo por el
/// vigilante</b> (el generador devuelve muestras; quien las pone en antena es el modem).
/// </para>
/// <para>
/// <b>Licencias.</b> WSJT-X es GPLv3: no se copia ni una linea de su codigo. Se implementa
/// desde la descripcion publicada de cada protocolo (articulos de Taylor y Franke en QEX y
/// QST, documentacion del protocolo). Las <i>constantes</i> del protocolo (vectores de
/// sincronismo, matrices de los codigos) no son codigo y se traen con su procedencia escrita,
/// como se hizo con la tabla LDPC de FT8 (ft8_lib, MIT).
/// </para>
/// </remarks>
public interface IModoDigital
{
    /// <summary>
    /// Nivel de fabrica de <see cref="AmplitudDeSalida"/>: conservador (-10,5 dBFS), no el maximo
    /// razonable (0,5, -6 dBFS) que llevaba escrito a fuego antes de que existiera este ajuste.
    /// </summary>
    /// <remarks>
    /// Una grabacion en bruto del audio que este programa manda a la tarjeta, hecha con WASAPI
    /// loopback, salio limpia -envolvente constante, pico fijo, tono correcto- para un «golpes de
    /// ruido» que se oia de verdad en el aire. La señal en el ordenador era perfecta; lo que esa
    /// grabacion no puede ver es que, pasado el PC, el nivel sature la entrada de datos o la
    /// interfaz del equipo en analogico, algo muy real si esa entrada espera menos de -6 dBFS.
    /// JTDX tiene para esto su «Pwr»; este nivel por omision es deliberadamente mas bajo que el
    /// 0,5 de antes para no partir ya saturando a quien lo pruebe por primera vez.
    /// </remarks>
    public const double AmplitudDeSalidaPorDefecto = 0.3;

    /// <summary>Modo que implementa.</summary>
    ModoDelModem Modo { get; }

    /// <summary>
    /// Amplitud de pico (0 a 1) con la que <see cref="Generar"/> sintetiza la señal. El operador
    /// la sube o la baja desde los ajustes de digital (<c>AjustesDeDigital.NivelDeSalida</c>) y
    /// el modem se la fija a este modo justo antes de cada emision: el cambio surte efecto en la
    /// siguiente transmision, sin reiniciar nada ni reabrir la salida de audio.
    /// </summary>
    double AmplitudDeSalida { get; set; }

    /// <summary>
    /// Duracion de la ventana de transmision: 15 s en FT8, 7,5 s en FT4, 120 s en WSPR, 60 s en
    /// JT65 y JT9, la del submodo en Q65 y FST4, 15 s en MSK144.
    /// </summary>
    TimeSpan Periodo { get; }

    /// <summary>
    /// Corrimiento de las ventanas respecto a su alineacion natural. <b>Cero en todos los modos
    /// conocidos.</b>
    /// </summary>
    /// <remarks>
    /// Las ventanas se alinean a multiplos de <see cref="Periodo"/> contados desde las 00:00 UTC
    /// (ver <see cref="ComienzoDeVentana"/>). Como 7,5, 15, 30, 60, 120 y 300 segundos caben un
    /// numero entero de veces en un dia, eso da de por si lo que pide cada protocolo: las de 15 s
    /// en los segundos 0, 15, 30 y 45; las de 120 s en los minutos pares; las de 300 s en los
    /// multiplos de cinco minutos. El segundo 1 de WSPR o de Q65 no es un corrimiento de la
    /// ventana sino del comienzo de la senal dentro de ella: eso es <see cref="ComienzoDeLaSenal"/>.
    /// </remarks>
    TimeSpan ArranqueDentroDelPeriodo { get; }

    /// <summary>
    /// Segundo de la ventana en que empieza la senal por convenio: 0,5 s en FT8 y FT4, 1 s en
    /// WSPR y en Q65 de 60 s o mas. El modem espera eso desde el comienzo de la ventana antes de
    /// sacar la primera muestra al emitir.
    /// </summary>
    /// <remarks>
    /// Por omision, el convenio de toda la familia: <b>1 s en los periodos de 60 s o mas</b> (WSPR,
    /// JT65, JT9, Q65 y FST4 largos, FST4W) y <b>0,5 s en los de 30 s o menos</b>. Asi un modo nuevo
    /// sale bien sin declarar nada, y si cambia de periodo (FST4) el comienzo le sigue. Solo lo
    /// sobreescribe quien se salga del convenio. <c>ModemPropioModosPruebas</c> cruza este valor con
    /// el <c>ComienzoNominal</c> que declara cada modo.
    /// </remarks>
    TimeSpan ComienzoDeLaSenal =>
        Periodo >= TimeSpan.FromSeconds(60) ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(0.5);

    /// <summary>
    /// Si tiene sentido lanzarle una decodificacion de adelanto sobre un prefijo de la ventana,
    /// antes de que se cierre del todo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Por omision, no: solo lo aprovechan FT8 y FT4 (<see cref="Ft8.ModoFt8"/>), cuyo periodo es
    /// tan corto —15 y 7,5 s— que todo el trabajo de decodificar cae, hoy, despues de que la
    /// ventana cierre, comiendose el poco margen de reaccion que queda para evaluar novedad,
    /// avanzar la secuencia del QSO y emitir a tiempo.
    /// </para>
    /// <para>
    /// Los demas modos de serie —WSPR, JT65, JT9, Q65, FST4 y MSK144— tienen periodos de sesenta
    /// segundos o mas (MSK144 es la excepcion de periodo corto, pero decodifica ahi por ventana
    /// completa de una sola vez y no se ha medido ni pedido forzarle esto): no les hace falta
    /// este adelanto y nadie lo ha medido para ellos, asi que no se les aplica sin pedirlo.
    /// </para>
    /// </remarks>
    bool SoportaDecodificacionProgresiva => false;

    /// <summary>Muestras por segundo a las que quiere el audio para analizarlo.</summary>
    int FrecuenciaDeAnalisis { get; }

    /// <summary>
    /// Lo que el secuenciador de QSO ya sabe del contacto en curso, para la decodificacion AP.
    /// </summary>
    /// <remarks>
    /// Por omision no hay pista y no cambia nada: solo los modos que la aprovechen (FT8 y FT4,
    /// de momento) la guardan de verdad. Ver <see cref="PistaDeQso"/>.
    /// </remarks>
    PistaDeQso PistaDeQso { get => default; set { } }

    /// <summary>
    /// Decodifica una ventana completa de audio, ya a <see cref="FrecuenciaDeAnalisis"/>.
    /// </summary>
    /// <param name="audio">Muestras de la ventana entera, mono, normalizadas a [-1, 1].</param>
    /// <param name="ventanaUtc">Instante de arranque de la ventana.</param>
    /// <param name="ct">Testigo de cancelacion: si la siguiente ventana ya empezo, se para.</param>
    /// <returns>Lo decodificado. Ante la duda, nada.</returns>
    IReadOnlyList<DecodificacionPropia> Decodificar(
        ReadOnlySpan<float> audio,
        DateTimeOffset ventanaUtc,
        CancellationToken ct);

    /// <summary>
    /// Genera las muestras de un mensaje, a la frecuencia indicada, listas para la salida de
    /// audio. No pone nada en antena.
    /// </summary>
    /// <param name="mensaje">Mensaje con la gramatica del modo.</param>
    /// <param name="tonoHz">Tono base dentro del ancho de audio.</param>
    /// <param name="frecuenciaDeMuestreo">Muestras por segundo de la salida.</param>
    /// <exception cref="FormatException">Si el mensaje no cabe en la gramatica del modo.</exception>
    float[] Generar(string mensaje, int tonoHz, int frecuenciaDeMuestreo);

    /// <summary>
    /// Se llama al empezar a escuchar: el modo olvida lo que aprendio de la escucha anterior
    /// (indicativos vistos, promedios). Por omision no hace nada.
    /// </summary>
    void Reiniciar()
    {
    }

    /// <summary>
    /// Decodifica una ventana tal y como la junta el modem: audio a la frecuencia de captura y
    /// con un trozo de la ventana anterior delante.
    /// </summary>
    /// <param name="audio">Muestras, mono, a <paramref name="frecuenciaDeMuestreo"/>.</param>
    /// <param name="frecuenciaDeMuestreo">Muestras por segundo del audio tal y como llega.</param>
    /// <param name="ventanaUtc">Instante de arranque de la ventana.</param>
    /// <param name="segundosDelPrimerMuestreo">
    /// Instante de la primera muestra respecto al comienzo de la ventana: negativo si el audio
    /// trae preludio (lo normal, para pillar a quien transmite adelantado), positivo si la
    /// escucha empezo tarde.
    /// </param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <remarks>
    /// Por omision se remuestrea a <see cref="FrecuenciaDeAnalisis"/>, se quita el preludio (o
    /// se rellena con silencio lo que falte al principio) y se llama a <see cref="Decodificar"/>,
    /// que asi recibe exactamente lo que promete su contrato. Un modo que sepa aprovechar el
    /// preludio o remuestrear por su cuenta, como FT8, lo sobreescribe.
    /// </remarks>
    IReadOnlyList<DecodificacionPropia> DecodificarVentana(
        ReadOnlySpan<float> audio,
        int frecuenciaDeMuestreo,
        DateTimeOffset ventanaUtc,
        double segundosDelPrimerMuestreo,
        CancellationToken ct)
    {
        var fa = FrecuenciaDeAnalisis;
        float[] muestras = frecuenciaDeMuestreo == fa
            ? audio.ToArray()
            : Senal.Remuestreador.Remuestrear(audio, frecuenciaDeMuestreo, fa);

        var corrimiento = (int)Math.Round(segundosDelPrimerMuestreo * fa);
        if (corrimiento < 0)
        {
            var quitar = Math.Min(-corrimiento, muestras.Length);
            return Decodificar(muestras.AsSpan(quitar), ventanaUtc, ct);
        }

        if (corrimiento > 0)
        {
            var relleno = new float[corrimiento + muestras.Length];
            muestras.CopyTo(relleno, corrimiento);
            return Decodificar(relleno, ventanaUtc, ct);
        }

        return Decodificar(muestras, ventanaUtc, ct);
    }

    /// <summary>
    /// Comienzo de la ventana a la que pertenece un instante: el multiplo de
    /// <paramref name="periodo"/> contado desde las 00:00 UTC que queda por debajo, mas
    /// <paramref name="arranque"/>.
    /// </summary>
    /// <param name="instante">Instante cualquiera.</param>
    /// <param name="periodo">Duracion de la ventana del modo.</param>
    /// <param name="arranque">Corrimiento de la ventana; cero en todos los modos conocidos.</param>
    static DateTimeOffset ComienzoDeVentana(DateTimeOffset instante, TimeSpan periodo, TimeSpan arranque = default)
    {
        if (periodo <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(periodo));
        var pulsos = instante.UtcTicks - arranque.Ticks;
        var resto = ((pulsos % periodo.Ticks) + periodo.Ticks) % periodo.Ticks;
        return new DateTimeOffset(pulsos - resto + arranque.Ticks, TimeSpan.Zero);
    }
}
