"""
Конвертер сценария Goliathchild из Ren'Py (script.rpy) в формат GiC_OS (.gics).

Зачем: перенести весь текст прототипа 2023 года без ручного копирования ~1000 реплик
и без опечаток. Рабочий стол, пароли и разблокировка архивов написаны вручную в
scenarios/goliathchild/desktop.gics, потому что в Ren'Py они жили в экранах (screens.rpy),
а не в сценарии. Эти метки конвертер пропускает.

Запуск (из корня проекта):
    python -I tools/rpy2gics.py "OLD/.../game/script.rpy" scenarios/goliathchild

Повторный запуск перезаписывает characters.gics, prolegomena.gics, act1.gics, act2.gics, act3.gics.
"""

import re
import sys
from pathlib import Path

# Метки, которые написаны вручную в desktop.gics (рабочий стол, BIOS, пароли архивов).
HAND_WRITTEN = {"main_menu", "start", "game", "act_one", "act_two", "act_three"}

# Распределение меток по файлам. Порядок внутри файла сохраняется как в script.rpy.
FILES = {
    "prolegomena.gics": ["prolegomena"],
    "act1.gics": ["one_log", "my_appeal", "four_log", "x_masgift", "seven_log", "mnogo_cifr", "eight_log"],
    "act2.gics": ["_02_05_1", "_05_05_1", "_06_05_1", "_20_05_1", "points", "_21_05_1"],
    "act3.gics": ["_25_05_1", "short25", "_26_05_1", "_27_05_1", "points1", "_29_05_0001",
                  "_28_05_01", "_29_05_1", "_01_06_1", "_02_06_1", "end"],
}

# Контрольные точки: автосохранение и открытие главы в меню «История».
CHECKPOINTS = {
    "one_log": ("act1", "Акт I"),
    "_02_05_1": ("act2", "Акт II"),
    "_25_05_1": ("act3", "Акт III"),
}

# Колбэки «голосов» персонажей из оригинала → файл мелодии.
VOICES = {
    "callback_al": "Alice.ogg",
    "callback_ki": "Kira.ogg",
    "callback_sc": "ScHill.ogg",
    "callback_ch": "Chopin.ogg",
    "callback_ge": "Geirby.ogg",
    "callback_re": "Renatus.ogg",
    "callback_he": "RenLaugh.ogg",
}

# Кто есть кто — комментарий в characters.gics, чтобы новым авторам было понятнее.
WHO = {
    "al": "Алиса (ник Celia)",
    "ki": "Кирилл (Kira)",
    "sc": "Шилл (SchHill)",
    "ch": "Шопен (Chopin)",
    "ge": "Гирби (Geirby_geirby)",
    "re": "Ренатус (REN)",
    "he": "Смех Ренатуса",
}

ASSET_PREFIX = "old2023/"

# В ремейке ассеты оригинала разложены по назначению, а в Ren'Py лежали почти плоско.
# Папки целиком: images/pic → иллюстрации, images/icon → иконки рабочего стола, audio/OST → музыка.
FOLDERS = {
    "images/pic/": "images/illustrations/",
    "images/icon/": "images/os/icons/",
    "audio/OST/": "audio/music/",
}
# Отдельные файлы из корня images/ и audio/ — по их роли в игре.
SCREENS = {"bios1", "bios2", "renatus", "credit"}
DOCUMENTS = {"about_site", "description", "LIKE", "page1", "page2", "web_page", "web_page1",
             "password_ACT_1", "password_ACT_2", "password_ACT_3"}
VOICE_FILES = {"Alice", "Chopin", "Geirby", "Kira", "Renatus", "RenLaugh", "ScHill"}


def asset(path: str) -> str:
    """Путь из Ren'Py (images/..., audio/...) → путь во встроенных ассетах ремейка."""
    path = path.lstrip("/")
    for old, new in FOLDERS.items():
        if path.startswith(old):
            return ASSET_PREFIX + new + path[len(old):]
    folder, _, file = path.rpartition("/")
    name = file.rsplit(".", 1)[0]
    if folder == "images":
        sub = "screens" if name in SCREENS else "documents" if name in DOCUMENTS else "os"
        return f"{ASSET_PREFIX}images/{sub}/{file}"
    if folder == "audio":
        return f"{ASSET_PREFIX}audio/{'voices' if name in VOICE_FILES else 'sfx'}/{file}"
    return ASSET_PREFIX + path


def quote(text: str) -> str:
    """Строка для .gics: экранируем только обратный слэш перед кавычкой и саму кавычку.
    Последовательности \\n из оригинала оставляем как есть — лексер .gics раскроет их так же, как Ren'Py."""
    return '"' + text.replace('"', '\\"') + '"'


def read_logical_lines(source: str):
    """Склеивает строки, если строка в кавычках продолжается на следующих строках файла.
    Ren'Py заменяет перевод строки внутри строки на пробел и схлопывает пробелы — делаем так же."""
    result = []
    buffer = None
    for number, raw in enumerate(source.splitlines(), 1):
        line = raw.replace("﻿", "")
        if buffer is not None:
            buffer[1] += " " + line.strip()
            if count_quotes(buffer[1]) % 2 == 0:
                result.append(tuple(buffer))
                buffer = None
            continue
        if count_quotes(line) % 2 == 1:
            buffer = [number, line.rstrip()]
            continue
        result.append((number, line.rstrip()))
    if buffer is not None:
        result.append(tuple(buffer))
    return result


