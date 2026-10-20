#!/usr/bin/env python3
"""Static checks only; not a replacement for a real Windows/WPF build."""
from pathlib import Path
import xml.etree.ElementTree as ET
import re
import sys

root = Path(__file__).resolve().parents[1]
problems = []
ns = {"x": "http://schemas.microsoft.com/winfx/2006/xaml"}
for name in ("App.xaml", "MainWindow.xaml", "SettingsWindow.xaml", "Themes/Midnight.xaml"):
    try:
        tree = ET.parse(root / name)
    except Exception as exc:
        problems.append(f"{name}: not well-formed XML: {exc}")
        continue
    # XAML templates create independent namescopes: x:Name may repeat in two
    # different ControlTemplates. Only detect duplicates inside one scope.
    scope_names = {}
    def walk(element, scope):
        tag = element.tag.rsplit("}", 1)[-1]
        if tag in ("ControlTemplate", "DataTemplate", "ItemsPanelTemplate", "HierarchicalDataTemplate"):
            scope = id(element)
        key = element.attrib.get(f"{{{ns['x']}}}Name")
        if key:
            seen = scope_names.setdefault(scope, set())
            if key in seen:
                problems.append(f"{name}: repeated x:Name within a single namescope: {key}")
            seen.add(key)
        for child in element:
            walk(child, scope)
    walk(tree.getroot(), id(tree.getroot()))

# Event handler declarations must exist in the associated code-behind.
for xaml, cs in (("MainWindow.xaml", "MainWindow.xaml.cs"), ("SettingsWindow.xaml", "SettingsWindow.xaml.cs"), ("App.xaml", "App.xaml.cs")):
    source = (root / xaml).read_text(encoding="utf-8")
    code = (root / cs).read_text(encoding="utf-8")
    events = ("Click", "Loaded", "Closed", "Closing", "MouseEnter", "MouseLeave", "MouseLeftButtonDown",
              "PreviewMouseLeftButtonDown", "PreviewMouseLeftButtonUp", "PreviewMouseWheel", "ValueChanged",
              "SelectionChanged", "Checked", "Unchecked", "LostMouseCapture", "LocationChanged", "SizeChanged")
    for event in events:
        handlers = re.findall(rf'\b{event}="([A-Za-z_][\w]*)"', source)
        for handler in handlers:
            if not re.search(rf'\b{re.escape(handler)}\s*\(', code):
                problems.append(f"{xaml}: handler {handler} missing from {cs}")

main = (root / "MainWindow.xaml").read_text(encoding="utf-8")
code = (root / "MainWindow.xaml.cs").read_text(encoding="utf-8")
required = ("CompactRepeatBadge", "RepeatButton", "SyncRepeatVisualState", "_repeatRequested", "_localRepeatEnabled")
for item in required:
    if item not in main and item not in code:
        problems.append(f"Missing repeat UI symbol: {item}")

# Asset entries required in csproj (includes resource wildcard).
proj = (root / "YMusicMini.csproj").read_text(encoding="utf-8")
if r"Assets\ButtonStyles\**\*.png" not in proj:
    problems.append("Button PNG resources not included in project")
for path in (root / "Assets/ButtonStyles").glob("*/Play.png"):
    for fname in ("Pause.png", "Previous.png", "Next.png"):
        if not (path.parent / fname).is_file():
            problems.append(f"{path.parent.name}: missing {fname}")

if problems:
    for error in problems:
        print("FAIL:", error)
    sys.exit(1)
print("OK: static XML, handler, repeat state and resource checks")
print("NOTE: Windows SDK + WPF compilation and runtime tests are still required.")
