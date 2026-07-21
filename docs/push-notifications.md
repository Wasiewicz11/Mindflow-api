# Powiadomienia Web Push

Ta funkcja wysyla:

- poranny brief o wybranej godzinie (domyslnie 06:00), z liczba zadan na dzis w projektach oraz konkretnymi zaleglymi zadaniami;
- drugi brief (domyslnie 13:00);
- przypomnienie przed blokiem w kalendarzu (domyslnie 10 minut);
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

Migracja `AddPushNotifications` tworzy tabele `notification_settings`, `push_notification_subscriptions` i `push_notification_deliveries`. Kolejna migracja `AddPushNotificationDeviceName` dodaje nazwę urządzenia do listy subskrypcji, a `AddNotificationInbox` tworzy centrum zapisanych porannych i popołudniowych briefów oraz podsumowań dnia. Zawsze zastosuj wszystkie oczekujące migracje tą samą komendą.

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

## Reczne sprawdzenie joba

Przed konfiguracja Cloudflare mozna wywolac job recznie:

```bash
curl -X POST 'https://twoj-backend.onrender.com/internal/jobs/notifications' \
  -H 'X-Job-Key: LOSOWY_DLUGI_SEKRET'
```

Odpowiedz zawiera liczbe wyslanych briefow, przypomnien o blokach i podsumowan wieczornych.
