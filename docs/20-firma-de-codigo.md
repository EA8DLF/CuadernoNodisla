# Firma de código y verificación del actualizador

Desde la **0.2.19** el instalador y los binarios propios de Cuaderno NODISLA van firmados con
Authenticode, y el programa **no ejecuta un instalador descargado que no lleve la firma de
NODISLA**. Este documento cuenta qué se firma, dónde vive el certificado, cómo se renueva y cómo
comprobar un instalador a mano. No contiene secretos, y no debe contenerlos nunca: ni
contraseñas, ni ficheros `.pfx`, ni claves privadas, ni nada derivado de ellos entra en el
repositorio.

## Qué problema resuelve

Hasta la 0.2.18 el actualizador solo comprobaba la **integridad**: que el SHA-256 del `.exe`
descargado casaba con el `.sha256` publicado a su lado. Pero los dos ficheros viven en la misma
release de GitHub: quien pueda cambiar la release cambia a la vez el instalador y su suma, y el
programa lo habría ejecutado. La suma dice que la descarga llegó entera; no dice **de quién** es.

Ahora, además de la suma, el programa exige:

1. que el fichero lleve una firma Authenticode **íntegra** (no se ha tocado desde que se firmó), y
2. que el certificado firmante sea **exactamente** uno de los fijados dentro del programa.

La lista de certificados aceptados va compilada en el programa
(`FirmantesDeConfianza`, en `src/Nodisla.Cuaderno.Servicios/Actualizaciones/FirmaDeInstalador.cs`)
y no se puede cambiar desde GitHub. Para colar un instalador falso hace falta la clave privada,
que no está ni en GitHub ni en ningún servidor.

Si la comprobación falla, el fichero descargado se borra, no se pregunta nada, no se ejecuta
nada, el operador ve un mensaje en su idioma («El instalador descargado no lleva la firma de
NODISLA…») y queda una línea de aviso en el registro con el estado, el firmante y la huella que
se han visto.

## El certificado, y lo que NO hace

| | |
|---|---|
| Sujeto | `CN=Jose M. EA8DLF (SDR Nodisla)` (el mismo que firma otros programas de NODISLA) |
| Tipo | **autofirmado**, RSA 2048, firmado con SHA-256 |
| Uso | Firma de código (EKU `1.3.6.1.5.5.7.3.3`), uso de clave «Firma digital» |
| Validez | 04/09/2026 – 04/09/2036 |
| Huella SHA-1 (la que enseña Windows y la que usa el script) | `919A06FFBDF618A0D1CE38426547328C1FB82E74` |
| Huella SHA-256 (la que lleva fijada el programa) | `EED2089F0BBDEA84445C022C8DC00C3D21A563A5EFEE3FC6742A7397FFE5552A` |

Las huellas son públicas: van dentro de cada fichero firmado.

**Es autofirmado, y eso tiene un límite que conviene tener claro.** No lo ha emitido ninguna
entidad de certificación, así que Windows no confía en su cadena en el PC de nadie que no lo
haya instalado a mano como raíz de confianza. En un PC ajeno:

- El aviso de **control de cuentas (UAC)** al instalar para todos los usuarios sigue diciendo
  **«Editor: desconocido»**, igual que con un fichero sin firmar.
- **SmartScreen** («Windows protegió su PC») sigue saliendo al abrir un instalador bajado con el
  navegador. SmartScreen trabaja por reputación del fichero y del certificado, y un certificado
  autofirmado no acumula reputación.
- En Propiedades › Firmas digitales la firma aparece, pero con el aviso de que el certificado
  no es de confianza.

O sea: **la firma no quita ningún aviso de Windows ni hace que Windows «confíe» en el programa.**
Lo que sí hace es lo que se buscaba: que el propio programa pueda distinguir un instalador de
NODISLA de uno que no lo es, y que cualquiera pueda comprobar la huella a mano. Para que
desaparezcan los avisos de Windows haría falta un certificado de firma de código emitido por una
entidad reconocida (de pago) o el servicio de firma de Microsoft; no se ha hecho. No se debe
pedir a los usuarios que instalen el certificado como raíz de confianza.

Por la misma razón la verificación del programa **no** se basa en «Windows confía en la cadena»
(nunca sería cierto), sino en firma íntegra + huella fijada.

### Dónde vive

- La clave privada está en el almacén de certificados **del usuario** de Windows en el PC desde
  el que se publica: `Cert:\CurrentUser\My`. El script de publicación la usa desde ahí, por
  huella; no hay contraseñas en el script ni en el repositorio.
