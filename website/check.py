"""Check emitted page titles, local links/assets, image descriptions and sitemap."""
from html.parser import HTMLParser
from pathlib import Path
from urllib.parse import unquote, urlsplit
import re
import sys

ROOT = Path(__file__).resolve().parents[1] / ".pages/site"
BASE = "https://pav-osmolski.github.io/DS4Windows/"


class Page(HTMLParser):
    def __init__(self):
        super().__init__()
        self.links = []
        self.ids = set()
        self.titles = 0
        self.missing_alt = 0

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if "id" in attrs:
            self.ids.add(attrs["id"])
        if tag == "title":
            self.titles += 1
        if tag == "img" and "alt" not in attrs:
            self.missing_alt += 1
        for attribute in ("href", "src"):
            if attrs.get(attribute):
                self.links.append(attrs[attribute])


def main():
    errors = []
    verification_files = set()
    static = Path(__file__).resolve().parent / "static"
    for source in static.glob("google*.html"):
        target = ROOT / source.name
        expected = f"google-site-verification: {source.name}".encode("ascii")
        if (not re.fullmatch(r"google[0-9a-f]+\.html", source.name)
                or source.read_bytes().strip() != expected):
            errors.append(f"Invalid Google verification file: {source.name}")
        if not target.is_file() or target.read_bytes() != source.read_bytes():
            errors.append(f"Missing or changed verification output: {source.name}")
        verification_files.add(target.resolve())
    pages = {}
    for path in ROOT.rglob("*.html"):
        # Google's exact plain-text HTML files intentionally have no page title.
        if path.resolve() in verification_files:
            continue
        page = Page()
        page.feed(path.read_text(encoding="utf-8"))
        pages[path.resolve()] = page
    checked = 0
    for source, page in pages.items():
        if page.titles != 1 or page.missing_alt:
            errors.append(f"{source.relative_to(ROOT)}: title count {page.titles}, images without alt {page.missing_alt}")
        for link in page.links:
            if link.startswith(BASE):
                link = "/" + link[len(BASE):]
            url = urlsplit(link)
            if url.scheme or url.netloc:
                continue
            path = unquote(url.path)
            if path.startswith("/DS4Windows/"):
                path = path[len("/DS4Windows"):]
            target = ROOT / path.lstrip("/") if path.startswith("/") else source.parent / path
            if not path:
                target = source
            elif path.endswith("/") or target.is_dir():
                target /= "index.html"
            target = target.resolve()
            checked += 1
            if not target.is_file():
                errors.append(f"{source.relative_to(ROOT)}: missing {link}")
            elif url.fragment and target in pages and unquote(url.fragment) not in pages[target].ids:
                errors.append(f"{source.relative_to(ROOT)}: missing anchor {link}")
    for name in ("sitemap.xml", "robots.txt", "assets/hero-background.webp", "docs/screenshots/index.html"):
        if not (ROOT / name).is_file():
            errors.append(f"Missing required output: {name}")
    if errors:
        print("\n".join(errors[:80]))
        sys.exit(f"{len(errors)} site validation errors")
    print(f"Validated {len(pages)} pages and {checked} internal link/asset references.")


if __name__ == "__main__":
    main()
