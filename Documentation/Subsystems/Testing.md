# Testing & Verification (ASTRA spec Sec 62–66)

> HONESTY: the sandbox this project was developed in has **no Unity Editor, no C#/.NET compiler,
> and no package network**. Therefore NONE of the C# has been compiled or run here. All "verified"
> claims below distinguish structural checks (done) from Editor/runtime checks (still required).

## What is verified in the sandbox

`Tools/validate_cs.py` performs a structural pass over the C# tree: it balances delimiters
(`{}`, `()`, `[]`), lexes strings/comments, and flags obvious structural breakage. It does **not**
compile, type-check, or resolve symbols. Current status: **0 errors / 0 warnings / 0 advisories**
across the tree.

Each `.cs` file has a matching `.cs.meta` with a unique 32-hex GUID; folder metas carry
`folderAsset: yes`.

## What must be verified in the Unity Editor (not doable in sandbox)

1. **Compile** the project in Unity 6 (6000.0.x) with URP 17.0.3 — resolve any type/symbol errors
   the structural validator cannot see.
2. **Pre-flight → GCS transition** (`MissionSetupManager`) and the self-installing bootstraps.
3. **Fly each mission profile** (Recon / Surveillance / Area Survey / Search / Point-to-Point) and
   confirm the waypoint patterns and observation dwell.
4. **Planner benchmark** (F11 → Run benchmark) comparing D* Lite / A* / Dijkstra / Theta* for plan
   time, expansions and path length; confirm Theta* produces shorter any-angle routes.
5. **GPS-denied drift** vs GPS-assisted (`GPS_Denied.md`).
6. **Threat/no-go zones** — confirm planner routes around no-go cells and biases away from
   soft-avoid/advisory zones (`Threat_Zone_Simulation.md`).
7. **LiDAR + thin-obstacle demo** — confirm detection matches the reported guaranteed-detection
   range (`Obstacle_Avoidance.md`).
8. **Digital-twin hierarchy** (D11) — walk the prefab against the spec (`Digital_Twin.md`).
9. **Cesium tiles** — only if a package + ion token are configured (`Map_System.md`).

## Reproducibility

`Core/ScenarioSeed` fixes the simulation's pseudo-randomness. Set/reuse a seed in the
`ExperimentPanel` (F11) so a test run can be reproduced or deliberately varied. Benchmark figures
come from the real planners over a fixed grid fixture; noise/seed affect only the simulation's
randomness — nothing here is a hardware measurement.
