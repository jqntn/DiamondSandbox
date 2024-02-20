using UnityEngine;
using UnityEngine.Events;

internal class OnMouseDown : MonoBehaviour
{
    private const int MOUSE_BUTTON = 0;

    [SerializeField] private UnityEvent _onMouseDown;

    private void Update()
    {
        if (Input.GetMouseButtonDown(MOUSE_BUTTON))
            _onMouseDown.Invoke();
    }
}