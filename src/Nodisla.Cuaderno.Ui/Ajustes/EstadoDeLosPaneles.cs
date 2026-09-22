using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace Nodisla.Cuaderno.Ui.Ajustes;

/// <summary>Cual de los paneles de operacion se esta mirando.</summary>
public enum PanelDeOperacion
{
    /// <summary>El mapa.</summary>
    Mapa,

    /// <summary>El cluster de DX.</summary>
    Cluster,

    /// <summary>Los modos digitales.</summary>
    Digital,
}

/// <summary>
/// Como dejo el operador los paneles de operacion la ultima vez.
/// </summary>
/// <remarks>
/// Se guarda en disco porque quien pliega la columna de operacion para dejarle la ventana al
/// cuaderno no quiere encontrarsela abierta otra vez al arrancar. Es un fichero pequeno y
/// suyo: si se borra o se corrompe, se vuelve a lo de fabrica sin molestar a nadie.
/// </remarks>
public sealed class EstadoDeLosPaneles
{
    /// <summary>Nombre del fichero dentro de la carpeta de datos del programa.</summary>
    public const string NombreDelFichero = "paneles.json";

    private static readonly JsonSerializerOptions Formato = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>La columna de operacion esta a la vista.</summary>
    public bool PanelVisible { get; set; } = true;

    /// <summary>Panel que se estaba mirando.</summary>
    public PanelDeOperacion PanelElegido { get; set; } = PanelDeOperacion.Mapa;

    /// <summary>Pestana que se estaba mirando: 0 Operar, 1 Cuaderno, 2 Mapa.</summary>
    public int Pestana { get; set; }

    /// <summary>El frontal del equipo estaba desplegado en la cabina.</summary>
    public bool EquipoDesplegado { get; set; } = true;

    /// <summary>Ancho de la columna de operacion, medido en letras.</summary>
    public double AnchoEnLetras { get; set; } = 34;

    /// <summary>
    /// El programa estaba en tema oscuro.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Quien opera de noche pone el tema oscuro y no quiere volver a ponerlo cada vez que
    /// abre el cuaderno. Lo mismo vale para la escala de letra.
    /// </para>
    /// <para>
    /// <b>Viene puesto de fabrica.</b> Una cabina de radio se mira a oscuras y durante horas,
    /// y el tema claro obliga a bajar el brillo de la pantalla para poder seguir mirando el
    /// dial. El claro sigue estando, para quien opere de dia o prefiera imprimir.
    /// </para>
    /// </remarks>
    public bool TemaOscuro { get; set; } = true;

    /// <summary>Escala de letra que tenia puesta, en tanto por ciento.</summary>
    public int EscalaDeLetra { get; set; } = 100;

    /// <summary>Se dibuja la sombra de la noche sobre el mapa.</summary>
    public bool PasoGris { get; set; } = true;

    /// <summary>Se descargan los mosaicos del mapa de fondo.</summary>
    public bool FondoDelMapa { get; set; } = true;

    /// <summary>Se pintan en el mapa los contactos del cuaderno.</summary>
    public bool ContactosEnElMapa { get; set; } = true;

    /// <summary>Se pintan en el mapa los spots del cluster.</summary>
    public bool SpotsEnElMapa { get; set; } = true;

    /// <summary>La consola cruda del cluster esta desplegada.</summary>
    public bool ConsolaDelCluster { get; set; } = true;

    /// <summary>Lee el estado guardado, o devuelve el de fabrica si no hay ninguno.</summary>
    /// <param name="carpeta">Carpeta de datos del programa.</param>
    /// <returns>El estado leido, nunca nulo.</returns>
    public static EstadoDeLosPaneles Leer(string carpeta)
    {
        try
        {
            var ruta = Path.Combine(carpeta, NombreDelFichero);
            if (!File.Exists(ruta)) return new EstadoDeLosPaneles();

            return JsonSerializer.Deserialize<EstadoDeLosPaneles>(File.ReadAllText(ruta), Formato)
                   ?? new EstadoDeLosPaneles();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "No se ha podido leer el estado de los paneles; se usa el de fábrica.");
            return new EstadoDeLosPaneles();
        }
    }

    /// <summary>Guarda el estado en la carpeta de datos del programa.</summary>
    /// <param name="carpeta">Carpeta de datos del programa.</param>
    public void Guardar(string carpeta)
    {
        try
        {
            Directory.CreateDirectory(carpeta);
            File.WriteAllText(Path.Combine(carpeta, NombreDelFichero), JsonSerializer.Serialize(this, Formato));
        }
        catch (Exception ex)
        {
            // Que no se pueda guardar una preferencia no puede impedir cerrar el programa.
            Log.Warning(ex, "No se ha podido guardar el estado de los paneles.");
        }
    }
}
