# Reflective Document — Advanced 3D Game Development CA3

**Student:** Sam Lyster-Cummins  
**Student Number:** 20102256  
**Module:** Advanced 3D Game Development  
**Submission:** CA3  
**Due:** 10 May 2026  

---

## Section 1: Rendering Foundations

### PBR Materials

The PBR setup from CA1 carried forward without changes to the material definitions themselves. All albedo textures kept sRGB colour space enabled, roughness and metallic maps kept it disabled, and normal maps were set to Normal Map import type with the correct DXT5nm compression. None of that needed revisiting. The vertical slice is still the same Abandoned Factory scene, so the existing materials just worked.

One thing that did change was how carefully I was checking the actual results during CA3 development. In CA1 I got the import settings right and moved on. During CA3, running the scene in two editor windows for multiplayer testing meant I was looking at the rendered output a lot more, and it became obvious that a couple of the concrete floor tiles had incorrect normal map settings left over from the starter pack assets. I corrected those during a CA3 session. Nothing dramatic, but it is the kind of thing you only notice when you are spending extended time in the scene rather than quickly testing one specific system.

The dissolve shader from CA1 is still in the project and still used for barrel despawning. `DissolveEffect.cs` uses a `MaterialPropertyBlock` to drive the `DissolveAmount` parameter so that multiple barrels can dissolve independently without sharing material state. That pattern held up fine through CA3.

![Vertical slice current scene state](Screenshots/01_Rendering_VerticalSlice.png)

### Lighting and Post-Processing

The dusk lighting rig from CA1 stayed the same in structure. The `Key_DuskSun` directional light at a low angle, four warm practical work lights with emissive bulbs and soft shadows, four cool blue fill lights with no shadows, two rim lights, the central reflection probe with box projection, and the dust particle system were all left untouched.

The Global Volume also stayed the same: ACES tonemapping, Bloom at threshold 1.0 and intensity 0.3, Vignette at intensity 0.24 and smoothness 0.4, and the volumetric fog pass. I adjusted the Vignette smoothness slightly upward (from 0.35 to 0.4) during CA3 because when running two client windows side by side the original setting felt a bit too heavy at smaller window sizes. That is a minor visual preference call rather than a technical one.

One thing CA3 made more obvious was that the volumetric fog is doing real work in the scene. When the NPC patrols into the darker areas near the back of the factory, the falloff into foggy shadow reads much better than it would under flat ambient lighting. It also makes the practical lights feel like they are actually providing localised illumination rather than just being bright spots. That was always the intent from CA1, but it became more noticeable once there was actual gameplay happening in the space.

### Unreal Engine Comparison

In CA1 I recreated the dissolve shader in Unreal Engine 5 and found several differences. Unreal expects roughness rather than smoothness in its PBR inputs, so the roughness map needed inverting. The Blend Mode has to be set to Masked manually before any opacity-based cutout will work — in Unity, Shader Graph handles that through the Graph Settings. There is no procedural noise node equivalent to Unity's Simple Noise node, so I had to import a noise texture instead. Emission values needed to be much higher (around 10 compared to roughly 2 in Unity) to produce the same visible glow. Unreal also uses a parent Material and Material Instance workflow as standard, where changes to exposed parameters are made on the instance and the base material stays untouched. Unity's Material Property Block pattern in `DissolveEffect.cs` achieves something similar at runtime, but Unity does not enforce this separation at the asset level the way Unreal does.

Looking at this again with the benefit of CA3 work, the Material Instance workflow in Unreal has an obvious advantage for the networked case: you could expose replicated parameters as Material Instance dynamic variables without a custom script, because Unreal's replication and Blueprint material interfaces are well integrated. In Unity I had to write `DissolveEffect.cs` to bridge that gap manually.

---

## Section 2: Optimisation and Constraints

### The Slow NPC Problem

The most concrete profiling work in CA3 came from investigating why the NPC appeared to move in slow motion on the host editor. For the first week of CA3 testing I was only running in a single-client configuration and everything looked fine. As soon as I opened a second client the host-side NPC started crawling. The first assumption was a NetworkTransform conflict with the NavMeshAgent.

