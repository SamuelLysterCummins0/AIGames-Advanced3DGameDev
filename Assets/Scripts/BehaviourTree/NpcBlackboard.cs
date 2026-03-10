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

        // The direction the player was moving when last detected.
        // Search uses this to fan points forward instead of searching randomly.
        public Vector3 LastKnownPlayerMoveDir;

        // --- Power Box Events (written by NpcController event handlers) ---
        public bool PowerBoxActive;
        public PowerBoxInteractable TargetPowerBox;

        // --- Debug Info (written by action nodes during Tick) ---
        // Stores the name of whichever action is currently running.
        public string ActiveNodeName = "None";
    }
}
