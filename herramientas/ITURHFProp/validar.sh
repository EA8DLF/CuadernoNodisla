#!/usr/bin/env bash
# Comprueba que el ITURHFProp compilado aqui reproduce los nueve casos de referencia que
# publica la UIT. Si alguno no sale identico, el motor no vale y no se debe usar: se enseñaria
# al operador un numero que nadie ha validado.
#
# Uso:  bash validar.sh
# Sale con 0 si todos coinciden y con 1 en cuanto uno falla.

set -u
aqui="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# El ejecutable es de Windows y no entiende las rutas al estilo de Git Bash ("/c/Users/..."):
# abre el fichero, no lo encuentra y sigue adelante dejando un informe a medias. Si estamos en
# Git Bash o en Cygwin hay que traducir la ruta antes de metersela en el fichero de entrada.
if command -v cygpath >/dev/null 2>&1; then
    aqui_nativo="$(cygpath -m "$aqui")"
else
    aqui_nativo="$aqui"
fi

bin="$aqui/programa"
ejemplos="$aqui/ejemplos"
trabajo="$(mktemp -d)"
trap 'rm -rf "$trabajo"' EXIT

casos=(
  1-5-85
  1-5-85_01
  1-8-84
  164-1-78
  itu_old
  itu_old22012020
  caracas_201805_10_31_B4
  moscow_201805_10_31_B4
  sydney_201805_10_31_B4
)

# El ejecutable carga P533.dll y P372.dll por nombre desde su propia carpeta, y los ficheros de
# antena los busca junto al fichero de entrada. Se monta todo en un directorio de trabajo.
cp "$bin/ITURHFProp.exe" "$bin/P533.dll" "$bin/P372.dll" "$trabajo/"
cp "$ejemplos"/*.13 "$trabajo/" 2>/dev/null

fallos=0
for caso in "${casos[@]}"; do
    # Los ejemplos de la UIT apuntan a "../Data/"; aqui la carpeta se llama "datos". Hay que
    # cambiarlo en todas las lineas, no solo en DataFilePath: itu_old22012020 tiene ademas un
    # fichero de antena con esa misma ruta y, si no se encuentra, el informe sale distinto sin
    # que el programa avise de nada.
    sed 's|"\.\./Data/|"'"$aqui_nativo"'/datos/|g' \
        "$ejemplos/$caso.in" > "$trabajo/$caso.in"

    ( cd "$trabajo" && ./ITURHFProp.exe "$caso.in" "$caso.out" >/dev/null 2>&1 )
    if [ ! -f "$trabajo/$caso.out" ]; then
        printf '%-28s NO GENERA SALIDA\n' "$caso"
        fallos=$((fallos + 1))
        continue
    fi

    # Se descartan las dos lineas que cambian siempre y con razon: la fecha de compilacion del
    # binario y la hora a la que se hizo el analisis. Todo lo demas tiene que ser identico.
    distintas=$(diff --strip-trailing-cr \
        <(grep -v 'Ver \|Analysis Prepared' "$ejemplos/$caso.out") \
        <(grep -v 'Ver \|Analysis Prepared' "$trabajo/$caso.out") | grep -c '^[<>]')

    if [ "$distintas" -eq 0 ]; then
        printf '%-28s identico\n' "$caso"
    else
        printf '%-28s %s lineas distintas\n' "$caso" "$distintas"
        fallos=$((fallos + 1))
    fi
done

echo
if [ "$fallos" -eq 0 ]; then
    echo "Los ${#casos[@]} casos de referencia coinciden. El motor vale."
    exit 0
fi

echo "$fallos caso(s) no coinciden. NO uses este binario."
exit 1
