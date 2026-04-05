using Fusion;
using UnityEngine;

namespace Semester2
{
    /// <summary>
    /// A networked pickup object. Any client can pick it up by pressing E while they have InputAuthority.
    /// State changes (setting IsPickedUp, despawning) are only executed on the entity with StateAuthority
    /// to prevent desyncs. The [Networked] property replicates the pickup state to all clients,
    /// including any that join after the pickup has already been collected.
    /// </summary>
    public class PickupItem : NetworkBehaviour
    {
        /// <summary>
        /// Synced across all clients. When true the pickup has been collected.
        /// Using [Networked] rather than an RPC because this is persistent state —
        /// a late-joining client needs to know the pickup is already gone.
        /// Changes are detected via ChangeDetector in Render() — the Fusion 2 approach.
        /// </summary>
        [Networked]
        public NetworkBool IsPickedUp { get; set; }

        [SerializeField] private MeshRenderer _mesh;
        [SerializeField] private KeyCode _interactKey = KeyCode.E;

        // Fusion 2 uses ChangeDetector instead of the OnChanged attribute from Fusion 1.
        private ChangeDetector _changeDetector;

        public override void Spawned()
        {
            // Initialise the change detector so Render() can diff state each frame.
            _changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState);

            // Make sure the pickup is visible when it first spawns on all clients.
            if (_mesh != null)
                _mesh.enabled = true;

            Debug.Log("[PickupItem] <color=cyan>Spawned on client. HasInputAuthority: </color>" + Object.HasInputAuthority);
        }

        /// <summary>
        /// Runs every Unity frame on all clients (not a Fusion tick).
        /// Any client can detect the E key locally and fire an RPC to the StateAuthority.
        /// Using Update() + RPC here instead of FixedUpdateNetwork() + InputAuthority because
        /// we want BOTH host and client to be able to trigger the pickup — not just the one
        /// player that was assigned InputAuthority at spawn.
        /// </summary>
        private void Update()
        {
            if (Input.GetKeyDown(_interactKey) && !IsPickedUp && Object.IsValid)
                RPC_RequestPickup();
        }

        /// <summary>
        /// Sent from any client (RpcSources.All) and received only on the StateAuthority (the host).
        /// The host is the only one that can write to [Networked] properties, so the actual
        /// state change is done here rather than locally. [Networked] then replicates the result
        /// to all clients — including any that join late.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestPickup(RpcInfo info = default)
        {
            // Guard: could arrive twice if both clients press E the same tick.
            if (!IsPickedUp)
            {
                IsPickedUp = true;
                Debug.Log("[PickupItem] <color=green>Pickup confirmed by StateAuthority. Requested by: </color>" + info.Source);
            }
        }

        /// <summary>
        /// Render() runs every Unity frame (not every network tick).
        /// This is the Fusion 2 way to react to [Networked] property changes on all clients —
        /// ChangeDetector diffs the current vs previous simulation state each frame.
        /// </summary>
        public override void Render()
        {
            foreach (var change in _changeDetector.DetectChanges(this))
            {
                switch (change)
                {
                    case nameof(IsPickedUp):
                        HandlePickupChanged();
                        break;
                }
            }
        }

        /// <summary>
        /// Runs on ALL clients when IsPickedUp flips to true.
        /// Hides the mesh immediately so the visual response is instant everywhere.
        /// The actual despawn is guarded by StateAuthority so only the host calls it.
        /// </summary>
        private void HandlePickupChanged()
        {
            if (!IsPickedUp) return;

            // Hide the pickup visually on all clients right away.
            if (_mesh != null)
                _mesh.enabled = false;

            Debug.Log("[PickupItem] <color=yellow>IsPickedUp changed — hiding mesh.</color>");

            // Only the host (StateAuthority) should actually despawn the object.
            // Calling Runner.Despawn on a non-authority client would throw an error.
            if (Object.HasStateAuthority)
            {
                Runner.Despawn(Object);
                Debug.Log("[PickupItem] <color=red>Despawned by StateAuthority.</color>");
            }
        }
    }
}
