# Mindflow API - instrukcje dla agentow

To repozytorium zawiera backend Mindflow.

- Repo: `git@github.com:Wasiewicz11/Mindflow-api.git`
- Domyslna galaz robocza: `develop`
- Glowne rozwiazanie: `src/Mindflow/Mindflow.sln`
- Glowny projekt API: `src/Mindflow/Mindflow.Api/Mindflow.Api.csproj`
- Runtime: ASP.NET Core Web API, .NET 10
- Baza: PostgreSQL przez Entity Framework Core + Npgsql
- Auth: JWT Mindflow + Google JWT
- Storage: Supabase Storage przez klienta S3
- Realtime: SignalR hub `TasksHub`

Przed praca z Gitem przeczytaj tez:

```bash
/Users/filipwasiewicz/Projekty/Mindflow/GIT.md
```

Nie commituj automatycznie. Commit rob tylko wtedy, gdy Filip wyraznie o to poprosi.

## Tokenmaxxing

Zawsze pracuj tak, zeby ograniczac zuzycie tokenow, czas i koszt.

Przed czytaniem duzych plikow najpierw znajdz waski kontekst:

```bash
rg "szukany_symbol" src/Mindflow/Mindflow.Api
rg --files src/Mindflow/Mindflow.Api
```

Nie czytaj masowo `bin/`, `obj/`, `node_modules`, `Migrations` ani calego repo, jesli nie jest to potrzebne. Migracje czytaj tylko wtedy, gdy zmiana dotyczy schematu bazy albo EF snapshotu.

Preferuj:

- `rg` zamiast szerokiego `find`/`grep`
- male zakresy `sed -n 'X,Yp'`
- czytanie interfejsu i implementacji tylko dla dotknietego obszaru
- lokalne, minimalne poprawki zamiast refaktorow calego modulu
- jednoznaczne nazwy i istniejace wzorce zamiast nowych abstrakcji

Gdy zadanie jest male, nie tworz dlugiego planu. Gdy zadanie dotyka kilku warstw, najpierw ustal minimalna sciezke przez:

```text
Controller -> Service -> Repository -> DbContext/Model/DTO
```

Po zmianach raportuj tylko istotne fakty: co zmieniono, co sprawdzono, czy security review przeszedl. Nie wklejaj dlugich diffow, chyba ze Filip o to poprosi.

## Architektura

Kod API jest w:

```text
src/Mindflow/Mindflow.Api/
  Controllers/     - warstwa HTTP i routing
  Data/            - MindflowDbContext i konfiguracja EF
  Exceptions/      - kontrolowane bledy domenowe/API
  Extensions/      - rejestracja DI, DB, auth, storage
  Hubs/            - SignalR
  Middleware/      - globalna obsluga bledow
  Migrations/      - migracje EF Core
  Models/          - encje EF
  Models/Dtos/     - request/response DTO
  Models/Enums/    - enumy zapisywane zwykle jako string
  Repositories/    - dostep do danych
  Services/        - logika aplikacyjna i autoryzacja domenowa
```

Trzymaj odpowiedzialnosci warstw:

- Controller: routing, status HTTP, wywolanie service, bez logiki biznesowej.
- Service: logika biznesowa, ownership checks, walidacja domenowa, orkiestracja repozytoriow.
- Repository: zapytania EF Core i zapis/odczyt danych.
- DTO: kontrakt API; nie wystawiaj encji EF bez potrzeby.
- Extensions: rejestracja zaleznosci i konfiguracja infrastruktury.

Nowe zaleznosci rejestruj w `ServiceCollectionExtensions.cs`.

## Aktualne obszary API

Kontrolery i glowne trasy:

```text
auth
  POST /auth/register
  POST /auth/login
  POST /auth/refresh
  POST /auth/logout

spaces
  GET    /spaces
  POST   /spaces
  PATCH  /spaces/{id}
  DELETE /spaces/{id}

spaces/{spaceId}/projects
  GET    /spaces/{spaceId}/projects
  POST   /spaces/{spaceId}/projects
  PATCH  /spaces/{spaceId}/projects/{id}
  DELETE /spaces/{spaceId}/projects/{id}

projects/{projectId}
  GET    /projects/{projectId}/tasks
  GET    /projects/{projectId}/tags
  POST   /projects/{projectId}/tags
  PUT    /projects/{projectId}/tags/{name}
  DELETE /projects/{projectId}/tags/{name}

tasks
  GET    /tasks
  GET    /tasks/{id}
  POST   /tasks
  PUT    /tasks/{id}
  DELETE /tasks/{id}

calendar/blocks
  GET    /calendar/blocks
  POST   /calendar/blocks
  PUT    /calendar/blocks/{id}
  DELETE /calendar/blocks/{id}

users
  GET /users/me

hubs/tasks
  SignalR hub
```

