# Design: integrar Cambio de Domicilio en licencias-carpetas

## Technical Approach

Fold-in idéntico al de F8: código bajo `src/LicenciasCarpetas/CambioDomicilio/`, páginas bajo
`Dashboard/Pages/CambioDomicilio/`, todo registrado como singleton en `Program.cs` contra el mismo
`connectionString` de `carpetas.db`. Port literal de la lógica EWS/extracción/ruteo (sin refactor),
con renombres solo donde el nombre chocaría con un tipo ya existente en esta app.

## Target Folder Structure

```
src/LicenciasCarpetas/CambioDomicilio/
  CambioDomicilioOptions.cs        (raíz del módulo, igual que F8/F8Options.cs)
  Domain/    PersonRequest.cs (RequestStatus, CaseDestination, FolderSector*), DiscardedEmail.cs,
             ComunaRoutingEntry.cs (+ IncomingEmail), EmailShapeValidator.cs,
             DeadlineCalculator.cs, SpanishDate.cs
  Data/      ICambioDomicilioRequestRepository.cs + CambioDomicilioRequestRepository.cs
             IDiscardedEmailRepository.cs + DiscardedEmailRepository.cs
  Ews/       IEwsClient/EwsClient, EwsEmailReader, EwsFolderRef, EwsMailSender, EwsMessages,
             EwsResponseParser, IEmailReader, IMailSender, IEmailMover   (se pliega Mail/ en Ews/)
  Extraction/ PersonDataExtractor.cs, RutValidator.cs
  Routing/   AddressChangeRoutingService.cs, CambioDomicilioSyncService.cs (ex RouterWorker)
  Directories/ IComunaDirectory + ComunaDirectory.cs  (mismo CSV, mismo formato, sin tocar)
  Notifications/ INotificationChannel, EmailNotificationChannel, EmailTemplates,
                 WindowsToastNotificationChannel
  Reporting/ ICsvReportWriter + CsvReportWriter.cs
  Statistics/ CambioDomicilioStatisticsService.cs
src/LicenciasCarpetas/Dashboard/Pages/CambioDomicilio/
  Index, Discarded, Comunas, Estadisticas, Certificado, Sector (.cshtml + .cshtml.cs)
```

Se usa `Data/` (no `Persistence/`) por consistencia con `F8/Data/`. No se portan
`Dashboard/Auth/*`, `Login/Logout/ChangePassword/Forgot/ResetPassword`, `F8.cshtml`,
`SectorF8.cshtml` ni `Program.cs` de la hermana: login y F8 ya existen acá.

## Architecture Decisions

| Decisión | Elegido | Alternativa rechazada | Razón |
|---|---|---|---|
| `IPersonRequestRepository` → `ICambioDomicilioRequestRepository` | Renombrar | Mantener nombre | "PersonRequest" no dice nada en una app que ya tiene `FolderCase` y `UrgentRequest`; el prefijo de módulo lo ubica igual que `IUrgentRequestRepository`. El **tipo de dominio** `PersonRequest` se mantiene (es el nombre de la tabla y de todo el SQL portado). |
| `Domain.ComunaContact` (record CSV) → `ComunaRoutingEntry` | Renombrar | Mantener / fusionar | `LicenciasCarpetas.Domain.ComunaContact` ya existe (tabla de notificación). Convivir con dos `ComunaContact` obligaría a alias `using` en cada página. Cero fusión (decisión del proposal). |
| `StatisticsService` → `CambioDomicilioStatisticsService` | Renombrar | Mantener | `LicenciasCarpetas.Statistics.StatisticsService` ya está registrado **por tipo concreto** en `Program.cs:52`; dos tipos homónimos en el mismo contenedor es una trampa de lectura. |
| `RouterWorker : BackgroundService` → `CambioDomicilioSyncService` (singleton, sin `BackgroundService`) | Convertir | `AddHostedService` | Su `ExecuteAsync` solo llamaba `EnsureSchema()`, que acá lo hace `EnsureSchemas()`. Sin ciclo automático, `BackgroundService` es andamiaje muerto. `RunCycleAsync` pasa a `public`. |
| `Mail/` se pliega en `Ews/` | Sí | Carpeta propia | Son 2 interfaces (`IEmailReader`, `IMailSender`) cuya única implementación es EWS. |
| Sync síncrono | Mantener bloqueante | Background + poll | Paridad exacta, cero regresión (decisión del proposal). |

