using System.Runtime.InteropServices;

using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Radio.Espectro;

/// <summary>Donde se pueden leer bytes del analizador. Existe para probar sin radio.</summary>
public interface IPuenteDelAnalizador : IDisposable
{
    /// <summary>Lee exactamente <paramref name="destino"/>.Length bytes, o falla.</summary>
    /// <param name="destino">Donde dejarlos.</param>
    /// <returns>Si se leyeron todos.</returns>
    bool Leer(Span<byte> destino);
}

/// <summary>Resultado de intentar abrir el puente.</summary>
public enum AperturaDelPuente
{
    /// <summary>Abierto.</summary>
    Abierto,

    /// <summary>No esta <c>LibFT4222-64.dll</c> o <c>ftd2xx.dll</c>.</summary>
    SinBiblioteca,

    /// <summary>No hay ningun «FT4222 A» conectado.</summary>
    SinDispositivo,

    /// <summary>Esta, pero no se deja abrir (otro programa lo tiene) o no acepta el modo SPI.</summary>
    Fallo,
}

/// <summary>
/// El puente USB–SPI FTDI FT4222H que el FT-710 lleva dentro, en modo maestro SPI y solo lectura.
/// </summary>
/// <remarks>
/// <para>
/// Carga en tiempo de ejecucion <c>ftd2xx.dll</c> (la instala el controlador D2XX de FTDI) y
/// <c>LibFT4222-64.dll</c> (biblioteca oficial de FTDI, viaja junto al programa desde
/// <c>lib/ftdi</c>). Si falta alguna, no revienta: <see cref="Abrir"/> lo dice.
/// </para>
/// <para>
/// Parametros los mismos que usa wfview: SPI de una linea, reloj del sistema a 24 MHz
/// dividido por 64, reloj en reposo alto, captura en el flanco de subida, CS 0. El PC solo
/// genera reloj y lee; no escribe nada al equipo.
/// </para>
/// </remarks>
public sealed class PuenteFt4222 : IPuenteDelAnalizador
{
    /// <summary>Nombre de la biblioteca de FTDI de 64 bits.</summary>
    public const string Biblioteca = "LibFT4222-64.dll";

    private const uint FtOk = 0;
    private const uint FtDeviceNotFound = 2;
    private const uint AbrirPorDescripcion = 2;
    private const int SpiUnaLinea = 1;
    private const int DivisorEntre64 = 6;
    private const int RelojEnReposoAlto = 1;
    private const int FlancoDeSubida = 0;
    private const int RelojDelSistema24Mhz = 1;

