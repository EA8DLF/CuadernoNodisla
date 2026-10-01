using Nodisla.Cuaderno.Aplicacion.Puertos;

namespace Nodisla.Cuaderno.Radio.Control.Icom;

/// <summary>
/// Un mando CI-V: la orden que lo lee (orden y suborden sin datos), como se escribe y como se
/// pasa del numero del equipo al del operador.
/// </summary>
/// <param name="Mando">Mando del puerto de aplicacion.</param>
/// <param name="Orden">Orden y suborden: leer es mandarla sola, escribir es mandarla con datos.</param>
/// <param name="Rango">Rango para el operador, en unidades de verdad.</param>
/// <param name="Leer">Datos de la respuesta (tras la orden) a valor; nulo si no se entienden.</param>
/// <param name="Escribir">Valor (ya ajustado al rango) a datos.</param>
public sealed record MandoIcom(
    MandoDeEquipo Mando,
    byte[] Orden,
    RangoDeMando Rango,
    Func<byte[], double?> Leer,
    Func<double, byte[]> Escribir);

/// <summary>
/// Los mandos CI-V de cada ICOM.
/// </summary>
/// <remarks>
/// <para>
/// Ordenes comunes a los seis manuales: niveles <c>14 xx</c> (dos bytes BCD, <c>00 00</c> a
/// <c>02 55</c>), interruptores <c>16 xx</c> (un byte), atenuador <c>11</c>, split <c>0F</c>, RIT
/// <c>21 00/01</c>, XIT <c>21 02</c>, ancho de filtro <c>1A 03</c> y acoplador <c>1C 01</c>. Lo que
/// cambia de un modelo a otro (posiciones del atenuador, preamplificador, APF, XIT, acoplador)
/// sale del <see cref="PerfilIcom"/>.
/// </para>
/// <para>
/// Ademas, al conectar se pregunta cada mando y el que contesta <c>FA</c> se queda fuera: el
/// equipo dice lo que sabe hacer. Ojo: un <c>FA</c> puede depender del modo (el manual del
/// FT-710 ya enseño que «no admitido» no siempre es «no existe»).
/// </para>
/// </remarks>
public static class MandosIcom
{
    /// <summary>Los mandos de un perfil.</summary>
    /// <param name="perfil">Perfil del modelo.</param>
    /// <returns>Sus mandos.</returns>
    public static IReadOnlyList<MandoIcom> De(PerfilIcom perfil)
    {
        ArgumentNullException.ThrowIfNull(perfil);
        List<MandoIcom> mandos =
        [
            Porcentaje(MandoDeEquipo.Volumen, 0x01),
            Porcentaje(MandoDeEquipo.GananciaRf, 0x02),
            Porcentaje(MandoDeEquipo.Silenciador, 0x03),
            Porcentaje(MandoDeEquipo.NivelReductorDeRuido, 0x06),
            Nivel(MandoDeEquipo.TonoCw, 0x09, new RangoDeMando(MandoDeEquipo.TonoCw, 300, 900, 5, "Hz"),
                crudo => 300 + Math.Round(crudo * 600d / 255d / 5d) * 5, hz => (int)Math.Round((hz - 300) * 255d / 600d)),
            Nivel(MandoDeEquipo.Potencia, 0x0A, new RangoDeMando(MandoDeEquipo.Potencia, 0, perfil.PotenciaMaxima, 1, "W"),
                crudo => Math.Round(crudo * perfil.PotenciaMaxima / 255d), w => (int)Math.Round(w * 255d / perfil.PotenciaMaxima)),
            Porcentaje(MandoDeEquipo.GananciaMicrofono, 0x0B),
            Nivel(MandoDeEquipo.VelocidadKeyer, 0x0C, new RangoDeMando(MandoDeEquipo.VelocidadKeyer, 6, 48, 1, "ppm"),
                crudo => Math.Round(6 + (crudo * 42d / 255d)), ppm => (int)Math.Round((ppm - 6) * 255d / 42d)),
            Nivel(MandoDeEquipo.Compresor, 0x0E, new RangoDeMando(MandoDeEquipo.Compresor, 0, 10, 1),
                crudo => Math.Round(crudo / 25.5), v => (int)Math.Round(v * 25.5)),
            Porcentaje(MandoDeEquipo.NivelSupresorDeRuido, 0x12),
            Porcentaje(MandoDeEquipo.Monitor, 0x15),
            Porcentaje(MandoDeEquipo.GananciaVox, 0x16),
            Porcentaje(MandoDeEquipo.AntiVox, 0x17),

            Posiciones(MandoDeEquipo.Atenuador, [0x11], "dB",
                perfil.Atenuadores.Select(db => (byte)BcdCiv.Numero(db, 1)[0]).ToArray(),
                perfil.Atenuadores.Select(db => db == 0 ? "Apagado" : $"{db} dB").ToArray()),
            Posiciones(MandoDeEquipo.Preamplificador, [0x16, 0x02], null,
                Enumerable.Range(0, perfil.Preamplificadores.Count).Select(i => (byte)i).ToArray(),
                perfil.Preamplificadores.ToArray()),
            Posiciones(MandoDeEquipo.Agc, [0x16, 0x12], null, [0x01, 0x02, 0x03], ["Rápido", "Medio", "Lento"]),
            Interruptor(MandoDeEquipo.SupresorDeRuido, [0x16, 0x22]),
            Interruptor(MandoDeEquipo.ReductorDeRuido, [0x16, 0x40]),
            Interruptor(MandoDeEquipo.MuescaAutomatica, [0x16, 0x41]),
            Interruptor(MandoDeEquipo.Vox, [0x16, 0x46]),
            Posiciones(MandoDeEquipo.BreakIn, [0x16, 0x47], null, [0x00, 0x01, 0x02], ["Apagado", "Semi", "Total"]),
            Interruptor(MandoDeEquipo.MuescaManual, [0x16, 0x48]),
            Interruptor(MandoDeEquipo.Bloqueo, [0x16, 0x50]),

            new MandoIcom(
                MandoDeEquipo.AnchoDeFiltro,
                [0x1A, 0x03],
                new RangoDeMando(MandoDeEquipo.AnchoDeFiltro, 0, 49, 1, "índice"),
                datos => datos.Length >= 1 ? BcdCiv.Numero(datos.AsSpan(0, 1)) : null,
                v => BcdCiv.Numero((int)v, 1)),

            Interruptor(MandoDeEquipo.Split, [0x0F]),
            Interruptor(MandoDeEquipo.Rit, [0x21, 0x01]),
            new MandoIcom(
                MandoDeEquipo.DesplazamientoRit,
                [0x21, 0x00],
                new RangoDeMando(MandoDeEquipo.DesplazamientoRit, -9999, 9999, 1, "Hz"),
                LeerDesplazamiento,
                EscribirDesplazamiento),
        ];

        if (perfil.Xit)
        {
            mandos.Add(Interruptor(MandoDeEquipo.Xit, [0x21, 0x02]));

            // RIT y XIT comparten el mismo desplazamiento en las ICOM (21 00).
            mandos.Add(new MandoIcom(
                MandoDeEquipo.DesplazamientoXit,
                [0x21, 0x00],
                new RangoDeMando(MandoDeEquipo.DesplazamientoXit, -9999, 9999, 1, "Hz"),
                LeerDesplazamiento,
                EscribirDesplazamiento));
        }

        if (perfil.Apf)
        {
            mandos.Add(Posiciones(MandoDeEquipo.Apf, [0x16, 0x32], null, [0x00, 0x01, 0x02, 0x03], ["Apagado", "Ancho", "Medio", "Estrecho"]));
        }

        if (perfil.Sintonizador)
        {
            // Posicion 2 = sintonizar: emite portadora. Solo dentro de una transmision del vigilante.
            var sintonizador = Posiciones(
                MandoDeEquipo.Sintonizador, [0x1C, 0x01], null, [0x00, 0x01, 0x02], ["Fuera", "En línea", "Sintonizar"]);
            mandos.Add(sintonizador with { Rango = sintonizador.Rango with { TransmiteAlAccionar = true } });
        }

        return mandos;
    }

