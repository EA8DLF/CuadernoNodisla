using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Satelites.Orbital;
using Nodisla.Cuaderno.Satelites.Seguimiento;

namespace Nodisla.Cuaderno.Satelites.Doppler;

/// <summary>Como quedan los diales despues de un ajuste.</summary>
/// <param name="Instante">Momento para el que se calculo.</param>
/// <param name="Sintonia">Las dos frecuencias corregidas.</param>
/// <param name="Escrita">Se ha llegado a escribir en el equipo.</param>
/// <param name="Motivo">Por que no se ha escrito, si no se ha escrito.</param>
public readonly record struct ResultadoDeAjuste(
    DateTimeOffset Instante,
    SintoniaCorregida Sintonia,
    bool Escrita,
    string? Motivo);

/// <summary>Ajustes del seguimiento Doppler sobre el equipo.</summary>
public sealed class OpcionesDeDoppler
{
    /// <summary>VFO por el que se transmite: el que lleva la <b>subida</b>.</summary>
    public NombreDeVfo VfoDeSubida { get; set; } = NombreDeVfo.A;

    /// <summary>VFO por el que se escucha: el que lleva la <b>bajada</b>.</summary>
    public NombreDeVfo VfoDeBajada { get; set; } = NombreDeVfo.B;

    /// <summary>
    /// Cuanto tiene que moverse una frecuencia para que merezca la pena escribirla, en hercios.
    /// </summary>
    /// <remarks>
    /// Veinte hercios. Por debajo de ahi el cambio no se oye —el oido no distingue veinte
    /// hercios en SSB— y escribir por escribir tiene dos costes reales: satura el puerto serie,
    /// que en el FT-710 comparte camino con las lecturas de estado, y produce un salto audible
    /// en el receptor cada vez. Con la banda muerta, un paso entero de 70 cm se resuelve en unas
    /// pocas centenas de escrituras en lugar de miles.
    /// </remarks>
    public int BandaMuertaHz { get; set; } = 20;

    /// <summary>
    /// Elevacion por debajo de la cual no se toca el equipo, en grados.
    /// </summary>
    /// <remarks>
    /// Cero: mientras el satelite este bajo el horizonte no hay nada que seguir y mover el dial
    /// solo estorba al operador, que puede estar en otra cosa.
    /// </remarks>
    public double ElevacionMinimaGrados { get; set; }
}

/// <summary>
/// Lleva los dos VFO del equipo detras del satelite corrigiendo el Doppler.
/// </summary>
/// <remarks>
/// <para>
/// <b>Este objeto no transmite nunca.</b> Solo escribe frecuencias en los dos VFO: no toca el
/// PTT, no pone el equipo en transmision y no cambia el modo. Quien decide cuando se transmite
/// es el operador, siempre.
/// </para>
/// <para>
/// <b>No lleva reloj propio.</b> Cada ajuste se pide con el instante que se quiere, y el
/// llamante decide cada cuanto. Asi el seguimiento se puede probar entero sin esperar y sin
/// depender del reloj de la maquina, y quien monte la pantalla elige la cadencia —un segundo
/// va sobrado para orbita baja— sin tener que pelearse con un temporizador escondido aqui.
/// </para>
/// <para>
/// <b>Los dos VFO llevan signos contrarios.</b> La bajada se sigue donde se oye; la subida se
/// pone donde haga falta para que al satelite le llegue su frecuencia. Lo resuelve
/// <see cref="CorreccionDoppler"/>; aqui solo se reparten entre los dos VFO.
/// </para>
/// </remarks>
public sealed class SeguimientoDoppler
{
    private readonly SeguidorDeSatelites _seguidor;
    private readonly IEquipoConDosVfos _equipo;
    private readonly OpcionesDeDoppler _opciones;
    private readonly ILogger _registro;

    private Frecuencia _ultimaSubidaEscrita = Frecuencia.Cero;
    private Frecuencia _ultimaBajadaEscrita = Frecuencia.Cero;

    /// <summary>Crea el seguimiento.</summary>
    /// <param name="seguidor">Seguidor con los elementos del satelite ya cargados.</param>
    /// <param name="equipo">Equipo con dos VFO sobre el que se escribe.</param>
    /// <param name="opciones">Ajustes; si no se dan, los de omision.</param>
    /// <param name="registro">Traza; por omision no se traza.</param>
    public SeguimientoDoppler(
        SeguidorDeSatelites seguidor,
        IEquipoConDosVfos equipo,
        OpcionesDeDoppler? opciones = null,
        ILogger<SeguimientoDoppler>? registro = null)
    {
        _seguidor = seguidor ?? throw new ArgumentNullException(nameof(seguidor));
        _equipo = equipo ?? throw new ArgumentNullException(nameof(equipo));
        _opciones = opciones ?? new OpcionesDeDoppler();
        _registro = registro ?? (ILogger)NullLogger<SeguimientoDoppler>.Instance;
    }