- Hay una copia de seguridad (`.pfx` con contraseña) **fuera del repositorio y fuera de
  cualquier servidor expuesto a internet**, en el almacenamiento local de copias. Si se pierde
  el almacén o se publica desde otro PC, se importa ahí:

  ```powershell
  Import-PfxCertificate -FilePath <copia.pfx> -CertStoreLocation Cert:\CurrentUser\My `
      -Password (Read-Host -AsSecureString)
  ```

- **No** se instala en «Entidades de certificación raíz de confianza» ni en «Editores de
  confianza»: no hace falta para firmar ni para que el programa verifique.

Comprobar que está y sirve (solo lectura):

```powershell
. .\herramientas\publicacion\Firma.ps1
Get-CertificadoDeFirma | Format-List Subject, Thumbprint, NotAfter, HasPrivateKey, EnhancedKeyUsageList
```

## Qué se firma al publicar

`herramientas\publicacion\Publicar-Version.ps1` (las funciones están en `Firma.ps1`, a su lado):

1. `dotnet publish` de la interfaz, self-contained win-x64.
2. Firma **los `.exe` y `.dll` propios** (`Nodisla.Cuaderno.*`, incluidos los satélite de idioma)
   de la carpeta publicada. Lo de terceros y el .NET se dejan como vienen.
3. Compila el instalador con Inno Setup pasándole la orden de firma
   (`ISCC /DFirmar /Snodisla=…`). Con `SignTool=nodisla` en el `.iss`, Inno firma **el
   desinstalador y el propio instalador**; si la firma falla, ISCC falla y no hay instalador.
4. Comprueba que los binarios propios y el instalador han quedado firmados **por el certificado
   de la huella y con sello de tiempo**. Si falta uno, se para.
5. Calcula el `.sha256` (siempre después de firmar: la firma cambia el fichero).

Se firma con `signtool.exe` del SDK de Windows:

```
signtool sign /s My /sha1 <huella> /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 <ficheros>
```

- **Sello de tiempo** (RFC 3161): sin él, la firma dejaría de valer el día que caduque el
  certificado. El servidor por omisión es el público de DigiCert; se cambia con
  `-ServidorDeSello`. **Sin red, o si el servidor no contesta, el script falla** con un mensaje
  claro: no se publica sin sello.
- Certificado: `-HuellaDeFirma` (por omisión, la de arriba), siempre del almacén del usuario.
- `-SignTool` e `-Iscc` permiten indicar las rutas si no se encuentran solas.

### Cuándo se niega a publicar

- No está el certificado en `Cert:\CurrentUser\My`, no tiene clave privada, no es de firma de
  código o está caducado.
- Falla la firma o el sello de tiempo.
- Algún binario propio o el instalador queda sin firma, firmado por otro, alterado o sin sello.
  También con `-SinCompilar`: el instalador reutilizado se vuelve a comprobar.
- Con `-Subir`: la etiqueta `v<Version>` **ya existe** en git (local o en `origin`) o ya hay una
  release con ese nombre. Ya pasó una vez que dos compilaciones distintas salieron como 0.2.18.
  Una versión publicada **no se reemplaza**: se sube `<Version>` y se publica otra. Por eso la
  etiqueta no se crea a mano antes: la crea `gh release create`. Sin `-Subir` (solo generar para
  revisar) lo mismo sale como aviso.

### El instalador compilado a mano

`ISCC instalador\CuadernoNodisla.iss` sin `/DFirmar` sigue funcionando y da un instalador **sin
firma**: vale para instalar a mano en casa, pero un programa 0.2.19 o posterior se negará a
actualizarse con él. Lo que se cuelga en GitHub sale siempre de `Publicar-Version.ps1`.

## Cómo lo comprueba el programa

`DescargadorDeInstalador` baja la suma, baja el instalador a un fichero `.parcial`, comprueba el
SHA-256 y, si casa, le pide a `IVerificadorDeFirma` que mire la firma **antes de que exista
ningún `.exe`**. `FirmantesDeConfianza.Acepta` decide: firma íntegra **y** huella en la lista.
Si no, borra el `.parcial` y devuelve `FirmaNoValida`.

La implementación de verdad, `VerificadorDeFirmaAuthenticode`, usa la API de Windows:

- `WinVerifyTrust` con la acción `WINTRUST_ACTION_GENERIC_VERIFY_V2` (Authenticode), sin interfaz
  (`WTD_UI_NONE`), sin salir a la red por revocación (`WTD_REVOKE_NONE`,
  `WTD_CACHE_ONLY_URL_RETRIEVAL`) y conservando el estado (`WTD_STATEACTION_VERIFY`).
- El firmante se lee de ese mismo estado (`WTHelperProvDataFromStateData` →
  `WTHelperGetProvSignerFromChain` → `WTHelperGetProvCertFromChain`): es el certificado con el
  que Windows ha comprobado la firma, no uno leído aparte.
- **Códigos de retorno.** Con un certificado autofirmado el resultado normal en un PC ajeno no
  es `0` sino `CERT_E_UNTRUSTEDROOT` (`0x800B0109`: la cadena acaba en una raíz que Windows no
  conoce). Se aceptan como «firma íntegra» **solo** `0` y `CERT_E_UNTRUSTEDROOT`, y solo si
  además los pasos del objeto y de la firma (`TRUSTERROR_STEP_FINAL_OBJPROV` y
  `TRUSTERROR_STEP_FINAL_SIGPROV` en `CRYPT_PROVIDER_DATA.padwTrustStepErrors`) están a cero.
  `TRUST_E_NOSIGNATURE` (`0x800B0100`), `TRUST_E_SUBJECT_FORM_UNKNOWN` y
  `TRUST_E_PROVIDER_UNKNOWN` son «sin firma». Cualquier otro código —por ejemplo
  `TRUST_E_BAD_DIGEST` (`0x80096010`), que es lo que da un fichero alterado— es «no vale».
  Lo que no se entiende, no vale.

Fuentes (Microsoft):

- WinVerifyTrust: <https://learn.microsoft.com/windows/win32/api/wintrust/nf-wintrust-winverifytrust>
  («No other value besides zero should be considered a successful return»; el valor es un `LONG`,
  no un `HRESULT`).
- WINTRUST_DATA: <https://learn.microsoft.com/windows/win32/api/wintrust/ns-wintrust-wintrust_data>
- CRYPT_PROVIDER_DATA: <https://learn.microsoft.com/windows/win32/api/wintrust/ns-wintrust-crypt_provider_data>
- Ejemplo «Verifying the Signature of a PE File»:
  <https://learn.microsoft.com/windows/win32/seccrypto/example-c-program--verifying-the-signature-of-a-pe-file>
- Cabeceras `wintrust.h`, `softpub.h` y `winerror.h` del SDK de Windows 10.0.22621: de ahí salen
  los valores de las constantes, los índices `TRUSTERROR_STEP_*` y el comentario de
  `WinVerifyTrust` que dice qué errores devuelve el proveedor Authenticode.
- SignTool: <https://learn.microsoft.com/windows/win32/seccrypto/signtool>
- SmartScreen (reputación de ficheros y certificados):
  <https://learn.microsoft.com/windows/security/operating-system-security/virus-and-threat-protection/microsoft-defender-smartscreen/>
- Inno Setup, `SignTool`: <https://jrsoftware.org/ishelp/topic_setup_signtool.htm>

### Pruebas

`tests/Nodisla.Cuaderno.Servicios.Pruebas/FirmaDeInstaladorPruebas.cs`:

- La decisión, con dobles (`FirmaDoble`): firmante fijado, otro firmante, sin firma, alterada,
  sin huella, lista rotada, verificador que lanza.
- **Integración real con Windows**: la prueba crea en memoria un certificado autofirmado
  temporal (no entra en ningún almacén de certificados; el `.pfx` temporal vive segundos en una
  carpeta que se borra), firma con él una copia de un ejecutable y comprueba: firmado y sin tocar
  → aceptado con esa huella (y Windows devuelve `CERT_E_UNTRUSTEDROOT`); otra huella → rechazado;
  alterado después de firmar → rechazado (`TRUST_E_BAD_DIGEST`); sin firma → rechazado
  (`TRUST_E_NOSIGNATURE`); y lo mismo de punta a punta por el descargador.

### Compatibilidad con lo ya instalado

- Las versiones **0.2.18 y anteriores** no miran la firma: siguen actualizándose como siempre
  con el `.exe` y su `.sha256`, que se publican igual. Un instalador firmado les vale.
- Desde la versión que lleva esta comprobación, **toda release tiene que salir firmada** por
  `Publicar-Version.ps1`. Si se cuelga a mano un instalador sin firmar, los programas ya
  actualizados no lo instalarán (el operador verá el mensaje y tendrá que bajarlo a mano).

### Lo que esto no cubre

- **Volver a servir una versión antigua.** Un instalador viejo, firmado de verdad por NODISLA,
  pasa la comprobación aunque la release diga que es una versión más nueva. No se compara la
  versión del fichero con la anunciada.
- **Quien ya puede escribir en el perfil del usuario** (otro programa con sus mismos permisos)
  puede cambiar el fichero entre la comprobación y el arranque. Contra eso no protege una firma.
- **Revocación.** Un certificado autofirmado no se puede revocar. Si la clave privada se
  filtrara, las copias instaladas seguirían aceptándola hasta actualizarse a una versión sin
  esa huella.

## Renovar o cambiar el certificado

El certificado caduca el **04/09/2036**. Lo ya firmado con sello de tiempo sigue valiendo después;
lo que no se podrá es firmar cosas nuevas. Hay que renovarlo **antes**, y en este orden, para no
dejar tirado a nadie:

1. **Crear el certificado nuevo** en el almacén del usuario y sacar copia de seguridad fuera del
   repositorio:

   ```powershell
   $nuevo = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=<sujeto>' `
       -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 `
       -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddYears(10)
   $nuevo.Thumbprint                          # SHA-1: la que usa el script
   $nuevo.GetCertHashString('SHA256')         # SHA-256: la que se fija en el programa
   ```

