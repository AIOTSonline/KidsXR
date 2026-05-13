using UnityEngine;

public class BundlePanelToggle : MonoBehaviour
{
    [SerializeField] private GameObject bundlePanel;

    public void OpenPanel() => bundlePanel.SetActive(true);
    public void ClosePanel() => bundlePanel.SetActive(false);
}