using UnityEngine;

namespace Semester2
{
    /// <summary>
    /// Simple player health component. NPCs deal damage via TakeDamage().
    /// Fires OnHealthChanged(fraction) whenever health changes so the HUD
    /// can update the health bar and damage vignette without being coupled here.
    /// Health regenerates automatically after the player has not been hit for
    /// RegenDelay seconds.
    /// On death, notifies GameManager to show the lose screen.
    /// </summary>
    public class PlayerHealth : MonoBehaviour
    {
        [Header("Health")]
        [SerializeField] private float maxHealth = 100f;

        [Header("Health Regeneration")]
        [SerializeField] private float regenDelay = 4f;   // seconds after last hit before regen starts
        [SerializeField] private float regenRate  = 8f;   // HP per second while regenerating

        /// <summary>
        /// Fired whenever health changes (damage or regen).
        /// Parameter is current health as a 0-1 fraction.
        /// </summary>
        public static event System.Action<float> OnHealthChanged;

        private float _currentHealth;
        private bool  _isDead = false;
        private float _timeSinceLastDamage = float.MaxValue;

        void Start()
        {
            _currentHealth = maxHealth;
            // Initialise HUD at full health
            OnHealthChanged?.Invoke(HealthFraction);
        }

        void Update()
        {
            if (_isDead) return;

            _timeSinceLastDamage += Time.deltaTime;

            // Regenerate once the delay has elapsed and health is not full
            if (_timeSinceLastDamage >= regenDelay && _currentHealth < maxHealth)
            {
                _currentHealth = Mathf.Min(_currentHealth + regenRate * Time.deltaTime, maxHealth);
                OnHealthChanged?.Invoke(HealthFraction);
            }
        }

        /// <summary>Called by BtActionAttack each time the NPC fires.</summary>
        public void TakeDamage(float amount)
        {
            if (_isDead) return;

            _currentHealth -= amount;
            _timeSinceLastDamage = 0f;
            Debug.Log($"[PlayerHealth] Player took {amount} damage. Health: {_currentHealth}/{maxHealth}");

            OnHealthChanged?.Invoke(HealthFraction);

            if (_currentHealth <= 0f)
                Die();
        }

        private void Die()
        {
            _isDead = true;
            Debug.Log("[PlayerHealth] <color=red>Player died!</color>");

            if (GameManager.Instance != null)
                GameManager.Instance.OnPlayerDied();
        }

        /// <summary>Public getter so other systems can check if the player is alive.</summary>
        public bool IsDead => _isDead;

        /// <summary>Returns health as a 0-1 fraction.</summary>
        public float HealthFraction => Mathf.Clamp01(_currentHealth / maxHealth);
    }
}