2. **Añadir** la huella SHA-256 nueva a la lista de `FirmantesDeConfianza.Nodisla`, **sin quitar
   la vieja**, y publicar una versión **firmada todavía con el certificado viejo**. Esa versión
   ya acepta los dos.
3. Dejar pasar tiempo para que los instalados se actualicen a esa versión.
4. Cambiar `$script:HuellaNodisla` en `herramientas\publicacion\Firma.ps1` por la SHA-1 nueva (o
   publicar con `-HuellaDeFirma`). A partir de aquí se firma con el nuevo.
5. Cuando ya no quede nadie en versiones anteriores al paso 2, se puede quitar la huella vieja
   de la lista.

Quien siga en una versión anterior al paso 2 cuando se empiece a firmar con el certificado nuevo
no podrá actualizarse solo: verá el mensaje de firma no válida y tendrá que bajar el instalador
a mano. Por eso el paso 3 no se salta.

Si la clave se **pierde** (almacén y copia), el camino es el mismo empezando por el paso 1, pero
el paso 2 no se puede firmar con la vieja: todos los instalados tendrán que actualizar a mano
una vez. Si la clave se **filtra**, publicar cuanto antes una versión que quite la huella vieja,
sabiendo el límite de la revocación contado arriba.

## Comprobar un instalador a mano

