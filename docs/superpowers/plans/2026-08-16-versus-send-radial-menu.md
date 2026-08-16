# Versus Send Radial Menu Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the in-match shop button list with a toggle IMGUI radial of catalog offerings, keep slim Versus info visible, support Mouse2/LT + mouse/right-stick selection, and freeze camera look without blocking mouse free or vanilla menus.

**Architecture:** Pure helpers (`RadialMath`, input edge helpers) stay Unity-free for xUnit. `SendRadialMenu` + `VersusUiTheme` draw IMGUI. `VersusInput` polls config KeyCodes / gamepad axes. `VersusCameraLookGate` freezes look only while radial open. `VersusHud` becomes info-only and hosts the radial.

**Tech Stack:** BepInEx, Harmony, Unity IMGUI (`OnGUI`), Unity `Input` / `KeyCode`, existing `VersusMatch.TryQueueSend`, xUnit on net472.

## Global Constraints

- Default open key is Unity `Mouse2` (middle mouse), **never** `Tab`
- Gamepad open default: **LT** (axis + threshold, configurable)
- Toggle + confirm (not hold-to-send)
- One radial = offerings only; target via Q/E, LB/RB, rival card click
- Never prevent mouse from being freed; do not fight Esc / buff / building UI for cursor ownership
- While radial open and driven by free mouse or right stick: **camera look stays fixed**; movement OK; no pause
- Slots = `Catalog.All` dynamically; all binds in `VersusConfig`
- Sends only via `VersusMatch.TryQueueSend`
- Spec: `docs/superpowers/specs/2026-08-16-versus-send-radial-menu-design.md`

## File map

| File | Role |
|------|------|
| Create `src/SineusArenaVersus/Hud/RadialMath.cs` | Angle → sector index (no Unity) |
| Create `src/SineusArenaVersus/Hud/VersusUiTheme.cs` | Sineus-like IMGUI colors/draw helpers |
| Create `src/SineusArenaVersus/Hud/SendRadialMenu.cs` | Open state, highlight, draw, confirm |
| Create `src/SineusArenaVersus/Ui/VersusInput.cs` | Config-driven edge keys + stick angle |
| Create `src/SineusArenaVersus/Ui/VersusCameraLookGate.cs` | Freeze look while radial open |
| Modify `src/SineusArenaVersus/Config/VersusConfig.cs` | New radial binds |
| Modify `src/SineusArenaVersus/Hud/VersusHud.cs` | Remove shop buttons; wire radial |
| Modify `src/SineusArenaVersus/Ui/VersusCursor.cs` | Scoped unlock (radial / menu only) |
| Modify `src/SineusArenaVersus/VersusPlugin.cs` | Tick input + look gate; version bump |
| Create `tests/SineusArenaVersus.Tests/RadialMathTests.cs` | Sector / deadzone tests |
| Create `tests/SineusArenaVersus.Tests/SendRadialLogicTests.cs` | Afford + target cycle helpers |

---

### Task 1: RadialMath (angle → sector)

**Files:**
- Create: `src/SineusArenaVersus/Hud/RadialMath.cs`
- Test: `tests/SineusArenaVersus.Tests/RadialMathTests.cs`

**Interfaces:**
- Produces: `RadialMath.SectorIndex(float angleRadians, int sectorCount)`, `RadialMath.AngleFromVector(float x, float y)`, `RadialMath.KeepOrUpdateSector(int previous, float stickMagnitude, float deadzone, int candidate)`

- [ ] **Step 1: Write the failing tests**

```csharp
using SineusArenaVersus.Hud;
using Xunit;

namespace SineusArenaVersus.Tests;

public sealed class RadialMathTests
{
    [Theory]
    [InlineData(0f, 4, 0)]
    [InlineData(1.5707963f, 4, 1)]   // ~pi/2
    [InlineData(3.1415926f, 4, 2)]   // ~pi
    [InlineData(-0.01f, 4, 0)]
    public void SectorIndex_maps_angle_into_equal_wedges(float angle, int count, int expected)
    {
        Assert.Equal(expected, RadialMath.SectorIndex(angle, count));
    }

    [Fact]
    public void SectorIndex_with_one_sector_always_zero()
    {
        Assert.Equal(0, RadialMath.SectorIndex(2.5f, 1));
    }

    [Fact]
    public void SectorIndex_returns_minus_one_when_count_invalid()
    {
        Assert.Equal(-1, RadialMath.SectorIndex(0f, 0));
    }

    [Fact]
    public void KeepOrUpdateSector_ignores_stick_inside_deadzone()
    {
        Assert.Equal(2, RadialMath.KeepOrUpdateSector(2, 0.1f, 0.35f, 0));
    }

    [Fact]
    public void KeepOrUpdateSector_updates_outside_deadzone()
    {
        Assert.Equal(0, RadialMath.KeepOrUpdateSector(2, 0.9f, 0.35f, 0));
    }

    [Fact]
    public void AngleFromVector_up_is_positive_y()
    {
        var angle = RadialMath.AngleFromVector(0f, 1f);
        Assert.InRange(angle, 1.5f, 1.6f);
    }
}
```

