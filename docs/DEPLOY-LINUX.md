# PRODVISOR AI – wdrożenie demo na serwer Linux

Instrukcja uruchomienia wersji demonstracyjnej na serwerze Linux x64 (Ubuntu / Debian / Rocky / Alma / Fedora).
Paczka jest **self-contained** – na serwerze nie trzeba instalować .NET. Aplikacja działa jako usługa systemd
na porcie **5080** w sieci wewnętrznej.

**Czas:** ok. 10 minut. **Potrzebne:** dostęp `sudo` na serwerze, SSH/SCP z komputera z repozytorium.

---

## 1. Zbuduj paczkę (na Windowsie, Git Bash)

```bash
cd ~/Desktop/workspace/ai_service-demo
git pull
./deploy/build-linux-package.sh
```

Wynik: `publish/prodvisor-ai-linux-x64.tar.gz` (~80 MB) – aplikacja + `install.sh` + plik usługi + ta instrukcja.

## 2. Skopiuj na serwer

```bash
scp publish/prodvisor-ai-linux-x64.tar.gz uzytkownik@serwer:/tmp/
```

## 3. Zainstaluj (na serwerze)

```bash
mkdir -p /tmp/prodvisor-ai && tar -xzf /tmp/prodvisor-ai-linux-x64.tar.gz -C /tmp/prodvisor-ai
cd /tmp/prodvisor-ai
sudo ./install.sh                 # inny port:  sudo PORT=8080 ./install.sh
```

Instalator:
- doinstalowuje pakiety systemowe: ICU (wymagane przez .NET) i czcionki DejaVu (napisy na podglądzie DXF),
- tworzy użytkownika systemowego `prodvisor`,
- kopiuje aplikację do `/opt/prodvisor-ai`,
- rejestruje i uruchamia usługę `prodvisor-ai`,
- na końcu wypisuje adres, pod którym działa aplikacja.

## 4. Sprawdź

- W przeglądarce: `http://<adres-serwera>:5080/desktop/` (symulacja aplikacji desktop) i `http://<adres-serwera>:5080/` (panel serwisu). Domyślnie **tryb mock**.
- Szybki test bez przeglądarki: `curl http://localhost:5080/api/v1/system-prompts`.
- Status i logi: `systemctl status prodvisor-ai`, `journalctl -u prodvisor-ai -f`.
- Logi zadań aplikacji: `/opt/prodvisor-ai/App_Data/logs/`.

Jeśli serwer ma firewall: `sudo ufw allow 5080/tcp` (Ubuntu) albo `sudo firewall-cmd --add-port=5080/tcp --permanent && sudo firewall-cmd --reload` (RHEL).

## 5. Dostęp dla innych

Aplikacja **nie ma logowania** – każdy, kto ma dostęp do adresu `http://<adres-serwera>:5080`, może zmieniać ustawienia
i generować technologie. Wystawiaj ją tylko w sieci wewnętrznej. Jeśli wpiszesz klucz OpenAI, generowanie idzie na koszt
Twojego projektu OpenAI.

## 6. Konfiguracja (opcjonalnie)

Wszystko poniżej ustawia się w pliku `/opt/prodvisor-ai/appsettings.Production.json`. Ponowna instalacja nie nadpisuje tego pliku.
Po zmianie: `sudo systemctl restart prodvisor-ai`.

**Klucz OpenAI.** Wpisz go w aplikacji: panel serwisu (`/`) → *Ustawienia → Klucz API → Testuj połączenie*, potem wyłącz tryb mock.
Na Linuksie klucz jest szyfrowany przez ASP.NET Data Protection (klucze w `App_Data/keys`, katalog dostępny tylko
dla użytkownika `prodvisor`). Klucza z instalacji Windows nie da się przenieść – trzeba go wpisać ponownie.

**Maszyny i materiały z bazy PRODVISOR** zamiast wbudowanej migawki:

```json
{
  "Prodvisor": {
    "Provider": "SqlServer",
    "ConnectionString": "Server=SERWER-SQL;Database=DANSOFT;User Id=prodvisor_ai;Password=...;TrustServerCertificate=true"
  }
}
```

- Z Linuksa używaj **loginu SQL**. Logowanie Windows (`Integrated Security`) tu nie działa.
- Wystarczy użytkownik z prawem **SELECT** do tabel `MASZYNY`, `MASZYNY_TYP`, `MASZYNY_PARAMETRY`,
  `MASZYNY_RODZAJ_PARAMETRU` i `MaterialsKind`. Aplikacja nic do PRODVISOR nie zapisuje.
- Po zmianie źródła: *Maszyny → Synchronizuj z PRODVISOR*.
- Bez tej konfiguracji aplikacja używa wbudowanej migawki tabel, co na demo w zupełności wystarcza.

Testowa baza z tymi tabelami: skrypty `tools/prodvisor-db/dansoft-sqlserver.sql` i `dansoft-postgres.sql` w repozytorium.
PostgreSQL w Dockerze na serwerze:

```bash
docker run -d --name dansoft -e POSTGRES_USER=prodvisor -e POSTGRES_PASSWORD=prodvisor-demo -e POSTGRES_DB=dansoft -p 127.0.0.1:5433:5432 postgres:17
docker exec -i dansoft psql -U prodvisor -d dansoft < dansoft-postgres.sql
```

Konfiguracja aplikacji dla tej bazy: `"Provider": "PostgreSql"` i
`"ConnectionString": "Host=localhost;Port=5433;Database=dansoft;Username=prodvisor;Password=prodvisor-demo"`.

## 7. Aktualizacja

Powtórz kroki 1–3 z nową paczką. Instalator zatrzyma usługę, podmieni pliki programu i uruchomi ją ponownie.
Baza, historia, zapisany klucz i `appsettings.Production.json` zostają. Nowe kolumny w bazie aplikacja dodaje sama.

## 8. Odinstalowanie

```bash
sudo systemctl disable --now prodvisor-ai
sudo rm /etc/systemd/system/prodvisor-ai.service && sudo systemctl daemon-reload
sudo rm -rf /opt/prodvisor-ai          # UWAGA: usuwa też bazę i historię (App_Data)
sudo userdel prodvisor
```

## 9. Rozwiązywanie problemów

| Objaw | Przyczyna / rozwiązanie |
|---|---|
| `Couldn't find a valid ICU package` w logach | Brak biblioteki ICU. Ubuntu: `sudo apt install libicu-dev`, RHEL: `sudo dnf install libicu` |
| Na podglądzie DXF brak napisów | Brak czcionek. Ubuntu: `sudo apt install fonts-dejavu-core`, RHEL: `sudo dnf install dejavu-sans-fonts`, potem restart usługi |
| `Address already in use` | Port zajęty. Zainstaluj ponownie z innym portem: `sudo PORT=8080 ./install.sh` |
| Strona nie otwiera się z innego komputera | Firewall (punkt 4) albo usługa nie działa: `systemctl status prodvisor-ai` |
| „Invalid API key (401)” w teście połączenia | Zły klucz lub klucz projektu bez dostępu do modelu. Sprawdź na platform.openai.com |
| „Cannot reach the OpenAI API” | Serwer nie ma wyjścia na `https://api.openai.com`. Sprawdź proxy / firewall wychodzący |
| Ostrzeżenie `No XML encryptor configured` w logach | Informacyjne na Linuksie: klucze Data Protection chroni uprawnienie do katalogu `App_Data` |
