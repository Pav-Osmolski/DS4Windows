"""Validate the shared layout of the installer and portable packages."""

from pathlib import Path


APPLICATION_FILES = (
    "DS4Windows.dll",
    "DS4Windows.deps.json",
    "DS4Windows.runtimeconfig.json",
)
RUNTIME_FILES = {
    "Runtime/BouncyCastle.Cryptography.dll",
    "Runtime/coreclr.dll",
    "Runtime/hostfxr.dll",
    "Runtime/hostpolicy.dll",
    *(f"Runtime/{name}" for name in APPLICATION_FILES),
}


def validate_runtime_layout(root: Path) -> None:
    unexpected = sorted(
        path.name for path in root.iterdir()
        if path.suffix.casefold() == ".dll" or path.name.casefold() in
        {name.casefold() for name in APPLICATION_FILES}
    )
    if unexpected:
        raise SystemExit("Unexpected top-level runtime files: " + ", ".join(unexpected))