- [ ] **Step 2: Run tests — expect FAIL**

Run: `dotnet test tests/SineusArenaVersus.Tests/SineusArenaVersus.Tests.csproj -c Release --filter FullyQualifiedName~RadialMathTests -v q`  
Expected: FAIL (type missing)

- [ ] **Step 3: Implement `RadialMath`**

```csharp
using System;

namespace SineusArenaVersus.Hud;

public static class RadialMath
{
    public static float AngleFromVector(float x, float y) =>
        MathF.Atan2(y, x);

    public static int SectorIndex(float angleRadians, int sectorCount)
    {
        if (sectorCount <= 0)
            return -1;
        if (sectorCount == 1)
            return 0;

        var tau = MathF.PI * 2f;
        var normalized = angleRadians % tau;
        if (normalized < 0f)
            normalized += tau;

        var wedge = tau / sectorCount;
        var index = (int)(normalized / wedge);
        if (index >= sectorCount)
            index = sectorCount - 1;
        return index;
    }

    public static int KeepOrUpdateSector(int previous, float stickMagnitude, float deadzone, int candidate)
    {
        if (stickMagnitude < deadzone)
            return previous;
        return candidate;
    }
}
```

Note: if `MathF` unavailable on net472, use `(float)Math.Atan2` / `(float)Math.PI`.

- [ ] **Step 4: Run tests — expect PASS**

Run: `dotnet test tests/SineusArenaVersus.Tests/SineusArenaVersus.Tests.csproj -c Release --filter FullyQualifiedName~RadialMathTests -v q`  
Expected: PASS

- [ ] **Step 5: Commit**

```bash
rtk git add src/SineusArenaVersus/Hud/RadialMath.cs tests/SineusArenaVersus.Tests/RadialMathTests.cs
rtk git commit -m "Add RadialMath sector mapping for Versus send wheel."
```

---

### Task 2: Target cycle + afford helpers (pure)

**Files:**
- Create: `src/SineusArenaVersus/Hud/SendRadialLogic.cs`
- Test: `tests/SineusArenaVersus.Tests/SendRadialLogicTests.cs`

**Interfaces:**
- Produces: `SendRadialLogic.CycleTarget(IReadOnlyList<ulong> living, int currentIndex, int delta)`, `SendRadialLogic.CanConfirm(bool shopEnabled, int vp, int cost)`, `SendRadialLogic.ResolveHighlight(int count, float angle, int previous, float stickMag, float deadzone)`

- [ ] **Step 1: Write failing tests**

```csharp
using System;
using SineusArenaVersus.Hud;
using Xunit;

namespace SineusArenaVersus.Tests;

public sealed class SendRadialLogicTests
{
    [Fact]
    public void CycleTarget_wraps_forward_and_back()
    {
        var living = new ulong[] { 10, 20, 30 };
        Assert.Equal(1, SendRadialLogic.CycleTarget(living, 0, +1));
        Assert.Equal(0, SendRadialLogic.CycleTarget(living, 0, -1));
        Assert.Equal(0, SendRadialLogic.CycleTarget(living, 2, +1));
    }

    [Fact]
    public void CycleTarget_empty_returns_minus_one()
    {
        Assert.Equal(-1, SendRadialLogic.CycleTarget(Array.Empty<ulong>(), 0, 1));
    }

    [Theory]
    [InlineData(true, 10, 10, true)]
    [InlineData(true, 9, 10, false)]
    [InlineData(false, 100, 10, false)]
    public void CanConfirm_requires_shop_and_funds(bool shop, int vp, int cost, bool expected)
    {
        Assert.Equal(expected, SendRadialLogic.CanConfirm(shop, vp, cost));
    }

    [Fact]
    public void ResolveHighlight_uses_deadzone()
    {
        var kept = SendRadialLogic.ResolveHighlight(4, 0f, previous: 2, stickMag: 0.1f, deadzone: 0.35f);
        Assert.Equal(2, kept);
        var moved = SendRadialLogic.ResolveHighlight(4, 0f, previous: 2, stickMag: 1f, deadzone: 0.35f);
        Assert.Equal(0, moved);
    }
}
```

