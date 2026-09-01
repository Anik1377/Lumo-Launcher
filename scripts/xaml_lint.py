#!/usr/bin/env python3
"""
xaml_lint — structural checks for Lumo's WPF windows (v2.6.0-alpha.5 rebuild).

The workspace resets have eaten this script twice; this is the canonical
version. It parses every .xaml under src/Lumo and enforces the rules that
actually bit us before:

  1. XML well-formedness (a stray & breaks InitializeComponent at runtime).
  2. Duplicate x:Key inside one ResourceDictionary scope.
  3. Duplicate x:Name — with WPF's REAL namescope rule: each ControlTemplate
     (and each DataTemplate) root starts a new namescope, so the same "ThumbBd"
     may repeat across templates but must be unique within one template /
     one window body.
  4. Event handler attributes (Click="X" …) must have a matching method in the
     sibling .xaml.cs (cheap regex — catches renamed handlers).
  5. {x:Reference Name} must point at a name that exists somewhere in the file.

Exit code 0 = clean; 1 = violations printed.
"""
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1] / "src" / "Lumo"
XAML_FILES = sorted(ROOT.rglob("*.xaml"))

X_NS = "http://schemas.microsoft.com/winfx/2006/xaml"

TEMPLATE_TAGS = {"ControlTemplate", "DataTemplate", "HierarchicalDataTemplate", "ItemsPanelTemplate"}

HANDLER_ATTRS = {
    "Click", "Checked", "Unchecked", "MouseLeftButtonDown", "MouseDoubleClick",
    "TextChanged", "KeyDown", "PreviewKeyDown", "ScrollChanged", "GotKeyboardFocus",
    "LostKeyboardFocus", "Loaded", "Closing", "Closed", "SelectionChanged",
    "MouseDown", "MouseUp", "ValueChanged", "Drop", "MouseEnter", "MouseLeave",
}


def local(tag: str) -> str:
    return tag.split("}", 1)[-1]


def check_file(path: Path) -> list[str]:
    problems: list[str] = []
    rel = path.relative_to(ROOT)
    try:
        tree = ET.parse(path)
    except ET.ParseError as e:
        return [f"{rel}: XML parse error: {e}"]

    root = tree.getroot()
    parent_map = {c: p for p in root.iter() for c in p}

    def scope(el):
        p = el
        while p is not None:
            if local(p.tag) in TEMPLATE_TAGS:
                return ("template", id(p))
            p = parent_map.get(p)
        return ("window", id(root))

    # ---- 2. duplicate x:Key inside one resource scope (the parent dictionary)
    key_counts: dict[tuple, list[str]] = {}
    for el in root.iter():
        key = el.get(f"{{{X_NS}}}Key")
        if key is not None:
            p = parent_map.get(el)
            dict_scope = ("dict", id(p if p is not None else root))
            key_counts.setdefault(dict_scope, []).append(key)
    for keys in key_counts.values():
        for k in sorted({k for k in keys if keys.count(k) > 1}):
            problems.append(f'{rel}: duplicate x:Key "{k}" in one resource scope')

    # ---- 3. duplicate x:Name per namescope (each template root = new scope)
    name_scopes: dict[tuple, dict[str, str]] = {}
    for el in root.iter():
        nm = el.get(f"{{{X_NS}}}Name") or el.get("Name")
        if nm is None:
            continue
        # WPF ignores plain Name= on non-FrameworkElements (e.g. Trigger source
        # properties) — only elements that are actually named objects count.
        sc = scope(el)
        names = name_scopes.setdefault(sc, {})
        if nm in names:
            problems.append(f'{rel}: duplicate x:Name "{nm}" inside one namescope ({sc[0]})')
        else:
            names[nm] = local(el.tag)

    # ---- 5. x:Reference targets exist
    all_names = {n for names in name_scopes.values() for n in names}
    for el in root.iter():
        for attr_val in el.attrib.values():
            if not attr_val:
                continue
            for m in re.finditer(r"\{x:Reference\s+(\w+)", attr_val):
                if m.group(1) not in all_names:
                    problems.append(f'{rel}: x:Reference to unknown name "{m.group(1)}"')

    # ---- 4. event handlers exist in the code-behind
    codebehind = path.with_suffix(".xaml.cs")
    code = codebehind.read_text(encoding="utf-8") if codebehind.exists() else ""
    if code:
        for el in root.iter():
            for attr, val in el.attrib.items():
                aname = local(attr)
                if aname in HANDLER_ATTRS and val and "{" not in val:
                    if not re.search(rf"\b(void|async)\s+{re.escape(val)}\s*\(", code):
                        problems.append(f'{rel}: handler "{val}" ({aname}) missing in {codebehind.name}')

    return problems


def main() -> int:
    all_problems: list[str] = []
    for f in XAML_FILES:
        all_problems += check_file(f)

    print(f"xaml_lint: {len(XAML_FILES)} files checked")
    if all_problems:
        for p in all_problems:
            print("  ✗", p)
        print(f"FAILED — {len(all_problems)} problem(s)")
        return 1
    print("clean")
    return 0


if __name__ == "__main__":
    sys.exit(main())
