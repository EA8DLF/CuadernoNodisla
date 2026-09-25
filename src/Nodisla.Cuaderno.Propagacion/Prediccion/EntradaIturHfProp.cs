using System.Globalization;
using System.Text;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Propagacion.Prediccion;

/// <summary>
/// Escribe el fichero de entrada que ITURHFProp espera.
/// </summary>
/// <remarks>
/// <para>
/// Es texto plano con una pareja nombre-valor por linea. El formato esta descrito en el LEEME de
/// <c>herramientas\ITURHFProp</c>, junto con las tres trampas que tiene y que aqui se esquivan.
/// </para>
/// <para>
/// Se genera sin tocar el disco para poder fijarlo con pruebas: lo que sale de aqui es una
/// cadena, y quien la escriba es cosa del motor.
/// </para>
/// </remarks>
public static class EntradaIturHfProp
{
    /// <summary>Frecuencia mas baja que el modelo P.533 acepta, en megahercios.</summary>
    public const double FrecuenciaMinimaMhz = 1.6;

    /// <summary>Frecuencia mas alta que el modelo P.533 acepta, en megahercios.</summary>
    public const double FrecuenciaMaximaMhz = 30.0;

    /// <summary>Numero de manchas mas bajo que acepta.</summary>
    public const int ManchasMinimas = 1;

    /// <summary>Numero de manchas mas alto que acepta.</summary>
    public const int ManchasMaximas = 311;

    /// <summary>
    /// Columnas que se le piden. Se usa <c>RPT_ALL</c> a proposito y no una lista de opciones.
    /// </summary>
    /// <remarks>
    /// El analizador de opciones de la UIT lee el resto de la linea con un <c>%[a-z,A-Z _|]</c>
    /// que <b>no admite digitos</b>: en cuanto aparece una opcion con numero, como
    /// <c>RPT_N0_F2</c>, se pierde esa y todas las que vengan detras, y el informe sale con menos
    /// columnas sin que nadie avise. Con <c>RPT_ALL</c> no hay lista que analizar y ademas la
    /// cabecera del informe numera las columnas, asi que se leen por su nombre.
    /// </remarks>
    public const string FormatoDelInforme = "RPT_ALL";

    /// <summary>La frecuencia cae dentro de lo que el modelo sabe calcular.</summary>
    /// <param name="frecuenciaMhz">Frecuencia de trabajo.</param>
    public static bool EstaEnRango(double frecuenciaMhz) =>
        frecuenciaMhz >= FrecuenciaMinimaMhz && frecuenciaMhz <= FrecuenciaMaximaMhz;

