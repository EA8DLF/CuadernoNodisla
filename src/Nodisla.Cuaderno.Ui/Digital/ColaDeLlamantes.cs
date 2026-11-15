using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Ui.Digital;

/// <summary>Alguien que ha contestado al CQ y espera a que se le atienda.</summary>
public sealed class LlamanteEnEspera
{
    /// <summary>Su indicativo.</summary>
    public required string Indicativo { get; init; }

    /// <summary>Su localizador, si lo ha dicho.</summary>
    public string Localizador { get; set; } = string.Empty;

    /// <summary>Con cuanta señal se le oyo la ultima vez: es el informe que se le mandara.</summary>
    public int Decibelios { get; set; }

    /// <summary>En que tono se le oyo la ultima vez, en hercios.</summary>
    public int TonoHz { get; set; }

    /// <summary>Ventana en la que se le oyo llamar por ultima vez.</summary>
    public DateTimeOffset OidoEn { get; set; }

    /// <summary>Ya estaba en el cuaderno: «AnsB4» lo salta al elegir solo.</summary>
    public bool TrabajadoAntes { get; set; }

    /// <summary>El informe con el que abrio, si en vez de con el localizador llamo con el informe.</summary>
    public int? InformeRecibido { get; set; }

    /// <summary>El intercambio con el que abrio, si lo hubo.</summary>
    public string IntercambioRecibido { get; set; } = string.Empty;

    /// <summary>Abrio con el informe: se le contesta con Tx3 (R+informe) y no con Tx2.</summary>
    public bool AbrioConInforme => InformeRecibido is not null || IntercambioRecibido.Length > 0;
}

/// <summary>
/// La lista de espera de quien llama CQ: los que han contestado y aun no se les ha atendido, por
/// orden de llegada.
/// </summary>
/// <remarks>
/// <para>
/// Solo guarda y ordena. Quien entra, quien sale por irse con otro y a quien le toca lo decide
/// <see cref="SecuenciadorDeQso"/>, que es quien oye las ventanas; aqui no hay ni radio ni reloj.
/// </para>
/// <para>
/// <b>Sin duplicados y sin colarse.</b> Quien vuelve a llamar conserva su sitio y solo se le
/// ponen al dia la señal, el tono y la hora.
/// </para>
/// </remarks>
public sealed class ColaDeLlamantes
{
    /// <summary>
    /// Ventanas suyas seguidas sin oirle tras las que se da a un llamante por ido. Tres: se le
    /// perdonan dos decodificaciones perdidas (QSB, alguien encima), pero a la tercera lo normal
    /// es que se haya ido a otra cosa, y contestar a quien ya no escucha son varias emisiones
    /// tiradas. En FT8 son 90 segundos; en FT4, 45.
    /// </summary>
    public const int VentanasSinOirloDeFabrica = 3;

    /// <summary>Cuantos caben como mucho. Con mas de estos esperando, el ultimo no iba a llegar a tiempo.</summary>
    public const int Maximo = 20;

    private readonly List<LlamanteEnEspera> _enEspera = [];

    /// <summary>Ventanas suyas seguidas sin oirle tras las que un llamante sale de la lista.</summary>
    public int VentanasSinOirlo { get; set; } = VentanasSinOirloDeFabrica;

    /// <summary>Los que esperan, por orden de turno.</summary>
    public IReadOnlyList<LlamanteEnEspera> EnEspera => _enEspera;

    /// <summary>Salta cada vez que la lista cambia: entra, sale o se pone al dia alguien.</summary>
    public event EventHandler? Cambiada;

    /// <summary>Quien espera con ese indicativo, o nulo.</summary>
    public LlamanteEnEspera? Buscar(string indicativo) =>
        _enEspera.FirstOrDefault(l => string.Equals(l.Indicativo, indicativo, StringComparison.Ordinal));

