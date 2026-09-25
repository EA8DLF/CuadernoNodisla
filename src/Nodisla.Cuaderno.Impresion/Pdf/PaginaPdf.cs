using System.Globalization;
using System.Text;

namespace Nodisla.Cuaderno.Impresion.Pdf;

/// <summary>
/// Una pagina sobre la que se pinta en milimetros, con el origen arriba a la izquierda.
/// </summary>
/// <remarks>
/// El PDF cuenta en puntos tipograficos y con el origen abajo a la izquierda; las plantillas de
/// etiquetas, los catalogos de papel y las reglas cuentan en milimetros desde arriba. La
/// conversion se hace aqui, en un solo sitio, para que el resto del modulo escriba las
/// coordenadas igual que vienen en la hoja de especificaciones del fabricante. Mezclar las dos
/// convenciones por ahi suelto es como salen las etiquetas del reves.
/// </remarks>
public sealed class PaginaPdf
{
    private const double PuntosPorMm = 72.0 / 25.4;

    private readonly DocumentoPdf _documento;
    private readonly StringBuilder _flujo = new();

    internal PaginaPdf(DocumentoPdf documento, TamanoDePagina tamano)
    {
        _documento = documento;
        Tamano = tamano;
    }

    /// <summary>Tamano de la pagina.</summary>
    public TamanoDePagina Tamano { get; }

    /// <summary>Lo que se ha pintado, en el lenguaje de contenido del PDF.</summary>
    internal string Contenido => _flujo.ToString();

    /// <summary>
    /// Escribe un texto con su esquina superior izquierda en el punto dado.
    /// </summary>
    /// <param name="texto">Texto; si esta vacio no se escribe nada.</param>
    /// <param name="xMm">Distancia desde el borde izquierdo.</param>
    /// <param name="yMm">Distancia desde el borde superior hasta lo alto de la linea.</param>
    /// <param name="familia">Fuente.</param>
    /// <param name="tamanoPuntos">Tamano en puntos.</param>
    /// <param name="gris">Tono de gris, de 0 (negro) a 1 (blanco).</param>
    public void Texto(
        string? texto,
        double xMm,
        double yMm,
        FamiliaDeFuente familia,
        double tamanoPuntos,
        double gris = 0.0)
    {
        if (string.IsNullOrEmpty(texto))
        {
            return;
        }

        var indice = _documento.IndiceDeFuente(familia);

        // La linea base va por debajo de lo alto de la linea: se baja aproximadamente el 80 %
        // del tamano, que es la altura de las mayusculas en las fuentes estandar.
        var baseY = yMm + (tamanoPuntos * 0.8 / PuntosPorMm);

        _flujo.Append(CultureInfo.InvariantCulture, $"BT /F{indice} {N(tamanoPuntos)} Tf ");
        if (gris > 0)
        {
            _flujo.Append(CultureInfo.InvariantCulture, $"{N(gris)} g ");
        }

        _flujo.Append(CultureInfo.InvariantCulture,
            $"1 0 0 1 {N(APuntosX(xMm))} {N(APuntosY(baseY))} Tm ");
        _flujo.Append(CultureInfo.InvariantCulture, $"({DocumentoPdf.Escapar(texto)}) Tj ET\n");

        if (gris > 0)
        {
            _flujo.Append("0 g\n");
        }
    }

    /// <summary>
    /// Escribe un texto dentro de una casilla, alineado y recortado si no cabe.
    /// </summary>
    /// <param name="texto">Texto.</param>
    /// <param name="xMm">Borde izquierdo de la casilla.</param>
    /// <param name="yMm">Borde superior de la casilla.</param>
    /// <param name="anchoMm">Ancho de la casilla.</param>
    /// <param name="familia">Fuente.</param>
    /// <param name="tamanoPuntos">Tamano en puntos.</param>
    /// <param name="alineacion">Como se alinea dentro de la casilla.</param>
    /// <param name="gris">Tono de gris, de 0 (negro) a 1 (blanco).</param>
    /// <returns>El texto que se ha escrito de verdad, por si se recorto.</returns>
    public string TextoEnCasilla(
        string? texto,
        double xMm,
        double yMm,
        double anchoMm,
        FamiliaDeFuente familia,
        double tamanoPuntos,
        Alineacion alineacion = Alineacion.Izquierda,
        double gris = 0.0)
    {
        var cabe = MetricaDeFuente.Recortar(texto, familia, tamanoPuntos, anchoMm);
        if (cabe.Length == 0)
        {
            return string.Empty;
        }

        var ancho = MetricaDeFuente.AnchuraMm(cabe, familia, tamanoPuntos);
        var x = alineacion switch
        {
            Alineacion.Centro => xMm + ((anchoMm - ancho) / 2),
            Alineacion.Derecha => xMm + anchoMm - ancho,
            _ => xMm,
        };

        Texto(cabe, x, yMm, familia, tamanoPuntos, gris);
        return cabe;
    }

