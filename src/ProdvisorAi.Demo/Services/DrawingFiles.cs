using System.Text.RegularExpressions;

namespace ProdvisorAi.Demo.Services;

/// <summary>A drawing ready for the model: PNG / JPG / PDF as-is, DXF rendered to PNG with the extracted dimensions.</summary>
public sealed record PreparedDrawing(string FileName, string ContentType, string Extension, byte[] Bytes, byte[]? SourceDxf, DxfConversion? Dxf);

public static class DrawingFiles
{
    /// <summary>Detects the type from the content (not the extension) and converts DXF. Returns an error message for bad files.</summary>
    public static (PreparedDrawing? Drawing, string? Error) Prepare(byte[] bytes, string fileName, DxfDrawingConverter dxf)
    {
        var (contentType, ext) = DetectType(bytes);
        if (contentType is null) return (null, "Nieobsługiwany typ pliku. Dozwolone: PNG, JPG, PDF, DXF.");
        if (contentType != "application/dxf") return (new PreparedDrawing(fileName, contentType, ext, bytes, null, null), null);
        try
        {
            var conversion = dxf.Convert(bytes);
            return (new PreparedDrawing(fileName, "image/png", ".png", conversion.Png, bytes, conversion), null);
        }
        catch (InvalidDataException ex)
        {
            return (null, $"Nie można przetworzyć pliku DXF: {ex.Message}.");
        }
    }

    static (string? ContentType, string Ext) DetectType(byte[] b)
    {
        if (b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return ("image/png", ".png");
        if (b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return ("image/jpeg", ".jpg");
        if (b.Length > 4 && b[0] == '%' && b[1] == 'P' && b[2] == 'D' && b[3] == 'F') return ("application/pdf", ".pdf");
        if (IsDxf(b)) return ("application/dxf", ".dxf");
        return (null, "");
    }

    /// <summary>ASCII DXF starts with group code 0 + SECTION (optionally after a 999 comment); binary DXF has a fixed sentinel.</summary>
    static bool IsDxf(byte[] b)
    {
        var head = System.Text.Encoding.ASCII.GetString(b, 0, Math.Min(b.Length, 256));
        return head.StartsWith("AutoCAD Binary DXF", StringComparison.Ordinal)
               || Regex.IsMatch(head, @"^\s*0\s*\r?\n\s*SECTION")
               || Regex.IsMatch(head, @"^\s*999\s*\r?\n");
    }
}