Opening the Unity Profiler during a two-client session told a clearer story. Two things were happening. First, `NetworkTransform.Render()` was being called every render frame and was writing the last replicated position to the `transform`, which was overwriting the position that the NavMeshAgent simulation had already moved to. The NPC was visually jittering between where Fusion thought it was and where the NavMesh had moved it. Second, and separately, the host editor was being throttled to around 10 FPS when focus shifted to the second client window. Fusion's fixed tick was running at 30 Hz but the render thread was barely keeping up, which made slow NavMesh movement look even worse.

The before state was: `NetworkTransform` on the root NetworkObject, CharacterController on a child capsule, no position override in `Render()`.

The after state was: Override `Render()` in `NpcController` to reassert `transform.position = _navAgent.nextPosition` after Fusion's interpolation runs. Also added `Application.runInBackground = true` and `Application.targetFrameRate = 60` in `Awake()` so the editor does not throttle when focus changes.

![Profiler — before capsule sync fix](ProfilerCaptures/before_capsule_sync.png)

![Profiler — after capsule sync fix](ProfilerCaptures/after_capsule_sync.png)

The NPC movement on the host went from visibly wrong to correct after both changes were in. In the profiler the `NetworkTransform.Render()` cost is still there but the conflict is resolved because the override runs after it in `LateUpdate`.

### Tick Rate and BT Cost

The Behaviour Tree ticks at 10 times per second via a `btTickInterval` timer in `Update()`. Perception still runs every frame because the debug overlay reads Blackboard values directly and those need to stay current. A `ProfilerMarker` named `NPC.BehaviourTree.Tick` wraps the tree evaluation so the cost shows up as a named entry in the CPU profiler hierarchy rather than disappearing inside `NpcController.Update`.

In profiling with two clients connected and all three NPCs active, the BT tick cost was well within budget. The per-frame raycasts in `UpdatePerception()` are the more significant cost — each NPC fires at least one sight raycast and one audio occlusion check per frame, so three NPCs means up to six raycasts per frame. That is fine for three agents. If the NPC count scaled to ten or more it would be worth switching to `Physics.RaycastNonAlloc` and batching queries, but that is not a problem at the scope this project is at.

### Capsule Position Sync: From FixedUpdateNetwork to LateUpdate

A second profiling-related decision was moving the `SyncedPosition` write from `FixedUpdateNetwork` (30 Hz) to `LateUpdate` (per render frame). `SyncedPosition` is the `[Networked]` field that publishes the player capsule's world position to remote clients. When it was written in `FixedUpdateNetwork` the remote player representation visibly stuttered because the position only updated at the simulation tick rate rather than per frame. Moving it to `LateUpdate` — after physics and the CharacterController have both run — removed the visible stutter at no meaningful extra cost.

### Real-Time Budget Assessment

For a two-player vertical slice in a factory environment, performance is fine. The frame rate holds well above 60 FPS on both client windows during gameplay. The main rendering costs are the volumetric fog pass and the multiple shadow-casting lights, neither of which causes problems at this scale.

The honest limitation is that none of this has been tested beyond two clients. The Photon Fusion 2 Free tier supports up to twenty concurrent users per session, but the NPC raycasts, the Blackboard reads, and the `SyncedPosition`/`SyncedNoiseLevel` broadcast pattern have not been evaluated under that kind of load. For a student vertical slice with two players it meets the target. For anything beyond that, the perception system would need profiling against the actual NPC count before assuming it holds up.

---

## Section 3: Networking Implementation

### CA2 Baseline and What CA3 Adds

CA2 established the core pattern: `[Networked]` properties for persistent state, RPCs for one-shot requests to StateAuthority, and `ChangeDetector` in `Render()` for visual changes that need to run on every client. The pickup used `[Networked] NetworkBool IsPickedUp`, `RPC_RequestPickup` targeted at StateAuthority, and `Runner.Despawn` inside the `ChangeDetector` callback. CA3 extends that same pattern across three more systems.

