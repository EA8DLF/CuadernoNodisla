using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Concursos.Calculo;
using Nodisla.Cuaderno.Concursos.Catalogo;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Concursos.Sesion;

/// <summary>
/// Una sesion de concurso: el numero de serie, el aviso de duplicado y el marcador en vivo.
/// </summary>
/// <remarks>
/// <para>
/// Lo que sale de aqui son <b>contactos normales del cuaderno</b> con sus campos de concurso
/// rellenos. No hay un cuaderno de concurso aparte ni una tabla paralela: un contacto de un
/// concurso es un <see cref="Qso"/> con <c>CONTEST_ID</c>, <c>STX</c> y <c>SRX</c>, y por eso
/// se puede exportar en ADIF, subir a LoTW y contar para los diplomas como cualquier otro.
/// Duplicar el modelo habria significado duplicar tambien el ADIF, las confirmaciones y las
/// estadisticas.
/// </para>
/// <para>
/// La sesion no guarda nada: devuelve el contacto armado y quien la use decide cuando lo
/// escribe en la base. Asi se puede probar entera sin base de datos y, sobre todo, el aviso
/// de duplicado nunca depende de que el disco responda.
/// </para>
/// </remarks>
public sealed class SesionDeConcurso
{
    private readonly ILogger _registro;
    private readonly IndiceDeDuplicados _duplicados;
    private readonly ContadorDeMultiplicadores _multiplicadores;
    private readonly List<Qso> _contactos = [];
    private int _puntos;
    private int _conDuplicados;

    /// <summary>Abre una sesion de concurso.</summary>
    /// <param name="opciones">Reglas del concurso y datos de mi estacion.</param>
    /// <param name="registro">Para las trazas. Si no se da, no se traza.</param>
    public SesionDeConcurso(OpcionesDeSesion opciones, ILogger<SesionDeConcurso>? registro = null)
    {
        ArgumentNullException.ThrowIfNull(opciones);
        Opciones = opciones;
        _registro = registro ?? (ILogger)NullLogger.Instance;
        _duplicados = new IndiceDeDuplicados(opciones.Concurso.Duplicado);
        _multiplicadores = new ContadorDeMultiplicadores(opciones.Concurso.Multiplicadores);
        ProximaSerie = Math.Max(1, opciones.PrimeraSerie);

        if (opciones.Concurso.NecesitaAviso)
        {
            _registro.LogWarning(
                "Concurso {Concurso}: las reglas que conoce el programa son {Estado}. El marcador es orientativo.",
                opciones.Concurso.Codigo, opciones.Concurso.Estado);
        }
    }

    /// <summary>Como se configuro la sesion.</summary>
    public OpcionesDeSesion Opciones { get; }

    /// <summary>Reglas del concurso.</summary>
    public ReglaDeConcurso Concurso => Opciones.Concurso;

    /// <summary>Numero de serie que se enviara en el proximo contacto.</summary>
    public int ProximaSerie { get; private set; }

    /// <summary>Contactos registrados en esta sesion, en el orden en que se hicieron.</summary>
    public IReadOnlyList<Qso> Contactos => _contactos;

    /// <summary>Multiplicadores conseguidos, escritos para ensenarlos.</summary>
    public IReadOnlyList<string> Multiplicadores => _multiplicadores.Conseguidos;

    /// <summary>
    /// Hay que ensenar al operador que el marcador no es la cuenta del organizador.
    /// </summary>
    public bool ReglasSinContrastar => Concurso.NecesitaAviso;

    /// <summary>
    /// El marcador en este momento.
    /// </summary>
    /// <remarks>
    /// El total es puntos por multiplicadores, salvo que el concurso no tenga multiplicadores
    /// declarados: entonces el total son los puntos. Multiplicar por cero dejaria el marcador
    /// a cero durante todo un concurso sin que nada estuviera roto, y el operador pensaria que
    /// el programa no cuenta.
    /// </remarks>
    public MarcadorDeConcurso Marcador => new(
        _contactos.Count - _conDuplicados,
        _conDuplicados,
        _puntos,
        _multiplicadores.Total,
        Concurso.Multiplicadores.Count == 0 ? _puntos : _puntos * _multiplicadores.Total);

    /// <summary>
    /// Dice al instante si lo escrito en la casilla ya esta trabajado.
    /// </summary>
    /// <remarks>
    /// Este es el camino corto, el que se recorre con cada tecla: mira el indice y no toca
    /// nada mas. Cuando ademas interese saber los puntos y los multiplicadores —que ya exige
    /// tener resuelto el pais del corresponsal— se usa <see cref="Previsualizar"/>.
    /// </remarks>
    /// <param name="indicativo">Lo que hay escrito, tal cual, aunque este a medias.</param>
    /// <param name="banda">Banda en la que se esta.</param>
    /// <param name="modo">Modo en el que se esta.</param>
    /// <returns>Si esa estacion es nueva, duplicada o conocida de otra banda.</returns>
    public EstadoDeDuplicado Comprobar(string? indicativo, Banda banda, Modo modo) =>
        _duplicados.Comprobar(indicativo, banda, modo);

