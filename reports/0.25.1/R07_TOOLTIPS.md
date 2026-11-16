# R07: item and map descriptions the mod adds

Made by `tools/gen_tooltips.py` from a dump of the game's data (`tools/run-test.sh tooltips`). The mod shows these
only where the game has no description; it never replaces one the game has. The text is English in every language;
item and location names in it are the game's own, in the player's language.

"Draft: yes" means part of the line was written by hand (what the thing is) and needs a review. "Draft: no" means the
whole line comes from the game's data: damage, magazine, ammunition, armour, effects, slots, what it's used to make,
what a map marks. To change a line, edit `ABOUT` or `LOC_ABOUT` in `tools/gen_tooltips.py` and run it again.

The map's locations come from the world the dump was taken in (the test's copy of profile 1, chapter 1). `LOC_ABOUT`
also has lines for some chapter 2 locations, keyed by their scene names; whether those are the names the chapter 2
map uses hasn't been checked, so they aren't listed below until a dump from a chapter 2 world has them.

## Items (100)

| Key | Name | Text | Draft |
|---|---|---|---|
| `acid` | Acid pill | A small pill. Whatever is in it, the world won't look the same for a while. For 20 minutes I take less damage, I'm faster and I tire less. Cookable. | yes |
| `ammo_clip_mediumCal` | Medium caliber magazine | A full magazine of medium caliber rounds. Used in: Assault rifle. | yes |
| `ammo_clip_smallCal` | Small caliber magazine | A full magazine of small caliber rounds. Used in: Pistol and Submachine gun. | yes |
| `ammo_single_mediumCal` | Medium caliber bullet | A single medium caliber round. Used in: Hunting rifle. | yes |
| `ammo_single_pellet` | Pellet gun ammo | Small lead pellets. Used in: Pellet gun and Pellet rifle. | yes |
| `ammo_single_shotgun` | Shotgun shells | Shotgun shells. Used in: Double barrel shotgun, Pump action shotgun and Single shot shotgun. | yes |
| `antler` | Antlers | A deer's antlers. | yes |
| `armor_light` | Light armor | Light armour. It takes some of the blows for me. Worn, it takes 25% off the damage I take. | yes |
| `axe_edge` | Axe blade | The head of an axe, without a handle. Used to make Axe. | yes |
| `bark_01` | Tree bark | A strip of tree bark. | yes |
| `battery9v` | 9v battery | A 9 volt battery. Used in: Flashlight and Military flashlight. | yes |
| `blanket_sister` | Warm blanket | A thick, warm blanket. | yes |
| `bone` | Bone | A bone, picked clean. | yes |
| `book` | Book | An old book with swollen pages. | yes |
| `cable` | Cable | A length of electric cable. | yes |
| `cassette` | Cassette tape | A cassette tape. | yes |
| `chainTrap` | Chain trap | A trap made of chains, for anything that walks into it. | yes |
| `chicken_egg` | Chicken egg | An ordinary chicken egg. For 60 seconds I tire less. | yes |
| `chicken_head` | Chicken head | The head of a chicken. | yes |
| `chitin` | Shell | A hard piece of shell from something that crawled. Used to make Light armor. | yes |
| `dead_rat` | Dead rat | A dead rat. A poor meal, but a meal. Heals 60 over 60 seconds. Cookable. | yes |
| `electronics` | Electronic parts | Wires, chips and bits of circuit boards. Used to make Watch. | yes |
| `firecraft_boltHandle` | Bolt handle | The bolt handle of a rifle. Used to make Hunting rifle. | yes |
| `firecraft_handgunFrame` | Handgun frame | The frame of a handgun. Used to make Pistol. | yes |
| `firecraft_longBarrel` | Long barrel | A long gun barrel. Used to make Hunting rifle, Double barrel shotgun, Pump action shotgun and Single shot shotgun. | yes |
| `firecraft_tubularMagazine` | Tubular magazine | A tube magazine for a shotgun. Used to make Pump action shotgun. | yes |
| `firecraft_weaponParts` | Weapon parts | Assorted gun parts: springs, pins and screws. Used to make Pistol, Hunting rifle, Double barrel shotgun, Pump action shotgun and Single shot shotgun. | yes |
| `fish` | Fish | A fish. Still fresh. Heals 51 over 60 seconds. For 60 seconds I tire less. Cookable. | yes |
| `flare` | Flare | Burns bright red for a while once lit. Throwable. | yes |
| `flashlight` | Flashlight | A flashlight. | yes |
| `flashlight_02` | Military flashlight | A sturdy military flashlight. | yes |
| `hammer` | Hammer | A heavy hammer. | yes |
| `healthUpgrade` | Red potion | A thick red liquid. Drinking it should make me sturdier for good. | yes |
| `home_apple` | Apple | An apple. | yes |
| `home_chemical_bottle_01` | Detergent | A bottle of detergent. | yes |
| `home_chemical_bottle_02` | Detergent | A bottle of detergent. | yes |
| `home_chemical_spray_01` | Spray can | A spray can. There's still something in it. | yes |
| `home_clothes` | Clothes | Some old clothes. | yes |
| `home_money` | Money | Some money. Out here it buys little. | yes |
| `home_pear` | Pear | A pear. | yes |
| `home_scissor` | Scissors | A pair of scissors. | yes |
| `home_shirt` | Sweater | A warm sweater. | yes |
| `home_towel` | Towel | A towel. | yes |
| `hotbarPlus_01` | Hotbar upgrade | A belt with more pouches. Using it makes my hotbar bigger, up to 4 times. | yes |
| `humanArm_01` | Arm | A human arm. | yes |
| `humanEar_01` | Ear | A human ear. | yes |
| `humanFinger_01` | Finger | A human finger. | yes |
| `humanLeg_01` | Leg | A human leg. | yes |
| `ice_01` | Ice | A piece of ice. It doesn't melt. For 30 seconds I tire less. | yes |
| `invPlus_01` | Inventory upgrade | A bigger bag. Using it makes my backpack bigger, up to 3 times. | yes |
| `junk` | Scrap metal | Bent pieces of scrap metal. Used to make Exploding barrel, Bear trap, Chain trap, Gas tank and 8 more. | yes |
| `kupaWilka` | The Wolf's gift | Something the Wolf left for me. | yes |
| `lifePotion` | Embryo | A twitching embryo. It looks alive. Heals 900 over 3 seconds. Stops bleeding. Cures poison. Cookable. | yes |
| `lockpick` | Lockpick | With a steady hand I can open locks with it. | yes |
| `map` | Map | My map of the area. | yes |
| `map_borderBurned` | Map | A hand-drawn map. It marks Burned cottage. | yes |
| `map_doctorEntrance` | Map | A hand-drawn map. | yes |
| `map_pigShed` | Map | A hand-drawn map. It marks Pig shed. | yes |
| `map_radioTower` | Map | A hand-drawn map. It marks Radio tower. | yes |
| `map_villageTree` | Map | A hand-drawn map. It marks Village. | yes |
| `matchstick` | Matchstick | A single matchstick. Throwable. Used to make Molotov cocktail and Torch. | yes |
| `molotov` | Molotov cocktail | A bottle of fuel with a rag in its neck. It bursts into flames where it lands. Throwable. | yes |
| `mushroom_rotten` | Rotten mushroom | A rotten mushroom. I wouldn't eat it. Used to make Gas bottle and Gas tank. | yes |
| `nail` | Nail | Nails. I can always use more of them. Used to make Pellet gun ammo, Pitchfork, Board with nails, Sickle and Workbench upgrade. | yes |
| `pigHead` | Pig head | The head of a pig. | yes |
| `pills` | Pill | A pill of unknown origin. For 60 seconds I tire less. | yes |
| `pincers` | Pliers | A pair of pliers. Used to make Workbench upgrade. | yes |
| `piotrek_parts_1` | Battery | A car battery. | yes |
| `piotrek_parts_2` | Fan belt | A rubber fan belt from a car engine. | yes |
| `piotrek_parts_3` | Tire | A car tire. | yes |
| `piotrek_parts_4` | Exhaust pipe | A car's exhaust pipe. | yes |
| `piotrek_parts_5` | Rearview mirror | A car's rearview mirror. | yes |
| `piotrek_parts_6` | Steering wheel | A car's steering wheel. | yes |
| `pipe_metal` | Metal pipe | A length of metal pipe. Used to make Single-shot pistol, Homemade shotgun and Workbench upgrade. | yes |
| `pitchfork` | Pitchfork | A pitchfork. It keeps things at a distance. Damage 14. | yes |
| `playerUpgradeIngredient` | Odd stone | A strange stone, warm to the touch. Someone in the woods may know what it's for. Used to make Red potion and Gray potion. | yes |
| `potato_01` | Potato | A raw potato. For 30 seconds I tire less. | yes |
| `rag` | Rag | A dirty rag. Used to make Light armor, Bandages, Lantern, Molotov cocktail and Torch. | yes |
| `ring2` | Ring | A ring. | yes |
| `ring_wedding` | Wedding ring | A wedding ring. | yes |
| `rope` | Rope | A length of rope. Used to make Hotbar upgrade and Inventory upgrade. | yes |
| `shiny_rock` | Shiny stone | A stone that shines in the dark. | yes |
| `shiny_rock_cross` | A shiny object | Something shiny. It catches the light. | yes |
| `shovel_edge` | Shovel blade | The blade of a shovel, without a handle. Used to make Pitchfork and Shovel. | yes |
| `spring` | Spring | A metal spring. | yes |
| `staminaUpgrade` | Gray potion | A cloudy gray liquid. Drinking it should let me run longer for good. | yes |
| `stone_weird_01` | Weird stone | A stone with an odd shape. | yes |
| `tape` | Tape | A roll of tape. Used to make Axe, Pitchfork, Shovel, Pistol and 4 more. | yes |
| `toolbox` | Toolbox | A toolbox with a few tools left in it. Used to make Workbench upgrade. | yes |
| `torch` | Torch | A burning torch. It sets things on fire. Damage 10. Gives light. | yes |
| `toy_rocket` | Toy | A child's toy rocket. | yes |
| `violin` | Violin | An old violin. Someone must miss it. | yes |
| `watch` | Watch | A wristwatch. It still works. | yes |
| `weapon_rifle_02_boltAction` | Hunting rifle | A bolt-action hunting rifle. Slow to work, but it hits hard and far. Damage 70, holds 1. Takes Medium caliber bullet. | yes |
| `weapon_shotgun_01_breakAction_Double` | Double barrel shotgun | A double-barrelled shotgun. Two shots before reloading. Damage 10 per pellet, 15 pellets a shot, holds 2. Takes Shotgun shells. | yes |
| `weapon_shotgun_02_pumpAction` | Pump action shotgun | A pump-action shotgun. Damage 10 per pellet, 15 pellets a shot, holds 8. Takes Shotgun shells. | yes |
| `weapon_shotgun_03_singleShot` | Single shot shotgun | A single-shot shotgun. One shell at a time. Damage 10 per pellet, 15 pellets a shot, holds 1. Takes Shotgun shells. | yes |
| `welder` | Welder | A small welder. Used to make Workbench upgrade. | yes |
| `wire` | Wire | A roll of wire. Used to make Lockpick, Watch, Pellet gun, Pellet rifle and Workbench upgrade. | yes |
| `wood` | Board | A wooden board. Used to make Board with nails, Toolbox, Torch, Pellet gun and 2 more. | yes |

