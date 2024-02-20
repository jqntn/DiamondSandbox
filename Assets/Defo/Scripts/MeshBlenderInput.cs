using UnityEngine;

internal class MeshBlenderInput : MonoBehaviour
{
    private const int FORGE_BUTTON = 0;
    private const int LOOK_BUTTON = 1;
    private const int RESET_VIEW_BUTTON = 2;

    private Camera _cam;

    private Vector3 _initPos;
    private Quaternion _initRot;

    [SerializeField] private Transform _target;
    [SerializeField] private float _speed = 10;
    [Space]
    [SerializeField] private float _force = 10;
    [SerializeField] private float _forceOffset = .1f;
    [Space]
    [SerializeField] private float _radius = 10;
    [Space]
    [SerializeField] private AudioSource _audioSource;

    private void Awake()
    {
        _cam = Camera.main;

        _cam.transform.LookAt(_target);

        _initPos = _cam.transform.position;
        _initRot = _cam.transform.rotation;
    }

    private void Update()
    {
        HandleForgeInput();
        HandleMouseLook();
    }

    private void HandleForgeInput()
    {
        if (MeshBlender.EnableSmoothInput)
            HandleInputSmooth();
        else
            HandleInput();

        void HandleInput()
        {
            if (!Input.GetMouseButtonDown(FORGE_BUTTON))
                return;

            if (!Physics.Raycast(_cam.ScreenPointToRay(Input.mousePosition), out var hit) ||
                !hit.collider.TryGetComponent(out MeshBlender blender))
                return;

            blender.ReceiveInput(hit.point, _radius, _force / 10);

            SendAudioFeedback();
        }

        void HandleInputSmooth()
        {
            if (!Input.GetMouseButton(FORGE_BUTTON))
                return;

            if (!Physics.Raycast(_cam.ScreenPointToRay(Input.mousePosition), out var hit) ||
                !hit.collider.TryGetComponent(out MeshBlender blender))
                return;

            blender.ReceiveInput(hit.point, _radius, _force * Time.deltaTime);
        }
    }

    private void SendAudioFeedback() => _audioSource.Play();

    private void HandleMouseLook()
    {
        _cam.transform.LookAt(_target);

        if (Input.GetMouseButtonDown(RESET_VIEW_BUTTON))
            _cam.transform.SetPositionAndRotation(_initPos, _initRot);

        if (Input.GetMouseButton(LOOK_BUTTON))
        {
            _cam.transform.RotateAround(_target.transform.position, -_cam.transform.up, Input.GetAxis("Mouse X") * _speed);
            _cam.transform.RotateAround(_target.transform.position, _cam.transform.right, Input.GetAxis("Mouse Y") * _speed);

            _cam.transform.rotation = Quaternion.Euler(_cam.transform.rotation.eulerAngles.x, _cam.transform.rotation.eulerAngles.y, 0);
        }
    }
}