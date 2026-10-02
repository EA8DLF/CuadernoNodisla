using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Satelites.Orbital;

/// <summary>Por que no se ha podido propagar la orbita.</summary>
public enum FalloDePropagacion
{
    /// <summary>Todo bien.</summary>
    Ninguno = 0,

    /// <summary>La excentricidad media se ha ido fuera del intervalo valido.</summary>
    ExcentricidadMedia,

    /// <summary>El movimiento medio ha salido negativo.</summary>
    MovimientoMedioNegativo,

    /// <summary>La excentricidad osculatriz se ha ido fuera del intervalo valido.</summary>
    ExcentricidadOsculatriz,

    /// <summary>El semilado recto ha salido negativo.</summary>
    SemiladoRectoNegativo,

    /// <summary>El satelite ha reentrado: el radio calculado esta por debajo de la superficie.</summary>
    Reentrado,

    /// <summary>La orbita es alta y haria falta el modelo de espacio profundo, que no esta implementado.</summary>
    EspacioProfundoNoImplementado,
}

/// <summary>Posicion y velocidad de un satelite en el sistema TEME.</summary>
/// <param name="Posicion">Posicion en kilometros.</param>
/// <param name="Velocidad">Velocidad en kilometros por segundo.</param>
/// <param name="Instante">Instante al que corresponden, en UTC.</param>
public readonly record struct EstadoTeme(Vector3 Posicion, Vector3 Velocidad, DateTimeOffset Instante);

/// <summary>
/// Propagador SGP4 para orbita baja, el mismo modelo con el que se generan los TLE.
/// </summary>
/// <remarks>
/// <para>
/// Un TLE <b>no</b> son elementos keplerianos corrientes: son los elementos medios que espera
/// este modelo concreto. Propagarlos con las ecuaciones de dos cuerpos da errores de decenas
/// de kilometros en un solo dia. Por eso hay que implementar SGP4 y no se puede atajar.
/// </para>
/// <para>
/// Se implementa la rama de orbita baja (periodo menor de 225 minutos), que cubre todos los
/// satelites de aficionado en servicio. La rama de espacio profundo (SDP4, con las
/// perturbaciones del Sol y la Luna y las resonancias) se declara no implementada y se avisa,
/// que es preferible a devolver un numero que nadie ha comprobado.
/// </para>
/// <para>
/// Las constantes son las de WGS-72, no las de WGS-84. No es un descuido: los TLE se generan
/// con WGS-72 y usar otro juego introduce un error que no estaba.
/// </para>
/// </remarks>
public sealed class Sgp4
{
    /// <summary>Radio ecuatorial terrestre de WGS-72, en kilometros.</summary>
    public const double RadioTerrestreKm = 6378.135;

    private const double Mu = 398600.8;
    private const double J2 = 0.001082616;
    private const double J3 = -0.00000253881;
    private const double J4 = -0.00000165597;
    private const double J3EntreJ2 = J3 / J2;
    private const double DosTercios = 2.0 / 3.0;

    private static readonly double Xke = 60.0 / Math.Sqrt(RadioTerrestreKm * RadioTerrestreKm * RadioTerrestreKm / Mu);
    private static readonly double KmPorSegundo = RadioTerrestreKm * Xke / 60.0;

    // Elementos de partida en unidades internas (radianes y radianes por minuto).
    private readonly double _bstar;
    private readonly double _ecco;
    private readonly double _inclo;
    private readonly double _nodeo;
    private readonly double _argpo;
    private readonly double _mo;
    private readonly double _noUnkozai;
    private readonly double _diaJulianoEpoca;

    // Constantes que se calculan una sola vez al inicializar.
    private readonly bool _simplificado;
    private readonly double _aycof, _con41, _cc1, _cc4, _cc5, _d2, _d3, _d4;
    private readonly double _delmo, _eta, _argpdot, _omgcof, _sinmao, _t2cof, _t3cof, _t4cof, _t5cof;
    private readonly double _x1mth2, _x7thm1, _mdot, _nodedot, _xlcof, _xmcof, _nodecf;

