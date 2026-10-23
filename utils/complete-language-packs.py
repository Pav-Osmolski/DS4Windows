#!/usr/bin/env python3
"""Offline maintainer translation completion. Not an application dependency.

Requires CTranslate2, SentencePiece and OpenCC in an isolated maintainer env.
Existing translations are preferred; new translations have an explicit receipt.
No application text, settings or diagnostic logs are uploaded to a service.
"""
from pathlib import Path
import argparse
import copy
import hashlib
import json
import os
import re
import sys
import time
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
CULTURES = ["ar", "cs", "de", "el", "es", "fi", "fr", "he", "hu-HU", "id", "it", "ja", "ms", "nl", "pl", "pt", "pt-BR", "ru", "sv", "tr", "uk-UA", "vi", "zh-Hans", "zh-Hant"]
MODEL_LANGUAGE = {"hu-HU": "hu", "uk-UA": "uk", "pt-BR": "pt", "zh-Hans": "zh", "zh-Hant": "zh"}
FORMAT = re.compile(r"(?<!\{)\{\d+(?:\s*,\s*-?\d+)?(?::[^{}]*)?\}(?!\})")
TOKEN = re.compile(r"(?<!\{)\{\d+(?:\s*,\s*-?\d+)?(?::[^{}]*)?\}(?!\})|https?://[^\s<>]+|\*\.[A-Za-z0-9*]+|\b[LR][123]\b")
BRANDS = ["DS4Windows", "VIIPER", "USB/IP", "HidHide", "HidGuardian", "FakerInput", "DsHidMini", "DualSense", "DualShock", "PlayStation", "Bluetooth", "DirectInput", "XInput", "Windows", "Nintendo", "Nefarius", "Steam"]
TOKEN = re.compile(TOKEN.pattern + "|" + "|".join(re.escape(value) for value in sorted(BRANDS, key=len, reverse=True)))
INVARIANT = set(BRANDS + ["DS4", "DS3", "Xbox", "Xbox 360", "Xbox One / Series", "DualShock 4", "DualSense Edge", "Switch 2 Pro", "Switch Pro", "Joy-Con", "Joy-Con 2", "RGB", "UDP", "OSC", "DSU", "MHz", "Hz", "ms", "dB", "CRC", "CPU", "VID", "PID", "MIT", "GPL", "GPL-3.0", "TCP", "USB", "N/A", "LX", "LY", "RX", "RY", "R1", "R2", "R3", "L1", "L2", "L3", "Ctrl", "Alt", "Shift", "Tab", "Esc", "Enter", "Space", "DS4 v.1", "DS4 v.2", "Windows 10", "Windows 11", "hbashton", "schmaldeo", "Ryochan7", "Jays2Kings", "nefarius", "Pav-Osmolski"])

def phrases(path):
    return {d.get("name"): d.findtext("value") or "" for d in ET.parse(path).findall("data") if not d.get("type") and not d.get("mimetype")}

def slots(text):
    return sorted(FORMAT.findall(text))

def translatable(text):
    value = text.strip()
    return value not in INVARIANT and bool(re.search(r"[A-Za-z]{3}", value)) and not re.fullmatch(r"https?://\S+", value)

def technical_names_intact(source, value):
    return all(not re.search(r"\b" + re.escape(brand) + r"\b", source) or brand in value for brand in BRANDS)

