using System.Net.Http;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Servicios.ClubLog;
using Nodisla.Cuaderno.Servicios.Eqsl;
using Nodisla.Cuaderno.Servicios.HamQth;
using Nodisla.Cuaderno.Servicios.Lotw;
using Nodisla.Cuaderno.Servicios.Qrz;
using Nodisla.Cuaderno.Servicios.Red;

namespace Nodisla.Cuaderno.Ui.Ajustes;

/// <summary>
/// Monta los servicios en linea con las cuentas que haya en Ajustes en cada momento.
/// </summary>
/// <remarks>
/// <para>
/// Los servicios reciben sus opciones al construirse y no las cambian. Si se registraran una
/// vez al arrancar, un usuario escrito en Ajustes no valdria hasta reiniciar. Aqui se piden
/// cada vez y se reutiliza la instancia mientras las opciones no cambien: asi QRZ.com
/// conserva su clave de sesion y no se abre una por consulta, que es lo que hace que QRZ
/// limite a un programa.
/// </para>
/// <para>
/// Un usuario vacio en Ajustes quiere decir el indicativo del perfil de estacion activo.
/// </para>
/// </remarks>
public sealed class CuentasDeServicios
{
    private readonly AjustesDelPrograma _ajustes;
    private readonly IAlmacenDeCredenciales _credenciales;
    private readonly IHttpClientFactory _fabrica;
    private readonly PoliticaDeReintentos _reintentos;
    private readonly Func<Indicativo> _perfil;
    private readonly object _cerrojo = new();

    private (OpcionesQrz Opciones, ConsultaQrzCom Consulta, ServicioQrzCuaderno Cuaderno)? _qrz;
    private (OpcionesHamQth Opciones, ConsultaHamQth Consulta)? _hamQth;
    private (OpcionesLotw Opciones, ServicioLotw Servicio)? _lotw;
    private (OpcionesEqsl Opciones, ServicioEqsl Servicio)? _eqsl;
    private (OpcionesClubLog Opciones, ServicioClubLog Servicio)? _clubLog;

    /// <summary>Monta el proveedor.</summary>
    /// <param name="ajustes">Ajustes del programa, con las cuentas.</param>
    /// <param name="credenciales">Almacen cifrado.</param>
    /// <param name="fabrica">Fabrica de clientes HTTP.</param>
    /// <param name="reintentos">Politica de reintentos.</param>
    /// <param name="perfil">Indicativo del perfil de estacion activo.</param>
    public CuentasDeServicios(
        AjustesDelPrograma ajustes,
        IAlmacenDeCredenciales credenciales,
        IHttpClientFactory fabrica,
        PoliticaDeReintentos reintentos,
        Func<Indicativo> perfil)
    {
        _ajustes = ajustes ?? throw new ArgumentNullException(nameof(ajustes));
        _credenciales = credenciales ?? throw new ArgumentNullException(nameof(credenciales));
        _fabrica = fabrica ?? throw new ArgumentNullException(nameof(fabrica));
        _reintentos = reintentos ?? throw new ArgumentNullException(nameof(reintentos));
        _perfil = perfil ?? throw new ArgumentNullException(nameof(perfil));
    }

    /// <summary>Las cuentas y casillas guardadas.</summary>
    public AjustesDeServicios Cuentas => _ajustes.Servicios;

