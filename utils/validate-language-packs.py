#!/usr/bin/env python3
"""Validate every shipped language without accepting neutral-resource fallback.

This checks coverage and runtime safety, not native-speaker fluency.
Run from any directory; --json writes a reviewable coverage report.
"""
from pathlib import Path
import argparse
import hashlib
import json
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
CULTURES = "ar cs de el es fi fr he hu-HU id it ja ms nl pl pt pt-BR ru sv tr uk-UA vi zh-Hans zh-Hant".split()
FORMAT = re.compile(r"(?<!\{)\{\d+(?:\s*,\s*-?\d+)?(?::[^{}]*)?\}(?!\})")

def read(path):
    values = {}
    for node in ET.parse(path).findall("data"):
        if node.get("type") or node.get("mimetype"):
            continue
        name = node.get("name")
        if name in values:
            raise ValueError(f"Duplicate resource {path.name}:{name}")
        values[name] = node.findtext("value") or ""
    return values

def validate():
    report, failures = [], []
    neutral_strings = None
    bases = {}
    for family, folder in (("Strings", "Translations"), ("Resources", "Properties")):
        directory = ROOT / "DS4Windows" / folder
        base = read(directory / (family + ".resx"))
        bases[family] = base
        if family == "Strings": neutral_strings = base
        for culture in CULTURES:
            matches = [p for p in directory.glob(family + ".*.resx") if p.name[len(family) + 1:-5].lower() == culture.lower()]
            if len(matches) != 1:
                failures.append(f"{family}:{culture}: expected one language pack, got {len(matches)}")
                continue
            values = read(matches[0])
            missing = sorted(set(base) - set(values))
            empty = sorted(key for key in base if not values.get(key, "").strip())
            slots = sorted(key for key in base if sorted(FORMAT.findall(base[key])) != sorted(FORMAT.findall(values.get(key, ""))))
            tokens = sorted(key for key, value in values.items() if re.search(r"<\s*x\s*\d+\s*>", value, re.I))
            # Legacy identities remain for binary/API compatibility, but their
            # guidance must describe the current backend in every language.
            repair_keys = ["ViGEmPluginFailure", "ViGEm117MinNeeded", "Welcome.Step1Text"] if family == "Strings" else ["OpenScpDriver", "Step1"]
            obsolete = [key for key in repair_keys if "VIIPER" not in values.get(key, "") or re.search(r"ViGEm|ScpDriver", values.get(key, ""), re.I)]
            for label, keys in (("missing", missing), ("empty", empty), ("format mismatch", slots), ("unresolved translation token", tokens), ("obsolete repair advice", obsolete)):
                if keys: failures.append(f"{family}:{culture}: {label}: {', '.join(keys)}")
            report.append({"family": family, "culture": culture, "required": len(base), "translated_entries": len(set(base) & set(values)), "missing": missing, "empty": empty, "placeholder_errors": slots})
    catalog = json.loads((ROOT / "utils/localization-interface-catalog.json").read_text(encoding="utf-8"))
    for key, row in catalog.items():
        expected = "UI_" + hashlib.sha256(row["source"].encode()).hexdigest()[:16].upper()
        if key != expected or neutral_strings.get(key) != row["source"]:
            failures.append("Catalog/neutral text mismatch: " + key)
    for path in (ROOT / "DS4Windows/DS4Forms").rglob("*.xaml"):
        for node in ET.parse(path).iter():
            for value in node.attrib.values():
                match = re.fullmatch(r"\{lex:Loc (?:(Strings|Resources):)?([A-Za-z0-9_.]+)\}", value)
                if not match: continue
                family, key = match.groups()
                found = key in bases[family] if family else any(key in base for base in bases.values())
                if not found: failures.append(f"Missing referenced UI resource: {path.name}:{key}")
                if key.startswith("UI_") and key not in catalog: failures.append(f"Uncatalogued UI resource: {path.name}:{key}")
    return {"languages": len(CULTURES), "families": 2, "coverage": report, "failures": failures,
            "review_note": "Complete resource coverage and placeholder checks do not certify native-speaker wording."}

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--json", type=Path)
    args = parser.parse_args()
    report = validate()
    if args.json: args.json.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    for failure in report["failures"]: print(failure)
    print(f"Checked {len(report['coverage'])} language/family pairs; {len(report['failures'])} failures.")
    raise SystemExit(bool(report["failures"]))

if __name__ == "__main__": main()