Wazne: endpoint taskow projektu to `GET /projects/{projectId}/tasks`. Nie zmieniaj go na query param.

## Auth i user context

Domyslna polityka wymaga uwierzytelnionego usera. Kontrolery aplikacyjne powinny miec `[Authorize]`, chyba ze endpoint jest celowo publiczny.

`CurrentUserService` odpowiada za aktualnego uzytkownika. Nie ufaj `userId`, `email`, `spaceId` lub `projectId` z requestu bez sprawdzenia dostepu w service.

Przy dodawaniu endpointu zawsze odpowiedz sobie:

- kto moze go wywolac?
- czy zasob nalezy do aktualnego usera albo jego space?
- czy uzytkownik ma role/uprawnienie do operacji?
- czy odpowiedz nie ujawnia danych innego usera?

## Dane i EF Core

Zmiany schematu rob przez migracje EF Core. Nie edytuj recznie snapshotu bez zrozumienia migracji.

Preferuj LINQ/EF Core. Raw SQL tylko gdy jest uzasadnione i zabezpieczone parametrami.

W encjach uwazaj na:

- `UserId` jako granice izolacji danych
- `SpaceId` i role w `SpaceMember`
- indeksy dla filtrow po `UserId`, `ProjectId`, `TaskId`, datach
- enumy zapisywane jako string
- pola tablicowe PostgreSQL, np. `text[]`
- pola JSONB, np. metadata aktywnosci

## Bezpieczenstwo - obowiazkowy review po zmianach

Po kazdej zmianie kodu wykonaj mini security review przed finalna odpowiedzia. Jesli znajdziesz problem, napraw go od razu albo jasno opisz blokade.

Checklist:

- Auth: czy endpoint wymaga `[Authorize]` albo jest swiadomie publiczny?
- Ownership: czy kazdy odczyt/zapis filtruje po aktualnym userze, space albo uprawnieniu?
- Role: czy operacje mutujace sprawdzaja role tam, gdzie dotycza space/project?
- Input: czy DTO i service waliduja wymagane pola, dlugosci, daty, enumy i puste stringi?
- Output: czy response nie zwraca sekretow, tokenow, hashy ani danych innego usera?
- Tokens: czy access/refresh tokeny nie trafiaja do logow, URL-i poza wymaganym SignalR flow ani response bez potrzeby?
- Secrets: czy nie dodano sekretow do kodu, appsettings, testow ani dokumentacji?
- EF: czy zapytania nie omijaja filtrow usera i nie wprowadzaja SQL injection?
- Errors: czy wyjatki nie ujawniaja stack trace, connection stringow ani sekretow?
- CORS/Auth: czy zmiana nie luzuje CORS, issuer/audience, lifetime albo signing key?
- Storage: czy operacje plikowe sprawdzaja wlasciciela i nie pozwalaja na dowolny path/key injection?
- Realtime: czy eventy SignalR trafiaja tylko do uprawnionych uzytkownikow/grup?
- Migration: czy migracja nie traci danych i ma sensowne defaulty/nullability?

W finalnej odpowiedzi po zmianach dodaj krotka linie:

```text
Security review: passed
```

albo:

```text
Security review: found/fixed ...
```

## Weryfikacja po zmianach

Minimalna weryfikacja backendu:

```bash
dotnet build src/Mindflow/Mindflow.sln
```

Jesli zmiana dotyczy EF/migracji, dodatkowo sprawdz, czy snapshot i migracja sa spojne.

Jesli zmiana dotyczy kontraktu API, sprawdz tez frontendowe oczekiwania w:

```bash
/Users/filipwasiewicz/Projekty/Mindflow/mindflow-ui/src
```

Nie uruchamiaj kosztownych lub dlugich operacji, jesli mala zmiana tego nie wymaga. Gdy nie uruchomisz builda/testow, powiedz to w finalnej odpowiedzi.

## Styl zmian

Trzymaj sie istniejacego stylu C# w repo:

- primary constructors tam, gdzie juz sa uzywane
- async/await dla operacji I/O
- `CancellationToken`, jesli lokalny wzorzec go uzywa w danym obszarze
- kontrolowane wyjatki z `Exceptions/` zamiast losowych statusow w service
- rejestracja interfejs + implementacja dla nowych service/repository
- nazwy DTO w `Models/Dtos`

Nie rob szerokich refaktorow przy okazji malej poprawki. Nie zmieniaj formatowania calego pliku, jesli dotykasz tylko kilku linii.

## Git

Przed zmianami i przed finalna odpowiedzia sprawdz zakres pracy, jesli modyfikowales pliki:

```bash
git status --short --branch
git diff --stat
```

Nie cofaj lokalnych zmian, ktorych nie zrobiles. Jesli trafisz na cudze zmiany w pliku, przeczytaj je i pracuj z nimi, nie nadpisuj ich bez zgody.