    /// <summary>Enlace que se esta siguiendo, o <c>null</c> si no hay ninguno.</summary>
    public EnlaceDeSatelite? Enlace { get; private set; }

    /// <summary>Ultima sintonia calculada, aunque no se haya llegado a escribir.</summary>
    public SintoniaCorregida? Ultima { get; private set; }

    /// <summary>Empieza a seguir un enlace. No escribe nada todavia.</summary>
    /// <param name="enlace">Enlace con las frecuencias nominales.</param>
    /// <remarks>
    /// Se olvida lo escrito antes a proposito: al cambiar de satelite o de transpondedor la
    /// banda muerta tiene que volver a partir de cero, o el primer ajuste del enlace nuevo se
    /// compararia contra la frecuencia del anterior y podria no escribirse.
    /// </remarks>
    public void Seguir(EnlaceDeSatelite enlace)
    {
        Enlace = enlace ?? throw new ArgumentNullException(nameof(enlace));
        _ultimaSubidaEscrita = Frecuencia.Cero;
        _ultimaBajadaEscrita = Frecuencia.Cero;
        Ultima = null;
    }

    /// <summary>Deja de seguir. No devuelve el equipo a ninguna frecuencia.</summary>
    /// <remarks>
    /// Deliberadamente no se restaura nada: el operador puede estar en mitad de un contacto y
    /// moverle el dial al soltar el seguimiento seria peor que dejarlo donde esta.
    /// </remarks>
    public void Soltar()
    {
        Enlace = null;
        Ultima = null;
    }

    /// <summary>Calcula y, si hace falta, escribe las dos frecuencias en el equipo.</summary>
    /// <param name="instanteUtc">Momento para el que se calcula.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>Que ha salido y si se ha escrito.</returns>
    public async Task<ResultadoDeAjuste> AjustarAsync(
        DateTimeOffset instanteUtc,
        CancellationToken ct = default)
    {
        if (Enlace is not { } enlace)
        {
            return new ResultadoDeAjuste(instanteUtc, default, Escrita: false, "no se está siguiendo nada");
        }

        var estado = _seguidor.Donde(enlace.Satelite, instanteUtc);
        if (estado is null)
        {
            return new ResultadoDeAjuste(
                instanteUtc, default, Escrita: false,
                $"«{enlace.Satelite}» no está cargado o no se puede propagar");
        }

        var sintonia = enlace.Sintonia(estado.Vista);
        Ultima = sintonia;

        if (estado.Vista.ElevacionGrados < _opciones.ElevacionMinimaGrados)
        {
            return new ResultadoDeAjuste(instanteUtc, sintonia, Escrita: false, "el satélite está bajo el horizonte");
        }

        var escribeBajada = enlace.TieneBajada && FueraDeLaBandaMuerta(sintonia.Bajada, _ultimaBajadaEscrita);
        var escribeSubida = enlace.TieneSubida && FueraDeLaBandaMuerta(sintonia.Subida, _ultimaSubidaEscrita);

        if (!escribeBajada && !escribeSubida)
        {
            return new ResultadoDeAjuste(instanteUtc, sintonia, Escrita: false, "el cambio no llega a la banda muerta");
        }

        // Primero la bajada: es la que el operador esta oyendo, y si el puerto se atasca a
        // mitad de ajuste vale mas tener bien la escucha que bien la transmision.
        if (escribeBajada)
        {
            await _equipo.PonerFrecuenciaDeAsync(_opciones.VfoDeBajada, sintonia.Bajada, ct).ConfigureAwait(false);
            _ultimaBajadaEscrita = sintonia.Bajada;
        }

        if (escribeSubida)
        {
            await _equipo.PonerFrecuenciaDeAsync(_opciones.VfoDeSubida, sintonia.Subida, ct).ConfigureAwait(false);
            _ultimaSubidaEscrita = sintonia.Subida;
        }

        _registro.LogDebug(
            "Doppler de {Satelite}: bajada {Bajada} Hz ({DesplazamientoBajada:F0} Hz), subida {Subida} Hz ({DesplazamientoSubida:F0} Hz).",
            enlace.Satelite,
            sintonia.Bajada.Hercios,
            sintonia.DesplazamientoBajadaHz,
            sintonia.Subida.Hercios,
            sintonia.DesplazamientoSubidaHz);

        return new ResultadoDeAjuste(instanteUtc, sintonia, Escrita: true, null);
    }

    private bool FueraDeLaBandaMuerta(Frecuencia nueva, Frecuencia escrita) =>
        escrita.EsCero || Math.Abs(nueva.Hercios - escrita.Hercios) >= _opciones.BandaMuertaHz;
}