    /// <summary>Prepara el propagador para un juego de elementos.</summary>
    /// <param name="elementos">Elementos orbitales de partida.</param>
    /// <exception cref="NotSupportedException">La orbita es de espacio profundo.</exception>
    public Sgp4(ElementosOrbitales elementos)
    {
        ArgumentNullException.ThrowIfNull(elementos);
        Elementos = elementos;

        if (elementos.EsEspacioProfundo)
        {
            throw new NotSupportedException(
                $"«{elementos.Nombre}» tiene un periodo de {elementos.Periodo.TotalMinutes:F0} minutos "
                + "y necesita el modelo de espacio profundo (SDP4), que este módulo no implementa.");
        }

        _bstar = elementos.BEstrella;
        _ecco = elementos.Excentricidad;
        _inclo = elementos.InclinacionGrados * Tiempos.GradosARadianes;
        _nodeo = elementos.NodoAscendenteGrados * Tiempos.GradosARadianes;
        _argpo = elementos.ArgumentoPerigeoGrados * Tiempos.GradosARadianes;
        _mo = elementos.AnomaliaMediaGrados * Tiempos.GradosARadianes;
        _diaJulianoEpoca = Tiempos.DiaJuliano(elementos.Epoca);

        var noKozai = elementos.MovimientoMedioVueltasDia * Tiempos.DosPi / 1440.0;

        // ── Deshacer la transformacion de Kozai: el movimiento medio del TLE lleva dentro
        //    el efecto de J2 y hay que quitarselo antes de usarlo.
        var eccsq = _ecco * _ecco;
        var omeosq = 1.0 - eccsq;
        var rteosq = Math.Sqrt(omeosq);
        var cosio = Math.Cos(_inclo);
        var cosio2 = cosio * cosio;

        var ak = Math.Pow(Xke / noKozai, DosTercios);
        var d1 = 0.75 * J2 * ((3.0 * cosio2) - 1.0) / (rteosq * omeosq);
        var del = d1 / (ak * ak);
        var adel = ak * (1.0 - (del * del) - (del * ((1.0 / 3.0) + (134.0 * del * del / 81.0))));
        del = d1 / (adel * adel);
        _noUnkozai = noKozai / (1.0 + del);

        var ao = Math.Pow(Xke / _noUnkozai, DosTercios);
        var sinio = Math.Sin(_inclo);
        var po = ao * omeosq;
        var con42 = 1.0 - (5.0 * cosio2);
        _con41 = -con42 - cosio2 - cosio2;
        var posq = po * po;
        var rp = ao * (1.0 - _ecco);

        // ── Preparacion de los coeficientes (sgp4init).
        const double ss = (78.0 / RadioTerrestreKm) + 1.0;
        var qzms2t = Math.Pow((120.0 - 78.0) / RadioTerrestreKm, 4);

        _simplificado = rp < (220.0 / RadioTerrestreKm) + 1.0;

        var sfour = ss;
        var qzms24 = qzms2t;
        var perigeo = (rp - 1.0) * RadioTerrestreKm;
        if (perigeo < 156.0)
        {
            sfour = perigeo - 78.0;
            if (perigeo < 98.0)
            {
                sfour = 20.0;
            }

            qzms24 = Math.Pow((120.0 - sfour) / RadioTerrestreKm, 4);
            sfour = (sfour / RadioTerrestreKm) + 1.0;
        }

        var pinvsq = 1.0 / posq;
        var tsi = 1.0 / (ao - sfour);
        _eta = ao * _ecco * tsi;
        var etasq = _eta * _eta;
        var eeta = _ecco * _eta;
        var psisq = Math.Abs(1.0 - etasq);
        var coef = qzms24 * Math.Pow(tsi, 4);
        var coef1 = coef / Math.Pow(psisq, 3.5);

        var cc2 = coef1 * _noUnkozai
                  * ((ao * (1.0 + (1.5 * etasq) + (eeta * (4.0 + etasq))))
                     + (0.375 * J2 * tsi / psisq * _con41 * (8.0 + (3.0 * etasq * (8.0 + etasq)))));
        _cc1 = _bstar * cc2;

        var cc3 = 0.0;
        if (_ecco > 1.0e-4)
        {
            cc3 = -2.0 * coef * tsi * J3EntreJ2 * _noUnkozai * sinio / _ecco;
        }

        _x1mth2 = 1.0 - cosio2;
        _cc4 = 2.0 * _noUnkozai * coef1 * ao * omeosq
               * ((_eta * (2.0 + (0.5 * etasq))) + (_ecco * (0.5 + (2.0 * etasq)))
                  - (J2 * tsi / (ao * psisq)
                     * ((-3.0 * _con41 * (1.0 - (2.0 * eeta) + (etasq * (1.5 - (0.5 * eeta)))))
                        + (0.75 * _x1mth2 * ((2.0 * etasq) - (eeta * (1.0 + etasq)))
                           * Math.Cos(2.0 * _argpo)))));
        _cc5 = 2.0 * coef1 * ao * omeosq * (1.0 + (2.75 * (etasq + eeta)) + (eeta * etasq));

        var cosio4 = cosio2 * cosio2;
        var temp1 = 1.5 * J2 * pinvsq * _noUnkozai;
        var temp2 = 0.5 * temp1 * J2 * pinvsq;
        var temp3 = -0.46875 * J4 * pinvsq * pinvsq * _noUnkozai;

        _mdot = _noUnkozai + (0.5 * temp1 * rteosq * _con41)
                + (0.0625 * temp2 * rteosq * (13.0 - (78.0 * cosio2) + (137.0 * cosio4)));
        _argpdot = (-0.5 * temp1 * con42)
                   + (0.0625 * temp2 * (7.0 - (114.0 * cosio2) + (395.0 * cosio4)))
                   + (temp3 * (3.0 - (36.0 * cosio2) + (49.0 * cosio4)));
        var xhdot1 = -temp1 * cosio;
        _nodedot = xhdot1
                   + (((0.5 * temp2 * (4.0 - (19.0 * cosio2)))
                       + (2.0 * temp3 * (3.0 - (7.0 * cosio2)))) * cosio);

        _omgcof = _bstar * cc3 * Math.Cos(_argpo);
        _xmcof = 0.0;
        if (_ecco > 1.0e-4)
        {
            _xmcof = -DosTercios * coef * _bstar / eeta;
        }

        _nodecf = 3.5 * omeosq * xhdot1 * _cc1;
        _t2cof = 1.5 * _cc1;

        // Con inclinacion de 180 grados el denominador se anula; se sustituye por un numero
        // muy pequeno, que es lo que hace el codigo de referencia.
        _xlcof = Math.Abs(cosio + 1.0) > 1.5e-12
            ? -0.25 * J3EntreJ2 * sinio * (3.0 + (5.0 * cosio)) / (1.0 + cosio)
            : -0.25 * J3EntreJ2 * sinio * (3.0 + (5.0 * cosio)) / 1.5e-12;
        _aycof = -0.5 * J3EntreJ2 * sinio;

        _delmo = Math.Pow(1.0 + (_eta * Math.Cos(_mo)), 3);
        _sinmao = Math.Sin(_mo);
        _x7thm1 = (7.0 * cosio2) - 1.0;

        _d2 = _d3 = _d4 = _t3cof = _t4cof = _t5cof = 0.0;
        if (!_simplificado)
        {
            var cc1sq = _cc1 * _cc1;
            _d2 = 4.0 * ao * tsi * cc1sq;
            var temp = _d2 * tsi * _cc1 / 3.0;
            _d3 = ((17.0 * ao) + sfour) * temp;
            _d4 = 0.5 * temp * ao * tsi * ((221.0 * ao) + (31.0 * sfour)) * _cc1;
            _t3cof = _d2 + (2.0 * cc1sq);
            _t4cof = 0.25 * ((3.0 * _d3) + (_cc1 * ((12.0 * _d2) + (10.0 * cc1sq))));
            _t5cof = 0.2 * ((3.0 * _d4) + (12.0 * _cc1 * _d3) + (6.0 * _d2 * _d2)
                            + (15.0 * cc1sq * ((2.0 * _d2) + cc1sq)));
        }
    }

