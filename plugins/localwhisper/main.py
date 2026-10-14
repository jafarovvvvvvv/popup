"""Плагин Popup: последние расшифровки из локального Whisper.

Заодно это образец, с которого удобно начинать свою интеграцию. Весь протокол
целиком — читать строки JSON из stdin, писать строки JSON в stdout:

    Popup -> плагин   {"type": "render"}
                      {"type": "action", "id": "...", "value": "..."}
                      {"type": "shutdown"}

    плагин -> Popup   {"type": "ui", "nodes": [...]}
                      {"type": "log", "message": "..."}

Узлы, которые Popup умеет рисовать: text, row, list, input, button, image,
progress. Действия "copy" и "open" панель выполняет сама, не будя процесс;
любое другое имя приезжает сюда как action.
"""

import json
import os
import sys
from pathlib import Path

HOME = Path(os.path.expanduser("~")) / ".localwhisper"
CONFIG = HOME / "config.json"
HISTORY = HOME / "history.json"

# Сколько последних расшифровок показывать. Панель высотой в 208 точек, и
# длинный список в ней всё равно не прочитать — для этого есть само приложение.
LIMIT = 8


def send(message):
    """Одно сообщение — одна строка. Без flush Popup не увидит её до выхода."""
    sys.stdout.write(json.dumps(message, ensure_ascii=False) + "\n")
    sys.stdout.flush()


def read_json(path, fallback):
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except Exception:
        return fallback


# Ключи текста в history.json, в порядке предпочтения. У localwhisper запись
# выглядит как {"time", "mode", "raw", "clean"}: "clean" — то, что прошло через
# super mode, "raw" — что услышала модель. Остальные имена оставлены на случай,
# если формат ещё поменяется: плагин от этого не должен разваливаться.
TEXT_KEYS = ("clean", "raw", "text", "result", "transcript")
TIME_KEYS = ("time", "timestamp", "at")


def entries():
    """Последние расшифровки. В файле они лежат от старых к новым."""
    data = read_json(HISTORY, [])
    if isinstance(data, dict):
        data = data.get("items") or data.get("history") or []
    if not isinstance(data, list):
        return []

    result = []
    for item in reversed(data):
        if isinstance(item, str):
            result.append({"text": item, "at": ""})
        elif isinstance(item, dict):
            text = next((item[k] for k in TEXT_KEYS if item.get(k)), "")
            if not text:
                continue
            at = next((str(item[k]) for k in TIME_KEYS if item.get(k)), "")
            result.append({"text": text, "at": at})
        else:
            continue
        if len(result) >= LIMIT:
            break
    return result


def one_line(text, width=70):
    flat = " ".join(str(text).split())
    return flat if len(flat) <= width else flat[: width - 1] + "…"


def ui():
    config = read_json(CONFIG, {})
    model = config.get("whisper_model", "?")
    device = config.get("whisper_device", "?")

    nodes = [
        {"type": "text", "text": f"{model} · {device}", "muted": True},
    ]

    items = entries()
    if not items:
        nodes.append({"type": "text", "text": "Расшифровок пока нет", "muted": True})
    else:
        nodes.append({
            "type": "list",
            "items": [
                {
                    "id": str(i),
                    # Клик по строке кладёт расшифровку в буфер. Это делает сама
                    # панель — процесс для такого будить незачем.
                    "label": one_line(item["text"]),
                    "text": item["at"],
                    "value": item["text"],
                    "action": "copy",
                }
                for i, item in enumerate(items)
            ],
        })

    nodes.append({
        "type": "row",
        "children": [
            {"type": "button", "id": "open-folder", "label": "Открыть папку"},
            {"type": "button", "id": "open-log", "label": "Журнал"},
        ],
    })
    return {"type": "ui", "nodes": nodes}


def act(message):
    action = message.get("id") or ""
    if action == "open-folder":
        # Ответить действием на действие: панель выполнит "open" сама.
        send({"type": "ui", "nodes": ui()["nodes"]})
        send({"type": "log", "message": f"открываю {HOME}"})
        os.startfile(str(HOME))  # noqa: S606 — своя папка на своей машине
    elif action == "open-log":
        log = HOME / "app.log"
        if log.exists():
            os.startfile(str(log))  # noqa: S606
        else:
            send({"type": "log", "message": "app.log не найден"})


def main():
    send({"type": "log", "message": f"localwhisper: слежу за {HOME}"})
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        try:
            message = json.loads(line)
        except json.JSONDecodeError:
            continue

        kind = message.get("type")
        if kind == "render":
            send(ui())
        elif kind == "action":
            act(message)
            send(ui())
        elif kind == "shutdown":
            break


if __name__ == "__main__":
    main()
