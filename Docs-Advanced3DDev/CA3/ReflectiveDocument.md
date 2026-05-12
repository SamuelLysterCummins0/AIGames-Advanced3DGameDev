# Reflective Document: Advanced 3D Game Development CA3

**Student:** Sam Lyster-Cummins  
**Student Number:** 20102256  
**Module:** Advanced 3D Game Development  
**Submission:** CA3   

---

## Section 1: Rendering Foundations

### PBR Materials

The PBR setup from CA1 carried forward unchanged. Albedo kept sRGB on, roughness and metallic maps kept it off, normal maps used Normal Map import with DXT5nm. The vertical slice uses the same Abandoned Factory scene so the existing materials worked as-is.

The only thing that changed was that running two editor windows for multiplayer testing meant I noticed a couple of concrete floor tiles had wrong normal map settings left from starter assets. I corrected those during a CA3 session.

The dissolve shader from CA1 still drives the barrel despawn. `DissolveEffect.cs` uses a `MaterialPropertyBlock` for the `DissolveAmount` parameter so multiple barrels dissolve independently.

![Vertical slice current scene state, main gangway under dusk lighting](Screenshots/01_Rendering_VerticalSlice.png)
*Vertical slice (`02_VerticalSlice`) running in Play mode. Dusk lighting rig from CA1 carried over: warm practical work lights with volumetric fog, cool blue fill, dust particles, ACES tonemapping. The exposed parameter values from the dissolve shader are still in use on the dropped-barrel cleanup.*

![Vertical slice, secondary angle showing NPC under volumetric lighting](Screenshots/02_Rendering_VerticalSlice.png)
*Same scene, alternate angle. The volumetric fog pass is doing visible work on the rim and fill lights, and the NPC patrolling into shadow reads cleanly against the lit area, which is what the lighting rig was set up to deliver.*

### Lighting and Post-Processing

The dusk lighting rig from CA1 stayed the same. `Key_DuskSun` at a low angle, four warm practical lights with emissive bulbs, four cool blue fills, two rim lights, a central reflection probe and the dust particle system all carried over untouched. The Global Volume kept ACES tonemapping, Bloom at threshold 1.0 / intensity 0.3, Vignette at 0.24 intensity, and the volumetric fog. I bumped Vignette smoothness from 0.35 to 0.4 during CA3 because at the smaller window sizes used for two-client testing the original setting felt heavy.

CA3 made the volumetric fog more visibly important. When the NPC patrols into shadow at the back of the factory the falloff reads much better than flat ambient would. The intent from CA1 was always to use light to shape the playable space, and having actual gameplay in there made it land.

### Unreal Engine Comparison

In CA1 I recreated the dissolve shader in Unreal Engine 5. Unreal expects roughness rather than smoothness so the map needed inverting, the Blend Mode has to be set to Masked manually, there is no procedural noise node so I had to import a noise texture, and emission values needed to be much higher (around 10 vs ~2 in Unity). Unreal also enforces a parent Material plus Material Instance workflow at the asset level, where Unity's `MaterialPropertyBlock` does something similar at runtime but does not enforce the separation.

Looking at this with CA3 in mind, the Material Instance workflow has an advantage for the networked case. Replicated parameters could go straight onto a Material Instance dynamic variable because Unreal's replication and Blueprint material interfaces are integrated. In Unity I had to write `DissolveEffect.cs` to bridge that gap manually.

---

## Section 2: Optimisation and Constraints

### The Slow NPC Problem

The main profiling work in CA3 came from investigating why the NPC was moving in slow motion on the host editor. Single-client testing looked fine, but opening a second client made the host's NPC start crawling. First assumption was a NetworkTransform vs NavMeshAgent conflict.

The Profiler showed two separate things going on. `NetworkTransform.Render()` was writing the last replicated position to the transform every frame, overwriting whatever the NavMeshAgent had moved to. The NPC was visually jittering between Fusion's position and the NavMesh position. Separately, the host editor was throttling to around 10 FPS when focus shifted to the second client window, which made it worse.