def atomic_save(tree, path):
    ET.indent(tree, space="  ")
    temporary = path.with_suffix(".resx.tmp")
    tree.write(temporary, encoding="utf-8", xml_declaration=True)
    with temporary.open("ab") as stream:
        stream.write(b"\n")
    temporary.replace(path)

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", required=True, type=Path)
    parser.add_argument("--tokenizer", required=True, type=Path)
    parser.add_argument("--cache", required=True, type=Path)
    parser.add_argument("--changes", required=True, type=Path)
    parser.add_argument("--cultures", nargs="*", default=CULTURES)
    parser.add_argument("--device", default="cuda", choices=["cuda", "cpu"])
    args = parser.parse_args()
    args.cache.mkdir(parents=True, exist_ok=True)
    # Scope DLL search to this disposable environment. Do not change PATH for
    # the desktop app or install a driver/toolkit on the user's machine.
    dll_handles = []
    for path in (Path(sys.prefix) / "Lib/site-packages/nvidia").glob("*/bin"):
        os.environ["PATH"] = str(path.resolve()) + os.pathsep + os.environ["PATH"]
        dll_handles.append(os.add_dll_directory(str(path.resolve())))
    import ctranslate2
    import sentencepiece
    from opencc import OpenCC
    sp = sentencepiece.SentencePieceProcessor(model_file=str(args.tokenizer))
    translator = ctranslate2.Translator(str(args.model), device=args.device,
        compute_type="float16" if args.device == "cuda" else "int8", inter_threads=1)
    traditional = OpenCC("s2twp")
    corrections = json.loads(args.changes.read_text(encoding="utf-8"))
    manual = json.loads((ROOT / "utils/localization-theme-overrides.json").read_text(encoding="utf-8"))
    ui_overrides = json.loads((ROOT / "utils/localization-ui-overrides.json").read_text(encoding="utf-8"))
    families = [("Strings", ROOT / "DS4Windows/Translations/Strings.resx"),
                ("Resources", ROOT / "DS4Windows/Properties/Resources.resx")]
    bases = {family: phrases(path) for family, path in families}
    receipt = {"method": "Existing translations plus offline M2M100-1.2B assistance and explicit overrides; not native-speaker certification.",
               "model": "michaelfeil/ct2fast-m2m100_1.2B", "model_revision": "081c9b82953d4eeb2309daabf2951e395c5dc979",
               "base_model": "facebook/m2m100_1.2B", "base_revision": "7b36184180524c1a1bbfa37f120a608046250b98", "license": "MIT", "cultures": []}
    receipt_path = args.cache / "completion-receipt.json"
    if receipt_path.exists():
        receipt["cultures"] = json.loads(receipt_path.read_text(encoding="utf-8")).get("cultures", [])

    for culture in args.cultures:
        started = time.monotonic()
        labels = dict(zip(ui_overrides["sources"], ui_overrides["translations"][culture], strict=True))
        cache_path = args.cache / (culture + ".json")
        cache = json.loads(cache_path.read_text(encoding="utf-8")) if cache_path.exists() else {}
        cache = {source: value for source, value in cache.items() if technical_names_intact(source, value)}
        paths, existing, memory = {}, {}, {}
        for family, neutral in families:
            matches = [p for p in neutral.parent.glob(family + ".*.resx") if p.name[len(family) + 1:-5].lower() == culture.lower()]
            path = matches[0] if matches else neutral.with_name(family + "." + culture + ".resx")
            paths[family] = path
            existing[family] = phrases(path) if path.exists() else {}
            for name, source in bases[family].items():
                value = existing[family].get(name, "")
                if name not in corrections.get(family, []) and value.strip() and slots(value) == slots(source) and value.strip() != source.strip() and technical_names_intact(source, value):
                    memory.setdefault(source, value)
        cache.update(memory)
        for source, value in zip(["Follow Windows", "Graphite", "Paper", "Midnight", "High contrast"], manual[culture]):
            cache[source] = value
        sources = sorted({value for base in bases.values() for value in base.values() if value not in cache and translatable(value)}, key=len)
        prefix = "__" + MODEL_LANGUAGE.get(culture, culture) + "__"
        constrained = []
        print(json.dumps({"culture": culture, "pending": len(sources)}, ensure_ascii=True), flush=True)

        def decode(batch):
            encoded = [["__en__"] + sp.encode(text, out_type=str) + ["</s>"] for text in batch]
            if any(len(tokens) > 1000 for tokens in encoded):
                raise ValueError("Source exceeds model context; split explicitly rather than truncate")
            outputs = translator.translate_batch(encoded, target_prefix=[[prefix] for _ in batch], beam_size=4,
                max_batch_size=32, max_decoding_length=512, repetition_penalty=1.05)
            return [sp.decode(result.hypotheses[0][1:]) for result in outputs]

        def restore(source, translated):
            if culture == "zh-Hant":
                translated = traditional.convert(translated)
            for brand in BRANDS:
                translated = re.sub(re.escape(brand), brand, translated, flags=re.IGNORECASE)
            # Keep spacing at concatenation boundaries, not arbitrary model spaces.
            leading = source[:len(source) - len(source.lstrip())]
            trailing = source[len(source.rstrip()):]
            return leading + translated.strip() + trailing

        for offset in range(0, len(sources), 32):
            batch = sources[offset:offset + 32]
            prepared, tokens_by_source = [], []
            for source in batch:
                tokens = []
                def mask(match):
                    tokens.append(match[0])
                    return "<x" + str(len(tokens) - 1) + ">"
                prepared.append(TOKEN.sub(mask, source))
                tokens_by_source.append(tokens)
            translated_batch = decode(prepared)
            for source, translated, tokens in zip(batch, translated_batch, tokens_by_source):
                intact = True
                for index, token in enumerate(tokens):
                    pattern = r"<\s*x\s*" + str(index) + r"\s*>"
                    if re.search(pattern, translated, re.I):
                        translated = re.sub(pattern, lambda _: token, translated, flags=re.I)
                    else:
                        intact = False
                if not intact or slots(source) != slots(translated) or not translated.strip():
                    # Never lose or invent a format placeholder. Translate the
                    # surrounding segments if the model fails to copy a token.
                    parts = TOKEN.split(source)
                    values = TOKEN.findall(source)
                    content = [part for part in parts if translatable(part)]
                    mapped = iter(decode(content)) if content else iter([])
                    rebuilt = []
                    for index, part in enumerate(parts):
                        rebuilt.append(restore(part, next(mapped)) if translatable(part) else part)
                        if index < len(values):
                            rebuilt.append(values[index])
                    translated = "".join(rebuilt)
                    constrained.append(source)
                cache[source] = restore(source, translated)
                if slots(cache[source]) != slots(source) or not cache[source].strip():
                    raise ValueError("Unresolved placeholder/empty translation: " + culture + " " + source)
            cache_path.write_text(json.dumps(cache, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
            print(json.dumps({"culture": culture, "done": min(offset + 32, len(sources)), "total": len(sources)}, ensure_ascii=True), flush=True)

        for family, neutral in families:
            path = paths[family]
            tree = ET.parse(path) if path.exists() else ET.parse(neutral)
            if not path.exists():
                for node in list(tree.getroot().findall("data")):
                    if node.get("type") or node.get("mimetype"):
                        tree.getroot().remove(node)
            nodes = {node.get("name"): node for node in tree.findall("data")}
            for name, source in bases[family].items():
                node = nodes.get(name)
                if node is None:
                    node = ET.SubElement(tree.getroot(), "data", {"name": name, "{http://www.w3.org/XML/1998/namespace}space": "preserve"})
                    ET.SubElement(node, "value")
                current = existing[family].get(name, "")
                keep = name not in corrections.get(family, []) and current.strip() and slots(current) == slots(source) and technical_names_intact(source, current) and (current.strip() != source.strip() or not translatable(source))
                value = labels.get(source, current if keep else cache.get(source, source))
                if translatable(source) and source not in cache and not keep:
                    raise ValueError("Translation missing: " + culture + " " + name)
                node.find("value").text = value
            atomic_save(tree, path)
        item = {"culture": culture, "machine_completed_phrases": len(sources), "preserved_memory": len(memory),
                "placeholder_segment_fallbacks": constrained, "seconds": round(time.monotonic() - started, 2)}
        receipt["cultures"] = [row for row in receipt["cultures"] if row["culture"] != culture] + [item]
        (args.cache / "completion-receipt.json").write_text(json.dumps(receipt, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        print(json.dumps({"completed": culture, "seconds": item["seconds"]}, ensure_ascii=True), flush=True)

if __name__ == "__main__":
    main()
