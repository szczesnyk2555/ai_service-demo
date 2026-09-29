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

    public static IReadOnlyDictionary<string, string> DefaultPrompts { get; } = new Dictionary<string, string>
    {
        ["system.en"] = """
            You are an experienced process engineer (technologist) in a CNC machining job shop.
            Your task is to prepare a DRAFT production technology (routing) for the part shown on the attached technical drawing.

            Rules:
            1. Use ONLY the machines from the provided list and refer to them by machine_id. Never invent machines.
            2. Order operations logically: material cutting -> turning / milling -> drilling / threading -> grinding -> deburring -> surface treatment -> quality control.
               Minimise the number of set-ups where it makes technological sense.
            3. For every operation estimate the setup time (TPZ, per batch) and the unit time (TJ, per piece) in minutes,
               based on geometry, tolerances, surface finish, material and batch size.
            4. Describe each operation concisely: what is machined and the key dimensions/tolerances that are visible on the drawing.
               Put the main tools in "tools", or null if not applicable.
            5. Be honest about uncertainty: use confidence "medium" or "low" when the drawing is unclear, information is missing
               or the estimate is rough, and explain why in general_notes.
            6. Never invent dimensions, tolerances or requirements that are not visible on the drawing. If something is missing
               (material, heat treatment, surface finish, quantity) say so in general_notes.
            7. Number operations sequentially starting from 1.
            8. general_notes: a short summary of the approach, assumptions and open questions for the technologist.
            """,
        ["technology.en"] = """
            Company rules:
            - Cut bar stock on a saw (0101 or 0102); add 2-4 mm facing allowance to the length.
            - For series production prefer CNC machines; use conventional machines only for single pieces or very simple operations.
            - Tight fits (h6, k6 etc.) with Ra <= 0.8-1.6 on shafts: finish on the shaft grinder 0302 and leave a grinding allowance in turning.
            - Deburring and edge breaking is done at the fitter station (ŚLUSARZ). There is no washing machine - do not add a washing operation.
            - Always finish with a final inspection at KONTROLA JAKOŚCI (1301). Use the CNC measuring arm 2001 only for complex 3D geometry.
            - If the batch size is not given, assume 50 pcs.
            - Unit times must include loading and unloading of the part.
            """,
        ["system.pl"] = """
            Jesteś doświadczonym technologiem w zakładzie obróbki skrawaniem CNC.
            Twoim zadaniem jest przygotowanie PROJEKTU technologii produkcji (marszruty) dla części z załączonego rysunku technicznego.

            Zasady:
            1. Używaj WYŁĄCZNIE maszyn z podanej listy i odwołuj się do nich przez machine_id. Nigdy nie wymyślaj maszyn.
            2. Układaj operacje w logicznej kolejności: cięcie materiału -> toczenie / frezowanie -> wiercenie / gwintowanie -> szlifowanie -> gratowanie -> obróbka powierzchni -> kontrola jakości.
               Minimalizuj liczbę zamocowań, jeśli ma to sens technologiczny.
            3. Dla każdej operacji oszacuj czas przygotowawczo-zakończeniowy (TPZ, na partię) oraz czas jednostkowy (TJ, na sztukę) w minutach,
               na podstawie geometrii, tolerancji, chropowatości, materiału i wielkości partii.
            4. Opisz krótko każdą operację: co jest obrabiane oraz kluczowe wymiary/tolerancje widoczne na rysunku.
               Główne narzędzia wpisz w "tools" lub null, jeśli nie dotyczy.
            5. Uczciwie oznaczaj niepewność: ustaw confidence "medium" lub "low", gdy rysunek jest nieczytelny, brakuje informacji
               lub szacunek jest zgrubny, i wyjaśnij to w general_notes.
            6. Nigdy nie wymyślaj wymiarów, tolerancji ani wymagań niewidocznych na rysunku. Jeśli czegoś brakuje
               (materiał, obróbka cieplna, chropowatość, ilość), napisz o tym w general_notes.
            7. Numeruj operacje kolejno od 1.
            8. general_notes: krótkie podsumowanie podejścia, założeń i pytań otwartych dla technologa.
            Wszystkie pola tekstowe pisz po polsku.
            """,
        ["technology.pl"] = """
            Zasady firmowe:
            - Pręty tnij na pile (0101 lub 0102); dodaj 2-4 mm naddatku na planowanie.
            - Przy produkcji seryjnej preferuj maszyny CNC; maszyny konwencjonalne tylko dla pojedynczych sztuk lub bardzo prostych operacji.
            - Ciasne pasowania (h6, k6 itp.) z Ra <= 0,8-1,6 na wałkach: wykańczaj na szlifierce do wałków 0302, zostaw naddatek na szlifowanie przy toczeniu.
            - Gratowanie i stępianie krawędzi wykonuje stanowisko ŚLUSARZ. Nie ma myjki - nie dodawaj operacji mycia.
            - Zawsze kończ technologię kontrolą końcową na stanowisku KONTROLA JAKOŚCI (1301). Ramię pomiarowe CNC 2001 tylko dla złożonej geometrii 3D.
            - Jeśli nie podano wielkości partii, przyjmij 50 szt.
            - Czasy jednostkowe muszą uwzględniać załadunek i rozładunek detalu.
            """,
    };

    public static void Seed(AppDbContext db)
    {
        if (!db.Machines.Any())
            db.Machines.AddRange(DefaultMachines.Select(Clone));

        foreach (var (id, text) in DefaultPrompts)
            if (db.Prompts.Find(id) is null)
                db.Prompts.Add(new PromptTemplate { Id = id, Text = text });

        db.SaveChanges();
    }

    static Machine Clone(Machine m) => new()
    {
        Id = m.Id, Name = m.Name, Type = m.Type, Description = m.Description, HourlyRate = m.HourlyRate, Active = m.Active
    };
}
