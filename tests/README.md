# Windows checks for the enamel wheel and Display trials

Use the same Windows machine, DPI and installed fonts for before/after WPF pixel comparisons.

**WheelSmoke:** Links the real Wheel source and supplies a controlled in-memory model boundary; exercises the WPF event handlers, exact sector hits, a 2×1 author icon, disabled/toggle/button/puppet controls, paging and both popup/embedded semantics. It does not open the desktop app or contact any API.

1. In a clean snapshot of the previous Display trial with its A home, copy `tests/WheelSmoke` (excluding bin/obj). Run `dotnet run --project tests/WheelSmoke -c Release -- ABSOLUTE_BASELINE_DIR --baseline`.
2. In this source tree, run `dotnet run --project tests/WheelSmoke -c Release -- ABSOLUTE_RESULT_DIR ABSOLUTE_BASELINE_DIR`. All protected PNGs and the complete path/cursor/label manifest must be byte-identical. The six standalone route classes must actually differ from the baseline.

**PlayerSmoke:** Build/publish the real Windows app first, then build the verifier with `dotnet build tests/PlayerSmoke -c Release`. Copy the app's `Renderer` and `示例模型` directories into `tests/PlayerSmoke/bin/Release/net8.0-windows/` for the in-process phase. Run the compiled `PlayerSmoke.exe <absolute path to 桌边伙伴.exe> <absolute evidence directory>` from a Windows session permitted to create a private Win32 desktop.

The verifier never switches the interactive desktop or moves the hardware pointer. First it launches the published self-contained EXE against an isolated data directory, verifies active model/load, HWND, accessibility tree, shortcut-message toggle and normal shutdown. Then it uses real PetWindow, Wheel, Studio and a second real renderer; exercises WPF's own input hit tests and routed mouse/key events, including the unmodified model fallback, a real motion and direct chat navigation. Only the WPF wheel visual is rendered to PNG, never the desktop. Neither phase sends a chat or loads a personal model/profile.

**DisplaySmoke live wheels.** Build `tests/DisplaySmoke` and stage the same renderer and generic example directories next to the test executable, both in a baseline snapshot and here. Run the baseline executable with `OUTPUT_DIR wheel-before` and the finished executable with `OUTPUT_DIR wheel-live ABSOLUTE_BASELINE_DIR`. These run on a private Win32 desktop, import a small programmatic variant of the existing example to exercise actual morphs, capture real WPF+Unity-backed routes, compare the protected pages, and export the final six-screen montage. To check the published launcher on its true path, run `OUTPUT_DIR wheel-launch ABSOLUTE_PATH_TO_Start-Subwheels-Trial.cmd` with the trial profile still absent.
