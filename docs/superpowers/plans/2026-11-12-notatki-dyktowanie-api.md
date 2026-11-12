# Dyktowanie notatek z AI — API (gałąź 3a) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven-development (recommended) or executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `POST /notes/dictation` przyjmuje nagranie albo tekst, od razu tworzy notatkę „Przetwarzanie…” (202), a worker w tle składa z AI sformatowaną notatkę markdown (tytuł, data wydarzenia, tagi, sekcje, transkrypcja) z przypiętymi sugestiami zadań, które użytkownik przyjmuje, odrzuca albo edytuje.

**Architecture:** Wspólny licznik i przejście na Lite wydzielone z `DictationService` do `DictationQuota`; budowa kontekstu (data, strefa, projekty) do `DictationContextBuilder` (działa po `userId`, bez HTTP). Silniki (`GeminiDictationEngine`, `OpenAiDictationEngine`) dostają zestaw promptów (`IDictationPrompts`) jako parametr, więc obsługują i zadania (`TaskDictationPrompts` = dzisiejszy `DictationPrompt`), i notatki (`NoteDictationPrompts`). Zlecenie trafia do tabeli `note_dictation_jobs` (nagranie w `bytea` tylko do przetworzenia) i do `NoteDictationQueue` (`Channel<Guid>`); `NoteDictationWorker` (`BackgroundService`) przetwarza po jednym, przy starcie podejmuje zaległe. Sugestie w `note_task_suggestions`; akceptacja tworzy zadanie przez `ITaskService.CreateAsync`.

**Tech Stack:** .NET 10, EF Core (Postgres, snake_case, `jsonb`, `bytea`), `System.Threading.Channels`, xUnit + WebApplicationFactory (`AiApiFactory`, `FakeAiHandler`).

**Spec:** `/Users/filipwasiewicz/Projekty/Mindflow/mindflow-web-reminders/docs/superpowers/specs/2026-11-12-notatki-design.md` (sekcja 3)

## Global Constraints

- Worktree `/Users/filipwasiewicz/Projekty/Mindflow/mindflow-api-reminders`, gałąź `feature/note-dictation` (utworzona od `origin/develop` = 9314264, zawiera `/notes` z gałęzi 1).
- Commity: Conventional Commits po angielsku, bez opisu, **bez `Co-Authored-By`**, bez wzmianek o AI; test w tym samym commicie; zero komentarzy „dlaczego”.
- Teksty dla użytkownika po polsku. Tytuł notatki w trakcie: `Nowa notatka`. Dopisek Lite na górze treści: `> Tryb Lite: dzienny limit lepszego modelu jest wykorzystany, więc notatka może być mniej dokładna.` + pusta linia. Błąd 429 jak przy dyktowaniu zadań (ten sam komunikat z `DictationQuota`). Retry niedozwolony: `BadRequestException("Tej notatki nie można już przetworzyć ponownie.")`.
- Limit: **wspólny** licznik `dictation_calls` z dyktowaniem zadań; liczony przy przyjęciu `POST /notes/dictation` (nie w workerze); retry nie liczy limitu. Lite tylko dla Gemini, jak w `feature/dictation-lite-fallback` (`Ai:Dictation:LiteDailyLimit`, `Ai:Dictation:GeminiFallbackModel`).
- Wejście jak `/ai/dictation`: multipart `audio` (≤ `Ai:Dictation:MaxAudioBytes`, typ przez `AudioFormats.Normalize`) albo JSON `{ "text": "…" }` (≤ `Ai:Dictation:MaxTextLength`); ta sama walidacja i ten sam limiter (`[EnableRateLimiting(AiRateLimits.Policy)]`).
- Przetwarzanie: maks. 3 próby (`Attempts`), timeout jak `Ai:Dictation:TimeoutSeconds`. Po sukcesie rekord zlecenia usuwany (nagranie znika z bazy). Po błędzie: `Note.Status = Failed`, zlecenie zostaje.
- Wynik AI (JSON): `title` (≤ 200), `eventDate` (`yyyy-MM-dd` albo null, zakres jak daty w `DictationResultParser`), `tags` (≤ 5, normalizacja `NoteText.NormalizeTags`), `markdown` (bez HTML — usuń znaczniki `<…>` przed zapisem; ≤ 100 000), `transcript`, `tasks` (ten sam kształt i parser co dyktowanie zadań: `DictationResultParser.Parse` czyta `transcript` i `tasks` z tego samego JSON-a). Sekcje markdownu w prompcie: `## Co się wydarzyło`, `## Co z tego wynika`, `## Do zapamiętania` (puste pomijane).
- Prywatność: do AI tylko nagranie/tekst, data/strefa, nazwy projektów (jak dziś). Notatka i zlecenia tylko właściciela; cudza notatka → 404.
- Zdarzenia SignalR: `NoteCreated` przy przyjęciu, `NoteUpdated` po przetworzeniu/błędzie/akceptacji sugestii.
- Testy DB: `docker start mindflow-db-reminders`, hasło z `docker inspect mindflow-db-reminders --format '{{range .Config.Env}}{{println .}}{{end}}' | sed -n 's/^POSTGRES_PASSWORD=//p'`, `ConnectionStrings__Database="Host=localhost;Port=5435;Database=mindflow_reminders_tests;Username=postgres;Password=$PW"`; po migracji `dotnet ef database update` z tym connection stringiem (fabryka nie migruje). Nigdy nie wypisuj hasła.
- Build/testy: `dotnet build src/Mindflow/Mindflow.sln`, `ConnectionStrings__Database='…' dotnet test src/Mindflow/Mindflow.sln` (289 przed gałęzią).
- Rozmiar: ≲ 20 plików i 1000 linii bez migracji; jeśli przekroczy, Task 4 idzie osobną gałęzią `feature/note-suggestions` od tej.

