# Technical Note — AI for Games CA2

## Behaviour Tree Structure

The NPC decision-making is handled by a pure C# Behaviour Tree. The root is a BtSelector, which evaluates its children left to right and stops at the first one that returns Running or Success. This gives a natural priority order, higher-priority branches win automatically, and there's no need for explicit interrupt or abort nodes.

There are four branches in priority order:

**Threat** is the highest priority. BtCheckPlayerVisible checks whether the player is within detection range, inside the NPC's FOV cone, and not blocked by geometry. If the player is visible, an inner BtSelector picks between attack (if in close range) and chase (if further away). The NPC runs toward the player during chase and strafes to hold a preferred shooting distance during attack.

**PowerBox** handles the investigation mechanic. BtCheckPowerBoxActive returns Success only when a power box fault is active and the NPC has no last known player position. The second condition stops the NPC going straight to Investigate after a chase, it has to search first.

**Search** wraps a BtSequence inside a BtCooldown decorator. The sequence checks HasLastKnownPosition and runs BtActionSearch if true. The cooldown prevents the NPC immediately re-entering Search after a failed search by blocking the branch for five seconds after it finishes.

**Patrol** is the fallback. BtActionPatrol always returns Running, so the NPC walks its waypoints whenever none of the higher-priority branches are active.

## Perception and the Blackboard

Rather than having condition nodes do their own raycasts, all perception runs in NpcController.UpdatePerception() each frame before the tree ticks. This writes results to the NpcBlackboard, a plain C# class with fields like PlayerVisible, PlayerHeard, HasLastKnownPosition, and LastKnownPlayerPosition.

Visual detection uses a distance check, a FOV angle check against the NPC's forward vector, and a raycast to confirm line of sight isn't blocked. Audio detection calculates an effective noise level from the player's movement speed, attenuated by distance, with an optional occlusion multiplier if a raycast finds a wall between the NPC and the player.

The blackboard also stores the last known movement direction of the player, which BtActionSearch uses to generate fan-shaped search points in the direction the player was heading when last seen.

## Navigation

NavMeshAgent handles all movement. The NPC walks during patrol and runs during chase and search. BtActionSearch generates up to four search points around the last known position, one at the position itself and the rest fanned out in the player's last movement direction with random jitter added to avoid a predictable pattern.

## Design Trade-off

The main trade-off was keeping each NPC's blackboard separate rather than shared. A shared blackboard would allow NPCs to directly coordinate, for example, NPC B immediately knowing where the player is because NPC A can see them. I didn't go that route for CA2 because the brief was focused on single-NPC behaviour and a shared state would have added complexity around synchronisation and priority that wasn't needed.

Instead, coordination is handled indirectly through a reinforcement alert system. When one NPC is chasing the player, it broadcasts position updates that nearby NPCs can subscribe to. This achieves the practical effect of NPCs converging on the same area without tightly coupling their internal state. It also degrades gracefully, if the chasing NPC is taken down, the others switch to a local fan search rather than getting stuck waiting for alerts that will never arrive.
