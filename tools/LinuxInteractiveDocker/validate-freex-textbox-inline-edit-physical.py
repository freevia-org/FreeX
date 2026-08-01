#!/usr/bin/env python3
import json
import sys
from pathlib import Path

from jsonschema import validate


def load_json(path: Path) -> object:
    with path.open(encoding="utf-8") as source:
        return json.load(source)


def main(arguments: list[str]) -> int:
    if len(arguments) != 3:
        print(f"Usage: {arguments[0]} SCHEMA_PATH MANIFEST_PATH", file=sys.stderr)
        return 2

    schema = load_json(Path(arguments[1]))
    manifest = load_json(Path(arguments[2]))
    validate(instance=manifest, schema=schema)
    print("FreeX TextBox inline-edit manifest JSON Schema validation passed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
