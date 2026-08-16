# Final whole-branch fix report

Date: 2026-08-16
Branch: `feat/versus-v1`

- Match start now launches the reflected vanilla solo path and aborts without entering `InMatch` if launch fails.
- `MatchStartMsg.WaveInterval` is validated, stored, and used by every client.
- Host disconnect eliminates the host, elects the lowest surviving SteamId, and lets a sole survivor win.
- Reliable VP reports are sent on change and at least every second; the host rejects/debits unaffordable remote sends.
- Steam invites are ignored unless lobby metadata contains `versus=1`.
- Hook signatures and the manual solo-start fallback are documented in `tools/dump_game_hooks.md`.

## Test evidence

`rtk dotnet test "SineusArenaVersus.sln"`  
Result: **59 passed, 0 failed, 0 skipped, 0 warnings** (1.8 s).
