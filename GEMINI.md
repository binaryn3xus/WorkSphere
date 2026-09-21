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
  - `MigrationService`: Handles importing legacy work logs from Markdown-formatted daily log files.
  - `ExportService` (`IExportService`): Generates RFC 4180 CSV exports, Obsidian markdown archives, and complete JSON database snapshots. Also provides headless CLI execution for Kubernetes CronJobs.
  - `ExportBackgroundService`: Built-in .NET `BackgroundService` that automatically runs scheduled database exports and backups while the web host is running.
- **Features:**
  - **Incident Tracking:** Track incidents with ticket numbers and link them to work logs.
  - **Comp Time Tracker:** Automatically calculate comp time earned based on logs marked as "Comp Time".
  - **Analytics Dashboard:** Visual representation of log distribution, employee activity, comp time stats, and customizable date range presets with employee filtering.
  - **Data Export Hub:** Web-based and server-side exports supporting customizable CSVs, Obsidian monthly markdown, and full JSON database backups.
  - **Automated Scheduled Backups:** In-process daily scheduled backups via `ExportBackgroundService` as well as headless CLI `--backup` / `--export` mode for Kubernetes CronJobs.
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
3. (Optional) Configure the `Migration:LogsPath` if you intend to use the migration service.

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