    private readonly IntPtr _d2xx;
    private readonly IntPtr _ft4222;
    private readonly FtClose _cerrar;
    private readonly Ft4222UnInitialize _desinicializar;
    private readonly Ft4222SpiMasterSingleRead _leer;
    private IntPtr _manejador;

    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Ansi)]
    private delegate uint FtOpenEx(string argumento, uint banderas, out IntPtr manejador);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint FtClose(IntPtr manejador);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint FtSetTimeouts(IntPtr manejador, uint lectura, uint escritura);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint FtSetLatencyTimer(IntPtr manejador, byte latencia);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int Ft4222UnInitialize(IntPtr manejador);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int Ft4222SpiMasterInit(IntPtr manejador, int lineas, int divisor, int polaridad, int fase, byte seleccion);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int Ft4222SetClock(IntPtr manejador, int reloj);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int Ft4222SpiMasterSingleRead(IntPtr manejador, ref byte destino, ushort cuantos, out ushort leidos, int finDeTransaccion);

    private PuenteFt4222(IntPtr d2xx, IntPtr ft4222, IntPtr manejador)
    {
        _d2xx = d2xx;
        _ft4222 = ft4222;
        _manejador = manejador;
        _cerrar = Funcion<FtClose>(d2xx, "FT_Close");
        _desinicializar = Funcion<Ft4222UnInitialize>(ft4222, "FT4222_UnInitialize");
        _leer = Funcion<Ft4222SpiMasterSingleRead>(ft4222, "FT4222_SPIMaster_SingleRead");
    }

    /// <summary>Busca las bibliotecas y abre «FT4222 A» en maestro SPI.</summary>
    /// <param name="puente">El puente abierto.</param>
    /// <param name="detalle">Que ha pasado, para el registro y la pantalla.</param>
    /// <returns>Como ha ido.</returns>
    public static AperturaDelPuente Abrir(out PuenteFt4222? puente, out string detalle)
    {
        puente = null;

        if (!NativeLibrary.TryLoad("ftd2xx.dll", out var d2xx))
        {
            detalle = Textos.T("Servicios.Radio.Analizador.SinD2xx");
            return AperturaDelPuente.SinBiblioteca;
        }

        var ruta = Path.Combine(AppContext.BaseDirectory, Biblioteca);
        if (!NativeLibrary.TryLoad(ruta, out var ft4222))
        {
            NativeLibrary.Free(d2xx);
            detalle = Textos.F("Servicios.Radio.Analizador.SinBiblioteca", Biblioteca);
            return AperturaDelPuente.SinBiblioteca;
        }

        var manejador = IntPtr.Zero;
        try
        {
            var abrir = Funcion<FtOpenEx>(d2xx, "FT_OpenEx");
            var estado = abrir("FT4222 A", AbrirPorDescripcion, out manejador);
            if (estado != FtOk)
            {
                NativeLibrary.Free(ft4222);
                NativeLibrary.Free(d2xx);
                detalle = estado == FtDeviceNotFound
                    ? Textos.T("Servicios.Radio.Analizador.SinPuente")
                    : Textos.F("Servicios.Radio.Analizador.Ocupado", estado);
                return estado == FtDeviceNotFound ? AperturaDelPuente.SinDispositivo : AperturaDelPuente.Fallo;
            }

            var abierto = new PuenteFt4222(d2xx, ft4222, manejador);
            if (Funcion<FtSetTimeouts>(d2xx, "FT_SetTimeouts")(manejador, 100, 100) != FtOk
                || Funcion<FtSetLatencyTimer>(d2xx, "FT_SetLatencyTimer")(manejador, 2) != FtOk
                || Funcion<Ft4222SpiMasterInit>(ft4222, "FT4222_SPIMaster_Init")(
                    manejador, SpiUnaLinea, DivisorEntre64, RelojEnReposoAlto, FlancoDeSubida, 0x01) != 0
                || Funcion<Ft4222SetClock>(ft4222, "FT4222_SetClock")(manejador, RelojDelSistema24Mhz) != 0)
            {
                abierto.Dispose();
                detalle = Textos.T("Servicios.Radio.Analizador.SinSpi");
                return AperturaDelPuente.Fallo;
            }

            puente = abierto;
            detalle = Textos.T("Servicios.Radio.Analizador.Abierto");
            return AperturaDelPuente.Abierto;
        }
        catch (EntryPointNotFoundException ex)
        {
            NativeLibrary.Free(ft4222);
            NativeLibrary.Free(d2xx);
            detalle = Textos.F("Servicios.Radio.Analizador.Incompleta", ex.Message);
            return AperturaDelPuente.SinBiblioteca;
        }
    }

    /// <inheritdoc />
    public bool Leer(Span<byte> destino)
    {
        if (_manejador == IntPtr.Zero || destino.IsEmpty) return false;
        var estado = _leer(_manejador, ref MemoryMarshal.GetReference(destino), (ushort)destino.Length, out var leidos, 0);
        return estado == 0 && leidos == destino.Length;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_manejador != IntPtr.Zero)
        {
            _desinicializar(_manejador);
            _cerrar(_manejador);
            _manejador = IntPtr.Zero;
            NativeLibrary.Free(_ft4222);
            NativeLibrary.Free(_d2xx);
        }
    }

    private static T Funcion<T>(IntPtr biblioteca, string nombre)
        where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(biblioteca, nombre));
}
