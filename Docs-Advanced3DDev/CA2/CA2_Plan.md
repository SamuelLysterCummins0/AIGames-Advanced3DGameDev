# CA2 Plan — Advanced 3D Game Development

## Chosen Feature

**Option B — Networked Pickup**

A pickup object spawns in the shared scene when the session starts. Either connected client can walk up and press E to collect it. When picked up it disappears on both clients.

---

## Why Option B

Option B has a clear, testable outcome and a straightforward authority model. Option A (networked movement) would have needed client-side prediction which is a lot more Fusion knowledge to get right in the time available. Option B can be fully tested with two clients and a single recorded interaction.

---

## Sync Approach

- `[Networked] bool IsPickedUp` holds the pickup state. Fusion replicates this to all clients including late joiners.
- `RPC_RequestPickup()` is fired by whichever client presses E. It targets StateAuthority, which sets `IsPickedUp = true`.
- `ChangeDetector` in `Render()` watches for the flag changing and handles the visual update and despawn on all clients.
- The pickup is spawned without `InputAuthority` so either client can trigger the RPC freely.

---

## Authority Assignment

- **StateAuthority** is held by the Host. The pickup is spawned by the host in `OnPlayerJoined`, so the host holds StateAuthority over it for its entire lifetime. Only the host writes to `IsPickedUp` and calls `Runner.Despawn`.
- **InputAuthority** is not assigned to any client at spawn time. Since either client needs to be able to trigger the pickup, assigning InputAuthority to one player would block the other. The RPC approach is used instead so both clients can send a request freely.

---

## Fusion Mode

Host / Client (`GameMode.Host` + `GameMode.Client`). The host holds StateAuthority over spawned objects, which keeps the authority model predictable for a two-client test.
