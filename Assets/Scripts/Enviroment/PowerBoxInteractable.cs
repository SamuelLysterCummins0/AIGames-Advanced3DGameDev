using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

namespace Semester2
{
    /// <summary>
    /// Handles player interaction with a power box.
    /// When activated, starts electrical sparks and notifies all listeners (NPC, lights)
    /// via static events - no direct references needed (Observer Pattern).
    /// </summary>
    public class PowerBoxInteractable : MonoBehaviour
    {
        [Header("Interaction")]
        [Tooltip("How close the player must be to see the interact prompt")]
        [SerializeField] private float interactDistance = 2.5f;

        [Tooltip("The UI GameObject showing 'Distraction [E] Interact' - assign a world/screen space canvas")]
        [SerializeField] private GameObject interactPromptUI;

        [Tooltip("Key the player presses to interact")]
        [SerializeField] private KeyCode interactKey = KeyCode.E;

        [Header("NPC Interaction Point")]
        [Tooltip("Optional: place an empty child GameObject here and position it where the NPC should stand " +
                 "to fix the box (in front of the panel). If left empty the code uses 1.5 m along the box's " +
                 "forward axis as a fallback.")]
        [SerializeField] private Transform interactPoint;

        [Header("NPC Response")]
        [Tooltip("Coarse pre-filter: NPCs further than this by straight-line distance are skipped before " +
                 "path calculation. Keep this large (100+) so ground-floor NPCs are never excluded when " +
                 "patrolling far from the box. Path distance handles multi-floor naturally.")]
        [SerializeField] private float maxResponseDistance = 100f;
        [Tooltip("Seconds to wait before reassigning a new NPC after the current responder is taken down.")]
        [SerializeField] private float reassignDelay = 5f;

        [Header("Spark Particles")]
        [Tooltip("Assign your electricity spark ParticleSystem here - must be set manually")]
        [SerializeField] private ParticleSystem sparkParticles;

        // ── Static Events (Observer Pattern) ─────────────────────────────────────
        // Any system (NPC, lights, audio) can subscribe without knowing about each other.
        public static event Action<PowerBoxInteractable> OnPowerBoxActivated;
        public static event Action<PowerBoxInteractable> OnPowerBoxFixed;

        // ── State ─────────────────────────────────────────────────────────────────
        private bool          isActivated = false;
        private Transform     player;
        private NpcController _assignedNpc;
        private Coroutine     _monitorCoroutine;

        /// <summary>World transform of the box itself (used for facing direction on fix).</summary>
        public Transform BoxTransform => transform;

        /// <summary>True while the power box is sparking and needs fixing.</summary>
        public bool IsActivated => isActivated;

        /// <summary>
        /// Returns the world position the NPC should navigate to before fixing this box.
        /// Uses the explicit interactPoint Transform if assigned in the Inspector;
        /// otherwise falls back to 1.5 m along the box's local forward axis so the NPC
        /// always approaches the front face of the panel, not the side or back.
        /// </summary>
        public Vector3 GetNpcStandPosition()
        {
            if (interactPoint != null)
                return interactPoint.position;

            // Fallback: step out 1.5 m from the box centre in its facing direction.
            // Make sure the box's blue (forward/Z) arrow in the Scene view points away
            // from the wall so this lands in the open space in front of the panel.
            return transform.position + transform.forward * 1.5f;
        }

        /// <summary>
        /// Returns the world-space horizontal direction the NPC should face when
        /// standing at GetNpcStandPosition() and looking at the box.
        ///
        /// When interactPoint is assigned we derive the direction from interactPoint
        /// toward the box pivot — this is independent of the box model's local axes
        /// so it works regardless of how the asset is oriented in the scene.
        /// Fallback uses -transform.forward for boxes that have no interactPoint.
        /// </summary>
        public Vector3 GetNpcFacingDirection()
        {
            if (interactPoint != null)
            {
                Vector3 dir = transform.position - interactPoint.position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.001f)
                    return dir.normalized;
            }

            // Fallback: -forward is the direction from the stand offset back to the box.
            Vector3 fallback = -transform.forward;
            fallback.y = 0f;
            return fallback.sqrMagnitude > 0.001f ? fallback.normalized : -Vector3.forward;
        }

        void Start()
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
                player = playerObj.transform;

            if (interactPromptUI != null)
                interactPromptUI.SetActive(false);

