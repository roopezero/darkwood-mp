"""Map test steps for windows kept up to date while open (World/UiRefresh.cs); same format as map_scenario.py.
The steps are in src/DarkwoodMP/World/MapScenarioUi.cs. Run alone: MAP_SCENARIO=map_scenario_ui.

One player has a window open and gives it no input; the other changes what it shows; the viewer's ui-*-check
reads what the window has drawn (icons, amounts, the recipes' tint, the saw's fuel label) and compares it with the
object's data. Storage slots by index, negative from the end (empty in the test save); "-" is an empty slot.

UI_BEFORE=1 in the environment switches UiRefresh off in both games first, to see what the checks find without it.
"""
import os

STEPS = [
    # Both players at the workbench with it open.
    ("host", "ui-wb-open"),
    ("client", "ui-wb-open"),
    # The host changes a slot; the client's open window shows it. And back.
    ("host", "ui-wb-set", "-1", "nail", "7"),
    ("client", "ui-wb-check", "-1", "nail", "7"),
    ("client", "ui-wb-set", "-1", "nail", "3"),
    ("host", "ui-wb-check", "-1", "nail", "3"),
    # Several changes in quick succession, each way: the window ends on the last.
    ("host", "ui-wb-burst", "-2", "rope", "5"),
    ("client", "ui-wb-check", "-2", "rope", "5"),
    ("client", "ui-wb-burst", "-3", "wire", "4"),
    ("host", "ui-wb-check", "-3", "wire", "4", "-2", "rope", "5"),
    # Ingredients put in by the other player make recipes makeable in the viewer's open window (their red tint
    # goes); taken out again, they're red again.
    ("host", "ui-wb-set", "-4", "wood", "40", "-5", "nail", "40", "-6", "wire", "20"),
    ("client", "ui-wb-check", "-4", "wood", "40", "-5", "nail", "40", "-6", "wire", "20", "--makeable", "1"),
    ("client", "ui-wb-set", "-4", "-", "0", "-5", "-", "0", "-6", "-", "0"),
    ("host", "ui-wb-check", "-4", "-", "0", "-5", "-", "0", "-6", "-", "0"),
    ("client", "ui-wb-check", "-4", "-", "0", "-5", "-", "0", "-6", "-", "0"),
    # Both players change different slots at the same moment (the host slot -7, the client slot -8), each
    # checked in both open windows.
    ("host+client", "ui-wb-race", "AT"),
    ("host", "ui-wb-check", "-7", "bandages", "2", "-8", "wire", "6"),
    ("client", "ui-wb-check", "-7", "bandages", "2", "-8", "wire", "6"),
    # Closed, changed, opened again: the latest state.
    ("client", "ui-wb-close"),
    ("host", "ui-wb-set", "-1", "-", "0", "-2", "rope", "9"),
    ("client", "ui-wb-open"),
    ("client", "ui-wb-check", "-1", "-", "0", "-2", "rope", "9"),
    # Put back as it was.
    ("host", "ui-wb-set", "-2", "-", "0", "-3", "-", "0", "-7", "-", "0", "-8", "-", "0"),
    ("client", "ui-wb-check", "-1", "-", "0", "-2", "-", "0", "-3", "-", "0", "-7", "-", "0", "-8", "-", "0"),
    ("host", "ui-wb-check", "-1", "-", "0", "-2", "-", "0", "-3", "-", "0", "-7", "-", "0", "-8", "-", "0"),
    ("client", "ui-wb-close"),
    ("host", "ui-wb-close"),

    # The saw: the client has it open, the host pours fuel twice; the label follows.
    ("client", "ui-saw-open", "saw_stationary_01@-480,-160"),
    ("host", "fuel", "saw_stationary_01@-480,-160", "10"),
    ("client", "ui-saw-check", "10"),
    ("host", "fuel", "saw_stationary_01@-480,-160", "5"),
    ("client", "ui-saw-check", "15"),
    ("client", "ui-saw-close"),

    # The trader: his stock shown in the client's trade window while the host restocks him.
    ("client", "ui-trader-show"),
    ("host", "trader-restock"),
    ("client", "ui-trader-check"),
    ("client", "ui-trader-hide"),
]

if os.environ.get("UI_BEFORE"):
    STEPS = [("host", "ui-refresh", "off"), ("client", "ui-refresh", "off")] + STEPS
