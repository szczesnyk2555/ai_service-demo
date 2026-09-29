using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ProdvisorAi.Demo.Data;

namespace ProdvisorAi.Demo.Prodvisor;

public sealed record MaterialCandidate(int Id, string Name, string? Form, double? SizeMm, int Score, string Reason);

public sealed record MaterialMatch(
    string Status,                            // "matched" | "grade_only" | "not_found" | "no_grade"
    string? Grade,                            // grade as read from the drawing
    string? GradeGroup,                       // recognised grade family, e.g. "C45 / 1.0503 / 45"
    IReadOnlyList<MaterialCandidate> Candidates);

/// <summary>
/// Finds the material from the drawing in the PRODVISOR material list (dbo.MaterialsKind).
/// Names differ between the drawing and the ERP ("C45" vs "Pręt 50 gat. C45" vs "STAL 45", "42CrMo4" vs "40HM"),
/// so matching works on grade FAMILIES (EN / PN / Werkstoff / AISI / trade names), then on stock form and size.
/// Deterministic and explainable - every candidate carries the reason it was chosen.
/// </summary>
public static partial class MaterialMatcher
{
    /// <summary>Grade families: label + patterns (applied to the normalised text: upper case, no diacritics, ',' -> '.').</summary>
    static readonly (string Label, Regex[] Patterns)[] Families = BuildFamilies(
        // Carbon & structural steels
        ("C45 / 1.0503 / 45", [@"\bC ?45(E|R)?\b", @"\b1\.0503\b", @"\bSTAL 45\b", @"\bCK ?45\b"]),
        ("C60 / 1.0601", [@"\bC ?60\b", @"\b1\.0601\b", @"\bSTAL 60\b"]),
        ("C55 / Ck55 / Cf53", [@"\bC[KF]? ?55\b", @"\bCF ?53\b", @"\b1\.1203\b", @"\b1\.1213\b", @"\bSTAL 55\b"]),
        ("S235 / St3S / 1.0038", [@"\bS ?235", @"\bST ?3S?\b", @"\bST\.? ?37", @"\b1\.0038\b", @"\bE235", @"\bST35"]),
        ("S355 / 18G2A / 1.0577", [@"\bS ?355", @"\bST ?355", @"\b18G2A?\b", @"\bST\.? ?52", @"\b1\.057[07]\b", @"\b1\.0562\b"]),
        ("St5 / E295", [@"\bST ?5\b", @"\bE295\b", @"\b1\.0050\b"]),
        ("St6 / E335", [@"\bST ?6\b", @"\bE335\b", @"\b1\.0060\b"]),
        ("S460", [@"\bS ?460"]),
        ("1.1730 / C45U", [@"\b1\.1730\b", @"\bC45U\b"]),
        // Alloy steels
        ("42CrMo4 / 40HM / 1.7225", [@"\b42 ?CRMO4?\b", @"\b40 ?HMT?\b", @"\b1\.7225\b"]),
        ("41Cr4 / 40H / 1.7035", [@"\b41 ?CR ?4\b", @"\b40H\b", @"\b1\.7035\b"]),
        ("16MnCr5 / 16HG / 1.7131", [@"\b16 ?MNCR(CR)?5\b", @"\b16HG\b", @"\b1\.7131\b"]),
        ("18CrNiMo7-6 / 1.6587", [@"\b18 ?CRNIMO", @"\b1\.6587\b"]),
        ("18HGT", [@"\b18HGT\b"]),
        ("18H2N2", [@"\b18H2N2\b"]),
        ("38H2MUA / 41CrAlMo7", [@"\b38H2MUA\b", @"\b41CRALMO7", @"\b1\.8509\b"]),
        ("35HGS", [@"\b35HGS\b"]),
        ("51CrV4 / 50HF / 1.8159", [@"\b51 ?CRV ?4\b", @"\b50HF\b", @"\b1\.8159\b"]),
        ("65Mn / 65G", [@"\b65 ?MN\b", @"\b65G\b"]),
        // Tool steels
        ("1.2311 / 40CrMnMo7 / P20", [@"\b1\.2311\b", @"\b40 ?CRMNMO ?7\b", @"\bP20\b"]),
        ("1.2312 / 40CrMnMoS8-6", [@"\b1\.2312\b", @"\b40 ?CRMNMOS"]),
        ("1.2379 / NC11LV / D2", [@"\b1\.2379\b", @"\bW\.? ?1?\.?2379\b", @"\bNC ?11 ?LV\b", @"\bX153CRMOV12\b", @"\bD2\b", @"\bSVERKER 21\b", @"\bK ?110\b"]),
        ("1.2344 / WCLV / H13", [@"\b1\.2344\b", @"\bWCLV\b", @"\bH13\b", @"\bX40CRMOV5", @"\bORVAR\b"]),
        ("1.2714 / 55NiCrMoV7", [@"\b1\.2714\b", @"\b55NICRMOV7\b"]),
        ("1.2083 / X40Cr14", [@"\b1\.2083\b", @"\bX40CR14\b"]),
        ("1.3343 / SW7M / HS6-5-2", [@"\b1\.3343\b", @"\bSW7M\b", @"\bHS ?6-5-2\b"]),
        ("NC6 / 1.2210", [@"\bNC ?6\b", @"\b1\.2210\b"]),
        ("1.3520", [@"\b1\.3520\b"]),
        ("1.0762", [@"\b1\.0762\b"]),
        ("IMPAX", [@"\bIMPAX\b"]), ("VANADIS", [@"\bVANADIS\b"]), ("CALMAX", [@"\bCALMAX\b"]), ("UNIMAX", [@"\bUNIMAX\b"]),
        ("CARMO", [@"\bCARMO\b"]), ("CALDAI", [@"\bCALDAI\b"]), ("SLEIPNER", [@"\bSLEIPNER\b"]), ("NIMAX", [@"\bNIMAX\b"]),
        ("TOOLOX 44", [@"\bTOOLOX ?44\b"]), ("K340", [@"\bK-? ?340\b"]), ("K360", [@"\bK-? ?360\b"]), ("K390", [@"\bK-? ?390\b"]),
        ("M261", [@"\bM261\b"]), ("W360", [@"\bW360\b"]), ("ETG 100", [@"\bETG ?100\b"]), ("REAX 400", [@"\bREAX ?400\b"]),
        ("HTK 420H", [@"\bHTK ?420"]), ("700MC", [@"\b700 ?MC\b"]), ("N9E", [@"\bN9E\b"]),
        // Stainless
        ("1.4301 / 304 / X5CrNi18-10", [@"\b1\.4301\b", @"\bAISI ?304\b", @"\bNIERDZEWKA 304\b", @"\bX5CRNI18-?10\b", @"\b0H18N9\b"]),
        ("1.4404 / 316L", [@"\b1\.4404\b", @"\b316L\b", @"\bX2CRNIMO17-12-2\b"]),
        ("1.4021 / 420", [@"\b1\.4021\b", @"\bAISI ?420\b"]),
        ("1.4057 / 431", [@"\b1\.4057\b", @"\bAISI ?431\b"]),
        ("1.4112", [@"\b1\.4112\b"]), ("1.4541 / 321", [@"\b1\.4541\b", @"\bAISI ?321\b"]),
        ("1.4310 / 301", [@"\b1\.4310\b", @"\bAISI ?301\b"]),
        ("1.4534 / X3CrNiMoAl13-8-2", [@"\b1\.4534\b", @"\bX3CRNIMOAL"]),
        ("15-5PH / AMS 5659", [@"\b15-5 ?PH\b", @"\bAMS-? ?5659\b"]),
        // Aluminium (PN "PA.." <-> EN AW)
        ("EN AW-2017A / PA6", [@"\bAW-? ?2017A?\b", @"\bPA ?6\b(?!-G)"]),
        ("EN AW-5083 / PA13", [@"\bAW-? ?5083\b", @"\bPA ?13\b", @"\b5083\b"]),
        ("EN AW-7075 / PA9", [@"\bAW-? ?7075\b", @"\bPA ?9\b", @"\b7075\b"]),
        ("EN AW-5754 / PA11", [@"\bAW-? ?5754\b", @"\bPA ?11\b", @"\b5754\b"]),
        ("EN AW-2024 / PA7", [@"\bAW-? ?2024\b", @"\b2024\b", @"\bPA ?7\b"]),
        ("EN AW-6082 / AlSi1MgMn", [@"\bAW-? ?6082\b", @"\b6082\b", @"\bALSI1MGMN\b"]),
        ("EN AW-6061", [@"\bAW-? ?6061\b", @"\b6061\b"]),
        ("EN AW-2007", [@"\bAW-? ?2007\b"]), ("EN AW-7050", [@"\b7050\b"]), ("PA47", [@"\bPA ?47\b"]), ("PA45", [@"\bPA ?45\b"]),
        // Copper alloys
        ("CuAl10Fe3Mn2 / BA1032", [@"\bBA ?1032\b", @"\bCUAL10FE3MN2\b"]),
        ("CuAl10Ni5Fe4 / BA1054", [@"\bBA ?1054\b", @"\bCUAL10NI5FE4\b"]),
        ("CuSn10P / B101", [@"\bB ?101\b", @"\bCUSN10P\b"]),
        ("CuSn12", [@"\bCUSN12\b"]), ("CuSn7ZnPb / RG7", [@"\bRG ?7\b", @"\bCUSN7ZNPB\b"]),
        ("CuAl11Fe6Ni6 / 2.0978", [@"\b2\.0978\b"]), ("AMPCO", [@"\bAMPCO\b"]), ("BSB23", [@"\bBSB ?23\b"]),
        ("Miedź / Cu-ETP", [@"\bMIEDZ\b", @"\bCU-?ETP\b", @"\bM1E\b"]),
        ("Mosiądz / CuZn39Pb3", [@"\bMOSIADZ\b", @"\bMO ?58\b", @"\bCUZN39PB3\b", @"\bCW614N\b"]),
        // Plastics
        ("POM", [@"\bPOM\b", @"\bPOLIACETAL\b", @"\bDELRIN\b"]), ("PEEK", [@"\bPEEK\b"]),
        ("Poliamid / PA6-G", [@"\bPOLIAMID\b", @"\bPA ?6-?G\b", @"\bNYLON\b"]), ("PE1000", [@"\bPE ?1000\b", @"\bUHMW"]),
        ("PTFE / Teflon", [@"\bTEFLON\b", @"\bPTFE\b"]), ("PMMA / Plexi", [@"\bPLEXI\b", @"\bPMMA\b"]),
        ("Poliwęglan / PC", [@"\bPOLIWEGLAN\b"]), ("Poliuretan", [@"\bPOLIURETAN\b", @"\bPUR?\b"]));

