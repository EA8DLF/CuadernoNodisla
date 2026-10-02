using System.IO.Ports;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Radio.Control;
using Nodisla.Cuaderno.Radio.Control.Ft710;
using Nodisla.Cuaderno.Radio.Control.OmniRig;
using Nodisla.Cuaderno.Radio.Control.Rigctld;
using Nodisla.Cuaderno.Radio.Control.Yaesu;
using Nodisla.Cuaderno.Radio.Modelos;

namespace Nodisla.Cuaderno.Radio;

/// <summary>Crea el control del equipo que toque segun los ajustes.</summary>
/// <remarks>
/// El CAT nativo vale para todos los modelos del <see cref="CatalogoDeModelos"/>. Con
/// <see cref="OpcionesDeRadio.Modelo"/> vacio o <c>auto</c> se busca el equipo: primero el CAT
/// nuevo de Yaesu (<c>ID;</c>; el FT-710 sale igual que siempre), luego el resto de
/// protocolos que sepan identificarse (ICOM por CI-V <c>19 00</c>).
/// </remarks>
[SupportedOSPlatform("windows")]
public static class FabricaDeControlEquipo
{
    /// <summary>
    /// Crea el control de la via indicada en los ajustes.
    /// </summary>
    /// <remarks>
    /// Si se pide el CAT nativo sin decir el puerto, hace falta buscarlo, y buscar lleva tiempo:
    /// para eso esta <see cref="CrearAsync"/>. Con el puerto dicho y el modelo en automatico se
    /// crea el FT-710, como antes de haber mas modelos (el control avisa si contesta otro).
    /// </remarks>
    /// <param name="opciones">Ajustes de radio.</param>
    /// <param name="registro">Donde anotar lo que pasa.</param>
    /// <returns>El control del equipo, listo para conectar.</returns>
    public static IControlEquipo Crear(OpcionesDeRadio? opciones = null, ILogger? registro = null)
    {
        var ajustes = opciones ?? new OpcionesDeRadio();

        if (ajustes.Via == ViaDeControl.CatNativo && !string.IsNullOrWhiteSpace(ajustes.Ft710.Puerto))
        {
            var modelo = CatalogoDeModelos.Buscar(ajustes.Modelo);
            if (modelo is not null && CatalogoDeModelos.ProveedorDe(modelo) is { } proveedor)
            {
                return proveedor.Crear(modelo, ajustes.Ft710.Puerto, ajustes.Ft710.Baudios, ajustes, registro);
            }

            return Ft710(ajustes, ajustes.Ft710.Puerto, ajustes.Ft710.Baudios, registro, null);
        }

        return ajustes.Via switch
        {
            ViaDeControl.Rigctld => new ControlRigctld(ajustes.Rigctld, registro),
            ViaDeControl.OmniRig => new ControlOmniRig(null, ajustes.OmniRig, registro),
            _ => new ControlNulo(registro),
        };
    }

    /// <summary>
    /// Crea el control y, si hace falta, busca el equipo por los puertos serie.
    /// </summary>
    /// <param name="opciones">Ajustes de radio.</param>
    /// <param name="registro">Donde anotar lo que pasa.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>
    /// El control del equipo. Si no aparece por ningun puerto, se devuelve el control sin equipo
    /// para que el operador pueda seguir apuntando a mano.
    /// </returns>
    public static async Task<IControlEquipo> CrearAsync(
        OpcionesDeRadio? opciones = null,
        ILogger? registro = null,
        CancellationToken ct = default)
    {
        var ajustes = opciones ?? new OpcionesDeRadio();
        var anotador = registro ?? NullLogger.Instance;

        if (ajustes.Via != ViaDeControl.CatNativo)
        {
            return Crear(ajustes, registro);
        }

        var elegido = CatalogoDeModelos.Buscar(ajustes.Modelo);
        if (elegido is not null && elegido.Protocolo != ProtocoloCat.YaesuAscii && !string.IsNullOrWhiteSpace(ajustes.Ft710.Puerto))
        {
            // Con el puerto dicho se crea sin mas: si no contesta, lo dira al conectar.
            return Crear(ajustes, registro);
        }

        var encontrado = await BuscarEquipoAsync(ajustes, null, registro, ct).ConfigureAwait(false);
        if (encontrado is null)
        {
            anotador.LogWarning(
                "No se ha encontrado {Que} en los puertos serie; se opera sin equipo.",
                elegido is null ? "ningún equipo conocido" : $"ningún {elegido.Nombre}");
            return new ControlNulo(registro);
        }

        return CrearPara(encontrado, ajustes, registro);
    }