    /// <summary>Indicativos ya trabajados que empiezan por lo escrito.</summary>
    /// <param name="prefijo">Lo escrito hasta ahora.</param>
    /// <param name="maximo">Cuantos devolver como mucho.</param>
    /// <returns>Los que casan, en orden alfabetico.</returns>
    public IReadOnlyList<string> Conocidos(string? prefijo, int maximo = 10) =>
        _duplicados.PorPrefijo(prefijo, maximo);

    /// <summary>Que pasaria si este contacto se diera por bueno, sin darlo.</summary>
    /// <param name="apunte">Contacto que se esta tecleando.</param>
    /// <returns>Duplicado, puntos y multiplicadores que traeria.</returns>
    public AvisoDeConcurso Previsualizar(ApunteDeConcurso apunte)
    {
        ArgumentNullException.ThrowIfNull(apunte);
        var estado = _duplicados.Comprobar(apunte.Call.Valor, apunte.Banda, apunte.Modo);
        if (estado == EstadoDeDuplicado.Duplicado) return new AvisoDeConcurso(estado, 0, []);
        return new AvisoDeConcurso(
            estado,
            CalculadoraDePuntos.De(Concurso, Opciones.MiEstacion, apunte),
            _multiplicadores.Previsualizar(apunte));
    }

    /// <summary>Da un contacto por bueno y lo convierte en contacto del cuaderno.</summary>
    /// <param name="apunte">Lo que ha tecleado el operador.</param>
    /// <returns>El contacto armado, con lo que ha sumado.</returns>
    /// <exception cref="InvalidOperationException">
    /// Es duplicado y la sesion se abrio sin admitirlos.
    /// </exception>
    public RegistroDeConcurso Registrar(ApunteDeConcurso apunte)
    {
        ArgumentNullException.ThrowIfNull(apunte);
        if (apunte.Call.EsVacio) throw new ArgumentException("El contacto no lleva indicativo.", nameof(apunte));

        var estado = _duplicados.Comprobar(apunte.Call.Valor, apunte.Banda, apunte.Modo);
        var esDuplicado = estado == EstadoDeDuplicado.Duplicado;
        if (esDuplicado && !Opciones.AdmiteDuplicados)
        {
            throw new InvalidOperationException(
                $"{apunte.Call.Valor} ya está trabajado en {apunte.Banda.Nombre} y esta sesión no admite duplicados.");
        }

        var serie = Concurso.UsaNumeroDeSerie ? ProximaSerie : (int?)null;
        var puntos = esDuplicado ? 0 : CalculadoraDePuntos.De(Concurso, Opciones.MiEstacion, apunte);
        IReadOnlyList<string> nuevos = [];
        if (!esDuplicado) nuevos = _multiplicadores.Registrar(apunte);

        var qso = Armar(apunte, serie);
        _contactos.Add(qso);
        _duplicados.Anadir(apunte.Call.Valor, apunte.Banda, apunte.Modo);
        _puntos += puntos;
        if (esDuplicado) _conDuplicados++;
        if (serie is not null) ProximaSerie = serie.Value + 1;

        return new RegistroDeConcurso(qso, puntos, nuevos, serie, esDuplicado);
    }

    /// <summary>
    /// Mete en la sesion contactos que ya estaban en el cuaderno.
    /// </summary>
    /// <remarks>
    /// Hace falta porque las sesiones se interrumpen: se cierra el programa a las tres de la
    /// madrugada y se vuelve a las nueve. Sin esto, el aviso de duplicado y el numero de
    /// serie empezarian de cero y el log saldria mal.
    /// </remarks>
    /// <param name="contactos">Contactos del concurso ya guardados.</param>
    public void Precargar(IEnumerable<Qso> contactos)
    {
        ArgumentNullException.ThrowIfNull(contactos);
        foreach (var qso in contactos.OrderBy(q => q.InicioUtc))
        {
            _contactos.Add(qso);
            _duplicados.Anadir(qso.Call.Valor, qso.Band, qso.Mode);
            if (qso.Stx is { } stx && stx >= ProximaSerie) ProximaSerie = stx + 1;
        }
        _registro.LogInformation(
            "Sesión de {Concurso} recuperada con {Cuantos} contactos; siguiente serie {Serie}.",
            Concurso.Codigo, _contactos.Count, ProximaSerie);
    }

