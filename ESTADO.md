# Cuaderno NODISLA

Clon en español de Log4OM NextGen 2.40.0.0, para uso particular de EA8DLF.

## Estado
Fase 1 — núcleo del cuaderno. Esqueleto de solución compilando; cuatro especialistas trabajando en ADIF, datos, DXCC e interfaz.

## Decisiones tomadas (2026-09-21)
- **Stack**: C# .NET 8 + WPF, escritorio Windows.
- **Alcance**: paridad completa con Log4OM, por fases.
- **Servicios**: LoTW, QRZ.com, ClubLog, eQSL, cluster DX, WSJT-X y JTDX.
- **Modo de trabajo**: team lead con especialistas; planificar y construir la v1.

## Entorno
- .NET SDK 8.0.422 · Visual Studio 18 · Windows 10 x64 (10.0.18362)
- Original instalado en `D:\Log4OM NextGen\`; datos en `%AppData%\Log4OM2\`

## Documentos
- `docs/00-plan-maestro.md` — fases, riesgos y decisiones
- `docs/01-inventario-funcional.md`
- `docs/02-modelo-datos.md`
- `docs/03-arquitectura.md`