    /// <summary>Dibuja una linea recta.</summary>
    /// <param name="x1Mm">Origen, distancia al borde izquierdo.</param>
    /// <param name="y1Mm">Origen, distancia al borde superior.</param>
    /// <param name="x2Mm">Destino, distancia al borde izquierdo.</param>
    /// <param name="y2Mm">Destino, distancia al borde superior.</param>
    /// <param name="grosorMm">Grosor del trazo.</param>
    /// <param name="gris">Tono de gris, de 0 (negro) a 1 (blanco).</param>
    public void Linea(
        double x1Mm,
        double y1Mm,
        double x2Mm,
        double y2Mm,
        double grosorMm = 0.2,
        double gris = 0.0)
    {
        _flujo.Append(CultureInfo.InvariantCulture,
            $"{N(grosorMm * PuntosPorMm)} w {N(gris)} G ");
        _flujo.Append(CultureInfo.InvariantCulture,
            $"{N(APuntosX(x1Mm))} {N(APuntosY(y1Mm))} m {N(APuntosX(x2Mm))} {N(APuntosY(y2Mm))} l S\n");
        _flujo.Append("0 G\n");
    }

    /// <summary>Dibuja el contorno de un rectangulo.</summary>
    /// <param name="xMm">Borde izquierdo.</param>
    /// <param name="yMm">Borde superior.</param>
    /// <param name="anchoMm">Ancho.</param>
    /// <param name="altoMm">Alto.</param>
    /// <param name="grosorMm">Grosor del trazo.</param>
    /// <param name="gris">Tono de gris, de 0 (negro) a 1 (blanco).</param>
    public void Rectangulo(
        double xMm,
        double yMm,
        double anchoMm,
        double altoMm,
        double grosorMm = 0.2,
        double gris = 0.0)
    {
        _flujo.Append(CultureInfo.InvariantCulture, $"{N(grosorMm * PuntosPorMm)} w {N(gris)} G ");
        _flujo.Append(CultureInfo.InvariantCulture,
            $"{N(APuntosX(xMm))} {N(APuntosY(yMm + altoMm))} "
            + $"{N(anchoMm * PuntosPorMm)} {N(altoMm * PuntosPorMm)} re S\n");
        _flujo.Append("0 G\n");
    }

    /// <summary>Rellena un rectangulo de un tono de gris.</summary>
    /// <param name="xMm">Borde izquierdo.</param>
    /// <param name="yMm">Borde superior.</param>
    /// <param name="anchoMm">Ancho.</param>
    /// <param name="altoMm">Alto.</param>
    /// <param name="gris">Tono de gris, de 0 (negro) a 1 (blanco).</param>
    public void Relleno(double xMm, double yMm, double anchoMm, double altoMm, double gris)
    {
        _flujo.Append(CultureInfo.InvariantCulture, $"{N(gris)} g ");
        _flujo.Append(CultureInfo.InvariantCulture,
            $"{N(APuntosX(xMm))} {N(APuntosY(yMm + altoMm))} "
            + $"{N(anchoMm * PuntosPorMm)} {N(altoMm * PuntosPorMm)} re f\n");
        _flujo.Append("0 g\n");
    }

    private double APuntosX(double xMm) => xMm * PuntosPorMm;

    private double APuntosY(double yMm) => (Tamano.AltoMm - yMm) * PuntosPorMm;

    private static string N(double valor) =>
        valor.ToString("0.###", CultureInfo.InvariantCulture);
}
