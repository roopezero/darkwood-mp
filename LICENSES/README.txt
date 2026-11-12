Licenses of the software in the DarkwoodMP zips
================================================

DarkwoodMP itself (BepInEx/plugins/DarkwoodMP/DarkwoodMP.dll) is under the
MIT license: DarkwoodMP.txt. Source: https://github.com/roopezero/darkwood-mp

The zips also contain BepInEx 5.4.23.2 as released at
https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.2
unchanged, except that in the Linux zip run_bepinex.sh has its
executable_name set to Darkwood.x86_64.
It is made of these projects:

  BepInEx (BepInEx/core/BepInEx*.dll, run_bepinex.sh)    MIT        BepInEx.txt
    https://github.com/BepInEx/BepInEx
  UnityDoorstop 4.3.0 (winhttp.dll, doorstop_config.ini,  LGPL-2.1   Doorstop.txt
    libdoorstop.so)
    Source: https://github.com/NeighTools/UnityDoorstop/tree/v4.3.0
  HarmonyX (BepInEx/core/0Harmony*.dll, HarmonyXInterop)  MIT        HarmonyX.txt
    https://github.com/BepInEx/HarmonyX
  MonoMod (BepInEx/core/MonoMod.*.dll)                    MIT        MonoMod.txt
    https://github.com/MonoMod/MonoMod
  Mono.Cecil (BepInEx/core/Mono.Cecil*.dll)               MIT        Mono.Cecil.txt
    https://github.com/jbevain/cecil

UnityDoorstop is distributed unmodified under the GNU Lesser General Public
License 2.1; its full source code is at the link above. You may replace
winhttp.dll or libdoorstop.so with your own build of it.

DarkwoodMP is a fan-made mod, not made or supported by Acid Wizard Studio.
Darkwood and its content belong to Acid Wizard Studio and are not included.