**Networked NPC takedown.** `NpcController` is now a `NetworkBehaviour`. When the player performs a stealth takedown, it fires `RPC_StartTakedown` with `RpcSources.All, RpcTargets.All`. The RPC triggers the NPC death animation and disables the agent on every connected client. Without this the NPC would only die on the screen of the client who triggered it. The `RpcTargets.All` approach here is appropriate because the takedown is a visible world event that every client needs to reflect simultaneously, not a state change that should be gated through StateAuthority.

**Networked PowerBox.** `PowerBoxInteractable` was converted from a plain `MonoBehaviour` to a `NetworkBehaviour`. It has `[Networked] NetworkBool IsActivatedNet` for the activation state, `RPC_RequestActivate` targeting StateAuthority for the client-side trigger, and a `ChangeDetector` in `Render()` that fires the spark particle effects and light flicker callbacks on every peer when the flag changes. The NPC's repair assignment logic — the part that checks whether the box is active and assigns an NPC via the Behaviour Tree — only runs on StateAuthority, because the BT itself only ticks there.

**SyncedPosition.** The CharacterController is a child of the NetworkObject root. The root transform never moves because the CharacterController drives the child capsule directly. A `NetworkTransform` on the root would only ever broadcast the spawn position. The fix was a `[Networked] Vector3 SyncedPosition` field on `NetworkPlayerSetup`, written every render frame by the input-authority client from the capsule's actual world position. NPC perception reads `nps.SyncedPosition` rather than calling `player.transform.position`, which means NPCs on the host can detect remote players correctly.

**SyncedNoiseLevel.** `CharacterController.velocity` only has meaningful values on the owning client. On the host, a remote player's CharacterController has zero velocity because the CharacterController simulation is not running for them. `PlayerAudioEmitter` was reading velocity to scale the noise level, so the host's NPCs never heard Player 2 moving. Fixed with `[Networked] float SyncedNoiseLevel` on `NetworkPlayerSetup`, written by the input-authority client each frame from its own `CharacterController.velocity.magnitude`, read by remote `PlayerAudioEmitter` instances.

### Issues Encountered

**Player 2 position not syncing.** Described above under SyncedPosition. Root cause: `NetworkTransform` on the root NetworkObject broadcasts the root transform, and the root never moves. CharacterController moves the capsule child, which has no `NetworkTransform`. The `[Networked]` field solution avoided needing a second `NetworkTransform`.

**Two NetworkTransforms on the same prefab.** During one attempt to fix the position sync I added a `NetworkTransform` to both the root and the child capsule, hoping one of them would catch the CharacterController's movement. Instead they fought each other and the capsule stopped moving entirely. The rule is: one `NetworkTransform` per prefab, on the root. If the root is not the object moving, use a `[Networked]` field instead.

**Despawn race on pickup advancing GameState.** The host sets `IsPickedUp = true` and despawns in the same Fusion tick. On the client, the `ChangeDetector` sometimes fires after the NetworkObject has already been despawned, meaning the callback that advances `GameState` to `TakedownGuards` never runs on the client. Fixed by firing `OnWeaponPickedUp` locally on the picker the moment they press E, before waiting for the network round-trip. The `[Networked]` state still replicates for consistency, but the local GameState transition no longer depends on it.

**Audio detection broken for Player 2.** Also described above under SyncedNoiseLevel. The fix was straightforward once the root cause was identified — the same pattern as SyncedPosition, just applied to the noise float.

**Slow NPC on host editor.** Covered in Section 2. Combined fix: `Render()` override to reassert NavMeshAgent position, and `Application.runInBackground = true` to stop editor throttling.

### Robustness and Limitations

Two-client local testing is passing across all scenarios including simultaneous E presses, client disconnect mid-session, and late-join state replication. The test matrix from CA2 was extended to cover the CA3 features and all rows pass.

The main known limitation is latency. Both clients are running on the same machine with localhost networking, so there is no actual round-trip latency being exercised. Issues that would appear over a real WAN connection — RPC reordering, visible lag before authority acknowledges a state write, prediction mismatches on the player controller — are not present in this test setup. The SyncedPosition pattern in particular would show noticeable lag at higher latency because there is no prediction on it; the remote player's position will always be one round-trip behind.