    /// <summary>
    /// Apunta una llamada: si ya estaba, le pone los datos al dia sin moverlo de sitio; si no,
    /// entra el ultimo.
    /// </summary>
    /// <param name="oido">Su llamada: con localizador o con informe.</param>
    /// <param name="ventana">Ventana en la que se le ha oido.</param>
    public void Anotar(MensajeOido oido, DateTimeOffset ventana)
    {
        var m = oido.Mensaje;
        if (m.Llamante.Length == 0) return;

        var llamante = Buscar(m.Llamante);
        if (llamante is null)
        {
            if (_enEspera.Count >= Maximo) return;
            llamante = new LlamanteEnEspera { Indicativo = m.Llamante };
            _enEspera.Add(llamante);
        }

        if (m.Locator.Length > 0) llamante.Localizador = m.Locator;
        llamante.Decibelios = oido.Decibelios;
        llamante.TonoHz = oido.TonoHz;
        llamante.OidoEn = ventana;
        llamante.TrabajadoAntes = oido.TrabajadoAntes;

        if (m.Clase == ClaseDeMensaje.Informe)
        {
            llamante.InformeRecibido = m.Informe;
            llamante.IntercambioRecibido = m.Intercambio;
        }
        else
        {
            llamante.InformeRecibido = null;
            llamante.IntercambioRecibido = string.Empty;
        }

        Cambiada?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Saca a uno de la lista. Dice si estaba.</summary>
    public bool Quitar(string indicativo)
    {
        var quitados = _enEspera.RemoveAll(l => string.Equals(l.Indicativo, indicativo, StringComparison.Ordinal));
        if (quitados > 0) Cambiada?.Invoke(this, EventArgs.Empty);
        return quitados > 0;
    }

    /// <summary>Vacia la lista.</summary>
    public void Vaciar()
    {
        if (_enEspera.Count == 0) return;
        _enEspera.Clear();
        Cambiada?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Saca a quien lleve <see cref="VentanasSinOirlo"/> ventanas suyas seguidas sin oirse. Las
    /// suyas son una de cada dos: las otras son las propias.
    /// </summary>
    /// <param name="ventana">La ventana suya que acaba de cerrar.</param>
    /// <param name="periodo">Lo que dura una ventana del modo en curso.</param>
    public void Caducar(DateTimeOffset ventana, TimeSpan periodo)
    {
        var tope = 2 * Math.Max(1, VentanasSinOirlo) * periodo;
        var quitados = _enEspera.RemoveAll(l => ventana - l.OidoEn >= tope);
        if (quitados > 0) Cambiada?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A quien le toca: el primero de la lista, saltando a los ya trabajados si se pide.</summary>
    /// <param name="saltarTrabajados">«AnsB4»: no elegir a quien ya esta en el cuaderno.</param>
    public LlamanteEnEspera? Siguiente(bool saltarTrabajados) =>
        _enEspera.FirstOrDefault(l => !saltarTrabajados || !l.TrabajadoAntes);
}

/// <summary>Comparar indicativos sabiendo que <c>EA8DLF</c> y <c>EA8DLF/P</c> son la misma estacion.</summary>
public static class MismoIndicativo
{
    /// <summary>
    /// Los dos indicativos son de la misma estacion: iguales sin mirar mayusculas, o con la misma
    /// parte principal una vez quitados los sufijos y prefijos de operacion
    /// (<see cref="Indicativo.Base"/>).
    /// </summary>
    public static bool Son(string uno, string otro)
    {
        if (string.IsNullOrWhiteSpace(uno) || string.IsNullOrWhiteSpace(otro)) return false;

        uno = uno.Trim().Trim('<', '>');
        otro = otro.Trim().Trim('<', '>');
        if (string.Equals(uno, otro, StringComparison.OrdinalIgnoreCase)) return true;

        return Indicativo.TryParse(uno, out var a) && Indicativo.TryParse(otro, out var b)
               && string.Equals(a.Base, b.Base, StringComparison.OrdinalIgnoreCase);
    }
}
