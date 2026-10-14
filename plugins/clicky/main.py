import json
import os
import sys
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")
if hasattr(sys.stdin, "reconfigure"):
    sys.stdin.reconfigure(encoding="utf-8")

APPDATA = Path(os.environ.get("APPDATA", "")) / "Clicky"
SETTINGS = APPDATA / "settings.json"
LOGS = APPDATA / "logs" / "clicky.log"


def send(message):
    sys.stdout.write(json.dumps(message, ensure_ascii=False) + "\n")
    sys.stdout.flush()


def read_settings():
    if not SETTINGS.exists():
        return {}
    try:
        return json.loads(SETTINGS.read_text(encoding="utf-8"))
    except Exception:
        return {}


def recent_logs(limit=4):
    if not LOGS.exists():
        return []
    try:
        lines = LOGS.read_text(encoding="utf-8", errors="ignore").splitlines()
        results = []
        for line in reversed(lines):
            line = line.strip()
            if not line:
                continue
            parts = line.split(maxsplit=3)
            time_str = parts[0] if len(parts) > 0 else ""
            msg = parts[3] if len(parts) > 3 else line
            results.append({"time": time_str, "text": msg})
            if len(results) >= limit:
                break
        return results
    except Exception:
        return []


def send_to_clicky(action, query=""):
    try:
        import ctypes
        from ctypes import wintypes
        kernel32 = ctypes.windll.kernel32
        CreateFileW = kernel32.CreateFileW
        CreateFileW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, wintypes.LPVOID, wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE]
        CreateFileW.restype = wintypes.HANDLE

        hPipe = CreateFileW(r"\\.\pipe\clicky", 0x40000000, 0, None, 3, 0, None)
        if hPipe != -1 and hPipe != 0xFFFFFFFFFFFFFFFF:
            msg = (json.dumps({"action": action, "query": query}) + "\n").encode("utf-8")
            written = wintypes.DWORD()
            kernel32.WriteFile(hPipe, msg, len(msg), ctypes.byref(written), None)
            kernel32.CloseHandle(hPipe)
            return
    except Exception:
        pass

    try:
        import ctypes
        from ctypes import wintypes
        kernel32 = ctypes.windll.kernel32
        CreateFileW = kernel32.CreateFileW
        CreateFileW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, wintypes.LPVOID, wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE]
        CreateFileW.restype = wintypes.HANDLE

        hPipe = CreateFileW(r"\\.\pipe\popup", 0x40000000, 0, None, 3, 0, None)
        if hPipe != -1 and hPipe != 0xFFFFFFFFFFFFFFFF:
            msg = (json.dumps({"type": "action", "source": "popup", "id": action}) + "\n").encode("utf-8")
            written = wintypes.DWORD()
            kernel32.WriteFile(hPipe, msg, len(msg), ctypes.byref(written), None)
            kernel32.CloseHandle(hPipe)
    except Exception:
        pass



def render():
    cfg = read_settings()
    model = cfg.get("aiModel", "gpt-6-luna")
    hotkey = cfg.get("triggerHotkey", "ctrl+alt+a")
    logs = recent_logs(4)

    nodes = [
        {
            "type": "row",
            "children": [
                {
                    "type": "text",
                    "text": f"Clicky ({model})",
                    "bold": True,
                    "muted": False,
                },
                {
                    "type": "button",
                    "id": "trigger",
                    "label": f"⚡ Позвать ({hotkey})",
                    "value": "trigger",
                },
                {
                    "type": "button",
                    "id": "test",
                    "label": "🎯 Тест",
                    "value": "test",
                },
                {
                    "type": "button",
                    "id": "dismiss",
                    "label": "❌ Скрыть",
                    "value": "dismiss",
                },
            ],
        },
        {
            "type": "input",
            "id": "ask_input",
            "value": "",
            "placeholder": "Задай вопрос об активном окне (Enter для отправки)...",
            "action": "ask",
        },
    ]

    items = []
    for idx, item in enumerate(logs):
        items.append({
            "id": str(idx),
            "label": item["time"],
            "text": item["text"],
            "value": item["text"],
            "action": "copy",
        })

    if items:
        nodes.append({"type": "list", "items": items})
    else:
        nodes.append({
            "type": "text",
            "text": "Нажми 'Позвать' или скажи в микрофон 'Клики, покажи...'",
            "muted": True,
            "bold": False,
        })

    send({"type": "ui", "nodes": nodes})


def main():
    render()

    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        try:
            msg = json.loads(line)
        except Exception:
            continue

        msg_type = msg.get("type")
        if msg_type == "render":
            render()
        elif msg_type == "action":
            action_id = msg.get("id")
            val = msg.get("value", "")
            if action_id == "trigger":
                send_to_clicky("trigger")
            elif action_id == "test":
                send_to_clicky("test")
            elif action_id == "dismiss":
                send_to_clicky("dismiss")
            elif action_id == "settings":
                send_to_clicky("settings")
            elif action_id == "ask":
                send_to_clicky("trigger", val)
            render()
        elif msg_type == "shutdown":
            break


if __name__ == "__main__":
    main()
