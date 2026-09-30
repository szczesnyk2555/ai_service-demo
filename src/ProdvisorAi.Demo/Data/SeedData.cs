namespace ProdvisorAi.Demo.Data;

/// <summary>Initial data for a fresh installation: the customer's work centres (from PRODVISOR) and default prompts.</summary>
public static class SeedData
{
    // Work centres as exported from PRODVISOR: Id = work centre code, Type = group, Name = name.
    // Non-production entries (office, foremen, designers, test/duplicate records) are seeded as inactive,
    // so they are never offered to the model. Hourly rates are EXAMPLE values - replace them with real rates.
    public static IReadOnlyList<Machine> DefaultMachines { get; } =
    [
        M("0101", "PIŁA ANALOGOWA", "PIŁA BIANCO", 90, "Piła do cięcia prętów i profili na długość."),
        M("0102", "PIŁA ANALOGOWA", "PIŁA BTM", 90, "Piła do cięcia prętów i profili na długość."),
        M("0103", "PIŁA ANALOGOWA", "PIŁA RĘCZNA", 70, "Piła ręczna – cięcie drobnych elementów, docinki."),
        M("0201", "FREZARKA ANALOGOWA", "Frezarka konwencjonalna", 120, "Frezarka konwencjonalna – proste frezowanie płaszczyzn, rowków, pojedyncze sztuki."),
        M("0202", "FREZARKA ANALOGOWA", "Frezarka konwencjonalna", 120, "Frezarka konwencjonalna – proste frezowanie płaszczyzn, rowków, pojedyncze sztuki."),
        M("0203", "FREZARKA ANALOGOWA", "Frezarka konwencjonalna", 120, "Frezarka konwencjonalna – proste frezowanie płaszczyzn, rowków, pojedyncze sztuki."),
        M("0301", "SZLIFIERKI DO PŁASZCZYZN", "Szlifierka do płaszczyzn", 150, "Szlifowanie płaszczyzn."),
        M("0302", "SZLIFIERKA ANALOGOWA", "Szlifierka do wałków", 150, "Szlifowanie wałków – czopy, pasowania, wymagana niska chropowatość."),
        M("0401", "FREZARKA CNC - ŚREDNIE", "Frezarka HYUNDAI 500 F", 200, "Frezarka CNC 3-osiowa, detale średniej wielkości."),
        M("0401_P", "401_ PRODUKCJA", "Frezarka HYUNDAI 500 F", 200, "Rekord produkcyjny maszyny 0401 (duplikat).", active: false),
        M("0402", "FREZARKA CNC - MAŁE", "Frezarka HYUNDAI 400 F", 180, "Frezarka CNC 3-osiowa, małe detale."),
        M("0402_P", "402_PRODUKCJA", "Frezarka HYUNDAI 400 F", 180, "Rekord produkcyjny maszyny 0402 (duplikat).", active: false),
        M("0403", "FREZARKA CNC - MAŁE", "Frezarka DUGARD 500 A", 180, "Frezarka CNC 3-osiowa, małe detale."),
        M("0404", "FREZARKA CNC - DUŻE", "Frezarka HYUNDAI 850 F", 240, "Frezarka CNC 3-osiowa, duże detale."),
        M("0404_IM", "BRAK", "Frezarka HYUNDAI 850 F_IM", 240, "Rekord pomocniczy maszyny 0404 (duplikat).", active: false),
        M("0405", "FREZARKA CNC - MAŁE", "Frezarka HYUNDAI 500 F PLUS", 180, "Frezarka CNC 3-osiowa, małe detale."),
        M("0406", "FREZARKA CNC - ŚREDNIE", "Frezarka HYUNDAI 500 F", 200, "Frezarka CNC 3-osiowa, detale średniej wielkości."),
        M("0407", "FREZARKA CNC - MAŁE", "Frezarka DUGART 750", 180, "Frezarka CNC 3-osiowa, małe detale."),
        M("0408", "FREZARKA CNC - 5 osi", "FREZARKA AKIRA SEIKI", 320, "Frezarka CNC 5-osiowa – złożone detale, obróbka wielostronna w jednym zamocowaniu."),
        M("0501", "TOKARKA ANALOGOWA", "TOKARKA", 120, "Tokarka konwencjonalna – proste toczenie, pojedyncze sztuki."),
        M("0502", "TOKARKA CNC", "TOKARKA HYUNDAI CNC", 190, "Tokarka CNC – toczenie wałków i tulei, gwinty toczone."),
        M("0503", "TOKARKA CNC", "TOKARKA OSA – CNC", 190, "Tokarka CNC – toczenie wałków i tulei, gwinty toczone."),
        M("0504", "TOKARKA CNC", "TOKARKA HYUNDAI CNC NOWA", 190, "Tokarka CNC – toczenie wałków i tulei, gwinty toczone."),
        M("0601", "PRASA", "PRASA NARZĘDZIOWA 100 TON", 130, "Prasa 100 t – tłoczenie, wciskanie."),
        M("0602", "PRASA", "PRASA HYDRAULICZNA PYE 160T", 130, "Prasa hydrauliczna 160 t – tłoczenie, gięcie, wciskanie."),
        M("0603", "PRASA", "PRASA HYDRAULICZNA PYE 250T", 140, "Prasa hydrauliczna 250 t – tłoczenie, gięcie, wciskanie."),
        M("0604", "PRASA", "PRASA MIMOŚRODOWA 160T", 130, "Prasa mimośrodowa 160 t – wykrawanie, tłoczenie."),
        M("0605", "PRASA", "PRASA MIMOŚRODOWA 40T", 110, "Prasa mimośrodowa 40 t – wykrawanie drobnych detali."),
        M("0606", "PRASA", "PRASA MIMOŚRODOWA 40T", 110, "Prasa mimośrodowa 40 t – wykrawanie drobnych detali."),
        M("0607", "PRASA", "PRASA HYDRAULICZNA PYE 160T", 130, "Prasa hydrauliczna 160 t – tłoczenie, gięcie, wciskanie."),
        M("0608", "PRASA", "PRASA HYDRAULICZNA PYE 250T", 140, "Prasa hydrauliczna 250 t – tłoczenie, gięcie, wciskanie."),
        M("0609", "PRASA", "PRASA MIMOŚRODOWA 250T", 140, "Prasa mimośrodowa 250 t – wykrawanie, tłoczenie."),
        M("0701", "KRAWĘDZIARKA", "KRAWĘDZIARKA", 150, "Gięcie blach."),
        M("0801", "Grawerka Elektroniczna", "GRAWERKA ELEKTROMAGNETYCZNA", 100, "Znakowanie detali."),
        M("0802", "Grawerka Elektroniczna", "GRAWERKA LASEROWA", 110, "Znakowanie laserowe detali."),
        M("0901", "ŚRUTOWNICA ANALOGOWA", "ŚRUTOWNICA ANALOGOWA", 110, "Śrutowanie – oczyszczanie i przygotowanie powierzchni."),
        M("1001", "Gwinciarka ANALOGOWA", "GWINCIARKA ANALOGOWA", 100, "Gwintowanie otworów."),
        M("1002", "Gwinciarka ANALOGOWA", "GWINCIARKA ANALOGOWA 2", 100, "Gwintowanie otworów."),
        M("1003", "Gwinciarka ANALOGOWA", "GWINCIARKA ANALOGOWA 3", 100, "Gwintowanie otworów."),
        M("1100", "POBIERANIE KANTOWKI Z MAGAZYNU", "POBIERANIE KANTOWKI Z MAGAZYNU", 60, "Pobranie materiału (kantówki) z magazynu."),
        M("1101", "TECHNOLOG", "Stanowiskowy Pakiet Narzędziowy 1101", 120, "Stanowisko technologa (nieprodukcyjne).", active: false),
        M("1201", "MISTRZ", "Stanowiskowy Pakiet Narzędziowy 1201", 120, "Stanowisko mistrza (nieprodukcyjne).", active: false),
        M("1202", "MISTRZ", "Stanowiskowy Pakiet Narzędziowy 1202", 120, "Stanowisko mistrza (nieprodukcyjne).", active: false),
        M("1203", "MISTRZ", "Stanowiskowy Pakiet Narzędziowy 1203", 120, "Stanowisko mistrza (nieprodukcyjne).", active: false),
        M("123", "401_ PRODUKCJA", "tedst", 0, "Rekord testowy.", active: false),
        M("1301", "KONTROLA JAKOŚCI", "Stanowiskowy Pakiet Narzędziowy 1301", 130, "Kontrola jakości – pomiary, kontrola końcowa, raport kontroli."),
        M("1302", "KONTROLA JAKOŚCI", "Stanowiskowy Pakiet Narzędziowy 1302", 130, "Kontrola jakości – pomiary, kontrola końcowa, raport kontroli."),
        M("1401", "MALARZ", "Stanowiskowy Pakiet Narzędziowy 1401", 100, "Malowanie."),
        M("1501", "ŚLUSARZ", "Stanowiskowy Pakiet Narzędziowy 1501", 100, "Prace ślusarskie: gratowanie, stępianie krawędzi, wiercenie ręczne, montaż."),
        M("1502", "ŚLUSARZ", "Stanowiskowy Pakiet Narzędziowy 1502", 100, "Prace ślusarskie: gratowanie, stępianie krawędzi, wiercenie ręczne, montaż."),
        M("1503", "ŚLUSARZ", "Stanowiskowy Pakiet Narzędziowy 1503", 100, "Prace ślusarskie: gratowanie, stępianie krawędzi, wiercenie ręczne, montaż."),
        M("1504", "UNIWERSALNE", "Stanosiko AUTOMATYKÓW", 100, "Stanowisko automatyków (nieprodukcyjne).", active: false),
        M("1601", "POLERKI", "Stanowiskowy Pakiet Narzędziowy 1601", 100, "Polerowanie."),
        M("1701", "Wiertarka ANALOGOWA", "Wiertarka słupowa", 100, "Wiertarka słupowa – wiercenie, rozwiercanie, pogłębianie."),
        M("1801", "BRAK", "Stanowiskowy Pakiet Narzędziowy 1801", 0, "Stanowisko bez przypisanej grupy.", active: false),
        M("1901", "Spawalnia", "Spawanie ręczne", 130, "Spawanie ręczne."),
        M("1902", "Spawalnia", "Spawanie ręczne", 130, "Spawanie ręczne."),
        M("2001", "Maszyna Pomiarowa CNC", "Ramie Pomiarowe CNC HEXAGON", 180, "Ramię pomiarowe CNC – pomiary 3D, kontrola wymiarowa złożonych detali."),
        M("2101", "ROBOT", "Stacja spawalnicza robot - COMAU", 220, "Zrobotyzowane spawanie."),
        M("2102", "ROBOT", "Stacja spawalnicza robot - KUKA", 220, "Zrobotyzowane spawanie."),
        M("2201", "LASER", "Laser do ciecia profili BODOR", 250, "Laserowe cięcie profili i rur."),
        M("2202", "LASER", "Laser do cięcia blach Bodor", 250, "Laserowe cięcie blach."),
        M("2301", "KONSTRUKTOR", "KONSTRUKTOR - STANOWISKO NR 1", 120, "Stanowisko konstruktora (nieprodukcyjne).", active: false),
        M("2401", "UNIWERSALNE", "Biuro", 0, "Biuro (nieprodukcyjne).", active: false),
        M("2501", "UNIWERSALNE", "Pozostali pracownicy", 0, "Pozostali pracownicy (nieprodukcyjne).", active: false),
        M("2601", "Spawalnia", "Obróbka ręczna", 100, "Obróbka ręczna."),
        M("2701", "DRUCIKARKI", "Drucikarka SODICK VN600Q", 200, "Elektrodrążarka drutowa – precyzyjne wycinanie konturów."),
        M("2801", "Rolownica", "Rolownica QS80M4B", 130, "Zwijanie blach."),
        M("2901", "Giętarki do rur", "YLM CNC65S2-RSM-4A", 180, "Giętarka CNC do rur."),
        M("2902", "Giętarki do rur", "YLM CNC -80MS-5A", 180, "Giętarka CNC do rur."),
    ];

