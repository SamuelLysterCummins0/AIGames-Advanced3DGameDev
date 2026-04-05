# Learning Journal — Advanced 3D Game Development CA2

---

## Weekly Log

### Week 6 — 2–8 Mar 2026

Read through the CA2 brief and decided to start looking at Photon Fusion 2 documentation ahead of the networking sessions. Went through the basic concepts around NetworkRunner, sessions, and NetworkObjects to get familiar with the terminology before writing any code. No implementation done this week, just research and planning.

---

### Week 7 — 9–15 Mar 2026

VCS branching session. Created the `feature/ca2-network` branch from main so all networking work stays isolated. Pushed the `ca3-alpha` tag to mark where the vertical slice playable loop was. Also reviewed the CA2 brief to decide on a feature scope ahead of Week 8.

Had a merge conflict on the scene file when creating the branch. Resolved by keeping the main version and re-applying the prefab change manually.

---

### Week 8 — 16–22 Mar 2026

Set up Fusion in the project. Configured `NetworkRunner` in a sandbox scene and registered the pickup prefab in `NetworkProjectConfig`. Got a two-client session connecting with the pickup spawning on both windows.

Decided on Option B (networked pickup). The scope felt right — one NetworkObject, one authority model decision, and a clear two-client test. Committed `CA2_Plan.md` documenting the chosen feature and sync approach.

**Blocker:** Second client could not see the spawned pickup. The prefab was not registered in `NetworkProjectConfig` so Fusion had no record of it.

**Resolution:** Registered the prefab in the config. Both clients then saw it correctly on session join.

![NetworkRunner Inspector setup](../CA2_Screenshots/NetworkRunner.png)

![Prefab registered in NetworkProjectConfig](../CA2_Screenshots/NetworkPrefabsInspector.png)

![Both client windows showing pickup spawned](../CA2_Screenshots/TwoClientPickup.png)

---

### Week 9 — 23–29 Mar 2026

Finished the pickup feature. Either client can press E near the pickup. The request goes as an RPC to StateAuthority, which sets `IsPickedUp = true`. `ChangeDetector` in `Render()` handles the visual change and despawn on all clients. Also built a basic room listing UI using `OnSessionListUpdated` so both clients can see and join active sessions from the lobby screen.

Ran all test scenarios from the test matrix. No persistent desyncs found. Wrote the critical discussion, Unreal replication note, and recorded the two-client submission video.

**Blocker:** Pickup was not despawning on the client side. The despawn call was inside `FixedUpdateNetwork`, which only runs on StateAuthority, so the client never ran it.

**Resolution:** Moved the visual and despawn logic to `Render()` with `ChangeDetector`. Both clients now react to the `IsPickedUp` flag changing.

![NetworkObject component on the pickup prefab](../CA2_Screenshots/NetworkObject.png)

---

## Critical Discussion

### 1. Why I Chose the Networked Pickup

I went with Option B (networked pickup) because it felt like the right size for a sprint. Option A (networked character movement) would have needed client-side prediction and reconciliation, which is a lot of Fusion-specific knowledge to get right in the time. Option B has one NetworkObject, one [Networked] property to justify, and a clear pass/fail test: press E, object disappears on both screens. I explicitly avoided anything involving continuous simulation sync because getting authority wrong there causes constant visible problems that are hard to fix under time pressure.

The scope also matched what was being taught. Week 8 introduced sessions and spawning, Week 9 covered authority and correctness. Option B maps directly onto both of those sessions without needing to go much further.

### 2. [Networked] Property vs RPC

I used both, each for a different reason. The `IsPickedUp` bool is a `[Networked]` property because it's persistent state. Once the item is picked up that state needs to stay true on every client, including any that join after the fact. Fusion replicates `[Networked]` properties to late joiners automatically, so a client joining mid-session would correctly see no pickup rather than a ghost object.

The pickup request itself is an RPC (`RPC_RequestPickup`) because it's a one-time trigger, not ongoing state. Any client can press E and fire it. The RPC targets StateAuthority, which is the only side allowed to write to `[Networked]` properties. So the flow is: client detects input locally in `Update()`, fires RPC to StateAuthority, StateAuthority sets `IsPickedUp = true`, Fusion replicates that change to all clients.

Using a `[Networked]` property alone wouldn't work because clients can't write to it directly. Using an RPC alone wouldn't work because late joiners would miss it and see a stale state. Both are needed here.

### 3. A Concrete Authority Model Decision

The main authority issue was around `InputAuthority`. My first version assigned `InputAuthority` to the host when spawning the pickup. I thought this was needed to control who could interact with it. What it actually did was prevent the client from triggering the pickup because the `HasInputAuthority` check failed on the client's side. The host could pick it up fine, but the client's E press did nothing.

The fix was to spawn the pickup with no `InputAuthority` assigned at all. Since I was using an RPC rather than `INetworkInput`, any client can fire `RPC_RequestPickup` freely. StateAuthority then handles the write. After that change both clients could trigger the pickup correctly.

A second authority issue was the despawn only happening on the host. I had the `Runner.Despawn` call inside `FixedUpdateNetwork`, which only runs on StateAuthority. Moving the visual change and despawn trigger to `Render()` with a `ChangeDetector` fixed it. `Render()` runs on all clients and fires when `IsPickedUp` changes, so every client handles the cleanup correctly.

### 4. What I'd Do Differently

I'd register the NetworkObject prefab in `NetworkProjectConfig` before writing any feature code. Not doing this was the first blocker in Week 8. The second client couldn't see anything spawning and it took a while to figure out why, because the scene and runner setup looked correct. If the prefab had been registered first I would have had a working baseline much earlier and more time for the actual feature.

---

## Lessons Learned

- Spawn NetworkObjects without `InputAuthority` unless a specific client needs to own it. Use RPCs for client-side requests and let StateAuthority handle all state writes.
- Visual changes based on `[Networked]` properties belong in `Render()` with `ChangeDetector`, not in `FixedUpdateNetwork`. `FixedUpdateNetwork` is simulation logic only.
- Register all prefabs in `NetworkProjectConfig` before testing. Objects not registered there can't spawn via the network at all, which is a silent failure that looks like a scene or runner problem.
- A small feature that works correctly on both clients scores better than a bigger feature that desyncs. Getting the authority model right on something simple is more valuable than rushing something larger.
- Running two clients early, before the feature is fully finished, surfaces authority problems much sooner than waiting until everything is complete.
