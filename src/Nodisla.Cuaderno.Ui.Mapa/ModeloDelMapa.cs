using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Ui.Mapa;

/// <summary>Que representa una marca del mapa, que es lo que decide como se pinta.</summary>
public enum ClaseDeMarca
{
    /// <summary>Un contacto del cuaderno.</summary>
    Contacto,

    /// <summary>Una estacion anunciada en el cluster.</summary>
    Spot,

    /// <summary>La estacion propia.</summary>
    EstacionPropia,
}

/// <summary>Un punto del mapa, ya listo para pintarlo.</summary>
/// <param name="Donde">Posicion sobre la Tierra.</param>
/// <param name="Etiqueta">Lo que se lee al lado de la marca; normalmente el indicativo.</param>
/// <param name="Clase">Que es la marca.</param>
public sealed record MarcaDelMapa(Coordenada Donde, string Etiqueta, ClaseDeMarca Clase)
{
    /// <summary>Texto largo para la sugerencia: banda, modo, fecha, pais.</summary>
    public string? Detalle { get; init; }

    /// <summary>La marca se pinta resaltada. Se usa para lo que seria nuevo en el cuaderno.</summary>
    public bool Destacada { get; init; }

    /// <summary>Lo que el que la pinto quiera colgar de ella; el mapa no lo mira.</summary>
    public object? Etiquetado { get; init; }
}

/// <summary>Un trayecto entre dos puntos, de los que se dibujan por circulo maximo.</summary>
/// <param name="Origen">Punto de salida, normalmente la estacion propia.</param>
/// <param name="Destino">Punto de llegada.</param>
public sealed record TrayectoDelMapa(Coordenada Origen, Coordenada Destino)
{
    /// <summary>Tambien se dibuja el camino largo, el que da la vuelta por el otro lado.</summary>
    public bool ConCaminoLargo { get; init; } = true;

    /// <summary>Texto que acompana al trayecto, si se quiere.</summary>
    public string? Etiqueta { get; init; }
}

/// <summary>Como de tupido se dibuja el mapa, para poder aflojar en equipos lentos.</summary>
public enum DetalleDelMapa
{
    /// <summary>Pocos puntos por curva; va fino en cualquier maquina.</summary>
    Ligero,

    /// <summary>Lo normal.</summary>
    Normal,

    /// <summary>Curvas muy finas; pide una maquina con soltura.</summary>
    Fino,
}
