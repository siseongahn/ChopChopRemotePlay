# Hive SDK v4 goes here

This folder is kept in the repository but its contents are not. The SDK is a vendor drop of
around 816MB of native binaries — two copies of `libcef.dll` at 211MB each account for most of
it — so it is fetched and imported rather than committed.

Import `Hive_SDK_Unity_Windows_Component.unitypackage` from the Hive developer portal and it
will fill this folder. The build then picks it up automatically: `HiveSDKWindowsPostBuild`
copies `Plugins/Windows/additional` to the build root and `Plugins/desktop/hive_string` plus
`Assets/Plugins/Windows/res/hive_config.xml` under `resources/`.

`Assets/MiniJSON/` ships inside the same package and is excluded for the same reason.

See the repository README for the full setup, including which of the two Hive packages you
need and what happens if you only have one of them.
