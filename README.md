# AI For Games – Advanced Game Dev 2026

## Unity Version
Unity 6 (6000.3.6f1)

## Packages Used
- AI Navigation – NavMesh baking and NavMeshAgent
- Input System – Player movement and interaction
- Universal Render Pipeline (URP) 
- Post Processing
- URP Volumetric Fog (CristianQiu) – Atmospheric fog via git package

## How to Open and Run
1. Clone or download the repository
2. Open Unity Hub and add the project from the `AIGames-Advanced3DGameDev` folder
3. Open the scene: `Assets/Scenes/CA1_RenderingFoundation`
4. Press Play

## Controls
| Input | Action |
|-------|--------|
| WASD | Move |
| Mouse | Look |
| E | Interact with power box |
| Shift | Sprint |

## Player Objective
Activate the power box in the environment to send the NPC to investigate and repair it. Use the distraction to move through the area without being detected. If the NPC spots or hears you it will chase and attack. The NPC will search for you once its loses you and then return to patrol.
