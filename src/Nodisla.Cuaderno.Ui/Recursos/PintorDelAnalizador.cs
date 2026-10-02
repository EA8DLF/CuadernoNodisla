using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Ui.Recursos;

/// <summary>Un pico marcado en la traza.</summary>
/// <param name="Posicion">Donde cae, de 0 (izquierda) a 1 (derecha).</param>
/// <param name="SobreElSuelo">Cuanto asoma sobre el suelo de ruido, en puntos de la escala de la radio.</param>
/// <param name="Arriba">Donde queda la punta de su marca, de 0 (arriba de la traza) a 1 (abajo).</param>
public readonly record struct PicoDeLaTraza(double Posicion, double SobreElSuelo, double Arriba = 0);

/// <summary>
/// Pinta el analizador del equipo como lo pinta la pantalla del FT-710: la traza arriba,
/// blanca azulada sobre negro con rejilla gris, y la cascada debajo.
/// </summary>
/// <remarks>
/// <para>
/// Trabaja con puntos en memoria (formato Bgra32, un <c>int</c> por punto) y no sabe nada de
/// WPF: el modelo de vista los copia a sus imagenes. Asi se prueba sin hilo de interfaz.
/// </para>
/// <para>
/// <b>Suelo de ruido y AGC de la cascada.</b> El nivel que manda la radio (0-255, relativo)
/// depende de la banda, del span, del preamplificador y del atenuador: con una escala fija una
/// banda tranquila saldria negra entera. <see cref="SueloDeRuido"/> sigue el ruido y la cascada
/// pone el negro un poco por debajo de el y el color mas vivo <see cref="Contraste"/> puntos
/// mas arriba (idea de Thetis: umbral bajo = suelo − desplazamiento). El suelo se recuerda por
/// banda y span: al volver a una banda ya vista, la cascada sale bien desde la primera fila.
/// </para>
/// <para>
/// <b>Al resintonizar en CENTER</b> la historia de la cascada se corre los puntos que
/// corresponden al salto, con el resto fraccional guardado para que muchos saltos pequeños no
/// se pierdan (idea de Thetis, <c>display.cs:prepareWaterfallBitmapShift</c>). Asi una señal
/// sigue en su sitio en vez de emborronarse en diagonal. Con un salto mayor que la pantalla
/// (cambio de banda) la cascada empieza limpia.
/// </para>
/// <para>
/// No pide memoria nueva entre pasadas: los bufferes son fijos (11 pasadas por segundo, 850
/// puntos), para no dar trabajo al recolector.
/// </para>
/// </remarks>
public sealed class PintorDelAnalizador
{
    /// <summary>Puntos de ancho: uno por cada punto de la traza del equipo.</summary>
    public const int Ancho = 850;

    /// <summary>Alto de la traza, en puntos.</summary>
    public const int AltoDeTraza = 110;

    /// <summary>Filas de cascada que se guardan.</summary>
    public const int AltoDeCascada = 120;

    /// <summary>Alto de la vista 3DSS: ocupa lo de la traza y la cascada juntas.</summary>
    public const int AltoTresD = AltoDeTraza + AltoDeCascada;

    /// <summary>Pasadas que se ven en fila en la vista 3DSS.</summary>
    public const int PasadasTresD = 36;

    /// <summary>Picos que se marcan como mucho.</summary>
    public const int PicosMaximos = 5;

    /// <summary>
    /// Lo que tiene que asomar un pico sobre el suelo para marcarlo. En las tramas reales los
    /// dientes del ruido llegan a unos +19 sobre el suelo (percentil 95): por debajo de 20 se
    /// marcaria ruido.
    /// </summary>
    public const double AlturaMinimaDePico = 20;

    /// <summary>Puntos de traza que tiene que haber entre dos picos marcados, para que sus cifras no se pisen.</summary>
    public const int SeparacionDePicos = 24;

    /// <summary>Lo que tiene que bajar la traza tras un pico para buscar el siguiente (histéresis).</summary>
    public const double HisteresisDePico = 8;

