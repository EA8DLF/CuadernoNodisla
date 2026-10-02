using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Telegrafia;

/// <summary>
/// Saca palabras terminadas del texto que va escribiendo el decodificador de telegrafia.
/// </summary>
/// <remarks>
/// <para>
/// Se engancha a la lista publica de palabras de la linea principal
/// (<see cref="LineaCw.Palabras"/>) y no toca el motor. El decodificador escribe la ultima
/// palabra letra a letra (la va reemplazando); una palabra esta terminada cuando empieza la
/// siguiente, o cuando hay silencio (<see cref="Cerrar"/>).
/// </para>
/// <para>
/// Lo que se lee mientras se transmite es el propio tono de escucha: <see cref="Saltar"/> lo da
/// por leido sin pasarlo.
/// </para>
/// </remarks>
public sealed class LectorDePalabrasCw : IDisposable
{
    private readonly ObservableCollection<PalabraCw> _palabras;
    private int _leidas;

    /// <summary>Se engancha a la lista de palabras.</summary>
    /// <param name="palabras">Las palabras de la linea principal del decodificador.</param>
    public LectorDePalabrasCw(ObservableCollection<PalabraCw> palabras)
    {
        ArgumentNullException.ThrowIfNull(palabras);
        _palabras = palabras;
        _leidas = palabras.Count;
        _palabras.CollectionChanged += AlCambiar;
    }

    /// <summary>Una palabra terminada.</summary>
    public event EventHandler<string>? PalabraLeida;

    /// <summary>Cuando llego la ultima novedad del decodificador.</summary>
    public DateTimeOffset UltimaNovedad { get; private set; } = DateTimeOffset.MinValue;

    /// <summary>Hay una palabra a medias, sin pasar.</summary>
    public bool HayPendiente => _palabras.Count > _leidas;

    /// <summary>Da por leido todo lo que hay, sin pasarlo.</summary>
    public void Saltar() => _leidas = _palabras.Count;

    /// <summary>Silencio: la ultima palabra tambien esta terminada.</summary>
    public void Cerrar()
    {
        while (_leidas < _palabras.Count)
        {
            var texto = _palabras[_leidas].Texto;
            _leidas++;
            PalabraLeida?.Invoke(this, texto);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _palabras.CollectionChanged -= AlCambiar;

    private void AlCambiar(object? origen, NotifyCollectionChangedEventArgs e)
    {
        UltimaNovedad = DateTimeOffset.UtcNow;
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Reset:
                _leidas = 0;
                return;
            case NotifyCollectionChangedAction.Remove when e.OldStartingIndex >= 0:
                if (e.OldStartingIndex < _leidas) _leidas = Math.Max(0, _leidas - (e.OldItems?.Count ?? 1));
                return;
            case NotifyCollectionChangedAction.Add:
                // Todas menos la ultima estan terminadas.
                while (_leidas < _palabras.Count - 1)
                {
                    var texto = _palabras[_leidas].Texto;
                    _leidas++;
                    PalabraLeida?.Invoke(this, texto);
                }

                return;
        }
    }
}