Fix: override `Render()` in `NpcController` to reassert `transform.position = _navAgent.nextPosition` after Fusion's interpolation runs, plus `Application.runInBackground = true` and `Application.targetFrameRate = 60` in `Awake()` so the editor doesn't throttle on focus change.

![Profiler, before capsule sync fix](Screenshots/before_capsule_sync.png)
*Before fix. Host editor's CPU Profiler captured during a session where Player 2 was walking around. `PlayerLoop` block durations vary across visible frames. The wider blocks are frames where Fusion's tick fires (and where `transform.position` was being written), the narrower ones are render-only frames between ticks. The position write only happening on the tick-rate cadence is what produced the visible step-stutter on the remote capsule.*

![Profiler, after capsule sync fix](Screenshots/after_capsule_sync.png)
*After fix. Same capture conditions with the position write moved into `LateUpdate`, this time with Deep Profile enabled so Fusion's `Runner.UpdateInternal` is visible by name inside the `PlayerLoop` block. Frame durations are consistent at ~16-18ms (Deep Profile inflates absolute numbers). The relevant comparison is the cadence: position now updates every render frame rather than only on the tick.*

After both changes the NPC movement on the host went from visibly wrong to correct. The `NetworkTransform.Render()` cost is still there but the conflict is gone because the override runs after it in `LateUpdate`.

### Tick Rate and BT Cost

The Behaviour Tree ticks at 10 Hz via a `btTickInterval` timer in `Update()`. Perception still runs every frame because the debug overlay reads Blackboard values directly. A `ProfilerMarker` called `NPC.BehaviourTree.Tick` wraps the tree evaluation so the cost shows up as a named entry. With three NPCs the BT cost was well within budget. The bigger cost is the per-frame raycasts in `UpdatePerception()` (one sight, one audio occlusion per NPC). At ten or more NPCs it would be worth switching to `Physics.RaycastNonAlloc`.

### Capsule Position Sync

Moving the `SyncedPosition` write from `FixedUpdateNetwork` (30 Hz) to `LateUpdate` (per render frame) fixed visible stutter on the remote player. Same write, just timed after the CharacterController has run.

### Real-Time Budget

Performance is fine for a two-player slice with frame rates above 60 FPS on both windows. The honest limitation is that nothing has been tested beyond two clients. The NPC raycasts and the broadcast pattern haven't been evaluated under heavier load, so anything bigger would need profiling against the actual NPC count first.

---

## Section 3: Networking Implementation

### CA2 Baseline and What CA3 Adds

CA2 established the core pattern: `[Networked]` properties for state, RPCs for one-shot requests to StateAuthority, and `ChangeDetector` in `Render()` for client-side visuals. The pickup used all three. CA3 extends that across three more systems and migrates everything from the sandbox scene into the production `02_VerticalSlice` where it has to coexist with the BT NPCs, GameManager, and a real player prefab.

**Networked NPC takedown.** `NpcController` is a `NetworkBehaviour`. The takedown fires `RPC_StartTakedown(RpcTargets.All)` so the death animation and agent disable run on every client, not just the killer's screen.

**Networked PowerBox.** `PowerBoxInteractable` is now a `NetworkBehaviour` with `[Networked] NetworkBool IsActivatedNet`, an `RPC_RequestActivate` to StateAuthority, and a `ChangeDetector` for the spark and light visuals. The repair assignment runs on StateAuthority only because the BT only ticks there.

**SyncedPosition / SyncedNoiseLevel.** The CharacterController is on a child of the NetworkObject root. The root never moves, so a `NetworkTransform` on it just broadcasts the spawn position. Fix was a `[Networked] Vector3 SyncedPosition` on `NetworkPlayerSetup`, written every render frame from the capsule's world position. The same pattern applies to `[Networked] float SyncedNoiseLevel` because `CharacterController.velocity` is only meaningful on the owning client. NPCs read both fields so the host detects and hears remote players correctly.

