#!/usr/bin/env python3
"""Extract literal WPF presentation text without rewriting binding identifiers.

Run after the syntax-aware C# inventory. Product names, units and editable
sample values are explicit invariants; they are not translation gaps.
"""
from pathlib import Path
import argparse
import hashlib
import html
import json
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
INVARIANT = {
    "DS4Windows", "DS4Windows - Pav-Osmolski Build", "Xbox 360", "Xbox One / Series",
    "DualShock 4", "DualSense", "DualSense Edge", "Switch 2 Pro", "Switch Pro", "Joy-Con", "Joy-Con 2",
    "VIIPER", "USB/IP", "HidHide", "FakerInput", "Bluetooth", "USB", "XInput", "DirectInput",
    "DualShock", "DS4", "DS3", "DS4 v.1", "DS4 v.2", "Windows", "Windows 10", "Windows 11",
    "MIT", "GPL", "GPL-3.0", "UDP", "OSC", "DSU", "CRC", "CPU", "Hz", "MHz", "ms", "dB", "RGB", "VID", "PID",
    "L1", "L2", "L3", "R1", "R2", "R3", "LX", "LY", "RX", "RY", "LB", "RB", "LT", "RT",
    "Aa", "Pav-Osmolski", "hbashton", "schmaldeo", "Ryochan7", "Jays2Kings", "nefarius", "Nefarius",
}
ATTRIBUTES = {"Text", "Content", "Header", "ToolTip", "Title", "Watermark"}
ATTRIBUTE = re.compile(r'(?P<name>\b(?:Text|Content|Header|ToolTip|Title|Watermark))="(?P<value>[^"]*)"')

def key(source):
    return "UI_" + hashlib.sha256(source.encode("utf-8")).hexdigest()[:16].upper()

def human(source):
    return (source.strip() not in INVARIANT and not source.startswith(("{", "http:", "https:"))
            and re.search(r"[A-Za-z]{3}", source) is not None)

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true")
    parser.add_argument("--inventory", type=Path, required=True)
    args = parser.parse_args()
    occurrences = []
    for path in sorted((ROOT / "DS4Windows/DS4Forms").rglob("*.xaml")):
        if "Themes" in path.parts:
            continue
        source = path.read_text(encoding="utf-8-sig")
        def replace(match):
            text = html.unescape(match["value"])
            if not human(text):
                return match[0]
            identity = key(text)
            occurrences.append({"File": path.relative_to(ROOT).as_posix(), "Source": text, "ResourceKey": identity})
            return match["name"] + '="{lex:Loc Strings:' + identity + '}"'
        changed = ATTRIBUTE.sub(replace, source)
        if changed != source:
            # Explicit dictionary prefixes work even in older views whose default
            # resource family is Properties.Resources rather than Strings.
            opening = changed.index(">", changed.index("<"))
            header = changed[:opening]
            additions = []
            if 'xmlns:lex=' not in header:
                additions.append('xmlns:lex="http://wpflocalizeextension.codeplex.com"')
            if 'lex:ResxLocalizationProvider.DefaultAssembly=' not in header:
                additions.append('lex:ResxLocalizationProvider.DefaultAssembly="DS4Windows"')
            if additions:
                changed = changed[:opening] + "\n        " + "\n        ".join(additions) + changed[opening:]
            ET.fromstring(changed)  # Refuse malformed namespace or quote edits.
            if not args.check:
                path.write_bytes(changed.encode("utf-8"))
    args.inventory.write_text(json.dumps(occurrences, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"{len(occurrences)} literal UI attributes; {len({x['Source'] for x in occurrences})} distinct phrases.")
    if args.check and occurrences:
        raise SystemExit("New literal UI strings require resource extraction or an explicit invariant.")

if __name__ == "__main__":
    main()
