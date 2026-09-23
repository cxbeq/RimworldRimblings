"""Convert Animalese Typing's eight A-Z AAC sets to Rimblings WAV banks.

Run once with the installed extension directory. The generated WAVs are package
assets; neither ffmpeg nor the extension is needed while playing RimWorld.
"""
from __future__ import annotations

import argparse
import array
import math
from pathlib import Path
import shutil
import subprocess
import wave


RATE = 44100
FRAMES = int(RATE * 0.15)
ROOT = Path(__file__).resolve().parents[1]


def import_banks(source: Path, ffmpeg: str) -> None:
    output = ROOT / "mod/Voices/Extension"
    output.mkdir(parents=True, exist_ok=True)
    for gender in ("female", "male"):
        for number in range(1, 5):
            folder = source / "assets/audio/animalese" / gender / f"voice_{number}"
            letters: list[array.array] = []
            for letter in "abcdefghijklmnopqrstuvwxyz":
                path = folder / f"{letter}.aac"
                if not path.is_file():
                    raise FileNotFoundError(path)
                decoded = subprocess.run(
                    [ffmpeg, "-nostdin", "-v", "error", "-i", str(path),
                     "-ac", "1", "-ar", str(RATE), "-f", "f32le", "-"],
                    check=True, capture_output=True,
                ).stdout
                samples = array.array("f")
                samples.frombytes(decoded)
                if len(samples) < FRAMES or not all(math.isfinite(x) for x in samples[:FRAMES]):
                    raise ValueError(f"Invalid or short letter clip: {path}")
                letters.append(samples[:FRAMES])
            # One gain per bank preserves relative letter dynamics while keeping
            # the eight source sets at similar levels before Rimblings mixing.
            energy = sum(x * x for samples in letters for x in samples)
            peak = max(abs(x) for samples in letters for x in samples)
            if energy <= 0 or peak <= 0:
                raise ValueError(f"Silent bank: {folder}")
            rms = math.sqrt(energy / (26 * FRAMES))
            gain = min(0.18 / rms, 0.95 / peak)
            target = output / f"{gender}_{number}.wav"
            with wave.open(str(target), "wb") as wav:
                wav.setnchannels(1)
                wav.setsampwidth(2)
                wav.setframerate(RATE)
                pcm = array.array("h", (round(max(-1, min(1, x * gain)) * 32767)
                                          for samples in letters for x in samples))
                wav.writeframes(pcm.tobytes())
            print(f"{target.name}: {target.stat().st_size} bytes")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("extension", type=Path)
    parser.add_argument("--ffmpeg", default=shutil.which("ffmpeg") or "ffmpeg")
    args = parser.parse_args()
    import_banks(args.extension, args.ffmpeg)
