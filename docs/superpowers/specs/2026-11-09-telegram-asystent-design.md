# Telegram jako asystent: briefy, dodawanie zadań, plan dnia od AI

Data: 2026-11-09. Repo: `mindflow-api`. Wymaga gałęzi `feature/reminder-telegram` (bot, `TelegramConnection`, `TelegramApi`, `TelegramService`, webhook i polling).

## Decyzje Filipa

- Briefy (poranny, w południe) i wieczorne podsumowanie mają przychodzić także na Telegram. Przypomnienia o blokach i zadaniach cyklicznych zostają bez zmian.
- Zadania tworzone z wiadomości do bota (tekst albo głosówka) bot najpierw pokazuje do akceptacji: „Dodaj wszystkie” albo „Anuluj”.
- Plan dnia od AI przychodzi rano jako **osobna wiadomość**, wysłana po porannym briefie. Każda sugestia ma przyciski Przyjmij / Odrzuć.
- Działamy tylko na VPS. Render i Supabase nie są ruszane.

## 1. Briefy i podsumowanie na Telegramie (`feature/telegram-briefs`)

- Dziś `NotificationService.SendOnceAsync` wysyła brief tylko przez Web Push i zapisuje wpis w inboxie tylko wtedy, gdy push się udał. Po zmianie brief idzie przez push **i** Telegram. Wpis w inboxie oraz dostawa w `push_notification_deliveries` powstają, gdy dotarł przez co najmniej jeden kanał.
- Job obejmuje także użytkowników bez subskrypcji push, jeśli mają połączony Telegram.
- Telegram dostaje tytuł pogrubiony i treść (HTML, wszystko enkodowane) oraz przycisk „Otwórz Mindflow” (link do powiadomienia w aplikacji, tylko https).
- Respektowane są istniejące ustawienia: główny przełącznik `Enabled`, przełączniki i godziny briefów oraz `TelegramRemindersEnabled`. Ten ostatni teraz oznacza „powiadomienia na Telegram” w ogóle; etykieta w UI zostaje.
- Przypomnienia o blokach i zadaniach cyklicznych: bez zmian (tylko push).

## 2. Dodawanie zadań z bota (`feature/telegram-task-capture`, od 1)

- Wiadomość tekstowa (nie komenda) albo głosówka (`voice`, ogg/opus, maks. 3 min jak w dyktowaniu) z połączonego **prywatnego** czatu trafia do istniejącego silnika dyktowania AI (to samo, co `POST /ai/dictation`, łącznie z dziennym limitem i konfiguracją AI użytkownika). Głosówkę bot pobiera przez `getFile` i pobranie pliku z `https://api.telegram.org/file/bot<token>/<path>`, a tokena nie loguje.
- Bot odpisuje listą rozpoznanych zadań: tytuł, termin (data i godzina), projekt, priorytet. Pod listą są przyciski `Dodaj wszystkie` i `Anuluj`.
- Rozpoznane zadania czekają w bazie (nowa tabela `telegram_task_drafts`: id, user, JSON z zadaniami, utworzono, wygasa po 24 h, status). `callback_data` ma postać `cap:add:<id N>` / `cap:no:<id N>`.
- „Dodaj wszystkie” tworzy zadania przez `TaskService.CreateAsync` w imieniu użytkownika (aktywność i SignalR jak przy zwykłym dodawaniu), oznacza szkic jako użyty (drugie kliknięcie niczego nie dubluje), odpowiada „✅ Dodano N zadań” i zdejmuje przyciski (`editMessageReplyMarkup`). „Anuluj” odpowiada „Anulowano”.
- Gdy AI nic nie rozpozna: „Nie znalazłem zadań w tej wiadomości.” Gdy minie dzienny limit albo AI zwróci błąd: krótki komunikat po polsku, bez szczegółów technicznych.
- Gdy wiadomość przyjdzie z niepołączonego czatu, bot odpisuje instrukcją połączenia, jak dziś.

## 3. Plan dnia od AI (`feature/telegram-ai-plan`, od 2)

- Po wysłaniu porannego briefu (ten sam przebieg joba, osobny klucz deduplikacji `telegram-ai-plan:<data>`) API generuje sugestie przez istniejący `SuggestionService` (ten sam co „Przeanalizuj dzień”, z konfiguracją AI użytkownika) i wysyła je osobną wiadomością na Telegram: nagłówek „🧠 Plan dnia od AI”, a pod nim każda sugestia jako osobna wiadomość z treścią i przyciskami `Przyjmij` / `Odrzuć` (`sg:ok:<id N>` / `sg:no:<id N>`). Wysyłane są maks. 3 sugestie.
- Przyjęcie i odrzucenie idą przez istniejącą logikę akceptacji i odrzucenia sugestii (wykonanie akcji na zadaniach), tylko dla sugestii właściciela czatu. Po kliknięciu bot zdejmuje przyciski i odpowiada „✅ Przyjęto” albo „Odrzucono”.
- Tylko dla użytkowników z połączonym Telegramem, włączonym porannym briefem i `TelegramRemindersEnabled`. Gdy AI nic nie zaproponuje albo zwróci błąd, plan nie jest wysyłany, a do logu trafia ostrzeżenie.

## Testy

Integracyjne (`LocalDatabaseFact`, `TelegramApiFactory`, `FakeTelegramHandler`, sztuczny handler AI wzorem `AiApiFactory`/`FakeAiHandler`):

- briefy trafiają na Telegram bez subskrypcji push i nie dublują się;
- tekst i głosówka tworzą szkic, „Dodaj wszystkie” tworzy zadania raz, „Anuluj” nic nie tworzy, a szkic innego użytkownika jest odrzucany;
- plan AI jest wysyłany raz dziennie po porannym briefie, a Przyjmij/Odrzuć działają tylko dla właściciela.

Wdrożenie: VPS (`deploy/vps/deploy.sh ../mindflow-api-reminders`), migracje skryptem idempotentnym z backupem bazy.