    /// <summary>Indicativo del perfil activo, el que vale cuando un usuario se deja vacio.</summary>
    public string IndicativoDelPerfil
    {
        get
        {
            try
            {
                return _perfil().Valor;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }

    /// <summary>Las consultas de indicativo por orden: QRZ.com y, de reserva, HamQTH.</summary>
    public IReadOnlyList<IConsultaIndicativo> Consultas()
    {
        lock (_cerrojo)
        {
            MontarQrz();
            var opcionesHamQth = new OpcionesHamQth { Usuario = Usuario(Cuentas.UsuarioHamQth) };
            if (_hamQth is not { } h || h.Opciones != opcionesHamQth)
            {
                _hamQth = (opcionesHamQth, new ConsultaHamQth(_fabrica, _credenciales, opcionesHamQth, _reintentos));
            }
            return [_qrz!.Value.Consulta, _hamQth!.Value.Consulta];
        }
    }

    /// <summary>Los servicios de confirmacion a los que se sube.</summary>
    public IReadOnlyList<IServicioQsl> ServiciosQsl()
    {
        lock (_cerrojo)
        {
            MontarQrz();

            var lotw = new OpcionesLotw
            {
                Usuario = Usuario(Cuentas.UsuarioLotw),
                UbicacionDeEstacion = Cuentas.UbicacionTqsl?.Trim() ?? string.Empty,
                RutaDeTqsl = string.IsNullOrWhiteSpace(Cuentas.RutaTqsl) ? null : Cuentas.RutaTqsl.Trim(),
            };
            if (_lotw is not { } l || l.Opciones != lotw)
            {
                _lotw = (lotw, new ServicioLotw(_fabrica, _credenciales, lotw, reintentos: _reintentos));
            }

            var eqsl = new OpcionesEqsl
            {
                Usuario = Usuario(Cuentas.UsuarioEqsl),
                ApodoDeEstacion = string.IsNullOrWhiteSpace(Cuentas.ApodoEqsl) ? null : Cuentas.ApodoEqsl.Trim(),
            };
            if (_eqsl is not { } e || e.Opciones != eqsl)
            {
                _eqsl = (eqsl, new ServicioEqsl(_fabrica, _credenciales, eqsl, _reintentos));
            }

            var clubLog = new OpcionesClubLog
            {
                Correo = Cuentas.CorreoClubLog?.Trim() ?? string.Empty,
                Indicativo = Usuario(Cuentas.IndicativoClubLog),
            };
            if (_clubLog is not { } c || c.Opciones != clubLog)
            {
                _clubLog = (clubLog, new ServicioClubLog(_fabrica, _credenciales, clubLog, _reintentos));
            }

            return [_lotw!.Value.Servicio, _eqsl!.Value.Servicio, _clubLog!.Value.Servicio, _qrz!.Value.Cuaderno];
        }
    }

    /// <summary>La casilla de subida de un servicio. Nulo: lo que digan las credenciales.</summary>
    /// <param name="medio">Servicio.</param>
    public bool? Activado(MedioDeConfirmacion medio) => Activado(Cuentas, medio);

    /// <summary>La casilla de subida de un servicio en unos ajustes dados.</summary>
    /// <param name="cuentas">Ajustes de los servicios.</param>
    /// <param name="medio">Servicio.</param>
    public static bool? Activado(AjustesDeServicios cuentas, MedioDeConfirmacion medio)
    {
        ArgumentNullException.ThrowIfNull(cuentas);
        return medio switch
        {
            MedioDeConfirmacion.Lotw => cuentas.SubirALotw,
            MedioDeConfirmacion.Eqsl => cuentas.SubirAEqsl,
            MedioDeConfirmacion.ClubLog => cuentas.SubirAClubLog,
            MedioDeConfirmacion.QrzCom => cuentas.SubirAQrz,
            _ => false,
        };
    }

    /// <summary>Fija la casilla de subida de un servicio.</summary>
    /// <param name="cuentas">Ajustes de los servicios.</param>
    /// <param name="medio">Servicio.</param>
    /// <param name="valor">Activada o no.</param>
    public static void Fijar(AjustesDeServicios cuentas, MedioDeConfirmacion medio, bool valor)
    {
        ArgumentNullException.ThrowIfNull(cuentas);
        switch (medio)
        {
            case MedioDeConfirmacion.Lotw: cuentas.SubirALotw = valor; break;
            case MedioDeConfirmacion.Eqsl: cuentas.SubirAEqsl = valor; break;
            case MedioDeConfirmacion.ClubLog: cuentas.SubirAClubLog = valor; break;
            case MedioDeConfirmacion.QrzCom: cuentas.SubirAQrz = valor; break;
        }
    }

    /// <summary>El usuario escrito o, si esta vacio, el indicativo del perfil.</summary>
    /// <param name="escrito">Lo escrito en Ajustes.</param>
    public string Usuario(string? escrito) =>
        string.IsNullOrWhiteSpace(escrito) ? IndicativoDelPerfil : escrito.Trim();

    private void MontarQrz()
    {
        var opciones = new OpcionesQrz { Usuario = Usuario(Cuentas.UsuarioQrz) };
        if (_qrz is { } q && q.Opciones == opciones) return;
        _qrz = (
            opciones,
            new ConsultaQrzCom(_fabrica, _credenciales, opciones, _reintentos),
            new ServicioQrzCuaderno(_fabrica, _credenciales, opciones, _reintentos));
    }
}
