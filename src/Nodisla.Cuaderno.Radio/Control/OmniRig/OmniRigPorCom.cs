using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Nodisla.Cuaderno.Radio.Control.OmniRig;

/// <summary>
/// OmniRig de verdad, por COM y con enlace tardio.
/// </summary>
/// <remarks>
/// OmniRig es un servidor COM <b>fuera de proceso</b> (<c>LocalServer32</c>, que es
/// <c>OmniRig.exe</c>), no una DLL en proceso. Esa es la razon por la que este programa puede
/// seguir siendo de 64 bits: quien corre en 32 bits es el propio <c>OmniRig.exe</c>, y COM se
/// encarga de cruzar la frontera. No hace falta ni compilar en x86 ni montar un proceso puente,
/// y asi la Fase 4 puede meter el modem digital en 64 bits sin pelearse con esto.
///
/// Se usa enlace tardio (<c>dynamic</c>) a proposito: evita arrastrar un ensamblado de interop
/// generado y, sobre todo, evita los eventos COM, que con enlace tardio son un problema. El
/// estado se sondea, que es lo que hace falta de todas formas.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class OmniRigPorCom : IOmniRigCrudo
{
    /// <summary>Identificador COM de OmniRig.</summary>
    public const string ProgId = "OmniRig.OmniRigX";

    private readonly int _numeroDeEquipo;
    private readonly object _candado = new();

    private object? _omni;
    private object? _equipo;

    /// <summary>Crea el envoltorio para uno de los dos equipos de OmniRig.</summary>
    /// <param name="numeroDeEquipo">1 o 2, como en OmniRig.</param>
    public OmniRigPorCom(int numeroDeEquipo = 1)
    {
        if (numeroDeEquipo is not (1 or 2))
        {
            throw new ArgumentOutOfRangeException(
                nameof(numeroDeEquipo),
                numeroDeEquipo,
                "OmniRig solo maneja los equipos 1 y 2.");
        }

        _numeroDeEquipo = numeroDeEquipo;
    }

    /// <inheritdoc />
    public void Abrir()
    {
        lock (_candado)
        {
            Soltar();

            var tipo = Type.GetTypeFromProgID(ProgId, throwOnError: false)
                ?? throw new InvalidOperationException(
                    "OmniRig no está instalado o no está registrado en este equipo.");

            var omni = Activator.CreateInstance(tipo)
                ?? throw new InvalidOperationException("No se pudo crear el objeto COM de OmniRig.");

            dynamic dinamico = omni;
            _omni = omni;
            _equipo = _numeroDeEquipo == 1 ? dinamico.Rig1 : dinamico.Rig2;
        }
    }

    /// <inheritdoc />
    public int Estado
    {
        get
        {
            dynamic equipo = Equipo();
            return (int)equipo.Status;
        }
    }

    /// <inheritdoc />
    public string? TipoDeEquipo
    {
        get
        {
            dynamic equipo = Equipo();
            return (string?)equipo.RigType;
        }
    }

    /// <inheritdoc />
    public long Frecuencia
    {
        get
        {
            dynamic equipo = Equipo();
            return (long)(int)equipo.Freq;
        }

        set
        {
            dynamic equipo = Equipo();
            equipo.Freq = (int)value;
        }
    }

    /// <inheritdoc />
    public int Modo
    {
        get
        {
            dynamic equipo = Equipo();
            return (int)equipo.Mode;
        }

        set
        {
            dynamic equipo = Equipo();
            equipo.Mode = value;
        }
    }

    /// <inheritdoc />
    public int Tx
    {
        get
        {
            dynamic equipo = Equipo();
            return (int)equipo.Tx;
        }

        set
        {
            dynamic equipo = Equipo();
            equipo.Tx = value;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_candado)
        {
            Soltar();
        }
    }

    private object Equipo() => _equipo
        ?? throw new InvalidOperationException("Hay que llamar a Abrir antes de hablar con OmniRig.");

    private void Soltar()
    {
        if (_equipo is not null)
        {
            try
            {
                Marshal.ReleaseComObject(_equipo);
            }
            catch (Exception)
            {
                // Soltar un objeto COM ya muerto no debe tumbar nada.
            }

            _equipo = null;
        }

        if (_omni is not null)
        {
            try
            {
                Marshal.ReleaseComObject(_omni);
            }
            catch (Exception)
            {
                // Idem.
            }

            _omni = null;
        }
    }
}
