"""Build the YamaPlayer desktop source ZIP for installation into Assets."""

import argparse
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile, ZipInfo


ROOT = Path(__file__).resolve().parents[1]
SOURCE_EXTENSIONS = {".cs", ".shader", ".otf", ".txt"}


def add_sources(archive, directory, target):
    for path in sorted(directory.rglob("*")):
        if path.is_file() and path.suffix in SOURCE_EXTENSIONS:
            entry = ZipInfo((target / path.relative_to(directory)).as_posix())
            archive.writestr(entry, path.read_bytes(), compress_type=ZIP_DEFLATED)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    output = parser.parse_args().output.resolve()
    output.mkdir(parents=True, exist_ok=True)

    path = output / "PaulKoiPlayer-YamaPlayer.zip"
    pending = path.with_suffix(".zip.tmp")
    with ZipFile(pending, "w") as archive:
        for part in ("Editor", "Runtime", "Shaders", "Fonts"):
            add_sources(archive, ROOT / part, Path("Assets/YamaBiliDanmakuV3") / part)
    pending.replace(path)
    print(path)


if __name__ == "__main__":
    main()
