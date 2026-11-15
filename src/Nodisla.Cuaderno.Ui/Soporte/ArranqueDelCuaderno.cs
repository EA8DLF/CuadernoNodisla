namespace Nodisla.Cuaderno.Ui.Soporte;

/// <summary>
/// El paso del arranque que deja el cuaderno listo, con su respuesta clara: sigue o no sigue.
/// </summary>
/// <remarks>
/// Si el cuaderno no se puede abrir <b>no hay programa</b>. Antes el fallo pedia el cierre
/// con <c>Shutdown(1)</c>, pero eso solo lo deja pedido: el arranque seguia, creaba la ventana
/// principal y la mostraba sobre un cuaderno que no se habia podido abrir. Quien llama tiene
/// que mirar lo que devuelve esto y cortar.
/// </remarks>
public static class ArranqueDelCuaderno
{
    /// <summary>Prepara el cuaderno y dice si el arranque puede seguir.</summary>
    /// <param name="preparar">Lo que deja el cuaderno listo (migraciones incluidas).</param>
    /// <param name="alFallar">Como se avisa del fallo: registro y cartel.</param>
    /// <returns>Falso si ha fallado: no se debe crear la ventana.</returns>
    public static bool Intentar(Action preparar, Action<Exception> alFallar)
    {
        ArgumentNullException.ThrowIfNull(preparar);
        ArgumentNullException.ThrowIfNull(alFallar);

        try
        {
            preparar();
            return true;
        }
        catch (Exception ex)
        {
            alFallar(ex);
            return false;
        }
    }
}