    /// <summary>Contraste de fabrica: puntos entre el negro y el color mas vivo.</summary>
    public const int ContrasteDeFabrica = 60;

    /// <summary>Negro de fabrica sin AGC (nivel de la radio).</summary>
    public const int NivelBajoDeFabrica = 60;

    /// <summary>
    /// Filas de cascada por cada pasada que llega, segun SPEED (SS00). La radio manda unas once
    /// tramas por segundo con cualquier SPEED (medido el 29-09-2026), asi que la velocidad de la
    /// cascada se hace aqui: SLOW1 una fila cada cuatro pasadas … FAST3 tres por pasada; STOP
    /// congela la pantalla, como en la radio.
    /// </summary>
    public static IReadOnlyList<double> FilasPorPasada { get; } = [0.25, 0.5, 1, 2, 3, 0];

    private const int Fondo = unchecked((int)0xFF05070A);
    private const int RejillaColor = unchecked((int)0xFF1C242E);
    private const int Relleno = unchecked((int)0xFF15356A);
    private const int Linea = unchecked((int)0xFFDDEEFF);
    private const int ColorDePico = unchecked((int)0xFFFFC233);

    /// <summary>Margen bajo el suelo en el que empieza el negro de la cascada.</summary>
    private const double NegroBajoElSuelo = 6;

    private readonly SueloDeRuido _suelo = new();
    private readonly Dictionary<string, double> _sueloPorBanda = new(StringComparer.Ordinal);
    private readonly double[] _media = new double[Ancho];
    private readonly byte[] _remuestreados = new byte[Ancho];
    private readonly byte[] _suavizada = new byte[Ancho];
    private readonly int[] _fila = new int[Ancho];
    private readonly byte[][] _historia = new byte[AltoDeCascada][];
    private readonly List<PicoDeLaTraza> _picos = new(PicosMaximos);
    private readonly List<(int X, double Alto)> _candidatos = new(32);
    private int[] _paleta = PaletasDelAnalizador.Tabla(PaletaDelAnalizador.Radio);
    private PaletaDelAnalizador _nombreDePaleta = PaletaDelAnalizador.Radio;
    private int _filasDeHistoria;
    private double _filasPendientes;
    private bool _hayMedia;
    private double _inicioHz = double.NaN;
    private double _anchoHz = double.NaN;
    private double _restoDeDesplazamiento;
    private string _clave = string.Empty;

    /// <summary>Monta el pintor con la pantalla vacia.</summary>
    public PintorDelAnalizador()
    {
        Array.Fill(Traza, Fondo);
        Array.Fill(Cascada, _paleta[0]);
        Array.Fill(VistaTresD, Fondo);
    }

    /// <summary>SPEED de la radio (0 SLOW1 … 4 FAST3, 5 STOP).</summary>
    public int Velocidad { get; set; } = 2;

    /// <summary>Se pinta tambien la vista 3DSS.</summary>
    public bool TresD { get; set; }

    /// <summary>Colores de la cascada.</summary>
    public PaletaDelAnalizador Paleta
    {
        get => _nombreDePaleta;
        set
        {
            if (value == _nombreDePaleta) return;
            _nombreDePaleta = value;
            _paleta = PaletasDelAnalizador.Tabla(value);
        }
    }

    /// <summary>El negro de la cascada sigue al suelo de ruido (AGC). Si no, va en <see cref="NivelBajo"/>.</summary>
    public bool SueloAutomatico { get; set; } = true;

    /// <summary>Sin AGC, el nivel de la radio (0-255) que se pinta en negro.</summary>
    public int NivelBajo { get; set; } = NivelBajoDeFabrica;

    /// <summary>Puntos de la escala de la radio entre el negro y el color mas vivo (20-150).</summary>
    public int Contraste { get; set; } = ContrasteDeFabrica;

    /// <summary>Sube (positivo) o baja el negro, en puntos: aclara u oscurece el ruido.</summary>
    public int Brillo { get; set; }

    /// <summary>Se buscan y marcan los picos de la traza.</summary>
    public bool MarcarPicos { get; set; }

