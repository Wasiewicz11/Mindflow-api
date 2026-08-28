#!/usr/bin/env python3
"""Minimal client for the Mindflow integration API. Standard library only."""

import argparse
import json
import os
import ssl
import stat
import sys
import urllib.error
import urllib.parse
import urllib.request

DEFAULT_API_URL = "https://mindflow-api-0506.onrender.com"
CONFIG_PATH = os.path.expanduser("~/.config/mindflow/config.json")
PRIORITIES = ("P1", "P2", "P3", "P4")
STATUSES = ("NotStarted", "InProgress", "Completed")


class MindflowError(Exception):
    pass


_SSL_CONTEXT = None

# python.org builds on macOS ship without a CA bundle, so the default store can be empty.
CA_BUNDLE_CANDIDATES = (
    "/etc/ssl/cert.pem",
    "/opt/homebrew/etc/ca-certificates/cert.pem",
    "/usr/local/etc/ca-certificates/cert.pem",
    "/etc/ssl/certs/ca-certificates.crt",
    "/etc/pki/tls/certs/ca-bundle.crt",
)


def has_certificates(context):
    return context.cert_store_stats().get("x509_ca", 0) > 0


def ssl_context():
    """A verifying context, falling back to any CA bundle present on the machine."""
    global _SSL_CONTEXT
    if _SSL_CONTEXT is not None:
        return _SSL_CONTEXT

    override = os.environ.get("MINDFLOW_CA_BUNDLE")
    if override:
        if not os.path.exists(override):
            raise MindflowError(f"MINDFLOW_CA_BUNDLE points at a file that does not exist: {override}")
        _SSL_CONTEXT = ssl.create_default_context(cafile=override)
        return _SSL_CONTEXT

    context = ssl.create_default_context()
    if has_certificates(context):
        _SSL_CONTEXT = context
        return _SSL_CONTEXT

    try:
        import certifi

        candidate = ssl.create_default_context(cafile=certifi.where())
        if has_certificates(candidate):
            _SSL_CONTEXT = candidate
            return _SSL_CONTEXT
    except Exception:
        pass

    for path in CA_BUNDLE_CANDIDATES:
        if not os.path.exists(path):
            continue
        candidate = ssl.create_default_context(cafile=path)
        if has_certificates(candidate):
            _SSL_CONTEXT = candidate
            return _SSL_CONTEXT

    raise MindflowError(
        "This Python has no CA certificates, so HTTPS cannot be verified.\n"
        "Fix it once by running the installer that ships with Python:\n"
        '  open "/Applications/Python 3.12/Install Certificates.command"\n'
        "or point the client at a bundle you already have:\n"
        "  export MINDFLOW_CA_BUNDLE=/etc/ssl/cert.pem"
    )


def load_config():
    if not os.path.exists(CONFIG_PATH):
        raise MindflowError(
            "No API key configured yet. Run: mindflow.py configure --key <mf_...>\n"
            "The user creates a key in Mindflow under Settings -> Integrations -> New key."
        )
    with open(CONFIG_PATH, encoding="utf-8") as handle:
        config = json.load(handle)
    if not config.get("key"):
        raise MindflowError("Config file has no key. Re-run: mindflow.py configure --key <mf_...>")
    return config


def save_config(key, api_url):
    os.makedirs(os.path.dirname(CONFIG_PATH), exist_ok=True)
    with open(CONFIG_PATH, "w", encoding="utf-8") as handle:
        json.dump({"key": key, "apiUrl": api_url.rstrip("/")}, handle, indent=2)
    os.chmod(CONFIG_PATH, stat.S_IRUSR | stat.S_IWUSR)


def request(method, path, payload=None):
    config = load_config()
    url = f"{config.get('apiUrl', DEFAULT_API_URL)}/api/integration/v1{path}"
    data = json.dumps(payload).encode("utf-8") if payload is not None else None
    req = urllib.request.Request(url, data=data, method=method)
    req.add_header("Authorization", f"Bearer {config['key']}")
    req.add_header("Accept", "application/json")
    if data is not None:
        req.add_header("Content-Type", "application/json")

    context = ssl_context() if url.lower().startswith("https:") else None

    try:
        with urllib.request.urlopen(req, timeout=60, context=context) as response:
            body = response.read().decode("utf-8")
            return json.loads(body) if body.strip() else None
    except urllib.error.HTTPError as error:
        raise MindflowError(explain_http_error(error)) from error
    except urllib.error.URLError as error:
        raise MindflowError(f"Could not reach the Mindflow API: {error.reason}") from error


