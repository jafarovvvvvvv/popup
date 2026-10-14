# Плагины Popup

Плагин — это папка в `%APPDATA%\Popup\plugins\` с файлом `plugin.json` и
программой, которая читает строки JSON из stdin и пишет строки JSON в stdout.

Язык не важен: Python, Node, Go, скомпилированный exe, `.bat`. Popup запускает
процесс, разговаривает с ним построчным JSON и рисует то, что тот описал.

Готовый пример лежит в `plugins/localwhisper/` — он читает историю локального
Whisper и показывает последние расшифровки.

## plugin.json

```json
{
  "id": "localwhisper",
  "name": "Whisper",
  "icon": "IconPlugin",
  "command": "python",
  "args": ["main.py"],
  "cwd": ".",
  "refreshMs": 3000,
  "env": { "MY_TOKEN": "..." }
}
```

| Поле | Смысл |
|---|---|
| `id` | Устойчивое имя. По умолчанию — имя папки |
| `name` | Подпись в шапке панели |
| `icon` | Ключ иконки из `src/Popup/UI/Icons.xaml`. По умолчанию `IconPlugin` |
| `command`, `args` | Чем запустить |
| `cwd` | Рабочая папка относительно папки плагина |
| `refreshMs` | Как часто просить перерисоваться, **пока вкладка открыта**. `0` — не просить |
| `env` | Дополнительные переменные окружения |

## Протокол

### Popup → плагин

```json
{"type": "render"}
{"type": "action", "id": "transcribe", "value": "текст из поля"}
{"type": "shutdown"}
```

### Плагин → Popup

```json
{"type": "ui", "nodes": [ ... ]}
{"type": "log", "message": "строка в журнал"}
```

Всё, что плагин напечатает в **stderr** или не-JSON строкой в stdout, попадает в
журнал вкладки Dev, а не в разборщик. Отладочный `print` во время разработки
ничего не ломает.

Python буферизует stdout — Popup выставляет `PYTHONUNBUFFERED=1` за вас. На
других языках сбрасывайте поток сами после каждой строки.

## Узлы интерфейса

```json
{"type": "text",     "text": "…", "muted": true, "bold": false}
{"type": "row",      "children": [ … ]}
{"type": "list",     "items": [
    {"id": "1", "label": "Заголовок", "text": "подпись",
     "value": "что скопировать", "action": "copy"}
]}
{"type": "input",    "id": "query", "value": "", "placeholder": "…", "action": "search"}
{"type": "button",   "id": "go", "label": "Расшифровать", "value": "…"}
{"type": "image",    "path": "C:\\…\\preview.png"}
{"type": "progress", "value": 0.4}
```

Набор узкий намеренно: из них нельзя собрать что попало, зато собранное из них
выглядит как остальная панель, а не как окно внутри окна.

## Действия

У `list`, `input` и `button` есть `action`. Два значения панель выполняет сама,
не будя процесс:

- `"copy"` — положить `value` в буфер обмена;
- `"open"` — открыть `value` как путь или ссылку.

Любое другое имя приезжает плагину как `{"type": "action", "id": "…", "value": "…"}`.
У `input` действие срабатывает по Enter, у `button` — по клику, у строки
`list` — по клику по строке.

## Разработка

Вкладка **Dev** — рабочее место:

- живой журнал всего разговора в обе стороны;
- список процессов и их состояние;
- **Reload plugins** — перезапускает всё без перезапуска Popup;
- консоль: выбрать плагин и отправить ему любой JSON руками.

Цикл разработки: поправил файл → Reload → навёл на вкладку. Перезапускать
панель не нужно.

## Минимальный плагин

```python
import json, sys

def send(m):
    sys.stdout.write(json.dumps(m, ensure_ascii=False) + "\n")
    sys.stdout.flush()

for line in sys.stdin:
    msg = json.loads(line)
    if msg["type"] == "render":
        send({"type": "ui", "nodes": [
            {"type": "text", "text": "Привет из плагина"},
            {"type": "button", "id": "ping", "label": "Проверить"},
        ]})
    elif msg["type"] == "action":
        send({"type": "log", "message": f"нажали {msg['id']}"})
    elif msg["type"] == "shutdown":
        break
```

## Про безопасность

Popup запускает всё, что лежит в папке плагинов, с вашими правами. Это
осознанно: папка ваша, и код в ней ваш. Не кладите туда то, чего не читали.
