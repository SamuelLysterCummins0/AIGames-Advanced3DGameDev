# CA2 Plan — Advanced 3D Game Development

**Feature scope decision and synchronisation approach**

---

## Chosen Feature Scope

**Option B — Networked Pickup / Interaction**

A shared scene object (a collectible) that spawns when the session starts, can be picked up by any connected client by pressing E, and is correctly removed across all clients when picked up.

---

## Why Option B

Option B is the right size for a two-week sprint. The core logic — spawn an object, detect input, remove it on pickup — is small enough to implement and test fully in the time available. The authority model is clear: the host holds StateAuthority over the object, and the picking-up client holds InputAuthority. There is exactly one `[Networked]` property to justify, one despawn call to guard, and the two-client test is straightforward to record.

I explicitly decided not to attempt Option A (networked character movement) because client-side prediction and reconciliation require significantly more Fusion knowledge than two lab sessions cover. A broken movement system would score worse than a correct, smaller feature.

---

## Synchronisation Approach: [Networked] Property

I am using a `[Networked]` property (`NetworkBool IsPickedUp`) rather than an RPC.

**Reason:** Pickup state is persistent. If a third client joins mid-session, they need to see the correct current state of the pickup (already collected or still present). A `[Networked]` property is replicated to all clients including late joiners. An RPC is fire-and-forget — it would not sync state to anyone who missed it. Because this feature is about shared world state rather than a one-time event, `[Networked]` is the correct tool.

---

## Authority Model

- **StateAuthority** is held by the **Host** (the object is spawned by the host in `OnPlayerJoined`)
- **InputAuthority** is assigned to the **joining client** at spawn time via `Runner.Spawn(..., inputAuthority: player)`
- Only the entity with `HasStateAuthority` writes to `IsPickedUp` and calls `Runner.Despawn`
- Only the entity with `HasInputAuthority` reads input each tick in `FixedUpdateNetwork`

---

## Fusion Mode

**Host / Client** (GameMode.Host + GameMode.Client). Chosen because:
- One client acts as both server and player — straightforward for a local two-client test
- Authority assignment is deterministic: the Host always has StateAuthority over objects it spawns
- The NetworkProjectConfig is already set to PeerMode 0 (Client-Server), which aligns with this mode
- Shared mode would distribute authority more loosely and complicate the authority discussion for the CA
