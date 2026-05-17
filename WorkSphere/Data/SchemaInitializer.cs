using Dapper;
using Npgsql;
using System.IO;

namespace WorkSphere.Data;

public static class SchemaInitializer
{
    public static async Task EnsureSchemaAsync(string connectionString)
    {
        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        try 
        {
            Console.WriteLine("Executing schema initialization...");
            
            // 1. Create Employees table
            await connection.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS Employees (
                    Id SERIAL PRIMARY KEY,
                    Name VARCHAR(100) NOT NULL,
                    Initials VARCHAR(5) NOT NULL UNIQUE
                );");

            // 2. Create Incidents table
            await connection.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS Incidents (
                    Id SERIAL PRIMARY KEY,
                    TicketNumber VARCHAR(50),
                    Title VARCHAR(200) NOT NULL,
                    Description TEXT,
                    StartedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    EndedAt TIMESTAMP WITH TIME ZONE,
                    IsClosed BOOLEAN DEFAULT FALSE
                );");

            // 3. Create WorkLogs table
            await connection.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS WorkLogs (
                    Id SERIAL PRIMARY KEY,
                    LogDate DATE NOT NULL,
                    LogTime TIME,
                    EmployeeId INT NOT NULL REFERENCES Employees(Id),
                    MainCategory VARCHAR(100) NOT NULL,
                    SubCategory VARCHAR(100) NOT NULL,
                    Details TEXT,
                    OriginalDetails TEXT,
                    IncidentId INT REFERENCES Incidents(Id),
                    EarnsCompTime BOOLEAN DEFAULT FALSE,
                    Hours DECIMAL(5,2) DEFAULT 0
                );");
            
            // 4. Check for local seed.sql and execute it
            // Look in the project root (up two levels from the executing assembly in some environments, 
            // but we'll try current directory first as that's where dotnet run usually starts)
            string seedPath = "seed.sql";
            if (File.Exists(seedPath))
            {
                Console.WriteLine("Found local seed.sql, executing...");
                string seedSql = await File.ReadAllTextAsync(seedPath);
                await connection.ExecuteAsync(seedSql);
                Console.WriteLine("Seed data applied.");
            }
            else
            {
                Console.WriteLine("No seed.sql found. Skipping data seeding.");
            }

            Console.WriteLine("Schema initialization complete.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Critical Error during schema initialization: {ex.Message}");
            throw;
        }
    }
}
