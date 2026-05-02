# Reflective Document — AI for Games CA3

**Module:** AI for Games  
**Student:** Sam Lyons  
**Submission:** CA3  
**Due:** Sunday, 10 May 2026  

---

## Section 1: Integrated Technical Account

### How Everything Fits Together

The system works in one direction. Perception runs first and writes everything it finds to the Blackboard. The Behaviour Tree reads from the Blackboard to pick a branch. NavMesh carries out whatever movement that branch requests. Nothing inside the tree does its own raycasting or distance calculations. That all happens in `NpcController.UpdatePerception()` before the tree ticks each frame. If the NPC is doing something unexpected, there are only two places to look: either the perception data going into the Blackboard is wrong, or the tree is reading it wrong. That made the whole system much easier to debug than the FSM was.

The FSM from CA1 is gone as a separate system. The states it managed (Patrol, Chase, Attack, Search, Investigate) are still all there but they are BT branches now rather than state objects with explicit transition guards. All of the CA1 perception code, the FOV cone, raycast, audio detection with noise scaling and occlusion, was carried forward without changes. The NavMesh configuration, speed per state and stopping distance, was also kept the same. The BT only replaced the decision layer, not the sensing or movement systems under it.

### Behaviour Tree Structure

The root is a `BtSelector` that re-evaluates its children left to right every tick. The highest-priority branch that returns Running or Success is the one that runs. There are no explicit interrupt or abort nodes. When a higher-priority branch becomes active again, it just wins on the next tick. The tree has five branches:

**Threat** is first. `BtCheckPlayerVisible` gates an inner Selector that picks between Attack (inside attack range) and Chase (further away). A `BtTimeout` wraps the whole threat response with a 30-second limit. If the NPC has been chasing or attacking for 30 seconds without resolving, for example if the player climbs somewhere unreachable, it aborts and falls through to the search branches rather than running forever.

**Audio Search** is second. `BtCheckPlayerHeard` directly gates `BtActionSearch`. This sits above the PowerBox branch on purpose so that hearing the player will interrupt an investigation attempt. When `PlayerHeard` is true, Search tracks the live heard position rather than fanning out.

**Post-Chase Search** is third. A `BtCooldown` decorator (5 seconds) wraps a Sequence that checks `HasLastKnownPosition` and runs `BtActionSearch`. The cooldown stops the NPC immediately re-entering Search after finishing one. Without it, the flag briefly stays true and the NPC drops back in on the very next tick. The cooldown also resets the moment new footsteps are heard, so fresh audio cues are never blocked by a leftover cooldown from an older search.

**PowerBox** is fourth. `BtCheckPowerBoxActive` only returns Success when the box is active and `HasLastKnownPosition` is false. That second check is what forces the NPC to search first after a chase before it goes back to the box. A `BtTimeout` wraps the Investigate action at 60 seconds as a fallback in case the box is somehow unreachable.

**Patrol** is the fallback and always returns Running.

### CA3 Additions

Three things were added for CA3 that were not in the CA2 submission.

**Suspicion.** The Blackboard now has a `SuspicionLevel` float between 0 and 1. Every frame, `AccumulateSuspicion()` checks for partial detection — the player is within detection range but outside the forward FOV cone (peripheral vision), or the player is making noise below the full hearing threshold. Either condition causes suspicion to rise at a set rate. When nothing is detected it decays. Once suspicion crosses the alert threshold (0.6 by default), the NPC sets a last-known position and the PostChaseSearch branch picks it up, even without confirmed sight or audio. The effect in play is that the NPC reacts to vague stimuli gradually rather than flipping instantly between unaware and fully alerted. The debug overlay shows the suspicion bar so this can be observed during a playthrough.

**Reinforcement.** While any NPC is actively chasing the player, it fires a static `OnNpcAlerting` event and broadcasts the current player position on a repeating interval. Nearby NPCs subscribe to this. When they receive an alert they set `ReinforcementTracking` on their Blackboard to true and use it as their own last-known position, which means `BtActionSearch` navigates toward the live position rather than fanning out from a static point. When alerts stop coming, either because the chasing NPC was taken down or it lost the player, each reinforcing NPC clears the flag and falls into a standard fan search. The design from CA2 intentionally kept Blackboards separate between NPCs. This achieves coordination without changing that. NPCs share position data through events but their decision-making stays independent.

**Game loop and takedown.** A `GameManager` handles a four-state loop: `FindWeapon`, `TakedownGuards`, `Win`, and `Defeat`. The player starts without the ability to take down NPCs. Finding and picking up a weapon enables `PlayerTakedownController`. Once all NPCs are dead the win screen appears. If health reaches zero the game moves to Defeat with a restart option. `PlayerTakedownController` checks each frame whether the player is behind a nearby NPC and the NPC is not currently detecting them. On E press, controls lock, the player snaps behind the NPC, animations fire on both characters, and the camera tracks the NPC's head bone as it falls before returning to level and restoring control.

### Screenshots

