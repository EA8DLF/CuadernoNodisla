; Script de Inno Setup para el instalador de Cuaderno NODISLA.
; Genera el .exe de instalacion a partir de la publicacion self-contained
; de src/Nodisla.Cuaderno.Ui (ver docs/07-instalador.md para los pasos completos).
;
; IMPORTANTE: este instalador nunca toca %AppData%\CuadernoNodisla\ (ahi vive
; la base de datos y los ajustes del operador). No hay ninguna seccion que
; referencie esa carpeta, ni en instalacion ni en desinstalacion, a proposito.

#define MyAppName "Cuaderno NODISLA"
#define MyAppPublisher "NODISLA"
#define MyAppURL "https://nodisla.org"
#define MyAppExeName "Nodisla.Cuaderno.Ui.exe"
#define MyPublishDir "publicar\win-x64"
#define MyAppVersion GetVersionNumbersString(MyPublishDir + "\" + MyAppExeName)

[Setup]
; GUID fijo del producto: no cambiar entre versiones, para que las
; actualizaciones se reconozcan como upgrade del mismo programa.
AppId={{7F0F8A15-3A0B-4B79-9733-DA6556395EF8}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
VersionInfoVersion={#MyAppVersion}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
AllowNoIcons=yes
OutputDir=salida
OutputBaseFilename=CuadernoNodisla-Instalador-{#MyAppVersion}
SetupIconFile=..\src\Nodisla.Cuaderno.Ui\Recursos\CuadernoNodisla.ico
; El icono NODISLA del Cuaderno (sale de herramientas\Icono\GenerarIcono.ps1) va en el
; asistente, en "Aplicaciones instaladas" de Windows y en todos los accesos directos.
UninstallDisplayIcon={app}\CuadernoNodisla.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Instala en Program Files (todos los usuarios) por omision, pero el asistente
; deja elegir "solo para mi usuario" (sin pedir permisos de administrador,
; en {localappdata}\Programs\Cuaderno NODISLA) mediante la casilla que Inno
; Setup anade automaticamente en la pagina de la carpeta de destino.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
; El asistente habla el idioma de Windows si es uno de los del programa; si no, pregunta,
; con el ingles marcado (el mismo criterio que el programa).
ShowLanguageDialog=auto
LanguageDetectionMethod=uilanguage

[Languages]
; Los seis idiomas del programa, con los textos que trae Inno Setup. El ingles va primero:
; es el que se ofrece si Windows no habla ninguno de los otros.
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "portuguese"; MessagesFile: "compiler:Languages\Portuguese.isl"
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
; Textos de serie de Inno Setup ({cm:...}): ya vienen traducidos a cada idioma.
Name: "escritorio"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Copia todo lo publicado (self-contained, win-x64). No incluye nada de
; %AppData%: la publicacion de dotnet solo contiene los binarios del programa.
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs
Source: "..\src\Nodisla.Cuaderno.Ui\Recursos\CuadernoNodisla.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\CuadernoNodisla.ico"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\CuadernoNodisla.ico"; Tasks: escritorio
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"; IconFilename: "{app}\CuadernoNodisla.ico"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

; No hay seccion [UninstallDelete] ni [Dirs] que apunten a {userappdata}:
; el desinstalador solo retira lo que el instalador copio en {app} y los
; accesos directos del menu inicio. La base de datos y los ajustes del
; operador en %AppData%\CuadernoNodisla\ quedan intactos siempre.
