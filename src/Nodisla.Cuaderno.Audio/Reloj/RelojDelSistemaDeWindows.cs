using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;

namespace Nodisla.Cuaderno.Audio.Reloj;

/// <summary>
/// El reloj de Windows de verdad: el unico sitio de este modulo que toca el sistema.
/// </summary>
/// <remarks>
/// <para>
/// Cambiar la hora del ordenador pide el privilegio <c>SeSystemtimePrivilege</c>. Los
/// administradores lo tienen, pero <b>apagado</b> en su credencial: hay que encenderlo antes de
/// pedirlo, o Windows contesta que no hay privilegios y parece un fallo de permisos cuando no
/// lo es.
/// </para>
/// <para>
/// Nada de aqui se llama solo. El reloj del ordenador es de su dueno.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class RelojDelSistemaDeWindows : IRelojDelSistema
{
    private const int TokenAjustarPrivilegios = 0x0020;
    private const int TokenConsultar = 0x0008;
    private const int PrivilegioActivado = 0x0002;
    private const string PrivilegioDeHora = "SeSystemtimePrivilege";

    /// <inheritdoc />
    public bool HayPermisosParaCambiarLaHora
    {
        get
        {
            try
            {
                using var identidad = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identidad).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (Exception)
            {
                // Si no se puede ni preguntar, se da por hecho que no.
                return false;
            }
        }
    }

    /// <inheritdoc />
    public void PonerHoraUtc(DateTime instanteUtc)
    {
        var enUtc = instanteUtc.Kind == DateTimeKind.Utc ? instanteUtc : instanteUtc.ToUniversalTime();

        EncenderElPrivilegioDeHora();

        var hora = new HoraDelSistema
        {
            Ano = (ushort)enUtc.Year,
            Mes = (ushort)enUtc.Month,
            DiaDeLaSemana = (ushort)enUtc.DayOfWeek,
            Dia = (ushort)enUtc.Day,
            Hora = (ushort)enUtc.Hour,
            Minuto = (ushort)enUtc.Minute,
            Segundo = (ushort)enUtc.Second,
            Milisegundos = (ushort)enUtc.Millisecond,
        };

        if (!SetSystemTime(ref hora))
        {
            var codigo = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"Windows no ha dejado cambiar la hora del sistema (error {codigo}).",
                new Win32Exception(codigo));
        }
    }

    /// <inheritdoc />
    public async Task<(int Codigo, string Salida)> EjecutarAsync(
        string programa,
        string argumentos,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(programa);

        var arranque = new ProcessStartInfo(programa, argumentos)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,

            // Sin ventana: Jose esta trabajando en este ordenador y no se le abre nada delante.
            CreateNoWindow = true,
        };

        using var proceso = Process.Start(arranque)
            ?? throw new InvalidOperationException($"No se ha podido ejecutar «{programa}».");

        var salida = new StringBuilder();
        salida.Append(await proceso.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false));
        salida.Append(await proceso.StandardError.ReadToEndAsync(ct).ConfigureAwait(false));

        await proceso.WaitForExitAsync(ct).ConfigureAwait(false);
        return (proceso.ExitCode, salida.ToString().Trim());
    }

    /// <summary>
    /// Enciende el privilegio de cambiar la hora en la credencial del proceso.
    /// </summary>
    /// <remarks>
    /// Si no se es administrador esto falla, y esta bien que falle aqui: el sincronizador
    /// pregunta antes por los permisos y no llega a este punto sin ellos.
    /// </remarks>
    private static void EncenderElPrivilegioDeHora()
    {
        var proceso = GetCurrentProcess();
        if (!OpenProcessToken(proceso, TokenAjustarPrivilegios | TokenConsultar, out var credencial))
        {
            throw new InvalidOperationException(
                "No se ha podido abrir la credencial del proceso para cambiar la hora.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }

        try
        {
            if (!LookupPrivilegeValue(null, PrivilegioDeHora, out var identificador))
            {
                throw new InvalidOperationException(
                    "Windows no reconoce el privilegio de cambiar la hora del sistema.",
                    new Win32Exception(Marshal.GetLastWin32Error()));
            }

            var privilegios = new PrivilegiosDeLaCredencial
            {
                Cuantos = 1,
                Identificador = identificador,
                Atributos = PrivilegioActivado,
            };

            AdjustTokenPrivileges(credencial, false, ref privilegios, 0, IntPtr.Zero, IntPtr.Zero);

            var codigo = Marshal.GetLastWin32Error();
            if (codigo != 0)
            {
                throw new InvalidOperationException(
                    "No se ha podido activar el permiso para cambiar la hora: hace falta ejecutar el "
                        + "programa como administrador.",
                    new Win32Exception(codigo));
            }
        }
        finally
        {
            CloseHandle(credencial);
        }
    }

    /// <summary>La hora tal y como la quiere Windows.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct HoraDelSistema
    {
        public ushort Ano;
        public ushort Mes;
        public ushort DiaDeLaSemana;
        public ushort Dia;
        public ushort Hora;
        public ushort Minuto;
        public ushort Segundo;
        public ushort Milisegundos;
    }

    /// <summary>Identificador interno de un privilegio.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct IdentificadorDePrivilegio
    {
        public uint ParteBaja;
        public int ParteAlta;
    }

    /// <summary>Un privilegio y su estado, tal y como los pide Windows.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct PrivilegiosDeLaCredencial
    {
        public int Cuantos;
        public IdentificadorDePrivilegio Identificador;
        public int Atributos;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetSystemTime(ref HoraDelSistema hora);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr manejador);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr proceso, int permisos, out IntPtr credencial);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValue(
        string? sistema,
        string nombre,
        out IdentificadorDePrivilegio identificador);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(
        IntPtr credencial,
        [MarshalAs(UnmanagedType.Bool)] bool quitarTodos,
        ref PrivilegiosDeLaCredencial nuevos,
        int tamanoAnterior,
        IntPtr anteriores,
        IntPtr tamanoDevuelto);
}
