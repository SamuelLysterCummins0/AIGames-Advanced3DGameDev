# Reflective Document: AI for Games CA3

**Module:** AI for Games  
**Student:** Sam Lyster-Cummins  
**Student Number:** 20102256  
**Submission:** CA3  
**Due:** Sunday, 10 May 2026  

---

## Section 1: Integrated Technical Account

### How Everything Fits Together

The system runs in one direction each frame. Perception writes to the Blackboard, the Behaviour Tree reads from it to pick a branch, and NavMesh carries out the movement. Nothing inside the tree does its own raycasting or distance checks. That all happens in `NpcController.UpdatePerception()` before the tree ticks. If the NPC misbehaves there are only two places to look: the data going into the Blackboard or the tree reading it wrong. That made it much easier to debug than the FSM was.

The CA1 FSM is gone as a separate system. The states it managed (Patrol, Chase, Attack, Search, Investigate) are now BT branches. All the CA1 perception code (FOV cone, raycast, audio detection with noise scaling and occlusion) carried forward unchanged, as did the NavMesh setup. The BT only replaced the decision layer.

### Behaviour Tree Structure

The root is a `BtSelector` that re-evaluates left to right every tick. There are no explicit aborts. A higher-priority branch just wins on the next tick when its condition becomes true.

**Threat** is first. `BtCheckPlayerVisible` gates an inner Selector that picks Attack (inside range) or Chase. A `BtTimeout` of 30 seconds wraps the whole branch so the NPC can't get stuck chasing a player who climbed somewhere unreachable.

**Audio Search** is second. `BtCheckPlayerHeard` gates `BtActionSearch`. Sits above PowerBox so hearing the player interrupts an investigation. When `PlayerHeard` is true, Search tracks the live heard position rather than fanning out.

**Post-Chase Search** is third. A `BtCooldown(5s)` wraps a Sequence checking `HasLastKnownPosition`. The cooldown stops re-entry the moment the search finishes. It resets on fresh audio so new footsteps are never blocked by leftover cooldown.

**PowerBox** is fourth. `BtCheckPowerBoxActive` returns Success only when the box is active and `HasLastKnownPosition` is false. That second check forces a search first after a chase before going back to the box. A `BtTimeout(60s)` is the fallback for unreachable boxes.

**Patrol** is the always-running fallback.

### CA3 Additions

Four things were added for CA3.

**Player-activated PowerBox distraction.** The PowerBox already existed as something NPCs reacted to. In CA3 the player can also press E to activate one, firing the same static `OnPowerBoxActivated` event the NPC already listens for. From the NPC's side nothing changes. From the player's side it's a tactical interrupt, so activating a distant box pulls a guard away from a takedown route. It also turned out to be the cleanest way to demonstrate the BT re-evaluating priorities mid-encounter.

**Spot Timer.** The detection system was reworked to make sight feel less binary. The Blackboard has a `SpotTimer` float that fills while the player is in line of sight and drains when they're not. Fill rate is scaled by distance and angle, so a player directly in front at point-blank fills the bar in about a quarter of a second, while a player at the edge of the cone at long range takes several seconds. Two thresholds drive the BT: at 50% the NPC sets an LKP and enters Search, at 100% the NPC commits to Chase. A small 0.3s grace stops one-frame LOS dropouts from resetting it unfairly. A `SpotTracking` flag tells `BtActionSearch` to live-track the player when re-acquired mid-search rather than continuing to a stale fan-out point. The earlier CA3 version used a `SuspicionLevel` float, but in practice it filled too gradually to feel meaningful. The Spot Timer replaces it.

**Reinforcement.** A chasing NPC fires a static `OnNpcAlerting` event with the current player position on a repeating interval. Nearby NPCs receiving the alert set `ReinforcementTracking` true and use the broadcast position as their own LKP, so `BtActionSearch` navigates to the live area rather than a fan from a stale point. When alerts stop coming the flag clears and they fall back to standard fan search. NPCs share position data through events but their decision-making stays independent. Each one keeps its own Blackboard.

**Game loop and takedown.** A `GameManager` handles a four-state loop: `FindWeapon`, `TakedownGuards`, `Win`, `Defeat`. Picking up the weapon enables `PlayerTakedownController`. On E behind an undetecting NPC, controls lock, the player snaps behind, animations fire on both characters, and the camera tracks the NPC's head bone as it falls before restoring control.

### Screenshots

![Patrol with three NPCs visible and debug overlay showing the Patrol branch](Screenshots/All3NpcOverlayAndPatrolBranch.png)
*Patrol. F1 overlay active across all three NPCs. BT Node: Patrol (green). Spot timer at zero, no LKP.*

![Chase, NPC in line of sight chasing the player](Screenshots/ChaseBranch.png)
*Chase. Player inside detection range and FOV cone. BT Node: Chase (yellow), Sight: SEEN, debug ray visible from NPC eye to player.*

![Attack, NPC firing at the player from inside attack range](Screenshots/AttackBranch.png)
*Attack. NPC within attack range and holding the shooting distance. BT Node: Attack (red).*