- [ ] **Step 2: Run — expect FAIL**

Run: `dotnet test tests/SineusArenaVersus.Tests/SineusArenaVersus.Tests.csproj -c Release --filter FullyQualifiedName~SendRadialLogicTests -v q`

- [ ] **Step 3: Implement**

```csharp
using System;
using System.Collections.Generic;

namespace SineusArenaVersus.Hud;

public static class SendRadialLogic
{
    public static int CycleTarget(IReadOnlyList<ulong> living, int currentIndex, int delta)
    {
        if (living is null || living.Count == 0)
            return -1;
        var index = currentIndex;
        if (index < 0 || index >= living.Count)
            index = 0;
        var next = (index + delta) % living.Count;
        if (next < 0)
            next += living.Count;
        return next;
    }

    public static bool CanConfirm(bool shopEnabled, int vp, int cost) =>
        shopEnabled && cost >= 0 && vp >= cost;

    public static int ResolveHighlight(int count, float angleRadians, int previous, float stickMag, float deadzone)
    {
        var candidate = RadialMath.SectorIndex(angleRadians, count);
        if (candidate < 0)
            return -1;
        if (previous < 0 || previous >= count)
            previous = candidate;
        return RadialMath.KeepOrUpdateSector(previous, stickMag, deadzone, candidate);
    }
}
```

- [ ] **Step 4: Run — expect PASS**

- [ ] **Step 5: Commit**

```bash
rtk git add src/SineusArenaVersus/Hud/SendRadialLogic.cs tests/SineusArenaVersus.Tests/SendRadialLogicTests.cs
rtk git commit -m "Add SendRadialLogic for target cycle and confirm gates."
```

---

### Task 3: Config binds + theme tokens

**Files:**
- Modify: `src/SineusArenaVersus/Config/VersusConfig.cs`
- Create: `src/SineusArenaVersus/Hud/VersusUiTheme.cs`

**Interfaces:**
- Produces: config entries listed below; `VersusUiTheme` static colors + `DrawPanel(Rect)` / `LabelStyle` helpers using UnityEngine

- [ ] **Step 1: Add config entries** (no unit test — bind-only; verify compile)

In `VersusConfig`, add:

```csharp
public static ConfigEntry<string> OpenSendRadialKey = null!;
public static ConfigEntry<string> ConfirmSendKey = null!;
public static ConfigEntry<string> CancelSendKey = null!;
public static ConfigEntry<string> CycleTargetPrevKey = null!;
public static ConfigEntry<string> CycleTargetNextKey = null!;
public static ConfigEntry<string> GamepadOpenAxis = null!;
public static ConfigEntry<float> GamepadOpenAxisThreshold = null!;
public static ConfigEntry<float> RadialStickDeadzone = null!;
public static ConfigEntry<string> GamepadRightStickXAxis = null!;
public static ConfigEntry<string> GamepadRightStickYAxis = null!;
```

Defaults in `Bind`:

| Entry | Default | Description |
|-------|---------|-------------|
| `OpenSendRadialKey` | `"Mouse2"` | Toggle radial |
| `ConfirmSendKey` | `"Mouse0"` | Confirm (Enter also hard-checked in input) |
| `CancelSendKey` | `"Escape"` | Cancel |
| `CycleTargetPrevKey` | `"Q"` | Prev target |
| `CycleTargetNextKey` | `"E"` | Next target |
| `GamepadOpenAxis` | `"3"` OR `"Joystick Axis 9"` — pick one string that works on Windows Unity legacy Input; document in description that users can override | LT |
| `GamepadOpenAxisThreshold` | `0.55` | Press when axis ≥ threshold |
| `RadialStickDeadzone` | `0.35` | Right stick |
| `GamepadRightStickXAxis` | `"4"` | Common Unity Win mapping; overrideable |
| `GamepadRightStickYAxis` | `"5"` | Overrideable |

Also update `OpenVersusMenuKey` description to say lobby/dev only, not match shop.

- [ ] **Step 2: Implement `VersusUiTheme`**

