#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Fabrica el recurso de satelites que viaja dentro de Nodisla.Cuaderno.Satelites.

Entrada  : recursos/satelites/satelites-nodisla.tsv   (se edita a mano)
           %AppData%/Log4OM2/satellites.csv           (opcional, solo para avisar de huecos)
Salida   : src/Nodisla.Cuaderno.Satelites/Catalogo/Recursos/catalogo-satelites.tsv.gz

El recurso va comprimido y dentro del ensamblado, igual que el catalogo de diplomas: asi el
modulo no depende en ejecucion de ningun fichero suelto en disco ni de que Log4OM este
instalado.

Uso:  python recursos/satelites/generar-satelites.py
"""

from __future__ import annotations

import gzip
import os
import sys

RAIZ = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ENTRADA = os.path.join(RAIZ, "recursos", "satelites", "satelites-nodisla.tsv")
SALIDA = os.path.join(
    RAIZ, "src", "Nodisla.Cuaderno.Satelites", "Catalogo", "Recursos", "catalogo-satelites.tsv.gz"
)
LOG4OM = os.path.join(
    os.environ.get("APPDATA", ""), "Log4OM2", "satellites.csv"
)

COLUMNAS = [
    "abreviatura", "nombre", "norad", "estado", "transpondedor", "clase", "modo",
    "subida_ini", "subida_fin", "bajada_ini", "bajada_fin", "invertido", "subtono", "notas",
]

ESTADOS = {"activo", "intermitente", "inactivo"}
CLASES = {"", "FM", "LINEAL", "DIGITAL", "BALIZA"}


def leer_tsv(ruta: str) -> list[list[str]]:
    with open(ruta, "r", encoding="utf-8") as f:
        lineas = [l.rstrip("\n").rstrip("\r") for l in f if l.strip()]
    # Las columnas vacías del final se pierden al editar (casi todos los editores recortan los
    # tabuladores sobrantes), así que se rellenan aquí en vez de dar el fichero por malo.
    filas = [(l.split("\t") + [""] * len(COLUMNAS))[: len(COLUMNAS)] for l in lineas]
    cabecera = filas[0]
    if cabecera != COLUMNAS:
        raise SystemExit(f"La cabecera de {ruta} no es la esperada:\n{cabecera}\n{COLUMNAS}")
    return filas


def validar(filas: list[list[str]]) -> None:
    """Comprueba lo que se puede comprobar sin consultar a nadie."""
    problemas = []
    for n, fila in enumerate(filas[1:], start=2):
        if len(fila) != len(COLUMNAS):
            problemas.append(f"línea {n}: tiene {len(fila)} columnas y hacen falta {len(COLUMNAS)}")
            continue
        d = dict(zip(COLUMNAS, fila))
        if not d["abreviatura"]:
            problemas.append(f"línea {n}: sin abreviatura")
        if d["estado"] not in ESTADOS:
            problemas.append(f"línea {n}: estado «{d['estado']}» desconocido")
        if d["clase"] not in CLASES:
            problemas.append(f"línea {n}: clase «{d['clase']}» desconocida")
        if d["norad"] and not d["norad"].isdigit():
            problemas.append(f"línea {n}: número NORAD «{d['norad']}» no es un entero")
        for campo in ("subida_ini", "subida_fin", "bajada_ini", "bajada_fin"):
            if d[campo]:
                try:
                    valor = float(d[campo])
                except ValueError:
                    problemas.append(f"línea {n}: {campo} «{d[campo]}» no es un número")
                    continue
                if valor <= 0:
                    problemas.append(f"línea {n}: {campo} tiene que ser positivo")
        for par in (("subida_ini", "subida_fin"), ("bajada_ini", "bajada_fin")):
            a, b = d[par[0]], d[par[1]]
            if a and b and float(a) > float(b):
                problemas.append(
                    f"línea {n}: {par[0]} es mayor que {par[1]}; el sentido invertido se marca "
                    f"en la columna «invertido», no dando la vuelta al margen"
                )
        if d["invertido"] not in ("si", "no"):
            problemas.append(f"línea {n}: «invertido» tiene que ser «si» o «no»")
    if problemas:
        for p in problemas:
            print("  " + p, file=sys.stderr)
        raise SystemExit(f"{len(problemas)} problema(s) en {ENTRADA}")


def avisar_de_huecos(filas: list[list[str]]) -> None:
    """Dice qué satélites conoce Log4OM y no están aquí. Solo informa; no toca nada."""
    if not os.path.exists(LOG4OM):
        return
    nuestras = {f[0] for f in filas[1:]}
    faltan = []
    with open(LOG4OM, "r", encoding="utf-8-sig") as f:
        for linea in f:
            linea = linea.strip()
            if not linea or ";" not in linea:
                continue
            _, abrev = linea.split(";", 1)
            abrev = abrev.strip()
            if abrev and abrev not in nuestras:
                faltan.append(abrev)
    if faltan:
        print(f"Aviso: Log4OM conoce {len(faltan)} abreviaturas que no están aquí: "
              + ", ".join(sorted(faltan)))


def main() -> None:
    filas = leer_tsv(ENTRADA)
    validar(filas)
    avisar_de_huecos(filas)

    texto = "\n".join("\t".join(f) for f in filas) + "\n"
    os.makedirs(os.path.dirname(SALIDA), exist_ok=True)
    # mtime=0 para que el recurso sea reproducible: el mismo TSV da el mismo .gz.
    with gzip.GzipFile(SALIDA, "wb", compresslevel=9, mtime=0) as g:
        g.write(texto.encode("utf-8"))

    satelites = {f[0] for f in filas[1:]}
    print(f"{SALIDA}: {len(satelites)} satélites, {len(filas) - 1} transpondedores, "
          f"{os.path.getsize(SALIDA)} bytes")


if __name__ == "__main__":
    main()
