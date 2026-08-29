# ASTRA UAV — Setup Guide

**Start here.** Do step 1 immediately and let it download while you read the rest — Unity plus the
Windows build module is roughly 10–15 GB and on a campus connection that is not a five-minute job.

---

## Honest note on what has been verified

I built this project without access to a Unity Editor, a C# compiler, or the internet. That has
three consequences you should know up front:

1. **Package version numbers and the Cesium installation route below are from recollection, not
   from a live check.** Where I am unsure I have said so inline. If a version string is rejected,
   Unity's Package Manager will show you the available versions — use those and tell me.
2. **The C# in this project has never been compiled.** I wrote a structural validator
   (`Tools/validate_cs.py`) to catch unbalanced braces, namespace mismatches and unimplemented
   interface members, but a validator is not a compiler. Expect a first-open error round. Paste
   the Console output to me and I will fix it.
3. **The hardware specifications in `Docs/10-UAV-Hardware-Layout.md` could not be verified against
   manufacturer datasheets.** Every number there is tagged with its evidence tier. Please read
   the Critical Concerns section of that document before you buy anything — there are two
   findings in it that affect flight safety.

---

## Step 1 — Install Unity (do this now)

1. Install Unity Hub → done, you have it.
2. **Unity `6000.0.0f1` → done.**
3. Confirm the **Windows Build Support (IL2CPP)** module is added (see 1c below) — this is the one
   easy thing to have missed, and you only find out when you try to build the `.exe`.

### 1a. Unity Hub

Download Unity Hub from <https://unity.com/download> and install it. Sign in with a free Unity
Personal account.

### 1b. Editor version — CONFIRMED

**You have Unity `6000.0.0f1` installed. That is what this project targets. No action needed.**

> **Should you upgrade to a later `6000.0.x` patch?** Not right now. `.0f1` is the least-patched
> release in the line, but the **API surface is identical across all `6000.0.x` patches**, so
> nothing in this codebase changes either way. Re-downloading costs a day you do not have before
> the presentation. If we hit a specific editor bug, upgrading then is a small incremental
> download through Unity Hub, and your project will open unchanged.
>
> **Language level:** Unity `6000.0` compiles **C# 9** against .NET Standard 2.1. All ASTRA code
> deliberately restricts itself to C# 7/8 constructs — no file-scoped namespaces, no records, no
> target-typed `new`, no global usings. That is not stylistic conservatism; it means the code
> cannot fail to compile because of a language-version mismatch, which is the one class of error I
> cannot detect without a compiler.

### 1c. Modules — check this even though Unity is already installed

In Unity Hub go to **Installs**, click the gear icon on `6000.0.0f1` → **Add modules**, and confirm
these are ticked:

- **Windows Build Support (IL2CPP)** — required for your final presentation `.exe`. The editor runs
  fine without it, so you will not notice it is missing until build day. Check now.
- **Documentation** — optional, useful offline.

You do **not** need Android, iOS, WebGL, Linux or Mac support. Skipping them saves several
gigabytes.

---

## Step 2 — Create the project

1. Unity Hub → **New project**
2. Template: **Universal 3D**

   > Use the URP template rather than Built-in or HDRP. Picking **Universal 3D** here means Unity
   > generates a correct URP asset, renderer, and quality settings for you. That is deliberate on
   > my part: hand-authoring URP settings assets outside the editor is fragile, and letting the
   > template do it removes an entire class of "everything renders pink" problems.
   >
   > If you see **Universal 3D Sample** vs **Universal 3D**, choose the plain one — the sample
   > loads a large demo scene you would only have to delete.

3. Project name: `ASTRA-UAV`
4. Location: anywhere with ~20 GB free. Avoid OneDrive-synced folders; Unity's `Library/` churns
   constantly and sync tools fight it.
5. Create, and wait for the first import to finish.

---

## Step 3 — Copy the ASTRA code in

From this delivery folder, copy:

```
ASTRA-UAV/Assets/ASTRA/          ->   <your project>/Assets/ASTRA/
```

Copy the whole `ASTRA` folder, keeping its internal structure. Unity will import and compile it.

**At this point you will probably see compile errors.** That is expected and fine — some scripts
reference packages you have not installed yet (step 4). Work through step 4 before diagnosing
anything.

---

## Step 4 — Install required packages

Open **Window → Package Manager**.

### 4a. From the Unity Registry

Set the dropdown at the top-left to **Unity Registry**, then install:

| Package | Why ASTRA needs it |
|---|---|
| **Input System** | Manual flight control. The brief specifies the new Input System, and it is genuinely better here — it handles simultaneous key states cleanly, which old `Input.GetKey` polling does not. |
| **Cinemachine** | The nine camera rigs (follow, FPV, orbit, top-down, engineering, mission, cinematic, map, perception) and smooth transitions between them. |
| **AI Navigation** | NavMesh, used only for the ground-vehicle dynamic obstacles — *not* for UAV flight planning. Flight planning uses the Margasoochi planners. |

> **Input System will prompt to restart the editor** and ask whether to enable the new input
> backends. Choose **Yes**. ASTRA sets `activeInputHandling` to *Both*, so legacy input still
> works if you need it.
>
> **Cinemachine version:** Unity 6 ships **Cinemachine 3.x**, which has a different API from 2.x
> (`CinemachineCamera` replaced `CinemachineVirtualCamera`, among other changes). My camera code
> targets **3.x**. If Package Manager offers you 2.x, tell me.
>
> **TextMeshPro:** in Unity 6 this is folded into the UGUI package and should already be present.
> If a `TMPro` namespace error appears, run **Window → TextMeshPro → Import TMP Essential
> Resources**.

