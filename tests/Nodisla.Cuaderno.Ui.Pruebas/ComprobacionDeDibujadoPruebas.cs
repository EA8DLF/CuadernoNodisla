using FluentAssertions;
using Nodisla.Cuaderno.Ui.Recursos;
using Serilog.Events;
using Xunit;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// La segunda comprobacion del dibujado no es motivo de error en el registro.
/// </summary>
/// <remarks>
/// Se hace nada mas esconder y volver a mostrar la ventana, cuando el dibujado por software
/// todavia no ha pintado: en los registros sale «EnBlanco» a los 50-120 ms en 2 de 61
/// arranques, y en los dos la ventana se pinto bien despues (en uno hay incluso un retrato de
/// la ventana guardado seis segundos mas tarde). El programa no hace nada con ese resultado,
/// asi que apuntarlo como error solo ensucia el registro con falsos positivos.
/// </remarks>
public class ComprobacionDeDibujadoPruebas
{
    [Theory]
    [InlineData(EstadoDelDibujado.EnBlanco)]
    [InlineData(EstadoDelDibujado.NoSeHaPodidoComprobar)]
    public void Si_tras_pasar_a_software_no_se_confirma_el_pintado_es_un_aviso_y_no_un_error(EstadoDelDibujado segunda) =>
        ComprobacionDeDibujado.NivelTrasPasarASoftware(segunda).Should().Be(LogEventLevel.Warning);

    [Fact]
    public void Si_tras_pasar_a_software_ya_pinta_es_informacion() =>
        ComprobacionDeDibujado.NivelTrasPasarASoftware(EstadoDelDibujado.Correcto)
            .Should().Be(LogEventLevel.Information);
}
