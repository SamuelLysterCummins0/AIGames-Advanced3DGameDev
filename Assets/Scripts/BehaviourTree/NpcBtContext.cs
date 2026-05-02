using UnityEngine;
using UnityEngine.AI;

namespace Semester2
{
    /// <summary>
    /// Bundles all the references that BT action and condition nodes need.
    /// Created once by NpcController and shared across all nodes.
    /// This avoids passing many individual parameters to every node constructor.
    /// </summary>
    public class NpcBtContext
    {
        public readonly string NpcName;
        public readonly GameObject Owner;
        public readonly NavMeshAgent Agent;
        public readonly Animator Anim;
        public Transform Player;
        public Vector3   PlayerWorldPosition; // synced world position — use this instead of Player.position on the host
        public PlayerAudioEmitter AudioEmitter;
        public System.Action OnAttackFired; // called by BtActionAttack on each shot to sync animation across peers
        public readonly NpcConfig Config;
        public readonly NpcBlackboard Blackboard;

        public NpcBtContext(GameObject owner, NpcConfig config, NpcBlackboard blackboard)
        {
            NpcName   = owner.name;
            Owner     = owner;
            Agent     = owner.GetComponent<NavMeshAgent>();
            Anim      = owner.GetComponentInChildren<Animator>();
            Config    = config;
            Blackboard = blackboard;

            // Find the player once at construction time
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                Player        = playerObj.transform;
                AudioEmitter  = playerObj.GetComponent<PlayerAudioEmitter>();
            }
        }
    }
}
