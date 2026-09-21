# -*- coding: utf-8 -*-
"""
Genera el recurso de paises de Cuaderno NODISLA a partir de los dos ficheros
que mantiene el ecosistema Log4OM / AD1C.

Entradas (solo lectura):
  country.xml   -> catalogo de entidades DXCC con VENTANAS TEMPORALES de prefijo.
  ctyfile.json  -> volcado de cty.dat de AD1C: prefijos actuales con zonas propias
                   y excepciones nominales (indicativo completo).
  nombres-es.tsv-> traducciones al espanol, editables a mano.

Salidas:
  paises-nodisla.tsv                                  (recurso canonico, versionable)
  ../src/Nodisla.Cuaderno.Dominio/Dxcc/DatosPaises.cs (el mismo texto incrustado)

Uso:  python generar-paises.py [carpeta_log4om]
"""
import collections
import datetime
import json
import os
import re
import sys
import xml.etree.ElementTree as ET

VERSION = 1
AQUI = os.path.dirname(os.path.abspath(__file__))
ORIGEN = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
    os.environ.get("APPDATA", ""), "Log4OM2")


def solo_fecha(texto):
    return texto[:10] if texto else ""


def leer_nombres_es():
    ruta = os.path.join(AQUI, "nombres-es.tsv")
    nombres = {}
    if not os.path.exists(ruta):
        return nombres
    with open(ruta, encoding="utf-8") as f:
        for linea in f:
            linea = linea.rstrip("\n")
            if not linea or linea.startswith("#"):
                continue
            partes = linea.split("\t")
            if len(partes) >= 2 and partes[0].strip().isdigit():
                nombres[int(partes[0])] = partes[1].strip()
    return nombres


def leer_country_xml(ruta):
    """Entidades + ventanas temporales de prefijo."""
    raiz = ET.parse(ruta).getroot()
    fecha_datos = solo_fecha(raiz.findtext("LastUpdate") or "")
    entidades = {}
    ventanas = collections.defaultdict(list)   # (prefijo, dxcc) -> [(desde, hasta)]
    descartados = collections.Counter()

    for c in raiz.find("CountryList").findall("Country"):
        dxcc = int(c.findtext("Dxcc"))
        activo = (c.findtext("Active") == "true")
        e = entidades.setdefault(dxcc, {
            "nombre": c.findtext("CountryName") or "",
            "prefijo": c.findtext("ArrlPrefix") or "",
            "cont": c.findtext("Continent") or "",
            "cq": int(c.findtext("CqZone") or 0),
            "itu": int(c.findtext("ItuZone") or 0),
            "lat": float(c.findtext("Latitude") or 0),
            "lon": float(c.findtext("Longitude") or 0),
            "activo": activo,
            "desde": None, "hasta": None, "abierta_izq": False, "abierta_der": False,
        })

        for cp in c.find("CountryPrefixList").findall("CountryPrefix"):
            desde = solo_fecha(cp.findtext("StartDate"))
            hasta = solo_fecha(cp.findtext("EndDate"))
            # Limites de validez de la entidad: la envolvente de sus ventanas.
            if desde:
                e["desde"] = desde if e["desde"] is None else min(e["desde"], desde)
            else:
                e["abierta_izq"] = True
            if hasta:
                e["hasta"] = hasta if e["hasta"] is None else max(e["hasta"], hasta)
            else:
                e["abierta_der"] = True

            for patron in (cp.findtext("PrefixList") or "").split("|"):
                clave = patron
                if clave.startswith("^"):
                    clave = clave[1:]
                if clave.endswith(".*"):
                    clave = clave[:-2]
                # Solo se aceptan prefijos llanos; los pocos patrones con sintaxis
                # de expresion regular o con barra los cubre ya cty.dat.
                if not clave or not re.fullmatch(r"[A-Z0-9]+", clave):
                    descartados[patron] += 1
                    continue
                ventanas[(clave, dxcc)].append((desde, hasta))

    for e in entidades.values():
        if e["abierta_izq"]:
            e["desde"] = None
        # Una entidad vigente no tiene fecha de baja aunque alguna de sus
        # ventanas de prefijo se cerrara al reorganizarse la lista.
        if e["activo"] or e["abierta_der"]:
            e["hasta"] = None

    return fecha_datos, entidades, ventanas, descartados


