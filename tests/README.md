# Windows checks for the home-wheel skin

Use the same Windows machine, DPI and installed fonts for before/after WPF pixel comparisons.

**WheelSmoke:** Links the real Wheel source and supplies a controlled in-memory model boundary; exercises the WPF event handlers, exact sector hits, a 2×1 author icon, disabled/toggle/button/puppet controls, paging and both popup/embedded semantics. It does not open the desktop app or contact any API.

1. In a clean upstream v1.0.0 worktree, copy just `tests/WheelSmoke` from this branch (excluding bin/obj). Run `dotnet run --project tests/WheelSmoke -c Release -- ABSOLUTE_BASELINE_DIR --baseline`.
2. In this branch, run `dotnet run --project tests/WheelSmoke -c Release -- ABSOLUTE_RESULT_DIR ABSOLUTE_BASELINE_DIR`. Eleven non-home PNGs must be byte-identical, not simply similar.

**PlayerSmoke:** Build/publish the real Windows app first, then build the verifier with `dotnet build tests/PlayerSmoke -c Release`. Copy the app's `Renderer` and `示例模型` directories into `tests/PlayerSmoke/bin/Release/net8.0-windows/` for the in-process phase. Run the compiled `PlayerSmoke.exe <absolute path to 桌边伙伴.exe> <absolute evidence directory>` from a Windows session permitted to create a private Win32 desktop.

The verifier never switches the interactive desktop or moves the hardware pointer. First it launches the published self-contained EXE against an isolated data directory, verifies active model/load, HWND, accessibility tree, shortcut-message toggle and normal shutdown. Then it uses real PetWindow, Wheel, Studio and a second real renderer; exercises WPF's own input hit tests and routed mouse/key events, including the unmodified model fallback, a real motion and direct chat navigation. Only the WPF wheel visual is rendered to PNG, never the desktop. Neither phase sends a chat or loads a personal model/profile.
