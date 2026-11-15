using System.IO;
using System.Runtime.CompilerServices;
using Serilog;

namespace Nodisla.Cuaderno.Ui.Soporte;

/// <summary>
/// Red para los manejadores <c>async void</c> de las vistas.
/// </summary>
/// <remarks>
/// <para>
/// Un manejador de evento asincrono no tiene a nadie esperandolo: si dentro se escapa una
/// excepcion —la radio no contesta al girar el dial, el cuaderno esta ocupado— va derecha al
/// hilo de interfaz y, como el arranque deja <c>Handled = false</c>, <b>cierra el programa</b>.
/// Un giro de rueda con el equipo apagado no puede costar el programa abierto en mitad de un
/// concurso.
/// </para>
/// <para>
/// Envolver el manejador con <see cref="EjecutarAsync"/> no cambia lo que hace: lo ejecuta
/// igual, en el mismo hilo, y solo si falla lo apunta en el registro y avisa por
/// <see cref="Fallo"/> (la ventana principal lo ensena en su cartel de fallos).
/// </para>
/// </remarks>
public static class ManejadorSeguro
{
    /// <summary>
    /// Un manejador ha fallado: la excepcion y donde (<c>Fichero.Manejador</c>). Se lanza en
    /// el mismo hilo en que fallo el manejador.
    /// </summary>
    public static event Action<Exception, string>? Fallo;

    /// <summary>Ejecuta el cuerpo de un manejador sin dejar que una excepcion se escape.</summary>
    /// <param name="accion">El cuerpo del manejador.</param>
    /// <param name="manejador">Nombre del manejador; lo pone el compilador.</param>
    /// <param name="fichero">Fichero del manejador; lo pone el compilador.</param>
    public static async Task EjecutarAsync(
        Func<Task> accion,
        [CallerMemberName] string manejador = "",
        [CallerFilePath] string fichero = "")
    {
        ArgumentNullException.ThrowIfNull(accion);
        try
        {
            await accion().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Cancelar no es fallar: el operador ha cortado, o la vista se ha cerrado.
        }
        catch (Exception ex)
        {
            Atender(ex, $"{NombreDelFichero(fichero)}.{manejador}");
        }
    }

    /// <summary>Apunta el fallo de un manejador y avisa, sin lanzar nunca.</summary>
    /// <param name="ex">Lo que ha fallado.</param>
    /// <param name="donde">En que manejador.</param>
    public static void Atender(Exception ex, string donde)
    {
        ArgumentNullException.ThrowIfNull(ex);

        // Al registro ANTES de avisar: si el aviso falla, que al menos quede escrito.
        Log.Error(ex, "Fallo en el manejador {Manejador}; el programa sigue abierto.", donde);

        try
        {
            Fallo?.Invoke(ex, donde);
        }
        catch (Exception alAvisar)
        {
            Log.Error(alAvisar, "No se ha podido avisar del fallo del manejador {Manejador}.", donde);
        }
    }

    private static string NombreDelFichero(string ruta)
    {
        // La ruta la pone el compilador y es la de la maquina donde se compilo, con sus barras.
        var nombre = ruta[(ruta.LastIndexOfAny(['\\', '/']) + 1)..];
        var punto = nombre.IndexOf('.', StringComparison.Ordinal);
        return punto > 0 ? nombre[..punto] : nombre;
    }
}
