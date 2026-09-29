using System.Globalization;

namespace Nodisla.Cuaderno.Satelites.Orbital;

/// <summary>
/// Lee ficheros de elementos orbitales de dos lineas, con o sin la linea del nombre.
/// </summary>
/// <remarks>
/// Los ficheros que se descargan de Celestrak o de AMSAT llegan en tres formas: con nombre
/// (tres lineas por satelite), sin nombre (dos lineas) y mezclados. El lector acepta las tres
/// y se salta lo que no entiende en vez de abortar: un fichero con un satelite roto no puede
/// dejar sin pasos a los otros cincuenta.
/// </remarks>
public static class LectorDeElementos
{
    /// <summary>Lee todos los juegos de elementos que haya en un texto.</summary>
    /// <param name="texto">Contenido completo del fichero.</param>
    /// <returns>Los juegos que se han podido leer, en el orden del fichero.</returns>
    public static IReadOnlyList<ElementosOrbitales> Leer(string texto)
    {
        ArgumentNullException.ThrowIfNull(texto);

        var lineas = texto.Split('\n');
        var resultado = new List<ElementosOrbitales>();
        string? posibleNombre = null;

        for (var i = 0; i < lineas.Length; i++)
        {
            var linea = lineas[i].TrimEnd('\r', ' ', '\t');
            if (linea.Length == 0 || linea[0] == '#')
            {
                continue;
            }

            if (EsLinea(linea, '1') && i + 1 < lineas.Length)
            {
                var linea2 = lineas[i + 1].TrimEnd('\r', ' ', '\t');
                if (EsLinea(linea2, '2')
                    && TryLeerPar(posibleNombre, linea, linea2, out var elementos))
                {
                    resultado.Add(elementos);
                    i++;
                }

                posibleNombre = null;
                continue;
            }

            // Cualquier otra cosa es candidata a ser la linea 0 con el nombre.
            posibleNombre = linea.Trim();
        }

        return resultado;
    }

    /// <summary>Lee un unico satelite a partir de sus dos lineas (y opcionalmente el nombre).</summary>
    /// <param name="nombre">Nombre del satelite; si es nulo se usa el numero de catalogo.</param>
    /// <param name="linea1">Primera linea.</param>
    /// <param name="linea2">Segunda linea.</param>
    /// <returns>Los elementos leidos.</returns>
    /// <exception cref="FormatException">Las lineas no son un juego de elementos valido.</exception>
    public static ElementosOrbitales LeerPar(string? nombre, string linea1, string linea2) =>
        TryLeerPar(nombre, linea1, linea2, out var elementos)
            ? elementos
            : throw new FormatException("Las dos líneas no forman un juego de elementos orbitales válido.");

    /// <summary>Intenta leer un unico satelite a partir de sus dos lineas.</summary>
    /// <param name="nombre">Nombre del satelite; si es nulo se usa el numero de catalogo.</param>
    /// <param name="linea1">Primera linea.</param>
    /// <param name="linea2">Segunda linea.</param>
    /// <param name="elementos">Los elementos leidos.</param>
    /// <returns><c>true</c> si las dos lineas eran validas.</returns>
    public static bool TryLeerPar(
        string? nombre,
        string linea1,
        string linea2,
        out ElementosOrbitales elementos)
    {
        elementos = null!;
        if (linea1 is null || linea2 is null)
        {
            return false;
        }

        var l1 = linea1.TrimEnd();
        var l2 = linea2.TrimEnd();
        if (!EsLinea(l1, '1') || !EsLinea(l2, '2'))
        {
            return false;
        }

        // Algunos ficheros vienen con la linea recortada en la ultima columna; se rellena.
        l1 = l1.PadRight(69);
        l2 = l2.PadRight(69);

        if (!TryEntero(l1, 2, 5, out var catalogo1) || !TryEntero(l2, 2, 5, out var catalogo2)
            || catalogo1 != catalogo2)
        {
            return false;
        }

        if (!TryEntero(l1, 18, 2, out var anio) || !TryReal(l1, 20, 12, out var dia))
        {
            return false;
        }

        if (!TryReal(l2, 8, 8, out var inclinacion)
            || !TryReal(l2, 17, 8, out var nodo)
            || !TryEntero(l2, 26, 7, out var excentricidadEntera)
            || !TryReal(l2, 34, 8, out var argumentoPerigeo)
            || !TryReal(l2, 43, 8, out var anomaliaMedia)
            || !TryReal(l2, 52, 11, out var movimientoMedio))
        {
            return false;
        }

        if (inclinacion is < 0 or > 180 || movimientoMedio <= 0)
        {
            return false;
        }

        var excentricidad = excentricidadEntera / 1.0e7;
        if (excentricidad is < 0 or >= 1)
        {
            return false;
        }

        TryReal(l1, 33, 10, out var primeraDerivada);

        elementos = new ElementosOrbitales
        {
            Nombre = string.IsNullOrWhiteSpace(nombre)
                ? catalogo1.ToString(CultureInfo.InvariantCulture)
                : nombre.Trim(),
            NumeroCatalogo = catalogo1,
            Clasificacion = l1[7] == ' ' ? 'U' : l1[7],
            DesignacionInternacional = l1.Substring(9, 8).Trim(),
            Epoca = EpocaDesde(anio, dia),
            PrimeraDerivadaMovimientoMedio = primeraDerivada,
            SegundaDerivadaMovimientoMedio = LeerExponencial(l1, 44),
            BEstrella = LeerExponencial(l1, 53),
            NumeroDeJuego = TryEntero(l1, 64, 4, out var juego) ? juego : 0,
            InclinacionGrados = inclinacion,
            NodoAscendenteGrados = nodo,
            Excentricidad = excentricidad,
            ArgumentoPerigeoGrados = argumentoPerigeo,
            AnomaliaMediaGrados = anomaliaMedia,
            MovimientoMedioVueltasDia = movimientoMedio,
            NumeroDeVuelta = TryEntero(l2, 63, 5, out var vuelta) ? vuelta : 0,
        };

        return true;
    }

