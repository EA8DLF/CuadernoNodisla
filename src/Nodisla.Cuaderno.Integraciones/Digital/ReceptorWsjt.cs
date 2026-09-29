using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Integraciones.Digital;

/// <summary>
/// Cerebro del puente: recibe datagramas sueltos, lleva la cuenta de las instancias vivas y
/// avisa de lo que va pasando. No sabe nada de sockets a proposito, para poder probarlo
/// metiendole datagramas a mano.
/// </summary>
/// <remarks>
/// Ningun datagrama puede romper el bucle. Lo que no cuadra se descarta con traza y se sigue:
/// basura de otro programa que comparte el puerto, un tipo de mensaje que aun no existia, un
/// datagrama cortado. Si ademas el receptor de eventos lanza, tampoco: eso es cosa suya.
/// </remarks>
public sealed class ReceptorWsjt
{
    private readonly ConcurrentDictionary<string, InstanciaDigital> _instancias =
        new(StringComparer.Ordinal);

    private readonly ILogger _registro;

    /// <summary>Crea el receptor.</summary>
    /// <param name="registro">Donde dejar constancia de lo que pasa. Nulo para no registrar nada.</param>
    public ReceptorWsjt(ILogger? registro = null) => _registro = registro ?? NullLogger.Instance;

    /// <summary>Instancias que han dado senales de vida.</summary>
    public IReadOnlyCollection<InstanciaDigital> Instancias => _instancias.Values.ToArray();

    /// <summary>Salta con cada decodificacion.</summary>
    public event EventHandler<DecodificacionDigital>? Decodificado;

    /// <summary>Salta con cada informe de estado.</summary>
    public event EventHandler<EstadoDigital>? EstadoRecibido;

    /// <summary>Salta con cada contacto cerrado, ya convertido en QSO del cuaderno.</summary>
    public event EventHandler<Qso>? QsoRegistrado;

    /// <summary>Salta con el texto ADIF que el programa manda al cerrar un contacto.</summary>
    public event EventHandler<string>? AdifRecibido;

    /// <summary>Salta cuando una instancia se cierra o deja de responder.</summary>
    public event EventHandler<string>? InstanciaPerdida;

    /// <summary>Busca una instancia por su identificador.</summary>
    /// <param name="identificador">Nombre de la instancia.</param>
    /// <param name="instancia">Instancia encontrada.</param>
    public bool TryInstancia(string identificador, out InstanciaDigital instancia) =>
        _instancias.TryGetValue(identificador, out instancia!);