    /// <summary>
    /// Busca el equipo de los ajustes: primero por el puerto guardado, luego por todos.
    /// </summary>
    /// <remarks>
    /// <b>Buscar es leer.</b> Solo se mandan ordenes de identificacion (<c>ID;</c>, CI-V
    /// <c>19 00</c>, lectura de frecuencia del CAT antiguo). Con el modelo en automatico se prueba
    /// el CAT nuevo de Yaesu y luego los protocolos que sepan identificarse; el CAT antiguo de
    /// Yaesu no, porque no tiene identificacion.
    /// </remarks>
    /// <param name="ajustes">Ajustes de radio.</param>
    /// <param name="aviso">Por donde se cuenta por donde va, o nulo.</param>
    /// <param name="registro">Donde anotar.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>Lo encontrado, o nulo.</returns>
    public static async Task<EquipoIdentificado?> BuscarEquipoAsync(
        OpcionesDeRadio ajustes,
        IProgress<string>? aviso,
        ILogger? registro,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ajustes);
        var anotador = registro ?? NullLogger.Instance;
        var elegido = CatalogoDeModelos.Buscar(ajustes.Modelo);
        var puertoGuardado = ajustes.Ft710.Puerto;

        if (elegido is not null && elegido.Protocolo != ProtocoloCat.YaesuAscii)
        {
            var proveedor = CatalogoDeModelos.ProveedorDe(elegido);
            if (proveedor is null) return null;
            if (!string.IsNullOrWhiteSpace(puertoGuardado))
            {
                aviso?.Report(Textos.F("Servicios.Radio.ProbandoModelo", elegido.Nombre, puertoGuardado));
                var alli = await proveedor.IdentificarAsync(
                    puertoGuardado, [ajustes.Ft710.Baudios, .. elegido.Velocidades], ajustes, registro, ct).ConfigureAwait(false);
                if (alli is not null) return alli with { Modelo = elegido };
            }

            aviso?.Report(Textos.F("Servicios.Radio.BuscandoModelo", elegido.Nombre));
            var visto = await BuscarConAsync(proveedor, elegido, ajustes, registro, ct).ConfigureAwait(false);
            return visto is null ? null : visto with { Modelo = elegido };
        }

        var perfilElegido = PerfilesYaesu.De(elegido);

        // Lo guardado es una pista, no una certeza: este equipo ha llegado a cambiar de puerto y
        // de velocidad a la vez entre dos encendidos —de COM3 a 115200 a COM15 a 38400—. Se
        // prueba primero por donde estaba, que es lo rápido, y si ya no contesta se barre en vez
        // de darse por vencido.
        if (!string.IsNullOrWhiteSpace(puertoGuardado))
        {
            aviso?.Report(Textos.F("Servicios.Radio.ProbandoPuerto", puertoGuardado, ajustes.Ft710.Baudios));
            var dondeEstaba = await AutodeteccionFt710.ComprobarAsync(
                puertoGuardado,
                ajustes.Ft710.Baudios,
                registro,
                ct).ConfigureAwait(false);

            if (dondeEstaba is not null)
            {
                return Yaesu(dondeEstaba, perfilElegido);
            }

            anotador.LogInformation(
                "Por {Puerto} a {Baudios} ya no contesta el equipo; se buscan todos los puertos.",
                puertoGuardado,
                ajustes.Ft710.Baudios);
        }

        aviso?.Report(Textos.T("Servicios.Radio.BuscandoEquipo"));
        var equipos = await AutodeteccionFt710.BuscarAsync(registro, ct).ConfigureAwait(false);
        var yaesu = perfilElegido is not null
            ? equipos.FirstOrDefault(e => PerfilesYaesu.PorIdentificador(e.Identificador) == perfilElegido)
              ?? equipos.FirstOrDefault()
            : equipos.FirstOrDefault(e => e.EsFt710)
              ?? equipos.FirstOrDefault(e => PerfilesYaesu.PorIdentificador(e.Identificador) is not null);

