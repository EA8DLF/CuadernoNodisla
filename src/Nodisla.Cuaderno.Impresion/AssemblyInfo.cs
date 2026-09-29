using System.Runtime.CompilerServices;

// Las pruebas comprueban directamente el ajuste de columnas de las etiquetas (Encajar, Celdas,
// Recortar): con una fuente incrustada en Unicode el texto ya no aparece como texto llano en los
// bytes del PDF, y medir el algoritmo con la misma fuente de verdad es una prueba mas fuerte que
// buscar cadenas en el fichero.
[assembly: InternalsVisibleTo("Nodisla.Cuaderno.Impresion.Pruebas")]