    // Stock form: word in the material name -> form code used in the technology JSON.
    static readonly (Regex Pattern, string Form)[] Forms =
    [
        (Rx(@"\bPRET (KWADRATOWY|KW\.)|\bKES KWADRATOWY\b"), "square_bar"),
        (Rx(@"\bSZESCIOKAT\b"), "hex_bar"),
        (Rx(@"\bPRET\b(?! M\d)"), "round_bar"),
        (Rx(@"\bPLASKOWNIK\b"), "flat_bar"),
        (Rx(@"\bBLACH[AY]\b|\bARKUSZ\b|\bPLYTA\b"), "plate"),
        (Rx(@"\bRURA\b"), "tube"),
        (Rx(@"\bPROFIL\b|\bKSZTALTOWNIK\b|\bKAT[K]?OWNIK\b|\bCEOWNIK\b"), "profile"),
    ];

    [GeneratedRegex(@"(?:PRET|SZESCIOKAT|BLACH[AY]|PLASKOWNIK|KES KWADRATOWY|RURA)(?:\s+(?:CIAGNIONY|WALCOWANY|OKRAGLY|KWADRATOWY|WALC\.))*\s*(?:FI\s*)?(\d+(?:\.\d+)?)")]
    private static partial Regex SizeAfterForm();

