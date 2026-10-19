"""Stage canonical repository docs for Pages without editing their source text."""
from pathlib import Path
import json
import posixpath
import re
import shutil
import xml.etree.ElementTree as ET
from urllib.parse import quote, unquote, urlsplit
import yaml

ROOT = Path(__file__).resolve().parents[1]
STAGE = ROOT / ".pages" / "docs"
REPO = "https://github.com/Pav-Osmolski/DS4Windows"


def destination(source):
    return Path("index.md") if source == Path("README.md") else source


def rewrite_link(match, source, documents):
    prefix, target, suffix = match.groups()
    parts = urlsplit(target)
    if parts.scheme or parts.netloc or not parts.path or parts.path.startswith("/"):
        return match.group(0)
    resolved = posixpath.normpath((source.parent / unquote(parts.path)).as_posix())
    path = Path(resolved)
    if resolved.startswith("../"):
        return match.group(0)
    if path in documents or path == Path("README.md"):
        target_path = posixpath.relpath(destination(path).as_posix(), destination(source).parent.as_posix())
        target = quote(target_path, safe="/.-")
        if parts.fragment:
            target += "#" + parts.fragment
    elif (ROOT / path).is_file() and not path.as_posix().startswith("docs/images/"):
        target = f"{REPO}/blob/main/{quote(path.as_posix())}"
        if parts.fragment:
            target += "#" + parts.fragment
    elif (ROOT / path).is_dir() and not path.as_posix().startswith("docs/"):
        target = f"{REPO}/tree/main/{quote(path.as_posix())}"
    return prefix + target + suffix


def main():
    # This path is a dedicated, ignored generated directory, never the source docs.
    expected = ROOT.resolve() / ".pages" / "docs"
    if STAGE.resolve() != expected or not STAGE.resolve().is_relative_to(ROOT.resolve()):
        raise RuntimeError("Refusing to clear a generated directory outside this checkout.")
    if STAGE.exists():
        shutil.rmtree(STAGE)
    STAGE.mkdir(parents=True, exist_ok=True)
    documents = set(p.relative_to(ROOT) for p in (ROOT / "docs").rglob("*.md"))
    documents.update(Path(p) for p in ("CHANGELOG.md", "contributors.md", "contributing.md"))
    for source in sorted(documents):
        text = (ROOT / source).read_text(encoding="utf-8-sig")
        # Preserve canonical source; adapt repository-only links in the staged copy.
        text = re.sub(r"(\]\()([^\s)]+)(\))", lambda m: rewrite_link(m, source, documents), text)
        # Historical investigations remain linked and readable, but should not
        # outrank current setup guidance when a player searches for a feature.
        if source.parts[:2] in (("docs", "validation"), ("docs", "protocols")):
            metadata = {}
            if text.startswith("---\n") and "\n---\n" in text[4:]:
                front, text = text[4:].split("\n---\n", 1)
                metadata = yaml.safe_load(front) or {}
            metadata.setdefault("search", {})["exclude"] = True
            text = "---\n" + yaml.safe_dump(metadata, sort_keys=False) + "---\n" + text
        target = STAGE / source
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(text, encoding="utf-8")
    shutil.copytree(ROOT / "docs/images", STAGE / "docs/images", dirs_exist_ok=True)
    shutil.copytree(ROOT / "website/assets", STAGE / "assets", dirs_exist_ok=True,
                    ignore=shutil.ignore_patterns("README.md"))
    shutil.copyfile(ROOT / "website/index.md", STAGE / "index.md")
    version = ET.parse(ROOT / "DS4Windows/DS4WinWPF.csproj").findtext(".//Version")
    (STAGE / "assets/release.json").write_text(json.dumps({"version": version}), encoding="utf-8")
    # The homepage uses one source of version truth as well.
    index = (STAGE / "index.md").read_text(encoding="utf-8")
    (STAGE / "index.md").write_text(index.replace("template: home.html", f"template: home.html\nrelease_version: '{version}'"), encoding="utf-8")
    (STAGE / "robots.txt").write_text("User-agent: *\nAllow: /\nSitemap: https://pav-osmolski.github.io/DS4Windows/sitemap.xml\n", encoding="utf-8")
    print(f"Staged {len(documents)} canonical documentation pages for DS4Windows {version}.")


if __name__ == "__main__":
    main()
