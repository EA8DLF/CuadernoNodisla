using System.Globalization;
using System.Text;
using Nodisla.Cuaderno.Concursos.Catalogo;
using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Concursos.Cabrillo;

/// <summary>Lo que hay que contarle al operador despues de exportar.</summary>
/// <param name="Lineas">Cuantas lineas <c>QSO:</c> se han escrito.</param>
/// <param name="Excluidas">Cuantas se han escrito como <c>X-QSO:</c>.</param>
/// <param name="Avisos">Lo que no cuadraba. Vacio significa que salio limpio.</param>
public sealed record ResultadoCabrillo(int Lineas, int Excluidas, IReadOnlyList<string> Avisos);

/// <summary>
/// Escribe el fichero Cabrillo que pide el organizador.
/// </summary>
/// <remarks>
/// <para>
/// Esta clase se ha escrito contra la especificacion publica de Cabrillo version 3 de la
/// WWROF, comprobando una por una las etiquetas de cabecera y la plantilla de la linea de
/// contacto —<c>QSO: freq mo date time call rst exch call rst exch t</c>—, las cinco
/// abreviaturas de modo admitidas (<c>CW</c>, <c>PH</c>, <c>FM</c>, <c>RY</c>, <c>DG</c>), el
/// uso de <c>X-QSO:</c> para los contactos que no cuentan y la obligacion de que los
/// contactos vayan en orden cronologico.
/// </para>
/// <para>
/// Lo que la especificacion <b>no</b> fija es el intercambio: eso lo pone cada organizador. Por
/// eso el exportador no se inventa columnas: escribe el informe y despues el intercambio tal y
/// como quedo guardado en <c>STX_STRING</c> y <c>SRX_STRING</c>, que es lo que de verdad se
/// dijo por la radio.
/// </para>
/// <para>
/// El fichero sale en <b>ASCII con saltos de linea CRLF</b>. Los robots son viejos y algunos se
/// atragantan con UTF-8 o con finales de linea de Unix; un log rechazado por eso es un concurso
/// perdido por una tonteria.
/// </para>
/// </remarks>
public sealed class EscritorCabrillo
{
    private const string Version = "3.0";

    /// <summary>Escribe el Cabrillo completo en un flujo de texto.</summary>
    /// <param name="destino">Donde se escribe.</param>
    /// <param name="cabecera">Cabecera del log.</param>
    /// <param name="contactos">Contactos del concurso, en cualquier orden.</param>
    /// <param name="excluir">
    /// Que contactos no cuentan. Se escriben igual, pero como <c>X-QSO:</c>, porque al
    /// organizador le sirven para comprobar los logs de los demas.
    /// </param>
    /// <returns>Cuantas lineas salieron y que no cuadraba.</returns>
    public ResultadoCabrillo Escribir(
        TextWriter destino,
        CabeceraCabrillo cabecera,
        IEnumerable<Qso> contactos,
        Func<Qso, bool>? excluir = null)
    {
        ArgumentNullException.ThrowIfNull(destino);
        ArgumentNullException.ThrowIfNull(cabecera);
        ArgumentNullException.ThrowIfNull(contactos);

        // El orden cronologico lo exige la especificacion. Se ordena aqui y no se confia en
        // que venga ordenado: un contacto corregido a mano rompe el orden sin avisar.
        var ordenados = contactos.OrderBy(q => q.InicioUtc).ToList();
        var avisos = new List<string>();

        EscribirCabecera(destino, cabecera, ordenados, avisos);

        var lineas = 0;
        var excluidas = 0;
        foreach (var qso in ordenados)
        {
            var esExcluido = excluir?.Invoke(qso) ?? false;
            destino.Write(esExcluido ? "X-QSO: " : "QSO: ");
            destino.Write(LineaDeContacto(cabecera, qso, avisos));
            destino.Write("\r\n");
            if (esExcluido) excluidas++; else lineas++;
        }

        destino.Write("END-OF-LOG:\r\n");
        return new ResultadoCabrillo(lineas, excluidas, avisos);
    }

