namespace Nodisla.Cuaderno.Radio.Control.OmniRig;

/// <summary>
/// Lo poco que se le pide a OmniRig, separado del COM para poder probarlo sin arrancar nada.
/// </summary>
public interface IOmniRigCrudo : IDisposable
{
    /// <summary>Crea o recrea el objeto de OmniRig y se queda con el equipo indicado.</summary>
    void Abrir();

    /// <summary>Estado del equipo segun OmniRig. Ver <see cref="EstadoOmniRig"/>.</summary>
    int Estado { get; }

    /// <summary>Nombre del fichero de equipo que tiene cargado OmniRig, si lo dice.</summary>
    string? TipoDeEquipo { get; }

    /// <summary>Frecuencia del VFO activo, en hercios.</summary>
    long Frecuencia { get; set; }

    /// <summary>Modo, con las constantes <c>PM_</c> de OmniRig.</summary>
    int Modo { get; set; }

    /// <summary>Estado de transmision, con las constantes <c>PM_RX</c> y <c>PM_TX</c>.</summary>
    int Tx { get; set; }
}

/// <summary>Estados que devuelve OmniRig en <see cref="IOmniRigCrudo.Estado"/>.</summary>
public static class EstadoOmniRig
{
    /// <summary>OmniRig no tiene equipo configurado en esa ranura.</summary>
    public const int SinConfigurar = 0;

    /// <summary>El puerto serie esta ocupado por otro programa.</summary>
    public const int PuertoOcupado = 1;

    /// <summary>El equipo no contesta.</summary>
    public const int NoContesta = 2;

    /// <summary>El equipo esta en linea.</summary>
    public const int EnLinea = 3;
}

/// <summary>
/// Constantes <c>PM_</c> de OmniRig que usa este modulo.
/// </summary>
/// <remarks>Valores de la interfaz COM de OmniRig 1.20; son campos de bits.</remarks>
public static class ParametrosOmniRig
{
    /// <summary>Valor desconocido.</summary>
    public const int Desconocido = 0x0;

    /// <summary>En recepcion.</summary>
    public const int Rx = 0x100000;

    /// <summary>En transmision.</summary>
    public const int Tx = 0x200000;

    /// <summary>CW banda lateral superior.</summary>
    public const int CwSuperior = 0x400000;

    /// <summary>CW banda lateral inferior.</summary>
    public const int CwInferior = 0x800000;

    /// <summary>Banda lateral superior.</summary>
    public const int SsbSuperior = 0x1000000;

    /// <summary>Banda lateral inferior.</summary>
    public const int SsbInferior = 0x2000000;

    /// <summary>Datos por banda lateral superior.</summary>
    public const int DatosSuperior = 0x4000000;

    /// <summary>Datos por banda lateral inferior.</summary>
    public const int DatosInferior = 0x8000000;

    /// <summary>Modulacion de amplitud.</summary>
    public const int Am = 0x10000000;

    /// <summary>Frecuencia modulada.</summary>
    public const int Fm = 0x20000000;
}
