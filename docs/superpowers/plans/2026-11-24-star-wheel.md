# Home Star Wheel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Repaint only the six original Windows home-wheel buttons, center and rim; every model-supplied menu stays untouched.

**Architecture:** Keep Wheel.Draw's original source of slices, order, commands, guards, history and pagination. At the existing final layout seam, call a private home-only WPF painter; all other routes continue through the unmodified original renderer. Routed mouse events still invoke the exact Slice provided by Draw.

**Tech Stack:** C#/.NET 8 Windows WPF (Canvas / Geometry / RenderTargetBitmap), unchanged Unity 2022.3.22f1 Windows Mono renderer.

**Spec:** Approved local preview Documents/DeskCompanion-Radial-Preview/index.html is the visual reference, not a menu specification. Latest direction: use original readable home-button art with blue-white/lilac/berry palette; the original model menu and all subpages are out of scope.

## Global Constraints

- Source: shiyo1843-tech/Deskside-Companion, v1.0.0 (`d29d1158e6d55b40d268ef180b3c64f4705f9a13`). Fresh isolated checkout; no push or release.
- Original home order: 角色菜单、表情、衣装、聊天、动作、桌宠设置. 聊天 directly opens the original tab. No prototype-only features or commands.
- One default appearance. Keep source-supplied icon bytes, expression parameters, disabled guards, back/reset behavior, hotkeys, popup and embedded modes untouched. No consultation of real character or API settings. No model or model texture in the new art.

## Review Focus

- Hit on an icon, label or narrow visual seam must reach the correct original wedge.
- A missing model/empty options must not make disabled entries active.
- Model nodes may be disabled or have a native icon, parameterized submenus, toggle, button or multi-axis puppet; leaving must still reset the parent parameter.
- A model can provide more than eight entries; next/previous and central back must still navigate, not change the model.
- Floating and embedded wheel have different root-center semantics and must keep those. A chat click must not introduce a submenu.

### 1. Pin the before state and the new visual requirement

- [x] Build the unmodified native host. In tests/WheelSmoke, compile the unaltered production wheel against an isolated fake PetWindow boundary, call real hit-target handlers, and save reference images of the home and untouched settings/face/model/puppet pages.
- [x] Check the area under the top button is light and expect the upstream dark implementation to fail before implementing the skin.

### 2. Paint only the home wheel

- [x] Add Wheel.Home.cs and an optional six-PNG resource group in the native host. Use the existing icon identifiers to resolve artwork; preserve unshrunk hit geometry and original callback. Make normal, hover, pressed and unavailable states distinct. Retain the exact root-center wording.
- [x] Wire only the home dispatch in Wheel.cs. Compare original and new shadow-free pixel buffers for all tested non-home routes; verify distinct states, exact accessible names and event outcomes.

### 3. Verify and stage on Windows

- [x] Publish the edited WPF host for win-x64; build the unchanged matching renderer or use the official digest-verified v1.0.0 renderer and explicitly record which.
- [x] Smoke the actual host/renderer with a fresh --data-dir and the original bundled generic sample. Provide local launch instructions, an evidence summary and app-only image(s); do not publish or capture the desktop.