def count_quotes(line: str) -> int:
    """Количество неэкранированных двойных кавычек."""
    return len(re.findall(r'(?<!\\)"', line))


def parse_characters(lines):
    """Ищет define X = Character(...) и $ X = Character(...) — объявления персонажей."""
    pattern = re.compile(r"^\s*(?:define|\$)\s*(\w+)\s*=\s*Character\('([^']*)'\s*,\s*color=\"([^\"]*)\"(?:\s*,\s*callback=(\w+))?\)")
    chars = []
    for _, line in lines:
        m = pattern.match(line)
        if m:
            cid, name, color, callback = m.groups()
            # [hai] в оригинале — имя текущего документа; в GiC_OS это переменная doc.
            name = name.replace("[hai]", "[doc]")
            chars.append((cid, name, color, VOICES.get(callback)))
    return chars


def split_labels(lines):
    """Делит сценарий на метки: имя → (комментарий, список строк тела)."""
    labels = {}
    order = []
    current = None
    for number, line in lines:
        m = re.match(r"^label\s+(\w+)\s*:\s*(?:#\s*(.*))?$", line)
        if m:
            current = m.group(1)
            labels[current] = (m.group(2) or "", [])
            order.append(current)
            continue
        if re.match(r"^\S", line):
            # Любая строка без отступа (init, define, комментарий) закрывает метку.
            current = None
            continue
        if current is not None:
            labels[current][1].append((number, line))
    return labels, order


class LabelConverter:
    """Переводит тело одной метки Ren'Py в команды .gics."""

    def __init__(self, viewer_state):
        # Состояние окна просмотра общее для всех меток: в Ren'Py экран живёт между метками.
        self.viewer = viewer_state
        self.out = []
        self.warnings = []

    def emit(self, text: str):
        self.out.append("    " + text)

    def convert(self, body):
        stmts = [(n, l.strip()) for n, l in body if l.strip() and not l.strip().startswith("#")]
        i = 0
        while i < len(stmts):
            number, s = stmts[i]
            consumed = self.statement(stmts, i)
            if consumed == 0:
                self.warnings.append(f"строка {number}: не переведено: {s}")
                self.emit(f"# НЕ ПЕРЕВЕДЕНО (строка {number}): {s}")
                consumed = 1
            i += consumed
        return self.out

    def statement(self, stmts, i):
        """Обрабатывает инструкцию stmts[i]. Возвращает, сколько строк поглощено (0 — не распознано)."""
        _, s = stmts[i]
        s = re.sub(r"\s+#.*$", "", s) if '"' not in s else s

        # Глитч: show layer screens at Glitch(...) / pause X / show layer screens → одна команда glitch.
        m = re.match(r"show layer screens at Glitch\(.*glitch_strength=\s*([\d.]+)\)", s)
        if m:
            strength = fmt(m.group(1))
            if i + 2 < len(stmts) and stmts[i + 1][1].startswith("pause") and stmts[i + 2][1] == "show layer screens":
                self.emit(f"glitch {strength} {fmt(stmts[i + 1][1].split()[1])}")
                return 3
            self.emit(f"glitch {strength} 0.2")
            return 1
        if s == "show layer screens":
            return 1

        m = re.match(r'\$\s*hai\s*=\s*"(.*)"$', s)
        if m:
            self.emit(f"doc {quote(m.group(1))}")
            return 1

        m = re.match(r'\$\s*image_file_name\s*=\s*"(.*)"$', s)
        if m:
            self.viewer["image"] = m.group(1)
            # Ren'Py обновляет открытый экран сразу при смене переменной.
            if self.viewer["shown"]:
                self.emit(f"viewer {quote(asset(m.group(1)))}")
            return 1

        if s.startswith("show screen imageviewer"):
            self.viewer["shown"] = True
            self.emit(f"viewer {quote(asset(self.viewer['image']))}")
            return 1
        if s == "call screen imageviewer":
            self.viewer["shown"] = False
            self.emit(f"viewer {quote(asset(self.viewer['image']))} wait")
            return 1
        if s == "hide screen imageviewer":
            self.viewer["shown"] = False
            self.emit("viewer hide")
            return 1

        if s == "show screen desktop":
            # Рабочий стол в GiC_OS виден всегда, пока его явно не скрыли.
            return 1
        if s == "hide screen desktop":
            self.emit("os desktop hide")
            return 1
        if s == "hide screen Folder":
            self.emit("os close")
            return 1

        m = re.match(r'play (music1|music|ambient|sound)\s+"([^"]+)"(?:\s+volume\s+([\d.]+))?', s)
        if m:
            channel = {"music1": "music"}.get(m.group(1), m.group(1))
            volume = f" volume={fmt(m.group(3))}" if m.group(3) else ""
            self.emit(f"{channel} {quote(asset(m.group(2)))}{volume}")
            return 1

        m = re.match(r"stop (music1|music|ambient|sound)(?:\s+fadeout\s+([\d.]+))?$", s)
        if m:
            channel = {"music1": "music"}.get(m.group(1), m.group(1))
            fade = f" fadeout={fmt(m.group(2))}" if m.group(2) else ""
            self.emit(f"stop {channel}{fade}")
            return 1

        m = re.match(r"pause\s+([\d.]+)$", s)
        if m:
            seconds = float(m.group(1))
            # pause 10000 в конце игры — по сути «ждать клика».
            self.emit("pause" if seconds >= 1000 else f"pause {fmt(m.group(1))}")
            return 1

        m = re.match(r"scene (\w[\w ]*?)(?:\s+with\s+(\w+))?$", s)
        if m:
            name = m.group(1)
            path = asset(f"images/{name}.png")
            trans = f" transition={m.group(2)}" if m.group(2) else ""
            self.emit(f"scene {quote(path)}{trans}")
            return 1

        m = re.match(r"show (\w+)(?:\s+with\s+(\w+))?$", s)
        if m:
            name = m.group(1)
            trans = f" transition={m.group(2)}" if m.group(2) else ""
            self.emit(f"show {name} {quote(asset('images/' + name + '.png'))}{trans}")
            return 1

        m = re.match(r"hide (\w+)$", s)
        if m:
            self.emit(f"hide {m.group(1)}")
            return 1

        m = re.match(r"jump (\w+)$", s)
        if m:
            self.emit(f"jump {m.group(1)}")
            return 1

        if s == "return":
            # После jump return недостижим — в оригинале это остатки шаблона, не переносим.
            if self.out and self.out[-1].strip().startswith("jump "):
                return 1
            self.emit("return")
            return 1

        if re.match(r"\$\s*renpy\.quit\(\)", s):
            self.emit("end")
            return 1

        # Реплика рассказчика: "текст"
        m = re.match(r'^"(.*)"$', s)
        if m:
            self.emit(quote(m.group(1)))
            return 1

        # Реплика персонажа: id "текст" (в оригинале часто без пробела: ch"текст")
        m = re.match(r'^(\w+)\s*"(.*)"$', s)
        if m:
            self.emit(f"{m.group(1)} {quote(m.group(2))}")
            return 1

        return 0