![Search, NPC investigating the last known position after losing LOS](Screenshots/SearchBranch.png)
*Search. Player broke line of sight. BT Node: Search (cyan), Has LKP: YES, destination showing one of the fan-out search points.*

![Investigate, NPC walking to power box after activation](Screenshots/InvestigateBranch.png)
*Investigate. Power box has been activated (sparks visible) and the eligible NPC has broken off patrol to repair it. BT Node: Investigate.*

![Reinforcement, second NPC converging on alert position from another spotter](Screenshots/ReinforcingOverlay.png)
*Reinforcement. One NPC has spotted the player and broadcast an alert. A second NPC inside the reinforce range has picked up the LKP and converges on the same area. ReinforcementTracking flag visible on the overlay.*

---

## Section 2: Performance and Robustness Evaluation

### Tick Rate

The BT ticks on a configurable interval (`btTickInterval`, defaulting to 0.1 seconds, or ten ticks per second), controlled by a timer in `Update()`. Perception still runs every frame because it needs to react to the player quickly and because other systems like the debug overlay read from the Blackboard directly. The tree itself only processes at the lower rate. Ten ticks per second is enough for believable NPC reaction time and noticeably cheaper than running the full tree at 60fps, especially with three NPCs in the scene. The `NPC.BehaviourTree.Tick` profiler marker in `NpcController` makes this cost visible in the Unity Profiler without needing deep profiling enabled.

### Profiling Evidence

![BT tick cost in the Unity CPU Profiler](Screenshots/BtTickProfiler.png)
*Unity CPU Profiler captured during a Chase/Attack sequence. The `NPC.BehaviourTree.Tick` marker (custom `ProfilerMarker` defined in `NpcController`) shows the per-tick cost of the entire Behaviour Tree evaluation. Because the BT only ticks at `btTickInterval` (0.1s), the marker fires roughly every six render frames rather than every frame.*

The most significant single cost in the AI stack is the per-frame raycasts in `UpdatePerception()`. Each NPC fires at least one raycast for line-of-sight and another for audio occlusion when that setting is on. With three NPCs that is up to six raycasts per frame. In profiling that was well within budget for three agents, but it would become a problem at ten or more. Switching to `Physics.RaycastNonAlloc` and batching the queries would be the first thing to address if scaling up.

### Edge Cases

**Player out of reach during Chase.** If the player climbs onto the forklift or stacks of barrels, the NavMesh agent stalls because there is no valid path. The threat branch's `BtTimeout` at 30 seconds handles this. After the limit it returns Failure, which drops the Selector through to Audio Search or PostChaseSearch. The NPC searches the last known position instead of standing still. Without this the NPC would stay frozen in the chase branch until the player came back down.

**Immediate re-entry into Search.** After the first version of the search branches was working, the NPC was finishing a search and then dropping straight back into one because `HasLastKnownPosition` was still true on the next tick. The `BtCooldown` decorator fixed this. It blocks re-entry into PostChaseSearch for five seconds after the branch finishes. The cooldown resets on fresh audio so it does not block the NPC from reacting to new footsteps.

**LOS lost during Attack.** The attack node does not stop firing the instant LOS breaks. It starts a timer (`_losLostStartTime`) and only stops after a sustained gap, controlled by `losLostThreshold`. This stops the NPC flickering between Attack and Chase every time the player briefly ducks behind a crate. Without it the two states were switching back and forth on every frame that had even a thin obstacle between the NPC and the player.

**Investigate interrupted then resumed.** `BtActionInvestigate` resets to `MoveToBox` phase every time it enters via `OnEnter()`. If the NPC breaks off to chase the player and then loses them, it walks all the way back to the box and starts the approach from scratch. Resuming from a saved phase mid-investigation would have been complicated to get right and the extra walk reads naturally in the encounter anyway.

---

## Section 3: AI in Modern Engines and Beyond

### Unity and Unreal Engine 5

Unity 6 and Unreal Engine 5 cover the same ground at a high level (NavMesh, Blackboard, Behaviour Tree) but the way those pieces are actually provided is quite different.

In Unity, AI Navigation handles the NavMesh and does the job well. The runtime obstacle carving system, used in this project for the forklift barrels, works without issues. The Behaviour Tree layer is a different story. Unity.Behavior was released as production-ready in 2024 and has a visual graph editor that looks similar to Unreal's. During CA2 development I spent a full session trying to get custom nodes to register in the graph editor and could not get it working. The package is new enough that there is very little community documentation to fall back on when things go wrong. The pure C# implementation ended up being the better choice for this project because all the CA1 code was already in plain C# and there was no dependency on the graph tool.

Unreal's Behaviour Tree editor is a first-party tool that has shipped with the engine for years and has a much larger ecosystem of documentation and examples. There are two structural differences that stand out compared to how this project was built. The first is **Service nodes**, which are nodes that run on a timer inside a composite node and update the Blackboard periodically. In this project that role is filled by `UpdatePerception()` running every frame before the tree ticks. Services are more flexible because you can configure the tick rate per node rather than having a single global tick rate, which means expensive perception checks can run less often than cheap flag reads. The second is the **Environment Query System (EQS)**. EQS lets a BT node issue a spatial query, for example "find the position in this radius that is farthest from the player and not in their line of sight", and get the best result back as a Blackboard value. In this project that logic is hand-coded in `BtActionSearch`: fan points are generated around the last known position using the player's last movement direction with random jitter. EQS would express the same intent through a query definition rather than procedural code and handles edge cases like off-NavMesh results automatically (Epic Games, 2024; Unity Technologies, 2024).