## Map locations (39)

| Element | Name | Text | Draft |
|---|---|---|---|
| `med_cottageTrailer_mapIcon` | Wedding | A trailer in the meadow, decked out for a wedding. | yes |
| `landmark_brokenTree_01` | Broken tree | A huge tree, split down the middle. Easy to find my way by. | yes |
| `landmark_antiTankGun_01` | Anti-tank gun | An old anti-tank gun, rusting where it was left. | yes |
| `mini_bodies_01` | Corpses | Bodies, left where they fell. | yes |
| `mini_mutated_roots_02` | ? | Something grows here that shouldn't. | yes |
| `mini_mutated_roots_01` | ? | Something grows here that shouldn't. | yes |
| `mini_tent_camp_01` | Camp | An abandoned camp. Worth searching. | yes |
| `landmark_brokenTree_02` | Broken tree | A huge tree, split down the middle. Easy to find my way by. | yes |
| `landmark_shrine_02` | Shrine | A small wayside shrine. | yes |
| `landmark_brokenTractor_01` | Broken tractor | A tractor left to rot in a field. | yes |
| `med_wolfman_camp_01` | Wolf's camp | The Wolf's camp, deep in the trees. | yes |
| `landmark_shrine_01` | Shrine | A small wayside shrine. | yes |
| `landmark_brokenTruck_01` | Broken truck | A truck that never made it out of the woods. | yes |
| `mini_tent_camp_02` | Camp | An abandoned camp. Worth searching. | yes |
| `landmark_hole_01` | Giant hole | A hole in the ground, too deep to see the bottom. | yes |
| `mini_tank_wreck_02` | Tank wreck | The burned-out wreck of a tank. | yes |
| `mini_tank_wreck_01` | Tank wreck | The burned-out wreck of a tank. | yes |
| `med_wolfman_camp_02` | Wolf's camp | The Wolf's camp, deep in the trees. | yes |
| `med_musicianHideout_01` | Silo | An old silo. Someone has made a home in it. | yes |
| `med_bunker_enter_01` | Underground entrance | A way down under the ground. | yes |
| `big_pigsheds_01` | Pig shed | Old pig sheds. Something still lives in them. | yes |
| `big_piotrek_01` | Piotrek's house | Piotrek's house and farm. | yes |
| `big_hunter_01` | Hunter's house | A hunter's house at the edge of the forest. | yes |
| `big_burned_houses_01` | Burned houses | A few houses, burned to the ground. | yes |
| `big_church_ruins_01` | Church ruins | What is left of a church. | yes |
| `big_farm_02` | Silent Forest hideout | An old farmhouse in the Silent Forest. Somewhere to hold out through the night. | yes |
| `big_hideout_03` | Old Woods hideout | A hideout in the Old Woods. Somewhere to hold out through the night. | yes |
| `med_musican_house_01` | Creepy house | A house I'd rather not go back to. | yes |
| `med_cottage_tree_01` | Dry Meadow hideout | A cottage under a big tree. Somewhere to hold out through the night. | yes |
| `sub_border_road_01` | Road | An old road at the edge of the woods. | yes |
| `big_trainWreck_01` | Train wreck | A train that came off its rails. | yes |
| `med_wolfmanHideout_01` | Barn ruins | The ruins of an old barn. | yes |
| `sub_gate_doctorEntrance_01` | Road to the Doctor's house | The road that leads to the doctor's house. | yes |
| `sub_burned_house_small_01` | Burned house | A small house, burned out. | yes |
| `sub_border_gate_02_bridge` | Bridge | A bridge over the water. | yes |
| `sub_border_cottage_01` | Cottage | A cottage at the edge of the forest. | yes |
| `sub_gate_meadowForest_01` | Entrance to Silent Forest | The way from the meadow into the Silent Forest. | yes |
| `sub_border_river_01` | Brook | A brook at the edge of the meadow. | yes |
| `sub_gate_villageEntrance_01` | Road to the village | The road to the village. | yes |

