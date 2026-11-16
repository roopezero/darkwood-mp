#!/usr/bin/env python3
"""Make src/DarkwoodMP/World/Tooltips.tsv (the mod's item and map descriptions) from the game's data.

    tools/run-test.sh tooltips                          # writes $WORK/tooltips/tooltips_dump.json
    tools/gen_tooltips.py $WORK/tooltips/tooltips_dump.json [--report reports/0.25.1/R07_TOOLTIPS.md]

The dump (World/Tooltips.cs, --dwmp-test-tooltips) has every ItemsDatabase item's InvItem fields, effects and
recipes, and every map element. It holds the game's own text (names), so it stays out of the repo. The report
(with --report) lists every line with the item's English name beside it, for reviewing the drafts.

Each line is made of two parts: what the item is for, written here by hand (ABOUT, LOC_ABOUT), and facts taken
from the item's fields (damage, magazine, ammunition, armour, effects, what it's used to make, what a map marks).
Hand-written text is a draft (flag 1) until the user has reviewed it; a line built only from the data isn't.
Names inside a line are written as {item:TYPE} and {ui:KEY}: the game puts its own name in, in its language.

Only items the game has no description for get one. Journal items (they have their own journal page) and the
game's nameless dev items are listed as "skip".
"""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "src/DarkwoodMP/World/Tooltips.tsv"

# What an item is, in the game's voice: short, plain, often first person. (text, draft)
ABOUT = {
    # _other
    "hammer": "A heavy hammer.",
    "healthUpgrade": "A thick red liquid. Drinking it should make me sturdier for good.",
    "staminaUpgrade": "A cloudy gray liquid. Drinking it should let me run longer for good.",
    "playerUpgradeIngredient": "A strange stone, warm to the touch. Someone in the woods may know what it's for.",
    # ammo
    "ammo_clip_mediumCal": "A full magazine of medium caliber rounds.",
    "ammo_clip_smallCal": "A full magazine of small caliber rounds.",
    "ammo_single_mediumCal": "A single medium caliber round.",
    "ammo_single_pellet": "Small lead pellets.",
    "ammo_single_shotgun": "Shotgun shells.",
    "battery9v": "A 9 volt battery.",
    # consumables
    "acid": "A small pill. Whatever is in it, the world won't look the same for a while.",
    "chicken_egg": "An ordinary chicken egg.",
    "dead_rat": "A dead rat. A poor meal, but a meal.",
    "fish": "A fish. Still fresh.",
    "lifePotion": "A twitching embryo. It looks alive.",
    "pills": "A pill of unknown origin.",
    "potato_01": "A raw potato.",
    # firearm parts and firearms
    "firecraft_boltHandle": "The bolt handle of a rifle.",
    "firecraft_handgunFrame": "The frame of a handgun.",
    "firecraft_longBarrel": "A long gun barrel.",
    "firecraft_tubularMagazine": "A tube magazine for a shotgun.",
    "firecraft_weaponParts": "Assorted gun parts: springs, pins and screws.",
    "weapon_rifle_02_boltAction": "A bolt-action hunting rifle. Slow to work, but it hits hard and far.",
    "weapon_shotgun_01_breakAction_Double": "A double-barrelled shotgun. Two shots before reloading.",
    "weapon_shotgun_02_pumpAction": "A pump-action shotgun.",
    "weapon_shotgun_03_singleShot": "A single-shot shotgun. One shell at a time.",
    # home
    "book": "An old book with swollen pages.",
    "home_apple": "An apple.",
    "home_chemical_bottle_01": "A bottle of detergent.",
    "home_chemical_bottle_02": "A bottle of detergent.",
    "home_chemical_spray_01": "A spray can. There's still something in it.",
    "home_clothes": "Some old clothes.",
    "home_money": "Some money. Out here it buys little.",
    "home_pear": "A pear.",
    "home_scissor": "A pair of scissors.",
    "home_shirt": "A warm sweater.",
    "home_towel": "A towel.",
    # maps
    "map": "My map of the area.",
    "map_borderBurned": "A hand-drawn map.",
    "map_doctorEntrance": "A hand-drawn map.",
    "map_pigShed": "A hand-drawn map.",
    "map_radioTower": "A hand-drawn map.",
    "map_villageTree": "A hand-drawn map.",
    # materials
    "axe_edge": "The head of an axe, without a handle.",
    "chitin": "A hard piece of shell from something that crawled.",
    "electronics": "Wires, chips and bits of circuit boards.",
    "junk": "Bent pieces of scrap metal.",
    "nail": "Nails. I can always use more of them.",
    "pincers": "A pair of pliers.",
    "pipe_metal": "A length of metal pipe.",
    "rag": "A dirty rag.",
    "rope": "A length of rope.",
    "shovel_edge": "The blade of a shovel, without a handle.",
    "spring": "A metal spring.",
    "tape": "A roll of tape.",
    "toolbox": "A toolbox with a few tools left in it.",
    "welder": "A small welder.",
    "wire": "A roll of wire.",
    "wood": "A wooden board.",
    # melee
    "pitchfork": "A pitchfork. It keeps things at a distance.",
    "torch": "A burning torch. It sets things on fire.",
    # misc
    "antler": "A deer's antlers.",
    "bark_01": "A strip of tree bark.",
    "blanket_sister": "A thick, warm blanket.",
    "bone": "A bone, picked clean.",
    "cable": "A length of electric cable.",
    "cassette": "A cassette tape.",
    "chicken_head": "The head of a chicken.",
    "humanArm_01": "A human arm.",
    "humanEar_01": "A human ear.",
    "humanFinger_01": "A human finger.",
    "humanLeg_01": "A human leg.",
    "ice_01": "A piece of ice. It doesn't melt.",
    "kupaWilka": "Something the Wolf left for me.",
    "lockpick": "With a steady hand I can open locks with it.",
    "mushroom_rotten": "A rotten mushroom. I wouldn't eat it.",
    "pigHead": "The head of a pig.",
    "ring2": "A ring.",
    "ring_wedding": "A wedding ring.",
    "shiny_rock": "A stone that shines in the dark.",
    "shiny_rock_cross": "Something shiny. It catches the light.",
    "stone_weird_01": "A stone with an odd shape.",
    "toy_rocket": "A child's toy rocket.",
    # quest items
    "piotrek_parts_1": "A car battery.",
    "piotrek_parts_2": "A rubber fan belt from a car engine.",
    "piotrek_parts_3": "A car tire.",
    "piotrek_parts_4": "A car's exhaust pipe.",
    "piotrek_parts_5": "A car's rearview mirror.",
    "piotrek_parts_6": "A car's steering wheel.",
    "violin": "An old violin. Someone must miss it.",
    # thrown, traps, useable
    "flare": "Burns bright red for a while once lit.",
    "matchstick": "A single matchstick.",
    "molotov": "A bottle of fuel with a rag in its neck. It bursts into flames where it lands.",
    "chainTrap": "A trap made of chains, for anything that walks into it.",
    "armor_light": "Light armour. It takes some of the blows for me.",
    "flashlight": "A flashlight.",
    "flashlight_02": "A sturdy military flashlight.",
    "hotbarPlus_01": "A belt with more pouches.",
    "invPlus_01": "A bigger bag.",
    "watch": "A wristwatch. It still works.",
}