        if (yaesu is not null)
        {
            return Yaesu(yaesu, perfilElegido);
        }

        if (elegido is null)
        {
            // Automatico y ningun Yaesu: los demas protocolos que sepan identificarse.
            foreach (var proveedor in CatalogoDeModelos.Proveedores.Where(p => p.Protocolo is not ProtocoloCat.YaesuAscii and not ProtocoloCat.YaesuBinario))
            {
                aviso?.Report(Textos.F("Servicios.Radio.BuscandoProtocolo", proveedor.Protocolo));
                var encontrado = await BuscarConAsync(proveedor, null, ajustes, registro, ct).ConfigureAwait(false);
                if (encontrado?.Modelo is not null)
                {
                    anotador.LogInformation("Encontrado: {Equipo}.", encontrado.Descripcion);
                    return encontrado;
                }
            }
        }

        return null;
    }

    /// <summary>Crea el control para un equipo encontrado.</summary>
    /// <param name="encontrado">Lo encontrado.</param>
    /// <param name="ajustes">Ajustes de radio (espera, via del PTT...).</param>
    /// <param name="registro">Donde anotar.</param>
    /// <returns>El control, sin conectar.</returns>
    public static IControlEquipo CrearPara(EquipoIdentificado encontrado, OpcionesDeRadio ajustes, ILogger? registro)
    {
        ArgumentNullException.ThrowIfNull(encontrado);
        ArgumentNullException.ThrowIfNull(ajustes);
        if (encontrado.Protocolo == ProtocoloCat.YaesuAscii)
        {
            var perfil = PerfilesYaesu.De(encontrado.Modelo) ?? PerfilesYaesu.PorIdentificador(encontrado.Identificacion);
            return Ft710(ajustes, encontrado.Puerto, encontrado.Baudios, registro, perfil);
        }

        var modelo = encontrado.Modelo ?? CatalogoDeModelos.Buscar(ajustes.Modelo)
            ?? throw new InvalidOperationException(Textos.F("Servicios.Radio.FueraDelCatalogo", encontrado.Protocolo));
        var proveedor = CatalogoDeModelos.ProveedorDe(modelo)
            ?? throw new InvalidOperationException(Textos.F("Servicios.Radio.SinControl", modelo.NombreCompleto));
        return proveedor.Crear(modelo, encontrado.Puerto, encontrado.Baudios, ajustes, registro);
    }

    private static EquipoIdentificado Yaesu(EquipoEncontrado equipo, PerfilYaesu? elegido) =>
        new(equipo.Puerto, equipo.Baudios, ProtocoloCat.YaesuAscii, equipo.Identificador,
            (elegido ?? PerfilesYaesu.PorIdentificador(equipo.Identificador))?.Modelo);

    private static async Task<EquipoIdentificado?> BuscarConAsync(
        IProveedorDeModelos proveedor,
        ModeloDeEquipo? modelo,
        OpcionesDeRadio ajustes,
        ILogger? registro,
        CancellationToken ct)
    {
        var velocidades = modelo?.Velocidades ?? [];
        foreach (var puerto in SerialPort.GetPortNames().Distinct(StringComparer.OrdinalIgnoreCase).Order())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var encontrado = await proveedor.IdentificarAsync(puerto, velocidades, ajustes, registro, ct).ConfigureAwait(false);
                if (encontrado is not null && (modelo is null || encontrado.Modelo is null || encontrado.Modelo == modelo))
                {
                    return encontrado;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                (registro ?? NullLogger.Instance).LogDebug(ex, "{Puerto}: fallo al identificar por {Protocolo}.", puerto, proveedor.Protocolo);
            }
        }

        return null;
    }

    private static ControlFt710 Ft710(OpcionesDeRadio ajustes, string puerto, int baudios, ILogger? registro, PerfilYaesu? perfil)
    {
        var canal = new CanalSerieCat(
            puerto,
            baudios,
            ajustes.Ft710.EsperaDeOrden,
            registro,
            ajustes.Ft710.ViaDePtt);
        return new ControlFt710(canal, ajustes.Ft710, registro, perfil);
    }
}