def explain_http_error(error):
    detail = ""
    try:
        detail = error.read().decode("utf-8").strip()
    except Exception:
        pass

    if error.code == 401:
        return "Key rejected (401). It is invalid, revoked, expired, or API access is switched off in Mindflow settings."
    if error.code == 403:
        return "Key lacks the required permission (403). Create a key with the missing scope."
    if error.code == 404:
        return "Not found (404). The task or project does not exist, or this key's account cannot see it."
    if error.code == 429:
        retry = error.headers.get("Retry-After")
        return f"Rate limited (429). Retry after {retry or 'a few'} seconds."
    if error.code == 503:
        return "Integration API is unavailable (503). The server is missing IntegrationTokens:HashPepper."
    return f"API error {error.code}. {detail}"


def page_items(result):
    return result.get("items", []) if isinstance(result, dict) else (result or [])


def cmd_configure(args):
    key = args.key.strip()
    if not key.startswith("mf_"):
        raise MindflowError("That does not look like a Mindflow key (expected it to start with 'mf_').")
    save_config(key, args.api_url)
    print(f"Key saved to {CONFIG_PATH} (permissions 600).")
    try:
        docs = request("GET", "/docs")
        scopes = ", ".join(scope["scope"] for scope in docs.get("scopes", []))
        print("Connection verified.")
        print(f"Scopes advertised by the API: {scopes}")
    except MindflowError as error:
        print(f"Saved, but the check failed: {error}", file=sys.stderr)
        return 1
    return 0


def cmd_status(args):
    request("GET", "/docs")
    projects = page_items(request("GET", "/projects?limit=200"))
    tasks = request("GET", "/tasks?limit=1")
    print("Connected.")
    print(f"Projects: {len(projects)}")
    print(f"Tasks: {tasks.get('total', 0)}")
    return 0


def cmd_projects(args):
    result = request("GET", "/projects?limit=200")
    projects = page_items(result)
    if args.json:
        print(json.dumps(projects, ensure_ascii=False, indent=2))
        return 0
    if not projects:
        print("No projects.")
        return 0
    for project in projects:
        print(f"{project['id']}  {project['name']}")
    return 0


def cmd_tasks(args):
    query = {"limit": args.limit, "offset": args.offset}
    if args.project:
        query["projectId"] = args.project
    if args.status:
        query["status"] = args.status
    if args.open:
        query["isCompleted"] = "false"
    if args.due_before:
        query["dueBefore"] = args.due_before

    result = request("GET", "/tasks?" + urllib.parse.urlencode(query))
    if args.json:
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return 0

    items = page_items(result)
    if not items:
        print("No tasks match.")
        return 0
    for task in items:
        due = f" due {task['dueDate']}" if task.get("dueDate") else ""
        mark = "x" if task.get("isCompleted") else " "
        print(f"[{mark}] {task['id']}  {task['priority']}  {task['content']}{due}")
    print(f"\n{len(items)} of {result.get('total', len(items))}")
    return 0


def build_task_payload(spec):
    """Accepts a dict describing one task and returns the API payload."""
    content = (spec.get("content") or "").strip()
    if not content:
        raise MindflowError("Every task needs non-empty 'content'.")
    if len(content) > 1000:
        raise MindflowError(f"Task title is longer than 1000 characters: {content[:60]}...")

    payload = {"content": content}
    for key in ("description", "projectId", "dueDate"):
        if spec.get(key):
            payload[key] = spec[key]

    if spec.get("priority"):
        if spec["priority"] not in PRIORITIES:
            raise MindflowError(f"priority must be one of {', '.join(PRIORITIES)}")
        payload["priority"] = spec["priority"]

    if spec.get("status"):
        if spec["status"] not in STATUSES:
            raise MindflowError(f"status must be one of {', '.join(STATUSES)}")
        payload["status"] = spec["status"]

    if spec.get("estimatedHours") is not None:
        payload["estimatedHours"] = float(spec["estimatedHours"])

    if spec.get("tags"):
        payload["tags"] = list(spec["tags"])

    if spec.get("subtasks"):
        payload["subtasks"] = [
            {"id": None, "content": item, "isCompleted": False, "description": None, "dueDate": None, "sortOrder": None}
            for item in spec["subtasks"]
        ]

    return payload


