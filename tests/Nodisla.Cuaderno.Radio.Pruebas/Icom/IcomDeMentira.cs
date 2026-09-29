using Nodisla.Cuaderno.Radio.Control.Icom;

namespace Nodisla.Cuaderno.Radio.Pruebas.Icom;

/// <summary>
/// Una radio ICOM de mentira que contesta por CI-V como dicen los manuales, con el eco del bus
/// incluido, y un canal en memoria que habla con ella a traves del analizador de tramas de verdad.
/// </summary>
/// <remarks>
/// Apunta todo lo que recibe y, sobre todo, <b>si alguna vez se la ha puesto en antena</b>
/// (<c>1C 00 01</c>, <c>1C 01 02</c>, <c>17</c>, <c>28</c>): las pruebas de los botones
/// comprueban que eso no pasa salvo en el PTT y en el TUNE.
/// </remarks>
public sealed class IcomDeMentira
{
    private readonly object _candado = new();
    private readonly List<byte[]> _recibidas = [];
    private readonly Dictionary<string, byte[]> _valores = [];

    /// <summary>Crea la radio con un perfil.</summary>
    /// <param name="perfil">Modelo simulado.</param>
    public IcomDeMentira(PerfilIcom perfil)
    {
        Perfil = perfil;
        Direccion = perfil.Direccion;

        // Niveles y ajustes de partida, como una radio recien encendida.
        foreach (var sub in new byte[] { 0x01, 0x02, 0x03, 0x06, 0x09, 0x0A, 0x0B, 0x0C, 0x0E, 0x12, 0x15, 0x16, 0x17 })
        {
            _valores[Clave(0x14, sub)] = [0x01, 0x28];
        }

        _valores["11"] = [0x00];
        foreach (var sub in new byte[] { 0x02, 0x22, 0x40, 0x41, 0x46, 0x47, 0x48, 0x50 })
        {
            _valores[Clave(0x16, sub)] = [0x00];
        }

        _valores[Clave(0x16, 0x12)] = [0x02];
        if (perfil.Apf) _valores[Clave(0x16, 0x32)] = [0x00];
        _valores[Clave(0x1A, 0x03)] = [0x31];
        _valores[Clave(0x21, 0x00)] = [0x00, 0x00, 0x00];
        _valores[Clave(0x21, 0x01)] = [0x00];
        if (perfil.Xit) _valores[Clave(0x21, 0x02)] = [0x00];

        Medidores[0x02] = 120;
        Medidores[0x11] = 213;
        Medidores[0x12] = 48;
        Medidores[0x13] = 60;
        Medidores[0x14] = 0;
        Medidores[0x15] = 13;
        Medidores[0x16] = 97;
    }

    /// <summary>Perfil simulado.</summary>
    public PerfilIcom Perfil { get; }

    /// <summary>Direccion CI-V.</summary>
    public byte Direccion { get; set; }

    /// <summary>Devuelve el eco de cada trama, como el bus CI-V.</summary>
    public bool ConEco { get; set; } = true;

    /// <summary>Frecuencias: indice 0 = elegido (o MAIN), 1 = el otro (o SUB).</summary>
    public long[] Hercios { get; } = [14_074_000, 7_074_000];

    /// <summary>Modos por VFO.</summary>
    public byte[] Modos { get; } = [0x01, 0x00];

    /// <summary>Datos por VFO.</summary>
    public bool[] Datos { get; } = [true, false];

    /// <summary>Filtro por VFO.</summary>
    public byte[] Filtros { get; } = [0x01, 0x01];

    /// <summary>El SUB (o el VFO B en los de un receptor) es el elegido.</summary>
    public bool SecundarioElegido { get; set; }

    /// <summary>Split puesto.</summary>
    public bool Split { get; set; }

    /// <summary>La radio esta en antena ahora.</summary>
    public bool EnAntena { get; private set; }

