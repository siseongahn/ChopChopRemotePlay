# Chop Chop with Hive RemotePlay

English | [한국어](#hive-remoteplay를-붙인-chop-chop)

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

### What actually runs

A session is not one process. The game starts, and RemotePlay brings up its own alongside it:

| Process | What it is |
|---|---|
| `ChopChop.exe` | The game. Loads `plugins/RemotePlayDll.dll` and receives the callback |
| `plugins/RemotePlay/HiveRemoteHost.exe` | **The host process.** Its own version info calls it `Hive SDK Remote Play Host Process` |
| `plugins/RemotePlay/HiveRemotePlayServiceMgr.exe` | Service manager |
| `plugins/RemotePlay/HiveVirtualInput.exe` | Virtual input helper |
| `cef.subprocess.exe` | CEF renderer children, one or more, when the in-app browser is open |

`HiveRemoteStreamer.dll` does the streaming in-process; `HiveRemoteStreamer.dat` beside it is written
at runtime rather than shipped.

Two things about the host process are worth knowing before you spend an afternoon on them.

**It outlives the game.** Quitting or killing `ChopChop.exe` does not necessarily take
`HiveRemoteHost.exe` with it, and a leftover one holds the build's files open, so the next build
fails on a file it cannot overwrite. Kill both before building:

```powershell
Get-Process ChopChop, HiveRemoteHost -ErrorAction SilentlyContinue | Stop-Process -Force
```

An orphaned host also lined up with a Hive sign-in hang that reproduced three times — the boot got as
far as the sign-in screen and stopped. Clearing the orphan and rebuilding cleared it. That is an
observed association rather than a proven cause, but it is the first thing to check when sign-in
hangs, and it is cheap to rule out.

**It is newer than the package.** The host binary here reports version `1.2.0.33`, while the
unitypackage this project was set up from is `RemotePlay_Release_1_01_00` — and that package does not
contain `HiveRemoteHost.exe` at all. So the files under `plugins/RemotePlay/` came from more than one
drop. If remote play misbehaves in a way none of this explains, checking that those binaries all came
from the same release is a reasonable early move.

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

---
---

# Hive RemotePlay를 붙인 Chop Chop

[English](#chop-chop-with-hive-remoteplay) | 한국어

Unity의 오픈소스 액션 어드벤처 데모 [Unity Open Project #1: Chop Chop](https://github.com/UnityTechnologies/open-project-1)의
포크이며 **Unity 2020.3 LTS**에서 동작합니다. 원본은 2021년 12월에 개발이 중단됐고, 게임 자체는 아래에
적은 부분을 제외하면 그대로입니다.

이 브랜치가 더한 것은 **Hive SDK**로 로그인하고 **원격 플레이**가 가능한 Windows 빌드입니다. 휴대폰이나
다른 기기의 뷰어가 Hive RemotePlay를 통해 게임을 조작하며, 터치와 키 입력이 네이티브 콜백으로 들어와
게임의 일반 입력으로 전달됩니다.

---

## 설치

클론만으로는 빌드되지 않습니다. 벤더 패키지 두 개를 먼저 임포트해야 하고, 둘 다 저장소에 없습니다.

### 1. Hive SDK v4

네이티브 바이너리 약 816MB이며, 211MB짜리 `libcef.dll` 두 개가 대부분을 차지합니다. Hive 개발자 포털에서
`Hive_SDK_Unity_Windows_Component.unitypackage`를 받아 임포트하면 `UOP1_Project/Assets/Hive_SDK_v4/`가
채워집니다. 그 폴더에 위치를 알리는 안내 파일이 있습니다.

Hive는 Unity SDK를 **두 개**의 패키지로 배포합니다:

| 패키지 | 내용 | 필요 여부 |
|---|---|---|
| Component | 네이티브 플러그인 (`HIVE_PLUGIN.dll`, `HIVE_SERVICE.dll`, CEF 등) | 필요 |
| Interface | C# API (`AuthV4`, `PlatformHelper`, `Configuration` 등) | 필요 |

여기에는 Component만 임포트되어 있어서 C#은 이전 설치가 남긴 버전입니다. 둘은 맞물린 함수 시그니처가 아니라
JSON 문자열로 통신하므로 버전 차이가 치명적이지는 않지만, 어긋나 있는 것은 사실입니다. 가능하면 짝이 맞는
두 패키지를 모두 임포트하세요.

`Assets/MiniJSON/`은 같은 패키지에 들어 있어 같은 이유로 제외됩니다.

### 2. Hive RemotePlay

SDK와 별개인 약 18MB의 드롭입니다. `RemotePlay_Release_*.unitypackage`를 임포트하면
`UOP1_Project/Assets/HiveRemotePlay/`가 채워지고, 이 폴더에도 안내 파일이 있습니다. 내용은:

- `Plugins/windows/RemotePlayDll.dll` — 게임이 직접 호출하는 라이브러리
- `Plugins/windows/HiveRemoteStreamer.dll` — 스트리머
- `Plugins/windows/HiveRemotePlayServiceMgr.exe`, `HiveVirtualInput.exe` — 함께 실행되는 도우미
- `Editor/HiveRemotePlayPostprocess.cs` — 자체 포스트빌드 단계. 위 파일들을 빌드된 실행 파일 옆
  `plugins/`에 배치합니다
- `Scripts/RemotePlayManager.cs`

### 3. 설정

`Assets/Plugins/Windows/res/hive_config.xml`은 저장소에 포함되며 앱의 Hive 식별자를 담고 있습니다.
빌드 시 `resources/hive_config.xml`로 복사됩니다.

### 빌드가 대신 해주는 일

`HiveSDKWindowsPostBuild`는 모든 Windows 빌드에서 실행되어 출하된 Hive 타이틀의 배치를 재현합니다 —
`Plugins/Windows/additional`의 전체를 빌드 루트에, `hive_string`과 `hive_config.xml`을 `resources/`
아래에 둡니다. RemotePlay는 자체 포스트빌드 단계가 `plugins/` 폴더를 같은 방식으로 처리합니다.

게임은 네이티브 라이브러리를 `plugins/RemotePlayDll`로 로드하며, 이는 **작업 디렉터리** 기준으로
해석됩니다. 실행 파일을 더블클릭하면 문제없습니다. 작업 디렉터리가 다른 곳에서 실행하면 시작 시
`DllNotFoundException`이 나고 이벤트가 하나도 도착하지 않습니다.

---

## 빌드

진입점이 둘이고, 모두 명령줄에서 쓸 수 있으며 **종료 코드로 실패를 알립니다.** batchmode는 그렇게 하지
않습니다 — 실패해도 성공을 보고하므로, 실패한 빌드가 성공한 빌드와 똑같이 보입니다.

```bash
# 애셋이 바뀐 경우 (프리팹, 로케일, 테이블): 콘텐츠 번들 후 플레이어
Unity.exe -quit -batchmode -projectPath <project> \
  -executeMethod GameBuilder.BuildContentAndPlayer -logFile <log>

# 스크립트만 바뀐 경우
Unity.exe -quit -batchmode -projectPath <project> \
  -executeMethod GameBuilder.BuildPlayerOnly -logFile <log>
```

번들만 만들고 싶으면 `AddressablesContentBuilder.BuildContent`가 있습니다. `-buildOutput <path>`로
출력 위치를 바꿀 수 있습니다.

**애셋을 수정하고 플레이어만 빌드하면 게임은 이전 번들의 내용을 계속 불러옵니다.** 씬과 그것이 끌어오는
모든 프리팹이 Addressables에서 나오므로, 애셋 변경에는 콘텐츠 빌드가 먼저 필요합니다. 코드는 번들에
들어가지 않으니 스크립트 변경에는 필요 없습니다.

에디터는 요청받은 일을 시작하기 전에 약 15초를 씁니다 — Addressables 빌드보다 긴 시간입니다.
`BuildContentAndPlayer`가 존재하는 이유는 그 비용을 두 번이 아니라 한 번만 내기 위해서입니다.

---

## 원격 플레이

### 실제로 실행되는 프로세스

세션은 프로세스 하나가 아닙니다. 게임이 시작되면 RemotePlay가 자체 프로세스들을 함께 띄웁니다:

| 프로세스 | 정체 |
|---|---|
| `ChopChop.exe` | 게임. `plugins/RemotePlayDll.dll`을 로드하고 콜백을 받습니다 |
| `plugins/RemotePlay/HiveRemoteHost.exe` | **호스트 프로세스.** 파일의 버전 정보가 스스로를 `Hive SDK Remote Play Host Process`라고 밝힙니다 |
| `plugins/RemotePlay/HiveRemotePlayServiceMgr.exe` | 서비스 관리자 |
| `plugins/RemotePlay/HiveVirtualInput.exe` | 가상 입력 도우미 |
| `cef.subprocess.exe` | 인앱 브라우저가 열렸을 때의 CEF 렌더러 자식 프로세스 (하나 이상) |

스트리밍은 `HiveRemoteStreamer.dll`이 인프로세스로 처리합니다. 옆에 있는 `HiveRemoteStreamer.dat`은
배포된 파일이 아니라 런타임에 생성됩니다.

호스트 프로세스에 대해 두 가지는 미리 알아두는 편이 낫습니다. 모르면 반나절을 씁니다.

**게임보다 오래 살아남습니다.** `ChopChop.exe`를 종료하거나 강제 종료해도 `HiveRemoteHost.exe`가 함께
사라지지는 않습니다. 남아 있는 프로세스가 빌드 폴더의 파일을 붙잡고 있으면 다음 빌드가 덮어쓸 수 없는
파일에서 실패합니다. 빌드 전에 둘 다 종료하세요:

```powershell
Get-Process ChopChop, HiveRemoteHost -ErrorAction SilentlyContinue | Stop-Process -Force
```

고아가 된 호스트는 Hive 로그인 행과도 겹쳤습니다 — 세 번 재현됐고, 부팅이 로그인 화면까지 가서 멈췄습니다.
고아 프로세스를 정리하고 다시 빌드하니 해결됐습니다. 인과가 증명된 것은 아니고 관찰된 상관관계이지만,
로그인이 멈출 때 가장 먼저 확인할 것이고 배제하는 비용도 낮습니다.

**패키지보다 새 버전입니다.** 여기 있는 호스트 바이너리는 버전 `1.2.0.33`을 보고하는데, 이 프로젝트를
구성할 때 쓴 unitypackage는 `RemotePlay_Release_1_01_00`이고 **그 패키지에는 `HiveRemoteHost.exe`가 아예
없습니다.** 즉 `plugins/RemotePlay/` 아래 파일들이 서로 다른 드롭에서 왔습니다. 원격 플레이가 여기 적힌
어떤 설명으로도 해석되지 않는 방식으로 오작동한다면, 그 바이너리들이 같은 릴리스에서 왔는지 확인하는 것이
합리적인 초기 조치입니다.

### 이벤트를 받는 부분

RemotePlay는 호스트 게임에 네이티브 콜백 하나를 넘깁니다. `HiveRemotePlayEvents`가 그것을 등록하고
페이로드를 해석하며, `RemotePlayInputRouter`가 입력으로 바꿉니다.

콜백은 RemotePlay 자체 스레드에서 오고, 네이티브 코드로 예외가 되돌아가면 프로세스가 함께 죽으므로 본문
전체가 `try` 안에 있습니다. IL2CPP가 요구하는 `[MonoPInvokeCallback]`이 붙어 있고, 델리게이트는 정적
필드에 보관합니다 — 등록이 끝나면 그것을 붙잡는 것이 아무것도 없어서, 그러지 않으면 수집되고 첫 이벤트가
프로세스를 죽입니다.

한 콜백으로 세 종류가 도착하는데 `type` 인수로는 구분되지 않습니다. 채팅은 0으로 오지만 상태와 컨트롤이
모두 1로 오므로, 분기해야 하는 것은 `eventType` 필드입니다.

| `eventType` | 내용 |
|---|---|
| `Event` | 스트림 연결과 해제 |
| `Message` | 뷰어가 입력한 채팅. UTF-8 보존을 위해 base64 |
| `Control` | 키 입력, 터치, 휠 — 실제로 게임을 조작하는 것 |

`Control`은 역직렬화 형태가 따로 필요합니다. 다른 둘은 문자열을 담는 자리에 `eventValue.value`가
**객체**로 옵니다.

| `controlType` | `controlValue.value` |
|---|---|
| `Key` | Windows 가상 키 코드 (16진수) |
| `Click` | 스트림 좌표계의 `"X#Y"` |
| `Wheel` | 같은 `"X#Y"`, 방향은 `action`에 |

`action`은 `Down`, `Move`, `Up`, `Out`입니다. `Out`은 포인터가 스트림 화면을 벗어난 것이고, 그때 쥐고
있던 것을 놓아주지 않으면 버튼이 영구히 눌린 상태로 남습니다.

스트림 좌표는 **Unity `Screen`과 1:1**이며 Y가 반전됩니다. 이것을 win32 클라이언트 사각형으로 재려 하면
안 됩니다. Windows는 DPI 인식을 선언하지 않은 프로세스에 대해 그 값을 가상화하므로, 150% 배율에서
1680x1050 창에 대해 `GetClientRect`가 1120x700을 답합니다 — 존재하지 않는 1.5배 스트림으로 읽힙니다.

### 입력으로 바꾸는 부분

라우터는 게임의 기존 디바이스에 이벤트를 위조하는 대신 자체 `Keyboard`와 `Mouse`를 Input System에
추가합니다. 그러면 뷰어의 입력이 다른 디바이스와 동등해지고 기존 바인딩이 그대로 받습니다.

UI 위에 내려온 손가락은 그 제스처가 끝날 때까지 UI를 가리키는 것으로 취급하며, 판정은 손가락이 닿는
순간에 확정됩니다. 게임 중에는 이동한 손가락이 카메라를 돌리고 이동하지 않은 손가락은 탭입니다. 이렇게
고정해 두면 중간에 메뉴가 열려도 제스처의 의미가 바뀌지 않고, HUD 버튼에서 손가락이 미세하게 흔들려도
탭이 카메라 드래그로 변하지 않습니다.

**UI 클릭은 직접 전달합니다.** `InputSystemUIInputModule`은 가상 마우스를 포인터로 추적하지 않아서,
공격과 이동은 되는데 뷰어의 손가락 아래 버튼은 아무 반응이 없었습니다. 라우터가 매핑된 좌표로
`EventSystem` 레이캐스트를 하고 포인터 이벤트를 직접 보냅니다. 그리고 UI 탭에서는 마우스 버튼을 **누르지
않습니다** — 함께 누르면 모듈이 포인터를 따라오는 경우에 클릭이 두 번 전달되고, 가방을 탭할 때 칼까지
휘둘렀습니다.

뷰어가 연결된 동안에는 **머신의 마우스가 물러납니다.** 두 포인터가 같은 UI를 두고 다투지 않게 하기
위해서입니다. 연결이 끊기면 돌아오고, 종료 시에도 무조건 돌려줍니다.

컨트롤 이벤트는 **뷰어가 떠난 뒤에도 계속 도착하므로**, 세션이 열려 있지 않으면 무시합니다. 이미 진행
중인 세션에 게임이 시작되면 시작을 들을 수 없으므로(RemotePlay는 변화만 보고합니다) 도착하는 컨트롤
이벤트를 누군가 있다는 증거로 삼되, 연결/해제가 한 번이라도 보고된 뒤에는 그것을 따릅니다.

### 시계를 실제 시간에 붙여 두기

세션 중 모니터를 끄면 게임이 슬로우 모션이 됐습니다 — 끊김 없이 매끄럽게 약 5분의 1 속도로, 오디오는
정확한 시간을 유지한 채로. 화면이 꺼지고 뷰어가 연결된 상태에서 측정한 값:

```
프레임 전달        78.6ms   (12.7 fps)
Time.deltaTime    16.65ms  (고정)
시뮬레이션은 실시간의 0.212배, 오디오는 1.000배
```

16.65ms는 60Hz의 리프레시 간격 하나입니다. VSync가 켜져 있으면 프레임은 다음 블랭킹 간격을 기다리며
끝나는데, 표시되는 것이 없으면 그 대기가 길어지는 동안 엔진이 넘기는 delta는 리프레시 하나에 해당하는
값에 머무릅니다. 모든 프레임이 같은 시간 간격을 받았고, 그래서 더듬거리지 않고 고르게 느려졌습니다.

그래서 뷰어가 보고 있는 동안에는 `RemotePlayFramePacing`이 프레임을 타이머로 맞춥니다 — VSync를 끄고
목표 프레임레이트를 설정합니다. 뷰어의 프레임레이트는 이 머신의 리프레시와 무관하기 때문입니다. 이전
값은 저장해 두고 연결 해제 시 되돌립니다. 같은 조건에서 이후 측정값: 59.3 fps, delta 16.85ms 대 실제
16.85ms, 비율 1.000.

`DisplaySleepBlock`은 더 작은 쪽입니다. Windows가 유휴로 화면을 끄면 뷰어에게 검은 화면이 전송되므로,
세션 동안 디스플레이를 깨어 있게 요청합니다. 요청은 **메인 스레드에서** 합니다 — 그 요청은 호출한
스레드에 귀속되고 플러그인의 콜백 스레드는 의존할 대상이 아닙니다. 머신이 **절전에 들어간 뒤에는** 이
방법이 통하지 않습니다(요청할 코드가 실행되지 않습니다). 모니터 전원 버튼을 직접 끄는 경우도 막지
못하는데, 그것이 페이싱 수정이 담당하는 부분입니다.

이식할 때 알아둘 것: `ES_DISPLAY_REQUIRED`는 Modern Standby 시스템에서 무시됩니다. `powercfg /a`로
확인하세요. S3 머신은 이 요청을 존중하고, S0 저전력 유휴 머신은 무시하며 `PowerCreateRequest`가
필요합니다.

### 뷰어가 쓸 수 있는 조작

뷰어에게는 손가락 하나뿐이므로, 게임이 키보드를 요구했던 것들을 포인터로 옮겼습니다.

| 동작 | 방법 |
|---|---|
| 이동 | 땅을 클릭 또는 탭하면 그곳으로 걸어갑니다 |
| 카메라 | 드래그 |
| 줌 | 휠 |
| 공격 | HUD 우하단 버튼 |
| 인벤토리 | HUD의 가방 |
| 상호작용 | 프롬프트 아이콘 — 대화, 요리, 줍기 |
| 대화 진행 | 대화창 클릭 |

땅 클릭은 좌표를 직접 옮기는 대신 **그곳으로 향하는 스틱 입력으로 변환**합니다. 그러면 걷기가 키를 누른
것과 같은 회전, 가속, 애니메이션, 상태를 거칩니다. 키에 손이 닿으면 즉시 취소됩니다. 직선으로만 조향하고
경로를 모르므로, 목표가 가까워지지 않으면 포기합니다 — 벽이 막고 있을 때가 그렇게 보입니다.

좌클릭은 원래 공격이었고, 둘은 공유할 수 없습니다. 모든 걸음이 칼질로 시작되고, 칼질은 걷는 속도를
20분의 1로 떨어뜨립니다. 그래서 HUD 버튼이 생겼는데, 이는 뷰어가 공격할 수 있게 된 첫 수단이기도
합니다. 키와 게임패드 버튼은 그대로입니다.

---

## 이 브랜치의 그 외 변경

**한국어.** 349개 항목 — 대화, 등장인물 이름, 메뉴, 아이템 이름과 설명. 게임이 출하한 폰트들이 모두
라틴 문자만 그리므로 Noto Sans KR을 폴백으로 등록했습니다. 번역이 없는 항목은 영어로 대체되는데, 이를
위해 로케일의 폴백 메타데이터와 데이터베이스의 `UseFallback` 스위치가 **둘 다** 필요했습니다. 조회
코드는 그 스위치가 켜져 있을 때만 폴백 로케일을 찾고, 없으면 메뉴에
`No translation found for 'Continue' in UI Misc`가 표시됩니다.

**작업 중 발견한 게임 버그** — 모두 이 브랜치가 만든 것이 아니라 원본에 있던 것입니다:

- `GameStateSO`가 경계 적 목록을 `Start`에서 만드는데, Unity는 ScriptableObject에 `Start`를 호출하지
  않습니다. 모든 호출이 예외를 던졌고 `GameState.Combat`으로 가는 유일한 경로가 그 예외 뒤에 있어서 그
  상태에 도달할 수 없었으며, 그것을 기준으로 하는 가드들이 전혀 동작하지 않았습니다. 이것을 고치자
  전투가 처음으로 동작했고, 그 뒤에 숨어 있던 결함 두 개가 드러났습니다.
- 적이 죽는 상태를 거치지 않고 씬과 함께 사라지면 전투가 끝나지 않았습니다.
- 요리나 대화가 끝난 시점에 플레이어가 대상에서 멀어져 있으면, 상호작용 프롬프트가 빈 목록에서 타입을
  읽었습니다.
- 설정의 언어 항목이 한 칸 뒤처져 표시됐습니다. 라벨을 다시 그리는 콜백을 잠시 끊어 놓고 그 일을 대신
  하지 않았기 때문입니다.

---

## 걸려 넘어지기 쉬운 것들

- **batchmode는 빌드가 실패해도 0을 반환합니다.** 그래서 두 빌드 진입점이 종료 코드를 직접 설정합니다.
  `Unity.exe -quit -batchmode`만 놓고 신뢰하지 마세요.
- **신규 직렬화 필드는 기존 번들에서 0으로 역직렬화됩니다** — C# 초기화 값이 아닙니다. 에디터에서는
  정상이고 빌드에서는 비어 있습니다. 스크립트와 함께 프리팹에도 값을 써 넣어야 합니다.
- **입력 바인딩 변경은 생성된 `GameInput.cs`도 함께 수정해야 합니다.** batchmode에서 재생성되지 않고,
  런타임은 `.inputactions` 애셋이 아니라 그 파일에 임베드된 JSON을 읽습니다.
- **새 로케일용 그룹은 빌드/로드 경로가 비어 있는 상태로 생성되며**, Addressables는 그룹 이름도 알려주지
  않고 `the given key was not present in the dictionary`로 콘텐츠 빌드 전체를 실패시킵니다.
  `KoreanLocalizationBuilder`가 이를 복구합니다.
- **배포하면 안 되는 것**: `ChopChop_BackUpThisFolder_ButDontShipItWithYourGame`(이름이 곧 힌트입니다),
  `cache` 폴더(런타임에 생성되는 Chromium 프로필), `cefsubprocess/*.lib`(링크 시점 라이브러리 약
  189MB, 로드되지 않음).

---

## 게임 실행

원본의 릴리스는 [release page](https://github.com/UnityTechnologies/open-project-1/releases)에
있습니다.