    /// <summary>
    /// Comprueba el digito de control de una linea, que es la suma de sus cifras modulo diez
    /// contando cada signo menos como una unidad.
    /// </summary>
    /// <param name="linea">Linea completa de 69 caracteres.</param>
    /// <returns><c>true</c> si el ultimo caracter coincide con la suma.</returns>
    /// <remarks>
    /// No se exige para aceptar la linea. Hay fuentes historicas y ficheros pegados a mano con
    /// el digito mal que por lo demas son correctos; negarse a leerlos no ayuda a nadie. Sirve
    /// para avisar de que un fichero puede venir corrompido.
    /// </remarks>
    public static bool DigitoDeControlCorrecto(string linea)
    {
        if (string.IsNullOrEmpty(linea) || linea.Length < 69)
        {
            return false;
        }

        var suma = 0;
        for (var i = 0; i < 68; i++)
        {
            var c = linea[i];
            if (c is >= '0' and <= '9')
            {
                suma += c - '0';
            }
            else if (c == '-')
            {
                suma++;
            }
        }

        return linea[68] == (char)('0' + (suma % 10));
    }

    private static bool EsLinea(string linea, char numero) =>
        linea.Length >= 68 && linea[0] == numero && linea[1] == ' ';

    /// <summary>
    /// Convierte el ano de dos cifras y el dia fraccionario de la epoca en un instante UTC.
    /// </summary>
    /// <remarks>
    /// El corte de siglo es el que fija el propio formato: de 57 a 99 es el siglo XX y de 00
    /// a 56 el XXI. Se escribe explicito porque es el error clasico al leer un TLE antiguo.
    /// </remarks>
    private static DateTimeOffset EpocaDesde(int anioDosCifras, double diaDelAnio)
    {
        var anio = anioDosCifras < 57 ? 2000 + anioDosCifras : 1900 + anioDosCifras;
        var principio = new DateTimeOffset(anio, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return principio.AddDays(diaDelAnio - 1.0);
    }

    /// <summary>
    /// Lee los campos en notacion exponencial comprimida del TLE (<c>28098-4</c> = 0,28098e-4).
    /// </summary>
    private static double LeerExponencial(string linea, int inicio)
    {
        var campo = linea.Substring(inicio, 8).Trim();
        if (campo.Length == 0)
        {
            return 0.0;
        }

        var signo = 1.0;
        var i = 0;
        if (campo[0] is '-' or '+')
        {
            signo = campo[0] == '-' ? -1.0 : 1.0;
            i = 1;
        }

        var mantisa = 0.0;
        var digitos = 0;
        while (i < campo.Length && campo[i] is >= '0' and <= '9')
        {
            mantisa = (mantisa * 10) + (campo[i] - '0');
            digitos++;
            i++;
        }

        if (digitos == 0)
        {
            return 0.0;
        }

        mantisa /= Math.Pow(10, digitos);

        var exponente = 0;
        if (i < campo.Length)
        {
            var signoExp = campo[i] == '-' ? -1 : 1;
            if (campo[i] is '-' or '+')
            {
                i++;
            }

            var valor = 0;
            var hayExp = false;
            while (i < campo.Length && campo[i] is >= '0' and <= '9')
            {
                valor = (valor * 10) + (campo[i] - '0');
                hayExp = true;
                i++;
            }

            if (hayExp)
            {
                exponente = signoExp * valor;
            }
        }

        return signo * mantisa * Math.Pow(10, exponente);
    }

    private static bool TryEntero(string linea, int inicio, int longitud, out int valor)
    {
        valor = 0;
        if (inicio + longitud > linea.Length)
        {
            return false;
        }

        var campo = linea.Substring(inicio, longitud).Trim();
        return campo.Length > 0
               && int.TryParse(campo, NumberStyles.Integer, CultureInfo.InvariantCulture, out valor);
    }

    private static bool TryReal(string linea, int inicio, int longitud, out double valor)
    {
        valor = 0;
        if (inicio + longitud > linea.Length)
        {
            return false;
        }

        var campo = linea.Substring(inicio, longitud).Trim();
        if (campo.Length == 0)
        {
            return false;
        }

        // La primera derivada se escribe a veces como ".00000023" o "-.00000084".
        if (campo[0] == '.')
        {
            campo = "0" + campo;
        }
        else if (campo.Length > 1 && campo[0] is '-' or '+' && campo[1] == '.')
        {
            campo = campo[0] + "0" + campo[1..];
        }

        return double.TryParse(campo, NumberStyles.Float, CultureInfo.InvariantCulture, out valor);
    }
}
