# AI For Games and Advanced 3D Game Dev — CA3

Single Unity project covering CA3 for both the AI for Games and Advanced 3D Game Development modules.

## Unity Version
Unity 6 (6000.3.6f1)

## Packages Used
- AI Navigation, for NavMesh baking and NavMeshAgent
- Input System, for player movement and interaction
- Universal Render Pipeline (URP)
- Post Processing
- URP Volumetric Fog (CristianQiu), atmospheric fog via git package
- Photon Fusion 2, multiplayer networking
- Unity Authentication SDK, anonymous sign-in before session connect
- Cinemachine, first-person camera rig
- Starter Assets, First Person Controller

## How to Open and Run
1. Clone or download the repository
2. Open Unity Hub and add the project from the `AIGames-Advanced3DGameDev` folder
3. Open the vertical slice scene: `Assets/Scenes/02_VerticalSlice`
4. Press Play
5. For multiplayer testing, open the Multiplayer Play Mode window and tick Player 2

## Controls
| Input | Action |
|-------|--------|
| WASD | Move |
| Mouse | Look |
| Shift | Sprint |
| E | Pick up weapon, interact with power box |
| F | Takedown (when prompted, weapon required) |
| F1 | Toggle per-NPC debug overlay |
| F2 | Toggle all-NPC debug overlay |

## Player Objective
Find the weapon that has spawned somewhere in the level, pick it up, then take down all three NPCs to win. The NPCs will chase and attack if they spot or hear you. If you lose them they will search the area before returning to patrol. Activating the power box will send the nearest NPC to investigate and repair it.

## Behaviour Tree (CA2 / CA3 AI for Games)
The CA1 FSM was replaced with a pure C# Behaviour Tree. All NPC values are configurable in the Inspector via `NpcConfig`.

**Branch priority (high to low):**
1. Threat, chase and attack when player is visible
2. Audio search, navigate to heard position while player is audible
3. Post-chase search, fan search after losing the player (5s cooldown)
4. Investigate, walk to and repair the power box
5. Patrol, default fallback

## Networking (CA2 / CA3 Advanced 3D)
Networked via Photon Fusion 2 Shared Mode. The session is gated by Unity Authentication and an Azure Functions JWKS proxy configured as a Custom Server Provider in the Photon dashboard. Clients without a valid token are refused at the Fusion connection layer.

Networked features in the vertical slice:
- Weapon pickup, `[Networked]` flag + RPC + ChangeDetector
- NPC takedown, `RPC_StartTakedown` to all peers
- PowerBox activation, networked between clients
- Player position and noise level synced via `[Networked]` fields on `NetworkPlayerSetup`

## Attribution
Third-party assets and SDKs used in this project:
- Volumetric Fog for URP by CristianQiu (https://github.com/CristianQiu/Unity-URP-Volumetric-Light.git)
- Starter Assets First Person Controller by Unity Technologies
- Mixamo SWAT character and animations from Adobe Mixamo
- Photon Fusion 2 by Exit Games / Photon Engine
- Unity Authentication SDK by Unity Technologies
- Unity 6 (URP) by Unity Technologies

AI tools (primarily Claude) were used as a coding pair-programmer for Fusion-specific debugging and for editing the reflective documents. All design decisions and final implementations are my own work.