def fusionar(intervalos):
    """Une las ventanas contiguas o solapadas de un mismo par (prefijo, entidad)."""
    orden = sorted(intervalos, key=lambda v: v[0] or "0000-00-00")
    salida = []
    for desde, hasta in orden:
        if salida:
            ad, ah = salida[-1]
            if ah == "" or (desde != "" and ah >= desde):
                salida[-1] = (ad, "" if (ah == "" or hasta == "") else max(ah, hasta))
                continue
        salida.append((desde, hasta))
    return salida


def leer_ctyfile(ruta):
    """Prefijos actuales y excepciones nominales de cty.dat."""
    datos = json.load(open(ruta, encoding="utf-8-sig"))
    prefijos = {}
    exactos = {}
    centros = {}
    for c in datos:
        dxcc = c["Dxcc"]
        cc = c["Coordinates"]
        centros.setdefault((dxcc, c["Continent"]), (cc["Latitude"], cc["Longitude"]))
        centros.setdefault(dxcc, (cc["Latitude"], cc["Longitude"]))
        for p in c["Prefixes"]:
            co = p["Coordinates"]
            regla = {
                "dxcc": dxcc,
                "cq": p["CQZone"] or c["CQZone"],
                "itu": p["ITUZone"] or c["ITUZone"],
                "cont": p["Continent"] or c["Continent"],
                "lat": co["Latitude"] or cc["Latitude"],
                "lon": co["Longitude"] or cc["Longitude"],
            }
            destino = exactos if p["ExactMatch"] else prefijos
            destino.setdefault(p["Callsign"].upper(), regla)
    return prefijos, exactos, centros


def num(v):
    """Formato corto y estable para los numeros del recurso."""
    if v is None:
        return ""
    if isinstance(v, int):
        return str(v)
    if abs(v - round(v)) < 1e-9:
        return str(int(round(v)))
    return ("%.2f" % v).rstrip("0").rstrip(".")


def campos_regla(clave, r, ent, desde="", hasta=""):
    """Los campos que coinciden con la entidad se dejan vacios: pesan menos."""
    e = ent.get(r["dxcc"])
    cq = "" if (e and r["cq"] == e["cq"]) else num(r["cq"])
    itu = "" if (e and r["itu"] == e["itu"]) else num(r["itu"])
    cont = "" if (e and r["cont"] == e["cont"]) else (r["cont"] or "")
    lat, lon = "", ""
    if e and (abs(r["lat"] - e["lat"]) > 0.005 or abs(r["lon"] - e["lon"]) > 0.005):
        lat, lon = num(round(r["lat"], 2)), num(round(r["lon"], 2))
    return [clave, str(r["dxcc"]), cq, itu, cont, lat, lon, desde, hasta]


