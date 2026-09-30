# PRODVISOR AI – serwis generowania technologii (demo)

Serwis, który na podstawie **rysunku** i **danych z tabel PRODVISOR** przygotowuje projekt technologii (operacje, maszyny, TPZ/TJ, materiał, wymiary, uwagi) przy pomocy OpenAI. Wynik wraca do aplikacji desktop z identyfikatorami PRODVISOR, technolog go poprawia i zatwierdza.

```
Desktop PRODVISOR ──(rysunek + element_id + wybrany prompt technologa)──► Serwis AI ──► OpenAI (Responses API)
        ▲                                                                       │  czyta: element, maszyny + parametry,
        └──────────── wynik z MASZYNY.ID / STANOWISKO.ID / MaterialsKind.ID ◄───┘  grupy stanowisk, materiały
```

W demo są dwie strony:

| Adres | Co to jest |
|---|---|
| `http://localhost:5080/desktop/` | **Symulacja aplikacji desktop**: lista elementów z rysunkami (dodawanie nowych z wgraniem rysunku, usuwanie), kilka promptów technologa (zasady firmy) z wyborem przy generowaniu, „Generuj technologię”, edycja karty, „Zatwierdź i zapisz”. Z serwisem rozmawia wyłącznie przez `/api/v1`. |
| `http://localhost:5080/` | **Panel serwisu**: zapytania (z podglądem requestu do OpenAI), jeden system prompt, maszyny, klucz API / model, opis API. |

## Uruchomienie

```bash
cd src/ProdvisorAi.Demo
dotnet run          # .NET 8 SDK
```

Przy pierwszym starcie powstaje `App_Data/` (SQLite, rysunki, logi, klucze szyfrujące). **Tryb mock jest domyślnie włączony** – całość działa bez klucza API (przykładowa odpowiedź z `samples/sample-mock-response.json`). Aby użyć OpenAI: Panel → **Ustawienia** → klucz API, model, „Testuj połączenie”, wyłączyć mock.

Instalacja na serwerze: Linux – [docs/DEPLOY-LINUX.md](docs/DEPLOY-LINUX.md); Windows Service – niżej.

## API dla aplikacji desktop

| | Endpoint | Opis |
|---|---|---|
| `POST` | `/api/v1/technology-jobs` | `multipart/form-data`: `drawing` (PNG/JPG/PDF/DXF), `element_id`, `mode` (`technolog` – domyślnie, pełna technologia | `konstruktor` – tylko materiał, ilość i wymiary z rysunku), `technology_prompt` (treść wybranego promptu technologa, tylko tryb technolog), `technology_prompt_name` (opcjonalnie, do logu) → `202 { job_id, status }` |
| `GET` | `/api/v1/technology-jobs/{job_id}` | Status `pending → running → done / failed`; po `done` – `result` + `warnings` |

Generowanie trwa od kilkunastu sekund do kilku minut, dlatego jest asynchroniczne: desktop dostaje `job_id` od razu i odpytuje GET co 1–2 s.

```jsonc
// GET /api/v1/technology-jobs/{job_id} (skrót)
{
  "status": "done", "element_id": "100232", "model": "gpt-6-astra", "duration_ms": 41800,
  "usage": { "input_tokens": 9120, "output_tokens": 2310, "total_tokens": 11430 },
  "result": {
    "operations": [{
      "no": 2, "name": "Toczenie CNC - strona A",
      "machine": { "machine_code": "0502", "maszyny_id": 19, "stanowisko_id": 16, "name": "TOKARKA HYUNDAI CNC", "group": "Tokarskie", "cooperation": false },
      "alternatives": [{ "machine_code": "0503", "maszyny_id": 27, "stanowisko_id": 17, ... }],
      "tpz_min": 35, "tj_min": 6, "description": "...", "comment": "..."
    }],
    "material": { "grade": "C45 (1.0503)", "stock": "Pręt okrągły Ø45 x 184 mm", "material_kind_id": 102, "material_kind_name": "Pręt 50 gat. C45" },
    "dimensions": { "part": { "length_mm": 180, "diameter_mm": 40, ... }, "stock": { "form": "round_bar", "size_mm": 45, "length_mm": 184 } },
    "quantity": null,   // ilość z rysunku (null = brak na rysunku)
    "notes": "..."
  },
  "warnings": [{ "code": "no_final_inspection", "operation_no": null, "message": "Brak operacji kontroli końcowej." }],
  "error": null   // przy failed: { "kind": "timeout|refusal|incomplete|...", "message": "..." }
}
```

## Co serwis wysyła do OpenAI

Jedno wywołanie `POST /v1/responses` (`store: false`):

- **developer** – system prompt (jeden, wbudowany w serwis, edytowalny w panelu),
- **user** – prompt technologa wybrany w desktopie (zasady firmy, np. „Obróbka skrawaniem CNC”, „Konstrukcje spawane i blachy”), dane elementu (numer, nazwa, materiał, ilość), dla DXF dokładne wymiary i teksty z pliku, aktywne maszyny z typem i parametrami, grupy stanowisk, lista materiałów (ID | nazwa) + **rysunek** (obraz lub PDF),
- **text.format** – JSON Schema w trybie `strict`; `machine_id` i `alternative_machine_ids` to `enum` aktywnych maszyn, więc model nie może zwrócić maszyny spoza listy.