    /// <summary>Al resintonizar en CENTER, la cascada se corre con la frecuencia.</summary>
    public bool Desplazar { get; set; } = true;

    /// <summary>Puntos de la vista 3DSS, fila a fila de arriba abajo.</summary>
    public int[] VistaTresD { get; } = new int[Ancho * AltoTresD];

    /// <summary>Filas que se han añadido a la cascada desde que se monto el pintor.</summary>
    public long FilasAnadidas { get; private set; }

    /// <summary>Puntos de la traza, fila a fila de arriba abajo.</summary>
    public int[] Traza { get; } = new int[Ancho * AltoDeTraza];

    /// <summary>Puntos de la cascada; la fila mas nueva arriba.</summary>
    public int[] Cascada { get; } = new int[Ancho * AltoDeCascada];

    /// <summary>Suelo de ruido que se esta usando (0–255).</summary>
    public double Suelo => _suelo.Valor;

    /// <summary>Los picos de la ultima pasada, de mas alto a mas bajo (vacio sin <see cref="MarcarPicos"/>).</summary>
    public IReadOnlyList<PicoDeLaTraza> Picos => _picos;

    /// <summary>Puntos que se corrio la cascada en la ultima pasada (0 si no hubo salto).</summary>
    public int UltimoDesplazamiento { get; private set; }

    /// <summary>Pinta una traza nueva y le añade filas a la cascada (sin saber su frecuencia).</summary>
    /// <param name="niveles">Niveles de 0 a 255, de frecuencia baja a alta.</param>
    public void Pintar(ReadOnlySpan<byte> niveles) => Pintar(niveles, double.NaN, double.NaN, Environment.TickCount64);

    /// <summary>Pinta una traza nueva y le añade filas a la cascada.</summary>
    /// <param name="niveles">Niveles de 0 a 255, de frecuencia baja a alta.</param>
    /// <param name="inicioHz">Frecuencia del primer punto; NaN si no se sabe.</param>
    /// <param name="finHz">Frecuencia del ultimo punto; NaN si no se sabe.</param>
    /// <param name="ahoraMs">Reloj en milisegundos (para el suelo de ruido).</param>
    public void Pintar(ReadOnlySpan<byte> niveles, double inicioHz, double finHz, long ahoraMs)
    {
        if (niveles.IsEmpty) return;
        var filas = FilasPorPasada[Math.Clamp(Velocidad, 0, FilasPorPasada.Count - 1)];
        if (filas <= 0) return; // STOP: la pantalla se queda quieta.

        for (var x = 0; x < Ancho; x++) _remuestreados[x] = niveles[(int)((long)x * niveles.Length / Ancho)];

        SeguirLaFrecuencia(inicioHz, finHz, ahoraMs);
        _suelo.Seguir(_remuestreados, ahoraMs);
        if (_clave.Length > 0) _sueloPorBanda[_clave] = _suelo.Valor;

        // La traza se suaviza un poco entre pasadas (la radio tambien promedia la suya); la
        // cascada no, para no emborronar lo que dura una sola pasada.
        for (var x = 0; x < Ancho; x++)
        {
            _media[x] = _hayMedia ? (_media[x] * 0.5) + (_remuestreados[x] * 0.5) : _remuestreados[x];
            _suavizada[x] = (byte)Math.Round(_media[x]);
        }

        _hayMedia = true;
        PintarLaTraza(_suavizada);
        if (MarcarPicos) BuscarPicos(_suavizada);
        else _picos.Clear();

        _filasPendientes += filas;
        while (_filasPendientes >= 1)
        {
            AnadirFila(_remuestreados);
            _filasPendientes -= 1;
        }

        if (TresD) PintarTresD();
    }

    /// <summary>Color de la cascada para un nivel, ya ajustado al suelo (o al nivel fijo).</summary>
    /// <param name="nivel">Nivel de 0 a 255.</param>
    /// <returns>Color Bgra32.</returns>
    public int ColorDeCascada(byte nivel)
    {
        var (bajo, rango) = Umbrales();
        var t = (nivel - bajo) / rango;
        return _paleta[(int)Math.Clamp(t * 255, 0, 255)];
    }

