// Generates samples/sample-drawing-shaft.dxf - stepped shaft Ø25h6 / Ø40 / Ø30k6 x 180 with keyway 8P9, material C45.
// Units: mm. Same part as samples/sample-drawing-shaft.png, but as a real DXF with DIMENSION entities.
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Tables;

var output = args.Length > 0 ? args[0] : Path.Combine(FindRepoRoot(), "samples", "sample-drawing-shaft.dxf");

var doc = new DxfDocument(DxfVersion.AutoCad2018);   // 2007+ = UTF-8, so Polish characters are kept
doc.BuildDimensionBlocks = true;                     // write the *D blocks (drawn dimension geometry), like AutoCAD does
doc.DrawingVariables.InsUnits = netDxf.Units.DrawingUnits.Millimeters;

var dimStyle = new DimensionStyle("ISO-3.5")
{
    TextHeight = 3.5,
    ArrowSize = 2.5,
    ExtLineOffset = 1,
    ExtLineExtend = 1.5,
    TextOffset = 1,
    LengthPrecision = 1,
    DecimalSeparator = ',',
};

var outline = new Layer("KONTUR") { Lineweight = Lineweight.W50 };
var axis = new Layer("OSIE") { Linetype = Linetype.Center };
var dims = new Layer("WYMIARY");
var texts = new Layer("OPISY");

void Add(EntityObject e, Layer layer) { e.Layer = layer; doc.Entities.Add(e); }
void Poly(Layer layer, bool closed, params (double X, double Y)[] pts) =>
    Add(new Polyline2D(pts.Select(p => new Vector2(p.X, p.Y)), closed), layer);
void Txt(string text, double x, double y, double h = 3.5, double rot = 0) =>
    Add(new Text(text, new Vector2(x, y), h) { Rotation = rot }, texts);
LinearDimension Dim(double x1, double y1, double x2, double y2, double offset, double rotation, string? userText = null)
{
    var d = new LinearDimension(new Vector2(x1, y1), new Vector2(x2, y2), offset, rotation, dimStyle);
    if (userText is not null) d.UserText = userText;
    Add(d, dims);
    return d;
}

// ---- Side view: shaft outline with 1x45° chamfers -----------------------------------------------
Poly(outline, true,
    (0, 11.5), (1, 12.5), (40, 12.5), (40, 20), (140, 20), (140, 15), (179, 15), (180, 14),
    (180, -14), (179, -15), (140, -15), (140, -20), (40, -20), (40, -12.5), (1, -12.5), (0, -11.5));
Add(new Line(new Vector2(1, 12.5), new Vector2(1, -12.5)), outline);        // chamfer edges
Add(new Line(new Vector2(179, 15), new Vector2(179, -15)), outline);
Poly(outline, false, (146, 15), (146, 11), (174, 11), (174, 15));            // keyway (depth 4)
Add(new Line(new Vector2(-6, 0), new Vector2(186, 0)), axis);                 // centre line

// ---- Section A-A through the keyway (Ø30 with 8 x 4 keyway) ----------------------------------------
const double sx = 250, r = 15;
var yEdge = Math.Sqrt(r * r - 4 * 4);
var gap = Math.Acos(4 / r) * 180 / Math.PI;                                   // 74.5°
Add(new Arc(new Vector2(sx, 0), r, 180 - gap, gap), outline);                 // circle without the keyway gap
Poly(outline, false, (sx - 4, yEdge), (sx - 4, r - 4), (sx + 4, r - 4), (sx + 4, yEdge));
Add(new Line(new Vector2(sx - 20, 0), new Vector2(sx + 20, 0)), axis);
Add(new Line(new Vector2(sx, -20), new Vector2(sx, 20)), axis);
Txt("A-A", sx - 5, 30, 5);
Txt("A", 160, -24, 5);                                                        // cutting plane mark
Add(new Line(new Vector2(160, -18), new Vector2(160, -26)), outline);

// ---- Dimensions (real DIMENSION entities; diameters as linear dims with %%c user text) ---------------
Dim(0, -11.5, 180, -14, -32, 0, "<> %%p0,1");                                // overall length 180 ±0,1
Dim(0, 11.5, 40, 12.5, 16, 0);                                                // 40
Dim(40, 20, 140, 20, 8, 0);                                                   // 100
Dim(140, 15, 180, 14, 13, 0);                                                 // 40
Dim(140, 20, 146, 15, 22, 0);                                                 // 6 (keyway position)
Dim(146, 15, 174, 15, 15, 0);                                                 // 28 (keyway length)
Dim(20, -12.5, 20, 12.5, 0, 90, "%%c25h6");                                   // Ø25h6
Dim(90, -20, 90, 20, 0, 90, "%%c40");                                         // Ø40
Dim(160, -15, 160, 15, 0, 90, "%%c30k6");                                     // Ø30k6
Dim(sx - 4, yEdge, sx + 4, yEdge, 8, 0, "8P9");                               // keyway width
Txt("t = 4", sx + 18, 12);                                                    // keyway depth

// ---- Surface finish / notes -------------------------------------------------------------------------
Txt("Ra 1,6", 8, 17, 2.5);
Txt("Ra 1,6", 150, -22, 2.5);
Txt("2x 1x45%%d", -20, -20, 3);
Txt("Uwagi / Notes:", -30, -60);
Txt("1. Tolerancje nietolerowane ISO 2768-m.", -30, -66, 3);
Txt("2. Stępić ostre krawędzie 0,3 max.", -30, -71, 3);
Txt("3. Pozostałe powierzchnie Ra 3,2.", -30, -76, 3);

// ---- Frame + title block ------------------------------------------------------------------------------
Poly(outline, true, (-40, -125), (310, -125), (310, 70), (-40, 70));
Poly(outline, true, (150, -125), (310, -125), (310, -75), (150, -75));
foreach (var y in new[] { -115.0, -105, -95, -85 }) Add(new Line(new Vector2(150, y), new Vector2(310, y)), outline);
Add(new Line(new Vector2(180, -125), new Vector2(180, -75)), outline);
Txt("Nazwa", 152, -82, 2.5);    Txt("WAŁEK / SHAFT", 183, -83, 4);
Txt("Nr rys.", 152, -92, 2.5);  Txt("PV-1001-A", 183, -93, 4);
Txt("Materiał", 152, -102, 2.5); Txt("C45 (1.0503) - pręt okrągły", 183, -103, 3);
Txt("Skala", 152, -112, 2.5);   Txt("1:1   Arkusz 1/1   mm", 183, -113, 3);
Txt("Rysował", 152, -122, 2.5); Txt("PRODVISOR AI demo (DXF)", 183, -123, 3);

doc.Save(output);
Console.WriteLine($"Saved {output}");

// Diagnostic for the converter: in which coordinate system does netDxf return ellipse vertexes?
var probe = new Ellipse(new Vector2(100, 100), 20, 10);
var first = probe.PolygonalVertexes(4)[0];
Console.WriteLine($"Ellipse(center 100,100; axes 20/10).PolygonalVertexes[0] = ({first.X}, {first.Y})");

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ProdvisorAi.Demo.sln"))) dir = dir.Parent;
    return dir?.FullName ?? Directory.GetCurrentDirectory();
}
