using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nodisla.Cuaderno.Aplicacion.Puertos;

namespace Nodisla.Cuaderno.Diplomas;

/// <summary>Registro del motor de diplomas en el contenedor de servicios.</summary>
public static class ExtensionesDeServicio
{
    /// <summary>
    /// Registra el motor de diplomas.
    /// </summary>
    /// <remarks>
    /// Se registra como <b>unico</b> a proposito: el motor mantiene abierta la conexion con el
    /// cuaderno, el catalogo compilado y la cache de progreso, y tenerlos por peticion echaria a
    /// perder justo lo que hace inmediato el aviso al teclear.
    /// <para>
    /// Hace falta que este registrado <see cref="IFabricaDeConexion"/>, que lo pone la capa de
    /// datos, y <see cref="Dominio.Dxcc.IResolutorDxcc"/>. El motor se registra ademas como
    /// <see cref="INotificadorDeDiplomas"/>: quien anada contactos o baje confirmaciones tiene
    /// que avisar por ahi.
    /// </para>
    /// </remarks>
    /// <param name="servicios">Coleccion de servicios.</param>
    /// <param name="configurar">Ajustes del motor.</param>
    /// <returns>La misma coleccion, para encadenar.</returns>
    public static IServiceCollection AnadirDiplomas(
        this IServiceCollection servicios,
        Action<OpcionesDeDiplomas> configurar)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(configurar);

        var opciones = new OpcionesDeDiplomas();
        configurar(opciones);
        servicios.TryAddSingleton(opciones);

        servicios.TryAddSingleton<MotorDeDiplomas>();
        servicios.TryAddSingleton<IDiplomas>(s => s.GetRequiredService<MotorDeDiplomas>());
        servicios.TryAddSingleton<INotificadorDeDiplomas>(s => s.GetRequiredService<MotorDeDiplomas>());

        return servicios;
    }
}
