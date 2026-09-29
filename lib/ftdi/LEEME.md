# LibFT4222 de FTDI (no incluida)

El analizador de espectro del FT-710 llega por el puente USB FT4222H de la radio, y para hablar
con él hace falta la biblioteca oficial de FTDI, **LibFT4222**. Su licencia prohíbe entregarla a
terceros, así que **no viene en este repositorio**.

1. Descárguela de la web de FTDI: <https://ftdichip.com/products/ft4222h/> → *Software* →
   *LibFT4222* (se probó con la versión 1.4.8).
2. Del paquete, copie la DLL de 64 bits aquí con este nombre exacto:

   ```
   lib/ftdi/LibFT4222-64.dll
   ```

   Si quiere que su licencia viaje junto al programa, guárdela también aquí como
   `lib/ftdi/LICENCIA-FTDI.txt`.

3. Vuelva a compilar. El proyecto `Nodisla.Cuaderno.Radio` la copia a la salida solo si existe;
   sin ella todo compila igual y el programa avisa, al abrir el analizador, de que falta.

El `.gitignore` ignora todo lo que haya en esta carpeta salvo este LEEME, para que la DLL no se
suba por descuido.
