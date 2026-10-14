"""Плагин Popup: что происходит на домашнем сервере.

Только чтение. Плагин ходит по SSH тем же ключом, что уже лежит в
``~/.ssh/id_ed25519``, и спрашивает две вещи: жив ли сервер и какие контейнеры
на нём подняты. Ничего не запускает, не останавливает и не требует sudo —
панель, которая может уронить прод одним промахом мыши, это не удобство.

Настройки — в ``config.json`` рядом с этим файлом; править его безопасно.
"""

import json
import shutil
import subprocess
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
CONFIG = HERE / "config.json"

DEFAULTS = {
    "host": "192.168.0.10",
    "user": "user",
    # Опрос по сети — дорогая операция по сравнению с перерисовкой. Панель
    # просит перерисоваться каждые 15 секунд, а на сервер мы ходим реже и
    # отдаём то, что узнали в прошлый раз.
    "poll_seconds": 30,
    "ssh_timeout": 6,
}

_cache = {"at": 0.0, "nodes": None}


def config():
    data = dict(DEFAULTS)
    try:
        data.update(json.loads(CONFIG.read_text(encoding="utf-8")))
    except Exception:
        pass
    return data


def send(message):
    sys.stdout.write(json.dumps(message, ensure_ascii=False) + "\n")
    sys.stdout.flush()


#: Где искать ssh, если его нет в PATH. У процесса плагина окружение
#: наследуется от Popup, а тот запускается ярлыком — переменные оболочки, в
#: которой ssh «есть», до него не доходят.
SSH_CANDIDATES = [
    r"C:\Windows\System32\OpenSSH\ssh.exe",
    r"C:\Program Files\Git\usr\bin\ssh.exe",
    r"C:\Program Files\OpenSSH\ssh.exe",
]


def ssh_path():
    found = shutil.which("ssh")
    if found:
        return found
    for candidate in SSH_CANDIDATES:
        if Path(candidate).exists():
            return candidate
    return None


def ssh(cfg, command):
    """Одна команда на сервере. BatchMode: без ключа сразу отказ, а не
    молчаливое ожидание пароля, которого здесь некому ввести."""
    exe = ssh_path()
    if exe is None:
        return None, "ssh не найден"
    argv = [
        exe,
        "-o", "BatchMode=yes",
        "-o", "StrictHostKeyChecking=accept-new",
        "-o", f"ConnectTimeout={cfg['ssh_timeout']}",
        f"{cfg['user']}@{cfg['host']}",
        command,
    ]
    try:
        result = subprocess.run(
            argv, capture_output=True, text=True,
            # Сервер говорит в UTF-8, а Windows по умолчанию расшифровал бы это
            # кодировкой системной локали — и русские буквы превратились бы в
            # кашу прямо в панели.
            encoding="utf-8", errors="replace",
            timeout=cfg["ssh_timeout"] + 4,
            creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
        )
    except (subprocess.TimeoutExpired, FileNotFoundError) as e:
        return None, str(e)
    if result.returncode != 0:
        return None, (result.stderr or "").strip().splitlines()[-1] if result.stderr else "ошибка ssh"
    return result.stdout, None


def containers(cfg):
    out, err = ssh(cfg, "docker ps --format '{{.Names}}\t{{.Status}}' 2>/dev/null || true")
    if err is not None:
        return None, err
    rows = []
    for line in (out or "").splitlines():
        if "\t" not in line:
            continue
        name, status = line.split("\t", 1)
        rows.append((name.strip(), status.strip()))
    return rows, None


def uptime(cfg):
    # Команда — только латиницей: единицы подписываются уже здесь. Русские
    # буквы, пропущенные через ssh и чужую локаль, приезжают обратно кашей.
    out, _ = ssh(cfg, "uptime -p 2>/dev/null; free -m | awk '/Mem:/{print $3\"/\"$2}'")
    lines = [l.strip() for l in (out or "").splitlines() if l.strip()]
    if len(lines) >= 2 and "/" in lines[-1]:
        lines[-1] += " МБ"
    return lines


def build_nodes():
    cfg = config()
    rows, err = containers(cfg)

    if err is not None:
        return [
            {"type": "text", "text": f"{cfg['host']} не отвечает", "bold": True},
            {"type": "text", "text": err, "muted": True},
            {"type": "button", "id": "refresh", "label": "Ещё раз"},
        ]

    lines = uptime(cfg)
    nodes = [{"type": "text", "text": " · ".join(lines) if lines else cfg["host"], "muted": True}]

    if not rows:
        nodes.append({"type": "text", "text": "Контейнеров не запущено", "muted": True})
    else:
        nodes.append({
            "type": "list",
            "items": [
                {
                    "id": name,
                    "label": name,
                    # Статус Docker сам по себе читается как «Up 3 days» —
                    # переводить его нечем и незачем.
                    "text": status,
                    "value": name,
                    "action": "copy",
                }
                for name, status in rows
            ],
        })

    nodes.append({
        "type": "row",
        "children": [
            {"type": "button", "id": "refresh", "label": "Обновить"},
            {"type": "button", "id": "open", "action": "open",
             "value": f"http://{cfg['host']}", "label": "Открыть"},
        ],
    })
    return nodes


def nodes(force=False):
    cfg = config()
    now = time.monotonic()
    if not force and _cache["nodes"] is not None and now - _cache["at"] < cfg["poll_seconds"]:
        return _cache["nodes"]
    _cache["nodes"] = build_nodes()
    _cache["at"] = now
    return _cache["nodes"]


def main():
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
            send({"type": "ui", "nodes": nodes()})
        elif kind == "action":
            send({"type": "ui", "nodes": nodes(force=True)})
        elif kind == "shutdown":
            break


if __name__ == "__main__":
    main()
