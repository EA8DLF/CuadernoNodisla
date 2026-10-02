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
    /// del equipo del operador y esta <b>sin confirmar contra el manual</b>: por eso se puede cambiar.
    /// </remarks>
    public string IndiceDeFuenteSsb { get; set; } = "010113";

    /// <summary>Valor de ese menu que significa «USB».</summary>
    public string ValorDeFuenteUsb { get; set; } = "1";

    // ── Procesado de la escucha ──────────────────────────────────────────────

    /// <summary>Reductor de ruido de la escucha encendido.</summary>
    public bool ReductorActivo { get; set; }

    /// <summary>Reductor: «Espectral» (NR2) o «Adaptativo» (NR1).</summary>
    public string TipoDeReductor { get; set; } = "Espectral";

    /// <summary>Cuanto quita el reductor, en tanto por ciento (0 a 100).</summary>
    public int NivelDeReduccion { get; set; } = 70;

    /// <summary>Notch automatico de portadoras encendido.</summary>
    public bool NotchActivo { get; set; }

    /// <summary>Limitador de los altavoces encendido.</summary>
    public bool LimitadorActivo { get; set; } = true;

    /// <summary>Techo del limitador de los altavoces, en dBFS (-20 a -1).</summary>
    public double TechoEscuchaDb { get; set; } = -3;

    // ── Procesado del microfono ──────────────────────────────────────────────

    /// <summary>Procesar la voz del microfono del PC antes de mandarla al equipo.</summary>
    public bool ProcesarMicro { get; set; }

    /// <summary>Puerta de ruido encendida.</summary>
    public bool PuertaActiva { get; set; } = true;

    /// <summary>Umbral de la puerta, en dBFS (-80 a -20).</summary>
    public double UmbralPuertaDb { get; set; } = -45;

    /// <summary>Corte de graves en hercios (0: sin corte).</summary>
    public int CorteDeGravesHz { get; set; } = 100;

    /// <summary>Graves, en dB (-12 a +6).</summary>
    public double GravesDb { get; set; }

    /// <summary>Medios, en dB (-12 a +6).</summary>
    public double MediosDb { get; set; }

    /// <summary>Agudos, en dB (-12 a +6).</summary>
    public double AgudosDb { get; set; }

    /// <summary>Compresor encendido.</summary>
    public bool CompresorActivo { get; set; } = true;

    /// <summary>Umbral del compresor, en dBFS (-40 a 0).</summary>
    public double UmbralCompresorDb { get; set; } = -18;

    /// <summary>Relacion del compresor (1 a 10).</summary>
    public double RelacionCompresor { get; set; } = 3;

    /// <summary>Techo del microfono, en dBFS (-12 a -1).</summary>
    public double TechoMicroDb { get; set; } = -1;

    // ── Grabador y voice keyer ───────────────────────────────────────────────

    /// <summary>Guardar en memoria los ultimos minutos de la recepcion.</summary>
    public bool GrabarRecepcion { get; set; } = true;

    /// <summary>Minutos que se guardan en memoria (1 a 10).</summary>
    public int MinutosDeGrabacion { get; set; } = 5;

    /// <summary>Al guardar lo ultimo, adjuntarlo al contacto que se esta escribiendo.</summary>
    public bool AdjuntarAlQso { get; set; } = true;

    /// <summary>Las teclas F1 a F6 lanzan los mensajes de voz grabados (en modo de voz).</summary>
    public bool TeclasDeMensajes { get; set; } = true;

    /// <summary>Recorta lo que venga fuera de rango de un fichero editado a mano.</summary>
    public AjustesDeFonia Acotar()
    {
        TipoDeReductor = TipoDeReductor is "Adaptativo" ? "Adaptativo" : "Espectral";
        NivelDeReduccion = Math.Clamp(NivelDeReduccion, 0, 100);
        TechoEscuchaDb = Acotado(TechoEscuchaDb, -20, -1, -3);
        UmbralPuertaDb = Acotado(UmbralPuertaDb, -80, -20, -45);
        CorteDeGravesHz = CorteDeGravesHz <= 0 ? 0 : Math.Clamp(CorteDeGravesHz, 50, 500);
        GravesDb = Acotado(GravesDb, -12, 6, 0);
        MediosDb = Acotado(MediosDb, -12, 6, 0);
        AgudosDb = Acotado(AgudosDb, -12, 6, 0);
        UmbralCompresorDb = Acotado(UmbralCompresorDb, -40, 0, -18);
        RelacionCompresor = Acotado(RelacionCompresor, 1, 10, 3);
        TechoMicroDb = Acotado(TechoMicroDb, -12, -1, -1);
        MinutosDeGrabacion = Math.Clamp(MinutosDeGrabacion, 1, 10);
        VolumenRx = Math.Clamp(VolumenRx, 0, 200);
        GananciaTxDb = double.IsFinite(GananciaTxDb) ? Math.Clamp(GananciaTxDb, -20, 20) : 0;
        TiempoMaximoSegundos = Math.Clamp(TiempoMaximoSegundos, 10, 600);
        TeclaDelPtt ??= string.Empty;
        IndiceDeFuenteSsb ??= string.Empty;
        ValorDeFuenteUsb ??= string.Empty;
        return this;
    }

    private static double Acotado(double valor, double minimo, double maximo, double siNoEsNumero) =>
        double.IsFinite(valor) ? Math.Clamp(valor, minimo, maximo) : siNoEsNumero;
}
