namespace Nodisla.Cuaderno.Ui.Ajustes;

/// <summary>Como se acciona el PTT de fonia.</summary>
public enum ModoDelPttDeFonia
{
    /// <summary>Se habla mientras se mantiene pulsado.</summary>
    Mantener,

    /// <summary>Un clic empieza y otro clic acaba.</summary>
    Conmutado,
}

/// <summary>
/// La fonia por el ordenador: por donde entra y sale el audio y cuanto se deja hablar.
/// </summary>
public sealed class AjustesDeFonia
{
    /// <summary>Entrada del codec del equipo (lo que se oye de la radio).</summary>
    public string? EntradaDelEquipo { get; set; }

    /// <summary>Altavoces o auriculares del PC.</summary>
    public string? Altavoces { get; set; }

    /// <summary>Microfono del PC.</summary>
    public string? Microfono { get; set; }

    /// <summary>Salida hacia el codec del equipo (lo que se transmite).</summary>
    public string? SalidaAlEquipo { get; set; }

    /// <summary>Volumen de la escucha, en tanto por ciento (0 a 200).</summary>
    public int VolumenRx { get; set; } = 100;

    /// <summary>La escucha esta en silencio.</summary>
    public bool SilencioRx { get; set; }

    /// <summary>Abrir la escucha por el PC en cuanto se entra en Operar.</summary>
    public bool EscucharAlArrancar { get; set; }

    /// <summary>Ganancia del microfono, en decibelios (-20 a +20).</summary>
    public double GananciaTxDb { get; set; }

    /// <summary>Tiempo maximo de una pasada de fonia, en segundos. Obligatorio.</summary>
    public int TiempoMaximoSegundos { get; set; } = 180;

    /// <summary>Mantener pulsado o conmutado.</summary>
    public ModoDelPttDeFonia ModoDelPtt { get; set; } = ModoDelPttDeFonia.Mantener;

    /// <summary>Tecla del PTT, con el nombre de <c>System.Windows.Input.Key</c>. Vacia: ninguna.</summary>
    public string TeclaDelPtt { get; set; } = string.Empty;

    /// <summary>Comprobar por CAT que la fuente de modulacion de fonia es USB.</summary>
    public bool ComprobarFuenteDeModulacion { get; set; } = true;

    /// <summary>
    /// Indice del menu del FT-710 que guarda la fuente de modulacion en SSB.
    /// </summary>
    /// <remarks>
    /// El equipo no dice como se llama cada ajuste, solo su indice. Este sale del mapa leido
    /// del equipo de Jose y esta <b>sin confirmar contra el manual</b>: por eso se puede cambiar.
    /// </remarks>
    public string IndiceDeFuenteSsb { get; set; } = "010113";

    /// <summary>Valor de ese menu que significa «USB».</summary>
    public string ValorDeFuenteUsb { get; set; } = "1";

    /// <summary>Recorta lo que venga fuera de rango de un fichero editado a mano.</summary>
    public AjustesDeFonia Acotar()
    {
        VolumenRx = Math.Clamp(VolumenRx, 0, 200);
        GananciaTxDb = double.IsFinite(GananciaTxDb) ? Math.Clamp(GananciaTxDb, -20, 20) : 0;
        TiempoMaximoSegundos = Math.Clamp(TiempoMaximoSegundos, 10, 600);
        TeclaDelPtt ??= string.Empty;
        IndiceDeFuenteSsb ??= string.Empty;
        ValorDeFuenteUsb ??= string.Empty;
        return this;
    }
}