---

### Task 1: Refaktor dyktowania — `DictationQuota`, `DictationContextBuilder`, `IDictationPrompts`

**Files:**
- Create: `src/Mindflow/Mindflow.Api/Services/Ai/Dictation/DictationQuota.cs` (+ interfejs `IDictationQuota` w tym samym pliku albo osobno, wg wzoru repo)
- Create: `src/Mindflow/Mindflow.Api/Services/Ai/Dictation/DictationContextBuilder.cs` (+ `IDictationContextBuilder`)
- Create: `src/Mindflow/Mindflow.Api/Services/Ai/Dictation/IDictationPrompts.cs`, `TaskDictationPrompts.cs`
- Modify: `DictationService.cs`, `IDictationEngine.cs`, `GeminiDictationEngine.cs`, `OpenAiDictationEngine.cs`, DI w `ServiceCollectionExtensions.cs`
- Test: istniejące `Mindflow.Api.Tests/Ai/DictationApiTests.cs`, `DictationEngineTests.cs`, testy Telegrama — muszą przejść bez zmian zachowania (dopuszczalne tylko zmiany wywołań `BuildRequest` w testach silników).

**Interfaces (Produces):**
- `record DictationReservation(AiCredentials Credentials, bool Lite, AiUsageResponse Usage);`
- `IDictationQuota.ReserveAsync(Guid userId, AiCredentials credentials, CancellationToken ct) → Task<DictationReservation>`: logika 1:1 z dzisiejszego `DictationService` (limit zależny od źródła klucza, Lite dla Gemini po limicie, `TooManyRequestsException` z tym samym tekstem, inkrementacja licznika), zwraca credentials z podmienionym modelem przy Lite.
- `IDictationContextBuilder.BuildAsync(Guid userId, CancellationToken ct) → Task<DictationContext>` (przeniesione `BuildContextAsync` + `ResolveZone`).
- `interface IDictationPrompts { string SystemInstruction(DictationContext c, bool audio); string UserText(string text); string AudioUserText { get; } object GeminiSchema(DictationContext c); object OpenAiSchema(DictationContext c); }`; `TaskDictationPrompts : IDictationPrompts` deleguje do `DictationPrompt` (singleton `TaskDictationPrompts.Instance`).
- `IDictationEngine.RunAsync(AiCredentials credentials, DictationInput input, DictationContext context, IDictationPrompts prompts, CancellationToken ct)`; `GeminiDictationEngine.BuildRequest(string model, DictationInput input, DictationContext context, IDictationPrompts prompts)` i analogicznie OpenAI.
- Statyczna `DictationInputRules.Validate(DictationInput input, DictationOptions options)` (wyciągnięta z `DictationService.Validate`, te same wyjątki i teksty).

- [ ] Step 1: Uruchom pełny zestaw testów — baseline zielony (289).
- [ ] Step 2: Refaktor jak wyżej; `DictationService` korzysta z `IDictationQuota`, `IDictationContextBuilder`, `DictationInputRules`, `TaskDictationPrompts.Instance`. Zachowanie i odpowiedzi `/ai/dictation` bez zmian.
- [ ] Step 3: Dodaj testy jednostkowe `DictationQuota` tylko jeśli istniejące `DictationApiTests` (limit, Lite, własny klucz) nie pokrywają ścieżek — one już je pokrywają przez API, więc wystarczy, że przechodzą.
- [ ] Step 4: Pełny zestaw zielony. Commit `refactor: extract dictation quota, context and prompts`.