    [GeneratedRegex(@"KLIENTA|POWIERZONY|TESTOWY")]
    private static partial Regex CustomerOrTest();

    public static MaterialMatch Match(string? grade, string? form, double? sizeMm, IEnumerable<ProdvisorMaterial> materials)
    {
        if (string.IsNullOrWhiteSpace(grade))
            return new MaterialMatch("no_grade", grade, null, []);

        var gradeNorm = Normalize(grade);
        var families = Families.Where(f => f.Patterns.Any(p => p.IsMatch(gradeNorm))).ToList();
        // Unknown grade: fall back to the literal designation tokens (e.g. "X3CrNiMoAl13-8-2").
        // Only real designations (letters AND digits, e.g. "X3CRNIMOAL13-8-2"), never bare numbers like "500".
        var literalTokens = families.Count == 0
            ? Regex.Matches(gradeNorm, @"[A-Z0-9][A-Z0-9.\-]{3,}").Select(m => m.Value)
                .Where(t => t.Any(char.IsDigit) && t.Any(char.IsLetter))
                .Select(t => (Token: t, Pattern: new Regex($@"(?<![A-Z0-9]){Regex.Escape(t)}(?![A-Z0-9])"))).ToList()
            : [];

        var candidates = new List<MaterialCandidate>();
        foreach (var m in materials.Where(m => m.Active))
        {
            var name = Normalize(m.Name);
            var byFamily = families.FirstOrDefault(f => f.Patterns.Any(p => p.IsMatch(name)));
            var byLiteral = literalTokens.FirstOrDefault(t => t.Pattern.IsMatch(name)).Token;
            if (byFamily.Label is null && byLiteral is null) continue;

            var (mForm, mSize) = ParseStock(name);
            var score = 50;
            var reasons = new List<string> { byFamily.Label is not null ? $"gatunek {byFamily.Label}" : $"oznaczenie {byLiteral}" };

            if (form is { Length: > 0 } && form != "unknown" && form != "other")
            {
                if (mForm == form) { score += 30; reasons.Add("ta sama forma"); }
                else if (mForm is null) { score += 5; reasons.Add("pozycja ogólna (bez formy/wymiaru)"); }
                else { score -= 25; reasons.Add($"inna forma ({FormLabel(mForm)})"); }
            }
            if (sizeMm is > 0 && mSize is > 0 && (mForm == form || form is null))
            {
                if (mSize >= sizeMm) { score += 20 - (int)Math.Min(15, (mSize.Value - sizeMm.Value) / 5); reasons.Add($"wymiar {Num(mSize.Value)} ≥ {Num(sizeMm.Value)}"); }
                else { score -= 40; reasons.Add($"za mały wymiar ({Num(mSize.Value)} < {Num(sizeMm.Value)})"); }
            }
            if (CustomerOrTest().IsMatch(name)) { score -= 15; reasons.Add("materiał klienta / testowy"); }

            candidates.Add(new MaterialCandidate(m.Id, m.Name, mForm, mSize, score, string.Join(", ", reasons)));
        }

        var ranked = candidates.OrderByDescending(c => c.Score).ThenBy(c => c.SizeMm ?? double.MaxValue).Take(6).ToList();
        var familyLabel = families.Count > 0 ? string.Join(" | ", families.Select(f => f.Label)) : null;
        if (ranked.Count == 0) return new MaterialMatch("not_found", grade, familyLabel, []);

        // "matched" = right grade AND (form/size fit, or the request has no form/size to check)
        var best = ranked[0];
        var formOk = form is null or "unknown" or "other" || best.Form == form;
        var sizeOk = sizeMm is not > 0 || best.SizeMm is null || best.SizeMm >= sizeMm;
        var status = formOk && sizeOk && best.Score >= 50 ? "matched" : "grade_only";
        return new MaterialMatch(status, grade, familyLabel, ranked);
    }

