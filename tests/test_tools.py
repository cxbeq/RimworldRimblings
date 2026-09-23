"""Packaging and safe-install tests. Uses only disposable directories."""
import importlib.util
from pathlib import Path
import struct
import tempfile
import unittest
import xml.etree.ElementTree as ET

spec = importlib.util.spec_from_file_location("manage", Path(__file__).resolve().parents[1] / "tools/manage.py")
manage = importlib.util.module_from_spec(spec)
spec.loader.exec_module(manage)


class ToolTests(unittest.TestCase):
    def mod(self, path, package="Paddy.rimblings", item=None):
        (path / "About").mkdir(parents=True)
        (path / "About/About.xml").write_text(f"<ModMetaData><packageId>{package}</packageId></ModMetaData>")
        (path / "payload.txt").write_text("new payload")
        if item:
            (path / "About/PublishedFileId.txt").write_text(item)

    def game(self, base):
        game = base / "Game"
        (game / "Data/Core/About").mkdir(parents=True)
        (game / "Data/Core/About/About.xml").write_text("<ModMetaData />")
        return game

    def test_new_install(self):
        with tempfile.TemporaryDirectory() as directory:
            base = Path(directory)
            self.mod(base / "stage")
            game = self.game(base)
            target = manage.install_into(base / "stage", game, base / "backups")
            self.assertEqual((target / "payload.txt").read_text(), "new payload")

    def test_update_preserves_id_and_backups(self):
        with tempfile.TemporaryDirectory() as directory:
            base = Path(directory)
            game = self.game(base)
            target = game / "Mods/Rimblings"
            self.mod(target, item="1234567890")
            (target / "payload.txt").write_text("old payload")
            self.mod(base / "stage")
            manage.install_into(base / "stage", game, base / "backups")
            self.assertEqual(manage.file_id(target / "About/PublishedFileId.txt"), "1234567890")
            backups = list((base / "backups").glob("*/payload.txt"))
            self.assertEqual(len(backups), 1)
            self.assertEqual(backups[0].read_text(), "old payload")
            self.assertEqual([p.name for p in (game / "Mods").iterdir()], ["Rimblings"])

    def test_refuses_different_mod(self):
        with tempfile.TemporaryDirectory() as directory:
            base = Path(directory)
            game = self.game(base)
            self.mod(game / "Mods/Rimblings", package="someone.else")
            self.mod(base / "stage")
            with self.assertRaises(ValueError):
                manage.install_into(base / "stage", game, base / "backups")
            self.assertTrue((game / "Mods/Rimblings/payload.txt").exists())

    def test_refuses_id_conflict(self):
        with tempfile.TemporaryDirectory() as directory:
            base = Path(directory)
            game = self.game(base)
            self.mod(game / "Mods/Rimblings", item="123")
            self.mod(base / "stage", item="456")
            with self.assertRaises(ValueError):
                manage.install_into(base / "stage", game, base / "backups")
            self.assertEqual(manage.file_id(game / "Mods/Rimblings/About/PublishedFileId.txt"), "123")

    def test_refuses_invalid_game(self):
        with tempfile.TemporaryDirectory() as directory:
            base = Path(directory)
            self.mod(base / "stage")
            with self.assertRaises(ValueError):
                manage.install_into(base / "stage", base / "not-game", base / "backups")

    def test_invalid_id(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "id.txt"
            for bad in ["0", "-1", "a123", "18446744073709551616", ""]:
                path.write_text(bad)
                with self.assertRaises(ValueError):
                    manage.file_id(path)

    def test_preview(self):
        data = manage.preview_bytes()
        self.assertTrue(data.startswith(b"\x89PNG\r\n\x1a\n"))
        self.assertEqual(struct.unpack(">II", data[16:24]), (512, 512))
        self.assertLess(len(data), 1024 * 1024)
        self.assertEqual(data, manage.preview_bytes())

    def test_git_blob_digest(self):
        self.assertEqual(manage.git_blob_hash(b""), "e69de29bb2d1d6434b8b29ae775ad8c2e48c5391")

    def test_repository_xml(self):
        for root in [manage.ROOT / "mod", manage.ROOT / "src"]:
            for pattern in ("*.xml", "*.csproj"):
                for path in root.rglob(pattern):
                    ET.parse(path)

    def test_extension_voice_banks(self):
        import hashlib
        import wave
        digests = []
        for name in manage.EXTENSION_BANKS:
            path = manage.MOD / name
            manage.check_wave(path)
            with wave.open(str(path), "rb") as bank:
                self.assertEqual(bank.getnchannels(), 1)
                self.assertEqual(bank.getframerate(), 44100)
                self.assertEqual(bank.getnframes(), 26 * 6615)
            digests.append(hashlib.sha256(path.read_bytes()).digest())
        self.assertEqual(len(set(digests)), 8)


if __name__ == "__main__":
    unittest.main()