### Task 2: Encje i migracja — `note_dictation_jobs`, `note_task_suggestions`

**Files:**
- Create: `Models/NoteDictationJob.cs`, `Models/NoteTaskSuggestion.cs`, `Models/Enums/NoteSuggestionStatus.cs` (`Pending`, `Accepted`, `Rejected`)
- Modify: `Data/MindflowDbContext.cs` (DbSety + konfiguracja), migracja `AddNoteDictation` + `docs/sql/<ts>_AddNoteDictation.sql` (idempotentny skrypt od `20261112121730_AddNotes`, bez BOM)
- Test: w Task 3/4 (encje bez logiki)

`NoteDictationJob`: `NoteId` (PK i FK do `notes`, Cascade), `UserId` (FK users, Cascade), `Audio byte[]?` (`bytea`), `AudioMimeType string?` (≤ 64), `Text string?`, `Lite bool`, `Attempts int`, `CreatedAt`. Tabela `note_dictation_jobs`.

`NoteTaskSuggestion`: `Id`, `NoteId` (FK `notes`, Cascade), `Order int`, `Title` (≤ 200), `Description string?`, `DueDate DateOnly?`, `DueTime TimeOnly?`, `Priority TaskPriority` (konwersja jak w `TaskItem`), `ProjectId Guid?` (FK `projects`, SetNull), `Subtasks List<string>` (`jsonb`), `Status NoteSuggestionStatus` (string, ≤ 16), `TaskId Guid?` (bez FK), `CreatedAt`. Tabela `note_task_suggestions`, indeks `(note_id, order)`.

Migracja musi zawierać **tylko** te dwie tabele, ich FK i indeksy; inaczej (rozjazd snapshotu) — zatrzymaj się i zgłoś. Zastosuj lokalnie (`dotnet ef database update`). Commit `feat: add note dictation jobs and task suggestions` (razem z Task 3, jeśli encje bez użycia psują analizę — wtedy jeden commit w Task 3).

### Task 3: `POST /notes/dictation`, kolejka, worker, retry

**Files:**
- Create: `Services/Notes/NoteDictationPrompts.cs` (`IDictationPrompts` dla notatek: instrukcja systemowa po polsku z zasadami z Global Constraints + zasady zadań z `DictationPrompt` w skrócie: tytuł w bezokoliczniku, daty z kalendarza, P1–P4, projekty tylko z listy; schematy Gemini/OpenAI z polami `transcript`, `title`, `eventDate`, `tags`, `markdown`, `tasks` — `tasks` ten sam kształt co dziś)
- Create: `Services/Notes/NoteDictationResultParser.cs` (`Parse(string? raw, DictationContext context, int maxTasks) → NoteDictationResult(string Title, DateOnly? EventDate, List<string> Tags, string Markdown, string Transcript, IReadOnlyList<DictatedTaskResponse> Tasks)`; zadania przez `DictationResultParser.Parse(raw, context, maxTasks)`; markdown: usunięcie znaczników HTML regexem `<[^>]+>`, przycięcie; pusty tytuł → `Notatka z {dd.MM.yyyy}` dnia `context.Today`)
- Create: `Services/Notes/NoteDictationQueue.cs` (singleton, `Channel<Guid>` unbounded: `Enqueue(Guid noteId)`, `ReadAllAsync(ct)`)
- Create: `Services/Notes/NoteDictationProcessor.cs` (scoped: `ProcessAsync(Guid noteId, CancellationToken ct)` — cała praca jednego zlecenia; testowalny bez workera)
- Create: `Services/Notes/NoteDictationWorker.cs` (`BackgroundService`: przy starcie wrzuca do kolejki `NoteId` wszystkich zleceń z `note_dictation_jobs`, których notatka ma `Status = Processing`; potem pętla `ReadAllAsync` → nowy scope → `processor.ProcessAsync`; wyjątki łapane i logowane, pętla trwa)
- Modify: `Services/Notes/INoteService.cs`, `NoteService.cs` (`DictateAsync(DictationInput input, ct) → NoteResponse`, `RetryAsync(Guid id, ct) → NoteResponse`), `Controllers/NotesController.cs` (`POST dictation` JSON i multipart jak `DictationController`, z limiterem; `POST {id}/retry`), DI (`AddSingleton<NoteDictationQueue>`, `AddScoped<NoteDictationProcessor>`, `AddHostedService<NoteDictationWorker>` — rejestracja workera **wyłączalna** opcją `Notes:Dictation:WorkerEnabled` (domyślnie `true`), żeby testy mogły sterować przetwarzaniem)
- Test: `Mindflow.Api.Tests/Notes/NoteDictationApiTests.cs` (+ ewentualnie `NoteDictationResultParserTests.cs`)

