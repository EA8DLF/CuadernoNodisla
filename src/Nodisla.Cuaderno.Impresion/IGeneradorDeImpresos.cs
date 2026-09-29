using Nodisla.Cuaderno.Impresion.Modelo;

namespace Nodisla.Cuaderno.Impresion;

/// <summary>Un documento listo para ver o imprimir.</summary>
/// <param name="Bytes">El fichero entero.</param>
/// <param name="TipoDeMedio">Tipo de medio, para quien lo tenga que abrir.</param>
/// <param name="Paginas">Cuantas hojas salen.</param>
/// <param name="NombreSugerido">Nombre de fichero propuesto al guardar.</param>
public sealed record Impreso(byte[] Bytes, string TipoDeMedio, int Paginas, string NombreSugerido);

/// <summary>
/// Quien convierte las etiquetas y las tarjetas en un documento imprimible.
/// </summary>
/// <remarks>
/// <para>
/// <b>Esta interfaz existe para poder cambiar de biblioteca sin tocar nada mas.</b> Hoy la
/// implementa <see cref="Pdf.GeneradorDeImpresosPdf"/>, que escribe el PDF a mano y no depende
/// de ningun paquete. Si algun dia hacen falta imagenes, codigos QR o fuentes incrustadas —que
/// es lo unico que no cubre el escritor propio— se escribe otra implementacion con la
/// biblioteca que se elija y se cambia el registro en el contenedor. Ni el modelo de la
/// etiqueta, ni la seleccion de contactos, ni la pantalla se enteran.
/// </para>
/// <para>
/// <b>Lo que se ve en pantalla y lo que sale por la impresora son el mismo fichero.</b> No hay
/// un dibujo para la vista previa y otro para el papel: la vista previa ensena estos bytes. Es
/// la unica forma de que la previsualizacion signifique algo.
/// </para>
/// </remarks>
public interface IGeneradorDeImpresos
{
    /// <summary>Compone una tirada de etiquetas de QSL.</summary>
    /// <param name="etiquetas">Las etiquetas, ya agrupadas por corresponsal.</param>
    /// <param name="plantilla">Papel de etiquetas sobre el que se imprime.</param>
    /// <param name="opciones">Ajustes de dibujo; si no se dan, los de omision.</param>
    /// <returns>El documento listo para ver o imprimir.</returns>
    Impreso Etiquetas(
        IReadOnlyList<EtiquetaDeQsl> etiquetas,
        PlantillaDeEtiquetas plantilla,
        OpcionesDeImpresion? opciones = null);

    /// <summary>Compone una tirada de tarjetas de QSL.</summary>
    /// <param name="tarjetas">Las tarjetas.</param>
    /// <param name="plantilla">Colocacion de las tarjetas en la hoja.</param>
    /// <param name="opciones">Ajustes de dibujo; si no se dan, los de omision.</param>
    /// <returns>El documento listo para ver o imprimir.</returns>
    Impreso Tarjetas(
        IReadOnlyList<TarjetaDeQsl> tarjetas,
        PlantillaDeTarjetas plantilla,
        OpcionesDeImpresion? opciones = null);
}

/// <summary>Ajustes de dibujo comunes a etiquetas y tarjetas.</summary>
public sealed class OpcionesDeImpresion
{
    /// <summary>Aire entre el borde del adhesivo o de la tarjeta y el texto, en milimetros.</summary>
    /// <remarks>
    /// Dos milimetros. Las impresoras domesticas se desvian medio milimetro largo al arrastrar
    /// el papel; con menos aire, una desviacion normal corta el texto por el borde.
    /// </remarks>
    public double MargenInteriorMm { get; set; } = 2.0;

    /// <summary>
    /// Dibujar el contorno de cada etiqueta.
    /// </summary>
    /// <remarks>
    /// Apagado. Sirve para la prueba en papel normal: se imprime con el contorno, se mira al
    /// trasluz contra la hoja de etiquetas y se comprueba que cuadra <b>antes</b> de gastar
    /// adhesivos. En la tirada de verdad hay que quitarlo.
    /// </remarks>
    public bool DibujarContorno { get; set; }

    /// <summary>
    /// Empezar a imprimir en esta posicion de la primera hoja, empezando en cero.
    /// </summary>
    /// <remarks>
    /// Para aprovechar una hoja a la que ya se le han quitado etiquetas. Es una peticion real y
    /// constante: las hojas medio usadas se acumulan.
    /// </remarks>
    public int PrimeraCasilla { get; set; }

    /// <summary>Tamano base del texto de los contactos, en puntos.</summary>
    public double TamanoDeTextoPuntos { get; set; } = 7.0;

    /// <summary>Titulo del documento.</summary>
    public string Titulo { get; set; } = "Etiquetas de QSL — Cuaderno NODISLA";
}
