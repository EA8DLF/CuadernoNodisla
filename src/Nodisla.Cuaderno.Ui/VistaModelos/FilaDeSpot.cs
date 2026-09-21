using System.Globalization;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>Una linea de la lista de spots, ya escrita como se lee en pantalla.</summary>
public sealed class FilaDeSpot
{
    /// <summary>Monta la fila a partir del anuncio recibido.</summary>
    /// <param name="anuncio">Anuncio ya marcado y con sus repeticiones juntas.</param>
    public FilaDeSpot(AnuncioDelCluster anuncio)
    {
        ArgumentNullException.ThrowIfNull(anuncio);

        Anuncio = anuncio;
        var spot = anuncio.Spot;
        Spot = spot;
        Clave = ClaveDeAnuncio.De(spot);

        QuienesLoOyen = anuncio.Veces > 1
            ? $"{anuncio.Veces.ToString(CultureInfo.CurrentCulture)} lo oyen"
            : string.Empty;

        DesdeDonde = anuncio.Continentes.Count > 1
            ? string.Join(" ", anuncio.Continentes)
            : string.Empty;

        Hora = spot.RecibidoUtc.UtcDateTime.ToString("HH:mm", CultureInfo.InvariantCulture);
        Indicativo = spot.Indicativo.Valor;
        Frecuencia = TextoDeFrecuencia.Escribir(spot.Frecuencia);
        Banda = spot.Banda.EsVacia ? string.Empty : spot.Banda.Nombre;
        Modo = spot.ModoAnunciado.EsVacio
            ? FiltroDeSpots.ModoProbable(spot.Frecuencia)
            : spot.ModoAnunciado.NombreUsual;
        Localizador = spot.Locator.EsVacio ? string.Empty : spot.Locator.Valor;
        Referencias = string.Join(
            " · ",
            spot.Referencias.Select(r => $"{r.Tipo.ToString().ToUpperInvariant()} {r.Codigo}"));
        PalabrasPorMinuto = spot.PalabrasPorMinuto is { } ppm
            ? $"{ppm.ToString(CultureInfo.CurrentCulture)} ppm"
            : string.Empty;
        Pais = spot.Pais ?? string.Empty;
        Continente = spot.Continente ?? string.Empty;
        Anunciante = spot.Anunciante.Valor;
        Comentario = spot.Comentario ?? string.Empty;

        Decibelios = spot.Decibelios is { } db
            ? db.ToString("+00;-00;+00", CultureInfo.InvariantCulture)
            : string.Empty;

        Novedad = spot switch
        {
            { EsEntidadNueva: true } => "Entidad nueva",
            { EsNuevoEnBandaYModo: true } => "Nueva en banda y modo",
            _ => string.Empty,
        };
    }

    /// <summary>Anuncio del que sale la fila, con sus repeticiones juntas.</summary>
    public AnuncioDelCluster Anuncio { get; }

    /// <summary>Spot mas reciente, que es el que manda.</summary>
    public Spot Spot { get; }

    /// <summary>Lo que identifica a esta estacion para saber si un anuncio se repite.</summary>
    public ClaveDeAnuncio Clave { get; }

    /// <summary>Cuantas estaciones la estan oyendo, cuando es mas de una.</summary>
    public string QuienesLoOyen { get; } = string.Empty;

    /// <summary>Continentes desde los que se la oye, cuando es mas de uno.</summary>
    public string DesdeDonde { get; } = string.Empty;

    /// <summary>La estan oyendo desde varios sitios: senal de que esta entrando bien.</summary>
    public bool LaOyenVarios => Anuncio.Veces > 1;

    /// <summary>Hora UTC en la que llego el anuncio.</summary>
    public string Hora { get; }

    /// <summary>Indicativo anunciado.</summary>
    public string Indicativo { get; }

    /// <summary>Frecuencia en megahercios, siempre con punto decimal.</summary>
    public string Frecuencia { get; }

    /// <summary>Banda del anuncio.</summary>
    public string Banda { get; }

    /// <summary>Modo del anuncio, deducido del plan de bandas si el cluster no lo dice.</summary>
    public string Modo { get; }

