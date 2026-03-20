# Evidence Log — AI for Games CA2

---
## Week 5 

Finished off the last two CA1 features this week. The forklift/barrel system took most of the session, getting the NavMeshObstacle carving to only activate when a barrel had settled took a bit of trial and error. The Investigate state for the power box also got completed and tested. Both features working before the CA2 handout drops.

---

## Week 6

Started looking into Behaviour Trees this week. Watched some videos on how BTs are used in games. The plan was to use the Unity.Behavior package since it has a visual graph editor and seemed like the easiest way to get started.

Spent most of the session mapping out what the tree would need to look like based on the CA1 states. The four main branches I settled on: patrol as the default fallback, investigate for the power box, search for when the player is lost, and a threat branch handling chase and attack.

---

## Week 6

Installed the Unity.Behavior package and started writing custom node scripts for things like NpcDetectPlayerAction and NpcInvestigateAction. The nodes compiled fine but wouldn't show up in the graph editor. Spent the rest of the session on it — tried rebuilding, restarting Unity, checking the node attributes — but couldn't get it working.

Eventually figured it was probably something to do with compile errors elsewhere blocking the package from registering custom types, but couldn't pin down the exact cause.

**Resolution:** Dropped the graph approach and decided to build the BT in plain C# instead. The node scripts were already written, they just needed different base classes.

---

## Week 7 

Switched to the pure C# approach. The core structure classes were already partly done (BtNode, BtSelector, BtSequence, BtDecorator) so the main job this week was wiring them together and setting up the NpcBlackboard.

The Blackboard is a plain C# class. NpcController writes all perception results to it before the BT ticks each frame, so the condition nodes just read a flag rather than doing their own raycasts.

Also worked on the Advanced 3D CA2 networking feature this session, the networked pickup using Photon Fusion 2.

![BT Files](BehaviourTree.png)

---

## Week 8 

Finished the full BT and got all branches working in play mode.

Two bugs found and fixed during testing:

First, the debug overlay was always showing "None" for the active node. It was reading from `NpcController.ActiveNodeName`, a leftover from the Unity.Behavior version, instead of `Blackboard.ActiveNodeName` where the BT nodes actually write. One line fix.

Second, after chasing the player the NPC was going straight back to Investigate instead of searching first. The PowerBox branch was higher priority than Search, so when the box was still active it always won. Fixed by adding a check to BtCheckPowerBoxActive: if HasLastKnownPosition is true, return Failure. That makes the Selector fall through to Search, and Investigate only resumes after the search clears the flag.

Also added NPC reinforcement tracking this week, when one NPC is chasing the player, nearby NPCs navigate to the live player position and then do a fan search when the alerts stop. On top of that, started CA3 alpha work: weapon spawner, weapon pickup, stealth takedown system, game loop with win and lose states, and a player health system with a health bar, edge vignette, and health regeneration.

![Profiler showing BT tick cost](profiler.png)

