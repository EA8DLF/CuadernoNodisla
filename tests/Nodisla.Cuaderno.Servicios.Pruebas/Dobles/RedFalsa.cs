using System.Net;

namespace Nodisla.Cuaderno.Servicios.Pruebas.Dobles;

/// <summary>
/// Servidor de mentira: responde lo que se le diga sin salir de la maquina.
/// </summary>
/// <remarks>
/// Ninguna prueba de este proyecto toca la red. Si la ARRL esta caida, o el portatil esta en
/// un monte sin cobertura, las pruebas tienen que seguir diciendo la verdad.
/// </remarks>
public sealed class ManejadorFalso : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, string?, HttpResponseMessage> _responder;

    /// <summary>Peticiones recibidas, en orden.</summary>
    public List<Uri> Direcciones { get; } = [];

    /// <summary>Cuerpos recibidos, en orden. Nulo cuando la peticion no llevaba cuerpo.</summary>
    public List<string?> Cuerpos { get; } = [];

    public ManejadorFalso(Func<HttpRequestMessage, string?, HttpResponseMessage> responder) =>
        _responder = responder;

    /// <summary>Responde siempre lo mismo, con codigo 200.</summary>
    public static ManejadorFalso ConTexto(string texto) =>
        new((_, _) => Respuesta(texto));

    /// <summary>Responde por turnos: la primera llamada lo primero, y asi sucesivamente.</summary>
    public static ManejadorFalso PorTurnos(params string[] textos)
    {
        var turno = 0;
        return new ManejadorFalso((_, _) =>
        {
            var texto = textos[Math.Min(turno, textos.Length - 1)];
            turno++;
            return Respuesta(texto);
        });
    }

    /// <summary>Respuesta con cuerpo de texto y el codigo indicado.</summary>
    public static HttpResponseMessage Respuesta(string texto, HttpStatusCode codigo = HttpStatusCode.OK) =>
        new(codigo) { Content = new StringContent(texto) };

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage peticion, CancellationToken cancellationToken)
    {
        Direcciones.Add(peticion.RequestUri!);
        string? cuerpo = null;
        if (peticion.Content is not null)
        {
            cuerpo = await peticion.Content.ReadAsStringAsync(cancellationToken);
        }
        Cuerpos.Add(cuerpo);
        return _responder(peticion, cuerpo);
    }
}

/// <summary>
/// Recoge los avisos de progreso en el mismo hilo.
/// </summary>
/// <remarks>
/// No se usa <see cref="Progress{T}"/> a proposito: ese vuelca en el contexto de sincronizacion
/// y en una prueba llega tarde o no llega, con lo que la comprobacion pasaria sin comprobar nada.
/// </remarks>
public sealed class ProgresoDeMentira : IProgress<Nodisla.Cuaderno.Aplicacion.Puertos.ProgresoDeSincronizacion>
{
    public List<Nodisla.Cuaderno.Aplicacion.Puertos.ProgresoDeSincronizacion> Avisos { get; } = [];

    public void Report(Nodisla.Cuaderno.Aplicacion.Puertos.ProgresoDeSincronizacion value) =>
        Avisos.Add(value);
}

/// <summary>Fabrica que siempre entrega el mismo cliente sobre un manejador de mentira.</summary>
public sealed class FabricaFalsa : IHttpClientFactory
{
    private readonly HttpMessageHandler _manejador;

    public FabricaFalsa(HttpMessageHandler manejador) => _manejador = manejador;

    /// <summary>Cuantos clientes se han pedido, para comprobar que se reutiliza la fabrica.</summary>
    public int Creados { get; private set; }

    public HttpClient CreateClient(string name)
    {
        Creados++;
        return new HttpClient(_manejador, disposeHandler: false)
        {
            BaseAddress = new Uri("https://ejemplo.invalido/"),
        };
    }
}
