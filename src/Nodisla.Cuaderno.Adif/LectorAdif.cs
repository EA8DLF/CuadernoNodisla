using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Adif;

/// <summary>
/// Lector de ficheros ADIF 3.1.5, en sus dos formatos: ADI (texto) y ADX (XML).
/// </summary>
/// <remarks>
/// El formato se deduce mirando los primeros bytes, asi que el operador no tiene que declarar
/// nada ni fiarse de la extension del fichero. Ningun registro estropeado interrumpe la
/// importacion: se anota el aviso y se pasa al siguiente.
/// </remarks>
public sealed class LectorAdif : ILectorAdif
{
    /// <summary>Bytes que se miran para decidir si el fichero es XML.</summary>
    private const int BytesParaOlfatear = 256;

    /// <summary>Lee un fichero ADI o ADX desde un flujo.</summary>
    public async Task<LecturaAdif> LeerAsync(Stream origen, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(origen);

        var prefijo = new byte[BytesParaOlfatear];
        var leidos = 0;
        while (leidos < prefijo.Length)
        {
            var n = await origen.ReadAsync(prefijo.AsMemory(leidos), ct).ConfigureAwait(false);
            if (n == 0) break;
            leidos += n;
        }

        var flujo = new FlujoPrefijado(prefijo, leidos, origen);
        return EsXml(prefijo.AsSpan(0, leidos))
            ? await AnalizadorAdx.LeerAsync(flujo, ct).ConfigureAwait(false)
            : await LeerAdiAsync(flujo, ct).ConfigureAwait(false);
    }

    /// <summary>Indica si lo que empieza el fichero es un documento XML y no un ADI.</summary>
    internal static bool EsXml(ReadOnlySpan<byte> prefijo)
    {
        var texto = TextoAdif.Ascii(prefijo).TrimStart('﻿', 'ï', '»', '¿', ' ', '\t', '\r', '\n');
        return texto.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase)
            || texto.StartsWith("<ADX", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<LecturaAdif> LeerAdiAsync(Stream flujo, CancellationToken ct)
    {
        var qsos = new List<Qso>();
        var avisos = new List<AvisoAdif>();
        var cabecera = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var pendientes = new List<CampoAdif>();
        var cabeceraCerrada = false;
        var numero = 0;

        var analizador = new AnalizadorAdi(
            flujo,
            (mensaje, campo) => avisos.Add(new AvisoAdif(cabeceraCerrada ? numero + 1 : 0, campo, mensaje, false)));

        while (await analizador.SiguienteAsync(ct).ConfigureAwait(false) is { } token)
        {
            switch (token.Clase)
            {
                case ClaseDeToken.Campo:
                    pendientes.Add(token.Campo);
                    break;

                case ClaseDeToken.FinDeCabecera:
                    foreach (var c in pendientes) cabecera[c.Nombre] = c.Valor;
                    pendientes.Clear();
                    cabeceraCerrada = true;
                    break;

                case ClaseDeToken.FinDeRegistro:
                    cabeceraCerrada = true;
                    numero++;
                    AnadirRegistro(qsos, pendientes, numero, avisos);
                    pendientes.Clear();
                    break;

                default:
                    break;
            }
        }

        if (pendientes.Count > 0)
        {
            numero++;
            avisos.Add(new AvisoAdif(
                numero, null,
                "El fichero termina con un registro sin cerrar; se importa igualmente con lo que trae.",
                false));
            AnadirRegistro(qsos, pendientes, numero, avisos);
        }

        if (!cabeceraCerrada && qsos.Count == 0 && avisos.Count == 0)
        {
            avisos.Add(new AvisoAdif(0, null, "El fichero no contiene ningun contacto.", false));
        }

        MarcarOrigen(qsos, cabecera);
        return new LecturaAdif { Qsos = qsos, Avisos = avisos, Cabecera = cabecera };
    }

    private static void AnadirRegistro(
        List<Qso> qsos, List<CampoAdif> campos, int numero, List<AvisoAdif> avisos)
    {
        if (campos.Count == 0)
        {
            avisos.Add(new AvisoAdif(numero, null, "Registro vacio; se descarta.", true));
            return;
        }

        try
        {
            qsos.Add(MapeoAdif.LeerContacto(campos, numero, avisos));
        }
#pragma warning disable CA1031 // Un registro estropeado no puede tumbar la importacion entera.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            avisos.Add(new AvisoAdif(
                numero, null,
                $"No se ha podido leer el registro y se descarta: {ex.Message}",
                true));
        }
    }

    /// <summary>Anota de donde vino cada contacto, para que el operador lo vea en la ficha.</summary>
    internal static void MarcarOrigen(List<Qso> qsos, Dictionary<string, string> cabecera)
    {
        var programa = cabecera.TryGetValue("PROGRAMID", out var p) && !string.IsNullOrWhiteSpace(p)
            ? p.Trim()
            : null;
        var origen = programa is null ? "ADIF" : $"ADIF ({programa})";
        foreach (var q in qsos) q.Origen ??= origen;
    }
}
