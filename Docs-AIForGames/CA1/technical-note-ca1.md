# Technical Note — AI for Games CA1

## FSM States and Transition Guards

The NPC uses a custom finite state machine with six states: Idle, Patrol, Chase, Attack, Search, and Investigate.

**Idle** is the starting state. The NPC stands still for a short duration before automatically moving to Patrol.

**Patrol** moves the NPC between a set of waypoints in order. Each frame it checks for the player using a combination of detection range, field of view angle, and a line-of-sight raycast. If all three conditions are met the NPC switches to Chase. The NPC can also hear the player — if the player's noise level (higher when running) exceeds a threshold within the hearing range, it also transitions to Chase. When returning to Patrol after a chase, the NPC resumes from the closest waypoint rather than backtracking to wherever it was before.

**Chase** runs the NPC toward the player's last known position. It transitions to Attack when the player is within attack range, or to Search if the player moves out of detection range for long enough.

**Attack** plays an attack animation and applies damage on cooldown. It returns to Chase if the player moves out of attack range.

**Search** sends the NPC to several points around the last known player position. Once all points are visited without finding the player, it returns to Patrol.

**Investigate** is triggered by a static C# event fired from the PowerBoxInteractable when a player activates a power box.

When the player presses E within interaction range of the box, the `PowerBoxInteractable` component activates. It starts an electricity spark `ParticleSystem` attached to the box and fires a static `OnPowerBoxActivated` event, passing a reference to itself. Using a static event here means the NPC and the light system can both subscribe without needing direct references to each other, which keeps the setup clean (Observer Pattern). A `LightFlickerController` on the nearby lights subscribes to this event and begins flickering them by rapidly toggling their enabled state on a short random interval, simulating an electrical fault caused by the box. The NPC controller also subscribes and immediately transitions the NPC into the Investigate state, passing the box reference so the NPC knows where to go.

The Investigate state has its own internal phase flow: MoveToBox, LookAround, Fixing, and Complete. The NPC walks to a designated stand position in front of the panel, then stops and does a 180-degree rotation sweep to check the surrounding area, then plays a fix animation until the repair is done. The look-around sweep is driven by a sine function applied to the NPC's Y rotation each frame over a set duration. To prevent the Animator's root motion overwriting the manual rotation, the sweep and the box-facing rotation during the fix phase are both re-applied in `LateUpdate`, which runs after Unity's animation step.

Once the fix animation completes, the NPC calls `FixPowerBox()` on the box. This stops the spark particles and fires the `OnPowerBoxFixed` event. The `LightFlickerController` subscribes to this event and stops flickering, restoring the lights to their normal state. At any point during investigation, if the player is spotted or heard, the NPC immediately switches to Chase, leaving the box still active. Once it loses the player and finishes searching, it returns to investigate the same box until the fix is complete.

## Navigation Approach and Complication

Navigation is handled with Unity's NavMeshAgent on the NPC and a baked NavMesh across the level. The NPC's speed and stopping distance are configured per-state, so it walks during patrol and investigation but runs during chase.

The main navigation complication is the forklift. A forklift vehicle patrols between a centre point and four outer positions. As it moves, barrels roll off and settle on the ground. Each barrel has a NavMeshObstacle component with carving enabled, so once a barrel stops moving it cuts a hole into the NavMesh and forces the NPC to route around it. The obstacles build up over time, making previously straight paths unavailable. Once the maximum number of barrels is reached, the oldest one dissolves using a dissolve shader, removing its obstacle so the NavMesh recovers that area.

A bug where the carving on the forklift itself was causing the NPC to glitch during navigation was fixed by only enabling carving on the forklift when it is stationary.

## Design Trade-off

One thing I chose not to implement was coordinated behaviour between multiple NPCs. It would have been possible to have a second NPC respond if the first one triggered an alert, which would make the encounter harder to exploit. I decided against it because it would have required a shared alert system, which felt out of scope given the time available and the single-NPC focus of the brief. Instead I put the extra effort into making the single NPC's state transitions feel convincing, particularly the investigation mechanic and the return-to-investigate behaviour after a chase.
