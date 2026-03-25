# CA2 Test Matrix

Two-client testing performed using ParallelSync (two Unity Editor instances from the same project). Host started first, then Client.

| # | Scenario | Expected Result | Actual Result | Pass / Fail |
|---|---|---|---|---|
| 1 | Host starts session, Client joins | Both windows connect without errors; pickup object visible on both | Both clients connected and pickup spawned correctly on both windows | Pass |
| 2 | Host presses E near pickup | Pickup disappears on both Host and Client windows simultaneously | Pickup despawned on both windows with no visible delay | Pass |
| 3 | Client presses E near pickup | Pickup disappears on both Host and Client windows simultaneously | Client triggered RPC, host despawned the object, both windows updated | Pass |
| 4 | Both clients press E at the same time | Pickup disappears once only; no null reference or double-despawn error | Despawned once; StateAuthority guard prevented double-despawn | Pass |
| 5 | Client joins after pickup has already been collected | Client window shows no pickup object (IsPickedUp already true) | Late-joining client received correct state via [Networked] property; no pickup shown | Pass |
| 6 | Client disconnects mid-session | Host session continues running without error; no crash | Host continued running cleanly after client dropped | Pass |
| 7 | Host starts without any client joining | Session starts cleanly; pickup spawns; no errors in console | Session started and pickup spawned with no console errors | Pass |
| 8 | Client presses E when not near pickup | No interaction; pickup remains visible on both clients | No RPC sent; pickup remained visible on both clients | Pass |