    /// <summary>Compone el fichero de entrada.</summary>
    /// <param name="origen">Estacion que transmite.</param>
    /// <param name="destino">Estacion a la que se quiere llegar.</param>
    /// <param name="momentoUtc">Momento para el que se predice.</param>
    /// <param name="frecuenciasMhz">Frecuencias a calcular, todas dentro del rango del modelo.</param>
    /// <param name="manchasSolares">Numero de manchas R12.</param>
    /// <param name="potenciaVatios">Potencia de transmision.</param>
    /// <param name="opciones">Antena, ancho de banda, ambiente de ruido y senal exigida.</param>
    /// <param name="rutaDeLosDatos">Carpeta con los mapas ionosfericos y los coeficientes.</param>
    public static string Componer(
        Coordenada origen,
        Coordenada destino,
        DateTimeOffset momentoUtc,
        IReadOnlyList<double> frecuenciasMhz,
        double manchasSolares,
        double potenciaVatios,
        OpcionesPropagacion opciones,
        string rutaDeLosDatos)
    {
        ArgumentNullException.ThrowIfNull(frecuenciasMhz);
        ArgumentNullException.ThrowIfNull(opciones);
        if (frecuenciasMhz.Count == 0)
        {
            throw new ArgumentException("Hace falta al menos una frecuencia.", nameof(frecuenciasMhz));
        }

        var utc = momentoUtc.ToUniversalTime();
        var cultura = CultureInfo.InvariantCulture;

        var texto = new StringBuilder();
        void Linea(string clave, string valor) => texto.Append(clave).Append(' ').Append(valor).Append('\n');
        void Numero(string clave, double valor) => Linea(clave, valor.ToString("0.######", cultura));

        Linea("PathName", "\"Cuaderno NODISLA\"");
        Linea("PathTXName", "\"ORIGEN\"");
        Numero("Path.L_tx.lat", origen.Latitud);
        Numero("Path.L_tx.lng", origen.Longitud);
        Linea("TXAntFilePath", "\"ISOTROPIC\"");
        Numero("TXGOS", opciones.GananciaAntenaDbi);
        Linea("PathRXName", "\"DESTINO\"");
        Numero("Path.L_rx.lat", destino.Latitud);
        Numero("Path.L_rx.lng", destino.Longitud);
        Linea("RXAntFilePath", "\"ISOTROPIC\"");
        Numero("RXGOS", opciones.GananciaAntenaDbi);
        Linea("AntennaOrientation", "\"TX2RX\"");

        Linea("Path.year", utc.Year.ToString(cultura));
        Linea("Path.month", utc.Month.ToString(cultura));
        Linea("Path.hour", HoraDelModelo(utc).ToString(cultura));
        Linea("Path.SSN", Manchas(manchasSolares).ToString(cultura));
        Linea("Path.frequency", string.Join(", ", frecuenciasMhz.Select(f => f.ToString("0.###", cultura))));
        Numero("Path.txpower", PotenciaDbKw(potenciaVatios));
        Numero("Path.BW", opciones.AnchoDeBandaHz);
        Numero("Path.SNRr", opciones.RelacionSenalRuidoRequeridaDb);
        Linea("Path.SNRXXp", "90");
        Linea("Path.ManMadeNoise", "\"" + NombreDelRuido(opciones.AmbienteDeRuido) + "\"");

        // No se usa modulacion digital: el cuaderno no pide dispersion de tiempo ni de frecuencia.
        Linea("Path.Modulation", "\"ANALOG\"");
        Numero("Path.SIRr", 0.0);
        Numero("Path.A", 0.0);
        Numero("Path.TW", 0.0);
        Numero("Path.FW", 0.0);
        Numero("Path.T0", 0.0);
        Numero("Path.F0", 0.0);
        Linea("Path.SorL", "\"SHORTPATH\"");
        Linea("RptFileFormat", "\"" + FormatoDelInforme + "\"");

        // Punto a punto: las cuatro esquinas del area son el mismo punto, el destino.
        foreach (var esquina in new[] { "LL", "LR", "UL", "UR" })
        {
            Numero(esquina + ".lat", destino.Latitud);
            Numero(esquina + ".lng", destino.Longitud);
        }

        Numero("latinc", 1.0);
        Numero("lnginc", 1.0);
        Linea("DataFilePath", "\"" + RutaConBarraFinal(rutaDeLosDatos) + "\"");
        return texto.ToString();
    }

    /// <summary>
    /// Hora que entiende el modelo, de 1 a 24.
    /// </summary>
    /// <param name="momentoUtc">Momento del que se toma la hora.</param>
    /// <remarks>
    /// <b>Cuidado:</b> a ITURHFProp no se le puede pasar la hora 0. No la rechaza: se cae con
    /// una violacion de segmento y no deja informe. La medianoche es la hora 24.
    /// </remarks>
    public static int HoraDelModelo(DateTimeOffset momentoUtc)
    {
        var hora = momentoUtc.ToUniversalTime().Hour;
        return hora == 0 ? 24 : hora;
    }

    /// <summary>Potencia en decibelios sobre un kilovatio, que es como la pide el modelo.</summary>
    /// <param name="vatios">Potencia de transmision en vatios.</param>
    public static double PotenciaDbKw(double vatios) =>
        10.0 * Math.Log10(Math.Max(1e-3, vatios) / 1000.0);

    /// <summary>Numero de manchas recortado al rango que acepta el modelo.</summary>
    /// <param name="manchas">Numero de manchas.</param>
    public static int Manchas(double manchas) =>
        (int)Math.Round(Math.Clamp(manchas, ManchasMinimas, ManchasMaximas));

    /// <summary>Nombre que el modelo da a cada ambiente de ruido.</summary>
    /// <param name="ambiente">Ambiente de ruido del emplazamiento.</param>
    public static string NombreDelRuido(AmbienteDeRuido ambiente) => ambiente switch
    {
        AmbienteDeRuido.Industrial => "CITY",
        AmbienteDeRuido.Residencial => "RESIDENTIAL",
        AmbienteDeRuido.Rural => "RURAL",
        _ => "QUIETRURAL",
    };

    private static string RutaConBarraFinal(string ruta)
    {
        // El modelo pega el nombre del fichero detras de esta ruta sin poner separador.
        var limpia = ruta.Replace('\\', '/');
        return limpia.EndsWith('/') ? limpia : limpia + "/";
    }
}