    /// <summary>Los elementos con los que se construyo el propagador.</summary>
    public ElementosOrbitales Elementos { get; }

    /// <summary>Propaga a un instante dado.</summary>
    /// <param name="instante">Instante al que se quiere la posicion, en UTC.</param>
    /// <param name="estado">Posicion y velocidad en TEME.</param>
    /// <param name="fallo">Motivo por el que no se ha podido propagar, si lo hay.</param>
    /// <returns><c>true</c> si el resultado es utilizable.</returns>
    public bool TryPropagar(DateTimeOffset instante, out EstadoTeme estado, out FalloDePropagacion fallo)
    {
        var minutos = (Tiempos.DiaJuliano(instante) - _diaJulianoEpoca) * 1440.0;
        var ok = TryPropagarMinutos(minutos, out var posicion, out var velocidad, out fallo);
        estado = new EstadoTeme(posicion, velocidad, instante);
        return ok;
    }

    /// <summary>Propaga a un instante dado y falla con excepcion si no se puede.</summary>
    /// <param name="instante">Instante al que se quiere la posicion, en UTC.</param>
    /// <exception cref="InvalidOperationException">La propagacion no ha salido bien.</exception>
    public EstadoTeme Propagar(DateTimeOffset instante) =>
        TryPropagar(instante, out var estado, out var fallo)
            ? estado
            : throw new InvalidOperationException(
                $"No se puede propagar «{Elementos.Nombre}» a {instante:u}: {Descripcion(fallo)}");