    /// <summary>Genera el Cabrillo entero como texto.</summary>
    /// <param name="cabecera">Cabecera del log.</param>
    /// <param name="contactos">Contactos del concurso.</param>
    /// <param name="excluir">Que contactos van como <c>X-QSO:</c>.</param>
    /// <returns>El texto del fichero y el resultado de la exportacion.</returns>
    public (string Texto, ResultadoCabrillo Resultado) Generar(
        CabeceraCabrillo cabecera,
        IEnumerable<Qso> contactos,
        Func<Qso, bool>? excluir = null)
    {
        var texto = new StringWriter(CultureInfo.InvariantCulture);
        var resultado = Escribir(texto, cabecera, contactos, excluir);
        return (texto.ToString(), resultado);
    }

    /// <summary>Guarda el Cabrillo en un fichero, en ASCII y con finales de linea CRLF.</summary>
    /// <param name="ruta">Fichero de destino.</param>
    /// <param name="cabecera">Cabecera del log.</param>
    /// <param name="contactos">Contactos del concurso.</param>
    /// <param name="excluir">Que contactos van como <c>X-QSO:</c>.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>Cuantas lineas salieron y que no cuadraba.</returns>
    public async Task<ResultadoCabrillo> GuardarAsync(
        string ruta,
        CabeceraCabrillo cabecera,
        IEnumerable<Qso> contactos,
        Func<Qso, bool>? excluir = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruta);
        var (texto, resultado) = Generar(cabecera, contactos, excluir);
        // ASCII a proposito: lo que no sea ASCII se sustituye por '?', que es feo pero no
        // rompe el analizador del organizador.
        await File.WriteAllTextAsync(ruta, texto, Encoding.ASCII, ct).ConfigureAwait(false);
        return resultado;
    }

    private static void EscribirCabecera(
        TextWriter destino, CabeceraCabrillo cabecera, IReadOnlyList<Qso> contactos, List<string> avisos)
    {
        Etiqueta(destino, "START-OF-LOG", Version);
        Etiqueta(destino, "CONTEST", Contest(cabecera, avisos));
        Etiqueta(destino, "CALLSIGN", cabecera.Indicativo.Valor);
        Etiqueta(destino, "CATEGORY-OPERATOR", cabecera.Operadores switch
        {
            CategoriaOperador.Multioperador => "MULTI-OP",
            CategoriaOperador.Comprobacion => "CHECKLOG",
            _ => "SINGLE-OP",
        });
        Etiqueta(destino, "CATEGORY-ASSISTED",
            cabecera.Asistencia == CategoriaAsistencia.ConAyuda ? "ASSISTED" : "NON-ASSISTED");
        Etiqueta(destino, "CATEGORY-BAND", cabecera.Banda ?? BandaDeducida(contactos));
        Etiqueta(destino, "CATEGORY-MODE", cabecera.Modo ?? ModoDeducido(contactos));
        Etiqueta(destino, "CATEGORY-POWER", cabecera.Potencia switch
        {
            CategoriaPotencia.Alta => "HIGH",
            CategoriaPotencia.Qrp => "QRP",
            _ => "LOW",
        });
        Etiqueta(destino, "CATEGORY-STATION", cabecera.Estacion switch
        {
            CategoriaEstacion.Movil => "MOBILE",
            CategoriaEstacion.Portable => "PORTABLE",
            CategoriaEstacion.Itinerante => "ROVER",
            CategoriaEstacion.Expedicion => "EXPEDITION",
            CategoriaEstacion.Sede => "HQ",
            CategoriaEstacion.Escuela => "SCHOOL",
            CategoriaEstacion.Distribuida => "DISTRIBUTED",
            _ => "FIXED",
        });
        if (cabecera.Transmisores is { } transmisores)
        {
            Etiqueta(destino, "CATEGORY-TRANSMITTER", transmisores switch
            {
                CategoriaTransmisor.Dos => "TWO",
                CategoriaTransmisor.Limitado => "LIMITED",
                CategoriaTransmisor.Ilimitado => "UNLIMITED",
                CategoriaTransmisor.Escucha => "SWL",
                _ => "ONE",
            });
        }
        else if (cabecera.Operadores == CategoriaOperador.Multioperador)
        {
            avisos.Add("Un log de multioperador necesita CATEGORY-TRANSMITTER y no se ha declarado.");
        }

        Etiqueta(destino, "CATEGORY-TIME", cabecera.Tiempo);
        Etiqueta(destino, "CATEGORY-OVERLAY", cabecera.Superpuesta);
        if (cabecera.PuntuacionReclamada is { } puntuacion)
        {
            // Sin puntos ni comas: lo dice la especificacion y lo exige el analizador.
            Etiqueta(destino, "CLAIMED-SCORE", puntuacion.ToString("0", CultureInfo.InvariantCulture));
        }
        Etiqueta(destino, "CLUB", cabecera.Club);
        Etiqueta(destino, "LOCATION", cabecera.Ubicacion);
        Etiqueta(destino, "CREATED-BY", cabecera.CreadoPor);
        Etiqueta(destino, "NAME", Recortar(cabecera.Nombre, 75, "NAME", avisos));
        foreach (var linea in cabecera.Direccion.Take(6))
        {
            Etiqueta(destino, "ADDRESS", Recortar(linea, 45, "ADDRESS", avisos));
        }
        if (cabecera.Direccion.Count > 6) avisos.Add("La dirección tiene más de seis líneas; se han escrito las seis primeras.");
        Etiqueta(destino, "ADDRESS-CITY", cabecera.Localidad);
        Etiqueta(destino, "ADDRESS-STATE-PROVINCE", cabecera.Provincia);
        Etiqueta(destino, "ADDRESS-POSTALCODE", cabecera.CodigoPostal);
        Etiqueta(destino, "ADDRESS-COUNTRY", cabecera.Pais);
        Etiqueta(destino, "EMAIL", cabecera.Correo);
        Etiqueta(destino, "GRID-LOCATOR", cabecera.Locator.EsVacio ? null : cabecera.Locator.Valor);
        if (cabecera.Operadoras.Count > 0)
        {
            Etiqueta(destino, "OPERATORS", Recortar(string.Join(' ', cabecera.Operadoras), 75, "OPERATORS", avisos));
        }
        if (cabecera.Certificado is { } certificado) Etiqueta(destino, "CERTIFICATE", certificado ? "YES" : "NO");
        foreach (var comentario in cabecera.Comentarios)
        {
            Etiqueta(destino, "SOAPBOX", Recortar(comentario, 75, "SOAPBOX", avisos));
        }
    }

    private static string Contest(CabeceraCabrillo cabecera, List<string> avisos)
    {
        var contest = (cabecera.Contest ?? string.Empty).Trim().ToUpperInvariant();
        if (contest.Length == 0)
        {
            avisos.Add("El log no dice a qué concurso pertenece (CONTEST).");
            return contest;
        }
        if (contest.Length > 32)
        {
            avisos.Add($"CONTEST pasa de 32 caracteres: «{contest}».");
            contest = contest[..32];
        }
        if (contest.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
        {
            avisos.Add($"CONTEST solo admite letras, números y guiones: «{contest}».");
        }
        return contest;
    }

    private static string LineaDeContacto(CabeceraCabrillo cabecera, Qso qso, List<string> avisos)
    {
        var frecuencia = FrecuenciaCabrillo.De(qso.Freq, qso.Band);
        if (frecuencia.Length == 0)
        {
            avisos.Add($"El contacto con {qso.Call.Valor} no tiene frecuencia ni banda reconocible.");
        }

        var linea = new StringBuilder(96);
        Columna(linea, frecuencia, 5, derecha: true);
        linea.Append(' ');
        Columna(linea, ClaseDeModo.Abreviatura(ClaseDeModo.De(qso.Mode)), 2);
        linea.Append(' ');
        linea.Append(qso.InicioUtc.UtcDateTime.ToString("yyyy-MM-dd HHmm", CultureInfo.InvariantCulture));
        linea.Append(' ');
        Columna(linea, Indicativo(cabecera.Indicativo.Valor, avisos), 13);
        linea.Append(' ');
        Columna(linea, Informe(qso.RstSent.Texto), 3);
        linea.Append(' ');
        Columna(linea, Intercambio(qso.StxString, qso.Stx), 6);
        linea.Append(' ');
        Columna(linea, Indicativo(qso.Call.Valor, avisos), 13);
        linea.Append(' ');
        Columna(linea, Informe(qso.RstRcvd.Texto), 3);
        linea.Append(' ');
        Columna(linea, Intercambio(qso.SrxString, qso.Srx), 6);
        if (cabecera.NumeroDeTransmisor is { } transmisor)
        {
            linea.Append(' ');
            linea.Append(transmisor.ToString("0", CultureInfo.InvariantCulture));
        }
        return linea.ToString().TrimEnd();
    }

    /// <summary>El informe va sin adornos; si falta, se escribe el de costumbre.</summary>
    private static string Informe(string? texto) =>
        string.IsNullOrWhiteSpace(texto) ? "599" : texto.Trim();

    private static string Intercambio(string? texto, int? numero)
    {
        if (!string.IsNullOrWhiteSpace(texto)) return texto.Trim();
        return numero?.ToString("000", CultureInfo.InvariantCulture) ?? string.Empty;
    }

    /// <summary>Un indicativo en Cabrillo solo puede llevar letras, numeros y barras.</summary>
    private static string Indicativo(string? texto, List<string> avisos)
    {
        var valor = (texto ?? string.Empty).Trim().ToUpperInvariant();
        if (valor.Length == 0)
        {
            avisos.Add("Hay un contacto sin indicativo.");
            return valor;
        }
        if (valor.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '/'))
        {
            avisos.Add($"El indicativo «{valor}» lleva caracteres que Cabrillo no admite.");
            valor = new string(valor.Where(c => char.IsAsciiLetterOrDigit(c) || c == '/').ToArray());
        }
        return valor;
    }

    private static void Columna(StringBuilder destino, string texto, int ancho, bool derecha = false)
    {
        var relleno = ancho - texto.Length;
        if (relleno <= 0)
        {
            destino.Append(texto);
            return;
        }
        if (derecha) destino.Append(' ', relleno);
        destino.Append(texto);
        if (!derecha) destino.Append(' ', relleno);
    }

    private static string BandaDeducida(IReadOnlyList<Qso> contactos)
    {
        var bandas = contactos.Select(q => q.Band.Nombre).Where(n => n.Length > 0).Distinct().ToList();
        if (bandas.Count != 1) return "ALL";
        // Cabrillo escribe las bandas en mayusculas: 20M, no 20m.
        return bandas[0].ToUpperInvariant();
    }

    private static string ModoDeducido(IReadOnlyList<Qso> contactos)
    {
        var clases = contactos.Select(q => ClaseDeModo.De(q.Mode)).Distinct().ToList();
        if (clases.Count != 1) return "MIXED";
        return clases[0] switch
        {
            ModoCabrillo.Cw => "CW",
            ModoCabrillo.Ry => "RTTY",
            ModoCabrillo.Dg => "DIGI",
            ModoCabrillo.Fm => "FM",
            _ => "SSB",
        };
    }

    private static string? Recortar(string? texto, int maximo, string etiqueta, List<string> avisos)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var valor = texto.Trim();
        if (valor.Length <= maximo) return valor;
        avisos.Add($"{etiqueta} pasa de {maximo} caracteres y se ha recortado.");
        return valor[..maximo];
    }

    private static void Etiqueta(TextWriter destino, string nombre, string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return;
        destino.Write(nombre);
        destino.Write(": ");
        destino.Write(valor.Trim());
        destino.Write("\r\n");
    }
}
