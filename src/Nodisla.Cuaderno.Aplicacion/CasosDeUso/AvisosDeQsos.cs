using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Aplicacion.CasosDeUso;

/// <summary>Que le ha pasado a un contacto guardado.</summary>
public enum TipoDeGuardado
{
    /// <summary>Alta: acaba de entrar en el cuaderno.</summary>
    Nuevo,

    /// <summary>Se ha modificado un contacto que ya estaba (F2).</summary>
    Modificado,

    /// <summary>
    /// Se han rellenado campos vacios con la ficha de QRZ despues de guardarlo, porque la
    /// consulta tardo mas de lo que se espera antes de guardar.
    /// </summary>
    Completado,
}

/// <summary>Datos del aviso de un contacto guardado.</summary>
/// <param name="Qso">El contacto, tal y como ha quedado en el cuaderno.</param>
/// <param name="Tipo">Que le ha pasado.</param>
public sealed class QsoGuardadoEventArgs(Qso qso, TipoDeGuardado tipo) : EventArgs
{
    /// <summary>El contacto, tal y como ha quedado en el cuaderno.</summary>
    public Qso Qso { get; } = qso;

    /// <summary>Que le ha pasado.</summary>
    public TipoDeGuardado Tipo { get; } = tipo;
}

/// <summary>
/// El punto comun por el que pasa todo contacto que se guarda: formulario, Digital automatico
/// y ronda de control.
/// </summary>
/// <remarks>
/// Lo disparan los casos de uso <see cref="RegistrarQso"/> y <see cref="EditarQso"/>, no las
/// pantallas: asi quien escucha —la cola de subidas a LoTW, eQSL, Club Log y QRZ— se entera
/// venga el contacto de donde venga. La importacion de ADIF no pasa por aqui a proposito: meter
/// un respaldo de diez anos no puede ponerse a subir diez anos de contactos.
/// </remarks>
public sealed class AvisosDeQsos
{
    /// <summary>Un contacto se ha guardado.</summary>
    public event EventHandler<QsoGuardadoEventArgs>? QsoGuardado;

    /// <summary>Avisa a quien escuche. Un oyente que falla no tumba el guardado.</summary>
    /// <param name="qso">El contacto guardado.</param>
    /// <param name="tipo">Que le ha pasado.</param>
    public void Avisar(Qso qso, TipoDeGuardado tipo)
    {
        ArgumentNullException.ThrowIfNull(qso);
        var oyentes = QsoGuardado;
        if (oyentes is null) return;

        foreach (var oyente in oyentes.GetInvocationList().Cast<EventHandler<QsoGuardadoEventArgs>>())
        {
            try
            {
                oyente(this, new QsoGuardadoEventArgs(qso, tipo));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning(
                    $"Un oyente del cuaderno ha fallado con el contacto {qso.Call.Valor}: {ex.Message}");
            }
        }
    }
}