    /// <summary>Texto en castellano de un fallo de propagacion, para ensenarselo al operador.</summary>
    /// <param name="fallo">El fallo.</param>
    public static string Descripcion(FalloDePropagacion fallo) => fallo switch
    {
        FalloDePropagacion.Ninguno => Textos.T("Servicios.Satelites.Fallo.Ninguno"),
        FalloDePropagacion.ExcentricidadMedia => Textos.T("Servicios.Satelites.Fallo.ExcentricidadMedia"),
        FalloDePropagacion.MovimientoMedioNegativo => Textos.T("Servicios.Satelites.Fallo.MovimientoMedioNegativo"),
        FalloDePropagacion.ExcentricidadOsculatriz => Textos.T("Servicios.Satelites.Fallo.ExcentricidadOsculatriz"),
        FalloDePropagacion.SemiladoRectoNegativo => Textos.T("Servicios.Satelites.Fallo.SemiladoRectoNegativo"),
        FalloDePropagacion.Reentrado => Textos.T("Servicios.Satelites.Fallo.Reentrado"),
        FalloDePropagacion.EspacioProfundoNoImplementado => Textos.T("Servicios.Satelites.Fallo.EspacioProfundo"),
        _ => Textos.T("Servicios.Satelites.Fallo.Desconocido"),
    };

