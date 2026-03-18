using UnityEngine;

namespace Semester2
{
    /// <summary>
    /// Place on the weapon prefab. Shows a prompt when the player is nearby and
    /// equips the weapon when E is pressed, notifying the GameManager.
    /// </summary>
    public class WeaponPickup : MonoBehaviour
    {
        [SerializeField] private float   pickupRange = 2.5f;
        [SerializeField] private KeyCode pickupKey   = KeyCode.E;

        private Transform  _player;
        private GameObject _promptUI;

        void Start()
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
                _player = playerObj.transform;

            if (GameManager.Instance != null)
                _promptUI = GameManager.Instance.weaponPromptUI;

            if (_promptUI != null)
                _promptUI.SetActive(false);
        }

        void OnDestroy()
        {
            if (_promptUI != null)
                _promptUI.SetActive(false);
        }

        void Update()
        {
            if (_player == null) return;

            bool inRange = Vector3.Distance(transform.position, _player.position) <= pickupRange;

            if (_promptUI != null)
                _promptUI.SetActive(inRange);

            if (inRange && Input.GetKeyDown(pickupKey))
            {
                GameManager.Instance.OnWeaponPickedUp();
                Destroy(gameObject);
            }
        }
    }
}