`FolderSector` y `DeadlineCalculator` conservan nombre: ya existen en `F8.Domain`, pero los
namespaces (`CambioDomicilio.Domain`) los separan y ninguna clase usa ambos.

## SQLite Schema

Tablas nuevas en `carpetas.db`: **`PersonRequest`**, **`DeletedSourceMessage`**, **`DiscardedEmail`**.

Sin colisión: las tablas existentes son `FolderCase`, `DailyCounter`, `ComunaContact`,
`DashboardUser`, `UrgentRequest`, `UrgentRequestFlag`, `UrgentImportRun` (verificado por grep de
`CREATE TABLE IF NOT EXISTS` en `src/`). No se prefija: los nombres ya son únicos y el SQL portado
queda intacto (un prefijo obligaría a reescribir cada query de los dos repositorios).

Wiring en `Program.cs`, dentro de `EnsureSchemas()` (línea ~258), después de
`IUrgentRequestRepository.EnsureSchema()`:

```csharp
services.GetRequiredService<ICambioDomicilioRequestRepository>().EnsureSchema();
services.GetRequiredService<IDiscardedEmailRepository>().EnsureSchema();
```

Corre después del `DatabaseBackup` (línea 93), así que el respaldo previo cubre el rollback.

## DI Registration (Program.cs, bloque nuevo tras el de F8, línea ~62)

```csharp
var cdOptions = builder.Configuration.GetSection(CambioDomicilioOptions.SectionName)
    .Get<CambioDomicilioOptions>() ?? new CambioDomicilioOptions();
builder.Services.AddSingleton(cdOptions);
builder.Services.AddSingleton<ICambioDomicilioRequestRepository>(_ => new CambioDomicilioRequestRepository(connectionString));
builder.Services.AddSingleton<IDiscardedEmailRepository>(_ => new DiscardedEmailRepository(connectionString));
builder.Services.AddSingleton<IComunaDirectory, ComunaDirectory>();
builder.Services.AddSingleton<IEwsClient, EwsClient>();
builder.Services.AddSingleton<EwsEmailReader>();
builder.Services.AddSingleton<IEmailReader>(sp => sp.GetRequiredService<EwsEmailReader>());
builder.Services.AddSingleton<IEmailMover>(sp => sp.GetRequiredService<EwsEmailReader>());
builder.Services.AddSingleton<IMailSender, EwsMailSender>();
builder.Services.AddSingleton<ICsvReportWriter, CsvReportWriter>();
builder.Services.AddSingleton<INotificationChannel, WindowsToastNotificationChannel>();
builder.Services.AddSingleton<INotificationChannel, EmailNotificationChannel>();
builder.Services.AddSingleton<AddressChangeRoutingService>();
builder.Services.AddSingleton<CambioDomicilioSyncService>();
builder.Services.AddSingleton<CambioDomicilioStatisticsService>();
```

`?? new CambioDomicilioOptions()` (no `throw`, a diferencia de `CarpetasOptions`) cumple el
criterio "sin sección configurada, el resto de la app arranca igual" — mismo patrón que `F8Options`.
Esto obliga a que **todas** las propiedades de `CambioDomicilioOptions` sean opcionales (no
`required`, a diferencia de `RouterOptions`); la ausencia se reporta al operador en la página, no
en el arranque.

## "Sincronizar ahora"

`Dashboard/Pages/CambioDomicilio/Index.cshtml.cs`, port directo de `IndexModel.OnPostSyncNowAsync`:

```csharp
public async Task<IActionResult> OnPostSyncNowAsync()
{
    var ran = await syncService.RunCycleAsync(HttpContext.RequestAborted);
    Message = ran ? "Sincronización completada."
                  : "Ya hay una sincronización en curso, intente en unos segundos.";
    MessageIsError = !ran;
    Load();
    return Page();
}
```

El `SemaphoreSlim cycleGuard = new(1,1)` vive como campo `private readonly` de
`CambioDomicilioSyncService`, registrado **singleton**: es lo único que hace válido el guard —
un scoped/transient daría un semáforo nuevo por request y no impediría nada. Los page models son
transient por diseño de Razor Pages, así que el guard NUNCA puede vivir en el page model.

## Config: `CambioDomicilio:` (mapeo 1:1 desde `RouterOptions`)

