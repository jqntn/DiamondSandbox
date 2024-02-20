using UnityEngine;

internal class MouseLookAt : MonoBehaviour
{
    private Camera _cam;

    private void Awake() => _cam = Camera.main;

    private void Update()
    {
        var mousePos_SS = Input.mousePosition;
        mousePos_SS.z = _cam.nearClipPlane;

        var mousePos_WS = _cam.ScreenToWorldPoint(mousePos_SS);

        transform.LookAt(mousePos_WS);
    }
}