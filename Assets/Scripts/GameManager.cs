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

            // Cursor is managed by GameBootstrap (lobby = unlocked, session = locked)

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

        /// <summary>Respawns the player without leaving the session — wired to the restart button.</summary>
        public void RestartGame()
        {
            Time.timeScale = 1f;

            GameBootstrap bootstrap = FindObjectOfType<GameBootstrap>();
            if (bootstrap != null)
            {
                bootstrap.RespawnLocalPlayer();
                SetState(GameState.FindWeapon);
            }
            else
            {
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            }
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
                    // Don't pause Time.timeScale — in multiplayer the host's NPCs run
                    // on the host's simulation, so pausing time here freezes NPCs on
                    // the surviving client too. The dead player's controls are disabled
                    // by ShowCursor, so they can't affect the world while spectating.
                    ShowCursor();
                    break;

                case GameState.Defeat:
                    if (goalText  != null) goalText.text = "";
                    if (losePanel != null) losePanel.SetActive(true);
                    // Same reasoning as Win — don't pause time in multiplayer.
                    ShowCursor();
                    break;
            }

            Debug.Log($"[GameManager] <color=cyan>State → {newState}</color>");
        }

        /// <summary>
        /// Unlocks and shows the cursor and disables FPS look so the player
        /// can click UI buttons on the win/lose screen.
        /// Disables ALL FirstPersonControllers AND StarterAssetsInputs in the scene —
        /// both have their own re-lock paths (FPC ticks every frame, Inputs locks on
        /// OnApplicationFocus) so disabling only one of them lets the other steal the
        /// cursor back the moment the player clicks anywhere on the lose screen.
        /// </summary>
        private void ShowCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;

            foreach (var fpc in FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None))
                fpc.enabled = false;

            foreach (var inputs in FindObjectsByType<StarterAssets.StarterAssetsInputs>(FindObjectsSortMode.None))
                inputs.enabled = false;
        }
    }
}
