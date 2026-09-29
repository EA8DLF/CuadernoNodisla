using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Dominio.Entidades;

/// <summary>
/// Perfil de estacion: quien soy, desde donde opero y con que. Al registrar un contacto sus
/// datos se copian al QSO, porque ADIF lo exige y porque asi el historico no se altera si
/// cambias de QTH o de equipo.
/// </summary>
public sealed class Estacion
{
    public long Id { get; set; }

    /// <summary>Nombre con el que se elige el perfil: «Casa», «Portable Teide», «Concursos».</summary>
    public required string NombrePerfil { get; set; }

    /// <summary>Indicativo con el que se transmite (<c>STATION_CALLSIGN</c>).</summary>
    public Indicativo StationCallsign { get; set; }

    /// <summary>Operador habitual (<c>OPERATOR</c>).</summary>
    public string? Operator { get; set; }

    /// <summary>Titular de la licencia (<c>OWNER_CALLSIGN</c>).</summary>
    public string? OwnerCallsign { get; set; }

    public string? MyName { get; set; }
    public string? MyStreet { get; set; }
    public string? MyCity { get; set; }
    public string? MyPostalCode { get; set; }
    public string? MyState { get; set; }
    public string? MyCnty { get; set; }
    public string? MyCountry { get; set; }
    public int? MyDxcc { get; set; }
    public int? MyCqZone { get; set; }
    public int? MyItuZone { get; set; }
    public string? MyIota { get; set; }

    /// <summary>Localizador de la estacion.</summary>
    public Locator MyGridsquare { get; set; }

    public double? MyLat { get; set; }
    public double? MyLon { get; set; }
    public double? MyAltitude { get; set; }

    public string? MyRig { get; set; }
    public string? MyAntenna { get; set; }

    /// <summary>Programa de activacion propio, por ejemplo <c>POTA</c> (<c>MY_SIG</c>).</summary>
    public string? MySig { get; set; }

    /// <summary>Referencia del programa propio (<c>MY_SIG_INFO</c>).</summary>
    public string? MySigInfo { get; set; }

    /// <summary>Potencia habitual en vatios, para rellenar por omision.</summary>
    public double? TxPwrDefecto { get; set; }

    /// <summary>Perfil que se usa si no se elige otro.</summary>
    public bool Predeterminado { get; set; }

    /// <summary>Los perfiles retirados dejan de ofrecerse pero no se borran.</summary>
    public bool Activo { get; set; } = true;

    /// <summary>Coordenada de la estacion, del campo explicito o deducida del localizador.</summary>
    public Coordenada? Coordenada =>
        MyLat is { } la && MyLon is { } lo ? new Coordenada(la, lo)
        : !MyGridsquare.EsVacio ? Valores.Coordenada.Desde(MyGridsquare)
        : null;

    /// <summary>Copia los datos del perfil sobre un contacto nuevo.</summary>
    public void AplicarA(Qso qso)
    {
        ArgumentNullException.ThrowIfNull(qso);

        qso.EstacionId = Id;
        qso.StationCallsign = StationCallsign;
        qso.Operator = Operator;
        qso.OwnerCallsign = OwnerCallsign;
        qso.MyGridsquare = MyGridsquare;
        qso.MyCity = MyCity;
        qso.MyState = MyState;
        qso.MyCnty = MyCnty;
        qso.MyCountry = MyCountry;
        qso.MyDxcc = MyDxcc;
        qso.MyCqZone = MyCqZone;
        qso.MyItuZone = MyItuZone;
        qso.MyLat = MyLat;
        qso.MyLon = MyLon;
        qso.MyAltitude = MyAltitude;
        qso.MyRig = MyRig;
        qso.MyAntenna = MyAntenna;
        qso.TxPwr ??= TxPwrDefecto;
    }
}
