using UnityEngine;

public class ResponsivePanel : MonoBehaviour
{
    [Header("Assign Panel Containers")]
    public RectTransform[] panels;

    private Vector2 lastScreenSize;

    void Start()
    {
        ApplyScale();
    }

    void Update()
    {
        if (lastScreenSize.x != Screen.width ||
            lastScreenSize.y != Screen.height)
        {
            lastScreenSize = new Vector2(Screen.width, Screen.height);
            ApplyScale();
        }
    }

    void ApplyScale()
    {
        bool isLandscape = Screen.width > Screen.height;

        float scale = isLandscape ? 1.5f : 1f;

        foreach (RectTransform panel in panels)
        {
            panel.localScale = Vector3.one * scale;
        }
    }
}