Zachowanie `DictateAsync`: walidacja (`DictationInputRules`), credentials (`resolver.ResolveAsync(userId).FirstOrDefault()` albo `ServiceUnavailableException` jak dziś), `quota.ReserveAsync` (429 → nic nie powstaje), utworzenie `Note { Source = Dictation, Status = Processing, Title = "Nowa notatka", ReviewedAt = null, Content = "" }` i `NoteDictationJob { Audio, AudioMimeType, Text, Lite = reservation.Lite }` w jednym `SaveChanges`, `NoteCreated`, `queue.Enqueue(note.Id)`, zwrot `NoteResponse` z kodem 202 (`Accepted`).

Zachowanie `ProcessAsync(noteId)`: wczytaj zlecenie + notatkę (brak albo notatka nie `Processing` → koniec); `Attempts++` i zapis; credentials ponownie z resolvera dla `job.UserId` (przy `Lite` podmień model na `GeminiFallbackModel`); kontekst z `IDictationContextBuilder`; silnik po `Provider`; timeout; `NoteDictationResultParser.Parse`; uzupełnij notatkę (`Title`, `Content` = dopisek Lite (gdy `Lite`) + markdown, `Tags`, `EventDate`, `Transcript` = przy tekście tekst wejściowy, przy nagraniu transkrypcja z wyniku, `Status = Ready`, `UpdatedAt = now`, `ReviewedAt` zostaje `null`), dodaj sugestie (`Order` wg kolejności, `Status = Pending`, `ProjectId` z parsera), usuń zlecenie, zapis, `NoteUpdated`. Błąd AI/HTTP/timeout/parsowania: `Status = Failed`, `UpdatedAt = now`, zlecenie zostaje, zapis, `NoteUpdated`, log ostrzeżenia (bez treści notatki).

`RetryAsync(id)`: notatka właściciela (`FindOwnAsync`), `Status == Failed`, zlecenie istnieje i `Attempts < 3` → `Status = Processing`, zapis, `NoteUpdated`, `Enqueue`; inaczej `BadRequestException` z tekstem z Global Constraints. Bez liczenia limitu.

`NoteResponse` i `NoteListItemResponse`: bez zmian w tym tasku (sugestie w Task 4).

Testy (`NoteDictationApiTests`, fabryka: `AiApiFactory` z `Notes:Dictation:WorkerEnabled=false`, procesor wywoływany ręcznie przez `factory.Services.CreateScope()` → `NoteDictationProcessor.ProcessAsync`):
1. Tekst → 202, notatka `Processing`, `Source = Dictation`, `IsNew`, zlecenie w bazie; po `ProcessAsync` notatka `Ready`, tytuł/markdown/tagi/`eventDate`/transkrypcja z odpowiedzi fake AI, sugestie zapisane w kolejności, zlecenie usunięte; HTML w markdownie usunięty.
2. Nagranie (multipart `audio/webm;codecs=opus`) → zlecenie ma `Audio` i `audio/webm`; żądanie do Gemini ma `inline_data`; po przetworzeniu `Audio` nie ma w bazie (zlecenie usunięte).
3. Wspólny limit: przy `Ai:Dictation:DailyLimit=3`, `LiteDailyLimit=2` — 3 wywołania `/ai/dictation` + 2 `/notes/dictation` przechodzą (ostatnie notatki z `Lite=true` i dopiskiem Lite po przetworzeniu, model `gemini-3.5-flash-lite` w ścieżce żądania), szóste `/notes/dictation` → 429 i żadna nowa notatka nie powstaje.
4. Błąd AI (fake zwraca 500 dwa razy, także na fallbacku) → `Failed`, zlecenie zostaje, `Attempts = 1`; `POST /notes/{id}/retry` → `Processing` bez zmiany licznika `dictation_calls`; po udanym `ProcessAsync` → `Ready`. Po 3 nieudanych próbach retry → 400.
5. Cudza notatka: `retry` → 404. Usunięcie notatki usuwa zlecenie i sugestie (kaskada).
6. Worker: z włączonym workerem (osobna mała fabryka albo `WorkerEnabled=true`) zaległe zlecenie wstawione do bazy przed startem hosta zostaje przetworzone (`vi.waitFor`-odpowiednik: pętla z `Task.Delay` do 5 s sprawdzająca `Status = Ready`).

