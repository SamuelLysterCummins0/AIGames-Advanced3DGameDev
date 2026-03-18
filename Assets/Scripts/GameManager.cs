using System.Collections;
using StarterAssets;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Semester2
{
    public enum GameState { FindWeapon, TakedownGuards, Win, Defeat }

    /// <summary>
    /// Central game state machine. Tracks objective progression and win condition.
    /// Also owns all HUD references directly — assign UI elements in the Inspector.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        public GameState State { get; private set; }

        [Header("UI - Goal")]
        [SerializeField] private TMP_Text   goalText;
        [SerializeField] private GameObject winPanel;    // green — you won
        [SerializeField] private GameObject losePanel;   // red   — you died

        [Header("UI - Prompts")]
        [SerializeField] public GameObject weaponPromptUI;

        private NpcController[]          _npcs;
        private PlayerTakedownController _takedownController;
        private FirstPersonController    _fpsController;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        void Start()
        {
            _npcs               = FindObjectsByType<NpcController>(FindObjectsSortMode.None);
            _takedownController = FindObjectOfType<PlayerTakedownController>();
            _fpsController      = FindObjectOfType<FirstPersonController>();

            // Cursor state persists across scene reloads — reset it every time the game starts
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;

            if (_takedownController != null)
                _takedownController.enabled = false;

            if (winPanel  != null) winPanel.SetActive(false);
            if (losePanel != null) losePanel.SetActive(false);

            SetState(GameState.FindWeapon);
        }

        /// <summary>Called by WeaponPickup when the player equips the weapon.</summary>
        public void OnWeaponPickedUp()
        {
            if (_takedownController != null)
                _takedownController.enabled = true;

            SetState(GameState.TakedownGuards);
            StartCoroutine(CheckWinCondition());
        }

        /// <summary>Called by GameManager when all NPCs are dead.</summary>
        private IEnumerator CheckWinCondition()
        {
            while (State == GameState.TakedownGuards)
            {
                yield return new WaitForSeconds(0.5f);

                bool allDead = true;
                foreach (NpcController npc in _npcs)
                {
                    if (!npc.IsDead) { allDead = false; break; }
                }

                if (allDead)
                {
                    // Wait for the takedown animation to finish before showing the win screen
                    while (_takedownController != null && _takedownController.IsAnimating)
                        yield return null;

                    SetState(GameState.Win);
                }
            }
        }

        /// <summary>Called by PlayerHealth when the player's health reaches zero.</summary>
        public void OnPlayerDied()
        {
            if (State == GameState.Win || State == GameState.Defeat) return;
            SetState(GameState.Defeat);
        }

        /// <summary>Reloads the current scene — wired to both restart buttons.</summary>
        public void RestartGame()
        {
            Time.timeScale = 1f; // Unpause before loading
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        private void SetState(GameState newState)
        {
            State = newState;

            switch (newState)
            {
                case GameState.FindWeapon:
                    if (goalText  != null) goalText.text = "Goal: Find a Weapon";
                    if (winPanel  != null) winPanel.SetActive(false);
                    if (losePanel != null) losePanel.SetActive(false);
                    break;

                case GameState.TakedownGuards:
                    if (goalText != null) goalText.text = "Goal: Takedown the Guards";
                    break;

                case GameState.Win:
                    if (goalText != null) goalText.text = "";
                    if (winPanel != null) winPanel.SetActive(true);
                    Time.timeScale = 0f;
                    ShowCursor();
                    break;

                case GameState.Defeat:
                    if (goalText  != null) goalText.text = "";
                    if (losePanel != null) losePanel.SetActive(true);
                    Time.timeScale = 0f;
                    ShowCursor();
                    break;
            }

            Debug.Log($"[GameManager] <color=cyan>State → {newState}</color>");
        }

        /// <summary>
        /// Unlocks and shows the cursor and disables FPS look so the player
        /// can click UI buttons on the win/lose screen.
        /// </summary>
        private void ShowCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
            if (_fpsController != null) _fpsController.enabled = false;
        }
    }
}