    /// <summary>Lee el desplazamiento del RIT: 2 bytes BCD invertidos (10/1 Hz, 1000/100 Hz) y el signo.</summary>
    /// <param name="datos">Datos tras <c>21 00</c>.</param>
    /// <returns>Hercios con signo.</returns>
    public static double? LeerDesplazamiento(byte[] datos)
    {
        if (datos.Length < 3) return null;
        var valor = BcdCiv.Hercios(datos.AsSpan(0, 2));
        if (valor is null) return null;
        return datos[2] == 0x01 ? -valor.Value : valor.Value;
    }

    /// <summary>Escribe el desplazamiento del RIT.</summary>
    /// <param name="hz">Hercios con signo.</param>
    /// <returns>Los tres bytes.</returns>
    public static byte[] EscribirDesplazamiento(double hz)
    {
        var entero = (long)Math.Round(hz);
        var bcd = BcdCiv.Frecuencia(Math.Min(9999, Math.Abs(entero)), 2);
        return [bcd[0], bcd[1], (byte)(entero < 0 ? 0x01 : 0x00)];
    }

    private static MandoIcom Porcentaje(MandoDeEquipo mando, byte sub) =>
        Nivel(mando, sub, new RangoDeMando(mando, 0, 100, 1, "%"),
            crudo => Math.Round(crudo * 100d / 255d), pc => (int)Math.Round(pc * 255d / 100d));

    private static MandoIcom Nivel(MandoDeEquipo mando, byte sub, RangoDeMando rango, Func<int, double> delEquipo, Func<double, int> alEquipo) =>
        new(
            mando,
            [0x14, sub],
            rango,
            datos => datos.Length >= 2 && BcdCiv.Numero(datos.AsSpan(0, 2)) is { } crudo ? delEquipo(crudo) : null,
            v => BcdCiv.Numero(Math.Clamp(alEquipo(v), 0, 255), 2));

    private static MandoIcom Interruptor(MandoDeEquipo mando, byte[] orden) =>
        new(
            mando,
            orden,
            new RangoDeMando(mando, 0, 1, 1),
            datos => datos.Length >= 1 ? (datos[0] == 0x01 ? 1 : 0) : null,
            v => [(byte)(v >= 0.5 ? 0x01 : 0x00)]);

    private static MandoIcom Posiciones(MandoDeEquipo mando, byte[] orden, string? unidad, byte[] codigos, string[] etiquetas) =>
        new(
            mando,
            orden,
            new RangoDeMando(mando, 0, codigos.Length - 1, 1, unidad, etiquetas),
            datos =>
            {
                if (datos.Length < 1) return null;
                var i = Array.IndexOf(codigos, datos[0]);
                return i >= 0 ? i : null;
            },
            v => [codigos[Math.Clamp((int)Math.Round(v), 0, codigos.Length - 1)]]);
}
