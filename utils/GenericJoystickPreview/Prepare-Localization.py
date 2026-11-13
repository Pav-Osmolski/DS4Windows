"""Give the lab preview the release Lang layout without release broker aliases."""
import importlib.util
from pathlib import Path
import re
import stat
import sys


def prepare(root: Path) -> None:
    root = root.absolute()
    entries = [root, *root.rglob("*")]
    if not root.is_dir() or any(p.is_symlink() or getattr(p.lstat(), "st_file_attributes", 0)
                               & getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0) for p in entries):
        raise SystemExit("Preview contains a missing or unsafe path.")
    cultures = sorted({p.parent for p in root.glob("*/*.resources.dll")})
    for culture in cultures:
        if not re.fullmatch(r"[A-Za-z0-9]+(?:-[A-Za-z0-9]+)*", culture.name):
            raise SystemExit("Invalid satellite culture directory: " + culture.name)
        if (root / "Lang" / culture.name).exists():
            raise SystemExit("Satellite destination already exists: " + culture.name)
    for culture in cultures:
        destination = root / "Lang" / culture.name
        destination.parent.mkdir(exist_ok=True)
        culture.rename(destination)
    spec = importlib.util.spec_from_file_location("package_validator", Path(__file__).resolve().parents[1] / "validate-installer.py")
    validator = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(validator)
    validator.validate_localization_package(root)
    # The final preview manifest is generated after localization and launchers.
    print("Preview Lang layout passed the release localization validator.")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit("Usage: Prepare-Localization.py <fresh-preview-directory>")
    prepare(Path(sys.argv[1]))
