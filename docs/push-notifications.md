# Powiadomienia Web Push

Ta funkcja wysyla:

- poranny brief o wybranej godzinie (domyslnie 06:00), z liczba zadan na dzis w projektach oraz konkretnymi zaleglymi zadaniami;
- drugi brief (domyslnie 13:00);
- przypomnienie przed blokiem w kalendarzu (domyslnie 10 minut) oraz przypomnienie o zadaniu z terminem i godzina (sekcja „Przypomnienia o zadaniach”);
- wieczorne podsumowanie (domyslnie 20:00), z liczba ukonczonych zadan i konkretnymi niewykonanymi zadaniami z terminem na dzis lub wczesniej.

Ustawienia sa per uzytkownik, a subskrypcja jest per urzadzenie. Kazda automatyczna wiadomosc ma klucz deduplikujacy w bazie, wiec ponowne wywolanie zadania nie powinno wyslac jej drugi raz.

## 1. Zastosuj migracje

Z lokalnego katalogu backendu, z produkcyjnym connection stringiem jako zmienna srodowiskowa:

```bash
ConnectionStrings__Database='PRODUCTION_POSTGRES_CONNECTION_STRING' \
dotnet ef database update \
  --project src/Mindflow/Mindflow.Api/Mindflow.Api.csproj \
  --startup-project src/Mindflow/Mindflow.Api/Mindflow.Api.csproj
```

Migracja `AddPushNotifications` tworzy tabele `notification_settings`, `push_notification_subscriptions` i `push_notification_deliveries`. Kolejna migracja `AddPushNotificationDeviceName` dodaje nazwę urządzenia do listy subskrypcji, `AddNotificationInbox` tworzy centrum zapisanych porannych i popołudniowych briefów oraz podsumowań dnia, a `AddTaskDueTime` dodaje opcjonalną godzinę terminu do zwykłych zadań. Migracja `AddTaskReminders` dodaje pola przypomnień do zadań. Zawsze zastosuj wszystkie oczekujące migracje tą samą komendą.

## 2. Wygeneruj klucze VAPID

Wygeneruj je tylko raz i zachowaj poza repozytorium:

```bash
npx web-push generate-vapid-keys --json
```

Wynik zawiera `publicKey` i `privateKey`. Publiczny klucz moze trafic do frontendu, prywatny pozostaje tylko na backendzie.

## 3. Ustaw zmienne na Renderze i Vercelu

Na Renderze, przy backendzie, dodaj:

```text
WebPush__Subject=mailto:twoj-email@example.com
WebPush__PublicKey=PUBLIC_KEY_Z_VAPID
WebPush__PrivateKey=PRIVATE_KEY_Z_VAPID
Jobs__ApiKey=LOSOWY_DLUGI_SEKRET
```

`Jobs__ApiKey` moze wykorzystac wartosc, ktora juz sluzy innym endpointom `internal/jobs`. Nie podawaj go frontendowi.

Na Vercelu, przy froncie, dodaj tylko:

```text
VITE_WEB_PUSH_PUBLIC_KEY=PUBLIC_KEY_Z_VAPID
```

Nastepnie zrob redeploy obu aplikacji. Zmienna Vercel musi byc ustawiona przed buildem, bo Vite wstawia ja do bundla.

## 4. Wdroz darmowy harmonogram Cloudflare

Katalog `ops/cloudflare-notification-cron` zawiera gotowego Workera. Uruchamia on endpoint backendu co piec minut. Taka czestotliwosc pozwala rzeczywiscie wysylac przypomnienie okolo 10 minut przed blokiem o dowolnej godzinie.

```bash
cd ops/cloudflare-notification-cron
npx wrangler login
npx wrangler secret put MINDFLOW_API_NOTIFICATIONS_URL
npx wrangler secret put MINDFLOW_JOBS_API_KEY
npx wrangler deploy
```

Przy pierwszym sekrecie wpisz pelen adres:

```text
https://twoj-backend.onrender.com/internal/jobs/notifications
```

Przy drugim wpisz dokladnie wartosc `Jobs__ApiKey` z Rendera. Cron jest zdefiniowany w `wrangler.toml` jako `*/5 * * * *`. Backend liczy godziny osobno dla kazdego uzytkownika, wedlug strefy czasowej przegladarki zapisanej przy wlaczeniu powiadomien.

Vercel Hobby nie jest tu dobrym harmonogramem: jego cron moze uruchomic sie najwyzej raz dziennie, wiec nie obsluzy przypomnien o blokach.