> **Screenshot 1** — `Screenshots/01_Patrol.png`  
> F1 overlay active. NPC walking patrol route. BT Node: Patrol (green).

> **Screenshot 2** — `Screenshots/02_Chase.png`  
> Player in detection range and FOV. BT Node: Chase (yellow), Sight: SEEN, debug ray visible.

> **Screenshot 3** — `Screenshots/03_Attack.png`  
> NPC within attack range. BT Node: Attack (red).

> **Screenshot 4** — `Screenshots/04_Search.png`  
> Player broke LOS. BT Node: Search (cyan), Has LKP: YES, Destination showing a fan-point coordinate.

---

## Section 2: Performance and Robustness Evaluation

### Tick Rate

The BT ticks on a configurable interval (`btTickInterval`, defaulting to 0.1 seconds, or ten ticks per second), controlled by a timer in `Update()`. Perception still runs every frame because it needs to react to the player quickly and because other systems like the debug overlay read from the Blackboard directly. The tree itself only processes at the lower rate. Ten ticks per second is enough for believable NPC reaction time and noticeably cheaper than running the full tree at 60fps, especially with three NPCs in the scene. The `NPC.BehaviourTree.Tick` profiler marker in `NpcController` makes this cost visible in the Unity Profiler without needing deep profiling enabled.

### Profiling Evidence

> **Profiler capture** — `ProfilerCaptures/BtTickProfiler.png`  
> *(Unity CPU Profiler with Play Mode active during a Chase/Attack sequence. Expand `NpcController.Update` in the hierarchy to see the BT tick cost under the `NPC.BehaviourTree.Tick` marker.)*

The most significant single cost in the AI stack is the per-frame raycasts in `UpdatePerception()`. Each NPC fires at least one raycast for line-of-sight and another for audio occlusion when that setting is on. With three NPCs that is up to six raycasts per frame. In profiling that was well within budget for three agents, but it would become a problem at ten or more. Switching to `Physics.RaycastNonAlloc` and batching the queries would be the first thing to address if scaling up.

### Edge Cases

**Player out of reach during Chase.** If the player climbs onto the forklift or stacks of barrels, the NavMesh agent stalls because there is no valid path. The threat branch's `BtTimeout` at 30 seconds handles this. After the limit it returns Failure, which drops the Selector through to Audio Search or PostChaseSearch. The NPC searches the last known position instead of standing still. Without this the NPC would stay frozen in the chase branch until the player came back down.

**Immediate re-entry into Search.** After the first version of the search branches was working, the NPC was finishing a search and then dropping straight back into one because `HasLastKnownPosition` was still true on the next tick. The `BtCooldown` decorator fixed this — it blocks re-entry into PostChaseSearch for five seconds after the branch finishes. The cooldown resets on fresh audio so it does not block the NPC from reacting to new footsteps.

**LOS lost during Attack.** The attack node does not stop firing the instant LOS breaks. It starts a timer (`_losLostStartTime`) and only stops after a sustained gap — controlled by `losLostThreshold`. This stops the NPC flickering between Attack and Chase every time the player briefly ducks behind a crate. Without it the two states were switching back and forth on every frame that had even a thin obstacle between the NPC and the player.

**Investigate interrupted then resumed.** `BtActionInvestigate` resets to `MoveToBox` phase every time it enters via `OnEnter()`. If the NPC breaks off to chase the player and then loses them, it walks all the way back to the box and starts the approach from scratch. Resuming from a saved phase mid-investigation would have been complicated to get right and the extra walk reads naturally in the encounter anyway.

---

## Section 3: AI in Modern Engines and Beyond

### Unity and Unreal Engine 5

Unity 6 and Unreal Engine 5 cover the same ground at a high level — NavMesh, Blackboard, Behaviour Tree — but the way those pieces are actually provided is quite different.

In Unity, AI Navigation handles the NavMesh and does the job well. The runtime obstacle carving system, used in this project for the forklift barrels, works without issues. The Behaviour Tree layer is a different story. Unity.Behavior was released as production-ready in 2024 and has a visual graph editor that looks similar to Unreal's. During CA2 development I spent a full session trying to get custom nodes to register in the graph editor and could not get it working. The package is new enough that there is very little community documentation to fall back on when things go wrong. The pure C# implementation ended up being the better choice for this project because all the CA1 code was already in plain C# and there was no dependency on the graph tool.

Unreal's Behaviour Tree editor is a first-party tool that has shipped with the engine for years and has a much larger ecosystem of documentation and examples. There are two structural differences that stand out compared to how this project was built. The first is **Service nodes** — a node type that runs on a timer inside a composite node and updates the Blackboard periodically. In this project that role is filled by `UpdatePerception()` running every frame before the tree ticks. Services are more flexible because you can configure the tick rate per node rather than having a single global tick rate, which means expensive perception checks can run less often than cheap flag reads. The second is the **Environment Query System (EQS)**. EQS lets a BT node issue a spatial query — find the position in this radius that is farthest from the player and not in their line of sight, for example — and get the best result back as a Blackboard value. In this project that logic is hand-coded in `BtActionSearch`: fan points are generated around the last known position using the player's last movement direction with random jitter. EQS would express the same intent through a query definition rather than procedural code and handles edge cases like off-NavMesh results automatically (Epic Games, 2024; Unity Technologies, 2024).