def fmt(number: str) -> str:
    """Число без лишних нулей: .3 → 0.3, 1.0 → 1."""
    value = float(number)
    return str(int(value)) if value.is_integer() else repr(value)


def main():
    if len(sys.argv) != 3:
        print(__doc__)
        sys.exit(1)
    source_path, out_dir = Path(sys.argv[1]), Path(sys.argv[2])
    out_dir.mkdir(parents=True, exist_ok=True)

    lines = read_logical_lines(source_path.read_text(encoding="utf-8-sig"))
    characters = parse_characters(lines)
    labels, order = split_labels(lines)

    header = ("# Автоматически перенесено из Ren'Py (script.rpy, Goliathchild v0.3.0, LDJAM 2023)\n"
              "# инструментом tools/rpy2gics.py. Правки текста лучше вносить здесь, а не в старом файле.\n\n")

    # ---- персонажи
    out = [header, "# Персонажи. Имя \"[doc]\" означает: в заголовке окна показывается имя текущего документа.\n",
           "# voice — мелодия, которая звучит, пока печатается реплика (как callback в оригинале).\n\n"]
    for cid, name, color, voice in characters:
        comment = f"  # {WHO[cid]}" if cid in WHO else ""
        voice_part = f" voice={quote(asset('audio/' + voice))}" if voice else ""
        out.append(f"character {cid} {quote(name)} color={color}{voice_part}{comment}\n")
    (out_dir / "characters.gics").write_text("".join(out), encoding="utf-8", newline="\n")

    # ---- сценарий по файлам
    viewer = {"shown": False, "image": "images/password.png"}
    used = set()
    report = []
    for file_name, names in FILES.items():
        out = [header]
        # Порядок меток — по сюжету (как в списке FILES), а не по положению в script.rpy.
        for name in [n for n in names if n in labels]:
            comment, body = labels[name]
            used.add(name)
            out.append(f"label {name}:" + (f"  # {comment}" if comment else "") + "\n")
            if name in CHECKPOINTS:
                cp_id, cp_title = CHECKPOINTS[name]
                out.append(f"    checkpoint {cp_id} {quote(cp_title)}\n")
            conv = LabelConverter(viewer)
            out.extend(line + "\n" for line in conv.convert(body))
            out.append("\n")
            report.extend(f"{name}: {w}" for w in conv.warnings)
        (out_dir / file_name).write_text("".join(out).rstrip() + "\n", encoding="utf-8", newline="\n")

    missing = [n for n in order if n not in used and n not in HAND_WRITTEN]
    print(f"Персонажей: {len(characters)}, меток перенесено: {len(used)}")
    if missing:
        print("Метки без файла назначения:", ", ".join(missing))
    for w in report:
        print("ВНИМАНИЕ:", w)


if __name__ == "__main__":
    main()
