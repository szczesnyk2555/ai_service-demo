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
    ];

    public static void Apply(AppDbContext db)
    {
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
