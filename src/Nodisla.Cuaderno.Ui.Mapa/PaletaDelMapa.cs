using Mapsui.Styles;

namespace Nodisla.Cuaderno.Ui.Mapa;

/// <summary>
/// Los colores del mapa para cada tema.
/// </summary>
/// <remarks>
/// Van aqui y no en los diccionarios de la interfaz porque el mapa se dibuja con su propio
/// motor y no entiende de pinceles de WPF. Los tonos son los mismos que usa el resto del
/// programa, escritos una sola vez para que cambiar el tema no deje el mapa a medias.
/// </remarks>
internal sealed record PaletaDelMapa(
    Color Fondo,
    Color Contacto,
    Color ContactoAproximado,
    Color Spot,
    Color SpotNuevo,
    Color EstacionPropia,
    Color CaminoCorto,
    Color CaminoLargo,
    Color Noche,
    Color LineaDelPasoGris,
    Color Texto,
    Color Halo,
    Color BordeDeMarca)
{
    /// <summary>Paleta del tema claro.</summary>
    public static PaletaDelMapa Clara { get; } = new(
        Fondo: Color.FromArgb(255, 222, 231, 240),
        Contacto: Color.FromArgb(215, 15, 95, 168),
        ContactoAproximado: Color.FromArgb(150, 91, 104, 120),
        Spot: Color.FromArgb(230, 200, 108, 20),
        SpotNuevo: Color.FromArgb(240, 190, 28, 40),
        EstacionPropia: Color.FromArgb(255, 20, 86, 42),
        CaminoCorto: Color.FromArgb(220, 190, 28, 40),
        CaminoLargo: Color.FromArgb(150, 120, 60, 160),
        Noche: Color.FromArgb(60, 10, 22, 45),
        LineaDelPasoGris: Color.FromArgb(190, 40, 60, 100),
        Texto: Color.FromArgb(255, 22, 32, 46),
        Halo: Color.FromArgb(210, 255, 255, 255),
        BordeDeMarca: Color.FromArgb(220, 255, 255, 255));

    /// <summary>Paleta del tema oscuro.</summary>
    public static PaletaDelMapa Oscura { get; } = new(
        Fondo: Color.FromArgb(255, 24, 30, 40),
        Contacto: Color.FromArgb(220, 96, 165, 235),
        ContactoAproximado: Color.FromArgb(150, 130, 145, 165),
        Spot: Color.FromArgb(235, 240, 165, 60),
        SpotNuevo: Color.FromArgb(245, 245, 95, 105),
        EstacionPropia: Color.FromArgb(255, 110, 210, 140),
        CaminoCorto: Color.FromArgb(225, 245, 95, 105),
        CaminoLargo: Color.FromArgb(160, 180, 140, 230),
        Noche: Color.FromArgb(90, 0, 0, 0),
        LineaDelPasoGris: Color.FromArgb(200, 170, 195, 235),
        Texto: Color.FromArgb(255, 232, 238, 246),
        Halo: Color.FromArgb(215, 12, 16, 24),
        BordeDeMarca: Color.FromArgb(200, 20, 26, 36));

    /// <summary>Devuelve la paleta que toca.</summary>
    /// <param name="oscuro">Cierto para el tema oscuro.</param>
    public static PaletaDelMapa Para(bool oscuro) => oscuro ? Oscura : Clara;
}
