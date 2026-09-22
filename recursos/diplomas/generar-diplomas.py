# -*- coding: utf-8 -*-
"""
Genera el recurso de diplomas de Cuaderno NODISLA a partir de la base de
activaciones de Log4OM.

Entrada (SOLO LECTURA, se abre con mode=ro):
  Activations.SQLite -> Awards (87), AwardConfig (315), AwardReferences (553.064)
                        y AwardCustomReports (5).

Entradas editables a mano (viven en esta carpeta):
  ajustes-diplomas.tsv -> nombres en espanol, gestor, exigencia de confirmacion,
                          medios admitidos, objetivo y trato de las entidades
                          borradas. Lo que ponga aqui manda sobre lo deducido.

Salidas:
  diplomas-nodisla.tsv                -> cabecera, diplomas, variantes e informes
  referencias/<CODIGO>.tsv            -> una linea por referencia, un fichero por
                                         diploma (asi un cambio en POTA no toca SOTA)
  ../../src/Nodisla.Cuaderno.Diplomas/Recursos/catalogo-diplomas.tsv.gz
                                      -> el mismo texto, concatenado y comprimido,
                                         que es lo que se incrusta en el ensamblado

Uso:  python generar-diplomas.py [ruta_de_Activations.SQLite]
"""
import datetime
import gzip
import os
import re
import sqlite3
import sys

VERSION = 1
AQUI = os.path.dirname(os.path.abspath(__file__))
RAIZ = os.path.abspath(os.path.join(AQUI, "..", ".."))
CARPETA_REFS = os.path.join(AQUI, "referencias")
DESTINO_GZ = os.path.join(
    RAIZ, "src", "Nodisla.Cuaderno.Diplomas", "Recursos", "catalogo-diplomas.tsv.gz")

ORIGEN = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
    os.environ.get("APPDATA", ""), "Log4OM2", "Activations.SQLite")

# ── traduccion de vocabulario ────────────────────────────────────────────────

CLASES = {
    "REFERENCE": "PorReferencia",
    "QSOFIELDS": "PorCampo",
    "CALLSIGN": "PorIndicativo",
}

# Vias de confirmacion de Log4OM -> MedioDeConfirmacion del dominio.
# CUSTOM no es una via: significa que el diploma lo valida su gestor con su propia
# base de datos, que el cuaderno no puede comprobar. Se trata aparte.
MEDIOS = {
    "QSL": "Papel",
    "LOTW": "Lotw",
    "EQSL": "Eqsl",
    "QRZCOM": "QrzCom",
    "HAMQTH": "HamQth",
    "HRDLOG": "HrdLog",
    "CLUBLOG": "ClubLog",
    "QRZCQ": "QrzCq",
}

# Campos del QSO que sabe leer el motor. Los que no estan aqui dejan el diploma
# marcado como no calculable, que es mejor que calcularlo mal. Que el campo este
# aqui no basta: el motor comprueba ademas que la columna exista de verdad en el
# cuaderno, porque el esquema puede ser mas viejo que el catalogo.
CAMPOS = {
    "DXCC": "Dxcc",
    "STATE": "State",
    "CQZONE": "CqZone",
    "ITUZONE": "ItuZone",
    "CONTINENT": "Continent",
    "PFX": "Pfx",
    "GRIDSQUARE4": "Gridsquare4",
    "CNTY": "Cnty",
    "QTH": "Qth",
    "ADDRESS": "Address",
    "SIGINFO": "SigInfo",
}

NOMBRE_VARIANTE_UNICA = "GENERAL"


def limpiar(texto):
    """Deja el texto apto para una celda TSV: sin tabuladores ni saltos."""
    if texto is None:
        return ""
    return re.sub(r"[\t\r\n]+", " ", str(texto)).strip()


def fecha(valor):
    """Las fechas de Log4OM son enteros AAAAMMDD; se guardan como texto ISO."""
    texto = str(valor or "").strip()
    if len(texto) != 8 or not texto.isdigit():
        return ""
    ano, mes, dia = texto[:4], texto[4:6], texto[6:]
    if ano >= "9000":          # 99981231 y 99991231 significan «sin fin»
        return ""
    if mes == "00" or dia == "00":
        return ""
    return f"{ano}-{mes}-{dia}"