### Authentication and Access Control

This is the A4 requirement for CA3. The implementation goes beyond a UI-level gate and puts validation on the server side.

**Unity Authentication sign-in.** Before `NetworkRunner.StartGame` is called, the client calls `await AuthenticationService.Instance.SignInAnonymouslyAsync()`. This gives each client a signed JWT issued by Unity Authentication. The token is proof that the client went through Unity's auth flow, but just having the token locally means nothing — any code in the build could grab it and present it. The server needs to verify the signature independently.

**Azure Functions proxy.** An HTTP-triggered Azure Function is deployed at:

`https://fusion-auth-proxy-sam-e9gjh2h7d3a6bjc4.germanywestcentral-01.azurewebsites.net/api/ValidateToken`

When a client attempts to connect to Fusion, this endpoint receives the Unity Authentication token. The function fetches Unity Services' public JWKS (JSON Web Key Set) endpoint and verifies the token signature against the public keys there. If the signature is valid it returns HTTP 200 to allow the connection. If verification fails — expired token, bad signature, missing audience claim — it returns a rejection response.

**Fusion dashboard configuration.** The Photon Fusion 2 project dashboard is configured with a Custom Server Provider pointing at the proxy URL. The "Reject all clients if not available" option is enabled, which means if the Azure Function is down, clients are refused rather than let through by default. Anonymous connections without a valid token are refused at the Fusion connection layer — they never reach the game session.

**Why this matters.** Client-side authentication checks can be bypassed by patching the binary. A modified build could skip the sign-in call entirely, or present a fabricated token. Because the proxy verifies the token signature against Unity's public keys rather than trusting what the client claims, a fabricated or replayed token will fail verification. The client cannot forge the signature without Unity's private key. This is server-side validation in the same sense as any other JWT-based auth flow: the client proves identity, the server independently verifies the proof.

![Azure Functions proxy configured in Fusion dashboard](Screenshots/AzureAuth.png)

---

## Section 4: Networking in Modern Engines

### Authority Models: Fusion 2 vs Unreal Engine 5

Both engines deal with the same core problem: multiple machines need to agree on the state of a shared world, and only one of them should be allowed to make authoritative writes at any given time. The solutions look similar at a surface level but are structured differently underneath.

In Photon Fusion 2 Host/Client mode, the host process holds StateAuthority over every spawned NetworkObject unless a specific object is assigned to a client. StateAuthority is the only side permitted to write `[Networked]` properties. Clients request state changes via RPCs. The host's simulation is the authoritative one and clients receive replicated state from it. In Fusion's Shared Mode the authority model is different — objects can be owned by individual clients and there is no single authoritative simulation, which changes the consistency guarantees and means server-reconciliation as a concept does not apply in the same way. Host/Client, which is what this project uses, has a clearer authority model and is closer to what dedicated-server games do (Photon Engine, 2024).

In Unreal Engine 5 with a dedicated server, the server owns all actors by default. The equivalent of StateAuthority is just the server, always. `UPROPERTY(Replicated)` corresponds to `[Networked]` in Fusion — a property that Unreal's replication system will push to connected clients. `UPROPERTY(ReplicatedUsing = OnRep_X)` maps more closely to the `ChangeDetector` pattern in Fusion: a property that, when it changes on the client, triggers a callback function. RPCs in Unreal are annotated with `UFUNCTION(Server, Reliable)` for a client-to-server call, `UFUNCTION(Client, Reliable)` for server-to-one-client, and `UFUNCTION(NetMulticast, Reliable)` for server-to-all. The `RPC_StartTakedown` in this project, which targets `RpcTargets.All`, is closest to a NetMulticast (Epic Games, 2024).

One structural difference worth noting: Unreal's Owning Client concept corresponds to Fusion's InputAuthority. The owning client in Unreal is the client connection associated with a PlayerController, and it gets special network treatment — certain RPCs are routed to it directly. In Fusion, InputAuthority is an explicit assignment that can be given to or taken from any client at runtime, which is more flexible but requires more intentional management. The pickup problem in CA2 — assigning InputAuthority to the host accidentally blocking the client from triggering the pickup — came directly from misunderstanding that distinction early on.

