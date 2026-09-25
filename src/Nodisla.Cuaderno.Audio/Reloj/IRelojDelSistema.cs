namespace Nodisla.Cuaderno.Audio.Reloj;

/// <summary>
/// Lo poco que hace falta del sistema operativo para poner el reloj en hora.
/// </summary>
/// <remarks>
/// Esta separado para que el sincronizador se pueda probar entero sin ser administrador y sin
/// tocar el reloj de nadie. Cambiar la hora del ordenador de otro no es algo que se pruebe
/// «a ver que pasa».
/// </remarks>
public interface IRelojDelSistema
{
    /// <summary>
    /// El proceso puede cambiar la hora del sistema, es decir, va como administrador.
    /// </summary>
    bool HayPermisosParaCambiarLaHora { get; }

    /// <summary>Pone la hora del sistema.</summary>
    /// <param name="instanteUtc">La hora buena, en UTC.</param>
    /// <exception cref="InvalidOperationException">Si Windows no deja.</exception>
    void PonerHoraUtc(DateTime instanteUtc);

    /// <summary>Ejecuta una orden del sistema sin abrir ninguna ventana.</summary>
    /// <param name="programa">Programa a ejecutar.</param>
    /// <param name="argumentos">Argumentos.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>El codigo de salida y lo que escribio.</returns>
    Task<(int Codigo, string Salida)> EjecutarAsync(string programa, string argumentos, CancellationToken ct = default);
}
