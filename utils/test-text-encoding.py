"""Read-only UTF-8/mojibake gate for tracked Markdown and release-note files."""
import argparse
from pathlib import Path
import re
import subprocess
import sys
import unittest

ROOT = Path(__file__).resolve().parents[1]
SUSPICIOUS = re.compile(
    r"[\ufffd\u0080-\u009f]|\u00e2\u20ac|\u00c3[\u0080-\u00bf]|\u00c2[\u00a0-\u00bf]"
)


def problems(data):
    try:
        text = data.decode("utf-8-sig", errors="strict")
    except UnicodeDecodeError as error:
        return [f"invalid UTF-8 at byte {error.start}"]
    return [f"line {number}: suspected garbled text ({ascii(match.group())})"
            for number, line in enumerate(text.splitlines(), 1)
            for match in SUSPICIOUS.finditer(line)]


class EncodingTests(unittest.TestCase):
    def test_valid_punctuation_and_names(self):
        self.assertEqual([], problems("DS4Windows \u2014 2026-10-18; 0.15\u20130.18; S\u00e3o; Ren\u00e9e; \u00a9".encode("utf-8")))

    def test_utf8_bom_is_supported(self):
        self.assertEqual([], problems(b"\xef\xbb\xbfRelease notes"))

    def test_invalid_utf8_is_rejected(self):
        self.assertTrue(problems(b"range \x96 end"))

    def test_garbled_utf8_decodings_are_rejected(self):
        for original in ["\u2013", "\u2014", "\u2019", "\u00b7", "\u00e9"]:
            for wrong_codec in ["cp1252", "latin1"]:
                with self.subTest(original=ascii(original), codec=wrong_codec):
                    garbled = original.encode("utf-8").decode(wrong_codec)
                    self.assertTrue(problems(garbled.encode("utf-8")))

    def test_replacement_character_is_rejected(self):
        self.assertTrue(problems("lost \ufffd text".encode("utf-8")))

    def test_reports_line_number(self):
        self.assertIn("line 2:", problems("good\n\ufffd".encode("utf-8"))[0])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("paths", nargs="*", help="Optional release-note files; default: all tracked Markdown")
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        result = unittest.TextTestRunner().run(unittest.defaultTestLoader.loadTestsFromTestCase(EncodingTests))
        if not result.wasSuccessful():
            return 1
    paths = args.paths or subprocess.check_output(
        ["git", "ls-files", "-z", "--", "*.md"], cwd=ROOT
    ).decode("utf-8").rstrip("\0").split("\0")
    failed = False
    for name in paths:
        try:
            errors = problems((ROOT / name).read_bytes())
        except OSError as error:
            errors = [str(error)]
        for error in errors:
            print(f"{name}: {error}", file=sys.stderr)
            failed = True
    if not failed:
        print(f"UTF-8/text encoding check passed for {len(paths)} files.")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
