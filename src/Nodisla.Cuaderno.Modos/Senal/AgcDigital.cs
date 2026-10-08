namespace Nodisla.Cuaderno.Modos.Senal;

/// <summary>
/// «AGCc»: control automatico de ganancia del propio decodificador, para nivelar el audio hacia
/// un objetivo antes de decodificar.
/// </summary>
/// <remarks>
/// <para>
/// No tiene nada que ver con el AGC del equipo, que sigue por CAT y vive en el propio receptor:
/// este trabaja sobre el audio ya capturado, dentro del modem. Sigue un envolvente (ataque
/// rapido, recuperacion lenta, como cualquier compresor de audio) y corrige la ganancia hacia el
/// nivel objetivo, con limites para no disparar el ruido de fondo cuando no hay senal.
/// </para>
/// </remarks>
public sealed class AgcDigital
{
    private const double NivelObjetivo = 0.3;
    private const double SegundosDeAtaque = 0.01;
    private const double SegundosDeRecuperacion = 0.5;
    private const double GananciaMinima = 0.1;
    private const double GananciaMaxima = 10.0;

    private double _envolvente;
    private double _ganancia = 1.0;

    /// <summary>Olvida el envolvente y la ganancia aprendidos: para un cambio de sesion limpio.</summary>
    public void Reiniciar()
    {
        _envolvente = 0;
        _ganancia = 1.0;
    }

    /// <summary>Nivela las muestras en el sitio.</summary>
    public void Procesar(Span<float> muestras, int frecuenciaDeMuestreo)
    {
        if (frecuenciaDeMuestreo <= 0) return;

        var coefAtaque = Math.Exp(-1.0 / (SegundosDeAtaque * frecuenciaDeMuestreo));
        var coefRecuperacion = Math.Exp(-1.0 / (SegundosDeRecuperacion * frecuenciaDeMuestreo));

        for (var i = 0; i < muestras.Length; i++)
        {
            var nivel = Math.Abs((double)muestras[i]);
            var coef = nivel > _envolvente ? coefAtaque : coefRecuperacion;
            _envolvente = (coef * _envolvente) + ((1 - coef) * nivel);

            var objetivoDeGanancia = _envolvente > 1e-6 ? NivelObjetivo / _envolvente : GananciaMaxima;
            objetivoDeGanancia = Math.Clamp(objetivoDeGanancia, GananciaMinima, GananciaMaxima);
            // La propia ganancia se suaviza para no «bombear» de muestra en muestra.
            _ganancia = (0.999 * _ganancia) + (0.001 * objetivoDeGanancia);

            muestras[i] = (float)Math.Clamp(muestras[i] * _ganancia, -1.0, 1.0);
        }
    }
}