```csharp
using UnityEngine;

namespace SineusArenaVersus.Hud;

public static class VersusUiTheme
{
    public static readonly Color PanelBg = new(0.08f, 0.09f, 0.11f, 0.88f);
    public static readonly Color PanelBorder = new(0.72f, 0.55f, 0.28f, 0.95f);
    public static readonly Color Accent = new(0.90f, 0.72f, 0.35f, 1f);
    public static readonly Color Text = new(0.93f, 0.90f, 0.82f, 1f);
    public static readonly Color Muted = new(0.45f, 0.45f, 0.48f, 0.85f);
    public static readonly Color HoverFill = new(0.90f, 0.72f, 0.35f, 0.35f);
    public static readonly Color SectorFill = new(0.12f, 0.13f, 0.16f, 0.92f);

    public static void DrawFilled(Rect rect, Color color)
    {
        var prev = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = prev;
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build src/SineusArenaVersus/SineusArenaVersus.csproj -c Release -v q`  
Expected: 0 errors

- [ ] **Step 4: Commit**

```bash
rtk git add src/SineusArenaVersus/Config/VersusConfig.cs src/SineusArenaVersus/Hud/VersusUiTheme.cs
rtk git commit -m "Add send-radial config binds and VersusUiTheme tokens."
```

---

### Task 4: VersusInput (keyboard + gamepad polling)

**Files:**
- Create: `src/SineusArenaVersus/Ui/VersusInput.cs`

**Interfaces:**
- Consumes: `VersusConfig` key/axis entries
- Produces: `VersusInput.Poll(out VersusInputFrame frame)` where frame has: `ToggleRadialEdge`, `ConfirmEdge`, `CancelEdge`, `CycleTargetDelta`, `RightStickX`, `RightStickY`, `RightStickMagnitude`, `PointerScreen`, `VanillaUiBlocksVersus` (best-effort)

- [ ] **Step 1: Implement edge helpers + Poll**

```csharp
using System;
using UnityEngine;

namespace SineusArenaVersus.Ui;

public readonly struct VersusInputFrame
{
    public bool ToggleRadialEdge { get; init; }
    public bool ConfirmEdge { get; init; }
    public bool CancelEdge { get; init; }
    public int CycleTargetDelta { get; init; }
    public float RightStickX { get; init; }
    public float RightStickY { get; init; }
    public float RightStickMagnitude { get; init; }
    public Vector2 PointerScreen { get; init; }
    public bool VanillaUiBlocksVersus { get; init; }
}

public static class VersusInput
{
    private static bool _ltWasDown;

    public static VersusInputFrame Poll()
    {
        var toggle = WasKeyEdge(VersusConfig.OpenSendRadialKey.Value) || WasLtEdge();
        var confirm = WasKeyEdge(VersusConfig.ConfirmSendKey.Value) ||
                      Input.GetKeyDown(KeyCode.Return) ||
                      Input.GetKeyDown(KeyCode.KeypadEnter) ||
                      Input.GetKeyDown(KeyCode.JoystickButton0);
        var cancel = WasKeyEdge(VersusConfig.CancelSendKey.Value) ||
                     Input.GetKeyDown(KeyCode.JoystickButton1);
        var cycle = 0;
        if (WasKeyEdge(VersusConfig.CycleTargetPrevKey.Value) || Input.GetKeyDown(KeyCode.JoystickButton4))
            cycle = -1;
        if (WasKeyEdge(VersusConfig.CycleTargetNextKey.Value) || Input.GetKeyDown(KeyCode.JoystickButton5))
            cycle = 1;

        var sx = ReadAxis(VersusConfig.GamepadRightStickXAxis.Value);
        var sy = ReadAxis(VersusConfig.GamepadRightStickYAxis.Value);
        // Unity Y often inverted on sticks — negate if wheel feels upside-down during manual test
        var mag = Mathf.Sqrt(sx * sx + sy * sy);

        return new VersusInputFrame
        {
            ToggleRadialEdge = toggle,
            ConfirmEdge = confirm,
            CancelEdge = cancel,
            CycleTargetDelta = cycle,
            RightStickX = sx,
            RightStickY = sy,
            RightStickMagnitude = mag,
            PointerScreen = Input.mousePosition,
            VanillaUiBlocksVersus = DetectVanillaUiBlock()
        };
    }

    private static bool WasKeyEdge(string keyName)
    {
        if (!Enum.TryParse(keyName, true, out KeyCode key))
            return false;
        return Input.GetKeyDown(key);
    }

    private static bool WasLtEdge()
    {
        var axis = VersusConfig.GamepadOpenAxis.Value;
        var threshold = VersusConfig.GamepadOpenAxisThreshold.Value;
        var value = ReadAxis(axis);
        var down = value >= threshold;
        var edge = down && !_ltWasDown;
        _ltWasDown = down;
        return edge;
    }

    private static float ReadAxis(string axisOrIndex)
    {
        try
        {
            if (int.TryParse(axisOrIndex, out var index))
            {
                // Legacy: Joy axis by name "Joy1 Axis 3" style — try common names
                var name = $"Joy1 Axis {index}";
                return Input.GetAxisRaw(name);
            }
            return Input.GetAxisRaw(axisOrIndex);
        }
        catch
        {
            return 0f;
        }
    }

    private static bool DetectVanillaUiBlock()
    {
        // Best-effort: if cursor unlocked by game menus, still allow radial,
        // but do not steal — caller skips forcing look/cursor fights.
        // Optional reflection: UIManager pause-for-buff flags if found.
        return false;
    }
}
```

