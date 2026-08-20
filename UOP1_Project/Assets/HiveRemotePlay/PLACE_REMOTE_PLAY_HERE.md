# Hive RemotePlay goes here

This folder is kept in the repository but its contents are not. RemotePlay is a separate vendor
drop from the SDK — roughly 18MB of native binaries and helper executables — so it is imported
rather than committed.

Import `RemotePlay_Release_*.unitypackage` from the Hive developer portal. It fills this folder
with:

- `Plugins/windows/RemotePlayDll.dll` — the one the game talks to
- `Plugins/windows/HiveRemoteStreamer.dll` — the streamer
- `Plugins/windows/HiveRemotePlayServiceMgr.exe`, `HiveVirtualInput.exe` — helpers it starts
- `Editor/HiveRemotePlayPostprocess.cs` — its own post-build step, which lays the above out under
  `plugins/` next to the built executable
- `Scripts/RemotePlayManager.cs`

The game resolves the DLL as `plugins/RemotePlayDll` against the working directory, so a build
has to be started from its own folder. Double-clicking the executable does that; launching it
from elsewhere with a different working directory does not.

See the repository README for what the game does with the callbacks this plugin raises.