**Reinforcement.** The NPC reinforcement system uses a plain C# `static event`, not an RPC. It still works across multiplayer because only the state authority ticks the BT, so alerts happen locally on the host and movement decisions flow to clients through the existing position sync.

### Issues Encountered

The two `NetworkTransform` problem was the worst trap. Adding one to both the root and the child made them fight each other and the capsule stopped moving entirely. The rule: one `NetworkTransform` per prefab, and if the root isn't moving, use a `[Networked]` field instead.

A despawn race on pickup also caught me out. The host set `IsPickedUp` and despawned in the same tick, so on the client the `ChangeDetector` sometimes fired after the object was already gone. Fixed by firing `OnWeaponPickedUp` locally on the picker on E press rather than waiting for the round-trip.

### Robustness and Limitations

Two-client local testing passes across all scenarios including simultaneous E presses, client disconnect mid-session, and late-join state replication. The main limitation is latency. Both clients run on localhost so WAN issues like RPC reordering or visible state-write lag aren't exercised. `SyncedPosition` in particular would show lag at real network latency because there's no prediction; the remote player's position would always be one round-trip behind.

### Authentication and Access Control

This is the A4 spec requirement and the validation runs server-side rather than as a UI gate.

**Unity Authentication sign-in.** Before `NetworkRunner.StartGame` is called, the client runs `await AuthenticationService.Instance.SignInAnonymouslyAsync()` which returns a signed JWT. Having the token locally means nothing on its own. The server has to verify it.

**Azure Functions proxy** at `https://fusion-auth-proxy-sam-...azurewebsites.net/api/ValidateToken` receives the token, checks the JWT structure, decodes the payload, and verifies the token has not expired. Valid tokens return Photon ResultCode 1 (allow); missing, malformed, or expired tokens return ResultCode 3 (deny). Full cryptographic signature verification against Unity's JWKS would be a production hardening step but is outside the scope here.

**Fusion dashboard config.** Custom Server Provider points at the proxy URL, "Allow anonymous clients" is off so every connection has to go through the proxy, and "Reject all clients if not available" is enabled so a downed proxy fails closed.

Why this matters: client-side checks can be bypassed by patching the binary, but because validation runs on the Azure proxy rather than in the build, a modified client can't skip it.

![Photon Fusion 2 dashboard with Custom Server Provider configured](Screenshots/FusionAuthentication.png)
*Photon Fusion 2 application dashboard. Custom Server Provider is configured to point at the Azure Functions proxy (`https://fusion-auth-proxy-sam-...azurewebsites.net/api/ValidateToken`) and "Reject all clients if not available" is enabled, so an unreachable proxy fails closed rather than open.*

![Unity Authentication token returned to the client at sign-in](Screenshots/AzureAuthenticationToken.png)
*Unity Authentication anonymous sign-in returning a signed JWT token. This is the value that gets handed to Fusion's connection layer, which forwards it to the Azure proxy for verification before the session join is permitted.*

---

## Section 4: Networking in Modern Engines

### Authority Models: Fusion 2 vs Unreal Engine 5

Both engines solve the same core problem: multiple machines need to agree on shared state, and only one of them should be allowed to make authoritative writes. The structures look similar on the surface but differ underneath.

In Fusion 2 Shared Mode, used in this project, state authority is held per-NetworkObject by a peer rather than by the cloud relay. `[Networked]` properties can only be written by the StateAuthority of an object. Clients request state changes via RPCs. In Unreal 5 with a dedicated server, the server owns all actors by default, so `UPROPERTY(Replicated)` is the equivalent of `[Networked]`, `UPROPERTY(ReplicatedUsing = OnRep_X)` maps to the `ChangeDetector` pattern, and RPCs are annotated as `Server`, `Client`, or `NetMulticast`. The `RPC_StartTakedown` here targeting `RpcTargets.All` is closest to a NetMulticast (Epic Games, 2024).