## 5. Aktywuj na iPhonie

1. Otworz Mindflow z ikony na ekranie poczatkowym, nie z karty Safari.
2. Wejdz w `Ustawienia -> Powiadomienia`.
3. Wybierz `Wlacz powiadomienia` i zaakceptuj prosbe systemu.
4. Wybierz `Wyslij test`, aby sprawdzic ekran blokady.

Wymagany jest iOS/iPadOS 16.4 lub nowszy, HTTPS i PWA dodane do ekranu poczatkowego. Po wdrozeniu manifestu warto usunac stara ikone PWA z ekranu poczatkowego i dodac ja ponownie, aby iOS pobral nowa konfiguracje.

## Przypomnienia o zadaniach

Zadanie z terminem i godziną ma jedno przypomnienie. Zamiast dawnego globalnego przypomnienia „X min przed zadaniem” ustawiasz je przy samym zadaniu:

- `reminderMinutesBefore` — ile minut przed terminem (0–10080; 0 = dokładnie o godzinie terminu). `null` oznacza wartość domyślną użytkownika, czyli `BlockReminderMinutes`, gdy włączone są „Przypomnienia o blokach”.
- `reminderDisabled` — wyłącza przypomnienie dla tego zadania.

Job `POST /internal/jobs/notifications` (co 5 min) wysyła przypomnienie raz (deduplikacja w `push_notification_deliveries`), gdy moment przypomnienia przypada w ciągu ostatnich 15 minut. Kanały:

- aplikacja: element skrzynki typu `TaskReminder` oraz zdarzenie SignalR `ReminderDue`;
- Web Push: trwałe powiadomienie z akcjami `Zrobione` i `Odłóż 10 min`.

Akcje z powiadomienia idą na `POST /notifications/reminder-actions` (anonimowo) z body `{ "token": "...", "action": "done" | "snooze" }`. Token jest podpisany HMAC (klucz wyprowadzony z `Jwt:Secret`) i ważny do terminu + 2 dni. `snooze` przesuwa przypomnienie o 10 min.

Nowe zmienne na Renderze, przy backendzie:

```text
Notifications__PublicApiUrl=https://twoj-backend.onrender.com
Notifications__FrontendUrl=https://twoj-front.vercel.app
```

- `Notifications__PublicApiUrl` — absolutny adres API, którego używa service worker do akcji; pusty = ten sam origin.
- `Notifications__FrontendUrl` — adres aplikacji do linków w powiadomieniach; gdy pusty, backend bierze `Cors__FrontendUrl`.

Migracja `AddTaskReminders` (punkt 1) dodaje 3 kolumny do tabeli zadań.

## E-mail

Przypomnienia o zadaniach ida takze mailem, gdy uzytkownik ma wlaczony przelacznik „E-mail” (`emailRemindersEnabled` w `/notifications/settings`, domyslnie wlaczony) i serwer ma skonfigurowany SMTP. Temat maila: `⏰ {zadanie} o {HH:mm}`. Mail zawiera przyciski:

- `Zrobione` i `Odloz 10 min` — prowadza do strony frontu `/przypomnienie?t=…&a=…`, ktora wymaga potwierdzenia kliknieciem;
- `Otworz zadanie`.

Zmienne na Renderze, przy backendzie:

```text
Email__Smtp__Host=smtp.gmail.com
Email__Smtp__Port=587
Email__Smtp__User=twoj-email@gmail.com
Email__Smtp__Password=HASLO_APLIKACJI
Email__Smtp__From=Mindflow <powiadomienia@domena>
Email__Smtp__UseSsl=true
```

- `Email__Smtp__Port` — domyslnie 587. Port 465 = SSL od razu, inne porty = STARTTLS (wtedy `UseSsl` pozostaje true).
- Bez `Host` i `From` kanal e-mail jest wylaczony.
- `Email__Smtp__From` musi byc adresem konta SMTP albo adresem z zweryfikowanej domeny nadawcy (SPF/DKIM); `UseSsl=false` jest tylko dla lokalnego Mailpit.
- Wysylka ma limit 30 s, a blad poczty nie blokuje innych kanalow.
- Przyklad Gmail: `smtp.gmail.com`, 587, haslo aplikacji (nie zwykle haslo konta).

Migracja `AddEmailReminderSetting` zastosowana jak pozostale (punkt 1).

Test lokalny z Mailpit:

```bash
docker run -d --name mindflow-mailpit -p 1025:1025 -p 8025:8025 axllent/mailpit
```

Backend uruchom z:

```text
Email__Smtp__Host=localhost
Email__Smtp__Port=1025
Email__Smtp__UseSsl=false
Email__Smtp__From=Mindflow <noreply@mindflow.local>
```

Skrzynka testowa: http://localhost:8025.

## Telegram

Przypomnienia o zadaniach moga przychodzic takze od bota Telegrama z przyciskami:

- `✅ Zrobione` i `⏰ Odłóż 10 min`;
- `Otwórz zadanie` — tylko gdy adres frontu jest na https.

Briefy (poranny i popołudniowy) oraz podsumowanie dnia też przychodzą od bota: pogrubiony tytuł, treść i przycisk `Otwórz Mindflow` (tylko gdy adres frontu jest na https). Idą równolegle z Web Push. Wpis w centrum powiadomień i znacznik dostawy powstają, gdy dotarł choć jeden kanał, więc kolejny przebieg joba niczego nie dubluje. Job obejmuje też konta bez subskrypcji push, jeśli mają połączony Telegram. Przełącznik `telegramRemindersEnabled` wyłącza teraz wszystkie powiadomienia na Telegram (przypomnienia, briefy, podsumowanie); etykieta w UI zostaje. Przypomnienia o blokach i zadaniach cyklicznych dalej idą tylko przez Web Push.

Konto łączysz w `Ustawienia -> Powiadomienia -> Telegram`: klikasz link `https://t.me/<bot>?start=<kod>`. Kod jest jednorazowy i ważny 15 minut. Rozłączasz tam albo komendą `/stop` w czacie z botem. Przełącznik `telegramRemindersEnabled` w `/notifications/settings` (domyślnie włączony) wyłącza wysyłkę bez rozłączania konta.

Utworzenie bota:

1. W Telegramie napisz do @BotFather i wyślij `/newbot`.
2. Podaj nazwę i username bota (username kończy się na `bot`).
3. Zapisz token, który dostaniesz.

Zmienne na Renderze, przy backendzie:

```text
Telegram__BotToken=TOKEN_Z_BOTFATHER
Telegram__BotUsername=nazwa_bota
Telegram__WebhookSecret=LOSOWY_SEKRET
```

- `Telegram__BotUsername` — bez `@`.
- `Telegram__WebhookSecret` — losowy, np. `openssl rand -hex 32`. Telegram wysyla go w naglowku `X-Telegram-Bot-Api-Secret-Token`.
- Lokalnie zamiast webhooka ustaw `Telegram__UsePolling=true`; backend sam pobiera aktualizacje przez `getUpdates`.
- Lokalnie używaj OSOBNEGO bota z BotFathera: polling (`UsePolling`) wywołuje `deleteWebhook` i po cichu zepsułby produkcję na tym samym bocie; po wdrożeniu sprawdź webhook przez `getWebhookInfo`.
- Token bota nie trafia do logow (logowanie HttpClient wylaczone dla tego klienta).

### Dodawanie zadań z bota

Wiadomość tekstowa (nie komenda) albo głosówka wysłana do bota z połączonego prywatnego czatu trafia do tego samego silnika co `POST /ai/dictation`: ta sama konfiguracja AI użytkownika (Ustawienia → AI) i ten sam dzienny limit dyktowania. Głosówka może mieć najwyżej 3 minuty i `Ai:Dictation:MaxAudioBytes` (domyślnie 8 MB). Bot pobiera ją przez `getFile` i `https://api.telegram.org/file/bot<token>/<path>`; ani token, ani ścieżka pliku nie trafiają do logów.

Bot odpisuje listą rozpoznanych zadań (tytuł, termin, projekt, priorytet) z przyciskami `Dodaj wszystkie` i `Anuluj`. Lista czeka w tabeli `telegram_task_drafts` przez 24 godziny. „Dodaj wszystkie” tworzy zadania tak jak zwykłe dodawanie (aktywność, odświeżenie na żywo w aplikacji), a drugie kliknięcie niczego nie dubluje. Po kliknięciu bot zdejmuje przyciski. Wiadomość z niepołączonego czatu dostaje instrukcję połączenia.

