using UnityEngine;

internal class MeshDeformerInput : MonoBehaviour
{
    private const int MOUSE_BUTTON = 0;

    private Camera _cam;

    [SerializeField] private AudioSource _audioSource;

    [SerializeField] private float _force = 10;
    [SerializeField] private float _forceOffset = .1f;

    private void Awake() => _cam = Camera.main;

    private void Update()
    {
        if (Input.GetMouseButton(MOUSE_BUTTON))
            HandleInput();
    }

    private void HandleInput()
    {
        if (!Physics.Raycast(_cam.ScreenPointToRay(Input.mousePosition), out var hit) ||
            !hit.collider.TryGetComponent(out MeshDeformer deformer))
            return;

        deformer.AddForceToMesh(hit.point + hit.normal * _forceOffset, _force);

        SendAudioFeedback();
    }

    private void SendAudioFeedback() => _audioSource.Play();
}