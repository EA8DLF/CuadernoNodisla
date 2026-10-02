using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Ui.Digital;

/// <summary>
/// Lo que la pantalla necesita saber de cada modo del modem: como se llama, cuanto dura su
/// ventana y como se apunta en ADIF.
/// </summary>
/// <remarks>
/// Vive en la interfaz y no en el modulo de los modos porque es informacion de <i>presentacion
/// y de cuaderno</i>, no de señal: el periodo se usa para la paridad de la secuencia y para el
/// texto del selector, y el modo ADIF para que el contacto entre bien en los diplomas. Los
/// modos con varios periodos (FST4, Q65) llevan aqui el que se usa en HF por omision.
/// </remarks>
public static class DescripcionDelModo
{
    /// <summary>Nombre corto del modo, tal y como se ve en el selector.</summary>
    public static string Nombre(ModoDelModem modo) => modo switch
    {
        ModoDelModem.Ft8 => "FT8",
        ModoDelModem.Ft4 => "FT4",
        ModoDelModem.Wspr => "WSPR",
        ModoDelModem.Jt65 => "JT65",
        ModoDelModem.Jt9 => "JT9",
        ModoDelModem.Q65 => "Q65",
        ModoDelModem.Msk144 => "MSK144",
        ModoDelModem.Fst4 => "FST4",
        ModoDelModem.Fst4w => "FST4W",
        _ => modo.ToString().ToUpperInvariant(),
    };

    /// <summary>Lo que dura una ventana del modo.</summary>
    public static TimeSpan Periodo(ModoDelModem modo) => modo switch
    {
        ModoDelModem.Ft8 => TimeSpan.FromSeconds(15),
        ModoDelModem.Ft4 => TimeSpan.FromSeconds(7.5),
        ModoDelModem.Wspr => TimeSpan.FromSeconds(120),
        ModoDelModem.Jt65 => TimeSpan.FromSeconds(60),
        ModoDelModem.Jt9 => TimeSpan.FromSeconds(60),
        ModoDelModem.Q65 => TimeSpan.FromSeconds(60),
        ModoDelModem.Msk144 => TimeSpan.FromSeconds(15),
        ModoDelModem.Fst4 => TimeSpan.FromSeconds(60),
        ModoDelModem.Fst4w => TimeSpan.FromSeconds(120),
        _ => TimeSpan.FromSeconds(15),
    };

    /// <summary>El periodo en palabras, para el selector.</summary>
    public static string PeriodoTexto(ModoDelModem modo)
    {
        var periodo = Periodo(modo);
        return periodo.TotalSeconds < 60
            ? string.Create(Textos.Cultura, $"{periodo.TotalSeconds:0.#} s")
            : string.Create(Textos.Cultura, $"{periodo.TotalMinutes:0.#} min");
    }

    /// <summary>El modo es una baliza: no se hacen contactos con el.</summary>
    public static bool EsBaliza(ModoDelModem modo) => modo is ModoDelModem.Wspr or ModoDelModem.Fst4w;

    /// <summary>
    /// Como se apunta el contacto en ADIF.
    /// </summary>
    /// <remarks>
    /// <b>FT8, JT65, JT9 y MSK144 son modos principales</b>; <b>FT4, Q65, FST4 y FST4W son
    /// submodos de MFSK</b>. Ponerlo al reves es el error tipico y deja el contacto fuera de
    /// los diplomas que cuentan por modo.
    /// </remarks>
    public static Modo ModoAdif(ModoDelModem modo) => modo switch
    {
        ModoDelModem.Ft8 => Modo.Parse("FT8"),
        ModoDelModem.Ft4 => Modo.Parse("MFSK", "FT4"),
        ModoDelModem.Wspr => Modo.Parse("WSPR"),
        ModoDelModem.Jt65 => Modo.Parse("JT65"),
        ModoDelModem.Jt9 => Modo.Parse("JT9"),
        ModoDelModem.Q65 => Modo.Parse("MFSK", "Q65"),
        ModoDelModem.Msk144 => Modo.Parse("MSK144"),
        ModoDelModem.Fst4 => Modo.Parse("MFSK", "FST4"),
        ModoDelModem.Fst4w => Modo.Parse("MFSK", "FST4W"),
        _ => Modo.Parse("FT8"),
    };
}