Refine `ReadAxis` / LT during manual test; keep names config-driven (no hard dependency on one OEM mapping).

- [ ] **Step 2: Build** — expect PASS

- [ ] **Step 3: Commit**

```bash
rtk git add src/SineusArenaVersus/Ui/VersusInput.cs
rtk git commit -m "Add VersusInput polling for send radial and gamepad."
```

---

### Task 5: Camera look gate + scoped cursor

**Files:**
- Create: `src/SineusArenaVersus/Ui/VersusCameraLookGate.cs`
- Modify: `src/SineusArenaVersus/Ui/VersusCursor.cs`
- Modify: `src/SineusArenaVersus/Hud/VersusHud.cs` (stop unconditional unlock — partial in this task or Task 6)

**Interfaces:**
- Produces: `VersusCameraLookGate.SetRadialOpen(bool open)`, `VersusCameraLookGate.Tick()` — while open, suppress look (reflection on `MouseLook` / zero axes / Harmony if needed). **Must not** call `Cursor.lockState = Locked`. May unlock only when radial open for IMGUI hit-testing; when closed, do nothing to cursor.

- [ ] **Step 1: Implement gate**

```csharp
using UnityEngine;

namespace SineusArenaVersus.Ui;

public static class VersusCameraLookGate
{
    private static bool _radialOpen;

    public static bool RadialOpen => _radialOpen;

    public static void SetRadialOpen(bool open) => _radialOpen = open;

    public static void Tick()
    {
        if (!_radialOpen)
            return;

        // Allow free mouse for wheel; never force Locked.
        VersusCursor.UnlockForUi();

        // Freeze look: zero common look axes while open (config-free best effort).
        // Prefer reflection into game MouseLook if discovered; else swallow mouse delta
        // by resetting Input influence is limited — document manual verify.
        TrySuppressMouseLookComponent();
    }

    private static void TrySuppressMouseLookComponent()
    {
        // Reflection: find active behaviours named MouseLook / similar and
        // set enabled=false while open; restore on close via saved list.
        // Keep implementation modular with enable/disable restore stack.
    }
}
```

Implement restore stack properly: on open, disable found look components and stash; on close, re-enable. Never touch vanilla UI pause objects.

- [ ] **Step 2: Change `VersusCursor` docs** to state callers must only unlock when Versus interactive UI is open.

- [ ] **Step 3: Build**

- [ ] **Step 4: Commit**

```bash
rtk git add src/SineusArenaVersus/Ui/VersusCameraLookGate.cs src/SineusArenaVersus/Ui/VersusCursor.cs
rtk git commit -m "Add camera look freeze while send radial is open."
```

---

### Task 6: SendRadialMenu + HUD slim refactor

**Files:**
- Create: `src/SineusArenaVersus/Hud/SendRadialMenu.cs`
- Modify: `src/SineusArenaVersus/Hud/VersusHud.cs`
- Modify: `src/SineusArenaVersus/VersusPlugin.cs` (Update tick)

**Interfaces:**
- Consumes: `VersusMatch`, `VersusInputFrame`, `SendRadialLogic`, `RadialMath`, `VersusUiTheme`, `VersusCameraLookGate`
- Produces: `SendRadialMenu` with `bool IsOpen`, `void Tick(VersusInputFrame)`, `void Draw()`, bound match/target index owned by HUD