    static Machine M(string id, string type, string name, double rate, string description, bool active = true) =>
        new() { Id = id, Type = type, Name = name, HourlyRate = rate, Description = description, Active = active };

    // ---- System prompt (one, built into the service, editable in the admin panel) ------------
    public const string SystemPromptId = "system";

    public const string DefaultSystemPrompt = """
        Jesteś doświadczonym technologiem produkcji w zakładzie wykonującym obróbkę skrawaniem, konstrukcje spawane i elementy z blach.
        Twoim zadaniem jest przygotowanie PROJEKTU technologii produkcji (marszruty) dla elementu z załączonego rysunku technicznego.

        Zasady:
        1. Używaj WYŁĄCZNIE maszyn z podanej listy i odwołuj się do nich przez machine_id. Nigdy nie wymyślaj maszyn.
        2. Układaj operacje w logicznej kolejności technologicznej (przygotowanie materiału -> obróbka główna -> obróbka wykańczająca
           -> gratowanie / powłoki -> kontrola jakości). Minimalizuj liczbę zamocowań, jeśli ma to sens technologiczny.
        3. Dla każdej operacji oszacuj czas przygotowawczo-zakończeniowy (TPZ, na partię) i czas jednostkowy (TJ, na sztukę) w minutach,
           na podstawie geometrii, tolerancji, chropowatości, materiału i wielkości partii.
        4. description: krótko, co jest wykonywane i kluczowe wymiary/tolerancje widoczne na rysunku.
           comment: uwagi do operacji dla technologa (niepewności, założenia, narzędzia / przyrządy, powód pominięcia maszyn z grupy).
        5. Nigdy nie wymyślaj wymiarów, tolerancji ani wymagań niewidocznych na rysunku. Jeśli czegoś brakuje, napisz o tym.
        6. Instrukcje technologa (zasady firmowe) mają pierwszeństwo przed ogólnymi zasadami powyżej.
        7. Numeruj operacje kolejno od 1. general_notes: krótkie podsumowanie podejścia, założeń i pytań otwartych.
        Wszystkie pola tekstowe pisz po polsku.
        """;