    /// <summary>
    /// Mira si la escala ha cambiado: corre la cascada si se ha resintonizado en CENTER, o la
    /// limpia y ataca rapido el suelo si se ha cambiado de banda o de span.
    /// </summary>
    private void SeguirLaFrecuencia(double inicioHz, double finHz, long ahoraMs)
    {
        UltimoDesplazamiento = 0;
        if (double.IsNaN(inicioHz) || double.IsNaN(finHz) || finHz <= inicioHz) return;

        var anchoHz = finHz - inicioHz;
        var primera = double.IsNaN(_inicioHz);
        var otroSpan = !primera && Math.Abs(anchoHz - _anchoHz) > 0.5;

        if (!primera && !otroSpan && inicioHz != _inicioHz)
        {
            var hzPorPunto = anchoHz / Ancho;
            var correr = _restoDeDesplazamiento - ((inicioHz - _inicioHz) / hzPorPunto);
            var entero = correr >= 0 ? (int)Math.Floor(correr) : (int)Math.Ceiling(correr);
            _restoDeDesplazamiento = correr - entero;

            if (Math.Abs(entero) >= Ancho)
            {
                Limpiar();
            }
            else if (entero != 0)
            {
                if (Desplazar) Correr(entero);
                else _restoDeDesplazamiento = 0;
                UltimoDesplazamiento = entero;
            }
        }
        else if (otroSpan)
        {
            _restoDeDesplazamiento = 0;
        }

        // La banda solo se mira cuando cambia la escala, no en cada pasada.
        if (primera || otroSpan || inicioHz != _inicioHz)
        {
            var clave = ClaveDeBanda(inicioHz, finHz);
            if (!string.Equals(clave, _clave, StringComparison.Ordinal))
            {
                // Otra banda u otro span: el suelo de antes no vale. Si ya se conoce el de
                // esta banda, se parte de el; si no, ataque rapido hasta medirlo.
                _suelo.AtaqueRapido(ahoraMs, _sueloPorBanda.TryGetValue(clave, out var conocido) ? conocido : null);
                _clave = clave;
            }
        }

        _inicioHz = inicioHz;
        _anchoHz = anchoHz;
    }

    /// <summary>Banda y span: el suelo de la radio cambia con los dos (con el span cambia lo que mide un punto).</summary>
    private static string ClaveDeBanda(double inicioHz, double finHz)
    {
        var centro = (inicioHz + finHz) / 2;
        var banda = centro > 0 ? Banda.DesdeFrecuencia(Frecuencia.DesdeHercios((long)centro)) : default;
        var nombre = banda.EsVacia ? $"{(long)(centro / 1_000_000)}MHz" : banda.Nombre;
        return $"{nombre}|{(long)(finHz - inicioHz)}";
    }

    /// <summary>Corre la historia de la cascada (y de la 3DSS) y la media de la traza.</summary>
    /// <param name="puntos">Positivo: lo pintado se va a la derecha (se ha bajado la frecuencia).</param>
    private void Correr(int puntos)
    {
        var negro = _paleta[0];
        for (var f = 0; f < AltoDeCascada; f++)
        {
            CorrerFila(Cascada.AsSpan(f * Ancho, Ancho), puntos, negro);
            if (_historia[f] is { } h) CorrerFila(h.AsSpan(), puntos, (byte)0);
        }

        // La media de la traza tambien, para que no deje una estela de la señal en su sitio viejo.
        CorrerFila(_media.AsSpan(), puntos, Suelo);
    }

    private static void CorrerFila<T>(Span<T> fila, int puntos, T relleno)
    {
        if (puntos > 0)
        {
            fila[..^puntos].CopyTo(fila[puntos..]);
            fila[..puntos].Fill(relleno);
        }
        else
        {
            var n = -puntos;
            fila[n..].CopyTo(fila[..^n]);
            fila[^n..].Fill(relleno);
        }
    }

    private void Limpiar()
    {
        Array.Fill(Cascada, _paleta[0]);
        Array.Clear(_historia);
        _filasDeHistoria = 0;
        _hayMedia = false;
        _restoDeDesplazamiento = 0;
    }