- [ ] **Step 1: Implement `SendRadialMenu`**

Behavior:
- `Tick`: if `VanillaUiBlocksVersus` and not open, ignore toggle; if toggle edge and match `ShopEnabled` and living targets > 0 and catalog count > 0 → flip open; on open set look gate; on close clear gate
- While open: cycle target via frame delta; compute angle from stick if mag ≥ deadzone else from mouse relative to screen center; update highlight; on confirm if `CanConfirm` → `match.TryQueueSend(target, offering.Id)` then optionally keep open; on cancel → close
- Auto-close on Eliminated/Ended
- `Draw`: only if open — full-screen dim light, N wedges (approximate with triangles or labeled arc buttons via `GUI.Button` in polar layout), center text

Polar button layout (practical IMGUI): place `GUI.Button` rects on a circle for each offering (dynamic radius), highlight selected with `VersusUiTheme.HoverFill`.

- [ ] **Step 2: Refactor `VersusHud`**

- Remove shop `GUILayout.Button` loop and target `SelectionGrid` from side panel
- Keep VP, passive, wave, incoming, spectate stub
- Show selected target name as label
- Rival cards: if click (`Event.current`) on card → set target index
- Remove `VersusCursor.UnlockForUi()` from `OnGUI` when radial closed; call unlock only from look gate / when radial open
- Field: `SendRadialMenu _radial`; public `int TargetIndex` / living targets refresh shared with radial
- `TickInput(VersusInputFrame frame)` called from plugin Update

- [ ] **Step 3: Wire `VersusPlugin.Update`**

After match active:

```csharp
var frame = VersusInput.Poll();
_hud?.TickInput(frame);
VersusCameraLookGate.Tick();
```

- [ ] **Step 4: Build + full tests**

Run: `dotnet test tests/SineusArenaVersus.Tests/SineusArenaVersus.Tests.csproj -c Release -v q`  
Expected: all existing + new tests PASS

- [ ] **Step 5: Commit**

```bash
rtk git add src/SineusArenaVersus/Hud/SendRadialMenu.cs src/SineusArenaVersus/Hud/VersusHud.cs src/SineusArenaVersus/VersusPlugin.cs
rtk git commit -m "Replace match shop list with SendRadialMenu and slim HUD."
```

---

### Task 7: Version bump + manual verify notes

**Files:**
- Modify: `src/SineusArenaVersus/VersusPlugin.cs` (`PluginVersion` → `0.1.14`)
- Modify: `thunderstore/manifest.json` (`0.1.14`)

- [ ] **Step 1: Bump versions**

- [ ] **Step 2: Release build (copies to BepInEx profile)**

Run: `dotnet build src/SineusArenaVersus/SineusArenaVersus.csproj -c Release`

- [ ] **Step 3: Manual checklist** (agent documents results in commit message or leaves for user)

1. Solo Dev Test → Mouse2 opens radial, middle click closes  
2. Right stick moves highlight; LT toggles if axis mapped  
3. Q/E changes target; confirm queues send (VP drops)  
4. Esc / buff menu: mouse frees; Versus does not re-lock  
5. With radial open + free mouse: camera does not look-drag  
6. Tab still rerolls vanilla reward (Versus does not bind Tab)

- [ ] **Step 4: Commit + push**

```bash
rtk git add src/SineusArenaVersus/VersusPlugin.cs thunderstore/manifest.json
rtk git commit -m "Bump to 0.1.14 for send radial menu."
rtk git push
```

---

## Spec coverage self-check

| Spec requirement | Task |
|------------------|------|
| Toggle + confirm radial | 6 |
| Dynamic catalog slots | 1, 6 |
| Info HUD remains | 6 |
| Mouse2 + LT | 3, 4 |
| Mouse + right stick aim | 4, 6 |
| Target Q/E LB/RB + cards | 2, 4, 6 |
| No Tab | 3 |
| Never block mouse free | 5 |
| Camera fixed while radial driven | 5 |
| Theme Sineus-like | 3, 6 |
| TryQueueSend only | 6 |
| Config modular | 3 |
| Pure tests | 1, 2 |

## Placeholder / consistency scan

- No TBD left; LT axis names are config defaults to tune in Task 4/7 manual pass  
- `Mouse2` consistent (Unity middle mouse)  
- Types: `VersusInputFrame`, `SendRadialLogic`, `RadialMath`, `SendRadialMenu` aligned across tasks