### Lag Compensation and Prediction

Lag is a fundamental problem in multiplayer games and there is no single solution that works for every case. The techniques in common use are client-side prediction, server reconciliation, lag compensation, interpolation, and extrapolation. They address different parts of the problem and are usually combined (Bernier, 2001).

**Client-side prediction** lets the client apply the player's own inputs immediately without waiting for a round-trip acknowledgement. The local game feels responsive. When the server's authoritative result arrives, if it differs from what the client predicted, the client reconciles by replaying inputs from the point of divergence. Fusion implements this for objects with InputAuthority when prediction is enabled — the simulation runs locally on the input-authority client and is reconciled against the host's result each tick. For a networked character controller with prediction enabled, the player would not feel input lag even at 200ms latency.

This project does not use Fusion's prediction system for the player controller because the CharacterController is on a child object outside Fusion's simulation. `SyncedPosition` publishes the result of the local simulation rather than integrating the player controller into Fusion's tick. That means remote players are always running one round-trip behind the owning client. For a stealth game at LAN latency this is imperceptible. At 150ms it would become visible. The correct long-term fix for a production-quality project would be to replace the CharacterController with Fusion's built-in `KCC` (Kinematic Character Controller) which integrates properly with the prediction system (Photon Engine, 2024).

**Interpolation and extrapolation** address the visual smoothness of remote objects. Fusion interpolates the positions of non-predicted objects between received state snapshots, which is why remote player movement looks smooth even though updates only arrive at the tick rate. Extrapolation (or dead reckoning) predicts where an object will be based on its last known velocity when a new update is overdue. Unreal's movement component uses a similar approach for simulated proxies.

**Lag compensation** is primarily relevant for hit detection. When a player fires a weapon, the projectile or trace should be evaluated against the positions of other players as they were at the moment of the shot from the shooter's perspective, not as they are when the message arrives. Halo: Reach's GDC talk (Aldridge, 2011) covers this in detail for a console shooter context. Fusion provides lag compensation via the `Runner.LagCompensationBuffer` for Physics-based traces. This project does not use lag compensation — takedowns are proximity checks at melee range where the timing difference is not perceptible.

**Dedicated servers vs peer-hosted.** In Unreal Engine with a dedicated server, the reconciliation story is cleaner: one machine holds all authority, has no rendering cost, and can run at a fixed simulation rate. Fusion Host/Client puts the server workload on one of the players' machines, which introduces variance based on the host's hardware and connection. For competitive games this matters significantly. For a two-player co-op stealth slice it is adequate. The authentication proxy deployed for this project is a step toward the pattern professional multiplayer games use — even if the game itself runs peer-hosted, validation happens on independently operated infrastructure.

### Industrial Context

