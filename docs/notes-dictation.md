# Dyktowanie notatek

## Przepływ

1. `POST /notes/dictation` (JSON `{ "text" }` albo multipart `audio`) waliduje wejście, rezerwuje limit i tworzy notatkę `Processing` wraz ze zleceniem `note_dictation_jobs`.
2. Odpowiedź `202 Accepted` zwraca notatkę. Przy przyjęciu leci `NoteCreated`.
3. `NoteDictationWorker` (kolejka w pamięci) bierze `noteId` i uruchamia `NoteDictationProcessor.ProcessAsync`.
4. Sukces: notatka `Ready`, zapisane sugestie zadań, zlecenie usunięte (nagranie znika z bazy), leci `NoteUpdated`.
5. Błąd: notatka `Failed`, zlecenie zostaje, leci `NoteUpdated`. Użytkownik może ponowić przez `POST /notes/{id}/retry`.

## Endpointy

| Metoda | Ścieżka | Opis |
|---|---|---|
| POST | `/notes/dictation` | Dyktowanie tekstu (JSON) lub nagrania (multipart `audio`). Zwraca 202. Limiter `AiRateLimits`. |
| POST | `/notes/{id}/retry` | Ponowne przetworzenie notatki `Failed`. Nie liczy limitu. |
| POST | `/notes/{id}/suggestions/{sid}/accept` | Tworzy zadanie z sugestii. Body opcjonalne: nadpisania pól, `clearDueDate`, `clearProject`. |
| POST | `/notes/{id}/suggestions/{sid}/reject` | `Pending → Rejected`. |
| POST | `/notes/{id}/suggestions/{sid}/restore` | `Rejected → Pending`. |
| POST | `/notes/{id}/suggestions/accept-all` | Akceptuje wszystkie `Pending` po kolei (pomija błędy pojedynczych). |
| GET | `/notes` | Lista. Pole `pendingSuggestions` (liczba sugestii `Pending`). |
| GET | `/notes/{id}` | Szczegóły. Pole `suggestions` (lista z `status`, `taskId`, `projectName`). |

## Limity

- Licznik `dictation_calls` jest wspólny z dyktowaniem zadań (`/ai/dictation`). Obsługuje go `DictationQuota`.
- Liczony przy przyjęciu `POST /notes/dictation`, nie w workerze. Notatka, która się nie uda, też zużywa limit.
- Po wyczerpaniu limitu: przy Gemini przechodzi na model Lite (`LiteDailyLimit`, `GeminiFallbackModel`), notatka dostaje dopisek Lite. Dla innych dostawców albo po wyczerpaniu Lite: 429.
- Retry nie liczy limitu. Maksymalnie 3 próby (`Attempts`). Po trzeciej porażce retry zwraca 400.

## Zlecenia i odporność

- Tabela `note_dictation_jobs` (klucz `note_id`): audio albo tekst, `lite`, `attempts`.
- Audio trzymane tylko do sukcesu. Po 3 nieudanych próbach kolumna `audio` jest zerowana.
- Przy starcie worker wrzuca do kolejki zlecenia, których notatka jest `Processing` (przeżywa restart).
- Jeden worker na instancję. Wystarcza na jeden VPS. Przy kilku instancjach kolejka w pamięci nie wystarczy: każda instancja skanowałaby i przetwarzała te same zlecenia (wymagany wtedy lock w bazie albo kolejka zewnętrzna).
- Notatki `Failed` nie są wznawiane automatycznie po restarcie, tylko ręcznie przez retry.
- Wyłączenie workera: `Notes:Dictation:WorkerEnabled=false` (używane w testach).

## Prywatność

- Do AI idzie tylko nagranie albo tekst notatki, aktualna data i godzina z przesunięciem strefy użytkownika, lista kalendarza (±7 dni) i nazwy dostępnych projektów.
- Nie idą: inne notatki, zadania użytkownika, identyfikatory.
- Treść traktowana jako dane, nie polecenia (zasada 12 w prompcie).
- Notatka i zlecenia tylko właściciela. Cudza notatka lub sugestia → 404.

## Migracja

- Migracja EF: `AddNoteDictation` (tworzy `note_dictation_jobs` i `note_task_suggestions`).
- Skrypt SQL: `docs/sql/20261112131106_AddNoteDictation.sql`. Idempotentny, sprawdza `__EFMigrationsHistory`.
- Na żywej bazie nie uruchamiać `dotnet ef database update` bez zgody; używać skryptu.
