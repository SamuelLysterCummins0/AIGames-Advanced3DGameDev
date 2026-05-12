using UnityEngine;

namespace Semester2
{
    /// <summary>
    /// Shared data store for all BT nodes on this NPC.
    /// NpcController writes perception results here before the BT ticks each frame.
    /// Nodes read from here instead of calling perception methods directly.
    /// </summary>
    public class NpcBlackboard
    {
        // --- Perception (written by NpcController.UpdatePerception each frame) ---
        public bool PlayerVisible;
        public bool PlayerHeard;
        public float DistanceToPlayer;

        // Last position where the player was seen or heard. Used by the Search branch.
        public bool HasLastKnownPosition;
        public Vector3 LastKnownPlayerPosition;

        // True when HasLastKnownPosition was set by visual contact (chasing).
        // False when set only by audio (hearing). BtCheckPowerBoxActive uses this
        // to block Investigate only after a real chase, not after a brief footstep.
        public bool LkpFromChase;

        // True while reinforcement alerts are still arriving from a chasing NPC.
        // BtActionSearch uses this to navigate toward the live LKP (same as PlayerHeard
        // tracking mode) instead of committing to static fan-search points.
        public bool ReinforcementTracking;

        // True while the player is currently visible and the SpotTimer is at/above the
        // Search threshold but not yet at the Chase threshold. Triggers live-tracking
        // mode in BtActionSearch — when the NPC re-acquires sight mid-search, they walk
        // straight toward the player's actual position instead of continuing the
        // fan-search around the original (now-stale) LKP.
        public bool SpotTracking;

        // The direction the player was moving when last detected.
        // Search uses this to fan points forward instead of searching randomly.
        public Vector3 LastKnownPlayerMoveDir;

        // --- Power Box Events (written by NpcController event handlers) ---
        public bool PowerBoxActive;
        public PowerBoxInteractable TargetPowerBox;

        // --- Spot Timer (written by NpcController.UpdatePerception each frame) ---
        // Rises while the player is in the NPC's vision cone — fill rate scales with
        // distance and angle off-centre, so a player directly in front close-up fills
        // it almost instantly while a player at the edge of the cone at long range
        // fills it slowly. Drains at a flat rate when the player is hidden.
        // At SpotTimerSearchThreshold the NPC sets a LKP and searches.
        // At SpotTimerChaseThreshold (higher) the NPC commits to Chase.
        // Replaces the previous Suspicion gradient — far cleaner and more readable.
        public float SpotTimer;

        // --- Debug Info (written by action nodes during Tick) ---
        // Stores the name of whichever action is currently running.
        public string ActiveNodeName = "None";
    }
}
