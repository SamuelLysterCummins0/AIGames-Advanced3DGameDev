using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Semester2
{
    /// <summary>
    /// Attached to each row in the lobby room list.
    /// GameBootstrap instantiates this prefab and calls Setup() for each session returned
    /// by OnSessionListUpdated.
    /// </summary>
    public class RoomEntryUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text _roomNameText;
        [SerializeField] private TMP_Text _playerCountText;
        [SerializeField] private Button   _joinButton;

        /// <summary>
        /// Populate this row with session info and wire the Join button.
        /// </summary>
        public void Setup(string roomName, int players, int maxPlayers, System.Action onJoin)
        {
            _roomNameText.text    = roomName;
            _playerCountText.text = $"{players}/{maxPlayers}";
            _joinButton.onClick.AddListener(() => onJoin());
        }
    }
}
