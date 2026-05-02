using UnityEngine;

namespace Semester2
{
    /// <summary>
    /// Attach to the Main Camera. GameBootstrap calls SetTarget() after spawning
    /// the local player so the camera follows the correct client's character.
    /// </summary>
    public class CameraFollowTarget : MonoBehaviour
    {
        [SerializeField] private Vector3 offset = new Vector3(0f, 5f, -8f);
        [SerializeField] private float   smoothSpeed = 10f;

        private Transform _target;

        public void SetTarget(Transform target) => _target = target;

        private void LateUpdate()
        {
            if (_target == null) return;

            Vector3 desired = _target.position + _target.TransformDirection(offset);
            transform.position = Vector3.Lerp(transform.position, desired, smoothSpeed * Time.deltaTime);
            transform.LookAt(_target.position + Vector3.up * 1.5f);
        }
    }
}
