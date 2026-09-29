using Microsoft.Extensions.Logging;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Modos.Ft8;
using Nodisla.Cuaderno.Modos.Ldpc;
using Nodisla.Cuaderno.Modos.Marco;

namespace Nodisla.Cuaderno.Modos.Fst4;

/// <summary>
/// FST4 (o FST4W) como modo del modem propio, con el periodo que se le cambie.
/// </summary>
/// <remarks>
/// <para>
/// Se registra en <see cref="RegistroDeModos"/> con
/// <c>registro.Anadir(new ModoFst4(TablaLdpc.Cargar(ParametrosFst4.FicheroDeTablas, 240, 101), esFst4w: false, periodoSegundos: 60))</c>
/// y
/// <c>registro.Anadir(new ModoFst4(TablaLdpc.Cargar(ParametrosFst4.FicheroDeTablasFst4w, 240, 74), esFst4w: true, periodoSegundos: 120))</c>.
/// </para>
/// <para>
/// El registro admite una sola entrada por modo, y FST4 tiene siete periodos: por eso el
/// periodo es un ajuste del modo (<see cref="CambiarPeriodo"/>) y no un modo distinto. Al
/// cambiarlo se cambia el decodificador entero, porque cada periodo analiza a una frecuencia
/// de muestreo distinta; el modem tiene que leer <see cref="IModoDigital.Periodo"/> y
/// <see cref="IModoDigital.FrecuenciaDeAnalisis"/> de nuevo al empezar a escuchar.
/// </para>
/// </remarks>
public sealed class ModoFst4 : IModoDigital
{
    private readonly TablaLdpc _tabla;
    private readonly bool _esFst4w;
    private readonly ILogger? _registro;
    private readonly CatalogoDeIndicativos _catalogo = new();
    private DecodificadorFst4 _decodificador;
    private CodificadorFst4 _codificador;

    /// <summary>Crea el modo.</summary>
    /// <param name="tabla">La (240,101) para FST4 o la (240,74) para FST4W.</param>
    /// <param name="esFst4w">Cierto para la variante baliza.</param>
    /// <param name="periodoSegundos">Periodo de arranque: 60 s es lo normal en FST4 y 120 en FST4W.</param>
    /// <param name="registro">Para dejar constancia.</param>
    public ModoFst4(TablaLdpc tabla, bool esFst4w, int periodoSegundos, ILogger? registro = null)
    {
        ArgumentNullException.ThrowIfNull(tabla);
        _tabla = tabla;
        _esFst4w = esFst4w;
        _registro = registro;
        _codificador = new CodificadorFst4(tabla, esFst4w);
        _decodificador = new DecodificadorFst4(periodoSegundos, tabla, esFst4w, registro);
    }

    /// <summary>El decodificador del periodo en curso.</summary>
    public DecodificadorFst4 Decodificador => _decodificador;

    /// <summary>El codificador.</summary>
    public CodificadorFst4 Codificador => _codificador;

    /// <summary>Parametros del periodo en curso.</summary>
    public ParametrosFst4 Parametros => _decodificador.Parametros;

    /// <summary>Es la variante baliza.</summary>
    public bool EsFst4w => _esFst4w;

    /// <summary>Se trabaja con el codigo de verdad.</summary>
    public bool EsElCodigoReal => _tabla.EsElCodigoReal;

    /// <summary>Catalogo de indicativos con el que se resuelven los resumidos.</summary>
    public CatalogoDeIndicativos Catalogo => _catalogo;

    /// <summary>Cambia el periodo: 15, 30, 60, 120, 300, 900 o 1800 s (120 en adelante en FST4W).</summary>
    public void CambiarPeriodo(int periodoSegundos)
    {
        if (periodoSegundos == Parametros.PeriodoSegundos) return;
        var nuevo = new DecodificadorFst4(periodoSegundos, _tabla, _esFst4w, _registro)
        {
            CorreccionDelInforme = _decodificador.CorreccionDelInforme,
            UsarRecuperacionProfunda = _decodificador.UsarRecuperacionProfunda,
        };
        _decodificador = nuevo;
        _codificador = new CodificadorFst4(_tabla, _esFst4w);
    }

    /// <inheritdoc/>
    public ModoDelModem Modo => _esFst4w ? ModoDelModem.Fst4w : ModoDelModem.Fst4;

    /// <inheritdoc/>
    public TimeSpan Periodo => TimeSpan.FromSeconds(Parametros.PeriodoSegundos);

    /// <inheritdoc/>
    public TimeSpan ArranqueDentroDelPeriodo => TimeSpan.Zero;

    /// <inheritdoc/>
    /// <remarks>
    /// Redondeada al entero: las frecuencias de analisis de FST4 (8533,3 Hz en el periodo de 15
    /// segundos, 12641,98 en el de 60...) no son enteras, pero el decodificador remuestrea el
    /// audio el mismo a la exacta, asi que aqui basta con la mas cercana.
    /// </remarks>
    public int FrecuenciaDeAnalisis => (int)Math.Round(Parametros.FrecuenciaDeAnalisis);

    /// <inheritdoc/>
    public IReadOnlyList<DecodificacionPropia> Decodificar(ReadOnlySpan<float> audio, DateTimeOffset ventanaUtc, CancellationToken ct) =>
        _decodificador.Decodificar(audio, FrecuenciaDeAnalisis, ventanaUtc, _catalogo, 0, ct).Decodificaciones;

    /// <inheritdoc/>
    public float[] Generar(string mensaje, int tonoHz, int frecuenciaDeMuestreo)
    {
        if (!_codificador.TryCodificar(mensaje, out var tonos, out var motivo)) throw new FormatException(motivo);
        return ModuladorFst4.Sintetizar(Parametros, tonos, tonoHz, frecuenciaDeMuestreo, amplitud: 0.5);
    }
}
