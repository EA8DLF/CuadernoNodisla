using CommunityToolkit.Mvvm.ComponentModel;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Ui.Ajustes;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>Una opción del desplegable de idioma: uno de los seis, o «el del sistema».</summary>
public sealed class OpcionDeIdioma : ObservableObject
{
    /// <summary>Monta la opción.</summary>
    /// <param name="codigo">Código de dos letras, o nulo para «el del sistema».</param>
    public OpcionDeIdioma(string? codigo)
    {
        Codigo = codigo;
        Textos.AlCambiar(this, static o => o.OnPropertyChanged(nameof(Nombre)));
    }

    /// <summary>Código de dos letras, o nulo para «el del sistema».</summary>
    public string? Codigo { get; }

    /// <summary>
    /// Lo que se ve: cada idioma en su propio idioma («Deutsch»), que es como lo busca quien lo
    /// habla aunque el programa esté en otro; y «el del sistema», en el idioma en uso.
    /// </summary>
    public string Nombre => Codigo is null
        ? Textos.F("Ajustes.Idioma.DelSistema", NombreDe(Textos.IdiomaDelSistema()))
        : NombreDe(Codigo);

    private static string NombreDe(string codigo) =>
        Textos.Idiomas.FirstOrDefault(i => i.Codigo == codigo)?.Nombre ?? codigo;

    /// <inheritdoc />
    public override string ToString() => Nombre;
}

/// <summary>
/// Apartado «Idioma» de la configuración: elegir el idioma lo cambia al momento y lo guarda.
/// </summary>
public sealed partial class VistaModeloIdioma : ObservableObject
{
    private readonly AjustesDelPrograma _ajustes;
    private readonly string? _carpeta;

    /// <summary>Monta el apartado con lo guardado en los ajustes.</summary>
    /// <param name="ajustes">Ajustes del programa, donde se guarda el idioma.</param>
    /// <param name="carpeta">Carpeta donde guardarlos; nula para no escribir nada (simulado y pruebas).</param>
    public VistaModeloIdioma(AjustesDelPrograma ajustes, string? carpeta)
    {
        _ajustes = ajustes ?? throw new ArgumentNullException(nameof(ajustes));
        _carpeta = carpeta;
        Opciones = [new OpcionDeIdioma(null), .. Textos.Idiomas.Select(i => new OpcionDeIdioma(i.Codigo))];

        var guardado = string.IsNullOrWhiteSpace(ajustes.Idioma) ? null : Textos.Resolver(ajustes.Idioma);
        _elegida = Opciones.First(o => o.Codigo == guardado);
    }

    /// <summary>Las siete opciones: el del sistema y los seis idiomas.</summary>
    public IReadOnlyList<OpcionDeIdioma> Opciones { get; }

    /// <summary>La opción elegida. Cambiarla cambia el idioma en el acto y lo guarda.</summary>
    [ObservableProperty]
    private OpcionDeIdioma _elegida;

    partial void OnElegidaChanged(OpcionDeIdioma value)
    {
        if (value is null) return;
        _ajustes.Idioma = value.Codigo;
        if (_carpeta is not null) _ajustes.Guardar(_carpeta);
        Textos.Cambiar(value.Codigo);
    }
}
