---
name: mindflow
description: Add, list, update or complete tasks in the user's Mindflow app, and log work time against them. Use whenever the user asks to put something into Mindflow ("dodaj to do mindflow", "wpisz te zadania do mindflow", "add this to my tasks", "wrzuć to na listę"), to record time spent ("zapisz 2h na to zadanie", "log 90 minutes"), to set an estimate, or asks what is on their Mindflow task list. Talks to the Mindflow integration API with a scoped key the user configures once.
---

# Mindflow

Turn things discussed in the conversation into real tasks in the user's Mindflow account.

All calls go through the bundled client:

```bash
python3 .claude/skills/mindflow/mindflow.py <command>
```

It uses only the Python standard library — no install step.

## First run: the key

The key is stored once at `~/.config/mindflow/config.json` (permissions 600) and reused afterwards.

If any command reports that no key is configured, ask the user for one and tell them where to get it:
**Mindflow → Ustawienia → Integracje → Nowy klucz**. Scopes the key needs:
`tasks:create` to add, `tasks:read` to list, `tasks:update` to edit or set estimates,
`projects:read` to match a project, and `time_entries:read` / `time_entries:create` for work time.

```bash
python3 .claude/skills/mindflow/mindflow.py configure --key mf_xxxxxxxx
```

The key is a secret. Never echo it back, never write it into a repo, never put it in a commit.
Once `configure` succeeds it verifies the connection and prints the granted scopes.

## Adding tasks — the main flow

When the user says something like "dodaj to do mindflow", do this without asking unnecessary questions:

1. Work out the task list from the conversation. Split a discussion into separate, actionable
   tasks — one concrete outcome per task. Keep titles short (max 1000 chars, ideally under ~80).
   Write them in the language the user used.
2. If the tasks clearly belong to a project the user mentioned, run `projects` and match by name.
   Otherwise leave the project out — the task lands in the inbox. Do not invent a project id.
3. Add them.

One task:

```bash
python3 .claude/skills/mindflow/mindflow.py add "Poprawić walidację formularza" \
  --priority P2 --due 2026-09-01 --project <project-id>
```

Several at once — preferred when the user dictates a list, because it is one call per task with a
single summary at the end:

```bash
echo '[
  {"content": "Zaprojektować ekran logowania", "priority": "P2"},
  {"content": "Podpiąć API", "projectId": "<id>", "dueDate": "2026-09-05"}
]' | python3 .claude/skills/mindflow/mindflow.py add-batch
```

Fields per task: `content` (required), `description`, `projectId`, `priority` (`P1`–`P4`,
P1 = most urgent), `status` (`NotStarted`, `InProgress`, `Completed`), `dueDate` (`YYYY-MM-DD`),
`estimatedHours` (number), `tags` (array of strings), `subtasks`.

A subtask is either a plain title or an object with its own `content`, `estimatedHours`,
`dueDate`, `description` or `status`:

```bash
echo '[{"content": "Migracja", "subtasks": [
  "Backup bazy",
  {"content": "Przepisać widoki", "estimatedHours": 3, "dueDate": "2026-09-05"}
]}]' | python3 .claude/skills/mindflow/mindflow.py add-batch
```

Then tell the user what landed there — count and titles, not raw ids.

## Reading and changing tasks

```bash
python3 .claude/skills/mindflow/mindflow.py tasks --open --limit 20
python3 .claude/skills/mindflow/mindflow.py tasks --project <id> --status InProgress
python3 .claude/skills/mindflow/mindflow.py projects
python3 .claude/skills/mindflow/mindflow.py update <task-id> --priority P1 --due 2026-09-10
python3 .claude/skills/mindflow/mindflow.py complete <task-id>
python3 .claude/skills/mindflow/mindflow.py delete <task-id>
python3 .claude/skills/mindflow/mindflow.py status
```

Add `--json` to `tasks` or `projects` when you need to process the output rather than show it.

## Subtasks

```bash
python3 .claude/skills/mindflow/mindflow.py subtasks <task-id>
python3 .claude/skills/mindflow/mindflow.py add-subtask <task-id> "Przepisać widoki" --estimate 3
python3 .claude/skills/mindflow/mindflow.py update-subtask <task-id> <subtask-id> --estimate 2
python3 .claude/skills/mindflow/mindflow.py update-subtask <task-id> <subtask-id> --done
```