### AI in Military and Industrial Training

Game AI techniques have been used in military training simulation for longer than most modern commercial game AI has existed. Zyda (2005) traces how real-time simulation technology developed first in the military context and then fed into commercial games, with the direction of influence later reversing as games developed more sophisticated techniques. Smith (2010) gives a detailed account of how this relationship evolved, noting that engines and AI systems originally built for training were adapted into entertainment products and vice versa.

**VBS4** (Virtual Battlespace 4) by Bohemia Interactive Simulations is one of the most widely deployed examples. It is used by the US Army, UK Ministry of Defence, Australian Defence Force, and several NATO allies for individual and collective training (Bohemia Interactive Simulations, 2023). The engine is built on the same lineage as the Arma series and uses behaviour trees for individual soldier AI. The key constraint in VBS4 that does not apply in commercial games is doctrine fidelity. Enemy NPCs have to behave in ways that reflect how real threat actors actually operate. If an AI soldier does something tactically unrealistic, the training event loses its transfer value — soldiers learn to exploit the AI's quirks rather than developing skills that apply to real situations. Designing AI that is challenging enough to be useful but predictable enough to be trainable is a different problem from designing AI that simply feels hard or fun.

Industrial and vocational training presents similar constraints in a different setting. Simulator-based training in sectors like aviation, emergency services, and healthcare requires that training events are logged and auditable. A trainee working through a flight simulator scenario has to be assessed against objective pass criteria, and the record of what the AI did during that session — which failure it injected, when, and with what parameters — has to be reproducible for certification purposes. Smith (2010) notes this as a core distinction from commercial games: the AI's internal decisions need to be surfaced and explained to an instructor or assessor, not just experienced by a player. That requirement changes the design from the ground up. The debug overlay in this project, which shows the active BT node, Blackboard values, perception state, and nav destination in real time, exists as a development tool. In a training simulator context that kind of observability would be a product requirement rather than an optional debug feature.

---

## Section 4: Carry-Forward and Professional Reflection

### One Technical Improvement

The reinforcement system works through loose event broadcasting — NPCs share the player's position but each still makes decisions independently. The limitation is that two NPCs can end up chasing the player by the same route at the same time, because neither knows what the other is doing. A proper fix would be a lightweight coordination layer that sits above the individual BTs — not a shared Blackboard, but a manager class that NPCs can query to find out whether another agent already has the player in sight and what they are doing about it. That would allow one NPC to cover a different exit while another gives chase, which is much closer to how real squad tactics work and would make the multi-NPC encounter significantly harder to exploit.

### What the Serious Games Research Changed

Looking at how training simulators handle AI observability made me think differently about the debug overlay. When I built it the intention was to make development easier — a way to see what the NPC was doing without adding breakpoints. In a training context the instructor needs exactly the same information during a live session. The difference is that in development I can turn it off before shipping. In a training simulator it stays on, gets refined, and becomes part of the product.

That reframe is useful because it means the design thinking behind a training AI is not fundamentally different from what was done here — the systems are the same, they just have different audiences. The Blackboard structure and the BT branching logic would translate directly. What would need to change is the audit trail: the system would need to log every decision and every perception event so a trainer could replay the session afterwards.

### Looking Back

The FSM from CA1 had a problem I did not recognise until CA2: I had built the states individually without thinking through what should happen when multiple conditions were true at the same time. The Investigate vs Search priority bug — where the NPC skipped searching after a chase and went straight back to the box — came from exactly that. I had not drawn out the interaction before coding it.

By CA3 I was planning the data flow first. The suspicion system and reinforcement system were both mapped out at the Blackboard level before any code was written — what fields they would write, what other systems would read them, and what the expected state transitions were. Neither needed significant rework. That is probably the most transferable thing from this module: working out what the data is doing before deciding how the logic should respond to it.

---

## References

Bohemia Interactive Simulations (2023) *VBS4: The Military Training Platform* [Online]. Available at: https://bisimulations.com/products/vbs4 (Accessed: 27 April 2026).

Epic Games (2024) *Behaviour Trees in Unreal Engine* [Online]. Available at: https://dev.epicgames.com/documentation/en-us/unreal-engine/behavior-trees-in-unreal-engine (Accessed: 27 April 2026).

Smith, R. (2010) 'The long history of gaming in military training', *Simulation & Gaming*, 41(1), pp. 6–19.

Unity Technologies (2024) *AI Navigation* [Online]. Available at: https://docs.unity3d.com/Manual/com.unity.ai.navigation.html (Accessed: 27 April 2026).

Zyda, M. (2005) 'From visual simulation to virtual reality to games', *Computer*, 38(9), pp. 25–32.