    private (double Bajo, double Rango) Umbrales()
    {
        var bajo = SueloAutomatico ? Suelo - NegroBajoElSuelo : NivelBajo;
        return (bajo - Brillo, Math.Clamp(Contraste, 10, 200));
    }

    private void PintarLaTraza(byte[] niveles)
    {
        Array.Fill(Traza, Fondo);

        // Rejilla: diez columnas y cinco filas, como la pantalla del equipo.
        for (var i = 1; i < 10; i++)
        {
            var x = i * Ancho / 10;
            for (var y = 0; y < AltoDeTraza; y++) Traza[(y * Ancho) + x] = RejillaColor;
        }

        for (var j = 1; j < 5; j++)
        {
            var y = j * AltoDeTraza / 5;
            Array.Fill(Traza, RejillaColor, y * Ancho, Ancho);
        }

        // El suelo de ruido cae a un cuarto de la altura; la escala es de 150 puntos de nivel.
        var suelo = Suelo;
        var anterior = -1;
        for (var x = 0; x < Ancho; x++)
        {
            var y = YDeLaTraza(niveles[x], suelo);

            for (var f = y + 1; f < AltoDeTraza; f++) Traza[(f * Ancho) + x] = Relleno;

            // Une con el punto anterior para que la linea no quede a trozos.
            var desde = anterior < 0 ? y : Math.Min(anterior, y);
            var hasta = anterior < 0 ? y : Math.Max(anterior, y);
            for (var f = desde; f <= hasta; f++) Traza[(f * Ancho) + x] = Linea;
            anterior = y;
        }
    }

    private static int YDeLaTraza(byte nivel, double suelo)
    {
        var altura = ((nivel - suelo) / 150.0 * AltoDeTraza) + (AltoDeTraza * 0.25);
        return AltoDeTraza - 1 - (int)Math.Clamp(altura, 0, AltoDeTraza - 1);
    }

    /// <summary>
    /// Los picos: maximos que asoman <see cref="AlturaMinimaDePico"/> sobre el suelo, separados
    /// por una bajada de <see cref="HisteresisDePico"/> (idea de Thetis, <c>display.cs</c>
    /// 5291-5321). Se quedan los <see cref="PicosMaximos"/> mas altos y se marcan en la traza.
    /// </summary>
    private void BuscarPicos(byte[] niveles)
    {
        _picos.Clear();
        _candidatos.Clear();
        var suelo = Suelo;
        var umbral = suelo + AlturaMinimaDePico;
        var mejorX = -1;
        double mejor = 0;
        double valle = 0;

        for (var x = 0; x < Ancho; x++)
        {
            double v = niveles[x];
            if (mejorX < 0)
            {
                // Tras un pico hay que volver a subir la histéresis desde el valle: la ladera
                // de bajada de un pico no es otro pico.
                valle = Math.Min(valle, v);
                if (v >= umbral && v >= valle + HisteresisDePico) (mejorX, mejor) = (x, v);
                continue;
            }

            if (v > mejor)
            {
                (mejorX, mejor) = (x, v);
            }
            else if (v <= mejor - HisteresisDePico)
            {
                _candidatos.Add((mejorX, mejor - suelo));
                mejorX = -1;
                valle = v;
            }
        }

        if (mejorX >= 0) _candidatos.Add((mejorX, mejor - suelo));
        _candidatos.Sort((a, b) => b.Alto.CompareTo(a.Alto));

        for (var i = 0; i < _candidatos.Count && _picos.Count < PicosMaximos; i++)
        {
            var (x, alto) = _candidatos[i];

            // Uno mas bajo pegado a otro ya marcado no se marca: seria la misma señal.
            var pegado = false;
            foreach (var p in _picos)
            {
                if (Math.Abs((p.Posicion * Ancho) - x) < SeparacionDePicos) pegado = true;
            }

            if (pegado) continue;

            // Un triangulito ambar encima del pico.
            var y = YDeLaTraza(niveles[x], suelo) - 3;
            _picos.Add(new PicoDeLaTraza((x + 0.5) / Ancho, alto, Math.Max(0, y - 3) / (double)AltoDeTraza));
            for (var f = 0; f < 4; f++)
            {
                var fila = y - f;
                if (fila < 0) break;
                for (var d = -f; d <= f; d++)
                {
                    var px = x + d;
                    if (px >= 0 && px < Ancho) Traza[(fila * Ancho) + px] = ColorDePico;
                }
            }
        }
    }

