using Microsoft.Extensions.Logging;
using System.Reflection;
using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Radio.Ptt;
using Nodisla.Cuaderno.Radio.Pruebas.Dobles;

namespace Nodisla.Cuaderno.Radio.Pruebas;

/// <summary>
/// Lo que pasa con el PTT cuando la aplicacion se cierra, incluido el cierre por fallo.
/// </summary>
/// <remarks>
/// Matar el proceso de las pruebas para comprobarlo no serviria de nada —nadie veria el
/// resultado—, asi que se hace lo siguiente mejor: se comprueba que el vigilante se engancha de
/// verdad a <see cref="AppDomain.ProcessExit"/> y a
/// <see cref="AppDomain.UnhandledException"/>, se saca de ahi el manejador enganchado y se
/// ejecuta. Es el mismo codigo, linea por linea, que correria el runtime al cerrarse.
/// </remarks>
public class CierreDelProcesoPruebas
{
    private static OpcionesDelVigilante OpcionesConCierre() => new()
    {
        TiempoMaximo = TimeSpan.FromSeconds(30),
        TiempoSinLatido = TimeSpan.FromSeconds(30),
        PasoDeVigilancia = TimeSpan.FromMilliseconds(10),
        EsperaDeSuelta = TimeSpan.FromMilliseconds(300),
        EngancharseAlCierreDelProceso = true,
    };

