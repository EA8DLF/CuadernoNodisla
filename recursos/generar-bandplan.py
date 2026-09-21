# -*- coding: utf-8 -*-
"""Fabrica el recurso de bandplan de Cuaderno NODISLA a partir de los XML de Log4OM.

Uso:
    python generar-bandplan.py                      # usa %APPDATA%\\Log4OM2\\bandplan
    python generar-bandplan.py D:\\ruta\\a\\bandplan  # o una carpeta cualquiera

Reescribe:
    recursos/bandplan-nodisla.tsv                    el recurso, versionable y diffable
    src/Nodisla.Cuaderno.Integraciones/Bandplan/DatosBandplan.cs
                                                     el mismo texto, incrustado

El formato esta descrito en LEEME-bandplan.md, junto a esta herramienta.
"""

import os
import sys
import datetime
import xml.etree.ElementTree as ET

AQUI = os.path.dirname(os.path.abspath(__file__))
RAIZ = os.path.dirname(AQUI)
TSV = os.path.join(AQUI, "bandplan-nodisla.tsv")
CS = os.path.join(RAIZ, "src", "Nodisla.Cuaderno.Integraciones", "Bandplan", "DatosBandplan.cs")

VERSION_FORMATO = 1

# Nombres de las entidades de los planes nacionales que publica Log4OM.
# Salen de recursos/paises-nodisla.tsv; son cinco y no cambian, asi que se copian
# aqui para que esta herramienta no dependa de la tabla de paises.
NOMBRES_DXCC = {
    108: "Brasil",
    221: "Dinamarca",
    223: "Inglaterra",
    230: "Alemania",
    291: "Estados Unidos de America",
}

NOMBRES_REGION = {
    1: "IARU Region 1 (Europa, Africa, Oriente Medio y norte de Asia)",
    2: "IARU Region 2 (America)",
    3: "IARU Region 3 (Asia y Pacifico)",
}


def texto(nodo, etiqueta, por_omision=""):
    hijo = nodo.find(etiqueta)
    if hijo is None or hijo.text is None:
        return por_omision
    return hijo.text.strip()


def numero(valor):
    """Devuelve el numero tal cual lo escribio Log4OM, sin ceros ni comas sobrantes."""
    d = float(valor)
    if d == int(d):
        return str(int(d))
    return repr(d)


def leer_plan(ruta):
    raiz = ET.parse(ruta).getroot()
    region = int(texto(raiz, "Region", "0") or 0)
    dxcc = int(texto(raiz, "Dxcc", "0") or 0)

    if dxcc:
        ident = "dxcc%d" % dxcc
        nombre = NOMBRES_DXCC.get(dxcc, "DXCC %d" % dxcc)
    else:
        ident = "r%d" % region
        nombre = NOMBRES_REGION.get(region, "Region %d" % region)

    segmentos = []
    for r in raiz.iter("BandPlanRange"):
        segmentos.append((
            texto(r, "Band"),
            numero(texto(r, "Start", "0")),
            numero(texto(r, "End", "0")),
            texto(r, "EmissionType").upper(),
            texto(r, "ModulationType").upper(),
        ))

    senaladas = []
    for s in raiz.iter("SpecialFrequencies"):
        for cadena in s.iter("string"):
            if not cadena.text:
                continue
            partes = cadena.text.strip().split("|")
            if len(partes) < 2:
                continue
            # Log4OM las da en hercios; el recurso las guarda en kilohercios,
            # que es la unidad del resto del fichero.
            hz = float(partes[0])
            senaladas.append((numero(hz / 1000.0), partes[1].strip().upper()))

    # Orden estable: por banda tal y como aparece, luego por frecuencia.
    segmentos.sort(key=lambda s: (float(s[1]), float(s[2])))
    senaladas.sort(key=lambda f: (float(f[0]), f[1]))
    return ident, region, dxcc, nombre, segmentos, senaladas


def main():
    origen = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
        os.environ.get("APPDATA", ""), "Log4OM2", "bandplan")
    if not os.path.isdir(origen):
        print("No encuentro la carpeta de bandplanes: %s" % origen)
        return 1

    ficheros = sorted(f for f in os.listdir(origen) if f.lower().endswith(".xml"))
    if not ficheros:
        print("No hay ningun XML en %s" % origen)
        return 1

    fecha_datos = datetime.date.fromtimestamp(
        max(os.path.getmtime(os.path.join(origen, f)) for f in ficheros))
    hoy = datetime.date.today()

    planes = [leer_plan(os.path.join(origen, f)) for f in ficheros]
    # Primero las regiones IARU, despues los planes nacionales.
    planes.sort(key=lambda p: (p[2] != 0, p[1], p[2]))

    lineas = [
        "# Bandplan de Cuaderno NODISLA. Generado por generar-bandplan.py; no editar a mano.",
        "# Formato descrito en recursos/LEEME-bandplan.md.",
        "V\t%d\t%s\t%s" % (VERSION_FORMATO, fecha_datos.isoformat(), hoy.isoformat()),
    ]
    n_seg = n_fre = 0
    for ident, region, dxcc, nombre, segmentos, senaladas in planes:
        lineas.append("P\t%s\t%d\t%d\t%s" % (ident, region, dxcc, nombre))
        for banda, desde, hasta, emision, modulacion in segmentos:
            # La ultima columna, las clases de licencia, va vacia: los XML de Log4OM
            # no las traen. Se deja para poder anadirlas sin cambiar el formato.
            lineas.append("S\t%s\t%s\t%s\t%s\t%s\t%s\t" % (
                ident, banda, desde, hasta, emision, modulacion))
            n_seg += 1
        for khz, modo in senaladas:
            lineas.append("F\t%s\t%s\t%s\t" % (ident, khz, modo))
            n_fre += 1

    texto_recurso = "\n".join(lineas) + "\n"
    with open(TSV, "w", encoding="utf-8", newline="\n") as f:
        f.write(texto_recurso)

    cs = [
        "// GENERADO POR recursos/generar-bandplan.py - NO EDITAR A MANO.",
        "// Copia literal de recursos/bandplan-nodisla.tsv.",
        "// Se incrusta como texto para que la consulta no dependa de ficheros sueltos.",
        "",
        "namespace Nodisla.Cuaderno.Integraciones.Bandplan;",
        "",
        "/// <summary>Bandplan incrustado, en el formato descrito en <c>recursos/LEEME-bandplan.md</c>.</summary>",
        "internal static class DatosBandplan",
        "{",
        "    /// <summary>Texto completo del recurso, una linea por registro.</summary>",
        '    public const string Texto =',
        '"""',
        texto_recurso.rstrip("\n"),
        '""";',
        "}",
        "",
    ]
    with open(CS, "w", encoding="utf-8", newline="\r\n") as f:
        f.write("\n".join(cs))

    print("Planes: %d   Segmentos: %d   Frecuencias senaladas: %d" % (len(planes), n_seg, n_fre))
    print("Datos de %s" % fecha_datos.isoformat())
    return 0


if __name__ == "__main__":
    sys.exit(main())