    private void AnadirFila(byte[] niveles)
    {
        Array.Copy(Cascada, 0, Cascada, Ancho, Cascada.Length - Ancho);

        var (bajo, rango) = Umbrales();
        var escala = 255.0 / rango;
        var paleta = _paleta;
        for (var x = 0; x < Ancho; x++)
        {
            var i = (int)((niveles[x] - bajo) * escala);
            _fila[x] = paleta[i < 0 ? 0 : i > 255 ? 255 : i];
        }

        Array.Copy(_fila, Cascada, Ancho);

        // La historia de niveles, para la vista 3DSS: la mas nueva en la posicion 0.
        var ultima = _historia[^1] ?? new byte[Ancho];
        Array.Copy(_historia, 0, _historia, 1, _historia.Length - 1);
        Array.Copy(niveles, ultima, Ancho);
        _historia[0] = ultima;
        _filasDeHistoria = Math.Min(_filasDeHistoria + 1, _historia.Length);
        FilasAnadidas++;
    }

    /// <summary>
    /// La vista 3DSS: las ultimas pasadas en fila, de la mas vieja (al fondo, arriba, estrecha y
    /// apagada) a la mas nueva (delante, abajo, ancha). Cada pasada tapa lo que queda detras.
    /// </summary>
    private void PintarTresD()
    {
        Array.Fill(VistaTresD, Fondo);
        var centro = (Ancho - 1) / 2.0;
        var cuantas = Math.Min(PasadasTresD, _filasDeHistoria);
        var salto = Math.Max(1, _filasDeHistoria / PasadasTresD);

        for (var k = cuantas - 1; k >= 0; k--)
        {
            if (_historia[k * salto] is not { } niveles) continue;
            var fondo = PasadasTresD <= 1 ? 0 : k / (double)(PasadasTresD - 1);
            var base0 = (int)(AltoTresD - 2 - (fondo * AltoTresD * 0.58));
            var estrechez = 1 - (0.38 * fondo);
            var altura = AltoTresD * 0.42 * (1 - (0.45 * fondo));
            var brillo = 1 - (0.6 * fondo);
            var anterior = -1;
            var anteriorX = -1;

            for (var x = 0; x < Ancho; x++)
            {
                var px = (int)Math.Round(centro + ((x - centro) * estrechez));
                var alto = Math.Clamp((niveles[x] - Suelo + 8) / 150.0 * altura, 0, altura);
                var y = Math.Clamp(base0 - (int)alto, 0, AltoTresD - 1);

                // Tapa lo de detras hasta la base de esta pasada.
                for (var f = y + 1; f <= base0 && f < AltoTresD; f++) VistaTresD[(f * Ancho) + px] = Fondo;

                // El ruido en la linea azulada de la traza; lo que asoma, en los colores de la cascada.
                var color = niveles[x] > Suelo + 20
                    ? Apagar(ColorDeCascada(niveles[x]), brillo)
                    : Apagar(Linea, brillo * 0.55);
                var seguida = anterior >= 0 && (anteriorX == px - 1 || anteriorX == px);
                var desde = seguida ? Math.Min(anterior, y) : y;
                var hasta = seguida ? Math.Max(anterior, y) : y;
                for (var f = desde; f <= hasta; f++) VistaTresD[(f * Ancho) + px] = color;
                anterior = y;
                anteriorX = px;
            }
        }
    }

    private static int Apagar(int color, double brillo)
    {
        var r = (int)(((color >> 16) & 0xFF) * brillo);
        var g = (int)(((color >> 8) & 0xFF) * brillo);
        var b = (int)((color & 0xFF) * brillo);
        return unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
    }
}