Szkic pamięta czat i `message_id` wiadomości źródłowej (unikalny indeks). Gdy Telegram ponowi webhook dla wiadomości, która ma już szkic, bot ją pomija i nie pyta AI drugi raz. Rozpoznanie AI dzieje się w trakcie żądania od Telegrama (do `Ai:Dictation:TimeoutSeconds`, domyślnie 60 s); ponowienie, które przyjdzie, zanim pierwszy szkic się zapisze, wywoła AI jeszcze raz, ale druga lista nie zostanie wysłana.

Migracja `AddTelegramTaskDrafts` zastosowana jak pozostałe (punkt 1). Wdrożenie: VPS (`deploy/vps/deploy.sh ../mindflow-api-reminders`), migracje skryptem idempotentnym z backupem bazy.

### Plan dnia od AI

Zaraz po porannym briefie (ten sam przebieg joba `POST /internal/jobs/notifications`) bot wysyła szczegółowy plan dnia: podsumowanie dnia, harmonogram godzinowy (maks. 12 punktów), sekcje „Najważniejsze”, „Uwaga” (tylko gdy są ryzyka) i „Wskazówka” oraz przycisk `Otwórz Mindflow` (tylko https). Plan pisze `DayPlanWriter` (`Services/Ai`) z konfiguracją AI użytkownika (jego klucz albo klucze instancji w kolejności `Ai:ProviderOrder`). Do AI idzie ten sam zminimalizowany pakiet co przy sugestiach (tytuły i metadane zadań bez opisów i GUID-ów, numery `#ref`) plus dzisiejsze bloki z kalendarza, godziny terminów i `estimatedHours`; numery `#ref` w odpowiedzi serwer zamienia z powrotem na tytuły. Strefa czasowa: `User.TimeZone`, domyślnie `Europe/Warsaw`.

Plan liczy się do dziennego limitu ręcznej analizy (`Ai:ManualAiDailyLimit`, z własnym kluczem `Ai:OwnKeyManualAiDailyLimit`). Gdy limit jest wykorzystany, AI zwróci błąd albo niepoprawny JSON, bot wysyła plan lokalny (bloki i zadania z godziną według czasu, potem zadania na dziś i zaległe według priorytetu w wolnych oknach) z nagłówkiem „🧠 Plan dnia” zamiast „🧠 Plan dnia od AI”, a w logu jest ostrzeżenie `Day plan for user … uses the local fallback.`. Plan wychodzi zawsze.

Pod planem przychodzą maks. 3 sugestie z „Przeanalizuj dzień” (ścieżka crona, bez ręcznego limitu), każda osobną wiadomością z przyciskami `Przyjmij` i `Odrzuć`. Przyjęcie wykonuje akcje sugestii tak jak w aplikacji; po kliknięciu bot zdejmuje przyciski. Wysyłane są tylko sugestie od dostawcy AI; gdy AI nic nie zaproponuje albo zwróci błąd (zostają tylko lokalne `RuleBased`), wiadomości z sugestiami są pomijane, a w logu jest ostrzeżenie `AI plan for user … has no AI suggestions.`.

Plan dostają tylko konta z połączonym Telegramem, włączonym porannym briefem i `telegramRemindersEnabled`. Wychodzi raz dziennie: klucz `telegram-ai-plan:<data>` w `push_notification_deliveries` jest zajmowany przed wywołaniem AI. Ta gałąź nie ma migracji.

Jednorazowe ustawienie webhooka na produkcji:

```bash
curl -X POST "https://api.telegram.org/bot$TELEGRAM_BOT_TOKEN/setWebhook" \
  -H 'Content-Type: application/json' \
  -d '{"url":"https://<api>/integrations/telegram/webhook","secret_token":"<Telegram__WebhookSecret>","allowed_updates":["message","callback_query"]}'
```

Endpointy:

- `GET /integrations/telegram` — status połączenia;
- `POST /integrations/telegram/link` — link z kodem;
- `DELETE /integrations/telegram` — rozłączenie;
- `POST /integrations/telegram/webhook` — webhook bota (nagłówek `X-Telegram-Bot-Api-Secret-Token`).

Migracja `AddTelegramReminders` zastosowana jak pozostale (punkt 1).

## Reczne sprawdzenie joba

Przed konfiguracja Cloudflare mozna wywolac job recznie:

```bash
curl -X POST 'https://twoj-backend.onrender.com/internal/jobs/notifications' \
  -H 'X-Job-Key: LOSOWY_DLUGI_SEKRET'
```

Odpowiedz zawiera liczbe wyslanych briefow, przypomnien o blokach, przypomnien o zadaniach z godzina i podsumowan wieczornych.