    static (string? Form, double? Size) ParseStock(string normalizedName)
    {
        var form = Forms.FirstOrDefault(f => f.Pattern.IsMatch(normalizedName)).Form;
        var sizeMatch = SizeAfterForm().Match(normalizedName);
        double? size = sizeMatch.Success && double.TryParse(sizeMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : null;
        return (form, size);
    }

    /// <summary>Upper case, no Polish diacritics, decimal comma -> dot, single spaces.</summary>
    public static string Normalize(string s)
    {
        var decomposed = s.Replace('ł', 'l').Replace('Ł', 'L').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        var upper = sb.ToString().ToUpperInvariant();
        upper = Regex.Replace(upper, @"(?<=\d),(?=\d)", ".");        // 1,4301 -> 1.4301
        return Regex.Replace(upper, @"\s+", " ").Trim();
    }

    public static string FormLabel(string? form) => form switch
    {
        "round_bar" => "pręt okrągły", "hex_bar" => "sześciokąt", "square_bar" => "pręt kwadratowy", "flat_bar" => "płaskownik",
        "plate" => "blacha", "tube" => "rura", "profile" => "profil", _ => "inna",
    };

    static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    static Regex Rx(string p) => new(p, RegexOptions.Compiled);

    static (string, Regex[])[] BuildFamilies(params (string Label, string[] Patterns)[] defs) =>
        defs.Select(d => (d.Label, d.Patterns.Select(Rx).ToArray())).ToArray();
}