    // ---- Technology prompts of the desktop application (company rules, chosen by the technologist) ----
    public static IReadOnlyList<TechnologyPrompt> DefaultTechnologyPrompts { get; } =
    [
        new()
        {
            Id = "cnc", Name = "Obróbka skrawaniem CNC",
            Text = """
                Zasady firmowe:
                - Pręty tnij na pile (0101 lub 0102); dodaj 2-4 mm naddatku na planowanie.
                - Przy produkcji seryjnej preferuj maszyny CNC; maszyny konwencjonalne tylko dla pojedynczych sztuk lub bardzo prostych operacji.
                - Ciasne pasowania (h6, k6 itp.) z Ra <= 0,8-1,6 na wałkach: wykańczaj na szlifierce do wałków 0302, zostaw naddatek na szlifowanie przy toczeniu.
                - Gratowanie i stępianie krawędzi wykonuje stanowisko ŚLUSARZ. Nie ma myjki - nie dodawaj operacji mycia.
                - Zawsze kończ technologię kontrolą końcową na stanowisku KONTROLA JAKOŚCI (1301).
                - Jeśli nie podano wielkości partii, przyjmij 50 szt.
                - Czasy jednostkowe muszą uwzględniać załadunek i rozładunek detalu.
                """,
        },
        new()
        {
            Id = "spawane", Name = "Konstrukcje spawane i blachy",
            Text = """
                Zasady firmowe:
                - Blachy do 20 mm tnij laserem (grupa Cięcie laserem), grubsze i profile - piła lub kooperacja.
                - Typowa kolejność: cięcie -> gięcie -> otwory -> spawanie -> prostowanie -> gratowanie -> malowanie (kooperacja) -> kontrola.
                - Spawanie na stanowiskach spawalniczo-ślusarskich; przy seriach powyżej 20 szt. rozważ robota spawalniczego.
                - W comment podaj potrzebne przyrządy spawalnicze i przygotowanie krawędzi pod spoiny.
                - Zawsze kończ technologię kontrolą końcową na stanowisku KONTROLA JAKOŚCI (1301).
                """,
        },
    ];