- [ ] TDD: testy → FAIL → implementacja → PASS → pełny zestaw zielony. Commit `feat: dictate notes in the background`.

### Task 4: Sugestie zadań w notatce

**Files:**
- Modify: `Models/Dtos/NoteDtos.cs` (`NoteSuggestionResponse(Guid Id, string Title, string? Description, DateOnly? DueDate, TimeOnly? DueTime, TaskPriority Priority, Guid? ProjectId, string? ProjectName, IReadOnlyList<string> Subtasks, NoteSuggestionStatus Status, Guid? TaskId)`; `NoteResponse` + `IReadOnlyList<NoteSuggestionResponse> Suggestions`; `NoteListItemResponse` + `int PendingSuggestions`; `AcceptNoteSuggestionRequest(string? Title, string? Description, DateOnly? DueDate, TimeOnly? DueTime, TaskPriority? Priority, Guid? ProjectId, IReadOnlyList<string>? Subtasks)` — wszystkie pola opcjonalne, brak = wartość z sugestii; `ClearDueDate bool`/`ClearProject bool` dla świadomego wyczyszczenia)
- Modify: `NoteMapper` (mapowanie z sugestiami; lista liczy `Pending`), `NoteService` (wczytywanie sugestii w `GetAsync`/odpowiedziach; `ListAsync` liczy `Pending` jednym zapytaniem grupującym)
- Create: `Services/Notes/NoteSuggestionService.cs` (+ interfejs): `AcceptAsync(noteId, suggestionId, AcceptNoteSuggestionRequest?)`, `RejectAsync`, `RestoreAsync`, `AcceptAllAsync(noteId)`
- Modify: `NotesController` (`POST {id}/suggestions/{sid}/accept|reject|restore`, `POST {id}/suggestions/accept-all`)
- Modify: `Services/Telegram/TelegramTaskCapture.cs` — mapowanie `DictatedTaskResponse → CreateTaskRequest` wyciągnięte do wspólnego `Services/Ai/Dictation/DictatedTaskRequests.cs` (`ToCreateRequest(...)`) i używane w obu miejscach
- Test: `Mindflow.Api.Tests/Notes/NoteSuggestionsApiTests.cs`

Zachowanie:
- `AcceptAsync`: notatka właściciela; sugestia tej notatki; atomowo `Pending → Accepted` (`ExecuteUpdateAsync` z warunkiem `Status == Pending`; 0 wierszy → `BadRequestException("Ta sugestia jest już obsłużona.")`); zbudowanie `CreateTaskRequest` z sugestii nadpisanej polami żądania; `ProjectId` spoza dostępu → `BadRequestException("Nie masz dostępu do tego projektu.")` i przywrócenie `Pending`; `ITaskService.CreateAsync` (null/błąd → przywróć `Pending` i rzuć dalej); zapis `TaskId` oraz nadpisanych pól w sugestii; `NoteUpdated`; zwrot `NoteSuggestionResponse`.
- `RejectAsync`: `Pending → Rejected`. `RestoreAsync`: `Rejected → Pending` (zaakceptowanej nie da się przywrócić). `AcceptAllAsync`: akceptuje wszystkie `Pending` po kolei, zwraca `NoteResponse`.
- `ProjectName` w odpowiedzi z projektów dostępnych użytkownikowi (jedno zapytanie).

Testy: akceptacja tworzy zadanie z polami sugestii (GET `/tasks/{taskId}` zwraca tytuł, termin, priorytet, projekt, podzadania), nadpisanie tytułu i terminu przy akceptacji, podwójna akceptacja → 400 i jedno zadanie, odrzucenie + przywrócenie, akceptuj wszystkie (pomija odrzucone), `PendingSuggestions` na liście, cudza notatka → 404 na wszystkich końcówkach, Telegram dalej działa (istniejące testy).

- [ ] TDD → commit `feat: accept tasks suggested in notes`.

### Task 5: Dokumentacja i skrypt SQL

- [ ] `docs/sql/<ts>_AddNoteDictation.sql` (jeśli nie powstał w Task 2) i wpis w `docs/push-notifications.md`? — nie: dopisz krótką sekcję do nowego pliku `docs/notes-dictation.md` (endpointy, przepływ, limity, zlecenia, retry, prywatność). Commit `docs: describe note dictation`.