def lista(texto):
    """Separa una lista de Log4OM (';') y devuelve los trozos no vacios."""
    return [t.strip() for t in (texto or "").split(";") if t.strip()]


def dxcc_permitidos(texto):
    """
    Normaliza AllowedDXCC: '#24#;#514#;#336#' -> [24, 336, 514].

    Es la lista que el original guarda en una sola columna de hasta 3.000
    caracteres y que por eso no puede indexar. Aqui se parte en numeros y el
    motor la vuelca en una tabla con clave primaria e indice propio.
    Lo que no sea un numero entre almohadillas se descarta y se cuenta.
    """
    numeros = set()
    descartes = []
    for trozo in (texto or "").split(";"):
        trozo = trozo.strip()
        if not trozo:
            continue
        coincide = re.fullmatch(r"#?(\d{1,5})#?", trozo)
        if coincide:
            numeros.add(int(coincide.group(1)))
        else:
            descartes.append(trozo)
    return sorted(numeros), descartes


def objetivo_de_la_descripcion(texto):
    """
    Saca el objetivo cuando el propio catalogo lo dice con todas las letras.
    Solo dos formas, las unicas que aparecen sin ambiguedad:
      '(100 credits)'          -> 100
      'Work all 52 Spanish...' -> 52
    Cualquier otra cosa se deja sin objetivo: inventarlo seria peor.
    """
    if not texto:
        return ""
    coincide = re.search(r"\((\d+)\s+credits?\)", texto, re.I)
    if coincide:
        return coincide.group(1)
    coincide = re.search(r"\ball\s+(\d+)\b", texto, re.I)
    if coincide:
        return coincide.group(1)
    return ""


def leer_ajustes():
    """Lee ajustes-diplomas.tsv: {(codigo, variante): {campo: valor}}."""
    ruta = os.path.join(AQUI, "ajustes-diplomas.tsv")
    ajustes = {}
    if not os.path.exists(ruta):
        return ajustes
    with open(ruta, encoding="utf-8") as f:
        cabecera = None
        for linea in f:
            linea = linea.rstrip("\n")
            if not linea.strip() or linea.lstrip().startswith("#"):
                continue
            campos = linea.split("\t")
            if cabecera is None:
                cabecera = [c.strip() for c in campos]
                continue
            fila = dict(zip(cabecera, [c.strip() for c in campos]))
            clave = (fila.get("codigo", "").upper(), fila.get("variante", "*").upper())
            ajustes[clave] = fila
    return ajustes


def ajuste(ajustes, codigo, variante, campo):
    """Busca un ajuste primero por variante concreta y luego por comodin."""
    for clave in ((codigo.upper(), variante.upper()), (codigo.upper(), "*")):
        fila = ajustes.get(clave)
        if fila and fila.get(campo):
            return fila[campo]
    return ""