def main():
    ruta_xml = os.path.join(ORIGEN, "country.xml")
    ruta_cty = os.path.join(ORIGEN, "ctyfile.json")
    for r in (ruta_xml, ruta_cty):
        if not os.path.exists(r):
            sys.exit("No encuentro %s" % r)

    fecha_datos, ent, ventanas, descartados = leer_country_xml(ruta_xml)
    prefijos, exactos, centros = leer_ctyfile(ruta_cty)

    # El centro de cada entidad se toma de cty.dat cuando esta: asi coincide con
    # el de sus prefijos y estos no tienen que repetirlo en cada linea.
    for dxcc, e in ent.items():
        c = centros.get((dxcc, e["cont"])) or centros.get(dxcc)
        if c:
            e["lat"], e["lon"] = c
        # cty.dat solo lista entidades vigentes: si esta ahi, no esta borrada del
        # listado DXCC aunque Log4OM la marque como inactiva (le pasa a Libia).
        if dxcc in centros:
            e["hasta"] = None
    nombres_es = leer_nombres_es()

    lineas = []
    hoy = datetime.date.today().isoformat()
    lineas.append("\t".join(["V", str(VERSION), fecha_datos, hoy]))

    for dxcc in sorted(ent):
        e = ent[dxcc]
        utc = round(e["lon"] / 15.0)
        lineas.append("\t".join([
            "E", str(dxcc), e["nombre"], nombres_es.get(dxcc, ""), e["prefijo"],
            e["cont"], num(e["cq"]), num(e["itu"]), num(round(e["lat"], 2)),
            num(round(e["lon"], 2)), num(utc), e["desde"] or "", e["hasta"] or "",
        ]))

    # Excepciones nominales: prioridad absoluta, van primero en el fichero.
    for clave in sorted(exactos):
        lineas.append("\t".join(["X"] + campos_regla(clave, exactos[clave], ent)))

    # Prefijos vigentes de cty.dat (sin fechas: valen mientras la entidad viva).
    for clave in sorted(prefijos):
        lineas.append("\t".join(["P"] + campos_regla(clave, prefijos[clave], ent)))

    # Ventanas historicas de country.xml. Se omiten las que no aportan nada
    # sobre cty.dat: mismo destino, sin fecha de cierre.
    historicas = 0
    for (clave, dxcc), brutas in sorted(ventanas.items()):
        e = ent.get(dxcc)
        if e is None:
            continue
        for desde, hasta in fusionar(brutas):
            if not hasta and prefijos.get(clave, {}).get("dxcc") == dxcc and not desde:
                continue
            r = {"dxcc": dxcc, "cq": e["cq"], "itu": e["itu"], "cont": e["cont"],
                 "lat": e["lat"], "lon": e["lon"]}
            lineas.append("\t".join(["H"] + campos_regla(clave, r, ent, desde, hasta)))
            historicas += 1

    texto = "\n".join(l.rstrip("\t") for l in lineas) + "\n"

    ruta_tsv = os.path.join(AQUI, "paises-nodisla.tsv")
    with open(ruta_tsv, "w", encoding="utf-8", newline="\n") as f:
        f.write(texto)

    ruta_cs = os.path.normpath(os.path.join(
        AQUI, "..", "src", "Nodisla.Cuaderno.Dominio", "Dxcc", "DatosPaises.cs"))
    with open(ruta_cs, "w", encoding="utf-8", newline="\n") as f:
        f.write("// GENERADO POR recursos/generar-paises.py - NO EDITAR A MANO.\n")
        f.write("// Copia literal de recursos/paises-nodisla.tsv.\n")
        f.write("// Se incrusta como texto para que el dominio no dependa de ficheros sueltos.\n\n")
        f.write("namespace Nodisla.Cuaderno.Dominio.Dxcc;\n\n")
        f.write("/// <summary>Tabla de paises incrustada, en el formato descrito en "
                "<c>recursos/LEEME.md</c>.</summary>\n")
        f.write("internal static class DatosPaises\n{\n")
        f.write("    /// <summary>Texto completo del recurso, una linea por registro.</summary>\n")
        f.write("    public const string Texto =\n")
        # El delimitador de cierre va en la columna 0: asi el compilador no
        # recorta nada del contenido.
        f.write('"""\n')
        f.write(texto)
        f.write('""";\n}\n')

    print("entidades:   %d" % len(ent))
    print("excepciones: %d" % len(exactos))
    print("prefijos:    %d" % len(prefijos))
    print("historicas:  %d" % historicas)
    print("descartados: %d patrones no llanos (%s)"
          % (sum(descartados.values()), ", ".join(list(descartados)[:4])))
    print("fecha datos: %s" % fecha_datos)
    print("escrito:     %s (%.0f KiB)" % (ruta_tsv, os.path.getsize(ruta_tsv) / 1024))
    print("escrito:     %s" % ruta_cs)


if __name__ == "__main__":
    main()