Professional network programmers are expected to understand the full stack: authority models, state synchronisation, prediction and reconciliation, bandwidth budgeting, and increasingly, the infrastructure layer beneath the matchmaking and relay services. The trend toward cloud gaming has moved some of this into managed infrastructure (AWS GameLift, Photon's Cloud, Azure PlayFab) rather than studios running bare metal, but the underlying networking concepts are the same (Glazer and Madhav, 2015).

Deterministic lockstep, used in real-time strategy games, sidesteps the authority problem entirely by ensuring every client runs an identical simulation from identical inputs. There is no state to replicate because all clients produce the same state. The limitation is that any divergence — floating-point differences, non-deterministic random calls, any input arriving out of order — causes a desync that cannot be recovered from without rolling back. State synchronisation, which is what Fusion and Unreal both use, is more tolerant of divergence because the authoritative state is pushed to clients periodically, but it requires bandwidth proportional to the amount of state changing per tick.

Large-scale multiplayer systems like those using Unreal Engine's Mass Entity plugin push this further by handling thousands of simulated entities using ECS-style data layouts that are efficient enough to run in a server tick. The Behaviour Tree-driven NPC in this project is designed for a handful of agents. Scaling the same pattern to hundreds would require a fundamentally different architecture: hierarchical LOD on AI update rates, spatially partitioned perception checks, and some form of group behaviour abstraction that avoids per-agent full BT evaluations.

---

## Section 5: Version Control and Professional Workflow

### CA2 to CA3 Evolution

CA2 established the core VCS habits: feature branch from main (`feature/ca2-network`), meaningful commit prefixes (`feat:`, `fix:`, `docs:`, `net:`), tags at key milestones (`ca2-baseline`, `ca2-submit`), and keeping main in a working state throughout.

CA3 continued that pattern. The `ca3-start` tag was applied at the beginning of CA3 development to mark where CA2 left off. `ca3-alpha` was tagged when the vertical slice playable loop was confirmed working — player can find weapon, perform a takedown, and reach a win or defeat state. `ca3-profiling-pack` was applied after the profiling captures and NetworkTransform/NavMeshAgent fix commits landed. `ca3-submit` will be applied at final submission.

Being honest about the tags: some of them were applied retrospectively to the correct commits rather than in the moment. `ca3-alpha` in particular was tagged a few days after the moment it logically represents. The commit it points to is the right commit — the playable loop was confirmed working at that point — but the tag itself went on later. That is worth acknowledging because a proper professional workflow would have the tags applied in real time. In a team setting a late tag could confuse other developers about when a milestone was actually reached.

![Commit graph](Screenshots/CommitGraph.png)

![Repository tags](Screenshots/Tags.png)

### Workflow Development Across the Module

In CA1 there were long gaps between commits and the commit messages were not consistent. A lot of work would accumulate before being pushed, which made it hard to tell from the history what had changed when. CA2 improved this significantly — committing after each working session, using the prefixes consistently, and keeping the feature branch isolated meant the history was actually useful during debugging. More than once in CA3 I used `git log` to find the commit where a specific bug was introduced, which would have been much harder with the sprawling commit history from CA1.

The `.gitignore` configuration is worth mentioning because it caused a problem early in CA2 — the `Library/` folder had been partially committed before the ignore rule was properly set up, which inflated the repository size. That was cleaned up during Week 7 and has not been an issue since. The general rule going forward is: check what is staged before committing, not just that the diff looks right.

---

## Section 6: Overall Reflection

When I think about what was genuinely difficult in this module compared to what just took time, three things stand out clearly.

The hardest technical problem was the slow NPC on the host editor. The symptom appeared in the first week of CA3 multi-client testing and it took multiple sessions across several days to fully diagnose. The initial assumption — that it was a NavMeshAgent and NetworkTransform fighting over the position — was partially right but not the whole story. I could reproduce the fight by looking at the transform positions in the inspector, and overriding `Render()` to reassert the NavMesh position fixed the visible jitter. But the NPC was still moving wrong. The second half of the problem, the editor frame throttle dropping the host to 10 FPS when focus shifted to the second client, only became obvious when I started profiling properly rather than just watching the scene view. `Application.runInBackground = true` fixed it in one line. The reason this one took so long is that there were two separate bugs with almost identical symptoms. Fixing one made the other more obvious, but you could spend a long time believing you were dealing with a single issue and keep hunting variations of the same fix. The lesson I took from it is to profile first when something looks wrong at runtime, rather than trying to reason about it from the code. The profiler showed `NetworkTransform.Render()` writing the position every frame immediately, which would have saved at least two sessions of guesswork.

The decision I am most satisfied with is the `[Networked] SyncedPosition` pattern. It feels like a proper solution rather than a workaround. The underlying problem — CharacterController moves the capsule child, NetworkTransform sits on the root, root never moves — is a genuine architectural mismatch between Unity's character controller design and Fusion's NetworkObject model. The conventional fix is to restructure the prefab so the NetworkTransform is on the object that actually moves, but that creates its own problems with the Cinemachine camera rig and the First Person Controller component hierarchy. Writing a single `[Networked]` float3 that the input-authority client publishes from the capsule's world position, and having NPCs read that instead of the transform, is clean and works correctly. It also made the SyncedNoiseLevel fix obvious — same problem, same pattern, just for a different property. Once you have one thing synced that way the same approach applies to anything else that is only valid on the owning client.

What I would do differently is start with the prefab hierarchy correct from the beginning of CA2. The position-sync problem, the double NetworkTransform problem, and several hours of debugging all trace back to building the player prefab as a standard First Person Controller and then attaching Fusion's networking on top of it late in development. The CharacterController-on-child structure made sense when the prefab was a single-player controller. It becomes a problem as soon as Fusion needs to know where the player is. If I had read more carefully about how Fusion expects a player prefab to be structured before writing the movement code, the capsule and the NetworkObject root would have been the same object from the start, and all of the position-sync complexity disappears. The documentation for this exists — Fusion's manual has a section on character controller integration and the KCC package (Photon Engine, 2024) — but I read it after the problem appeared rather than before setting up the prefab. The same instinct applies to the auth pipeline: I implemented authentication during CA3 when the spec required it. If I had set it up at the start of CA2 as infrastructure, it would not have been a time-pressured task at the end. Server-side validation does not interact with gameplay systems at all; it could have been built and left running from Week 8 onwards. It ended up being a CA3 sprint because I treated it as a CA3 feature rather than a baseline requirement.

The module overall has changed how I think about distributed systems, which is something I did not expect from a Unity-focused course. The fundamental question in networked game development is: who is allowed to decide what, and how does everyone else find out? Everything in the networking sections of this project traces back to that question. Authority assignment, the RPC direction, the choice between `[Networked]` state and a one-shot RPC, the decision to put validation on a server rather than in the client build — all of it is different ways of answering the same question. That framing makes the decisions easier to reason about and easier to explain. When the NPC's audio detection was broken for Player 2, the reason was that the wrong machine was being trusted to know the player's speed. Once that is the framing, the fix is obvious.

---

## References

Aldridge, D. (2011) 'I Shot You First: Networking the Gameplay of Halo: Reach', *Game Developers Conference*. Available at: https://www.gdcvault.com/play/1014345 (Accessed: 2 May 2026).

Bernier, Y. W. (2001) 'Latency Compensating Methods in Client/Server In-game Protocol Design and Optimization', *Game Developers Conference*. Available at: https://developer.valvesoftware.com/wiki/Latency_Compensating_Methods_in_Client/Server_In-game_Protocol_Design_and_Optimization (Accessed: 2 May 2026).

Epic Games (2024) *Networking Overview for Unreal Engine* [Online]. Available at: https://dev.epicgames.com/documentation/en-us/unreal-engine/networking-overview-for-unreal-engine (Accessed: 2 May 2026).

Glazer, J. and Madhav, S. (2015) *Multiplayer Game Programming: Architecting Networked Games*. Boston: Addison-Wesley.

Photon Engine (2024) *Fusion 2 Manual* [Online]. Available at: https://doc.photonengine.com/fusion/v2/getting-started (Accessed: 2 May 2026).

---

## Credits and Third-Party Assets

Assets carried forward from CA1:

- **Volumetric Fog for URP** — CristianQiu (https://github.com/CristianQiu/Unity-URP-Volumetric-Light.git)
- **Starter Assets — First Person Controller** — Unity Technologies
- **Mixamo SWAT Character and animations** — Adobe Mixamo
- **Abandoned Factory environment** — sourced for CA1, used throughout

Additional assets and SDKs used in CA2 and CA3:

- **Photon Fusion 2** — Exit Games / Photon Engine
- **Unity Authentication SDK** — Unity Technologies (com.unity.services.authentication)
- **Azure Functions runtime** — Microsoft Azure (HTTP-triggered proxy for token validation)
- **Unity 6 (URP)** — Unity Technologies

---

## Use of AI Assistants

AI tools (primarily Claude) were used during this project as a coding pair-programmer, mainly for working through Fusion-specific API questions, debugging the NetworkTransform and NavMeshAgent conflict, and for drafting and editing sections of this document. The design decisions — the authority model, the `[Networked]` sync approach, the choice to build a server-side validation proxy rather than a client-side gate — were made and reasoned through independently. All debugging interpretations, test observations, and final implementations are my own work. Where AI suggestions were used in code, I read through and understood them before integrating them, and in several cases replaced or reworked the suggestion after testing it against the actual runtime behaviour.