def main():
    if not os.path.exists(ORIGEN):
        sys.exit(f"No encuentro la base de activaciones: {ORIGEN}")

    ajustes = leer_ajustes()
    con = sqlite3.connect(f"file:{ORIGEN.replace(os.sep, '/')}?mode=ro", uri=True)
    con.text_factory = lambda b: b.decode("utf-8", "replace")

    hoy = datetime.date.today().isoformat()
    fecha_datos = con.execute(
        "SELECT MAX(LastUpdate) FROM Awards").fetchone()[0] or ""
    fecha_datos = fecha(fecha_datos[:8]) or hoy

    # ── diplomas ─────────────────────────────────────────────────────────────
    diplomas = []
    filas_d = []
    for f in con.execute("""
            SELECT AwardCode, AwardType, AwardName, Description, ConfirmationMethod,
                   ValidationMethod, QsoField, QsoLeadingField, QsoFieldSeparator,
                   QsoFieldExact, LeadingString, TrailingString, ReferencePrefixes,
                   AllowMultipleRefs, ReferenceLess, AwardURL, ReferenceURL,
                   ValidFrom, ValidTo, Valid, AllowedBands, AllowedEmissionType
              FROM Awards ORDER BY AwardCode"""):
        (codigo, tipo, nombre, descripcion, confirmacion, validacion, campo, campo_lider,
         separador, exacto, inicial, final, prefijos, multiples, sin_ref, web, web_ref,
         desde, hasta, valido, bandas, emision) = f

        clase = CLASES.get((tipo or "").upper(), "PorReferencia")

        vias = [v.upper() for v in lista(confirmacion)]
        medios = [MEDIOS[v] for v in vias if v in MEDIOS]
        # Si el diploma solo admite la validacion de su gestor, el cuaderno no
        # puede comprobarla. Se exige confirmacion por cualquier via, que cuenta
        # de menos y no de mas, y se marca para poder decir por que.
        gestor_valida = "1" if "CUSTOM" in vias else "0"

        campo_normal = CAMPOS.get(re.sub(r"[^A-Z0-9]", "", (campo or "").upper()), "")
        calculable = "1"
        motivo = ""
        if clase == "PorCampo" and not campo_normal:
            calculable, motivo = "0", f"campo del QSO no soportado: {campo}"

        nombre_es = ajuste(ajustes, codigo, "*", "nombre_es")
        gestor = ajuste(ajustes, codigo, "*", "gestor")
        exigencia = ajuste(ajustes, codigo, "*", "exigencia") or "Confirmado"
        medios_ajuste = ajuste(ajustes, codigo, "*", "medios")
        if medios_ajuste:
            medios = [m.strip() for m in medios_ajuste.split(",") if m.strip()]
        solo_vigentes = ajuste(ajustes, codigo, "*", "solo_vigentes") or "0"

        diplomas.append(codigo)
        filas_d.append("\t".join([
            "D", codigo, clase, limpiar(nombre), limpiar(nombre_es), limpiar(gestor),
            limpiar(web), limpiar(web_ref), exigencia, ",".join(medios), gestor_valida,
            campo_normal, limpiar(campo_lider), limpiar(separador), str(int(exacto or 0)),
            limpiar(inicial), limpiar(final), limpiar(prefijos),
            str(int(multiples or 0)), str(int(sin_ref or 0)),
            fecha(desde), fecha(hasta), str(int(valido or 0)),
            limpiar(bandas), limpiar(emision), solo_vigentes, calculable, limpiar(motivo),
            limpiar(descripcion),
        ]))

    # ── variantes ────────────────────────────────────────────────────────────
    filas_c = []
    con_variante = set()
    originales = 0
    for f in con.execute("""
            SELECT AwardCode, ConfigName, Description, ContactType, Modes, Bands,
                   EmissionType, Continents, YearlyAward, RequireSatellite,
                   ExcludeSatellite, GrantCode, ValidFrom, ValidTo
              FROM AwardConfig ORDER BY AwardCode, ConfigName"""):
        (codigo, variante, descripcion, contacto, modos, bandas, emision, continentes,
         anual, requiere_sat, excluye_sat, grant, desde, hasta) = f
        originales += 1
        con_variante.add(codigo.upper())
        objetivo = (ajuste(ajustes, codigo, variante, "objetivo")
                    or objetivo_de_la_descripcion(descripcion))
        filas_c.append("\t".join([
            "C", codigo, variante, limpiar(descripcion), limpiar(contacto),
            limpiar(modos), limpiar(bandas), limpiar(emision), limpiar(continentes),
            str(int(anual or 0)), str(int(requiere_sat or 0)), str(int(excluye_sat or 0)),
            limpiar(grant), fecha(desde), fecha(hasta), objetivo, "0",
        ]))

    # 74 de los 87 diplomas no traen ninguna fila en AwardConfig: el original
    # los trata como si tuvieran una sola clase sin restriccion. Se hace
    # explicito aqui para que el motor no tenga que adivinarlo.
    sinteticas = 0
    for codigo in diplomas:
        if codigo.upper() in con_variante:
            continue
        sinteticas += 1
        objetivo = ajuste(ajustes, codigo, NOMBRE_VARIANTE_UNICA, "objetivo")
        filas_c.append("\t".join([
            "C", codigo, NOMBRE_VARIANTE_UNICA, "Clase unica, sin restriccion de banda ni modo",
            "Chaser", "", "", "", "", "0", "0", "0", codigo, "", "", objetivo, "1",
        ]))
    filas_c.sort(key=lambda l: (l.split("\t")[1].upper(), l.split("\t")[2].upper()))

    # ── informes ─────────────────────────────────────────────────────────────
    filas_i = []
    for f in con.execute("""
            SELECT ReportId, Title, Description, ProviderId, Enabled
              FROM AwardCustomReports ORDER BY ReportId"""):
        filas_i.append("\t".join([
            "I", limpiar(f[0]), limpiar(f[1]), limpiar(f[2]), limpiar(f[3]),
            str(int(f[4] or 0)),
        ]))

    # ── referencias, un fichero por diploma ──────────────────────────────────
    os.makedirs(CARPETA_REFS, exist_ok=True)
    for viejo in os.listdir(CARPETA_REFS):
        if viejo.endswith(".tsv"):
            os.remove(os.path.join(CARPETA_REFS, viejo))

    total_refs = 0
    total_dxcc = 0
    descartes_dxcc = []
    ficheros_refs = []
    por_diploma = {}
    for f in con.execute("""
            SELECT AwardCode, ReferenceCode, ReferenceCodeAlias, ValidFrom, ValidTo,
                   ReferenceDescription, AllowedDXCC, ReferenceGroup, ReferenceSubGroup,
                   GridSquare, ReferenceScore, ReferenceBonusScore, Valid, SearchPattern
              FROM AwardReferences"""):
        (codigo, ref, alias, desde, hasta, descripcion, permitidos, grupo, subgrupo,
         grid, puntos, bonus, valido, patron) = f
        numeros, sobra = dxcc_permitidos(permitidos)
        if sobra:
            descartes_dxcc.extend(sobra[:3])
        total_dxcc += len(numeros)
        total_refs += 1
        por_diploma.setdefault(codigo.upper(), []).append("\t".join([
            "R", codigo, limpiar(ref), limpiar(alias), fecha(desde), fecha(hasta),
            limpiar(descripcion), limpiar(grupo), limpiar(subgrupo), limpiar(grid),
            f"{float(puntos or 0):g}", f"{float(bonus or 0):g}", str(int(valido or 0)),
            limpiar(patron), ",".join(str(n) for n in numeros),
        ]))

    for codigo in sorted(por_diploma):
        lineas = sorted(por_diploma[codigo], key=lambda l: l.split("\t")[2].upper())
        nombre = re.sub(r"[^A-Za-z0-9_.-]", "_", codigo) + ".tsv"
        ruta = os.path.join(CARPETA_REFS, nombre)
        with open(ruta, "w", encoding="utf-8", newline="\n") as g:
            g.write(f"# Referencias del diploma {codigo}. Generado; no se edita a mano.\n")
            g.write("\n".join(lineas) + "\n")
        ficheros_refs.append(ruta)

    # ── fichero principal ────────────────────────────────────────────────────
    cabecera = [
        "# Catalogo de diplomas de Cuaderno NODISLA. Generado por generar-diplomas.py.",
        "# No se edita a mano: lo que haya que tocar va en ajustes-diplomas.tsv.",
        "\t".join(["V", str(VERSION), fecha_datos, hoy,
                   str(len(diplomas)), str(len(filas_c)), str(total_refs)]),
    ]
    principal = os.path.join(AQUI, "diplomas-nodisla.tsv")
    with open(principal, "w", encoding="utf-8", newline="\n") as g:
        g.write("\n".join(cabecera + filas_d + filas_c + filas_i) + "\n")

    # ── el mismo texto, comprimido, para incrustar en el ensamblado ──────────
    os.makedirs(os.path.dirname(DESTINO_GZ), exist_ok=True)
    with gzip.GzipFile(DESTINO_GZ, "wb", compresslevel=9, mtime=0) as g:
        with open(principal, "rb") as e:
            g.write(e.read())
        for ruta in ficheros_refs:
            with open(ruta, "rb") as e:
                g.write(e.read())

    print(f"diplomas    : {len(diplomas)}")
    print(f"variantes   : {len(filas_c)} ({originales} del original + {sinteticas} sinteticas)")
    print(f"referencias : {total_refs} en {len(ficheros_refs)} ficheros")
    print(f"dxcc permitidos normalizados: {total_dxcc}")
    if descartes_dxcc:
        print(f"AVISO: tokens de AllowedDXCC descartados, muestra: {descartes_dxcc[:5]}")
    print(f"informes    : {len(filas_i)}")
    print(f"comprimido  : {os.path.getsize(DESTINO_GZ) / 1e6:.1f} MB -> {DESTINO_GZ}")


if __name__ == "__main__":
    main()
