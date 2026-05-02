using Fusion;
using UnityEngine;

namespace Semester2
{
    /// <summary>
    /// Networked player health. TakeDamage routes through an RPC so the NPC (running
    /// on the host/state-authority) can deal damage to whichever player it is targeting,
    /// even when that player's NetworkObject is owned by a different client.
    ///
    /// Health is stored as a [Networked] float so all peers see the correct value.
    /// OnHealthChanged only fires for the locally-controlled player so the HUD
    /// never shows another player's health on your screen.
    /// </summary>
    public class PlayerHealth : NetworkBehaviour
    {
        [Header("Health")]
        [SerializeField] private float maxHealth = 100f;

        [Header("Health Regeneration")]
        [SerializeField] private float regenDelay = 4f;
        [SerializeField] private float regenRate  = 8f;

        /// <summary>
        /// Fired whenever the LOCAL player's health changes (damage or regen).
        /// Parameter is current health as a 0-1 fraction.
        /// </summary>
        public static event System.Action<float> OnHealthChanged;

        // Networked health — StateAuthority writes it, all peers read it.
        [Networked] private float Health { get; set; }

        // Non-networked fallback used when Fusion is not running (e.g. scene testing).
        private float _localHealth;

        private ChangeDetector _changeDetector;
        private bool  _isDead              = false;
        private float _timeSinceLastDamage = float.MaxValue;

        // ── Fusion lifecycle ───────────────────────────────────────────────────────

        public override void Spawned()
        {
            _changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState);

            // StateAuthority initialises health (in Shared Mode the player owns themselves).
            if (HasStateAuthority)
                Health = maxHealth;
        }

        public override void Render()
        {
            if (_changeDetector == null) return;

            foreach (var change in _changeDetector.DetectChanges(this))
            {
                // Only update the HUD for the locally controlled player.
                if (change == nameof(Health) && HasInputAuthority)
                    OnHealthChanged?.Invoke(HealthFraction);
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority || _isDead) return;

            _timeSinceLastDamage += Runner.DeltaTime;

            if (_timeSinceLastDamage >= regenDelay && Health < maxHealth)
                Health = Mathf.Min(Health + regenRate * Runner.DeltaTime, maxHealth);
        }

        // ── Damage RPC ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Called by BtActionAttack on any peer. The RPC routes to the StateAuthority
        /// (the player who owns this object), so health is always written by the right client.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_TakeDamage(float amount)
        {
            if (_isDead) return;

            Health = Mathf.Max(Health - amount, 0f);
            _timeSinceLastDamage = 0f;
            Debug.Log($"[PlayerHealth] Took {amount} damage. Health: {Health}/{maxHealth}");

            // Die() is called here (on the StateAuthority, which equals the InputAuthority
            // for player objects in Shared Mode) so the death screen shows immediately on
            // the correct client without waiting for a ChangeDetector round-trip.
            if (Health <= 0f)
                Die();
        }

        // ── Non-networked fallback (single-player / scene testing) ─────────────────

        private void Start()
        {
            if (Runner == null)
            {
                _localHealth = maxHealth;
                OnHealthChanged?.Invoke(1f);
            }
        }

        private void Update()
        {
            if (Runner != null || _isDead) return; // networked path handled in FixedUpdateNetwork

            _timeSinceLastDamage += Time.deltaTime;
            if (_timeSinceLastDamage >= regenDelay && _localHealth < maxHealth)
            {
                _localHealth = Mathf.Min(_localHealth + regenRate * Time.deltaTime, maxHealth);
                OnHealthChanged?.Invoke(HealthFraction);
            }
        }

        /// <summary>Non-networked TakeDamage — used when Fusion is not active.</summary>
        public void TakeDamage(float amount)
        {
            if (_isDead) return;

            _localHealth         -= amount;
            _timeSinceLastDamage  = 0f;
            Debug.Log($"[PlayerHealth] Player took {amount} damage. Health: {_localHealth}/{maxHealth}");

            OnHealthChanged?.Invoke(HealthFraction);

            if (_localHealth <= 0f)
                Die();
        }

        // ── Shared helpers ─────────────────────────────────────────────────────────

        private void Die()
        {
            _isDead = true;
            Debug.Log("[PlayerHealth] <color=red>Player died!</color>");

            if (GameManager.Instance != null)
                GameManager.Instance.OnPlayerDied();
        }

        /// <summary>Returns current health as a 0-1 fraction.</summary>
        public float HealthFraction
        {
            get
            {
                float current = Runner != null ? Health : _localHealth;
                return Mathf.Clamp01(current / maxHealth);
            }
        }

        /// <summary>True after health reaches zero.</summary>
        public bool IsDead => _isDead;
    }
}
