# Versus Send Radial Menu — Design Spec

**Date:** 2026-08-16  
**Status:** Approved (brainstorm)  
**Parent:** `2026-08-16-sineus-versus-mode-design.md`  
**Delivery:** BepInEx IMGUI (Approach 1) inside existing Versus HUD stack

---

## 1. Pitch

Replace the in-match shop **button list** with a **toggle radial menu** of catalog offerings (dynamic slots). Persistent Versus **info stays visible**. Usable with **mouse + keyboard** and **gamepad**. Visual style approximates Sineus Arena UI (dark translucent panels, warm accent, readable labels) without hijacking native UGUI.

---

## 2. Interaction model (Approach B — toggle + confirm)

### Always visible (HUD slim — no purchase buttons)

- Rival strip (HP cards)
- VP + passive income
- Wave timer + incoming preview
- Active send **target** highlighted on rival strip / center hint when radial open

### Radial (match only, `ShopEnabled`)

| Action | Keyboard / mouse | Gamepad |
|--------|------------------|---------|
| Open / close | **Mouse2** (middle click) | **LT** (if available; configurable fallback) |
| Aim sector | Mouse position → angle from wheel center | **Right stick** angle |
| Confirm send | LMB / Enter | A / South |
| Cancel / close | Mouse2 / Esc | B / East / LT again |
| Cycle target | Q / E | LB / RB |
| Pick target | Click rival card | Cycle only (cards still show state) |

- Slots = `VersusCatalog.All` in catalog order; **N equal sectors** (dynamic).
- Unaffordable / shop disabled → sector muted; confirm no-ops with short flash.
- Center label: target name + hovered offering + cost + remaining VP.
- Solo Dev redirect-to-self still uses living “rival” peer as target.

### Not used

- **Tab** — reserved by vanilla (reroll / reward flow). Must never be the default open key.

---

## 3. Mouse, camera, and game UI coexistence (hard rules)

These override any earlier “soft-lock fire” wording:

1. **Never block the mouse from being freed.** Versus must not force `Cursor.lockState = Locked` against the player or against vanilla menus (Esc, buff/upgrade, building, etc.).
2. **Do not own or steal cursor from vanilla UI.** If the game has unlocked the cursor for its own menu, Versus radial may still open/close only when Versus match UI is appropriate, but must not fight `CursorOwner` / vanilla pause-for-buff flows.
3. **When the radial is open and the player is driving it with a free mouse *or* the right stick, camera look must stay fixed** (no yaw/pitch from look delta). Movement (left stick / WASD) remains allowed; match does not pause (`timeScale` stays 1 per parent spec).
4. Prefer **camera look suppression** (swallow / zero look input while radial open) over disabling the whole combat input stack. Fire/abilities: best-effort non-interference — do not break buff/build menus; if fire continues while radial is open with locked gameplay cursor, that is acceptable; when cursor is free for radial, look must not spin the camera.
5. Existing `VersusCursor.UnlockForUi()` used for IMGUI must be **scoped**: only unlock while Versus is drawing interactive UI **and** vanilla has not claimed a higher-priority UI pause. Prefer unlocking for radial when open; when radial closed, **stop forcing unlock** every frame from the slim HUD if that fights gameplay look — slim HUD should be non-capturing (read-only) where possible so camera look works in combat.

**Implementation note:** Probe game types such as `CursorLock` / `CursorOwner` / `MouseLook` via reflection; freeze look by gating look delta or temporary owner flag **only while radial open**, then restore. If reflection path is unavailable, fall back to zeroing look axes while open without changing global cursor policy beyond “allow free cursor”.

---

## 4. Architecture

```
VersusHud (slim info + rival strip)
    └── SendRadialMenu (open state, highlight index, confirm/cancel draw)
VersusInput (config-driven key / gamepad / stick-angle polling)
VersusUiTheme (colors, panel draw helpers — Sineus-like tokens)
VersusCameraLookGate (radial-open → freeze look; never deny mouse free)
VersusMatch.TryQueueSend (unchanged economy path)
```

| Unit | Responsibility |
|------|----------------|
| `SendRadialMenu` | Open flag, sector from angle, draw wheel, invoke send |
| `VersusInput` | Edge-detect open key, LT, confirm, cancel, target cycle, right-stick angle |
| `VersusUiTheme` | Shared IMGUI colors/fonts for HUD + radial |
| `VersusCameraLookGate` | While radial open: fix camera look; cooperate with vanilla cursor |
| `VersusHud` | Remove shop buttons; keep info; host radial + target highlight |
| Config | All binds + optional deadzone for stick |

**Hors scope (this change):** native UGUI clone, per-offering icon art, two-step rival radial, Tab bind, forcing cursor lock.

---

## 5. Config defaults

| Key | Default | Notes |
|-----|---------|--------|
| `OpenSendRadialKey` | `Mouse2` | Unity middle mouse |
| `OpenSendRadialGamepad` | `LT` | Axis/button mapping modular |
| `ConfirmSendKey` | `Mouse0` / Enter | Plus gamepad A |
| `CancelSendKey` | `Escape` | Plus gamepad B |
| `CycleTargetPrev` / `Next` | `Q` / `E` | Plus LB / RB |
| `RadialStickDeadzone` | `0.35` | Below → keep last highlight |
| `OpenVersusMenuKey` | `F8` | Lobby / Solo Dev only — **not** in-match shop |

---

## 6. Visual direction (IMGUI theme)

- Dark translucent wedge / ring, warm gold/copper accent on hover
- Muted grey for unaffordable
- Cream/off-white labels; cost in accent color
- Avoid default purple/glow AI look; match “arena dark UI” feel of Sineus panels
- No card clutter in the wheel center beyond target + cost summary

---

## 7. Edge cases

- 0 living targets → cannot open (or open with “No target” and no confirm)
- 0 catalog offerings → cannot open
- Eliminated / Ended → force close; no open
- Target dies while open → retarget next living or close if none
- Vanilla Esc menu open → do not steal cursor; prefer ignore Versus open edge that frame if `PausedForBuffSelection` / escape UI active (detect best-effort)

---

## 8. Testing

**Pure logic (xUnit, no Unity runtime required where possible):**

- Angle → sector index for N = 1..8
- Deadzone keeps previous index
- Afford gate blocks confirm
- Target cycle wraps living peers only

**Manual:** Mouse2 open, stick aim, LT open, Q/E target, confirm send, Esc/buff menu still frees mouse, camera does not drift while radial open with free mouse or right stick.

---

## 9. Success criteria

- Shop list gone from match HUD; info remains readable
- Radial slots track catalog size
- Mouse2 + LT open/close; mouse + right stick select; confirm required
- No Tab default
- Mouse can always be freed by vanilla flows; camera look fixed while driving the radial with free mouse or right stick
- Sends still go through `TryQueueSend` / wave flush unchanged
