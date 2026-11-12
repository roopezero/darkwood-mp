# Save transfer benchmarks

Measured 2026-11-11 on DarkwoodMP 0.20.0 (protocol 25), to decide whether the join-time save transfer's
compression (SaveOffer packing 1: raw Deflate, `CompressionLevel.Optimal`) should change.

## What is sent

- `WorldFiles.Names`: `sav.dat`, `savs.dat`, `savch.dat` from the host's profile folder (or the live snapshot of it),
  back to back, then Deflated as one stream. Nothing else in the folder goes: not `savs.dat_bak`, not
  `darkwoodmp_players/` (per-player characters travel separately). The profile JSON goes uncompressed in SaveOffer.
- The client asks for 256 KiB pieces (`NeedSave`, `WorldShare.PieceSize`), 4 in flight. A `SaveData` message is
  under Steam's 512 KiB send limit, so it is not split again into `SteamPiece`s. Neither transport compresses.
- Integrity: SHA-256 over the profile JSON and every file's name, length and bytes (`WorldFiles.Hash`). The client
  checks it after unpacking and before writing anything (`WorldShare.Finish`), and Inflate must come to exactly the
  offered size. A bad transfer fails with "the world didn't arrive intact (hash mismatch)". No change needed.

## The save (copy of the user's prof1, day 1)

| file | size | content |
|---|---|---|
| sav.dat | 2 225 456 B | JSON |
| savs.dat | 9 027 410 B | JSON; 5 758 756 B of it is one base64 string, `"graph"`, which is a ZIP (4 319 067 B) of the A* pathfinding graphs, already Deflated inside |
| savch.dat | not present | |
| total sent | 11 252 866 B (10.7 MiB) | |

So about half of what is sent is already compressed once (base64 of a ZIP). Deflating it again still helps a lot:
base64 wastes a quarter, and the ZIP's entries were packed at a weak level (the decoded ZIP Deflates from 4.3 MB to
1.7 MB). The rest of savs.dat (3.27 MB of JSON) Deflates to 0.28 MB.

## Method and machine

- Intel i7-11370H (4 cores, 8 threads, `nproc` 8), 15 GiB RAM, Fedora, Linux 7.3.
- .NET numbers: a console program on .NET 8.0.33 using `System.IO.Compression` the way `WorldFiles.Deflate` and
  `Inflate` do (MemoryStream in, one Write, read back into a fixed buffer), best of 5 runs after one warm-up,
  allocation from `GC.GetTotalAllocatedBytes`. .NET 8 uses zlib-ng, so it is faster than the game's Mono.
- In-game numbers (Unity 2021.3 Mono, the mod targets net472): from the mod's own log lines.
- zstd, xz, gzip: their command-line tools, as reference only (none of them is usable from the mod without adding a
  native library).

## Compression of the 11 252 866 bytes

| method | sent KiB | ratio | compress ms | decompress ms | usable in the mod |
|---|---|---|---|---|---|
| **Deflate Optimal (current)**, .NET 8 | 2568 | 23.4% | 86 | 12 | yes |
| Deflate Optimal, **in game (Mono)** | 2573-2575 | 23.4% | 111-122 | 151-236 incl. hash and write | yes |
| Deflate Fastest, .NET 8 | 2858 | 26.0% | 44 | 13 | yes |
| Deflate SmallestSize, .NET 8 | 2588 | 23.6% | 144 | 13 | no (.NET 6+) |
| GZip Optimal, .NET 8 | 2568 | 23.4% | 84 | 11 | yes, same as Deflate plus a header |
| Brotli Optimal, .NET 8 | 2344 | 21.3% | 78 | 11 | no (not in .NET Framework / Mono) |
| Brotli Fastest, .NET 8 | 2658 | 24.2% | 22 | 14 | no |
| graph base64-decoded, then Deflate Optimal | 2203 | 20.0% of the original | 75 (+4 to re-encode) | 9 | possible, would need a new packing value |
| graph base64-decoded, then Deflate Fastest | 2444 | 22.2% | 34 | 10 | possible |
| zlib level 1 / 6 / 9 (Python) | 2858 / 2568 / 2588 | | 46 / 90 / 152 | 19 / 17 / 18 | reference |
| zstd -3 (CLI) | 2449 | 22.3% | 28 | 9 | no |
| zstd -19 (CLI) | 2058 | 18.7% | 2633 | 10 | no |
| xz -6 (CLI) | 1960 | 17.8% | 1865 | 69 | no |

Memory: compressing allocates about 10.5 MiB on top of the data (the MemoryStream growing); the host's worker holds
the files plus their concatenation (about 22 MB) for a moment, and keeps only the 2.5 MB it sends. The client holds
the 2.5 MB it received, the 11 MB unpacked and the 11 MB split into files (about 25 MB) while it checks and writes.

## The real join (two real games, Direct over loopback TCP)

`MAP_SCENARIO=map_scenario_join tools/run-test.sh map` (the join and one step, then both games dumped: `dumps: SAME`,
`map: PASS`, 56 s). Client log (`[join]` lines, times since the loading screen came up; the "at" times and the
transfer line were added for this measurement):

| stage | when / how long |
|---|---|
| first Hello answered, host makes its snapshot (game stopped 113 ms, `join-stop`) and packs it | offer arrived at 0.6 s |
| transfer: `the world's 2575 KiB came in 48 ms (53402 KiB/s, 11 pieces)` | 48 ms |
| unpack, SHA-256 check, write: `unpacked, checked and stored in 236 ms on a worker thread` | 0.6 s to 0.9 s |
| asks to join with it, is let in, game loading starts | 1.4 s |
| new scene up (`the game's loading has it now`) | 3.6 s |
| `World ready 22.4 s after start, 15.0 s after leaving the menu` | about 15 s |

Host, same save, from earlier runs' logs: `read and hashed in 146-150 ms, packed in 111-122 ms on a worker thread`,
and `host frames while #1000 joined (let in, 3.1 s): longest 80 ms, 1 of 337 over 50 ms`.

Over the internet the transfer is the stage that grows. Not measured here (Steam's relay can't be tested with one
machine); computed: 2.5 MiB takes 2.1 s at 10 Mbit/s, 0.4 s at 50 Mbit/s. With 4 pieces of 256 KiB in flight the
window is 1 MiB, enough for 10 MiB/s at 100 ms round trip.

## Conclusion: keep Deflate Optimal

- **Fastest** saves about 40-70 ms of a worker thread on the host, which doesn't stop the host's game anyway, and
  sends 290 KiB (11%) more. At 10 Mbit/s that's 0.23 s more transfer. Worse for the slow links where it matters.
- **Decoding the graph's base64 first** (a new packing 2) would send 365 KiB (14%) less: 0.3 s at 10 Mbit/s,
  under 0.1 s at 50 Mbit/s, against a world load of about 15 s. It needs a new packing value, finding the field in
  the JSON, a check that the base64 re-encodes to the same bytes and a fallback. Not worth a protocol change now;
  it is the first thing to do if slow links turn out to matter.
- Brotli and zstd/xz would be 9-24% smaller but aren't available on .NET Framework / Unity Mono without a native
  library; not worth adding one for under a second.
- The world load on the client (the game's own loading, about 13 s of the 15) dominates the join, not the transfer.

No change to the bytes on the wire; Protocol.Version stays as it is.