| `RouterOptions` | Clave nueva | Nota |
|---|---|---|
| `Ews.Url` | `CambioDomicilio:Ews:Url` | |
| `Ews.Username` | `CambioDomicilio:Ews:Username` | **User Secrets / env** |
| `Ews.Password` | `CambioDomicilio:Ews:Password` | **User Secrets / env** |
| `MailboxAddress` | `CambioDomicilio:MailboxAddress` | |
| `OwnDomain` | `CambioDomicilio:OwnDomain` | |
| `SourceFolderName` | `CambioDomicilio:SourceFolderName` | default `CARP. PARA PEDIR` |
| `ConfirmationFolderName` | `CambioDomicilio:ConfirmationFolderName` | default `CARP. YA SUBIDAS` |
| `PlazoDiasHabiles` | `CambioDomicilio:PlazoDiasHabiles` | default 15 |
| `ComunaDirectoryCsvPath` | `CambioDomicilio:ComunaDirectoryCsvPath` | resolver contra `AppContext.BaseDirectory` si es relativa (igual que `SqliteDbPath`) |
| `ReportCsvPath` | `CambioDomicilio:ReportCsvPath` | idem |
| `NotificationEmailAddress` | `CambioDomicilio:NotificationEmailAddress` | |
| `CertificateRequestEmailAddress` | `CambioDomicilio:CertificateRequestEmailAddress` | |
| `ToastNotificationsEnabled` | `CambioDomicilio:ToastNotificationsEnabled` | default true |
| `SqliteDbPath` | **eliminada** | usa la `carpetas.db` compartida |
| `PollIntervalMinutes` | **eliminada** | muerta: no hay polling automático |
| `PublicBaseUrl` | **eliminada** | solo se usaba para links de reset de contraseña, flujo que no se porta |

Secretos: el módulo manda su correo por **EWS**, no por SMTP, así que `Smtp:*` (usado por F8)
queda intacto y separado. En dev, `dotnet user-secrets set "CambioDomicilio:Ews:Password" ...`;
en prod, variables de entorno. `appsettings.json` lleva la sección **sin** `Username`/`Password`.

## Nav (`_Layout.cshtml:78`)

```diff
-<a href="https://localhost:5001" target="_blank" rel="noopener" class="nav-pill nav-module">Cambio de Domicilio</a>
+<a asp-page="/CambioDomicilio/Index" class="nav-pill nav-module">Cambio de Domicilio</a>
```

Actualizar también el comentario de las líneas 72-75 (ya no hay app externa). El gate real es
`[Authorize(Policy = "CambioDomicilioAccess")]` en cada page model — el nav solo oculta.

## Desvíos del port literal (explícitos)

1. `RouterWorker` deja de ser `BackgroundService` → `CambioDomicilioSyncService` singleton.
2. Tres renombres de tipo (`ICambioDomicilioRequestRepository`, `ComunaRoutingEntry`,
   `CambioDomicilioStatisticsService`) para evitar colisiones/ambigüedad.
3. `Persistence/` → `Data/`, `Mail/` → dentro de `Ews/`.
4. `RouterOptions` pierde 3 claves y todas sus propiedades dejan de ser `required`.
5. Se descarta `Dashboard/Auth/*`, `PasswordReset*`, `Mutex` de instancia única, `DataProtection`
   y `--smoke-test`: ya existen o no aplican en el host anfitrión.

## Testing Strategy

| Capa | Qué | Cómo |
|---|---|---|
| Unit | `RutValidator`, `PersonDataExtractor`, `ComunaDirectory.ResolveByDomain`, `DeadlineCalculator` | Portar los tests existentes de la hermana, ajustando namespaces |
| Integración | `CambioDomicilioRequestRepository` + `EnsureSchema` sobre SQLite temporal; convivencia con `FolderCase`/`UrgentRequest` en la misma db | Repo real contra archivo temp |
| E2E | `OnPostSyncNowAsync` con `IEmailReader` falso; gate de `CambioDomicilioAccess` sin claim → redirect a `/Index` | `WebApplicationFactory<Program>` |

## Migration / Rollout

Sin migración de datos: la `router.db` de la app hermana **no** se migra (fuera de alcance). El
módulo arranca vacío y se puebla en el primer "Sincronizar ahora". La hermana sigue viva.

## Open Questions

- [ ] ¿El CSV de comunas se copia al `AppContext.BaseDirectory` de esta app o se apunta a la ruta
      actual de la hermana? Un solo archivo compartido evita divergencia, pero acopla los deploys.
- [ ] `WindowsToastNotificationChannel` en un proceso que puede correr como servicio: hoy funciona
      porque la hermana corre en sesión de usuario. Verificar antes de habilitarlo.