    // ---- Simulated PRODVISOR elements with drawings uploaded by the designer ------------------
    public static IReadOnlyList<ProdvisorElement> DefaultElements { get; } =
    [
        new() { Id = "100231", Number = "PV-1001-A", Name = "Wałek stopniowany", Material = "C45", Quantity = 50, DrawingFile = "sample-drawing-shaft.png" },
        new() { Id = "100232", Number = "PV-1001-B", Name = "Wałek stopniowany (rysunek DXF)", Material = "C45", Quantity = 20, DrawingFile = "sample-drawing-shaft.dxf" },
    ];

    public static void Seed(AppDbContext db)
    {
        // One system prompt. Databases of the previous version had several profiles - replaced by the single prompt.
        if (db.SystemPrompts.Find(SystemPromptId) is null)
        {
            db.SystemPrompts.RemoveRange(db.SystemPrompts);
            db.SystemPrompts.Add(new SystemPrompt { Id = SystemPromptId, Name = "System prompt", Text = DefaultSystemPrompt, IsDefault = true });
        }

        if (!db.TechnologyPrompts.Any())
            db.TechnologyPrompts.AddRange(DefaultTechnologyPrompts.Select(p => new TechnologyPrompt { Id = p.Id, Name = p.Name, Text = p.Text }));

        foreach (var e in DefaultElements)
            if (db.Elements.Find(e.Id) is null)
                db.Elements.Add(new ProdvisorElement
                    { Id = e.Id, Number = e.Number, Name = e.Name, Material = e.Material, Quantity = e.Quantity, DrawingFile = e.DrawingFile });

        db.SaveChanges();
    }
}
