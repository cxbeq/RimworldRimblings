#!/usr/bin/env python3
"""Rimblings development/release helper. Python standard library only.

No command uploads to Steam, handles passwords, or modifies game saves.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import struct
import subprocess
import sys
import tempfile
import urllib.request
import uuid
import wave
import xml.etree.ElementTree as ET
import zipfile
import zlib

ROOT = Path(__file__).resolve().parents[1]
MOD = ROOT / "mod"
DIST = ROOT / "dist"
LOCAL = ROOT / ".local"
PACKAGE_ID = "paddy.rimblings"
EXTENSION_BANKS = [f"Voices/Extension/{gender}_{number}.wav"
                   for gender in ("female", "male") for number in range(1, 5)]


def run(command: list[str]) -> None:
    print("+", subprocess.list2cmdline(command), flush=True)
    result = subprocess.run(command, cwd=ROOT, check=False)
    if result.returncode:
        raise RuntimeError(f"Command failed with exit code {result.returncode}")


def version() -> str:
    value = ET.parse(ROOT / "src/Rimblings/Rimblings.csproj").findtext("./PropertyGroup/Version", "")
    if not re.fullmatch(r"\d+\.\d+\.\d+(?:-[A-Za-z0-9.]+)?", value):
        raise ValueError("Set a valid Version in Rimblings.csproj")
    return value


def file_id(path: Path) -> str | None:
    if not path.exists():
        return None
    text = path.read_text(encoding="utf-8-sig").strip()
    if not re.fullmatch(r"[0-9]{1,20}", text) or not 0 < int(text) < 2**64:
        raise ValueError(f"Invalid Workshop ID in {path}")
    return str(int(text))


def package_id(path: Path) -> str:
    return ET.parse(path / "About/About.xml").findtext("packageId", "").lower()


def git_blob_hash(data: bytes) -> str:
    return hashlib.sha1(b"blob " + str(len(data)).encode() + b"\0" + data).hexdigest()


def acquire_assets() -> None:
    lock = json.loads((ROOT / "assets/voicebank.lock.json").read_text(encoding="utf-8"))
    target = MOD / "Voices/default.wav"
    target.parent.mkdir(parents=True, exist_ok=True)
    if target.exists():
        data = target.read_bytes()
    else:
        print("Fetching pinned Acedio voice bank (CC BY 4.0). Attribution is retained.")
        request = urllib.request.Request(lock["url"], headers={"User-Agent": "Rimblings-build/0.1"})
        with urllib.request.urlopen(request, timeout=60) as response:
            data = response.read(4 * 1024 * 1024 + 1)
    if len(data) > 4 * 1024 * 1024 or git_blob_hash(data) != lock["git_blob_sha1"]:
        raise ValueError("Voice-bank integrity mismatch. Refusing to package unexpected audio.")
    target.write_bytes(data)
    check_wave(target)
    print("Voice bank verified:", hashlib.sha256(data).hexdigest())


def check_wave(path: Path) -> None:
    with wave.open(str(path), "rb") as wav:
        if wav.getcomptype() != "NONE" or wav.getnchannels() not in (1, 2) or wav.getsampwidth() not in (1, 2):
            raise ValueError("Voice bank must be PCM8/PCM16 mono/stereo")
        if not 8000 <= wav.getframerate() <= 96000 or wav.getnframes() < int(wav.getframerate() * 0.15) * 26:
            raise ValueError("Voice bank needs 26 consecutive 150 ms A-Z samples")


# Original development artwork, deliberately labelled PROTOTYPE. Replace for release.
FONT = {
    "R": ["11110", "10001", "10001", "11110", "10100", "10010", "10001"],
    "I": ["11111", "00100", "00100", "00100", "00100", "00100", "11111"],
    "M": ["10001", "11011", "10101", "10101", "10001", "10001", "10001"],
    "B": ["11110", "10001", "10001", "11110", "10001", "10001", "11110"],
    "L": ["10000", "10000", "10000", "10000", "10000", "10000", "11111"],
    "N": ["10001", "11001", "10101", "10011", "10001", "10001", "10001"],
    "G": ["01111", "10000", "10000", "10111", "10001", "10001", "01111"],
    "S": ["01111", "10000", "10000", "01110", "00001", "00001", "11110"],
    "P": ["11110", "10001", "10001", "11110", "10000", "10000", "10000"],
    "O": ["01110", "10001", "10001", "10001", "10001", "10001", "01110"],
    "T": ["11111", "00100", "00100", "00100", "00100", "00100", "00100"],
    "Y": ["10001", "10001", "01010", "00100", "00100", "00100", "00100"],
    "E": ["11111", "10000", "10000", "11110", "10000", "10000", "11111"],
}


def preview_bytes() -> bytes:
    size = 512
    pixels = bytearray(size * size * 3)
    for y in range(size):
        for x in range(size):
            at = (y * size + x) * 3
            pixels[at:at + 3] = bytes((20 + y // 32, 30 + y // 24, 39 + x // 32))
    def box(x: int, y: int, w: int, h: int, colour: tuple[int, int, int]) -> None:
        for yy in range(max(y, 0), min(y + h, size)):
            for xx in range(max(x, 0), min(x + w, size)):
                at = (yy * size + xx) * 3
                pixels[at:at + 3] = bytes(colour)
    def text(value: str, y: int, scale: int, colour: tuple[int, int, int]) -> None:
        left = (size - (len(value) * 6 - 1) * scale) // 2
        for index, letter in enumerate(value):
            for row, bits in enumerate(FONT[letter]):
                for col, bit in enumerate(bits):
                    if bit == "1":
                        box(left + (index * 6 + col) * scale, y + row * scale, scale, scale, colour)
    for i, height in enumerate([36, 74, 115, 168, 120, 202, 146, 82, 42]):
        box(93 + i * 37, 190 - height // 2, 18, height, (127, 223, 172))
    text("RIMBLINGS", 335, 8, (240, 243, 231))
    text("PROTOTYPE", 424, 3, (178, 190, 192))
    raw = b"".join(b"\0" + pixels[y * size * 3:(y + 1) * size * 3] for y in range(size))
    def chunk(tag: bytes, value: bytes) -> bytes:
        return struct.pack(">I", len(value)) + tag + value + struct.pack(">I", zlib.crc32(tag + value) & 0xffffffff)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 2, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")


def ensure_preview() -> None:
    target = MOD / "About/Preview.png"
    if not target.exists():
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(preview_bytes())


def validate(folder: Path, release: bool = False) -> None:
    if package_id(folder) != PACKAGE_ID:
        raise ValueError("Wrong mod packageId")
    metadata = ET.parse(folder / "About/About.xml")
    if [node.text for node in metadata.findall("./supportedVersions/li")] != ["1.6"]:
        raise ValueError("This scaffold targets RimWorld 1.6 only")
    for required in ["Assemblies/Rimblings.dll", "Voices/default.wav", "About/Preview.png", "About/ModIcon.png", "LICENSE", "LICENSES/Animalese.txt", *EXTENSION_BANKS]:
        if not (folder / required).is_file():
            raise ValueError(f"Missing package file: {required}")
    for path in folder.rglob("*"):
        if path.is_symlink():
            raise ValueError("Symlinks are not allowed in distributable packages")
        if path.suffix.lower() == ".dll" and path.name != "Rimblings.dll":
            raise ValueError(f"Unexpected DLL in package: {path.name}")
    preview = folder / "About/Preview.png"
    if not preview.read_bytes().startswith(b"\x89PNG\r\n\x1a\n") or preview.stat().st_size >= 1024 * 1024:
        raise ValueError("Use a PNG preview below 1 MiB")
    icon = (folder / "About/ModIcon.png").read_bytes()
    if len(icon) < 24 or not icon.startswith(b"\x89PNG\r\n\x1a\n") or struct.unpack_from(">II", icon, 16) != (64, 64):
        raise ValueError("Use a 64x64 PNG mod icon")
    check_wave(folder / "Voices/default.wav")
    for name in EXTENSION_BANKS:
        check_wave(folder / name)
    file_id(folder / "About/PublishedFileId.txt")
    if release:
        status_path = LOCAL / "release-status.json"
        if not status_path.is_file():
            raise ValueError("Release checks are not approved: missing local release-status.json")
        status = json.loads(status_path.read_text(encoding="utf-8"))
        for key in ("in_game_tested", "listening_comparison_complete", "licences_reviewed", "final_preview"):
            if status.get(key) is not True:
                raise ValueError(f"Release check not approved: {key}")
        if preview.read_bytes() == preview_bytes():
            raise ValueError("Replace the PROTOTYPE preview before public release")
    print("Validated:", folder)


def package() -> Path:
    dll = ROOT / "src/Rimblings/bin/Release/net472/Rimblings.dll"
    if not dll.is_file():
        raise ValueError("Build the Release DLL first")
    ensure_preview()
    DIST.mkdir(exist_ok=True)
    stage = DIST / "Rimblings"
    if stage.exists():
        shutil.rmtree(stage)
    stage.mkdir()
    for directory in ("About", "Languages", "Voices", "Defs", "Patches", "Textures", "Sounds"):
        source = MOD / directory
        if source.is_dir():
            shutil.copytree(source, stage / directory, ignore=shutil.ignore_patterns("PublishedFileId.txt"))
    for name in ("LoadFolders.xml", "README.md"):
        if (MOD / name).is_file():
            shutil.copy2(MOD / name, stage / name)
    (stage / "Assemblies").mkdir(exist_ok=True)
    shutil.copy2(dll, stage / "Assemblies/Rimblings.dll")
    for name in ("LICENSE",):
        shutil.copy2(ROOT / name, stage / name)
    shutil.copytree(ROOT / "LICENSES", stage / "LICENSES", dirs_exist_ok=True)
    saved_id = file_id(LOCAL / "PublishedFileId.txt")
    if saved_id:
        (stage / "About/PublishedFileId.txt").write_text(saved_id + "\n", encoding="utf-8")
    validate(stage)
    archive = DIST / f"Rimblings-{version()}.zip"
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as output:
        for path in sorted(stage.rglob("*")):
            if path.is_file():
                output.write(path, path.relative_to(DIST))
    digest = hashlib.sha256(archive.read_bytes()).hexdigest()
    (DIST / (archive.name + ".sha256")).write_text(f"{digest}  {archive.name}\n", encoding="utf-8")
    print("Manual-install archive:", archive)
    return stage


def install_into(stage: Path, game: Path, backup_root: Path) -> Path:
    game = game.resolve()
    if not (game / "Data/Core/About/About.xml").is_file():
        raise ValueError("--game must be the RimWorld installation root containing Data/Core/About/About.xml")
    mods = game / "Mods"
    mods.mkdir(exist_ok=True)
    target = mods / "Rimblings"
    if target.is_symlink() or mods.is_symlink():
        raise ValueError("Refusing to replace a symlinked Mods/Rimblings installation")
    if target.exists() and package_id(target) != PACKAGE_ID:
        raise ValueError("Existing target is not Rimblings; nothing was changed")
    for tree in [stage, target] if target.exists() else [stage]:
        if any(path.is_symlink() for path in tree.rglob("*")):
            raise ValueError("Refusing an installation containing symlinks")
    old_id = file_id(target / "About/PublishedFileId.txt")
    new_id = file_id(stage / "About/PublishedFileId.txt")
    if old_id and new_id and old_id != new_id:
        raise ValueError("Workshop IDs conflict. Refusing to overwrite the published item identity")
    incoming = Path(tempfile.mkdtemp(prefix=".Rimblings-stage-", dir=mods))
    backup = None
    try:
        shutil.copytree(stage, incoming, dirs_exist_ok=True)
        if old_id:
            (incoming / "About/PublishedFileId.txt").write_text(old_id + "\n", encoding="utf-8")
        if target.exists():
            backup_root.mkdir(parents=True, exist_ok=True)
            backup = backup_root / ("Rimblings-" + uuid.uuid4().hex)
            shutil.copytree(target, backup)
            shutil.rmtree(target)
        try:
            incoming.rename(target)
        except OSError:
            if backup is not None and not target.exists():
                shutil.copytree(backup, target)
            raise
    finally:
        if incoming.exists():
            shutil.rmtree(incoming)
    print("Installed:", target)
    if backup:
        print("Previous version backed up outside the game's Mods directory:", backup)
    return target


def make_vdf(stage: Path, item: str | None, output: Path) -> None:
    validate(stage)
    # SteamCMD writes the new ID back into its VDF. Never reset that ID on regeneration.
    if output.exists():
        match = re.search(r'"publishedfileid"\s+"([0-9]+)"', output.read_text(encoding="utf-8"))
        previous = match.group(1) if match and int(match.group(1)) else None
        if previous and item and item != previous:
            raise ValueError("Existing VDF belongs to another item. Refusing to reset its ID")
        item = item or previous
    item = item or "0"  # 0 creates a new private item; existing IDs are preserved.
    if not re.fullmatch(r"[0-9]{1,20}", item) or int(item) >= 2**64:
        raise ValueError("Invalid item ID")
    def quote(value: str) -> str:
        return '"' + value.replace("\\", "/").replace('"', '\\"').replace("\n", " ") + '"'
    fields = {"appid": "294100", "publishedfileid": item, "contentfolder": str(stage.resolve()), "previewfile": str((stage / "About/Preview.png").resolve()), "visibility": "2", "title": "Rimblings", "description": "Experimental close-up Animalese-style pawn speech. See included README and attribution.", "changenote": f"Rimblings {version()} prototype"}
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text('"workshopitem"\n{\n' + "".join(f"    {quote(k)} {quote(v)}\n" for k, v in fields.items()) + "}\n", encoding="utf-8")
    print("PRIVATE upload descriptor only (nothing uploaded):", output)
    print("Prefer RimWorld's in-game uploader. SteamCMD is an optional alternative.")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("doctor")
    sub.add_parser("assets")
    sub.add_parser("preview")
    test = sub.add_parser("test")
    test.add_argument("--python-only", action="store_true")
    build = sub.add_parser("build")
    build.add_argument("--managed", type=Path)
    sub.add_parser("package")
    check = sub.add_parser("validate")
    check.add_argument("--release", action="store_true")
    install = sub.add_parser("install")
    install.add_argument("--game", type=Path, required=True)
    remember = sub.add_parser("remember-id")
    remember.add_argument("--game", type=Path, required=True)
    vdf = sub.add_parser("workshop-vdf")
    vdf.add_argument("--item-id")
    audition = sub.add_parser("audition")
    audition.add_argument("--text", default="Hello there! This is Rimblings.")
    audition.add_argument("--sex", default="Female")
    audition.add_argument("--body", default="Thin")
    audition.add_argument("--head", default="Female_AverageNormal")
    audition.add_argument("--age", default="28")
    args = parser.parse_args()
    if sys.version_info < (3, 11):
        raise RuntimeError("Python 3.11 or newer is required")
    if args.command == "doctor":
        print("Python:", sys.version.split()[0])
        for name in ("git", "dotnet"):
            print(name + ":", shutil.which(name) or "MISSING")
        if shutil.which("dotnet"):
            run(["dotnet", "--list-sdks"])
        print("Required: stable .NET 10 SDK. Runtime target: .NET Framework 4.7.2. Game reference: 1.6.4871.")
    elif args.command == "assets":
        acquire_assets()
    elif args.command == "preview":
        ensure_preview()
        print(MOD / "About/Preview.png")
    elif args.command in ("test", "build"):
        run([sys.executable, "-m", "unittest", "discover", "-s", "tests", "-p", "test_*.py", "-v"])
        if args.command == "test" and args.python_only:
            return
        if not shutil.which("dotnet"):
            raise RuntimeError("Install the .NET 10 SDK and reopen the terminal")
        run(["dotnet", "run", "--project", "tests/Rimblings.Checks.csproj", "-c", "Release"])
        if args.command == "build":
            command = ["dotnet", "build", "src/Rimblings/Rimblings.csproj", "-c", "Release", "--nologo"]
            if args.managed:
                path = str(args.managed.resolve())
                if any(c in path for c in ";\n\r"):
                    raise ValueError("Managed path cannot contain semicolons or newlines")
                command.append("-p:GameManagedDir=" + path)
            run(command)
            acquire_assets()
            package()
    elif args.command == "package":
        package()
    elif args.command == "validate":
        validate(DIST / "Rimblings", args.release)
    elif args.command == "install":
        validate(DIST / "Rimblings")
        install_into(DIST / "Rimblings", args.game, LOCAL / "backups")
    elif args.command == "remember-id":
        installed = args.game.resolve() / "Mods/Rimblings"
        if package_id(installed) != PACKAGE_ID:
            raise ValueError("Not a Rimblings installation")
        item = file_id(installed / "About/PublishedFileId.txt")
        if not item:
            raise ValueError("Publish once from RimWorld first; no PublishedFileId.txt was found")
        previous = file_id(LOCAL / "PublishedFileId.txt")
        if previous and previous != item:
            raise ValueError("Saved Workshop ID conflicts; inspect both IDs manually")
        LOCAL.mkdir(exist_ok=True)
        (LOCAL / "PublishedFileId.txt").write_text(item + "\n", encoding="utf-8")
        print("Remembered Workshop ID:", item)
    elif args.command == "workshop-vdf":
        known = file_id(LOCAL / "PublishedFileId.txt") or file_id(DIST / "Rimblings/About/PublishedFileId.txt")
        if args.item_id and known and args.item_id != known:
            raise ValueError("Explicit item ID conflicts with the known Rimblings item")
        make_vdf(DIST / "Rimblings", args.item_id or known, LOCAL / "workshop.vdf")
    elif args.command == "audition":
        LOCAL.mkdir(exist_ok=True)
        run(["dotnet", "run", "--project", "tools/VoiceLab/VoiceLab.csproj", "-c", "Release", "--", str(MOD / "Voices/Extension"), str(LOCAL / "audition.wav"), args.text, args.sex, args.body, args.head, args.age])


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, RuntimeError, ET.ParseError, wave.Error) as error:
        print("ERROR:", error, file=sys.stderr)
        sys.exit(1)