    /// <summary>Entidad DXCC de la estacion anunciada.</summary>
    public string Pais { get; }

    /// <summary>Continente de la entidad.</summary>
    public string Continente { get; }

    /// <summary>Quien puso el anuncio.</summary>
    public string Anunciante { get; }

    /// <summary>Texto libre del anuncio.</summary>
    public string Comentario { get; }

    /// <summary>Relacion senal-ruido, cuando la fuente la da.</summary>
    public string Decibelios { get; }

    /// <summary>Localizador de la estacion, cuando el nodo lo manda.</summary>
    public string Localizador { get; } = string.Empty;

    /// <summary>Referencias de activacion que venian en el comentario.</summary>
    public string Referencias { get; } = string.Empty;

    /// <summary>Velocidad de manipulacion, cuando la fuente la da.</summary>
    public string PalabrasPorMinuto { get; } = string.Empty;

    /// <summary>El anuncio lo puso una estacion automatica de escucha, no una persona.</summary>
    public bool EsDeEscuchaAutomatica => Spot.EsDeEscuchaAutomatica;

    /// <summary>Por que este spot merece atencion, o vacio si no la merece.</summary>
    public string Novedad { get; }

    /// <summary>La entidad no esta en el cuaderno en ninguna banda.</summary>
    public bool EsEntidadNueva => Spot.EsEntidadNueva;

    /// <summary>Esta en el cuaderno, pero no en esta banda y este modo.</summary>
    public bool EsHuecoNuevo => !Spot.EsEntidadNueva && Spot.EsNuevoEnBandaYModo;

    /// <summary>El spot aporta algo: o es entidad nueva o llena un hueco.</summary>
    public bool EsInteresante => EsEntidadNueva || EsHuecoNuevo;

    /// <summary>Texto largo para la sugerencia.</summary>
    public string Detalle
    {
        get
        {
            var partes = new List<string> { $"{Indicativo} en {Frecuencia} MHz" };
            if (Banda.Length > 0 || Modo.Length > 0) partes.Add($"{Banda} {Modo}".Trim());
            if (Pais.Length > 0) partes.Add(Pais);
            if (Localizador.Length > 0) partes.Add(Localizador);
            if (Referencias.Length > 0) partes.Add(Referencias);
            if (PalabrasPorMinuto.Length > 0) partes.Add(PalabrasPorMinuto);
            if (QuienesLoOyen.Length > 0) partes.Add(QuienesLoOyen);
            if (DesdeDonde.Length > 0) partes.Add($"desde {DesdeDonde}");
            if (Novedad.Length > 0) partes.Add(Novedad);
            if (Comentario.Length > 0) partes.Add($"«{Comentario}»");
            partes.Add(EsDeEscuchaAutomatica
                ? $"escucha automática {Anunciante} a las {Hora} UTC"
                : $"anunciado por {Anunciante} a las {Hora} UTC");
            return string.Join(" · ", partes);
        }
    }

    /// <summary>
    /// Donde cae la estacion en el mapa, cuando se sabe.
    /// </summary>
    /// <remarks>
    /// Manda el localizador si el nodo lo mando: son unos kilometros de precision, frente a
    /// los cientos que da el centro de una entidad. Solo cuando no hay localizador se cae al
    /// prefijo y, en ultimo termino, al centro del pais.
    /// </remarks>
    /// <param name="resolutor">Resolutor de entidades DXCC.</param>
    /// <returns>La coordenada, o nulo si no hay manera de situarla.</returns>
    public Coordenada? EnElMapa(Nodisla.Cuaderno.Dominio.Dxcc.IResolutorDxcc resolutor)
    {
        ArgumentNullException.ThrowIfNull(resolutor);

        if (!Spot.Locator.EsVacio) return Coordenada.Desde(Spot.Locator);

        var resuelto = resolutor.Resolver(Spot.Indicativo, DateOnly.FromDateTime(Spot.RecibidoUtc.UtcDateTime));
        return resuelto.Coordenada ?? resuelto.Entidad?.Coordenada;
    }
}