En cualquier PC, sin instalar nada:

```powershell
Get-AuthenticodeSignature .\CuadernoNodisla-Instalador-0.2.19.0.exe |
    Format-List Status, StatusMessage, SignerCertificate, TimeStamperCertificate
```

- `SignerCertificate` tiene que ser `CN=Jose M. EA8DLF (SDR Nodisla)` con huella
  `919A06FFBDF618A0D1CE38426547328C1FB82E74`.
- `TimeStamperCertificate` no puede estar vacío.
- `Status`: en un PC sin el certificado instalado sale `UnknownError` (Windows PowerShell 5.1) o
  `NotTrusted` (PowerShell 7) con el mensaje de que la raíz no es de confianza. **Es lo
  esperado** con un certificado autofirmado. Lo que **no** puede salir es `NotSigned` (sin
  firma) ni `HashMismatch` (alterado después de firmar).

Con las funciones del repositorio (dice `OK` o el motivo):

```powershell
. .\herramientas\publicacion\Firma.ps1
$motivo = Test-FirmaNodisla -Fichero .\CuadernoNodisla-Instalador-0.2.19.0.exe
if ($motivo) { "MAL: $motivo" } else { 'OK' }
```

Con la misma lógica que usa el programa (`WinVerifyTrust` + huella fijada):

```powershell
$env:CUADERNO_INSTALADOR_A_COMPROBAR = (Resolve-Path .\CuadernoNodisla-Instalador-0.2.19.0.exe)
dotnet test tests\Nodisla.Cuaderno.Servicios.Pruebas -c Release --filter "FullyQualifiedName~indicado_a_mano"
$env:CUADERNO_INSTALADOR_A_COMPROBAR = $null
```

`signtool verify /pa /v <fichero>` también enseña la firma y el sello, pero **termina con error**
de raíz no confiable en un PC sin el certificado instalado: con un autofirmado no sirve como
semáforo.

Con el ratón: botón derecho › Propiedades › Firmas digitales › Detalles › Ver certificado ›
Detalles › «Huella digital».
