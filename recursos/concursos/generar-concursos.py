# -*- coding: utf-8 -*-
"""
Genera el recurso de concursos de Cuaderno NODISLA.

Entrada (SOLO LECTURA):
  %AppData%\\Log4OM2\\contest.csv  -> 242 pares "nombre;IDENTIFICADOR-CABRILLO".
                                     Es lo unico que el original guarda de los
                                     concursos: ni una regla, ni una fecha.

Entrada editable a mano (vive en esta carpeta):
  reglas-concursos.tsv            -> las reglas de verdad: intercambio, puntuacion,
                                     multiplicadores, bandas, modos y fecha de
                                     celebracion. Lo que ponga aqui manda.

Salidas:
  concursos-nodisla.tsv                                 -> el recurso en texto, revisable en git
  ../../src/Nodisla.Cuaderno.Concursos/Recursos/catalogo-concursos.tsv.gz
                                                        -> el mismo texto comprimido, que es
                                                           lo que se incrusta en el ensamblado

Uso:  python generar-concursos.py [ruta_de_contest.csv]
"""
import datetime
import gzip
import io
import os
import sys

VERSION = 1
AQUI = os.path.dirname(os.path.abspath(__file__))
RAIZ = os.path.abspath(os.path.join(AQUI, "..", ".."))
REGLAS = os.path.join(AQUI, "reglas-concursos.tsv")
SALIDA_TSV = os.path.join(AQUI, "concursos-nodisla.tsv")
SALIDA_GZ = os.path.join(
    RAIZ, "src", "Nodisla.Cuaderno.Concursos", "Recursos", "catalogo-concursos.tsv.gz")

ORIGEN = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
    os.environ.get("APPDATA", ""), "Log4OM2", "contest.csv")

# Columnas de cada tipo de registro, para que el recurso tenga siempre la misma forma.
ANCHO = {"C": 17, "P": 6, "M": 4}


def leer_log4om(ruta):
    """Devuelve [(codigo, nombre)] leyendo el catalogo del original."""
    if not os.path.exists(ruta):
        print("AVISO: no esta %s; el recurso saldra solo con lo escrito a mano." % ruta)
        return []
    salida = []
    with io.open(ruta, "r", encoding="utf-8-sig", errors="replace") as f:
        for linea in f:
            linea = linea.strip()
            if not linea or ";" not in linea:
                continue
            nombre, codigo = linea.rsplit(";", 1)
            codigo = codigo.strip()
            nombre = nombre.strip()
            # Log4OM marca asi los que solo sabe importar. El identificador es el de antes.
            codigo = codigo.replace("(import-only)", "").strip()
            if not codigo:
                continue
            salida.append((codigo, nombre))
    return salida


def leer_reglas(ruta):
    """Devuelve (concursos, puntos, multiplicadores) del fichero editable a mano."""
    concursos, puntos, mults = [], [], []
    with io.open(ruta, "r", encoding="utf-8") as f:
        for linea in f:
            if linea.startswith("#") or not linea.strip():
                continue
            campos = linea.rstrip("\n").split("\t")
            destino = {"C": concursos, "P": puntos, "M": mults}.get(campos[0])
            if destino is None:
                raise SystemExit("Registro desconocido en reglas-concursos.tsv: %r" % campos[0])
            esperado = ANCHO[campos[0]]
            if len(campos) > esperado:
                raise SystemExit("Registro %s con %d campos, se esperaban %d: %r"
                                 % (campos[0], len(campos), esperado, campos[1]))
            campos += [""] * (esperado - len(campos))
            destino.append(campos)
    return concursos, puntos, mults


def sin_tildes(texto):
    """El recurso va sin tildes: se lee desde C# y desde Python y no siempre coinciden."""
    tabla = {u"á": "a", u"é": "e", u"í": "i", u"ó": "o", u"ú": "u", u"ü": "u",
             u"Á": "A", u"É": "E", u"Í": "I", u"Ó": "O", u"Ú": "U", u"Ü": "U",
             u"ñ": "n", u"Ñ": "N", u"ç": "c", u"Ç": "C", u"º": "o", u"ª": "a"}
    return "".join(tabla.get(c, c) for c in texto)


def main():
    del_original = leer_log4om(ORIGEN)
    concursos, puntos, mults = leer_reglas(REGLAS)

    con_reglas = {fila[1] for fila in concursos}

    # Los que trae el original y no tienen reglas escritas entran igual: asi el operador
    # puede marcar el CONTEST_ID correcto de un contacto y exportar Cabrillo aunque el
    # programa no sepa puntuar ese concurso.
    vistos = set(con_reglas)
    for codigo, nombre in del_original:
        if codigo in vistos:
            continue
        vistos.add(codigo)
        fila = ["C", codigo, sin_tildes(nombre), "", "SoloCatalogo", "", "", "", "",
                "0", "0", "N", "", "", "0", "Banda", ""]
        concursos.append(fila)

    concursos.sort(key=lambda f: f[1])
    puntos.sort(key=lambda f: (f[1],))
    mults.sort(key=lambda f: (f[1], f[2]))

    hoy = datetime.date.today().isoformat()
    lineas = []
    lineas.append("# Catalogo de concursos de Cuaderno NODISLA. GENERADO: no se edita a mano.\n")
    lineas.append("# Se fabrica con recursos/concursos/generar-concursos.py a partir de\n")
    lineas.append("# reglas-concursos.tsv (escrito a mano) y del contest.csv de Log4OM.\n")
    lineas.append("#\n")
    lineas.append("# Las reglas marcadas 'Aproximado' no se han leido de las bases del ano en curso.\n")
    lineas.append("# Antes de enviar un log hay que contrastarlas con el organizador.\n")
    lineas.append("\t".join(["V", str(VERSION), hoy, str(len(concursos)), str(len(con_reglas))]) + "\n")
    for fila in concursos + puntos + mults:
        lineas.append("\t".join(sin_tildes(c) for c in fila).rstrip("\t") + "\n")

    texto = "".join(lineas)
    with io.open(SALIDA_TSV, "w", encoding="utf-8", newline="\n") as f:
        f.write(texto)

    if not os.path.isdir(os.path.dirname(SALIDA_GZ)):
        os.makedirs(os.path.dirname(SALIDA_GZ))
    # mtime=0 para que dos generaciones del mismo texto den el mismo fichero y git no vea ruido.
    crudo = texto.encode("utf-8")
    with open(SALIDA_GZ, "wb") as f:
        with gzip.GzipFile(fileobj=f, mode="wb", compresslevel=9, mtime=0) as gz:
            gz.write(crudo)

    print("%d concursos (%d con reglas), %d lineas de puntuacion, %d de multiplicador"
          % (len(concursos), len(con_reglas), len(puntos), len(mults)))
    print("  %s  (%d bytes)" % (SALIDA_TSV, len(crudo)))
    print("  %s  (%d bytes)" % (SALIDA_GZ, os.path.getsize(SALIDA_GZ)))


if __name__ == "__main__":
    main()
