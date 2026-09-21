namespace Nodisla.Cuaderno.Dominio.Entidades;

/// <summary>Servicio o via por la que se confirma un contacto.</summary>
public enum MedioDeConfirmacion
{
    /// <summary>Tarjeta QSL en papel.</summary>
    Papel,
    Lotw,
    Eqsl,
    ClubLog,
    QrzCom,
    HamQth,
    HrdLog,
    QrzCq,
}

/// <summary>Estado de una confirmacion, en el sentido de los campos <c>*_QSL_SENT/RCVD</c> de ADIF.</summary>
public enum EstadoDeConfirmacion
{
    /// <summary>No consta nada.</summary>
    Ninguno,
    /// <summary>Pendiente de enviar o de recibir.</summary>
    Pendiente,
    /// <summary>Enviado o recibido y valido.</summary>
    Confirmado,
    /// <summary>Solicitado a la otra parte.</summary>
    Solicitado,
    /// <summary>La otra parte no lo quiere, o se decide no enviarlo.</summary>
    Rechazado,
    /// <summary>Enviado y devuelto sin entregar.</summary>
    Devuelto,
    /// <summary>Invalido o no verificado por el servicio.</summary>
    Invalido,
}

/// <summary>Via de envio de una tarjeta en papel (campo <c>QSL_SENT_VIA</c> de ADIF).</summary>
public enum ViaDeEnvio
{
    Ninguna,
    /// <summary>Buro.</summary>
    Buro,
    /// <summary>Correo directo.</summary>
    Directo,
    /// <summary>A traves de un gestor (manager).</summary>
    Gestor,
    /// <summary>Electronico.</summary>
    Electronico,
}

/// <summary>Tipo de referencia asociada a un contacto: programas de activaciones y divisiones.</summary>
public enum TipoDeReferencia
{
    Iota,
    Sota,
    Pota,
    Wwff,
    Wca,
    Dme,
    /// <summary>Referencia de un programa no modelado; el nombre va en el texto.</summary>
    Otra,
}

/// <summary>Indica si una referencia es del corresponsal o de mi estacion.</summary>
public enum LadoDeReferencia
{
    /// <summary>Del corresponsal (campos <c>SIG</c>, <c>IOTA</c>, <c>POTA_REF</c>…).</summary>
    Corresponsal,
    /// <summary>Mia (campos <c>MY_SIG</c>, <c>MY_IOTA</c>, <c>MY_POTA_REF</c>…).</summary>
    Propia,
}
