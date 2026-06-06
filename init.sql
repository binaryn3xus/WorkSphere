-- This script reflects the current schema for WorkSphere
-- It can be used for manual database setup or as a reference for the SchemaInitializer

-- 1. Create Employees table
CREATE TABLE IF NOT EXISTS Employees (
    Id SERIAL PRIMARY KEY,
    Name VARCHAR(100) NOT NULL,
    Initials VARCHAR(5) NOT NULL UNIQUE
);

-- 2. Create Incidents table
CREATE TABLE IF NOT EXISTS Incidents (
    Id SERIAL PRIMARY KEY,
    TicketNumber VARCHAR(50),
    Title VARCHAR(200) NOT NULL,
    Details TEXT,
    StartedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    EndedAt TIMESTAMP WITH TIME ZONE,
    IsClosed BOOLEAN DEFAULT FALSE
);

-- 3. Create WorkLogs table
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
    UsesCompTime BOOLEAN DEFAULT FALSE,
    Hours DECIMAL(5,2) DEFAULT 0
);

-- 4. Grant Permissions (Modify 'worksphere_admin' to match your actual database user)
-- GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO worksphere_admin;
-- GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public TO worksphere_admin;