    /// <summary>La radio se ha puesto en antena alguna vez.</summary>
    public bool HaTransmitido { get; private set; }

    /// <summary>Estado del acoplador: 0 fuera, 1 en linea, 2 sintonizando.</summary>
    public byte Acoplador { get; set; } = 1;

    /// <summary>Se ha apagado.</summary>
    public bool Apagada { get; private set; }

    /// <summary>Lecturas de los medidores (<c>15 xx</c>), en crudo.</summary>
    public Dictionary<byte, int> Medidores { get; } = [];

    /// <summary>Memorias: canal a (hercios, modo, nombre).</summary>
    public Dictionary<int, (long Hz, byte Modo, string Nombre)> Memorias { get; } = [];

    /// <summary>Pila de banda (registro 01): codigo a (hercios, modo, filtro, datos).</summary>
    public Dictionary<byte, (long Hz, byte Modo, byte Filtro, bool Datos)> Pila { get; } = [];

    /// <summary>Cuerpos recibidos, en orden.</summary>
    public IReadOnlyList<byte[]> Recibidas
    {
        get
        {
            lock (_candado) return _recibidas.ToList();
        }
    }

    /// <summary>Los cuerpos recibidos en hexadecimal.</summary>
    public IReadOnlyList<string> RecibidasEnHex => Recibidas.Select(c => Hex.De(c)).ToList();

    /// <summary>Olvida lo recibido.</summary>
    public void Olvidar()
    {
        lock (_candado) _recibidas.Clear();
    }

    /// <summary>Valor guardado de un ajuste.</summary>
    /// <param name="orden">Orden.</param>
    /// <param name="sub">Suborden.</param>
    /// <returns>Los datos.</returns>
    public byte[]? Valor(byte orden, byte? sub = null) =>
        _valores.TryGetValue(sub is { } s ? Clave(orden, s) : orden.ToString("X2"), out var v) ? v : null;

    /// <summary>La radio deja de admitir un ajuste (contesta FA).</summary>
    /// <param name="orden">Orden.</param>
    /// <param name="sub">Suborden.</param>
    public void NoAdmitir(byte orden, byte sub) => _valores.Remove(Clave(orden, sub));

    private static string Clave(byte orden, byte sub) => $"{orden:X2}{sub:X2}";

    private int Elegido => SecundarioElegido ? 1 : 0;

