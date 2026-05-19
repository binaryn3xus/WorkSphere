using Dapper;
using Npgsql;
using System.IO;
using Serilog;

namespace WorkSphere.Data;

public static class SchemaInitializer
{
    public static async Task EnsureSchemaAsync(string connectionString)
    {
        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        try 
        {
            Log.Information("Executing schema initialization...");
            
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
                    Details TEXT,
                    StartedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    EndedAt TIMESTAMP WITH TIME ZONE,
                    IsClosed BOOLEAN DEFAULT FALSE
                );");

            // Migration: Rename Description to Details if it exists
            await connection.ExecuteAsync(@"
                DO $$ 
                BEGIN 
                    IF EXISTS (SELECT 1 FROM information_schema.columns 
                               WHERE table_name='incidents' AND column_name='description') THEN
                        ALTER TABLE Incidents RENAME COLUMN description TO details;
                    END IF;
                END $$;");

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
                    IncidentId INT REFERENCES Incidents(Id) ON DELETE SET NULL,
                    EarnsCompTime BOOLEAN DEFAULT FALSE,
                    Hours DECIMAL(5,2) DEFAULT 0
                );");

            // Migration: Update IncidentId foreign key to ON DELETE SET NULL if not already set
            await connection.ExecuteAsync(@"
                DO $$ 
                BEGIN 
                    IF EXISTS (SELECT 1 FROM information_schema.table_constraints 
                               WHERE constraint_name='worklogs_incidentid_fkey' AND table_name='worklogs') THEN
                        ALTER TABLE WorkLogs DROP CONSTRAINT worklogs_incidentid_fkey;
                        ALTER TABLE WorkLogs ADD CONSTRAINT worklogs_incidentid_fkey 
                            FOREIGN KEY (IncidentId) REFERENCES Incidents(Id) ON DELETE SET NULL;
                    END IF;
                END $$;");
            
            // 4. Check for local seed.sql and execute it
            string seedPath = "seed.sql";
            if (File.Exists(seedPath))
            {
                Log.Information("Found local seed.sql, executing...");
                string seedSql = await File.ReadAllTextAsync(seedPath);
                await connection.ExecuteAsync(seedSql);
                Log.Information("Seed data applied.");
            }

            Log.Information("Schema initialization complete.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Critical Error during schema initialization");
            throw;
        }
    }
}
