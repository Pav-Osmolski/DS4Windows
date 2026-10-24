#!/usr/bin/env python3
"""Prepare neutral UI resources; never invent translations or copy English into packs."""
from pathlib import Path
import argparse
import hashlib
import json
import re
import xml.etree.ElementTree as ET
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parent.parent
XML_SPACE = "{http://www.w3.org/XML/1998/namespace}space"

def identity(text):
    return "UI_" + hashlib.sha256(text.encode("utf-8")).hexdigest()[:16].upper()

def save(tree, path):
    ET.indent(tree, space="  ")
    tree.write(path, encoding="utf-8", xml_declaration=True)
    with path.open("ab") as stream:
        stream.write(b"\n")

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--inventory-directory", required=True, type=Path)
    args = parser.parse_args()
    inventory = []
    for name in ("xaml-extraction.json", "csharp-extraction.json"):
        inventory += json.loads((args.inventory_directory / name).read_text(encoding="utf-8"))
    # These labels are supplied through catalog records, rather than literal
    # properties; UiText translates them when the language-specific UI is built.
    phrases = ["Follow Windows", "Light", "Dark", "Graphite", "Paper", "Midnight", "High contrast",
               "Theme accent", "Windows accent", "Custom colour", "Blue", "Violet", "Teal", "Amber", "Rose"]
    for phrase in phrases:
        inventory.append({"File": "DS4Windows/DS4Forms/Themes/AppThemeCatalog.cs", "Source": phrase, "ResourceKey": identity(phrase)})
    # One legacy calibration message is element content rather than an attribute.
    path = ROOT / "DS4Windows/DS4Forms/StickCalibrationWindow.xaml"
    source = path.read_text(encoding="utf-8")
    text = "Please move your stick to the sides and release it a few times, then after you are done press Save."
    if text in source:
        source = source.replace('Padding="5" TextWrapping="Wrap">\n            ' + text,
                                'Padding="5" TextWrapping="Wrap" Text="{lex:Loc Strings:' + identity(text) + '}">')
        ET.fromstring(source)
        path.write_bytes(source.encode("utf-8"))
    inventory.append({"File": str(path.relative_to(ROOT)).replace("\\", "/"), "Source": text, "ResourceKey": identity(text)})
    resources = ROOT / "DS4Windows/Translations/Strings.resx"
    tree = ET.parse(resources)
    existing = {node.get("name"): node for node in tree.findall("data")}
    target = ROOT / "utils/localization-interface-catalog.json"
    catalog = json.loads(target.read_text(encoding="utf-8")) if target.exists() else {}
    for row in inventory:
        key, phrase = row["ResourceKey"], row["Source"]
        if key in catalog and catalog[key]["source"] != phrase:
            raise ValueError("Resource identity collision: " + key)
        entry = catalog.setdefault(key, {"source": phrase, "files": []})
        if row["File"] not in entry["files"]:
            entry["files"].append(row["File"])
    for key, row in sorted(catalog.items()):
        node = existing.get(key)
        if node is None:
            node = ET.SubElement(tree.getroot(), "data", {"name": key, XML_SPACE: "preserve"})
            ET.SubElement(node, "value")
        node.find("value").text = row["source"]
        comment = node.find("comment")
        if comment is None:
            comment = ET.SubElement(node, "comment")
        comment.text = "Interface text. Preserve numbered placeholders and technical names. Used in: " + "; ".join(row["files"])
    # Retain historical resource identities used by the existing generated API,
    # while replacing obsolete installation/recovery instructions with current ones.
    corrections = {
        "ViGEmPluginFailure": "The virtual controller could not start (error {0}). Open Settings > Controllers and select Install / Repair VIIPER. Check Log for details.",
        "ViGEm117MinNeeded": "Requires the supported VIIPER and USB/IP versions. Open Settings > Controllers and select Install / Repair VIIPER.",
        "Welcome.Step1Text": "Step 1: Install VIIPER and USB/IP",
        "FirstLaunch.DS3EnableText": "Support for DualShock 3 (PS3) controllers. Requires the DsHidMini driver from Nefarius.",
        "Welcome.Step2Text": "Step 2: Check virtual controller support",
        "Welcome.Step2HelpText": "This build requires Windows 10 or Windows 11. VIIPER and USB/IP provide virtual controller output; the old Windows 7 Xbox 360 driver is not required.",
        "Welcome.Step5HelpText": "FakerInput provides virtual keyboard and mouse input for profiles that need it. It is optional for controller-only output. Compatibility depends on the game and its input restrictions.",
    }
    for key, value in corrections.items():
        existing[key].find("value").text = value
    save(tree, resources)
    properties = ROOT / "DS4Windows/Properties/Resources.resx"
    tree = ET.parse(properties)
    other = {node.get("name"): node for node in tree.findall("data")}
    property_corrections = {
        "OpenScpDriver": "Open Settings > Controllers and select Install / Repair VIIPER.",
        "Step1": "Step 1: Install VIIPER and USB/IP",
        "DinputOnly": "Disable virtual controller output",
        "OutContNotice": "Choose the virtual controller games should detect. Save the profile to apply the change.",
    }
    for key, value in property_corrections.items():
        other[key].find("value").text = value
    save(tree, properties)
    target = ROOT / "utils/localization-interface-catalog.json"
    target.write_text(json.dumps(catalog, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    (args.inventory_directory / "updated-legacy-keys.json").write_text(json.dumps(
        {"Strings": list(corrections), "Resources": list(property_corrections)}, indent=2) + "\n", encoding="utf-8")
    print(f"Prepared {len(catalog)} distinct interface phrases and corrected {len(corrections) + len(property_corrections)} inherited messages.")

if __name__ == "__main__":
    main()