    /// <summary>Atiende una trama del ordenador y devuelve lo que la radio pone en el bus.</summary>
    /// <param name="trama">Trama recibida.</param>
    /// <returns>Cuerpos de respuesta (sin el eco).</returns>
    public byte[]? Atender(TramaCiv trama)
    {
        if (trama.Destino != Direccion && trama.Destino != TramaCiv.Difusion) return null;
        var c = trama.Cuerpo;
        lock (_candado) _recibidas.Add(c);
        if (Apagada) return null;

        var bien = new byte[] { TramaCiv.Bien };
        var no = new byte[] { TramaCiv.NoAdmitido };
        var datos = c.Length > 1 ? c[1..] : [];

        switch (c[0])
        {
            case 0x19 when c is [0x19, 0x00]:
                return [0x19, 0x00, Direccion];
            case 0x03:
                return [0x03, .. BcdCiv.Frecuencia(Hercios[Elegido])];
            case 0x04:
                return [0x04, Modos[Elegido], Filtros[Elegido]];
            case 0x05:
                Hercios[Elegido] = BcdCiv.Hercios(datos) ?? Hercios[Elegido];
                return bien;
            case 0x06:
                Modos[Elegido] = datos[0];
                if (datos.Length > 1) Filtros[Elegido] = datos[1];
                return bien;
            case 0x07:
                if (datos is [0xD2]) return Perfil.Reparto == RepartoDeVfos.PrincipalYSecundario ? [0x07, 0xD2, (byte)Elegido] : no;
                if (datos is [0x00] or [0xD0]) SecundarioElegido = false;
                if (datos is [0x01] or [0xD1]) SecundarioElegido = true;
                if (datos is [0xB0]) (Hercios[0], Hercios[1]) = (Hercios[1], Hercios[0]);
                if (datos is [0xA0]) Hercios[1 - Elegido] = Hercios[Elegido];
                return bien;
            case 0x08:
            case 0x0A:
                return bien;
            case 0x09 or 0x0B:
                return bien;
            case 0x0F:
                if (datos.Length == 0) return [0x0F, (byte)(Split ? 0x01 : 0x00)];
                Split = datos[0] == 0x01;
                return bien;
            case 0x10:
                return [0x10, 0x00];
            case 0x11:
                return LeerOEscribir("11", [0x11], datos);
            case 0x14 or 0x16 or 0x21 when datos.Length >= 1:
                var clave = Clave(c[0], datos[0]);
                if (!_valores.ContainsKey(clave)) return no;
                return LeerOEscribir(clave, c[..2], datos[1..]);
            case 0x15 when datos.Length == 1 && Medidores.TryGetValue(datos[0], out var crudo):
                return [0x15, datos[0], .. BcdCiv.Numero(crudo, 2)];
            case 0x1A when datos is [0x06]:
                return [0x1A, 0x06, (byte)(Datos[Elegido] ? 0x01 : 0x00), 0x01];
            case 0x1A when datos is [0x06, var d, ..]:
                Datos[Elegido] = d == 0x01;
                return bien;
            case 0x1A when datos is [0x03, ..]:
                return LeerOEscribir(Clave(0x1A, 0x03), [0x1A, 0x03], datos[1..]);
            case 0x1A when datos is [0x01, var banda, 0x01]:
                if (!Pila.TryGetValue(banda, out var p)) return no;
                return [0x1A, 0x01, banda, 0x01, .. BcdCiv.Frecuencia(p.Hz), p.Modo, p.Filtro, (byte)(p.Datos ? 0x10 : 0x00), 0x00, 0x08, 0x85, 0x00, 0x08, 0x85];
            case 0x1A when datos is [0x00, _, _]:
                var canal = BcdCiv.Numero(datos.AsSpan(1, 2)) ?? 0;
                if (!Memorias.TryGetValue(canal, out var m)) return [0x1A, 0x00, datos[1], datos[2], 0xFF];
                var nombre = System.Text.Encoding.ASCII.GetBytes(m.Nombre.PadRight(10));
                return [0x1A, 0x00, datos[1], datos[2], 0x00, .. BcdCiv.Frecuencia(m.Hz), m.Modo, 0x01, 0x00, 0x00, 0x08, 0x85, 0x00, 0x08, 0x85,
                    .. BcdCiv.Frecuencia(m.Hz), m.Modo, 0x01, 0x00, 0x00, 0x08, 0x85, 0x00, 0x08, 0x85, .. nombre];
            case 0x1C when datos is [0x00]:
                return [0x1C, 0x00, (byte)(EnAntena ? 0x01 : 0x00)];
            case 0x1C when datos is [0x00, var tx]:
                EnAntena = tx == 0x01;
                HaTransmitido |= EnAntena;
                return bien;
            case 0x1C when datos is [0x01] && Perfil.Sintonizador:
                return [0x1C, 0x01, Acoplador];
            case 0x1C when datos is [0x01, var a] && Perfil.Sintonizador:
                Acoplador = a;
                if (a == 0x02) HaTransmitido = true;
                return bien;
            case 0x25 when datos.Length >= 1:
                var i = VfoDelSelector(datos[0]);
                if (datos.Length == 1) return [0x25, datos[0], .. BcdCiv.Frecuencia(Hercios[i])];
                Hercios[i] = BcdCiv.Hercios(datos.AsSpan(1)) ?? Hercios[i];
                return bien;
            case 0x26 when datos.Length >= 1:
                var j = VfoDelSelector(datos[0]);
                if (datos.Length == 1) return [0x26, datos[0], Modos[j], (byte)(Datos[j] ? 0x01 : 0x00), Filtros[j]];
                Modos[j] = datos[1];
                if (datos.Length > 2) Datos[j] = datos[2] == 0x01;
                if (datos.Length > 3) Filtros[j] = datos[3];
                return bien;
            case 0x18 when datos is [0x00]:
                Apagada = true;
                return bien;
            case 0x17 or 0x28:
                HaTransmitido = true;
                return bien;
            default:
                return no;
        }
    }

