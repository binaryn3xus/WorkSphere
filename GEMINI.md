# WorkSphere

WorkSphere is a comprehensive work logging and employee management system built with **.NET 9.0** and **Blazor**. It features a modern, interactive UI and a robust backend designed for tracking daily activities, managing employee records, and generating insightful reports.

## Project Overview

### Tech Stack
- **Framework:** .NET 9.0 Blazor Web App (Interactive Server)
- **UI Components:** [MudBlazor](https://mudblazor.com/)
- **Database:** PostgreSQL
- **Data Access:** [Dapper](https://github.com/DapperLib/Dapper) (Lightweight ORM)
- **Database Driver:** [Npgsql](https://www.npgsql.org/)
- **Containerization:** Docker & Kubernetes (K8s)

### Architecture
- **WorkSphere:** The main Blazor Web App project. It handles database initialization, provides API services, and serves the interactive components using **Server** render mode.
- **Data Layer:** Uses Dapper with custom type handlers (`DapperTypeHandlers.cs`) for modern .NET types like `DateOnly` and `TimeOnly`.
- **Service Layer:** 
  - `WorkLogService`: Manages CRUD operations for employees, work logs, and incidents. It also handles statistical queries for categories, employee activity, and comp time.
  - `MigrationService`: Handles importing work logs from Markdown-formatted daily log files.
- **Features:**
  - **Incident Tracking:** Track incidents with ticket numbers and link them to work logs.
  - **Comp Time Tracker:** Automatically calculate comp time earned based on logs marked as "Comp Time".
  - **Analytics Dashboard:** Visual representation of log distribution, employee activity, and comp time stats.

---

## Building and Running

### Prerequisites
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
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

---

## Development Conventions

### Coding Style
- Follow standard C# and .NET naming conventions (PascalCase for public members, camelCase for private fields and local variables).
- Use **File-scoped namespaces** for cleaner code.
- Prefer **Primary Constructors** where appropriate (introduced in C# 12).

### UI/Frontend
- Utilize **MudBlazor** components for all UI elements to maintain visual consistency.
- Define common layouts and imports in `_Imports.razor` and the `Layout` folder.

### Data Access
- All database interactions should go through `WorkLogService`.
- Use Dapper for SQL queries. Avoid complex EF Core mapping unless explicitly required.
- Ensure `DapperTypeHandlers.Register()` is called at startup (currently in `Program.cs`).

### Testing
- **Status:** No automated tests are currently present in the solution.
- **Guideline:** When adding features, prioritize creating a separate test project (e.g., `WorkSphere.Tests`) using xUnit or NUnit.

### Database Migrations
- The project uses a custom `SchemaInitializer` for basic schema setup.
- For complex changes, update `init.sql` and the `EnsureSchemaAsync` method in `SchemaInitializer.cs`.