def cmd_add(args):
    payload = build_task_payload({
        "content": args.content,
        "description": args.description,
        "projectId": args.project,
        "priority": args.priority,
        "status": args.status,
        "dueDate": args.due,
        "estimatedHours": args.estimate,
        "tags": args.tag,
        "subtasks": args.subtask,
    })
    created = request("POST", "/tasks", payload)
    print(f"Added: {created['id']}  {created['content']}")
    return 0


def cmd_add_batch(args):
    raw = sys.stdin.read()
    try:
        specs = json.loads(raw)
    except json.JSONDecodeError as error:
        raise MindflowError(f"stdin is not valid JSON: {error}") from error
    if not isinstance(specs, list):
        raise MindflowError("Expected a JSON array of task objects on stdin.")

    payloads = [build_task_payload(spec) for spec in specs]

    created, failed = [], []
    for payload in payloads:
        try:
            created.append(request("POST", "/tasks", payload))
        except MindflowError as error:
            failed.append((payload["content"], str(error)))

    for task in created:
        print(f"Added: {task['id']}  {task['content']}")
    for content, reason in failed:
        print(f"FAILED: {content} -- {reason}", file=sys.stderr)

    print(f"\n{len(created)} added, {len(failed)} failed.")
    return 1 if failed else 0


def cmd_update(args):
    payload = {}
    if args.content:
        payload["content"] = args.content
    if args.description:
        payload["description"] = args.description
    if args.priority:
        payload["priority"] = args.priority
    if args.status:
        payload["status"] = args.status
    if args.due:
        payload["dueDate"] = args.due
    if args.clear_due:
        payload["clearDueDate"] = True
    if args.project:
        payload["projectId"] = args.project
    if args.estimate is not None:
        payload["estimatedHours"] = args.estimate
    if args.clear_estimate:
        payload["clearEstimatedHours"] = True
    if not payload:
        raise MindflowError("Nothing to update.")

    updated = request("PATCH", f"/tasks/{args.id}", payload)
    print(f"Updated: {updated['id']}  {updated['content']}")
    return 0


def resolve_minutes(minutes, hours):
    """The API stores whole minutes; hours are a convenience for dictated input."""
    if minutes is not None and hours is not None:
        raise MindflowError("Give either --minutes or --hours, not both.")
    if hours is not None:
        minutes = int(round(hours * 60))
    if minutes is None:
        return None
    if not 1 <= minutes <= 1440:
        raise MindflowError("Work time must be between 1 minute and 24 hours (1440 minutes).")
    return minutes


def cmd_time(args):
    result = request("GET", f"/tasks/{args.id}/time-entries?" + urllib.parse.urlencode({"limit": args.limit}))
    if args.json:
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return 0

    items = page_items(result)
    if not items:
        print("No time logged on this task.")
        return 0

    total = 0
    for entry in items:
        minutes = entry.get("durationMinutes") or 0
        total += minutes
        notes = f"  {entry['notes']}" if entry.get("notes") else ""
        print(f"{entry['id']}  {entry.get('workDate', '?')}  {format_minutes(minutes)}{notes}")
    print(f"\nTotal: {format_minutes(total)} across {len(items)} entries")
    return 0


def cmd_log_time(args):
    minutes = resolve_minutes(args.minutes, args.hours)

    if (args.start is None) != (args.end is None):
        raise MindflowError("Give both --start and --end, or neither.")
    if minutes is None and args.start is None:
        raise MindflowError("Provide the time worked: --minutes, --hours, or --start with --end.")

    payload = {"clearEstimatedHours": False}
    if minutes is not None:
        payload["durationMinutes"] = minutes
    if args.date:
        payload["workDate"] = args.date
    if args.start:
        payload["startAt"] = args.start
        payload["endAt"] = args.end
    if args.notes:
        payload["notes"] = args.notes
    if args.estimate is not None:
        payload["estimatedHours"] = args.estimate

    result = request("POST", f"/tasks/{args.id}/time-entries", payload)
    entry = result.get("timeEntry", result) if isinstance(result, dict) else result
    print(f"Logged {format_minutes(entry.get('durationMinutes') or 0)} on {entry.get('workDate', 'today')}")
    return 0


