# LicenciasCarpetas

Reemplazo del libro **"DETALLE CARPETAS DEPTO. LICENCIAS DE CONDUCIR"** por una aplicación web
(.NET 10, Razor Pages, SQLite) con el mismo stack que `outlook-comuna-router`, que corre en el
mismo equipo del Depto. de Licencias de Conducir de la Municipalidad de Valparaíso.

El Excel se importa una vez; desde ahí la base de datos es la fuente de verdad. El archivo original
nunca se modifica: solo se lee.

## Qué reemplaza, hoja por hoja

| En el Excel | En la aplicación |
| --- | --- |
| 18 hojas de agenda (mes × oficina: Av. Argentina, Placilla, Merc. Puerto) | Pantalla **Casos** (`/`), con filtros por oficina, año, mes, estado, decisión y sector |
| Hojas `PLANTILLA MODELO` (listas desplegables) | Catálogos fijos de estado de carpeta, decisión final e idoneidad moral |
| Columna `FECHA ULTIMA CARPETA` (fecha **o** comuna) | `Última carpeta`: si es fecha, deriva el sector; si es comuna, marca la carpeta como pedida a otro municipio |
| Búsqueda manual de la carpeta física | Sector derivado automáticamente: **Archivo** (antes de julio 2023) u **Oficina 43** (desde julio 2023), con listado imprimible en `/Sector/Archivo` y `/Sector/Oficina43` |
| Hoja `ESCANEADAS Y SUBIDAS` | Pantalla **Estadísticas** (`/Estadisticas`): agendadas, atendidas y % de atención se calculan solos; escaneadas y subidas siguen siendo contadores manuales por día |
| Hoja `CORREOS CAMBIO DE DOMICLIO` | Pantalla **Comunas** (`/Comunas`), buscable y editable |
| Copiar/pegar entre hojas para reportes | **Exportar a Excel** respetando los filtros aplicados |

Además de lo que hacía el Excel:

- **RUT validado** con dígito verificador y normalizado a `13.025.150-1`. Los RUT inválidos se
  conservan tal cual y el caso queda marcado como *Requiere revisión*.
- **Sin duplicados**: reimportar el mismo archivo actualiza los casos existentes en lugar de
  duplicarlos (clave: oficina + fecha de citación + RUT; si el RUT o la fecha no son legibles, la
  celda de origen — hoja y fila).
- **Variantes de escritura unificadas**: `ESPERA EXÁMEN`/`ESPERA EXAMEN`,
  `SE ENCUENTRA EN OF.43`/`OF. 43`, `CAMBIO DE DOM SUBIDO CON CORREO`/`CAMBIO DOM. SUBIDO CON CORREO`
  colapsan a un solo valor de catálogo.
- **Trazabilidad**: cada caso guarda de qué hoja y fila del Excel salió.

## Requisitos

- .NET 10 SDK
- El archivo `.xlsx` accesible (puede estar abierto en Excel o sincronizando en Google Drive: se lee
  con acceso compartido).

## Puesta en marcha

```powershell
dotnet build -c Release
```

1. Crear el usuario del dashboard:

```powershell
dotnet run -c Release --project src/LicenciasCarpetas -- --add-user operador
```

2. Importar el Excel (una vez; se puede repetir cuando el archivo cambie):

```powershell
dotnet run -c Release --project src/LicenciasCarpetas -- --import "G:\...\DETALLE CARPETAS DEPTO. LICENCIAS DE CONDUCIR 2026.xlsx"
```

Salida de la importación real del libro 2026:

```
Hojas leídas:        20
Filas leídas:        20932
Casos nuevos:        20894
Casos actualizados:  38
Requieren revisión:  31
Días con contadores: 149
Correos de comunas:  494
```

3. Levantar el dashboard:

```powershell
dotnet run -c Release --project src/LicenciasCarpetas
```

Abre `https://localhost:5011` (el puerto HTTP 5010 solo redirige). Los puertos son distintos a los
de `outlook-comuna-router` (5000/5001) para que ambos puedan correr a la vez.

La ruta por defecto del Excel se configura en `Carpetas:DefaultWorkbookPath` (ver
`appsettings.Example.json`) y queda precargada en la pantalla `/Importar`.

## Pantallas

- `/` **Casos**: tabla paginada (100 por página) con edición en línea de nombre, RUT, fechas,
  estado, decisión e idoneidad; alta manual de filas nuevas; marcado personal; borrado; exportación
  a `.xlsx` de la vista filtrada.
- `/Estadisticas`: día a día del mes elegido — escaneadas y subidas editables, agendadas/atendidas y
  % de atención por oficina calculados, más el desglose por estado de carpeta y decisión final.
- `/Sector/Archivo` y `/Sector/Oficina43`: listado imprimible (imprimir del navegador → PDF) de las
  carpetas a retirar. Por defecto muestra solo los casos marcados; hay un enlace para ver todo el sector.
- `/Comunas`: directorio de correos por municipio.
- `/Importar`: importación desde el dashboard, con resumen y avisos.
- `/ChangePassword`: cambio de contraseña del operador.

## Pruebas

```powershell
dotnet test -c Release
```

89 pruebas: catálogos y variantes de escritura, validación de RUT, lectura de celdas (fecha real,
serial de Excel, texto tipeado), mapeo de filas, deduplicación al reimportar, filtros y paginación
del repositorio, estadísticas y saneado del libro.

## Detalles de implementación que conviene conocer

- **Validaciones de datos del Excel**: ClosedXML se niega a abrir el libro real porque las listas
  desplegables de las hojas `PLANTILLA` superan los 255 caracteres que acepta. La importación
  detecta ese fallo, hace una copia temporal sin los nodos `dataValidation` y la lee desde ahí
  (`Import/WorkbookSanitizer.cs`). El archivo original queda intacto.
- **La importación tarda ~90 segundos** para las ~21.000 filas del libro completo.
- **Datos personales en reposo**: la base SQLite guarda nombres y RUT sin cifrar; depende del
  cifrado de disco del equipo. Está en `data/`, ignorado por git.

## Estructura

```
src/LicenciasCarpetas/
  Domain/          # FolderCase, catálogos (estado, decisión, idoneidad, oficina), RUT, fechas en español
  Configuration/   # CarpetasOptions (bind de appsettings)
  Import/          # Lectura del .xlsx: hojas de agenda, contadores diarios, directorio de comunas
  Persistence/     # Repositorios SQLite (sin ORM)
  Statistics/      # Reconstrucción de la hoja ESCANEADAS Y SUBIDAS
  Reporting/       # Exportación a .xlsx
  Dashboard/       # Auth (PBKDF2) y páginas Razor
tests/LicenciasCarpetas.Tests/
```
