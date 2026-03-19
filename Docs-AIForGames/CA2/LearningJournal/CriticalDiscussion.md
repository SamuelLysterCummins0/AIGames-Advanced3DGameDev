# Critical Discussion — AI for Games CA2

## Approach and Design Choices

The main decision I made early was to use a pure C# BT rather than the Unity.Behavior visual graph. I tried the graph first because it seemed like the right tool — it has a built-in editor and is how BTs are usually shown in examples. I had persistent issues getting custom nodes to register in the graph editor and after a full session on it I switched to building the tree in code instead.

In hindsight the pure C# approach worked out better for this project. All the CA1 perception and NavMesh code was already written in plain C# scripts, so carrying it forward was straightforward. The NpcBlackboard is just a class with public fields. NpcController runs perception checks every frame and writes the results to the blackboard before the tree ticks, so the condition nodes just read a flag rather than doing their own raycasts. This keeps the perception logic in one place which made it a lot easier to debug.

The interrupt mechanism was simpler than I expected. In the Unity.Behavior graph you need explicit Abort nodes to handle interruption. In the pure C# version, the root BtSelector re-evaluates from child[0] every tick automatically. When the player becomes visible mid-patrol, the Threat branch just wins on the next tick. The patrol node stops being ticked, and when it's entered again its OnEnter method resets it. It handles the interrupt cleanly without needing any extra logic.

The BtCooldown decorator was needed to stop the NPC going straight back into Search immediately after finishing one. Without it, the NPC finishes searching, HasLastKnownPosition briefly stays true, and it enters Search again on the very next frame. Wrapping the search sequence in a 5 second cooldown stops that.

One thing that took more work than expected was getting the Investigate vs Search priority right. Originally the PowerBox branch was higher priority than Search. So after chasing the player, the NPC would skip Search entirely and go straight back to Investigate. The fix was a one-line change to BtCheckPowerBoxActive: if HasLastKnownPosition is true, return Failure. That blocks the Investigate branch until Search finishes and clears the flag.

## What Didn't Go Well

The Unity.Behavior attempt was wasted time. I should have moved on quicker once it was clear the custom nodes weren't registering. Also the debug overlay bug (always showing "None") was there for a while before I noticed. It was a leftover property from the Unity.Behavior version that the overlay was still reading. Simple fix but it would've been caught earlier if I'd tested things as I built them rather than leaving it to the end.

## What I'd Carry Forward to CA3

I'd plan the tree structure more carefully upfront. The investigate/search priority bug happened because I hadn't thought through what should happen when multiple conditions are active at the same time. Drawing the tree out before coding would have caught that.

For CA3 I've started adding more depth to the BT. The reinforcement tracking system was one of the first additions, when one NPC chases the player, nearby NPCs navigate toward the live position and then do a fan search when alerts stop arriving. The game now has a full loop with a weapon pickup, stealth takedown system, win and lose states, and a health system. The NPC coordination is something I want to push further as CA3 continues.
