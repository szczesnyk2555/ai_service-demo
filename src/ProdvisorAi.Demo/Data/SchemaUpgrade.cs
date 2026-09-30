using Microsoft.EntityFrameworkCore;

namespace ProdvisorAi.Demo.Data;

/// <summary>
/// The demo uses EnsureCreated (no EF migrations). Columns added in later versions are added here to
/// databases created by an older version, so existing installations keep their data.
/// </summary>
public static class SchemaUpgrade
{
    static readonly (string Table, string Column, string Type)[] AddedColumns =
    [
        ("Jobs", "SourceDrawingPath", "TEXT NULL"),
        ("Jobs", "DrawingText", "TEXT NULL"),
        ("Machines", "ProdvisorId", "INTEGER NULL"),
        ("Machines", "TypeId", "INTEGER NULL"),
        ("Machines", "Kind", "INTEGER NULL"),
        ("Machines", "IsVirtual", "INTEGER NOT NULL DEFAULT 0"),
        ("Machines", "ParametersJson", "TEXT NULL"),
        ("Machines", "WorkplaceId", "INTEGER NULL"),
        ("Machines", "GroupCode", "TEXT NULL"),
        ("Machines", "GroupId", "INTEGER NULL"),
        ("Machines", "GroupName", "TEXT NULL"),
        ("Machines", "Hall", "INTEGER NULL"),
        ("Machines", "MultiOperation", "INTEGER NOT NULL DEFAULT 0"),
        ("Machines", "Multiperson", "INTEGER NOT NULL DEFAULT 0"),
        ("Machines", "WorkplacesGroup", "INTEGER NULL"),
        ("Jobs", "ElementId", "TEXT NULL"),
        ("Jobs", "TechnologyPrompt", "TEXT NULL"),
        ("Jobs", "SystemPromptId", "TEXT NULL"),
        ("Jobs", "PartName", "TEXT NULL"),
    ];

    public static void Apply(AppDbContext db)
    {
        // Tables added in later versions (EnsureCreated only creates tables for a brand-new database).
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "Materials" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_Materials" PRIMARY KEY,
                "Name" TEXT NOT NULL,
                "AlloyId" INTEGER NULL,
                "Active" INTEGER NOT NULL)
            """);
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "SystemPrompts" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_SystemPrompts" PRIMARY KEY,
                "Name" TEXT NOT NULL, "Text" TEXT NOT NULL, "IsDefault" INTEGER NOT NULL, "UpdatedAt" TEXT NOT NULL)
            """);
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "Elements" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_Elements" PRIMARY KEY,
                "Number" TEXT NOT NULL, "Name" TEXT NOT NULL, "Material" TEXT NULL, "Quantity" INTEGER NULL, "DrawingFile" TEXT NOT NULL)
            """);
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "SavedTechnologies" (
                "ElementId" TEXT NOT NULL CONSTRAINT "PK_SavedTechnologies" PRIMARY KEY,
                "JobId" TEXT NOT NULL, "SavedAt" TEXT NOT NULL, "Json" TEXT NOT NULL)
            """);

        foreach (var (table, column, type) in AddedColumns)
        {
            var exists = db.Database
                .SqlQuery<string>($"SELECT name AS Value FROM pragma_table_info({table})")   // parameterized
                .AsEnumerable()
                .Contains(column, StringComparer.OrdinalIgnoreCase);
            if (!exists)
            {
                // DDL cannot be parameterized; table/column/type are compile-time constants from the list above.
                var sql = "ALTER TABLE \"" + table + "\" ADD COLUMN \"" + column + "\" " + type;
                db.Database.ExecuteSqlRaw(sql);
            }
        }
    }
}
