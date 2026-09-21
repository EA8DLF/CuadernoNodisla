using System.Xml;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Adif;

/// <summary>
/// Lectura del formato ADX, la variante XML de ADIF.
/// </summary>
/// <remarks>
/// ADX guarda cada campo en un elemento con su propio nombre, salvo los campos de aplicacion,
/// que van en elementos <c>APP</c> con el programa y el campo como atributos. Al leerlos se
/// reconstruye el nombre clasico <c>APP_PROGRAMA_CAMPO</c>, de modo que a partir de ahi ADI y
/// ADX son indistinguibles para el resto del codigo.
/// </remarks>
internal static class AnalizadorAdx
{
    /// <summary>Lee un documento ADX completo.</summary>
    public static async Task<LecturaAdif> LeerAsync(Stream origen, CancellationToken ct)
    {
        var qsos = new List<Qso>();
        var avisos = new List<AvisoAdif>();
        var cabecera = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var numero = 0;

        var ajustes = new XmlReaderSettings
        {
            Async = true,
            IgnoreComments = true,
            IgnoreWhitespace = true,
            IgnoreProcessingInstructions = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
        };

        try
        {
            using var lector = XmlReader.Create(origen, ajustes);
            while (await lector.ReadAsync().ConfigureAwait(false))
            {
                ct.ThrowIfCancellationRequested();
                if (lector.NodeType != XmlNodeType.Element) continue;

                if (lector.Name.Equals("HEADER", StringComparison.OrdinalIgnoreCase))
                {
                    await LeerCamposAsync(lector, c => cabecera[c.Nombre] = c.Valor).ConfigureAwait(false);
                }
                else if (lector.Name.Equals("RECORD", StringComparison.OrdinalIgnoreCase))
                {
                    var campos = new List<CampoAdif>();
                    await LeerCamposAsync(lector, campos.Add).ConfigureAwait(false);
                    numero++;
                    if (campos.Count == 0)
                    {
                        avisos.Add(new AvisoAdif(numero, null, "Registro vacio; se descarta.", NivelDeAviso.Error));
                        continue;
                    }
                    qsos.Add(MapeoAdif.LeerContacto(campos, numero, avisos));
                }
            }
        }
        catch (XmlException ex)
        {
            avisos.Add(new AvisoAdif(
                numero + 1, null,
                $"El fichero ADX esta mal formado y se deja de leer aqui: {ex.Message}",
                NivelDeAviso.Error));
        }

        LectorAdif.MarcarOrigen(qsos, cabecera);
        return new LecturaAdif { Qsos = qsos, Avisos = avisos, Cabecera = cabecera };
    }

    /// <summary>Recorre los elementos hijos del contenedor en el que esta posicionado el lector.</summary>
    private static async Task LeerCamposAsync(XmlReader lector, Action<CampoAdif> anadir)
    {
        if (lector.IsEmptyElement) return;
        var profundidad = lector.Depth;
        var avanzar = true;

        while (true)
        {
            if (avanzar && !await lector.ReadAsync().ConfigureAwait(false)) return;
            avanzar = true;

            if (lector.NodeType == XmlNodeType.EndElement && lector.Depth == profundidad) return;
            if (lector.NodeType != XmlNodeType.Element) continue;

            var nombre = lector.Name;
            var tipo = lector.GetAttribute("TYPE");

            if (nombre.Equals("APP", StringComparison.OrdinalIgnoreCase))
            {
                var programa = lector.GetAttribute("PROGRAMID") ?? "DESCONOCIDO";
                var campo = lector.GetAttribute("FIELDNAME") ?? string.Empty;
                nombre = $"APP_{programa}_{campo}";
            }
            else if (nombre.Equals("USERDEF", StringComparison.OrdinalIgnoreCase)
                     && lector.GetAttribute("FIELDNAME") is { Length: > 0 } definido)
            {
                nombre = definido;
            }

            string valor;
            if (lector.IsEmptyElement)
            {
                valor = string.Empty;
            }
            else
            {
                valor = await lector.ReadElementContentAsStringAsync().ConfigureAwait(false);
                avanzar = false;
            }

            anadir(new CampoAdif(nombre.ToUpperInvariant(), valor, tipo));
        }
    }
}