# Items that need no text of their own, with why.
SKIP = {
    "recipe": "its popup names the recipe it teaches",
}

# The map's locations: what's there, as far as a player learns it. All drafts.
LOC_ABOUT = {
    "med_cottage_tree_01": "A cottage under a big tree. Somewhere to hold out through the night.",
    "big_farm_02": "An old farmhouse in the Silent Forest. Somewhere to hold out through the night.",
    "big_hideout_03": "A hideout in the Old Woods. Somewhere to hold out through the night.",
    "med_hideout_04": "A hideout. Somewhere to hold out through the night.",
    "med_hideout_05": "A hideout. Somewhere to hold out through the night.",
    "gridObj_tent_respawn_01": "An abandoned camp. Worth searching.",
    "mini_tent_camp_01": "An abandoned camp. Worth searching.",
    "mini_tent_camp_02": "An abandoned camp. Worth searching.",
    "med_wolfman_camp_01": "The Wolf's camp, deep in the trees.",
    "med_wolfman_camp_02": "The Wolf's camp, deep in the trees.",
    "landmark_antiTankGun_01": "An old anti-tank gun, rusting where it was left.",
    "landmark_brokenTractor_01": "A tractor left to rot in a field.",
    "landmark_brokenTree_01": "A huge tree, split down the middle. Easy to find my way by.",
    "landmark_brokenTree_02": "A huge tree, split down the middle. Easy to find my way by.",
    "landmark_brokenTruck_01": "A truck that never made it out of the woods.",
    "landmark_hole_01": "A hole in the ground, too deep to see the bottom.",
    "landmark_shrine_01": "A small wayside shrine.",
    "landmark_shrine_02": "A small wayside shrine.",
    "big_church_ruins_01": "What is left of a church.",
    "big_burned_houses_01": "A few houses, burned to the ground.",
    "big_hunter_01": "A hunter's house at the edge of the forest.",
    "big_pigsheds_01": "Old pig sheds. Something still lives in them.",
    "big_piotrek_01": "Piotrek's house and farm.",
    "med_bunker_enter_01": "A way down under the ground.",
    "med_butcher_house_01": "A lone house in the woods.",
    "med_musican_house_01": "A house I'd rather not go back to.",
    "mini_bodies_01": "Bodies, left where they fell.",
    "mini_mutated_roots_01": "Something grows here that shouldn't.",
    "mini_mutated_roots_02": "Something grows here that shouldn't.",
    "mini_tank_wreck_01": "The burned-out wreck of a tank.",
    "mini_tank_wreck_02": "The burned-out wreck of a tank.",
    "outside_bunker_underground_02": "A passage under the ground.",
    "outside_bunker_underground_part2_01": "A passage under the ground.",
    "outside_church_underground_01": "Tunnels under the church.",
    "outside_church_underground_02": "Tunnels under the church.",
    "outside_doctor_house_01": "The doctor's house.",
    "med_musicianHideout_01": "An old silo. Someone has made a home in it.",
    "med_larva_01": "Something stirs here.",
    "big_swamp_lake_01": "A wide, still lake in the swamp.",
    "big_swamp_lake_02": "A wide, still lake in the swamp.",
    "med_junkyard_01": "A junkyard. Plenty of scrap to pick through.",
    "med_mushroomGranny_01": "A glade full of mushrooms.",
    "outside_village_ch2_cellar_01": "A cellar under the village.",
    "outside_cottage_snail_01": "A small cottage. Whoever lives here keeps to themselves.",
    "dream_tutorial_00": "My home.",
    "med_cottageTrailer_mapIcon": "A trailer in the meadow, decked out for a wedding.",
    "big_trainWreck_01": "A train that came off its rails.",
    "med_wolfmanHideout_01": "The ruins of an old barn.",
    "sub_border_road_01": "An old road at the edge of the woods.",
    "sub_gate_doctorEntrance_01": "The road that leads to the doctor's house.",
    "sub_burned_house_small_01": "A small house, burned out.",
    "sub_border_gate_02_bridge": "A bridge over the water.",
    "sub_border_cottage_01": "A cottage at the edge of the forest.",
    "sub_gate_meadowForest_01": "The way from the meadow into the Silent Forest.",
    "sub_border_river_01": "A brook at the edge of the meadow.",
    "sub_gate_villageEntrance_01": "The road to the village.",
}
for _i in ("1_01", "1_02", "1_03", "1_04", "1_05", "1_06", "2_01", "2_02", "2_04"):
    LOC_ABOUT["gridObj_cottageRuin_" + _i] = "The ruins of an old cottage. Worth searching."