    // 25/26: en MAIN/SUB 00 = MAIN, 01 = SUB; en A/B 00 = el elegido, 01 = el otro.
    private int VfoDelSelector(byte selector) =>
        Perfil.Reparto == RepartoDeVfos.PrincipalYSecundario ? selector : (selector == 0x00 ? Elegido : 1 - Elegido);

    private byte[] LeerOEscribir(string clave, byte[] prefijo, byte[] datos)
    {
        if (datos.Length == 0)
        {
            return _valores.TryGetValue(clave, out var v) ? [.. prefijo, .. v] : [TramaCiv.NoAdmitido];
        }

        _valores[clave] = datos;
        return [TramaCiv.Bien];
    }
}

/// <summary>Canal CI-V en memoria: lo que se escribe va a la radio de mentira y lo que contesta vuelve por el analizador.</summary>
public sealed class CanalCivDeMentira : CanalCiv
{
    private bool _abierto;

    /// <summary>Crea el canal.</summary>
    /// <param name="radio">Radio al otro lado.</param>
    /// <param name="direccion">Direccion a la que se habla (nula = la de la radio).</param>
    public CanalCivDeMentira(IcomDeMentira radio, byte? direccion = null)
        : base(direccion ?? radio.Direccion, TimeSpan.FromMilliseconds(300), null) => Radio = radio;

    /// <summary>La radio.</summary>
    public IcomDeMentira Radio { get; }

    /// <summary>Tramas completas escritas, en bruto (para comprobar el despertar).</summary>
    public List<byte[]> Escrito { get; } = [];

    /// <inheritdoc />
    public override bool Abierto => _abierto;

    /// <inheritdoc />
    public override string Descripcion => "canal de mentira";

    /// <summary>La radio manda un aviso transceive (a la direccion 00).</summary>
    /// <param name="cuerpo">Cuerpo del aviso.</param>
    public void Transceive(byte[] cuerpo) =>
        AlRecibir(new TramaCiv(TramaCiv.Difusion, Radio.Direccion, cuerpo).ABytes());

    /// <inheritdoc />
    protected override Task AbrirNucleoAsync(CancellationToken ct)
    {
        _abierto = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override Task EscribirNucleoAsync(byte[] bytes, CancellationToken ct)
    {
        EscribirNucleoSincrono(bytes);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override void EscribirNucleoSincrono(byte[] bytes)
    {
        lock (Escrito) Escrito.Add(bytes);
        if (Radio.ConEco) AlRecibirEnTrozos(bytes);

        foreach (var trama in new AnalizadorDeTramasCiv().Anadir(bytes))
        {
            var respuesta = Radio.Atender(trama);
            if (respuesta is not null)
            {
                AlRecibirEnTrozos(new TramaCiv(trama.Origen, Radio.Direccion, respuesta).ABytes());
            }
        }
    }

    // En trozos de 3 bytes: el puerto serie entrega lo que hay, no tramas enteras.
    private void AlRecibirEnTrozos(byte[] bytes)
    {
        for (var i = 0; i < bytes.Length; i += 3)
        {
            AlRecibir(bytes.AsSpan(i, Math.Min(3, bytes.Length - i)));
        }
    }

    /// <inheritdoc />
    protected override void CerrarNucleo() => _abierto = false;
}