The structural difference worth flagging: Fusion's InputAuthority can move between peers at runtime. Unreal's Owning Client is fixed at spawn. That flexibility cost me time during CA2, because assigning InputAuthority to the host accidentally blocked the client from triggering the pickup, which came directly from not understanding that distinction (Photon Engine, 2024).

### Lag Compensation and Prediction

The standard toolkit is client-side prediction, server reconciliation, interpolation, extrapolation, and lag compensation. They solve different parts of the same problem and are usually combined (Bernier, 2001).

Client-side prediction runs the local input immediately and reconciles when the authoritative result arrives. Fusion implements this for objects with InputAuthority when prediction is enabled. This project doesn't use Fusion's prediction for the player controller because the CharacterController sits on a child outside Fusion's simulation, so `SyncedPosition` just publishes the result of the local simulation. That means remote players are one round-trip behind. At LAN latency it's imperceptible; at 150ms+ it would become visible. The proper production fix would be to replace the CharacterController with Fusion's `KCC` which integrates with the prediction system (Photon Engine, 2024).

Interpolation smooths remote objects between received snapshots. Extrapolation predicts ahead from last-known velocity when a snapshot is late. Lag compensation rewinds the world to the shooter's view-time for hit detection, the Halo: Reach pattern (Aldridge, 2011); this project doesn't need it because takedowns are melee-range proximity checks.

### Industrial Context

Network programmers are expected to understand the full stack: authority, state synchronisation, prediction and reconciliation, bandwidth budgeting, and the infrastructure under matchmaking and relay services (Glazer and Madhav, 2015). Cloud gaming has moved a lot of this into managed services (AWS GameLift, Photon Cloud, Azure PlayFab).

Deterministic lockstep sidesteps authority entirely by having every client simulate identically from identical inputs, which is used in RTS games where state-sync bandwidth doesn't scale. The trade-off is that any divergence is catastrophic. Large-scale multiplayer systems like Mass Entity push this further with ECS layouts. Scaling the BT NPC in this project to hundreds of agents would need hierarchical LOD on update rates, spatially partitioned perception, and group behaviour abstractions rather than per-agent full BT evaluation.

---

## Section 5: Version Control and Professional Workflow

### CA2 to CA3 Evolution

CA2 set up the core VCS habits: feature branch from main (`feature/ca2-network`), commit prefixes (`feat:`, `fix:`, `docs:`, `net:`), tags at milestones, and keeping main working throughout.

CA3 continued that pattern. `ca3-start` marked where CA2 left off, `ca3-alpha` was tagged when the playable loop (find weapon → takedown → win/defeat) confirmed working, `ca3-profiling-pack` after the profiler captures and the NetworkTransform fixes landed, and `ca3-submit` at final submission.

Being honest: some tags were applied retrospectively to the right commits rather than in the moment. `ca3-alpha` in particular went on a few days after the moment it represents. The commit it points to is correct, but the tag itself was late. A proper professional workflow tags in real time. In a team setting a late tag could confuse other developers about when a milestone was actually reached.

![Repository tags pushed to the remote](Screenshots/Tags.png)
*Tag list on the remote, including the CA1, CA2, and CA3 milestones. `ca3-start`, `ca3-alpha`, `ca3-beta`, `ca3-profiling-pack`, and `ca3-submit` are all visible alongside the prior `ca2-submit` and `ca1-submit` tags from previous coursework.*

> **Commit graph:** the repository's full commit graph spanning Weeks 1-12 is visible on the remote at the GitHub Insights → Network view. Refer to that page rather than a static screenshot, since the graph is interactive and easier to read at full size.

### Workflow Development Across the Module

CA1 had long gaps between commits and inconsistent messages, so a lot of work would pile up before being pushed and the history was hard to read after the fact. CA2 improved this. Committing after each working session and using prefixes consistently meant the history was actually useful during debugging. More than once in CA3 I used `git log` to find the commit where a specific bug was introduced, which would have been much harder with the sprawling CA1 history.

