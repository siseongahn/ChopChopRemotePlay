# Chop Chop with Hive RemotePlay

A fork of [Unity Open Project #1: Chop Chop](https://github.com/UnityTechnologies/open-project-1),
Unity's open-source action-adventure demo, built on **Unity 2020.3 LTS**. Upstream stopped
development in December 2021; the game itself is unchanged except where noted below.

What this branch adds is a Windows build that signs in through the **Hive SDK** and can be played
**remotely** — a viewer on a phone or another machine drives the game through Hive RemotePlay, with
touches and key presses arriving over a native callback and going into the game as ordinary input.

---

## Setting up

The project does not build from a fresh clone on its own. Two vendor packages have to be imported
first, and neither is in the repository.

### 1. Hive SDK v4

Around 816MB of native binaries, two copies of `libcef.dll` at 211MB each accounting for most of it.
Get `Hive_SDK_Unity_Windows_Component.unitypackage` from the Hive developer portal and import it. It
fills `UOP1_Project/Assets/Hive_SDK_v4/`, where you will find a note marking the spot.

Hive ships its Unity SDK as **two** packages:

| Package | Contents | Needed |
|---|---|---|
| Component | Native plugins (`HIVE_PLUGIN.dll`, `HIVE_SERVICE.dll`, CEF, …) | Yes |
| Interface | The C# API (`AuthV4`, `PlatformHelper`, `Configuration`, …) | Yes |

Only the Component package has been imported here, so the C# is whatever an earlier install left
behind. The two talk to each other through JSON strings rather than matched function signatures, so a
version gap is tolerated rather than fatal — but it is a gap. Import both, matched, if you can.

`Assets/MiniJSON/` ships inside the same package and is excluded for the same reason as the SDK.

### 2. Hive RemotePlay

A separate drop, roughly 18MB. Import `RemotePlay_Release_*.unitypackage`; it fills
`UOP1_Project/Assets/HiveRemotePlay/`, which also carries a note. Inside:

- `Plugins/windows/RemotePlayDll.dll` — the library the game talks to
- `Plugins/windows/HiveRemoteStreamer.dll` — the streamer
- `Plugins/windows/HiveRemotePlayServiceMgr.exe`, `HiveVirtualInput.exe` — helpers it starts
- `Editor/HiveRemotePlayPostprocess.cs` — its own post-build step, which lays those out under
  `plugins/` beside the built player
- `Scripts/RemotePlayManager.cs`

### 3. Configuration

`Assets/Plugins/Windows/res/hive_config.xml` is committed and holds the app's Hive identifiers. It is
copied to `resources/hive_config.xml` at build time.

### What the build does for you

`HiveSDKWindowsPostBuild` runs on every Windows build and mirrors the layout of a shipped Hive title:
everything from `Plugins/Windows/additional` at the build root, `hive_string` and `hive_config.xml`
under `resources/`. RemotePlay's own post-build step does the same for its `plugins/` folder.

The game loads the native library as `plugins/RemotePlayDll`, resolved against the **working
directory**. Double-clicking the executable is fine. Launching it from somewhere else with a
different working directory is not, and shows up as a `DllNotFoundException` at startup with no
events ever arriving.

---

## Building

Two entry points, both usable from the command line and both reporting failure through the exit code.
Batchmode does not do that on its own — it reports success whatever happened, so a build that failed
looks exactly like one that worked.

```bash
# Assets changed (prefabs, locales, tables): content bundles, then the player
Unity.exe -quit -batchmode -projectPath <project> \
  -executeMethod GameBuilder.BuildContentAndPlayer -logFile <log>

# Only scripts changed
Unity.exe -quit -batchmode -projectPath <project> \
  -executeMethod GameBuilder.BuildPlayerOnly -logFile <log>
```

`AddressablesContentBuilder.BuildContent` is still there if you want the bundles alone. An optional
`-buildOutput <path>` overrides where the player goes.

**Edit an asset and build only the player, and the game keeps loading what the bundles held last
time.** Scenes and every prefab they pull in come out of Addressables, so an asset change needs the
content build first. Code does not live in bundles, so a script change does not.

Starting the editor costs about fifteen seconds before it does any of the work asked of it, which is
longer than an addressables build takes. `BuildContentAndPlayer` exists so that is paid once rather
than twice.

---

## Remote play

### Getting the events in

RemotePlay hands the host game a single native callback. `HiveRemotePlayEvents` registers it and
parses what arrives; `RemotePlayInputRouter` turns that into input.

The callback comes from RemotePlay's own thread, and an exception thrown back into native code takes
the process with it, so the whole body stays inside a `try`. It is marked `[MonoPInvokeCallback]`,
which IL2CPP requires, and the delegate is held in a static field — nothing else holds it once
registration returns, so without that it is collected and the first event kills the process.

Three kinds of event arrive on the one callback, and the `type` argument does not tell them apart:
chat arrives as 0 but status and control both arrive as 1, so the `eventType` field is what to branch
on.

| `eventType` | Carries |
|---|---|
| `Event` | The stream connecting and disconnecting |
| `Message` | Chat the viewer typed, base64'd so it survives as UTF-8 |
| `Control` | Key presses, touches and wheel — what actually drives the game |

`Control` needs its own deserialisation shape: `eventValue.value` is an **object** there, where the
other two carry a string.

| `controlType` | `controlValue.value` |
|---|---|
| `Key` | A Windows virtual-key code in hex |
| `Click` | `"X#Y"` in stream space |
| `Wheel` | The same `"X#Y"`, with the direction in `action` |

`action` is `Down`, `Move`, `Up` or `Out`. `Out` means the pointer left the streamed view, and
whatever it was holding has to be let go or the button stays down for good.

Stream coordinates are **1:1 with Unity's `Screen`**, with Y flipped. Beware measuring that against a
win32 client rect: Windows virtualises those for a process that has not declared itself DPI aware, so
at 150% scaling `GetClientRect` answers 1120x700 for a 1680x1050 window, which reads as a 1.5x stream
that is not there.

### Turning it into input

The router adds its own `Keyboard` and `Mouse` to the Input System rather than faking events at the
game's own devices, so the viewer's input is a device like any other and the existing bindings pick
it up.

A finger that lands on the UI is treated as pointing at it for the rest of the gesture, settled when
it goes down. In play, a finger that travels turns the camera and one that does not is a tap. Held
that way, a menu opening midway cannot change what a gesture meant, and the slightest wobble on a HUD
button no longer turns a tap into a camera drag.

**UI clicks are dispatched by hand.** `InputSystemUIInputModule` does not track the virtual mouse as
a pointer, so buttons under the viewer's finger did nothing while attacking and moving worked fine.
The router raycasts the mapped position through the `EventSystem` and sends the pointer events itself.
It also does **not** press the mouse button for a UI tap: pressing as well delivered the click twice
over whenever the module did happen to follow the pointer, and it meant a tap on the bag swung the
sword on the way past.

While a viewer is connected the **machine's own mouse stands down**, so two pointers are not fighting
over the same UI. It comes back on disconnect, and on shutdown whatever happens.

Control events **keep arriving after a viewer has gone**, so they are ignored unless a session is up.
A game started into a session that was already running never hears it begin — RemotePlay only reports
the change — so an arriving control event is taken as proof somebody is there, but only while nothing
has said otherwise.

### Keeping the clock honest

Turn the monitor off during a session and the game went into slow motion: smoothly, at about a fifth
speed, with the audio keeping perfect time. Measured, screen dark, viewer connected:

```
frame delivery   78.6ms   (12.7 fps)
Time.deltaTime   16.65ms  (pinned)
simulation ran at 0.212 of real time, audio at 1.000
```

16.65ms is one refresh interval at 60Hz. With vsync on a frame ends by waiting for the next blanking
interval, and with nothing being presented that wait grows while the delta the engine hands out stays
at what a refresh would have cost. Every frame got the same step, which is why it crawled evenly
instead of stuttering.

So while a viewer is watching, `RemotePlayFramePacing` runs frames off a timer instead — vsync off,
target frame rate set — since the viewer's frame rate has nothing to do with this machine's refresh.
The previous values are saved and put back on disconnect. Same conditions afterwards: 59.3 fps, delta
16.85ms against a real 16.85ms, ratio 1.000.

`DisplaySleepBlock` is the smaller half: Windows idling the screen out sends the viewer a black
picture, so the display is asked to stay awake for the length of a session. Asked for from the main
thread, because the request belongs to the thread that makes it and the plugin's callback thread is
not ours to depend on. It cannot help once the machine has **suspended** — nothing of ours is running
to ask — nor against a hand on the monitor's power button, which is what the pacing change is for.

Worth knowing if you port this: `ES_DISPLAY_REQUIRED` is ignored on Modern Standby systems. Check
with `powercfg /a`; an S3 machine honours it, an S0 low-power-idle one does not and wants
`PowerCreateRequest` instead.

### Controls a viewer can reach

A viewer has a finger and nothing else, so what the game asked a keyboard for moved onto the pointer.

| Action | How |
|---|---|
| Move | Click or tap the ground; the character walks there |
| Camera | Drag |
| Zoom | Wheel |
| Attack | A button in the corner of the HUD |
| Inventory | The bag on the HUD |
| Interact | The prompt icon — talks, cooks, picks up |
| Advance dialogue | Click the dialogue box |

Clicking the ground is turned into the stick input that would carry the character there rather than
moving it directly, so the walk goes through the same turning, acceleration, animation and states a
held key does. A hand on the keys calls it off at once. It steers in a straight line and knows
nothing of paths, so it gives up when the ground stops getting nearer — which is what a wall in the
way looks like from here.

The left button used to attack, and the two cannot share it: every step would have opened with a
swing, and a swing drops the walk to a twentieth of its speed. Hence the HUD button, which is also
the first way a viewer has had to attack at all. The key and the gamepad button are untouched.

---

## Other changes on this branch

**Korean.** 349 entries — the dialogue, the actor names, the menus, the item names and descriptions.
Noto Sans KR is attached as a fallback on the fonts already in use, since every font the game shipped
with draws Latin and nothing else. Untranslated entries fall back to English, which needed both the
locale's fallback metadata **and** the database's `UseFallback` switch: the lookup only asks for a
fallback locale when that switch is on, and without it the menus read
`No translation found for 'Continue' in UI Misc`.

**Game bugs found along the way**, all upstream rather than introduced here:

- `GameStateSO` built its alert-enemy list in `Start`, which Unity never calls on a ScriptableObject.
  Every call threw, and the only way into `GameState.Combat` sits after the throw — so that state was
  unreachable and the guards keyed off it never fired. Fixing it made combat work for the first time,
  which then surfaced two more faults behind it.
- Combat never ended if its enemies left with the scene rather than through their dying state.
- The interaction prompt read the type off an empty list when cooking or talking finished after the
  player had walked away.
- The language field in the settings read one language behind, because the code muted the callback
  that redraws it and then did not redraw it.

---

## Things that will catch you out

- **Batchmode returns 0 even when the build failed.** Both build entry points set the exit code
  themselves for this reason. Do not trust a bare `Unity.exe -quit -batchmode`.
- **A new serialized field deserializes to zero from an existing bundle**, not to whatever the C#
  initialiser says. It looks right in the editor and is empty in the build. Write the value into the
  prefab as well as the script.
- **Input binding changes need the generated `GameInput.cs` edited too.** Nothing regenerates it in
  batchmode, and the runtime reads the JSON embedded in that file rather than the `.inputactions`
  asset.
- **A group created for a new locale comes out with no build or load path**, and Addressables then
  fails the whole content build with `the given key was not present in the dictionary`, naming no
  group. `KoreanLocalizationBuilder` puts those back.
- **Don't ship `ChopChop_BackUpThisFolder_ButDontShipItWithYourGame`** (the name is the hint), the
  `cache` folder (a Chromium profile written at runtime), or `cefsubprocess/*.lib` (link-time
  libraries, ~189MB, never loaded).

---

## Play the game

Upstream releases of the original are on the
[release page](https://github.com/UnityTechnologies/open-project-1/releases).
