namespace Nodisla.Cuaderno.Adif;

/// <summary>
/// Recorre un fichero ADI y va devolviendo campos, fines de cabecera y fines de registro.
/// </summary>
/// <remarks>
/// Trabaja sobre bytes y no sobre texto por dos motivos. El primero es que el estandar declara
/// la longitud de cada campo en bytes; el segundo, que los ficheros reales mienten en esa
/// longitud —unos la cuentan en caracteres y otros directamente se equivocan— y la unica forma
/// de recuperarse es mirar si detras del valor hay de verdad una etiqueta.
/// </remarks>
internal sealed class AnalizadorAdi(Stream origen, Action<string, string?> avisar)
{
    /// <summary>Longitud maxima admitida para el interior de una etiqueta.</summary>
    private const int MaximoDeEtiqueta = 512;

    /// <summary>Blancos que se toleran entre el final de un valor y la etiqueta siguiente.</summary>
    private const int BlancosTolerados = 16;

    private readonly VentanaDeBytes _ventana = new(origen);
    private bool _marcaComprobada;

    /// <summary>Devuelve el siguiente elemento del fichero, o nulo cuando se acaba.</summary>
    public async Task<TokenAdi?> SiguienteAsync(CancellationToken ct)
    {
        await SaltarMarcaAsync(ct).ConfigureAwait(false);

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var abre = await _ventana.BuscarAsync((byte)'<', 0, ct).ConfigureAwait(false);
            if (abre < 0)
            {
                _ventana.Avanzar(_ventana.Disponible);
                return null;
            }
            _ventana.Avanzar(abre);

            var cierra = await _ventana.BuscarAsync((byte)'>', 1, ct).ConfigureAwait(false);
            if (cierra < 0)
            {
                avisar("Hay una etiqueta sin cerrar al final del fichero; se descarta lo que quedaba.", null);
                _ventana.Avanzar(_ventana.Disponible);
                return null;
            }
            if (cierra > MaximoDeEtiqueta)
            {
                // Un signo de menor que suelto dentro de un texto: no empieza ninguna etiqueta.
                _ventana.Avanzar(1);
                continue;
            }

            var especificacion = TextoAdif.Ascii(_ventana.Trozo(1, cierra - 1));
            if (!TryPartir(especificacion, out var nombre, out var longitud, out var tipo))
            {
                _ventana.Avanzar(1);
                continue;
            }

            _ventana.Avanzar(cierra + 1);

            if (nombre.Equals("EOR", StringComparison.OrdinalIgnoreCase))
                return new TokenAdi(ClaseDeToken.FinDeRegistro, default);
            if (nombre.Equals("EOH", StringComparison.OrdinalIgnoreCase))
                return new TokenAdi(ClaseDeToken.FinDeCabecera, default);

            if (longitud < 0)
            {
                avisar($"El campo «{nombre}» no declara longitud; se lee hasta la etiqueta siguiente.", nombre);
            }

            var (finDelValor, consumir) = await DeterminarFinDelValorAsync(nombre, longitud, ct)
                .ConfigureAwait(false);
            var valor = TextoAdif.Decodificar(_ventana.Trozo(0, finDelValor));
            _ventana.Avanzar(consumir);

            return new TokenAdi(ClaseDeToken.Campo, new CampoAdif(nombre.ToUpperInvariant(), valor, tipo));
        }
    }

    /// <summary>Se come la marca de orden de bytes de UTF-8 si el fichero empieza con ella.</summary>
    private async Task SaltarMarcaAsync(CancellationToken ct)
    {
        if (_marcaComprobada) return;
        _marcaComprobada = true;
        if (await _ventana.AsegurarAsync(3, ct).ConfigureAwait(false)
            && _ventana.Trozo(0, 3).SequenceEqual(TextoAdif.MarcaUtf8))
        {
            _ventana.Avanzar(3);
        }
    }

    /// <summary>
    /// Parte el interior de una etiqueta en nombre, longitud y tipo. Devuelve falso si lo que
    /// hay dentro no puede ser una etiqueta ADIF.
    /// </summary>
    private static bool TryPartir(string especificacion, out string nombre, out int longitud, out string? tipo)
    {
        longitud = -1;
        tipo = null;

        var partes = especificacion.Split(':');
        nombre = partes[0].Trim();
        if (nombre.Length == 0) return false;
        foreach (var c in nombre)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_') return false;
        }

        if (partes.Length >= 2 && int.TryParse(partes[1].Trim(), out var n) && n >= 0) longitud = n;
        if (partes.Length >= 3 && partes[2].Trim() is { Length: > 0 } t) tipo = t.ToUpperInvariant();
        return true;
    }

    /// <summary>
    /// Decide donde termina el valor del campo que empieza en el byte actual y cuanto hay que
    /// consumir del flujo, que no siempre es lo mismo.
    /// </summary>
    /// <remarks>
    /// Se prueban tres lecturas por orden de confianza. Primero la longitud declarada entendida
    /// como bytes, que es lo que manda el estandar. Si detras no hay una etiqueta, se prueba la
    /// misma cifra entendida como caracteres, que es lo que hacen algunos programas con los
    /// acentos. Si tampoco cuadra pero la longitud cabe, manda la longitud y lo que sobra se
    /// descarta: es el caso de Log4OM escribiendo <c>&lt;CNTY:10&gt;CA,VENTURA // Ventura</c>,
    /// donde el rotulo de adorno no forma parte del dato. Solo cuando la longitud no cabe —o no
    /// hay longitud— se busca a ojo la etiqueta siguiente.
    /// </remarks>
    private async Task<(int Valor, int Consumir)> DeterminarFinDelValorAsync(
        string nombre, int longitud, CancellationToken ct)
    {
        if (longitud >= 0)
        {
            await _ventana.AsegurarAsync((longitud * 3) + BlancosTolerados + 8, ct).ConfigureAwait(false);

            if (_ventana.Disponible >= longitud
                && await EsDelimitadorAsync(longitud, ct).ConfigureAwait(false))
            {
                return (longitud, longitud);
            }

            var enBytes = TextoAdif.BytesDeUnidades(_ventana.Trozo(0, _ventana.Disponible), longitud);
            if (enBytes > 0 && enBytes != longitud
                && await EsDelimitadorAsync(enBytes, ct).ConfigureAwait(false))
            {
                return (enBytes, enBytes);
            }

            if (_ventana.Disponible >= longitud)
            {
                var hasta = await BuscarFinPorEtiquetaAsync(longitud, ct).ConfigureAwait(false);
                var sobrante = TextoAdif.Decodificar(_ventana.Trozo(longitud, hasta - longitud)).Trim();
                if (sobrante.Length > 40) sobrante = sobrante[..40] + "…";
                avisar(
                    $"El campo «{nombre}» lleva «{sobrante}» detras de los {longitud} bytes que declara; "
                    + "manda la longitud declarada y lo demas se descarta.",
                    nombre);
                return (longitud, hasta);
            }

            avisar(
                $"El campo «{nombre}» dice ocupar {longitud} bytes pero el fichero se acaba antes.",
                nombre);
        }

        var fin = await BuscarFinPorEtiquetaAsync(0, ct).ConfigureAwait(false);
        return (fin, fin);
    }

    /// <summary>Comprueba si en el desplazamiento dado termina el valor de verdad.</summary>
    private async ValueTask<bool> EsDelimitadorAsync(int desplazamiento, CancellationToken ct)
    {
        var d = desplazamiento;
        for (var i = 0; i <= BlancosTolerados; i++)
        {
            if (!await _ventana.AsegurarAsync(d + 1, ct).ConfigureAwait(false)) return true; // fin de fichero
            var b = _ventana.En(d);
            if (b == (byte)'<') return await EsInicioDeEtiquetaAsync(d, ct).ConfigureAwait(false);
            if (!TextoAdif.EsBlanco(b)) return false;
            d++;
        }
        return false;
    }

    /// <summary>Busca la siguiente etiqueta creible desde un punto y recorta los blancos del final.</summary>
    private async Task<int> BuscarFinPorEtiquetaAsync(int desdeDesplazamiento, CancellationToken ct)
    {
        var desde = desdeDesplazamiento;
        var fin = -1;
        while (true)
        {
            var p = await _ventana.BuscarAsync((byte)'<', desde, ct).ConfigureAwait(false);
            if (p < 0) break;
            if (await EsInicioDeEtiquetaAsync(p, ct).ConfigureAwait(false)) { fin = p; break; }
            desde = p + 1;
        }
        if (fin < 0) fin = _ventana.Disponible;
        while (fin > desdeDesplazamiento && TextoAdif.EsBlanco(_ventana.En(fin - 1))) fin--;
        return fin;
    }

    /// <summary>Indica si en ese punto empieza algo con forma de etiqueta ADIF.</summary>
    private async ValueTask<bool> EsInicioDeEtiquetaAsync(int desplazamiento, CancellationToken ct)
    {
        if (!await _ventana.AsegurarAsync(desplazamiento + 1, ct).ConfigureAwait(false)) return false;
        if (_ventana.En(desplazamiento) != (byte)'<') return false;

        var letras = 0;
        for (var i = desplazamiento + 1; i < desplazamiento + 1 + 64; i++)
        {
            // Una etiqueta cortada por el final del fichero sigue siendo el final del valor.
            if (!await _ventana.AsegurarAsync(i + 1, ct).ConfigureAwait(false)) return letras > 0;
            var c = (char)_ventana.En(i);
            if (char.IsAsciiLetterOrDigit(c) || c == '_') { letras++; continue; }
            return letras > 0 && c is ':' or '>';
        }
        return false;
    }
}