    /// <summary>Procesa un datagrama recibido.</summary>
    /// <param name="datagrama">Bytes tal y como llegaron.</param>
    /// <param name="remitente">De donde vino, para saber a quien contestar.</param>
    /// <param name="ahoraUtc">Momento de la recepcion.</param>
    /// <returns>Cierto si el datagrama contenia un mensaje que se entendio.</returns>
    public bool Recibir(ReadOnlySpan<byte> datagrama, EndPoint? remitente, DateTimeOffset ahoraUtc)
    {
        ResultadoDeAnalisis resultado;
        try
        {
            var dialectoPrevio = DialectoPrevio(datagrama);
            resultado = AnalizadorWsjt.Analizar(datagrama, dialectoPrevio);
        }
        catch (Exception e)
        {
            // No deberia pasar: el analizador no lanza. Aun asi, el bucle no se para por esto.
            Advertir($"Un datagrama de {datagrama.Length} bytes rompio el analizador: {e.Message}");
            return false;
        }

        if (resultado.Mensaje is null)
        {
            Trazar($"Datagrama descartado ({resultado.Motivo}): {resultado.Detalle}");
            return false;
        }

        try
        {
            Encaminar(resultado, remitente, ahoraUtc);
            return true;
        }
        catch (Exception e)
        {
            Advertir($"Fallo al tratar un mensaje de tipo {resultado.Mensaje.Tipo}: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Mira el identificador sin llegar a analizar del todo, para saber con que dialecto hay
    /// que leer el mensaje de estado. Es un adelanto barato: solo la cabecera y el texto.
    /// </summary>
    private DialectoDigital DialectoPrevio(ReadOnlySpan<byte> datagrama)
    {
        var lector = new LectorQDataStream(datagrama);
        if (!lector.TryUInt32(out var magia) || magia != AnalizadorWsjt.Magia) return DialectoDigital.Desconocido;
        if (!lector.TryUInt32(out _)) return DialectoDigital.Desconocido;
        if (!lector.TryUInt32(out _)) return DialectoDigital.Desconocido;
        if (!lector.TryTexto(out var id) || id is null) return DialectoDigital.Desconocido;

        if (_instancias.TryGetValue(id, out var conocida) && conocida.Dialecto != DialectoDigital.Desconocido)
            return conocida.Dialecto;

        return DeteccionDeDialecto.PorIdentificador(id);
    }

    private void Encaminar(ResultadoDeAnalisis resultado, EndPoint? remitente, DateTimeOffset ahoraUtc)
    {
        var mensaje = resultado.Mensaje!;
        var instancia = Tocar(mensaje.Id, remitente, ahoraUtc);

        if (resultado.DialectoProbado != DialectoDigital.Desconocido)
        {
            instancia.Dialecto = resultado.DialectoProbado;
        }

        switch (mensaje)
        {
            case LatidoWsjt latido:
                instancia.Version = latido.Version;
                instancia.Revision = latido.Revision;
                break;

            case EstadoWsjt estado:
                AplicarEstado(instancia, estado, ahoraUtc);
                break;

            case DecodificacionWsjt decodificacion:
                AplicarDecodificacion(instancia, decodificacion, ahoraUtc);
                break;

            case QsoRegistradoWsjt qso:
                Avisar(QsoRegistrado, ConversorDeQsoDigital.AQso(qso, instancia.Dialecto));
                break;

            case AdifRegistradoWsjt adif when !string.IsNullOrWhiteSpace(adif.TextoAdif):
                Avisar(AdifRecibido, adif.TextoAdif!);
                break;

            case CierreWsjt:
                Olvidar(mensaje.Id, "la instancia aviso de que se cerraba");
                break;

            case MensajeSinDetallar sinDetallar:
                Trazar($"Mensaje de tipo {(uint)sinDetallar.Tipo} de «{sinDetallar.Id}»: "
                    + $"se reconoce pero no se usa ({sinDetallar.Cuerpo.Length} bytes de cuerpo).");
                break;
        }
    }

    private void AplicarEstado(InstanciaDigital instancia, EstadoWsjt estado, DateTimeOffset ahoraUtc)
    {
        // Un estado que se acaba donde JTDX lo acaba es indicio, no prueba: solo se hace caso
        // si no se sabia nada del dialecto, nunca para desmentir un identificador claro.
        if (instancia.Dialecto == DialectoDigital.Desconocido && estado.TerminoComoJtdx)
        {
            instancia.Dialecto = DialectoDigital.Jtdx;
            Trazar($"«{instancia.Identificador}» parece JTDX: su mensaje de estado termina donde el de JTDX.");
        }

        var frecuencia = estado.DialHz == 0
            ? Frecuencia.Cero
            : Frecuencia.DesdeHercios((long)estado.DialHz);

        var nuevo = new EstadoDigital(
            instancia.Dialecto,
            instancia.Identificador,
            frecuencia,
            ConversorDeQsoDigital.LeerModo(estado.Modo, estado.Submodo),
            estado.Transmitiendo,
            estado.Decodificando,
            Indicativo.Crudo(estado.IndicativoDx),
            ahoraUtc)
        {
            // Lo que el dialecto no informa se queda nulo, que tambien es un dato.
            TonoRxHz = (int)estado.RxDf,
            TonoTxHz = (int)estado.TxDf,
            TransmisionHabilitada = estado.TxHabilitado,
            VigilanteDisparado = estado.PerroGuardianTx,
            TransmiteElPrimero = estado.TxPrimero,
            ModoEspecial = estado.ModoDeOperacionEspecial is { } codigo
                ? DeteccionDeDialecto.ModoEspecial(codigo)
                : null,
            Configuracion = estado.NombreDeConfiguracion,
            MensajeEnTransmision = estado.MensajeTx,
            LocatorDelCorresponsal = Locator.TryParse(estado.LocatorDx, out var suyo) ? suyo : Locator.Vacio,
        };

        instancia.Estado = nuevo;
        Avisar(EstadoRecibido, nuevo);
    }

    private void AplicarDecodificacion(
        InstanciaDigital instancia, DecodificacionWsjt cruda, DateTimeOffset ahoraUtc)
    {
        var texto = cruda.Mensaje ?? string.Empty;
        var analisis = AnalizadorMensajeDigital.Analizar(texto);

        var decodificacion = new DecodificacionDigital(
            instancia.Identificador,
            texto,
            cruda.Decibelios,
            cruda.DesfaseSegundos,
            (int)cruda.DeltaFrecuenciaHz,
            ConversorDeQsoDigital.LeerModoDeDecodificacion(cruda.Modo, instancia.Estado.Modo),
            InstanteDe(cruda.HoraDelDia, ahoraUtc),
            instancia.Dialecto)
        {
            IndicadorDelPrograma = cruda.Modo,
            Llamante = analisis.Llamante,
            Llamado = analisis.Llamado,
            Locator = analisis.Locator,
            EsCq = analisis.EsCq,
        };

        Avisar(Decodificado, decodificacion);
    }

    /// <summary>
    /// El protocolo manda la hora del periodo pero no la fecha. Se le pone la de hoy, salvo
    /// que la diferencia pase de doce horas: entonces el periodo es de ayer o de manana y lo
    /// que ha ocurrido es que se ha cruzado la medianoche.
    /// </summary>
    /// <param name="horaDelDia">Hora del periodo tal y como la manda el programa.</param>
    /// <param name="ahoraUtc">Momento en que se recibio.</param>
    public static DateTimeOffset InstanteDe(TimeSpan horaDelDia, DateTimeOffset ahoraUtc)
    {
        var ahora = ahoraUtc.ToUniversalTime();
        var candidato = new DateTimeOffset(ahora.UtcDateTime.Date, TimeSpan.Zero) + horaDelDia;
        var diferencia = candidato - ahora;
        if (diferencia > TimeSpan.FromHours(12)) candidato = candidato.AddDays(-1);
        else if (diferencia < TimeSpan.FromHours(-12)) candidato = candidato.AddDays(1);
        return candidato;
    }

    private InstanciaDigital Tocar(string identificador, EndPoint? remitente, DateTimeOffset ahoraUtc)
    {
        var instancia = _instancias.GetOrAdd(identificador, id =>
        {
            var dialecto = DeteccionDeDialecto.PorIdentificador(id);
            Trazar($"Nueva instancia «{id}», dialecto {DeteccionDeDialecto.Nombre(dialecto)}.");
            return new InstanciaDigital(id, new EstadoDigital(
                dialecto, id, Frecuencia.Cero, Modo.Vacio, false, false, Indicativo.Vacio, ahoraUtc))
            {
                Dialecto = dialecto,
            };
        });

        instancia.UltimaSenalUtc = ahoraUtc;
        if (remitente is not null) instancia.Remitente = remitente;
        return instancia;
    }

    /// <summary>Da por perdidas las instancias que llevan callando demasiado tiempo.</summary>
    /// <param name="limite">Silencio que se tolera antes de darlas por perdidas.</param>
    /// <param name="ahoraUtc">Momento actual.</param>
    public void CaducarInactivas(TimeSpan limite, DateTimeOffset ahoraUtc)
    {
        foreach (var instancia in _instancias.Values.ToArray())
        {
            if (ahoraUtc - instancia.UltimaSenalUtc <= limite) continue;
            Olvidar(instancia.Identificador, $"lleva {limite.TotalSeconds:0} segundos sin dar senales");
        }
    }

    private void Olvidar(string identificador, string motivo)
    {
        if (!_instancias.TryRemove(identificador, out _)) return;
        Trazar($"Se pierde la instancia «{identificador}»: {motivo}.");
        Avisar(InstanciaPerdida, identificador);
    }

    /// <summary>Deja constancia de algo que se descarta o se ignora.</summary>
    private void Trazar(string texto) => _registro.LogDebug("{Traza}", texto);

    /// <summary>Deja constancia de algo que no deberia pasar y conviene mirar.</summary>
    private void Advertir(string texto) => _registro.LogWarning("{Traza}", texto);

    private void Avisar<T>(EventHandler<T>? evento, T argumento)
    {
        if (evento is null) return;
        foreach (var suscriptor in evento.GetInvocationList())
        {
            try
            {
                ((EventHandler<T>)suscriptor)(this, argumento);
            }
            catch (Exception e)
            {
                Advertir($"Un suscriptor lanzo al recibir {typeof(T).Name}: {e.Message}");
            }
        }
    }
}
