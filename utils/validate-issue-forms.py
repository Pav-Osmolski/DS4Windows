"""Validate GitHub issue-form structure without building the Windows app."""
from pathlib import Path
import re
import sys

import yaml


class UniqueLoader(yaml.SafeLoader):
    pass


def unique_mapping(loader, node):
    result = {}
    for key_node, value_node in node.value:
        key = loader.construct_object(key_node)
        if key in result:
            raise ValueError(f"Duplicate YAML key: {key}")
        result[key] = loader.construct_object(value_node)
    return result


UniqueLoader.add_constructor(yaml.resolver.BaseResolver.DEFAULT_MAPPING_TAG, unique_mapping)


def require(condition, message):
    if not condition:
        raise ValueError(message)


def text(value):
    return isinstance(value, str) and bool(value.strip())


def validate_form(data):
    require(isinstance(data, dict), "Form must be a mapping")
    require(text(data.get("name")) and text(data.get("description")), "Name and description are required")
    for key in ("labels", "assignees"):
        if key in data:
            require(isinstance(data[key], list) and all(text(x) for x in data[key]), f"{key} must contain strings")
    require(isinstance(data.get("body"), list) and data["body"], "Body must contain fields")
    ids = set()
    inputs = 0
    for field in data["body"]:
        require(isinstance(field, dict), "Field must be a mapping")
        kind = field.get("type")
        require(kind in ("markdown", "input", "textarea", "dropdown", "checkboxes"), f"Unsupported field type: {kind}")
        attrs = field.get("attributes")
        require(isinstance(attrs, dict), "Field attributes are required")
        if "id" in field:
            identifier = field["id"]
            require(text(identifier) and re.fullmatch(r"[A-Za-z0-9_-]+", identifier), "Invalid field ID")
            require(identifier not in ids, f"Duplicate field ID: {identifier}")
            ids.add(identifier)
        validation = field.get("validations", {})
        require(isinstance(validation, dict), "Validations must be a mapping")
        if "required" in validation:
            require(type(validation["required"]) is bool, "Required must be a boolean")
        if kind == "markdown":
            require(text(attrs.get("value")), "Markdown value is required")
            continue
        inputs += 1
        require(text(attrs.get("label")), "Input label is required")
        if kind in ("dropdown", "checkboxes"):
            options = attrs.get("options")
            require(isinstance(options, list) and options, "Options must be a nonempty list")
            if kind == "dropdown":
                require(all(text(x) for x in options), "Dropdown options must be strings")
                require(len(options) == len(set(options)), "Duplicate dropdown options")
                if "multiple" in attrs:
                    require(type(attrs["multiple"]) is bool, "Multiple must be a boolean")
            else:
                for option in options:
                    require(isinstance(option, dict) and text(option.get("label")), "Checkbox label is required")
                    if "required" in option:
                        require(type(option["required"]) is bool, "Checkbox required must be a boolean")
    require(inputs > 0, "Form must have an input field")


def validate_config(data):
    require(isinstance(data, dict), "Chooser must be a mapping")
    if "blank_issues_enabled" in data:
        require(type(data["blank_issues_enabled"]) is bool, "Blank issues setting must be a boolean")
    links = data.get("contact_links", [])
    require(isinstance(links, list), "Contact links must be a list")
    for link in links:
        require(isinstance(link, dict) and all(text(link.get(k)) for k in ("name", "url", "about")), "Contact link needs name, URL and description")
        require(link["url"].startswith("https://"), "Contact link must use HTTPS")


def main():
    folder = Path(__file__).resolve().parents[1] / ".github" / "ISSUE_TEMPLATE"
    paths = sorted(p for p in folder.iterdir() if p.suffix in (".yml", ".yaml"))
    require(paths, "No issue forms found")
    names = set()
    for path in paths:
        try:
            data = yaml.load(path.read_text(encoding="utf-8"), Loader=UniqueLoader)
            if path.stem == "config":
                validate_config(data)
            else:
                validate_form(data)
                require(data["name"] not in names, "Duplicate form name")
                names.add(data["name"])
        except (ValueError, yaml.YAMLError) as exc:
            raise ValueError(f"{path.name}: {exc}") from exc
    print(f"Validated {len(names)} issue forms and chooser configuration.")


if __name__ == "__main__":
    try:
        main()
    except ValueError as exc:
        print(exc, file=sys.stderr)
        sys.exit(1)