### 4b. Cesium for Unity

This is the one dependency I am least certain about, so I am giving you two routes.

**Route A — scoped registry (try this first).**

Close Unity. Open `<your project>/Packages/manifest.json` in a text editor and add a
`scopedRegistries` block alongside the existing `dependencies` block:

```json
{
  "scopedRegistries": [
    {
      "name": "Cesium",
      "url": "https://unity.pkg.cesium.com",
      "scopes": [ "com.cesium.unity" ]
    }
  ],
  "dependencies": {
    "...": "your existing entries stay here"
  }
}
```

Reopen Unity, then in Package Manager switch the dropdown to **My Registries** and install
**Cesium for Unity**.

**Route B — tarball, if Route A fails.**

Go to the Cesium for Unity GitHub releases page, download the `.tgz` for the latest release, then
in Package Manager use **+ → Add package from tarball** and select it.

`Packages/manifest.reference.json` in this folder shows the shape of a working manifest.

> **Reminder of the risk I flagged:** Cesium plus Google Photorealistic 3D Tiles needs live
> internet *during your presentation* and a valid token. ASTRA is built with an `IMapDataProvider`
> abstraction and ships a procedural offline provider as a fallback. **Press `F9` at any time to
> switch map backends.** Rehearse that keystroke. If campus Wi-Fi fails mid-demo, that key is
> what saves you.

### 4c. Cesium ion token

Cesium needs a token to stream Google's photorealistic tiles.

1. Create a free account at <https://ion.cesium.com>.
2. In Unity, open **Cesium → Cesium** panel and connect to ion when prompted.
3. Add the **Google Photorealistic 3D Tiles** asset from the ion asset depot.

> Going through Cesium ion is easier than wiring a Google Maps Platform API key directly, because
> ion proxies the tiles and handles the key for you. Free-tier quota is generous but **not
> unlimited** — do not leave the simulator running unattended for hours, or you may find the
> quota exhausted on demo day. This is a real failure mode worth respecting.

---

## Step 5 — Generate the scene

ASTRA builds its scenes **procedurally from code** rather than shipping `.unity` files.

> **Why:** a Unity scene file is YAML full of GUID and fileID cross-references. Hand-authoring one
> outside the editor risks silent corruption that is very hard to diagnose. Generating the scene
> from code is more reliable, and it means your scene is version-controlled as reviewable C#
> instead of an unmergeable blob — which is better engineering practice for a research platform
> anyway.

In the Unity menu bar:

1. **ASTRA → Setup → Validate Project** — checks packages, render pipeline and settings, and
   reports anything missing.
2. **ASTRA → Setup → Configure Project Settings** — sets physics timestep, fixed timestep, layers,
   tags and input handling.
3. **ASTRA → Build → Generate UAV Prefab** — constructs the digital-twin hierarchy.
4. **ASTRA → Build → Generate Demo Scene** — builds the GCS scene, environment and cameras.

Then press **Play**.

---

## Step 6 — Controls

### Manual flight

| Key | Action |
|---|---|
| `R` | Arm |
| `F` | Disarm |
| `W` / `S` | Pitch forward / backward |
| `A` / `D` | Roll left / right |
| `Q` / `E` | Yaw left / right |
| `Space` | Increase throttle |
| `Left Ctrl` | Decrease throttle |
| `H` | Hover hold |
| `L` | Land |

### Modes and views

| Key | Action |
|---|---|
| `F1` | Manual mode |
| `F2` | Autonomous GPS mode |
| `F3` | Autonomous GPS-denied mode |
| `F5` | Toggle Real World / Perception view |
| `F6` | Engineering view |
| `F8` | Presentation mode (automated demo) |
| `F9` | **Switch map backend (Cesium ↔ offline fallback)** |
| `Tab` | Cycle camera rig |
| `Esc` | Abort mission |

---

## Troubleshooting

**Everything is pink / magenta.**
Materials compiled for a different render pipeline. Confirm you used the Universal 3D template:
*Edit → Project Settings → Graphics* should show a URP asset. If not, tell me and I will add a
URP-asset generator to the setup menu.

**`The type or namespace name 'InputSystem' could not be found`.**
Input System package not installed, or the editor has not restarted since installing it. Step 4a.

**`CinemachineCamera could not be found` or `CinemachineVirtualCamera could not be found`.**
Cinemachine major-version mismatch. Tell me which version Package Manager shows and I will adjust.

**`Cesium` namespace errors.**
Cesium for Unity is not installed. ASTRA is designed to run without it — the offline map provider
takes over. If you want to defer Cesium, tell me and I will wrap the Cesium code in a
`#if ASTRA_CESIUM` define so the project compiles cleanly without the package.

**Drone sits on the ground and will not arm.**
Expected until you arm it. Press `R`. Watch the event log at the bottom of the GCS — it will state
which preflight check failed if arming is refused.

**Drone flips immediately on takeoff.**
A motor-mixing sign error or a mismatched physics timestep. Send me a screenshot of the Motors
panel and I will correct the mixer.

---

## What to send me after your first open

1. The exact Unity version you installed.
2. The full Console output, errors and warnings, copied as text.
3. Which Cesium install route worked, A or B.
4. A screenshot after pressing Play.

That gives me everything needed to close out the first error round quickly.
