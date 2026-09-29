using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using netDxf;
using netDxf.Entities;
using SkiaSharp;

namespace ProdvisorAi.Demo.Services;

/// <summary>Result of converting a DXF drawing for the model.</summary>
public sealed record DxfConversion(
    byte[] Png,                        // rendered model space, sent to OpenAI as input_image
    string ExtractedText,              // exact dimensions + texts, sent to OpenAI as input_text
    int DimensionCount,
    int TextCount,
    IReadOnlyList<string> Warnings);

/// <summary>
/// DXF support. OpenAI accepts images and PDFs, not CAD files, so a DXF is:
///  1. rendered to PNG (model space: lines, arcs, circles, polylines, splines, ellipses, texts, dimensions, blocks), and
///  2. mined for EXACT data: dimension values (incl. user texts like "%%c25h6") and all texts, also from paper-space
///     layouts where the title block usually lives. The model gets both: the picture for geometry, the text for numbers.
/// Uses netDxf (MIT) for reading and SkiaSharp (MIT) for rendering.
/// </summary>
public sealed partial class DxfDrawingConverter(ILogger<DxfDrawingConverter> log)
{
    const int TargetLongSide = 2400;   // px; high enough for small dimension texts, within OpenAI image limits
    const int Margin = 40;
    const int MaxBlockDepth = 8;
    const int MaxListedItems = 400;

    // ---- geometry collected from the DXF (world coordinates, drawing units) --------------------------
    sealed record Stroke(List<(double X, double Y)> Points, bool Closed, bool Dashed);
    sealed record Label(string Text, double X, double Y, double Height, double RotationDeg, double AlignX, double AlignY);
    sealed record Fill(List<(double X, double Y)> Points);

    sealed class Scene
    {
        public List<Stroke> Strokes { get; } = [];
        public List<Label> Labels { get; } = [];
        public List<Fill> Fills { get; } = [];
        public Dictionary<string, int> Skipped { get; } = [];
    }

    public DxfConversion Convert(byte[] dxfBytes)
    {
        DxfDocument doc;
        try
        {
            using var ms = new MemoryStream(dxfBytes);
            doc = DxfDocument.Load(ms) ?? throw new InvalidDataException("unsupported or invalid DXF");
        }
        catch (Exception ex) when (ex is not InvalidDataException)
        {
            throw new InvalidDataException($"cannot read DXF ({ex.Message})", ex);
        }

        // 1. Render model space
        var scene = new Scene();
        foreach (var e in doc.Entities.All) Collect(e, scene, 0);
        if (scene.Strokes.Count == 0 && scene.Labels.Count == 0)
            throw new InvalidDataException("the DXF model space contains no drawable entities");
        var png = Render(scene);

        // 2. Extract exact data (all layouts: model + paper space)
        var dims = new List<string>();
        var texts = new List<string>();
        foreach (var layout in doc.Layouts)
        {
            var entities = layout.Name == "Model" ? doc.Entities.All : layout.AssociatedBlock?.Entities ?? Enumerable.Empty<EntityObject>();
            foreach (var e in entities) Extract(e, dims, texts, 0);
        }
        var uniqueTexts = texts.Where(t => t.Length > 0).Distinct().ToList();

        var warnings = scene.Skipped.Select(kv => $"{kv.Value} x {kv.Key} not rendered").ToList();
        if (warnings.Count > 0) log.LogInformation("DXF conversion: {Warnings}", string.Join(", ", warnings));

        return new DxfConversion(png, BuildPromptText(doc, dims, uniqueTexts), dims.Count, uniqueTexts.Count, warnings);
    }