EFFECTS = {  # CharacterEffectType -> what it does while it lasts
    "nightVision": "I see in the dark", "strength": "I'm stronger", "speed": "I'm faster",
    "endurance": "I tire less", "armor": "I take less damage", "FOV": "I see further", "aim": "my aim is steadier",
    "poison": "I'm poisoned", "bleeding": "I bleed", "protectionFromPoison": "poison can't hurt me",
    "protectionFromGas": "gas can't hurt me", "invulnerability": "nothing can hurt me",
    "invertControls": "my legs don't do what I tell them",
}


def fmt(x):
    x = float(x)
    return str(int(x)) if x == int(x) else ("%.1f" % x).rstrip("0").rstrip(".")


def span(secs):
    return "%s minutes" % fmt(round(secs / 60.0, 1)) if secs >= 120 else "%s seconds" % fmt(secs)


def join(parts):
    return parts[0] if len(parts) == 1 else ", ".join(parts[:-1]) + " and " + parts[-1]


def facts(it, by_type, names):
    """Plain sentences from the item's fields (and, where noted, the game's code). Nothing here is guessed."""
    f = it.get("fields") or {}
    out = []
    if f.get("isFirearm"):
        dmg, pel = f.get("damage", 0), f.get("projectileAmount", 0) or 1
        s = "Damage %s" % fmt(dmg) + (" per pellet, %d pellets a shot" % pel if pel > 1 else "")
        if f.get("clipSize"):
            s += ", holds %d" % f["clipSize"]
        out.append(s + ".")
        if f.get("ammoType") and f["ammoType"] in by_type:
            out.append("Takes {item:%s}." % f["ammoType"])
    elif f.get("isMelee") and f.get("damage"):
        s = "Damage %s" % fmt(f["damage"])
        if f.get("specialDamage", 0) > f["damage"]:
            s += ", %s with a strong attack" % fmt(f["specialDamage"])
        out.append(s + ".")
    if f.get("isThrowable"):
        out.append("Throwable.")
    if f.get("lightEmitter"):
        out.append("Gives light.")
    if f.get("isArmor") and f.get("armorValue"):
        # Player: damage taken x (1 - itemArmorValue).
        out.append("Worn, it takes %s%% off the damage I take." % fmt(round(f["armorValue"] * 100)))
    # InvItemClass: each use is one upgrade, refused after the 4th (hotbar) or 3rd (backpack).
    if f.get("addsHotbarSlot"):
        out.append("Using it makes my hotbar bigger, up to 4 times.")
    if f.get("addsInventorySlot"):
        out.append("Using it makes my backpack bigger, up to 3 times.")
    if f.get("addsPoisonImmunity"):
        out.append("Protects from poison.")
    # What uses this as its ammunition (batteries, ammo).
    users = dedupe(sorted(k for k, o in by_type.items() if (o.get("fields") or {}).get("ammoType") == it["key"] and named(o)), names)
    if users:
        out.append("Used in: %s." % join(["{item:%s}" % u for u in users[:5]]))
    # Effects only for what the player eats or uses from the inventory (the game's "Eat" action and the like).
    if f.get("useable") and f.get("useableText"):
        heal, other, extra = [], {}, []
        for e in it.get("effects") or []:
            t, d = e["type"], e.get("duration") or 0
            if t == "heal" and e.get("modifier") and e.get("interval") and d:
                heal.append((e["modifier"] * d / e["interval"], d))
            elif t in EFFECTS and d:
                other.setdefault(d, []).append(EFFECTS[t])
            if e.get("stopsBleeding") and "Stops bleeding." not in extra:
                extra.append("Stops bleeding.")
            if e.get("stopsPoison") and "Cures poison." not in extra:
                extra.append("Cures poison.")
        for total, d in heal:
            out.append("Heals %s over %s." % (fmt(round(total)), span(d)))
        out.extend(extra)
        for d, what in sorted(other.items()):
            out.append("For %s %s." % (span(d), join(what)))
    if f.get("isExpItem"):
        out.append("Cookable.")
    if f.get("rotten") is False and it.get("rottenItem"):
        out.append("Rots over time.")
    used = dedupe(sorted(u for u in (it.get("usedIn") or []) if u in by_type and named(by_type[u])), names)
    if used:
        shown = ["{item:%s}" % u for u in (used if len(used) <= 5 else used[:4])]
        if len(used) > 5:
            shown.append("%d more" % (len(used) - 4))
        out.append("Used to make %s." % join(shown))
    marks = [l for l, ui in zip(it.get("locations") or [], it.get("locationsUi") or []) if ui and ui != l]
    if marks:
        out.append("It marks %s." % join(["{ui:%s}" % l for l in marks]))
    return out


