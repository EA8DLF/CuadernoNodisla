using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>
/// Primer arranque: lo que hay que preguntar antes de dejar registrar nada. Sin indicativo de
/// estacion el cuaderno no vale para ADIF ni para pedir diplomas, asi que no se puede saltar.
/// </summary>
public sealed partial class VistaModeloPrimerArranque(CrearPerfilDeEstacion crearPerfil) : ObservableObject
{
    /// <summary>Se dispara cuando el perfil ya esta creado y se puede empezar a operar.</summary>
    public event EventHandler<Estacion>? PerfilCreado;

    [ObservableProperty]
    private string _indicativo = string.Empty;

    [ObservableProperty]
    private string _localizador = string.Empty;

    [ObservableProperty]
    private string _nombreOperador = string.Empty;

    [ObservableProperty]
    private string _localidad = string.Empty;

    [ObservableProperty]
    private string _nombrePerfil = CrearPerfilDeEstacion.NombrePorOmision;

    [ObservableProperty]
    private string _mensaje = string.Empty;

    [ObservableProperty]
    private bool _hayError;

    /// <summary>Crea el perfil con lo que ha escrito el operador.</summary>
    [RelayCommand]
    private async Task CrearAsync()
    {
        var resultado = await crearPerfil.EjecutarAsync(new PeticionDePerfil
        {
            Indicativo = Indicativo,
            NombrePerfil = NombrePerfil,
            Localizador = Localizador,
            NombreOperador = NombreOperador,
            Localidad = Localidad,
        }).ConfigureAwait(true);

        if (!resultado.Correcto)
        {
            Mensaje = string.Join("  ", resultado.Errores);
            HayError = true;
            return;
        }

        HayError = false;
        Mensaje = string.Empty;
        PerfilCreado?.Invoke(this, resultado.Creado!);
    }
}
