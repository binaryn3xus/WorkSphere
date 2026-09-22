# WorkSphere

WorkSphere is a comprehensive work logging and employee management system built with **.NET 10.0** and **Blazor**. It features a modern, interactive UI and a robust backend designed for tracking daily activities, managing employee records, and generating insightful reports.

## Project Overview

### Tech Stack
- **Framework:** .NET 10.0 Blazor Web App (Interactive Server)
- **UI Components:** [MudBlazor](https://mudblazor.com/)
- **Database:** PostgreSQL
- **Data Access:** [Dapper](https://github.com/DapperLib/Dapper) (Lightweight ORM)
- **Database Driver:** [Npgsql](https://www.npgsql.org/)
- **Containerization:** Docker & Kubernetes (K8s)

### Architecture
- **WorkSphere:** The main Blazor Web App project. It handles database initialization, provides API services, and serves the interactive components using **Server** render mode.
- **Data Layer:** Uses Dapper with custom type handlers (`DapperTypeHandlers.cs`) for modern .NET types like `DateOnly` and `TimeOnly`.
- **Service Layer:** 
  - `WorkLogService` (`IWorkLogService`): Manages CRUD operations for employees, work logs, and incidents. It also handles statistical queries for categories, employee activity, and comp time.
  - `RestoreService` (`IRestoreService`): Handles restoring complete JSON database snapshots and importing RFC 4180 CSV work log exports. Features automatic deduplication, dynamic ID remapping (employees and incidents), and auto-creation of missing employee records. Also manages discovering existing server-side backup snapshots.
  - `MigrationService`: Legacy parser utility used for Markdown daily log audits in schedule analysis.
  - `ExportService` (`IExportService`): Generates RFC 4180 CSV exports, Markdown notes, and complete JSON database snapshots into timestamped snapshot folders (`snapshot-YYYYMMDD-HHmmss`). Also provides headless CLI execution for Kubernetes CronJobs and automated backup retention cleanup (`MaxBackupHistory`).
  - `ExportBackgroundService`: Built-in .NET `BackgroundService` that automatically runs scheduled database exports and backups via Cron expressions while the web host is running.
  - `UserTimeService` (`IUserTimeService`): Resolves the client user's local date and time via client browser cookie (`ws_tz_offset`), with fallback to configured time zone (`TimeZone` / `APP_TIMEZONE` / `TZ` environment variables) or server local time, preventing UTC calendar skew in containerized environments.
- **Features:**
  - **Incident Tracking:** Track incidents with ticket numbers and link them to work logs.
  - **Comp Time Tracker:** Automatically calculate comp time earned based on logs marked as "Comp Time".
  - **Analytics Dashboard:** Visual representation of log distribution, employee activity, comp time stats, and customizable date range presets with employee filtering.
  - **Data Export Hub:** Web-based and server-side exports supporting customizable CSVs, Markdown notes, and full JSON database snapshots.
  - **Data Restore & Import Hub:** Restore from server-side snapshot folders or upload `.json` / `.csv` files directly with automatic deduplication and ID remapping.
  - **Automated Scheduled Backups:** In-process scheduled backups via `ExportBackgroundService` (Cron schedule) with snapshot history retention management, as well as headless CLI `--backup` / `--export` mode for Kubernetes CronJobs.
  - **Client Preferences:** LocalStorage persistence for selected theme (Dark/Light mode) and preferred employee auto-selection on quick logging.

---

## Building and Running

### Prerequisites
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [PostgreSQL](https://www.postgresql.org/) database

### Configuration
1. Ensure a PostgreSQL instance is running.
2. Update the `DefaultConnection` string in `WorkSphere/appsettings.json`:
   ```json
   "ConnectionStrings": {
     "DefaultConnection": "Host=your_host;Database=worksphere;Username=your_user;Password=your_password"
   }
   ```

### Execution
Run the project using the .NET CLI:
```bash
dotnet run --project WorkSphere
```
The application will automatically attempt to initialize the database schema on startup via `SchemaInitializer`.

### Docker
To build and run via Docker:
```bash
docker build -t worksphere .
docker run -p 8080:8080 worksphere
```

### Automated Backups (Kubernetes CronJob / CLI)
WorkSphere can run headlessly to export database snapshots, CSVs, and Obsidian markdown notes into an arbitrary storage volume:
```bash
dotnet run --project WorkSphere -- --backup
# Or override output path:
dotnet run --project WorkSphere -- --backup --output /mnt/backups
```

**Kubernetes CronJob Example:**
```yaml
apiVersion: batch/v1
kind: CronJob
metadata:
  name: worksphere-backup
spec:
  schedule: "0 2 * * *" # Daily at 2:00 AM
  jobTemplate:
    spec:
      template:
        spec:
          containers:
          - name: backup
            image: worksphere:latest
            args: ["--backup"]
            env:
            - name: ConnectionStrings__DefaultConnection
              valueFrom:
                secretKeyRef:
                  name: worksphere-secrets
                  key: db-connection
            - name: EXPORT_PATH
              value: "/backups"
            - name: TZ
              value: "America/New_York"
            volumeMounts:
            - name: backup-storage
              mountPath: /backups
          restartPolicy: OnFailure
          volumes:
          - name: backup-storage
            persistentVolumeClaim:
              claimName: worksphere-backup-pvc
```

---

## Development Conventions

### Coding Style
- Follow standard C# and .NET naming conventions (PascalCase for public members, camelCase for private fields and local variables).
- Use **File-scoped namespaces** for cleaner code.
- Prefer **Primary Constructors** where appropriate (introduced in C# 12).

### UI/Frontend
- Utilize **MudBlazor** components for all UI elements to maintain visual consistency.
- Define common layouts and imports in `_Imports.razor` and the `Layout` folder.
- Shared CSS and JavaScript utilities are maintained in `wwwroot/app.css` and `wwwroot/app.js`. Scoped CSS files (`*.razor.css`) or `app.css` should be used instead of inline `<style>` tags.

### Data Access
- All database interactions should go through `IWorkLogService` / `WorkLogService`.
- Use Dapper for SQL queries. Avoid complex EF Core mapping unless explicitly required.
- Ensure `DapperTypeHandlers.Register()` is called at startup (currently in `Program.cs`).

### Testing
- **Status:** Automated test suite is located in `WorkSphere.Tests` using **xUnit**, **bUnit**, and **Moq**.
- **Guideline:** When adding features or refactoring UI components and services, write corresponding unit and bUnit tests in `WorkSphere.Tests`. Run tests with `dotnet test`.

### Database Migrations
- The project uses a custom `SchemaInitializer` for basic schema setup.
- For complex changes, update `init.sql` and the `EnsureSchemaAsync` method in `SchemaInitializer.cs`.