    /// <summary>
    /// Propaga a un numero de minutos desde la epoca. Es la firma con la que se comparan los
    /// vectores oficiales de verificacion, por eso es publica.
    /// </summary>
    /// <param name="minutosDesdeEpoca">Minutos transcurridos desde la epoca de los elementos.</param>
    /// <param name="posicion">Posicion en TEME, en kilometros.</param>
    /// <param name="velocidad">Velocidad en TEME, en km/s.</param>
    /// <param name="fallo">Motivo por el que no se ha podido propagar, si lo hay.</param>
    /// <returns><c>true</c> si el resultado es utilizable.</returns>
    public bool TryPropagarMinutos(
        double minutosDesdeEpoca,
        out Vector3 posicion,
        out Vector3 velocidad,
        out FalloDePropagacion fallo)
    {
        posicion = default;
        velocidad = default;
        fallo = FalloDePropagacion.Ninguno;

        var t = minutosDesdeEpoca;

        var xmdf = _mo + (_mdot * t);
        var argpdf = _argpo + (_argpdot * t);
        var nodedf = _nodeo + (_nodedot * t);
        var argpm = argpdf;
        var mm = xmdf;
        var t2 = t * t;
        var nodem = nodedf + (_nodecf * t2);
        var tempa = 1.0 - (_cc1 * t);
        var tempe = _bstar * _cc4 * t;
        var templ = _t2cof * t2;

        if (!_simplificado)
        {
            var delomg = _omgcof * t;
            var delm = _xmcof * (Math.Pow(1.0 + (_eta * Math.Cos(xmdf)), 3) - _delmo);
            var tempDelta = delomg + delm;
            mm = xmdf + tempDelta;
            argpm = argpdf - tempDelta;
            var t3 = t2 * t;
            var t4 = t3 * t;
            tempa = tempa - (_d2 * t2) - (_d3 * t3) - (_d4 * t4);
            tempe += _bstar * _cc5 * (Math.Sin(mm) - _sinmao);
            templ = templ + (_t3cof * t3) + (t4 * (_t4cof + (t * _t5cof)));
        }

        var nm = _noUnkozai;
        var em = _ecco;
        var inclm = _inclo;

        if (nm <= 0.0)
        {
            fallo = FalloDePropagacion.MovimientoMedioNegativo;
            return false;
        }

        var am = Math.Pow(Xke / nm, DosTercios) * tempa * tempa;
        nm = Xke / Math.Pow(am, 1.5);
        em -= tempe;

        if (em is >= 1.0 or < -0.001)
        {
            fallo = FalloDePropagacion.ExcentricidadMedia;
            return false;
        }

        if (em < 1.0e-6)
        {
            em = 1.0e-6;
        }

        mm += _noUnkozai * templ;
        var xlm = mm + argpm + nodem;

        nodem %= Tiempos.DosPi;
        argpm %= Tiempos.DosPi;
        xlm %= Tiempos.DosPi;
        mm = (xlm - argpm - nodem) % Tiempos.DosPi;

        // ── Periodicas de largo periodo.
        var sinip = Math.Sin(inclm);
        var cosip = Math.Cos(inclm);
        var ep = em;
        var argpp = argpm;
        var nodep = nodem;
        var mp = mm;

        var axnl = ep * Math.Cos(argpp);
        var tempAux = 1.0 / (am * (1.0 - (ep * ep)));
        var aynl = (ep * Math.Sin(argpp)) + (tempAux * _aycof);
        var xl = mp + argpp + nodep + (tempAux * _xlcof * axnl);

        // ── Ecuacion de Kepler, resuelta por Newton con paso limitado a 0,95 radianes:
        //    sin ese tope no converge en las orbitas muy excentricas.
        var u = (xl - nodep) % Tiempos.DosPi;
        var eo1 = u;
        var tem5 = 9999.9;
        var sineo1 = 0.0;
        var coseo1 = 0.0;
        for (var ktr = 1; Math.Abs(tem5) >= 1.0e-12 && ktr <= 10; ktr++)
        {
            sineo1 = Math.Sin(eo1);
            coseo1 = Math.Cos(eo1);
            tem5 = 1.0 - (coseo1 * axnl) - (sineo1 * aynl);
            tem5 = (u - (aynl * coseo1) + (axnl * sineo1) - eo1) / tem5;
            if (Math.Abs(tem5) >= 0.95)
            {
                tem5 = tem5 > 0.0 ? 0.95 : -0.95;
            }

            eo1 += tem5;
        }

        var ecose = (axnl * coseo1) + (aynl * sineo1);
        var esine = (axnl * sineo1) - (aynl * coseo1);
        var el2 = (axnl * axnl) + (aynl * aynl);
        var pl = am * (1.0 - el2);

        if (pl < 0.0)
        {
            fallo = FalloDePropagacion.SemiladoRectoNegativo;
            return false;
        }

        var rl = am * (1.0 - ecose);
        var rdotl = Math.Sqrt(am) * esine / rl;
        var rvdotl = Math.Sqrt(pl) / rl;
        var betal = Math.Sqrt(1.0 - el2);
        var tempBeta = esine / (1.0 + betal);
        var sinu = am / rl * (sineo1 - aynl - (axnl * tempBeta));
        var cosu = am / rl * (coseo1 - axnl + (aynl * tempBeta));
        var su = Math.Atan2(sinu, cosu);
        var sin2u = (cosu + cosu) * sinu;
        var cos2u = 1.0 - (2.0 * sinu * sinu);

        var temp = 1.0 / pl;
        var temp1 = 0.5 * J2 * temp;
        var temp2 = temp1 * temp;

        // Se recalculan a partir de la inclinacion instantanea: en orbita baja coincide con la
        // de partida, pero escribirlo asi deja el codigo alineado con el de referencia.
        var cosisq = cosip * cosip;
        var con41 = (3.0 * cosisq) - 1.0;
        var x1mth2 = 1.0 - cosisq;
        var x7thm1 = (7.0 * cosisq) - 1.0;

        var mrt = (rl * (1.0 - (1.5 * temp2 * betal * con41))) + (0.5 * temp1 * x1mth2 * cos2u);
        su -= 0.25 * temp2 * x7thm1 * sin2u;
        var xnode = nodep + (1.5 * temp2 * cosip * sin2u);
        var xinc = inclm + (1.5 * temp2 * cosip * sinip * cos2u);
        var mvt = rdotl - (nm * temp1 * x1mth2 * sin2u / Xke);
        var rvdot = rvdotl + (nm * temp1 * ((x1mth2 * cos2u) + (1.5 * con41)) / Xke);

        // ── Vectores de orientacion.
        var sinsu = Math.Sin(su);
        var cossu = Math.Cos(su);
        var snod = Math.Sin(xnode);
        var cnod = Math.Cos(xnode);
        var sini = Math.Sin(xinc);
        var cosi = Math.Cos(xinc);
        var xmx = -snod * cosi;
        var xmy = cnod * cosi;

        var ux = (xmx * sinsu) + (cnod * cossu);
        var uy = (xmy * sinsu) + (snod * cossu);
        var uz = sini * sinsu;
        var vx = (xmx * cossu) - (cnod * sinsu);
        var vy = (xmy * cossu) - (snod * sinsu);
        var vz = sini * cossu;

        posicion = new Vector3(mrt * ux * RadioTerrestreKm, mrt * uy * RadioTerrestreKm, mrt * uz * RadioTerrestreKm);
        velocidad = new Vector3(
            ((mvt * ux) + (rvdot * vx)) * KmPorSegundo,
            ((mvt * uy) + (rvdot * vy)) * KmPorSegundo,
            ((mvt * uz) + (rvdot * vz)) * KmPorSegundo);

        if (mrt < 1.0)
        {
            fallo = FalloDePropagacion.Reentrado;
            return false;
        }

        return true;
    }
}
