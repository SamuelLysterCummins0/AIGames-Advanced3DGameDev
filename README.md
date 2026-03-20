# AI For Games – Advanced Game Dev 2026

## Unity Version
Unity 6 (6000.3.6f1)

## Packages Used
- AI Navigation – NavMesh baking and NavMeshAgent
- Input System – Player movement and interaction
- Universal Render Pipeline (URP)
- Post Processing
- URP Volumetric Fog (CristianQiu) – Atmospheric fog via git package
- Photon Fusion 2 – Multiplayer networking (CA2 Advanced 3D)

## How to Open and Run
1. Clone or download the repository
2. Open Unity Hub and add the project from the `AIGames-Advanced3DGameDev` folder
3. For the AI for Games CA2 scene open: `Assets/Scenes/02_VerticalSlice`
4. For the networking scene open: `Assets/Scenes/01_Sandbox/CA2_NetworkTest`
5. Press Play

## Controls
| Input | Action |
|-------|--------|
| WASD | Move |
| Mouse | Look |
| Shift | Sprint |
| E | Pick up weapon / interact with power box |
| F | Takedown (when prompted, weapon required) |
| F1 | Toggle per-NPC debug overlay |
| F2 | Toggle all-NPC debug overlay |

## Player Objective
Find the weapon that has spawned somewhere in the level, pick it up, then take down all three NPCs to win. The NPCs will chase and attack if they spot or hear you. If you lose them they will search the area before returning to patrol. Activating the power box will send the nearest NPC to investigate and repair it.

## CA2 — AI for Games
The FSM from CA1 has been replaced with a pure C# Behaviour Tree. All NPC values are configurable in the Inspector via `NpcConfig`.

**BT branch priority (high to low):**
1. Threat — chase and attack when player is visible
2. Audio search — navigate to heard position while player is audible
3. Post-chase search — fan search after losing the player (5s cooldown)
4. Investigate — walk to and repair the power box
5. Patrol — default fallback