    // ============================================================================================
    // Geometry
    // ============================================================================================
    void Collect(EntityObject e, Scene s, int depth)
    {
        if (!e.IsVisible || e.Layer is { IsVisible: false } || e.Layer is { IsFrozen: true }) return;
        var dashed = IsDashed(e);

        switch (e)
        {
            case Line l:
                s.Strokes.Add(new([(l.StartPoint.X, l.StartPoint.Y), (l.EndPoint.X, l.EndPoint.Y)], false, dashed));
                break;
            case Arc a:
                s.Strokes.Add(new(ArcPoints(a.Center.X, a.Center.Y, a.Radius, a.StartAngle, a.EndAngle, a.Normal.Z < 0), false, dashed));
                break;
            case Circle c:
                s.Strokes.Add(new(ArcPoints(c.Center.X, c.Center.Y, c.Radius, 0, 360, c.Normal.Z < 0), true, dashed));
                break;
            case Polyline2D p:
                var flip = p.Normal.Z < 0;
                s.Strokes.Add(new(p.PolygonalVertexes(8).Select(v => (flip ? -v.X : v.X, v.Y)).ToList(), p.IsClosed, dashed));
                break;
            case Polyline3D p3:
                s.Strokes.Add(new(p3.Vertexes.Select(v => (v.X, v.Y)).ToList(), p3.IsClosed, dashed));
                break;
            case Ellipse el:
                s.Strokes.Add(new(el.PolygonalVertexes(72).Select(v => (el.Center.X + v.X, el.Center.Y + v.Y)).ToList(), el.IsFullEllipse, dashed));
                break;
            case Spline sp:
                s.Strokes.Add(new(sp.PolygonalVertexes(64).Select(v => (v.X, v.Y)).ToList(), sp.IsClosed, dashed));
                break;
            case Point:   // defpoints - construction helpers, never drawn
                break;
            case Solid so:   // arrowheads in dimension blocks
                s.Fills.Add(new([(so.FirstVertex.X, so.FirstVertex.Y), (so.SecondVertex.X, so.SecondVertex.Y),
                                 (so.FourthVertex.X, so.FourthVertex.Y), (so.ThirdVertex.X, so.ThirdVertex.Y)]));
                break;
            case Text t:
                var (tx, ty) = TextAlign(t.Alignment.ToString());
                s.Labels.Add(new(DecodeSpecialChars(t.Value), t.Position.X, t.Position.Y, t.Height, t.Rotation, tx, ty));
                break;
            case MText mt:
                var (mx, my) = TextAlign(mt.AttachmentPoint.ToString());
                s.Labels.Add(new(CleanMText(mt), mt.Position.X, mt.Position.Y, mt.Height, mt.Rotation, mx, my));
                break;
            case Dimension d when depth < MaxBlockDepth:
                // The dimension's anonymous block holds the drawn lines, arrows and text (in world coordinates).
                // Some CAD programs do not write these blocks - then let netDxf build them from the definition points.
                if (d.Block is null || d.Block.Entities.Count == 0)
                {
                    try { d.Update(); } catch (Exception ex) { log.LogDebug("Dimension block rebuild failed: {Error}", ex.Message); }
                }
                foreach (var child in d.Block?.Entities ?? Enumerable.Empty<EntityObject>()) Collect(child, s, depth + 1);
                break;
            case Insert ins when depth < MaxBlockDepth:
                foreach (var child in ins.Explode()) Collect(child, s, depth + 1);
                break;
            default:
                var name = e.GetType().Name;
                s.Skipped[name] = s.Skipped.GetValueOrDefault(name) + 1;
                break;
        }
    }

    static List<(double, double)> ArcPoints(double cx, double cy, double r, double startDeg, double endDeg, bool mirrored)
    {
        if (endDeg <= startDeg) endDeg += 360;
        var steps = Math.Max(8, (int)((endDeg - startDeg) / 5));
        var pts = new List<(double, double)>(steps + 1);
        for (var i = 0; i <= steps; i++)
        {
            var a = (startDeg + (endDeg - startDeg) * i / steps) * Math.PI / 180;
            var x = cx + r * Math.Cos(a);
            pts.Add((mirrored ? -x : x, cy + r * Math.Sin(a)));   // OCS with normal (0,0,-1) mirrors X
        }
        return pts;
    }

    static bool IsDashed(EntityObject e)
    {
        var lt = e.Linetype?.Name ?? "";
        if (lt.Equals("ByLayer", StringComparison.OrdinalIgnoreCase)) lt = e.Layer?.Linetype?.Name ?? "";
        return Regex.IsMatch(lt, "CENTER|DASH|HIDDEN|PHANTOM|DOT|DIVIDE|BORDER|ACAD_ISO", RegexOptions.IgnoreCase);
    }

    /// <summary>Alignment name (e.g. "MiddleCenter", "TopLeft", "BaselineRight") -> fraction of text box to shift.</summary>
    static (double X, double Y) TextAlign(string name)
    {
        double x = name.EndsWith("Center") || name == "Middle" ? 0.5 : name.EndsWith("Right") ? 1 : 0;
        double y = name.StartsWith("Top") ? 1 : name.StartsWith("Middle") ? 0.5 : 0;   // 0 = baseline / bottom
        return (x, y);
    }