def cmd_delete_time(args):
    request("DELETE", f"/time-entries/{args.id}")
    print(f"Deleted time entry {args.id}")
    return 0


def format_minutes(minutes):
    hours, rest = divmod(int(minutes), 60)
    if hours and rest:
        return f"{hours}h {rest}m"
    if hours:
        return f"{hours}h"
    return f"{rest}m"


def cmd_complete(args):
    updated = request("PATCH", f"/tasks/{args.id}", {"status": "Completed"})
    print(f"Completed: {updated['content']}")
    return 0


def cmd_delete(args):
    request("DELETE", f"/tasks/{args.id}")
    print(f"Deleted {args.id}")
    return 0


def main():
    parser = argparse.ArgumentParser(description="Mindflow integration API client")
    sub = parser.add_subparsers(dest="command", required=True)

    p = sub.add_parser("configure", help="store the API key")
    p.add_argument("--key", required=True)
    p.add_argument("--api-url", default=DEFAULT_API_URL)
    p.set_defaults(func=cmd_configure)

    p = sub.add_parser("status", help="verify the connection")
    p.set_defaults(func=cmd_status)

    p = sub.add_parser("projects", help="list projects")
    p.add_argument("--json", action="store_true")
    p.set_defaults(func=cmd_projects)

    p = sub.add_parser("tasks", help="list tasks")
    p.add_argument("--project")
    p.add_argument("--status", choices=STATUSES)
    p.add_argument("--open", action="store_true", help="only tasks that are not completed")
    p.add_argument("--due-before")
    p.add_argument("--limit", type=int, default=50)
    p.add_argument("--offset", type=int, default=0)
    p.add_argument("--json", action="store_true")
    p.set_defaults(func=cmd_tasks)

    p = sub.add_parser("add", help="add one task")
    p.add_argument("content")
    p.add_argument("--description")
    p.add_argument("--project")
    p.add_argument("--priority", choices=PRIORITIES)
    p.add_argument("--status", choices=STATUSES)
    p.add_argument("--due", help="YYYY-MM-DD")
    p.add_argument("--estimate", type=float, help="hours")
    p.add_argument("--tag", action="append")
    p.add_argument("--subtask", action="append")
    p.set_defaults(func=cmd_add)

    p = sub.add_parser("add-batch", help="add many tasks from a JSON array on stdin")
    p.set_defaults(func=cmd_add_batch)

    p = sub.add_parser("update", help="update a task")
    p.add_argument("id")
    p.add_argument("--content")
    p.add_argument("--description")
    p.add_argument("--priority", choices=PRIORITIES)
    p.add_argument("--status", choices=STATUSES)
    p.add_argument("--due")
    p.add_argument("--clear-due", action="store_true")
    p.add_argument("--project")
    p.add_argument("--estimate", type=float, help="hours")
    p.add_argument("--clear-estimate", action="store_true")
    p.set_defaults(func=cmd_update)

    p = sub.add_parser("time", help="list time logged on a task")
    p.add_argument("id")
    p.add_argument("--limit", type=int, default=50)
    p.add_argument("--json", action="store_true")
    p.set_defaults(func=cmd_time)

    p = sub.add_parser("log-time", help="log work time on a task")
    p.add_argument("id")
    p.add_argument("--minutes", type=int)
    p.add_argument("--hours", type=float)
    p.add_argument("--date", help="YYYY-MM-DD, defaults to today")
    p.add_argument("--start", help="ISO timestamp, use with --end")
    p.add_argument("--end", help="ISO timestamp, use with --start")
    p.add_argument("--notes")
    p.add_argument("--estimate", type=float, help="also set the task estimate, in hours")
    p.set_defaults(func=cmd_log_time)

    p = sub.add_parser("delete-time", help="delete a time entry")
    p.add_argument("id")
    p.set_defaults(func=cmd_delete_time)

    p = sub.add_parser("complete", help="mark a task completed")
    p.add_argument("id")
    p.set_defaults(func=cmd_complete)

    p = sub.add_parser("delete", help="delete a task")
    p.add_argument("id")
    p.set_defaults(func=cmd_delete)

    args = parser.parse_args()
    try:
        return args.func(args) or 0
    except MindflowError as error:
        print(f"Error: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
