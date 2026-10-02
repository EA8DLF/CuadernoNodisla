namespace Nodisla.Cuaderno.Audio.Procesado;

/// <summary>
/// Filtro de segundo orden con las formulas del «Audio EQ Cookbook» de Robert Bristow-Johnson
/// (dominio publico de hecho, publicadas para que se usen).
/// </summary>
public sealed class FiltroBicuadratico
{
    private double _b0 = 1, _b1, _b2, _a1, _a2;
    private double _z1, _z2;

    /// <summary>Pasa todo tal cual.</summary>
    public bool EsNeutro { get; private set; } = true;

    /// <summary>Paso alto de Butterworth (Q 0,707).</summary>
    public void PasoAlto(double frecuencia, double corte)
    {
        var w0 = 2 * Math.PI * corte / frecuencia;
        var alfa = Math.Sin(w0) / (2 * Math.Sqrt(0.5));
        var c = Math.Cos(w0);
        Poner((1 + c) / 2, -(1 + c), (1 + c) / 2, 1 + alfa, -2 * c, 1 - alfa);
    }

    /// <summary>Estanteria de graves.</summary>
    public void EstanteriaBaja(double frecuencia, double corte, double db)
    {
        var a = Math.Pow(10, db / 40);
        var w0 = 2 * Math.PI * corte / frecuencia;
        var c = Math.Cos(w0);
        var alfa = Math.Sin(w0) / 2 * Math.Sqrt(2);
        var raiz = 2 * Math.Sqrt(a) * alfa;
        Poner(
            a * ((a + 1) - ((a - 1) * c) + raiz),
            2 * a * ((a - 1) - ((a + 1) * c)),
            a * ((a + 1) - ((a - 1) * c) - raiz),
            (a + 1) + ((a - 1) * c) + raiz,
            -2 * ((a - 1) + ((a + 1) * c)),
            (a + 1) + ((a - 1) * c) - raiz);
    }

    /// <summary>Estanteria de agudos.</summary>
    public void EstanteriaAlta(double frecuencia, double corte, double db)
    {
        var a = Math.Pow(10, db / 40);
        var w0 = 2 * Math.PI * corte / frecuencia;
        var c = Math.Cos(w0);
        var alfa = Math.Sin(w0) / 2 * Math.Sqrt(2);
        var raiz = 2 * Math.Sqrt(a) * alfa;
        Poner(
            a * ((a + 1) + ((a - 1) * c) + raiz),
            -2 * a * ((a - 1) + ((a + 1) * c)),
            a * ((a + 1) + ((a - 1) * c) - raiz),
            (a + 1) - ((a - 1) * c) + raiz,
            2 * ((a - 1) - ((a + 1) * c)),
            (a + 1) - ((a - 1) * c) - raiz);
    }

    /// <summary>Campana.</summary>
    public void Campana(double frecuencia, double centro, double q, double db)
    {
        var a = Math.Pow(10, db / 40);
        var w0 = 2 * Math.PI * centro / frecuencia;
        var alfa = Math.Sin(w0) / (2 * q);
        var c = Math.Cos(w0);
        Poner(1 + (alfa * a), -2 * c, 1 - (alfa * a), 1 + (alfa / a), -2 * c, 1 - (alfa / a));
    }

    /// <summary>Deja el filtro neutro.</summary>
    public void Neutro()
    {
        _b0 = 1;
        _b1 = _b2 = _a1 = _a2 = 0;
        EsNeutro = true;
    }

    /// <summary>Olvida el estado.</summary>
    public void Reiniciar() => _z1 = _z2 = 0;

    /// <summary>Filtra en el sitio (forma directa II transpuesta).</summary>
    public void Procesar(Span<float> muestras)
    {
        if (EsNeutro) return;
        for (var i = 0; i < muestras.Length; i++)
        {
            double x = muestras[i];
            var y = (_b0 * x) + _z1;
            _z1 = (_b1 * x) - (_a1 * y) + _z2;
            _z2 = (_b2 * x) - (_a2 * y);
            muestras[i] = (float)y;
        }
    }

    private void Poner(double b0, double b1, double b2, double a0, double a1, double a2)
    {
        _b0 = b0 / a0;
        _b1 = b1 / a0;
        _b2 = b2 / a0;
        _a1 = a1 / a0;
        _a2 = a2 / a0;
        EsNeutro = false;
    }
}