def dedupe(keys, names):
    """One of each name (the toolbox and torch have several recipes, the workbench upgrades share a name)."""
    seen, out = set(), []
    for k in keys:
        n = names.get(k, k)
        if n not in seen:
            seen.add(n); out.append(k)
    return out


def named(o):
    n = (o.get("name") or "").strip()
    return bool(n) and n != o["key"] + "_name" and re.search(r"\w", n) is not None


def tsv_text(s):
    return s.replace("\t", " ").replace("\n", "\\n")


def main():
    args = sys.argv[1:]
    if not args:
        sys.exit(__doc__)
    report = None
    if "--report" in args:
        i = args.index("--report"); report = Path(args[i + 1]); del args[i:i + 2]
    dump = json.load(open(args[0], encoding="utf-8"))
    by_type = {o["key"]: o for o in dump["items"]}
    names = {o["key"]: (o.get("name") or "").strip() for o in dump["items"]}
    rows, skips, problems = [], [], []
    for k in sorted(by_type):
        o = by_type[k]
        if o.get("gameDesc"):
            continue
        if k in SKIP:
            skips.append((k, SKIP[k])); continue
        if "/journalItems/" in (o.get("path") or ""):
            skips.append((k, "journal item")); continue
        if not named(o):
            skips.append((k, "no name (dev item)")); continue
        about = ABOUT.get(k)
        fs = facts(o, by_type, names)
        if about is None and not fs:
            problems.append(k); continue
        text = " ".join(([about] if about else []) + fs)
        rows.append(("item", k, 1 if about else 0, text, o.get("name", "")))
    for k in sorted(set(ABOUT) - {r[1] for r in rows}):
        print("note: ABOUT has %s, which needs no text (the game has one, or it's skipped)" % k, file=sys.stderr)

    locs, seen = [], set()
    for e in dump["mapElements"]:
        n = e.get("elementName") or ""
        if not e.get("selectable") or e.get("isDeathDrop") or e.get("isWorldChunk") or not n:
            continue
        base = n[:-5] if n.endswith("_done") else n
        if base in seen:
            continue
        seen.add(base)
        if base not in LOC_ABOUT:
            problems.append("map:" + base); continue
        locs.append(("loc", base, 1, LOC_ABOUT[base], e.get("ui", "")))
    if problems:
        sys.exit("no text for: " + ", ".join(problems))

    with open(OUT, "w", encoding="utf-8", newline="\n") as w:
        w.write("# Made by tools/gen_tooltips.py from the game's item data; see World/Tooltips.cs for the format.\n")
        for kind, k, draft, text, _ in rows + locs:
            w.write("%s\t%s\t%d\t%s\n" % (kind, k, draft, tsv_text(text)))
        for k, why in skips:
            w.write("skip\t%s\t%s\n" % (k, why))
    print("%s: %d items, %d locations, %d skipped" % (OUT.relative_to(ROOT), len(rows), len(locs), len(skips)))

    if report:
        ui = {k: o.get("ui", "") for o in dump["mapElements"] for k in [o.get("elementName")]}
        for o in dump["items"]:
            ui.update(zip(o.get("locations") or [], o.get("locationsUi") or []))

        def shown(t):  # the line as an English game shows it
            t = re.sub(r"\{item:([^}]*)\}", lambda m: names.get(m.group(1), m.group(1)), t)
            return re.sub(r"\{ui:([^}]*)\}", lambda m: ui.get(m.group(1)) or m.group(1), t)

        def cell(s):
            return s.replace("|", "\\|").replace("\n", " / ")
        with open(report, "w", encoding="utf-8") as w:
            w.write(REPORT_HEAD)
            w.write("\n## Items (%d)\n\n| Key | Name | Text | Draft |\n|---|---|---|---|\n" % len(rows))
            for _, k, d, t, n in rows:
                w.write("| `%s` | %s | %s | %s |\n" % (k, cell(n), cell(shown(t)), "yes" if d else "no"))
            w.write("\n## Map locations (%d)\n\n| Element | Name | Text | Draft |\n|---|---|---|---|\n" % len(locs))
            for _, k, d, t, n in locs:
                w.write("| `%s` | %s | %s | %s |\n" % (k, cell(n), cell(shown(t)), "yes" if d else "no"))
            w.write("\n## Skipped items (%d)\n\n| Key | Name | Why |\n|---|---|---|\n" % len(skips))
            for k, why in skips:
                w.write("| `%s` | %s | %s |\n" % (k, cell(names.get(k, "")), why))
        print("report:", report)


REPORT_HEAD = """# R07: item and map descriptions the mod adds

Made by `tools/gen_tooltips.py` from a dump of the game's data (`tools/run-test.sh tooltips`). The mod shows these
only where the game has no description; it never replaces one the game has. The text is English in every language;
item and location names in it are the game's own, in the player's language.

"Draft: yes" means part of the line was written by hand (what the thing is) and needs a review. "Draft: no" means the
whole line comes from the game's data: damage, magazine, ammunition, armour, effects, slots, what it's used to make,
what a map marks. To change a line, edit `ABOUT` or `LOC_ABOUT` in `tools/gen_tooltips.py` and run it again.

The map's locations come from the world the dump was taken in (the test's copy of profile 1, chapter 1). `LOC_ABOUT`
also has lines for some chapter 2 locations, keyed by their scene names; whether those are the names the chapter 2
map uses hasn't been checked, so they aren't listed below until a dump from a chapter 2 world has them.
"""

if __name__ == "__main__":
    main()