    /// <summary>
    /// Saca los manejadores enganchados a un evento del proceso.
    /// </summary>
    /// <remarks>
    /// Se busca en <see cref="AppDomain"/> y en <see cref="AppContext"/> porque el runtime ha
    /// movido de sitio el campo que guarda la lista mas de una vez; lo que se comprueba es que
    /// el manejador del vigilante esta de verdad enganchado, no donde lo guarda .NET.
    /// </remarks>
    private static Delegate[] ManejadoresDe(string nombre, Type tipoDelDelegado)
    {
        foreach (var tipo in new[] { typeof(AppDomain), typeof(AppContext) })
        {
            var campos = tipo.GetFields(
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

            foreach (var campo in campos)
            {
                if (!tipoDelDelegado.IsAssignableFrom(campo.FieldType)
                    || !campo.Name.Replace("_", string.Empty, StringComparison.Ordinal)
                        .Contains(nombre, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var valor = (Delegate?)campo.GetValue(campo.IsStatic ? null : AppDomain.CurrentDomain);
                return valor?.GetInvocationList() ?? [];
            }
        }

        throw new InvalidOperationException(
            $"No se encuentra dónde guarda .NET los manejadores de {nombre}. El vigilante se engancha a ese "
            + "evento: si el runtime lo ha movido, hay que arreglar esta prueba, no quitarla.");
    }

    private static Delegate[] ManejadoresDelCierre() =>
        ManejadoresDe("processExit", typeof(EventHandler));

    private static Delegate[] ManejadoresDelFallo() =>
        ManejadoresDe("unhandledException", typeof(UnhandledExceptionEventHandler));

    [Fact]
    public async Task El_vigilante_se_engancha_al_cierre_del_proceso()
    {
        var equipo = new ControlDeMentira();
        var vigilante = new VigilantePtt(equipo, OpcionesConCierre());

        try
        {
            ManejadoresDelCierre().Should()
                .Contain(manejador => ReferenceEquals(manejador.Target, vigilante));
            ManejadoresDelFallo().Should()
                .Contain(manejador => ReferenceEquals(manejador.Target, vigilante));
        }
        finally
        {
            await vigilante.DisposeAsync();
        }

        // Y al liberarlo se desengancha: si no, cada vigilante dejaría basura en el proceso.
        ManejadoresDelCierre().Should()
            .NotContain(manejador => ReferenceEquals(manejador.Target, vigilante));
    }

    [Fact]
    public async Task El_manejador_del_cierre_baja_el_ptt()
    {
        var equipo = new ControlDeMentira();
        var vigilante = new VigilantePtt(equipo, OpcionesConCierre());
        var sueltas = new EsperaDeSueltas(vigilante);

        try
        {
            var transmision = await vigilante.PedirAntenaAsync("una transmisión que pilla el cierre");
            equipo.PttArriba.Should().BeTrue();

            var manejador = ManejadoresDelCierre()
                .First(candidato => ReferenceEquals(candidato.Target, vigilante));

            // Esto es exactamente lo que hace el runtime al cerrarse el proceso.
            manejador.DynamicInvoke(null, EventArgs.Empty);

            equipo.PttArriba.Should().BeFalse("el proceso no se puede ir con el equipo en antena");
            (await sueltas.PrimeraAsync(TimeSpan.FromSeconds(10))).Should().Be(MotivoDeSuelta.Cierre);
            vigilante.EnAntena.Should().BeFalse();
            await transmision.DisposeAsync();
        }
        finally
        {
            await vigilante.DisposeAsync();
        }
    }

    [Fact]
    public async Task En_el_cierre_se_baja_el_ptt_aunque_la_via_normal_este_rota()
    {
        var equipo = new ControlDeMentira();
        var registro = new RegistroDeMentira();
        var vigilante = new VigilantePtt(equipo, OpcionesConCierre(), registro);

        try
        {
            var transmision = await vigilante.PedirAntenaAsync("prueba");

            // El canal con el equipo se rompe justo cuando la aplicación se está cerrando.
            equipo.FallaLaViaNormal = true;

            var manejador = ManejadoresDelCierre()
                .First(candidato => ReferenceEquals(candidato.Target, vigilante));
            manejador.DynamicInvoke(null, EventArgs.Empty);

            equipo.PttArriba.Should().BeFalse();
            equipo.BajadasPorEmergencia.Should().BeGreaterThan(0, "hay que probar todas las vías");
            await transmision.DisposeAsync();
        }
        finally
        {
            await vigilante.DisposeAsync();
        }
    }

    [Fact]
    public async Task Si_todo_lo_demas_falla_en_el_cierre_queda_la_via_sincrona()
    {
        var equipo = new ControlDeMentira();
        var registro = new RegistroDeMentira();
        var vigilante = new VigilantePtt(equipo, OpcionesConCierre(), registro);

        try
        {
            var transmision = await vigilante.PedirAntenaAsync("prueba");

            // A partir de aquí toda orden tarda cinco segundos: en el cierre del proceso no hay
            // tanto tiempo, así que la suelta asíncrona no llega y entra la vía síncrona.
            equipo.Tardanza = TimeSpan.FromSeconds(5);
            var manejador = ManejadoresDelCierre()
                .First(candidato => ReferenceEquals(candidato.Target, vigilante));
            manejador.DynamicInvoke(null, EventArgs.Empty);

            equipo.BajadasSincronas.Should().BeGreaterThan(0, "en el cierre hay que soltar sin esperar a nadie");
            equipo.PttArriba.Should().BeFalse();
            registro.De(LogLevel.Error).Should().NotBeEmpty("esto tiene que quedar anotado");

            _ = transmision;
        }
        finally
        {
            equipo.Tardanza = TimeSpan.Zero;
            await vigilante.DisposeAsync();
        }
    }

    [Fact]
    public async Task El_manejador_de_excepciones_no_atendidas_baja_el_ptt()
    {
        var equipo = new ControlDeMentira();
        var vigilante = new VigilantePtt(equipo, OpcionesConCierre());

        try
        {
            var transmision = await vigilante.PedirAntenaAsync("prueba");

            var manejador = ManejadoresDelFallo()
                .First(candidato => ReferenceEquals(candidato.Target, vigilante));
            manejador.DynamicInvoke(
                null,
                new UnhandledExceptionEventArgs(new InvalidOperationException("se cayó todo"), isTerminating: true));

            equipo.PttArriba.Should().BeFalse("ni siquiera un cierre por fallo puede dejar el equipo en antena");
            await transmision.DisposeAsync();
        }
        finally
        {
            await vigilante.DisposeAsync();
        }
    }
}
