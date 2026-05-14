using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

[RequireComponent(typeof(LifeCycleInteraction))]
public class TapDetector : MonoBehaviour
{
    LifeCycleInteraction lifeCycle;

    void Awake()
    {
        lifeCycle = GetComponent<LifeCycleInteraction>();
    }

    void OnEnable()
    {
        EnhancedTouchSupport.Enable();
    }

    void OnDisable()
    {
        EnhancedTouchSupport.Disable();
    }

    void Update()
    {
        // ── On-device finger tap ──────────────────────────────────────────────
        foreach (Touch touch in Touch.activeTouches)
        {
            if (touch.phase == UnityEngine.InputSystem.TouchPhase.Began)
            {
                TryTap(touch.screenPosition);
                return; // only handle one tap per frame
            }
        }

        // ── Editor / mouse fallback ───────────────────────────────────────────
#if UNITY_EDITOR
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            TryTap(Mouse.current.position.ReadValue());
#endif
    }

    void TryTap(Vector2 screenPosition)
    {
        // Ignore taps that land on UI elements
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            return;

        Ray ray = Camera.main.ScreenPointToRay(screenPosition);
        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            if (hit.transform.IsChildOf(transform) || hit.transform == transform)
                lifeCycle.OnStageTapped();
        }
    }
}