## Skipped items (147)

| Key | Name | Why |
|---|---|---|
| `aw_artur_drawings` | Scribbles | journal item |
| `aw_grzegorz` | Grzegorz | journal item |
| `aw_gutek` | Family photo | journal item |
| `aw_ivanka` | Ivanka | journal item |
| `aw_karolina` | Karolina | journal item |
| `aw_killerfriend` | Killerfriend | journal item |
| `aw_kuba` | Drawings | journal item |
| `aw_maciek` | Maciek | journal item |
| `aw_marcin` | Marcin | journal item |
| `aw_mazyrun` | Mazyrun | journal item |
| `aw_mugelisica` | Mugetron and Lisica | journal item |
| `aw_mugelisica_2` | Mugetron and Lisica | journal item |
| `aw_nullwinter` | Null Winter | journal item |
| `aw_piotrek` | Piotrek | journal item |
| `book_wiedza_lekarska_01` | My medical book | journal item |
| `book_wiedza_lekarska_02` | Doctor's notes | journal item |
| `book_wiedza_lekarska_02_burned` | Burned book | journal item |
| `box_church_drawings` | Drawings from the box | journal item |
| `box_church_escaped` | Strange box | journal item |
| `box_church_key` | Twisted key | journal item |
| `box_church_killedChomper` | Strange box | journal item |
| `bullet` | Bullet | journal item |
| `chemical_bottle_01` | chemical_bottle_01_name | no name (dev item) |
| `cigarettes_klubowe` | Pack of cigarettes | journal item |
| `cigarettes_klubowe2` | Pack of cigarettes | journal item |
| `cowbell_01` | Rusty iron cowbell | journal item |
| `cowbell_02` | Iron cowbell | journal item |
| `cross_01_c` | Cross | journal item |
| `doctor_photo` | Destroyed photo | journal item |
| `doctor_wife_photo` | Torn photo | journal item |
| `drewniana_kurka_blood` | Bloodstained plastic chick | journal item |
| `drewniane_kurki` | Plastic chickens | journal item |
| `exp_epickiEXP` | exp_epickiEXP_name | no name (dev item) |
| `exp_piskle` | ? | no name (dev item) |
| `fartPillow` | fartPillow_name | no name (dev item) |
| `fists` | fists_name | no name (dev item) |
| `flashlight_rechargable` | flashlight_rechargable_name | no name (dev item) |
| `grannyPainting_01` | Painting from 1972 | journal item |
| `grannyPainting_02` | Painting from 1973 | journal item |
| `grannyPainting_03` | Painting from 1978 | journal item |
| `grannyPainting_04` | Painting from 1979 | journal item |
| `grannyPainting_05` | Painting from 1975 | journal item |
| `hunter_photo_1` | Photo | journal item |
| `jar_roadToHome` | Jar with a sample | journal item |
| `journal_clone` | Weird book | journal item |
| `kartkiEkspedycja` | Ration stamps | journal item |
| `kasetaZniszczona` | Old cassette | journal item |
| `kebab` | kebab_name | no name (dev item) |
| `key_21` | key_21_name | journal item |
| `key_baba_sister` | Key covered in chicken feces | journal item |
| `key_big_farm_02_shed` | Chest key | journal item |
| `key_border_burnedCottage` | Key from the burned cottage | journal item |
| `key_bunker_act1` | Key to the underground | journal item |
| `key_doctorHideout` | Key to a train car | journal item |
| `key_epilog_sypialnia` | Bedroom key | journal item |
| `key_epilog_wyjscieZpokoju` | key_epilog_wyjscieZpokoju_name | journal item |
| `key_mushroomGranny_boy_01` | Mushroom Granny key | journal item |
| `key_outside_doctor_01_rozwalonyDom` | Rusty key | journal item |
| `key_outside_village_ch1_cottage01_underground_01` | Cellar key | journal item |
| `key_radioTower_01` | Old key to the radio tower | journal item |
| `key_sawmill` | Sawmill key | journal item |
| `key_treeVillage_mainKey` | Key | journal item |
| `key_village_brother_key_01` | key_village_brother_key_01_name | journal item |
| `key_wolfmanHideout_01` | Key to Wolf's hideout | journal item |
| `klisza` | Photographic film | journal item |
| `lesnyLudek` | Chestnut man | journal item |
| `lotto` | Lottery ticket | journal item |
| `maszynaDoMiesa` | Meat grinder | journal item |
| `medalion_brother` | Dog tag | journal item |
| `medical_tools_01` | medical_tools_01_name | journal item |
| `medical_tools_02` | Medical tools | journal item |
| `noiseLure` | noiseLure_name | no name (dev item) |
| `notatnik_doktora_01_ch2_01` | Doctor's note #1 | journal item |
| `notatnik_doktora_01_ch2_02` | Doctor's note #2 | journal item |
| `notatnik_doktora_01_ch2_03` | Doctor's note #3 | journal item |
| `notatnik_doktora_01_ch2_04` | Doctor's note #4 | journal item |
| `notatnik_doktora_01_ch2_05` | Doctor's note #5 | journal item |
| `notatnik_doktora_01_ch2_06` | Doctor's note #6 | journal item |
| `notatnik_doktora_01_swamp_01` | Notepad | journal item |
| `notatnik_doktora_burnedMap` | Burned page with a map. | journal item |
| `note_burnedHouse_01` | Note from the burned house | journal item |
| `note_elephant_door` | Note | journal item |
| `note_elephant_picture` | Child's drawing | journal item |
| `note_gate_meadowForest_01` | Note | journal item |
| `note_generator` | Generator note | journal item |
| `note_hideout_swamp_01` | Note | journal item |
| `note_journal_bloody` | Bloodied journal | journal item |
| `note_kapelusznik_01` | Torn page from a journal #1 | journal item |
| `note_kapelusznik_02` | Torn page from a journal #2 | journal item |
| `note_kapelusznik_03` | Torn page from a journal #3 | journal item |
| `note_macius_1` | Note - desertion #1 | journal item |
| `note_macius_2` | Note - desertion #2 | journal item |
| `note_mail` | Postcard | journal item |
| `note_mail_2` | note_mail_2_name | journal item |
| `note_nightGiftMushroom_01` | Mushroom note | journal item |
| `note_pigshed_02` | Madman's scribblings. | journal item |
| `note_pigshed_flyer` | Flyer | journal item |
| `note_przyjaciolka_wolf_01` | Note from the Wolf | journal item |
| `note_psalm91` | Scrap of paper | journal item |
| `note_radioTower_02` | Journal from the radio tower #2 | journal item |
| `note_rozdarteOpakowaniePapierosow` | Cigarette pack | journal item |
| `note_shadowarmor_01` | Note about the black shell | journal item |
| `note_signs_01` | A note with a photo of signs | journal item |
| `note_snail_journal_01` | Note | journal item |
| `note_snail_junk_01` | Note | journal item |
| `note_snail_numbers_01` | Note | journal item |
| `note_truck_01` | Note from the truck | journal item |
| `note_wallOfTrees_01` | Note about walls of trees | journal item |
| `note_wiazki_01` | Note about the bundle of roots | journal item |
| `note_wilk_klucz` | Note with a key | journal item |
| `note_wolfman_stolenItems` | note_wolfman_stolenItems_name | journal item |
| `note_xmass` | Christmas card | journal item |
| `photo_burnedCottage_01` | Photo | journal item |
| `photo_burnedCottage_02` | Photo | journal item |
| `photo_doctor` | Photo of the Doctor | journal item |
| `photo_forest_dog` | Photo | journal item |
| `photo_forest_redneck_01` | Photo | journal item |
| `photo_forest_redneck_02` | Photo | journal item |
| `photo_karmiciel` | Photo of the sow | journal item |
| `photo_mushroomGranny` | Photo of a family | journal item |
| `photo_roadToHome` | Photo of a road | journal item |
| `pingpong_01` | Ping-pong ball | journal item |
| `player_epilogue_test_item` | player_epilogue_test_item_name | no name (dev item) |
| `radio_small_01` | Damaged short-wave radio | journal item |
| `radio_small_01c` | Weird short-wave radio | journal item |
| `recipe` | Recipe | its popup names the recipe it teaches |
| `rock_blood` | Bloodstained rock | journal item |
| `rocket_blueprint_01` | Blueprint | journal item |
| `rope_journal` | Bloodstained rope | journal item |
| `screwdriver_epilog` | Large screwdriver | journal item |
| `skrety_01` | Tobacco | journal item |
| `swiecznik_01` | A note with a key attached to it. | journal item |
| `syfon` | Siphon | journal item |
| `syringe_01` | Syringe | journal item |
| `teddyBear` | . | no name (dev item) |
| `termometr` | Thermometer | journal item |
| `tooth` | . | no name (dev item) |
| `twins_church_01` | Medallion | journal item |
| `twins_church_01_dream` | Medallion | journal item |
| `unitra_doctor` | unitra_doctor_name | journal item |
| `uzytek_z_grzybow_01` | Mushroom book | journal item |
| `violin_old` | Violin | journal item |
| `weapon_flamethrower_homeMade` | weapon_flamethrower_homeMade_name | no name (dev item) |
| `weapon_flamethrower_military` | weapon_flamethrower_military_name | no name (dev item) |
| `weapon_shotgun_04_fullAuto_ZePSUTY` | weapon_shotgun_04_fullAuto_ZePSUTY_name | no name (dev item) |
| `wedding_invitation` | Invitation | journal item |
| `wedding_invitation_2` | wedding_invitation_2_name | journal item |