The `.gitignore` also caused a problem early in CA2. The `Library/` folder was partially committed before the ignore rule was set up properly, which inflated the repo size. Cleaned up in Week 7 and not an issue since. Lesson: check what's staged before committing, not just that the diff looks right.

---

## Section 6: Overall Reflection

Looking back, three things stand out from this module.

The hardest technical problem was the slow NPC on the host editor. It appeared the first week of CA3 multi-client testing and took several days to fully diagnose. The initial assumption was that the NavMeshAgent and NetworkTransform were fighting over position, which was partially right. Overriding `Render()` to reassert the NavMesh position fixed the visible jitter, but the NPC was still moving wrong. The second half of the problem, the editor frame throttle dropping the host to 10 FPS when focus shifted to the other client, only became obvious when I started actually profiling rather than reading code. `Application.runInBackground = true` fixed it in one line. There were two bugs with almost identical symptoms, so fixing one made the other more obvious, but you could spend a long time believing it was a single issue. The lesson I took from it is to profile first when something looks wrong at runtime.

The decision I am most satisfied with is the `[Networked] SyncedPosition` pattern. It feels like a proper solution rather than a workaround. The conventional fix would have been restructuring the prefab so the NetworkTransform sits on the moving object, but that creates its own problems with the Cinemachine rig and the First Person Controller hierarchy. Writing a single `[Networked]` field that the input-authority client publishes, and having NPCs read it instead of the transform, is clean. It also made the `SyncedNoiseLevel` fix obvious: same problem, same pattern, different property. Once you have one thing synced that way, the same approach applies to anything else only valid on the owning client.

What I would do differently is start with the prefab hierarchy correct from the beginning of CA2. The position-sync problem and the double NetworkTransform problem both trace back to building the player as a standard FPC prefab and bolting Fusion onto it later. The CharacterController-on-child structure makes sense for single-player but becomes a problem as soon as Fusion needs to know where the player is. The documentation for this exists in the Fusion manual (Photon Engine, 2024). I read it after the problem appeared rather than before setting up the prefab. The same instinct applies to the auth pipeline. I implemented it when CA3 required it instead of treating it as infrastructure to set up at the start of CA2. Server-side validation doesn't interact with gameplay; it could have been built and left running from Week 8 onwards rather than being a CA3 sprint at the end.

The module overall has changed how I think about distributed systems, which I didn't really expect from a Unity course. The fundamental question in networked games is: who is allowed to decide what, and how does everyone else find out? Authority assignment, RPC direction, choosing between `[Networked]` state and a one-shot RPC, putting validation on the server rather than the client. All of it is different ways of answering that one question. When the audio detection was broken for Player 2 the reason was that the wrong machine was being trusted to know the player's speed. Once that's the framing, the fix is obvious.

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

- **Volumetric Fog for URP** by CristianQiu (https://github.com/CristianQiu/Unity-URP-Volumetric-Light.git)
- **Starter Assets, First Person Controller** by Unity Technologies
- **Mixamo SWAT Character and animations** from Adobe Mixamo
- **Abandoned Factory environment**, sourced for CA1 and used throughout

Additional assets and SDKs used in CA2 and CA3:

- **Photon Fusion 2** by Exit Games / Photon Engine
- **Unity Authentication SDK** by Unity Technologies (com.unity.services.authentication)
- **Azure Functions runtime** by Microsoft Azure (HTTP-triggered proxy for token validation)
- **Unity 6 (URP)** by Unity Technologies

---

## Use of AI Assistants

AI tools (primarily Claude) were used during this project as a coding pair-programmer, mainly for working through Fusion-specific API questions, debugging the NetworkTransform and NavMeshAgent conflict, and for drafting and editing sections of this document. The design decisions, such as the authority model, the `[Networked]` sync approach, and the choice to build a server-side validation proxy rather than a client-side gate, were made and reasoned through independently. All debugging interpretations, test observations, and final implementations are my own work. Where AI suggestions were used in code, I read through and understood them before integrating them, and in several cases replaced or reworked the suggestion after testing it against the actual runtime behaviour.