    private Qso Armar(ApunteDeConcurso apunte, int? serie)
    {
        var informeEnviado = apunte.InformeEnviado.EsVacio
            ? Informe.PorOmisionPara(apunte.Modo)
            : apunte.InformeEnviado;
        var informeRecibido = apunte.InformeRecibido.EsVacio
            ? Informe.PorOmisionPara(apunte.Modo)
            : apunte.InformeRecibido;

        return new Qso
        {
            Call = apunte.Call,
            Band = apunte.Banda,
            Mode = apunte.Modo,
            Freq = apunte.Frecuencia,
            InicioUtc = apunte.InicioUtc ?? DateTimeOffset.UtcNow,
            RstSent = informeEnviado,
            RstRcvd = informeRecibido,
            StationCallsign = Opciones.MiEstacion.Indicativo,
            Operator = Opciones.Operador,
            MyGridsquare = Opciones.MiEstacion.Locator,
            MyDxcc = Opciones.MiEstacion.Dxcc > 0 ? Opciones.MiEstacion.Dxcc : null,
            MyCqZone = Opciones.MiEstacion.ZonaCq,
            MyItuZone = Opciones.MiEstacion.ZonaItu,
            TxPwr = Opciones.MiEstacion.Potencia,
            ContestId = Concurso.Codigo,
            Stx = serie,
            StxString = Enviado(serie),
            Srx = apunte.SerieRecibida,
            SrxString = Recibido(apunte),
            Gridsquare = apunte.Locator,
            Dxcc = apunte.Dxcc ?? 0,
            Cqz = apunte.ZonaCq,
            Ituz = apunte.ZonaItu,
            Cont = apunte.Continente,
            Country = apunte.Pais,
            State = apunte.Estado,
            Name = apunte.Nombre,
            Origen = "Concurso",
        };
    }

    /// <summary>Compone el intercambio enviado, sin el informe: eso ya va en <c>RST_SENT</c>.</summary>
    private string? Enviado(int? serie)
    {
        var piezas = new List<string>(2);
        foreach (var campo in Concurso.Enviado)
        {
            var valor = campo switch
            {
                TipoDeIntercambio.Informe => null,
                TipoDeIntercambio.Serie => serie?.ToString("000", CultureInfo.InvariantCulture),
                TipoDeIntercambio.ZonaCq => Opciones.MiEstacion.ZonaCq?.ToString("00", CultureInfo.InvariantCulture),
                TipoDeIntercambio.ZonaItu => Opciones.MiEstacion.ZonaItu?.ToString("00", CultureInfo.InvariantCulture),
                TipoDeIntercambio.Locator => Opciones.MiEstacion.Locator.EsVacio ? null : Opciones.MiEstacion.Locator.Valor,
                TipoDeIntercambio.Provincia => Opciones.MiEstacion.Provincia,
                TipoDeIntercambio.Estado => Opciones.MiEstacion.Estado,
                TipoDeIntercambio.Potencia => Opciones.MiEstacion.Potencia?.ToString("0", CultureInfo.InvariantCulture),
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(valor)) piezas.Add(valor);
        }
        return piezas.Count == 0 ? null : string.Join(' ', piezas);
    }

    /// <summary>Compone el intercambio recibido, sin el informe.</summary>
    private string? Recibido(ApunteDeConcurso apunte)
    {
        // Si el operador lo escribio a mano, manda lo que escribio: el concurso puede pedir
        // algo que el programa no modela y lo que vale es lo que se oyo.
        if (!string.IsNullOrWhiteSpace(apunte.IntercambioRecibido)) return apunte.IntercambioRecibido.Trim();

        var piezas = new List<string>(2);
        foreach (var campo in Concurso.Recibido)
        {
            var valor = campo switch
            {
                TipoDeIntercambio.Informe => null,
                TipoDeIntercambio.Serie => apunte.SerieRecibida?.ToString("000", CultureInfo.InvariantCulture),
                TipoDeIntercambio.ZonaCq => apunte.ZonaCq?.ToString("00", CultureInfo.InvariantCulture),
                TipoDeIntercambio.ZonaItu => apunte.ZonaItu?.ToString("00", CultureInfo.InvariantCulture),
                TipoDeIntercambio.Locator => apunte.Locator.EsVacio ? null : apunte.Locator.Valor,
                TipoDeIntercambio.Provincia => apunte.Provincia,
                TipoDeIntercambio.Estado => apunte.Estado,
                TipoDeIntercambio.Seccion => apunte.Seccion,
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(valor)) piezas.Add(valor);
        }
        return piezas.Count == 0 ? null : string.Join(' ', piezas);
    }
}
