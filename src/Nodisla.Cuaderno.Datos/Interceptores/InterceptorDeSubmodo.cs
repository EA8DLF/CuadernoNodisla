using Microsoft.EntityFrameworkCore.Diagnostics;
using Nodisla.Cuaderno.Datos.Configuracion;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Datos.Interceptores;

/// <summary>
/// Recompone <see cref="Qso.Mode"/> con su submodo al leer de la base.
/// </summary>
/// <remarks>
/// El modo ADIF ocupa dos columnas, <c>mode</c> y <c>submode</c>. La columna <c>mode</c> se
/// mapea con un conversor sobre <see cref="Qso.Mode"/> y el submodo viaja en una propiedad
/// sombra. Al materializar, el conversor solo ha visto <c>mode</c>, asi que aqui se vuelve a
/// unir el par. Hacerlo en un interceptor y no en los repositorios garantiza que cualquier
/// consulta, venga de donde venga, devuelva el modo completo.
/// </remarks>
public sealed class InterceptorDeSubmodo : IMaterializationInterceptor
{
    /// <summary>Instancia compartida; el interceptor no guarda estado.</summary>
    public static InterceptorDeSubmodo Instancia { get; } = new();

    /// <inheritdoc/>
    public object InitializedInstance(MaterializationInterceptionData materializationData, object instance)
    {
        if (instance is Qso qso && !qso.Mode.EsVacio)
        {
            var submodo = materializationData.GetPropertyValue<string?>(NombresDeColumna.PropiedadSubmodo);
            if (!string.IsNullOrEmpty(submodo))
            {
                qso.Mode = Modo.Crudo(qso.Mode.Principal, submodo);
            }
        }

        return instance;
    }
}