    // ============================================================================================
    // Rendering (SkiaSharp)
    // ============================================================================================
    static byte[] Render(Scene s)
    {
        // Bounding box of everything (texts approximated by their insertion point + extent)
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        void Grow(double x, double y) { minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); }
        foreach (var st in s.Strokes) foreach (var p in st.Points) Grow(p.X, p.Y);
        foreach (var f in s.Fills) foreach (var p in f.Points) Grow(p.X, p.Y);
        foreach (var l in s.Labels) { Grow(l.X, l.Y); Grow(l.X + l.Height * 0.6 * FirstLine(l.Text).Length, l.Y + l.Height); }

        var w = Math.Max(maxX - minX, 1e-6);
        var h = Math.Max(maxY - minY, 1e-6);
        var scale = (TargetLongSide - 2 * Margin) / Math.Max(w, h);
        var width = (int)Math.Ceiling(w * scale) + 2 * Margin;
        var height = (int)Math.Ceiling(h * scale) + 2 * Margin;

        // world -> pixel (Y axis flipped)
        SKPoint P(double x, double y) => new((float)((x - minX) * scale + Margin), (float)((maxY - y) * scale + Margin));

        using var bmp = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.White);

        var lineWidth = Math.Max(1.6f, TargetLongSide / 1400f);
        using var solid = new SKPaint { Color = SKColors.Black, IsAntialias = true, StrokeWidth = lineWidth, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
        using var dashed = solid.Clone();
        dashed.StrokeWidth = lineWidth * 0.8f;
        dashed.PathEffect = SKPathEffect.CreateDash([18f, 5f, 3f, 5f], 0);
        using var fill = new SKPaint { Color = SKColors.Black, IsAntialias = true, Style = SKPaintStyle.Fill };

        SKPath Poly(List<(double X, double Y)> points, bool closed)
        {
            using var builder = new SKPathBuilder();
            builder.AddPoly(points.Select(p => P(p.X, p.Y)).ToArray(), closed);
            return builder.Detach();
        }

        foreach (var st in s.Strokes.Where(x => x.Points.Count > 1))
        {
            using var path = Poly(st.Points, st.Closed);
            canvas.DrawPath(path, st.Dashed ? dashed : solid);
        }

        foreach (var f in s.Fills)
        {
            using var path = Poly(f.Points, true);
            canvas.DrawPath(path, fill);
        }

        using var typeface = SKTypeface.FromFamilyName("Arial") ?? SKTypeface.Default;
        foreach (var l in s.Labels)
        {
            var size = (float)(l.Height * scale);
            if (size < 3) continue;   // unreadable anyway
            using var font = new SKFont(typeface, size);
            var lines = l.Text.Split('\n');
            var textW = lines.Max(line => font.MeasureText(line));
            var blockH = size * 1.25f * lines.Length;

            canvas.Save();
            var origin = P(l.X, l.Y);
            canvas.Translate(origin);
            canvas.RotateDegrees((float)-l.RotationDeg);
            // Shift by alignment: X fraction of width; Y: 0 = baseline of the last line, 1 = top of the block.
            var dx = (float)(-textW * l.AlignX);
            var top = l.AlignY switch { >= 1 => 0f, >= 0.5 => -blockH / 2, _ => -blockH + size * 0.25f };
            for (var i = 0; i < lines.Length; i++)
                canvas.DrawText(lines[i], dx, top + size * (1 + 1.25f * i), SKTextAlign.Left, font, fill);
            canvas.Restore();
        }

        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    static string FirstLine(string text) => text.Split('\n')[0];

    // ============================================================================================
    // Text / dimension extraction
    // ============================================================================================
    void Extract(EntityObject e, List<string> dims, List<string> texts, int depth)
    {
        if (dims.Count > MaxListedItems || texts.Count > MaxListedItems * 2) return;
        switch (e)
        {
            case Dimension d:
                dims.Add(DescribeDimension(d));
                break;
            case Text t:
                texts.Add(DecodeSpecialChars(t.Value).Trim());
                break;
            case MText mt:
                texts.Add(CleanMText(mt).Replace('\n', ' ').Trim());
                break;
            case Insert ins when depth < MaxBlockDepth:
                foreach (var a in ins.Attributes) texts.Add(DecodeSpecialChars(a.Value ?? "").Trim());   // title block attributes
                foreach (var child in ins.Block?.Entities ?? Enumerable.Empty<EntityObject>()) Extract(child, dims, texts, depth + 1);
                break;
        }
    }

    static string DescribeDimension(Dimension d)
    {
        // Best source: the text the dimension actually DISPLAYS (from its block), exactly as on the drawing.
        var displayed = string.Join(" ", (d.Block?.Entities ?? Enumerable.Empty<EntityObject>())
            .Select(e => e switch { MText m => CleanMText(m), Text t => DecodeSpecialChars(t.Value), _ => null })
            .Where(s => !string.IsNullOrWhiteSpace(s))).Replace('\n', ' ').Trim();
        var typeName = d is LinearDimension or AlignedDimension ? "linear" : d.DimensionType.ToString().ToLowerInvariant();
        if (displayed.Length > 0) return $"{typeName}: {displayed}";

        // For linear (rotated) dimensions compute the value from the definition points, projected on the
        // dimension direction - netDxf's Measurement after loading returns the point-to-point (aligned) distance.
        var (kind, measured) = d switch
        {
            LinearDimension ld => ("linear", Math.Abs(Vector2.DotProduct(
                ld.SecondReferencePoint - ld.FirstReferencePoint,
                new Vector2(Math.Cos(ld.Rotation * Math.PI / 180), Math.Sin(ld.Rotation * Math.PI / 180))))),
            AlignedDimension ad => ("aligned", Vector2.Distance(ad.FirstReferencePoint, ad.SecondReferencePoint)),
            _ => (d.DimensionType.ToString().ToLowerInvariant(), d.Measurement),
        };
        var value = d.DimensionType switch
        {
            DimensionType.Diameter => $"Ø{Num(measured)}",
            DimensionType.Radius => $"R{Num(measured)}",
            DimensionType.Angular or DimensionType.Angular3Point => $"{Num(measured)}°",
            _ => Num(measured),
        };

        // UserText: null/"" = measured value, "<>" = placeholder for the measured value, " " = text suppressed.
        var shown = d.UserText switch
        {
            null or "" => value,
            " " => "(text suppressed)",
            var u => CleanFormatting(DecodeSpecialChars(u.Replace("<>", value))),
        };
        return shown == value ? $"{kind}: {value}" : $"{kind}: {shown}  (measured {Num(measured)})";
    }

    static string Num(double v) => Math.Round(v, 3).ToString("0.###", CultureInfo.InvariantCulture);

    static string BuildPromptText(DxfDocument doc, List<string> dims, List<string> texts)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Units (INSUNITS): {doc.DrawingVariables.InsUnits}");
        sb.AppendLine($"Dimensions ({dims.Count}):");
        foreach (var d in dims.Take(MaxListedItems)) sb.AppendLine($"- {d}");
        sb.AppendLine($"Texts ({texts.Count}, incl. title block):");
        foreach (var t in texts.Take(MaxListedItems)) sb.AppendLine($"- {t}");
        return sb.ToString();
    }

    /// <summary>AutoCAD control codes: %%c = Ø, %%d = °, %%p = ±, %%u/%%o = underline/overline (dropped).</summary>
    static string DecodeSpecialChars(string s) => s
        .Replace("%%c", "Ø", StringComparison.OrdinalIgnoreCase)
        .Replace("%%d", "°", StringComparison.OrdinalIgnoreCase)
        .Replace("%%p", "±", StringComparison.OrdinalIgnoreCase)
        .Replace("%%u", "", StringComparison.OrdinalIgnoreCase)
        .Replace("%%o", "", StringComparison.OrdinalIgnoreCase)
        .Replace("%%%", "%");

    static string CleanMText(MText mt)
    {
        string plain;
        try { plain = mt.PlainText(); } catch { plain = mt.Value ?? ""; }
        return CleanFormatting(DecodeSpecialChars(plain));
    }

    /// <summary>Removes MTEXT formatting codes: \P (new line), \S+0,1^-0,2; (stacked tolerances), {\f...;}, \H..; etc.</summary>
    static string CleanFormatting(string s)
    {
        s = StackedRegex().Replace(s, m => $" {m.Groups[1].Value}/{m.Groups[2].Value}");
        s = s.Replace("\\P", "\n");
        s = FormatCodeRegex().Replace(s, "");
        return s.Replace("{", "").Replace("}", "").Trim();
    }

    [GeneratedRegex(@"\\S([^^/#;]*)[\^/#]([^;]*);")]
    private static partial Regex StackedRegex();

    [GeneratedRegex(@"\\[A-OQ-Za-oq-z][^;\\{}]*;?")]
    private static partial Regex FormatCodeRegex();
}
