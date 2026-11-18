"""Peach SDK for Python mods.  `import peach`"""
import json
import os
import time
import urllib.error
import urllib.request

_API = os.environ.get("PEACH_API")
_TOKEN = os.environ.get("PEACH_TOKEN")
mod = os.environ.get("PEACH_MOD")
sleep = time.sleep


def call(method, **params):
    if not _API:
        raise RuntimeError("Not running under Peach (PEACH_API is not set)")
    req = urllib.request.Request(
        f"{_API}/{method}",
        data=json.dumps(params).encode("utf-8"),
        headers={"Content-Type": "application/json", "Authorization": f"Bearer {_TOKEN}"},
        method="POST",
    )
    try:
        with urllib.request.urlopen(req) as res:
            body = json.load(res)
    except urllib.error.HTTPError as e:
        body = json.load(e)
    if not body.get("ok"):
        raise RuntimeError(body.get("error", "unknown error"))
    return body["result"]


def log(*args):
    return call("log", message=" ".join(str(a) for a in args))


def notify(title, message):
    return call("notify", title=title, message=message)


def system_info():
    return call("system.info")


def storage_get(key=None):
    return call("storage.get", key=key) if key is not None else call("storage.get")


def storage_set(key, value):
    return call("storage.set", key=key, value=value)


def clipboard_get():
    return call("clipboard.get")


def clipboard_set(text):
    return call("clipboard.set", text=text)


def windows_list():
    return call("windows.list")


def registry_get(path, name):
    return call("registry.get", path=path, name=name)


def wallpaper_set(path):
    return call("wallpaper.set", path=path)


def shell_run(command, args=(), timeout_ms=None):
    return call("shell.run", command=command, args=list(args), timeoutMs=timeout_ms)


def shell_open(target):
    return call("shell.open", target=target)


# ---- hotkeys & events -------------------------------------------------------
# peach.hotkey("ctrl+alt+u", handler); peach.on("focus", handler); then peach.run()
_handlers = {}


def on(event_type, fn):
    _handlers.setdefault(event_type, []).append(fn)
    if event_type in ("focus", "clipboard"):
        call("events.subscribe", events=[event_type])


def hotkey(combo, fn):
    combo = call("hotkey.register", combo=combo)["combo"]
    on("hotkey", lambda ev: fn(ev) if ev.get("combo") == combo else None)
    return combo


def hotkey_unregister(combo):
    return call("hotkey.unregister", combo=combo)


def events_poll(timeout_ms=25000):
    return call("events.poll", timeoutMs=timeout_ms)


def run():
    """Block and dispatch events to the registered handlers."""
    import traceback
    while True:
        try:
            events = events_poll()
        except (urllib.error.URLError, ConnectionError):
            raise SystemExit(1)  # Peach is gone
        except RuntimeError as e:
            if "invalid token" in str(e):
                raise SystemExit(1)
            time.sleep(1)
            continue
        for ev in events:
            for fn in _handlers.get(ev.get("type"), []):
                try:
                    fn(ev)
                except Exception:
                    traceback.print_exc()