Break a task into subtasks when the user describes steps within one piece of work. Keep a task
flat when the items are independent — separate tasks are easier to schedule than subtasks.

## Work time and estimates

An **estimate** is how long something should take and lives on the task or subtask itself.
A **time entry** is work actually done. They are separate — do not use one for the other.

A task and its subtasks keep **independent** estimates. The API also reports the sum of the
subtask estimates separately (`subtasksEstimatedHours`), so "the whole thing is 2h" and a
breakdown adding up to 3h can coexist. Never overwrite one to make them match — if the user
points at the gap, show both numbers and ask which one is right.

Set or change an estimate:

```bash
python3 .claude/skills/mindflow/mindflow.py add "Migracja bazy" --estimate 3
python3 .claude/skills/mindflow/mindflow.py update <task-id> --estimate 1.5
python3 .claude/skills/mindflow/mindflow.py update <task-id> --clear-estimate
```

Estimates on subtasks:

```bash
python3 .claude/skills/mindflow/mindflow.py add-subtask <task-id> "Testy" --estimate 1.5
python3 .claude/skills/mindflow/mindflow.py update-subtask <task-id> <subtask-id> --clear-estimate
```

Log work that was done — add `--subtask` to attribute it to one step rather than the whole task:

```bash
python3 .claude/skills/mindflow/mindflow.py log-time <task-id> --subtask <subtask-id> --hours 1
python3 .claude/skills/mindflow/mindflow.py log-time <task-id> --hours 2 --notes "Debug importu"
python3 .claude/skills/mindflow/mindflow.py log-time <task-id> --minutes 45 --date 2026-08-27
python3 .claude/skills/mindflow/mindflow.py log-time <task-id> --start 2026-08-28T09:00:00Z --end 2026-08-28T10:30:00Z
python3 .claude/skills/mindflow/mindflow.py time <task-id>
python3 .claude/skills/mindflow/mindflow.py delete-time <entry-id>
```

Rules the API enforces, so get them right before calling:

- One entry is between **1 minute and 24 hours**. Longer work spans several entries, one per day.
- `--start` and `--end` go together, `--end` must be later, and the duration is derived from them —
  do not also pass `--minutes`.
- Without `--date` the entry lands on today. If the user says "wczoraj", compute the real date.
- `--hours` accepts fractions (`1.5` = 1h 30m); it is a convenience, the API stores whole minutes.

When the user says something like "zapisz 2h na to zadanie", find the task first (`tasks --open`),
confirm which one you matched if it is ambiguous, then log the time.

## Judgement calls

- **Ask before deleting.** `delete` and `delete-time` are permanent. Completing a task is almost
  always what the user means by "zrobione" — prefer `complete`.
- **Never invent logged time.** Only record hours the user actually stated. If they said "trochę
  nad tym siedziałem", ask how long instead of guessing.
- **Do not add duplicates.** If the user might be repeating themselves, list open tasks first and
  say what already exists instead of adding a second copy.
- **Do not silently reinterpret.** If a request is one vague sentence ("ogarnij projekt"), ask what
  the concrete tasks are rather than inventing a breakdown.
- **Deadlines only when stated.** Do not guess a `dueDate` the user never gave. If they said
  "na jutro", compute the real date first.

## When something fails

The client returns a plain-language reason. What each one means:

- **401** — the key is invalid, revoked, expired, or the user switched API access off. Ask them to
  check Ustawienia → Integracje, and reconfigure if they generated a new key.
- **403** — the key is missing that permission. Name the scope they need and ask for a new key.
- **429** — rate limited; the message says how long to wait. Wait and retry once.
- **503** — the server is missing its `IntegrationTokens:HashPepper` secret. Nothing the user can
  fix from the app; it is a deployment setting.

- **SSL: CERTIFICATE_VERIFY_FAILED** — that Python has no CA bundle (common with python.org builds
  on macOS). The client already falls back to a system bundle; if it still fails, tell the user to
  run `open "/Applications/Python 3.12/Install Certificates.command"` once, or to set
  `MINDFLOW_CA_BUNDLE=/etc/ssl/cert.pem`. Never work around it by disabling verification.

Report failures honestly. If a batch partly failed, say which tasks did not make it and why —
never claim everything was added.