            if (sparkParticles == null)
            {
                Debug.LogWarning($"[PowerBox] {name} has no Spark Particles assigned - assign one in the Inspector!");
            }
            else
            {
                // Force-stop in case Play On Awake is still checked in the Inspector
                sparkParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        void Update()
        {
            // Once activated, hide prompt and skip interaction checks
            if (isActivated || player == null)
            {
                if (interactPromptUI != null) interactPromptUI.SetActive(false);
                return;
            }

            float dist = Vector3.Distance(transform.position, player.position);
            bool inRange = dist <= interactDistance;

            if (interactPromptUI != null)
                interactPromptUI.SetActive(inRange);

            if (inRange && Input.GetKeyDown(interactKey))
                Activate();
        }

        private void Activate()
        {
            isActivated = true;

            if (interactPromptUI != null)
                interactPromptUI.SetActive(false);

            if (sparkParticles != null)
                sparkParticles.Play();

            Debug.Log($"[PowerBox] {name} activated - sparking!");
            OnPowerBoxActivated?.Invoke(this); // lights/audio subscribe here

            SelectAndAssignResponder();
        }

        private void SelectAndAssignResponder()
        {
            NpcController best     = null;
            float         bestDist = float.MaxValue;

            // FindObjectsByType is fine — this runs once on activation and once on reassignment, not per-frame
            foreach (NpcController npc in FindObjectsByType<NpcController>(FindObjectsSortMode.None))
            {
                if (!npc.IsEligibleForPowerBoxRepair) continue;

                // Coarse sphere pre-filter — skip clearly out-of-range NPCs before expensive path calc
                float straightLine = Vector3.Distance(transform.position, npc.transform.position);
                if (straightLine > maxResponseDistance) continue;

                // NavMesh path distance — naturally longer for upstairs NPC (must descend stairs)
                float pathDist = GetNavMeshPathDistance(npc.transform.position, transform.position);
                if (pathDist < bestDist) { bestDist = pathDist; best = npc; }
            }

            if (best == null)
            {
                Debug.Log($"[PowerBox] No eligible NPC within {maxResponseDistance}m — will retry when one enters range.");
                if (_monitorCoroutine != null) StopCoroutine(_monitorCoroutine);
                _monitorCoroutine = StartCoroutine(WaitForNpcInRange());
                return;
            }

            _assignedNpc = best;
            best.AssignPowerBoxRepair(this);

            // Monitor so we can hand off to another NPC if this one is taken down
            if (_monitorCoroutine != null) StopCoroutine(_monitorCoroutine);
            _monitorCoroutine = StartCoroutine(MonitorAssignedNpc());
        }

        private IEnumerator MonitorAssignedNpc()
        {
            // Poll until the NPC dies or the box is no longer active
            while (_assignedNpc != null && !_assignedNpc.IsDead && isActivated)
                yield return new WaitForSeconds(0.5f);

            // Box was fixed normally — nothing more to do
            if (!isActivated) yield break;

            // Assigned NPC was taken down — wait, then try the next eligible NPC
            Debug.Log($"[PowerBox] {_assignedNpc?.name ?? "NPC"} taken down — reassigning in {reassignDelay}s.");
            _assignedNpc = null;
            yield return new WaitForSeconds(reassignDelay);

            if (isActivated)
                SelectAndAssignResponder();
        }

        /// <summary>
        /// Returns the total NavMesh path length from 'from' to 'to'.
        /// Accounts for stairs and multi-floor layouts — an upstairs NPC must descend before
        /// reaching a ground-floor box, so their path measures longer than a ground-floor NPC's.
        /// Returns float.MaxValue if no complete path exists (NPC can't reach the box at all).
        /// </summary>
        private float GetNavMeshPathDistance(Vector3 from, Vector3 to)
        {
            NavMeshPath path = new NavMeshPath();
            if (NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path)
                && path.status == NavMeshPathStatus.PathComplete)
            {
                float dist = 0f;
                for (int i = 1; i < path.corners.Length; i++)
                    dist += Vector3.Distance(path.corners[i - 1], path.corners[i]);
                return dist;
            }
            return float.MaxValue; // no complete path = can't reach = never selected
        }

        /// <summary>
        /// Polls every second until an eligible NPC walks within maxResponseDistance.
        /// Triggers SelectAndAssignResponder as soon as one enters range.
        /// Stops cleanly if the box is fixed while waiting.
        /// </summary>
        private IEnumerator WaitForNpcInRange()
        {
            while (isActivated)
            {
                yield return new WaitForSeconds(1f);
                if (!isActivated) yield break;

                foreach (NpcController npc in FindObjectsByType<NpcController>(FindObjectsSortMode.None))
                {
                    if (!npc.IsEligibleForPowerBoxRepair) continue;
                    float dist = Vector3.Distance(transform.position, npc.transform.position);
                    if (dist <= maxResponseDistance)
                    {
                        // At least one NPC is now in range — run full selection to pick the closest
                        SelectAndAssignResponder();
                        yield break;
                    }
                }
            }
        }

        /// <summary>
        /// Called by NpcInvestigateState when the NPC finishes the fix animation.
        /// Stops sparks and fires the Fixed event so lights can restore.
        /// </summary>
        public void FixPowerBox()
        {
            if (!isActivated) return;

            isActivated  = false;
            _assignedNpc = null;

            // Stop the takedown monitor — the box is fixed, no reassignment needed
            if (_monitorCoroutine != null)
            {
                StopCoroutine(_monitorCoroutine);
                _monitorCoroutine = null;
            }

            if (sparkParticles != null)
                sparkParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            Debug.Log($"[PowerBox] {name} fixed - sparks stopped!");
            OnPowerBoxFixed?.Invoke(this);
        }

    }
}