Decyzje należą do AI: kolejność operacji, maszyna główna, które maszyny z grupy stanowiska mogą ją zastąpić (pominięte – z powodem w `comment`), czasy, dobór pozycji z kartoteki materiałów. Serwis tylko sprawdza wynik (nieznane ID materiału jest odrzucane, walidator zgłasza ostrzeżenia) i mapuje kody na ID PRODVISOR.

**DXF**: OpenAI nie czyta plików CAD, więc serwis renderuje DXF do PNG (netDxf + SkiaSharp) i dodatkowo wyciąga wymiary/teksty jako tekst – model ma je traktować jako dokładne.

## Dane PRODVISOR (tylko odczyt)

| Tabela | Do czego |
|---|---|
| `MASZYNY`, `MASZYNY_TYP`, `MASZYNY_PARAMETRY`, `MASZYNY_RODZAJ_PARAMETRU` | maszyny i ich parametry; `VirtualMachine = 1` = kooperacja |
| `STANOWISKO`, `STANOWISKO_NAZWA` | stanowisko maszyny (`STANOWISKO.ID`) i grupa = nazwa stanowiska |
| `MaterialsKind` | kartoteka materiałów |
| elementy / rysunki | w demo symulowane (`Elements` w SQLite, rysunki z `samples/`) |

Źródło ustawia `Prodvisor:Provider` w `appsettings.json`: `Snapshot` (wbudowana kopia tabel, domyślnie), `SqlServer` (produkcja) lub `PostgreSql` (kopia testowa; skrypty w `tools/prodvisor-db/`). Panel → Maszyny → „Synchronizuj z PRODVISOR”.

## Klucz API

Każdy klient używa własnego projektu i klucza OpenAI. Klucz jest zapisywany zaszyfrowany (Windows DPAPI, na Linuksie ASP.NET Data Protection), nigdy nie wraca do przeglądarki, nie trafia do logów ani do podglądu requestu.

## Konfiguracja (`appsettings.json`)

| Klucz | Domyślnie | Opis |
|---|---|---|
| `Urls` | `http://localhost:5080` | `http://0.0.0.0:5080` = dostęp z innych komputerów |
| `Storage:DataDirectory` | `App_Data` | baza, rysunki, logi, klucze |
| `OpenAI:DefaultModel` | `gpt-6-astra` | model, dopóki nie wybrano innego w panelu |
| `OpenAI:TimeoutSeconds` | `180` | timeout (1 automatyczne ponowienie przy timeout / 5xx) |
| `OpenAI:MaxOutputTokens` | `16000` | `max_output_tokens` |
| `OpenAI:ReasoningEffort` | *(puste)* | opcjonalnie `low` / `medium` / `high` |
| `Demo:MockModeDefault` | `true` | tryb mock na świeżej instalacji |
| `Prodvisor:Provider` / `ConnectionString` | `Snapshot` | źródło danych PRODVISOR |
| `Upload:MaxSizeMb` | `20` | maks. rozmiar rysunku |

Logi: `App_Data/logs/prodvisor-ai-RRRRMMDD.log` – jedna linia na zapytanie (model, mock, czas, tokeny, element, błąd).

## Windows Service

W PowerShell jako administrator:

```powershell
dotnet publish src/ProdvisorAi.Demo -c Release -r win-x64 --self-contained true -o C:\Services\ProdvisorAi
sc.exe create ProdvisorAi binPath= "C:\Services\ProdvisorAi\ProdvisorAi.Demo.exe --urls http://0.0.0.0:5080" start= auto DisplayName= "PRODVISOR AI (Demo)"
netsh advfirewall firewall add rule name="PRODVISOR AI" dir=in action=allow protocol=TCP localport=5080
sc.exe start ProdvisorAi
```

Serwer potrzebuje wyjścia HTTPS do `api.openai.com` (proxy: zmienna `HTTPS_PROXY`).

## Struktura

```
src/ProdvisorAi.Demo/
  Endpoints/TechnologyApiEndpoints.cs   /api/v1 – API dla desktopu
  Endpoints/AdminEndpoints.cs           /api/admin – panel serwisu
  Endpoints/DesktopSimEndpoints.cs      /sim/api – dane symulowanego desktopu (nie jest częścią serwisu)
  Services/TechnologyRequestBuilder.cs  request do OpenAI (prompt + JSON Schema)
  Services/TechnologyGenerator.cs       wykonanie zadania (mock / OpenAI)
  Services/JobWorker.cs, JobQueue.cs    kolejka zadań w tle
  Services/DxfDrawingConverter.cs       DXF → PNG + wymiary
  Services/TechnologyValidator.cs       ostrzeżenia dla technologa
  Prodvisor/                            odczyt tabel PRODVISOR, dopasowanie materiałów (mock)
  wwwroot/index.html, admin.js          panel serwisu
  wwwroot/desktop/                      symulacja aplikacji desktop
samples/                                rysunki (PNG, DXF), odpowiedź mock, snapshot tabel
```