### AI in Military and Industrial Training

Game AI techniques have been used in military training simulation for longer than most modern commercial game AI has existed. Zyda (2005) traces how real-time simulation technology developed first in the military context and then fed into commercial games, with the direction of influence later reversing as games developed more sophisticated techniques. Smith (2010) gives a detailed account of how this relationship evolved, noting that engines and AI systems originally built for training were adapted into entertainment products and vice versa.

**VBS4** (Virtual Battlespace 4) by Bohemia Interactive Simulations is one of the most widely deployed examples. It is used by the US Army, UK Ministry of Defence, Australian Defence Force, and several NATO allies for individual and collective training (Bohemia Interactive Simulations, 2023). The engine is built on the same lineage as the Arma series and uses behaviour trees for individual soldier AI. The key constraint in VBS4 that does not apply in commercial games is doctrine fidelity. Enemy NPCs have to behave in ways that reflect how real threat actors actually operate. If an AI soldier does something tactically unrealistic, the training event loses its transfer value, because soldiers learn to exploit the AI's quirks rather than developing skills that apply to real situations. Designing AI that is challenging enough to be useful but predictable enough to be trainable is a different problem from designing AI that simply feels hard or fun.

Industrial and vocational training presents similar constraints in a different setting. Simulator-based training in sectors like aviation, emergency services, and healthcare requires that training events are logged and auditable. A trainee working through a flight simulator scenario has to be assessed against objective pass criteria, and the record of what the AI did during that session (which failure it injected, when, and with what parameters) has to be reproducible for certification purposes. Smith (2010) notes this as a core distinction from commercial games: the AI's internal decisions need to be surfaced and explained to an instructor or assessor, not just experienced by a player. That requirement changes the design from the ground up. The debug overlay in this project, which shows the active BT node, Blackboard values, perception state, and nav destination in real time, exists as a development tool. In a training simulator context that kind of observability would be a product requirement rather than an optional debug feature.

---

## Section 4: Carry-Forward and Professional Reflection

### One Technical Improvement

The reinforcement system works through loose event broadcasting. NPCs share the player's position but each still makes decisions independently. The limitation is that two NPCs can end up chasing the player by the same route at the same time, because neither knows what the other is doing. A proper fix would be a lightweight coordination layer that sits above the individual BTs. Not a shared Blackboard, but a manager class that NPCs can query to find out whether another agent already has the player in sight and what they are doing about it. That would allow one NPC to cover a different exit while another gives chase, which is much closer to how real squad tactics work and would make the multi-NPC encounter significantly harder to exploit.

### What the Serious Games Research Changed

Looking at how training simulators handle AI observability made me think differently about the debug overlay. When I built it the intention was to make development easier, a way to see what the NPC was doing without adding breakpoints. In a training context the instructor needs exactly the same information during a live session. The difference is that in development I can turn it off before shipping. In a training simulator it stays on, gets refined, and becomes part of the product.

That reframe is useful because it means the design thinking behind a training AI is not fundamentally different from what was done here. The systems are the same, they just have different audiences. The Blackboard structure and the BT branching logic would translate directly. What would need to change is the audit trail: the system would need to log every decision and every perception event so a trainer could replay the session afterwards.

### Looking Back

The FSM from CA1 had a problem I did not recognise until CA2: I had built the states individually without thinking through what should happen when multiple conditions were true at the same time. The Investigate vs Search priority bug, where the NPC skipped searching after a chase and went straight back to the box, came from exactly that. I had not drawn out the interaction before coding it.

By CA3 I was planning the data flow first. The spot timer and reinforcement system were both mapped out at the Blackboard level before any code was written, so I knew what fields they would write, what other systems would read them, and what the expected state transitions were. Neither needed significant rework. That is probably the most transferable thing from this module: working out what the data is doing before deciding how the logic should respond to it.

---

## References

Bohemia Interactive Simulations (2023) *VBS4: The Military Training Platform* [Online]. Available at: https://bisimulations.com/products/vbs4 (Accessed: 27 April 2026).

Epic Games (2024) *Behaviour Trees in Unreal Engine* [Online]. Available at: https://dev.epicgames.com/documentation/en-us/unreal-engine/behavior-trees-in-unreal-engine (Accessed: 27 April 2026).

Smith, R. (2010) 'The long history of gaming in military training', *Simulation & Gaming*, 41(1), pp. 6–19.

Unity Technologies (2024) *AI Navigation* [Online]. Available at: https://docs.unity3d.com/Manual/com.unity.ai.navigation.html (Accessed: 27 April 2026).

Zyda, M. (2005) 'From visual simulation to virtual reality to games', *Computer*, 38(9), pp. 25–32.
