using UnityEngine;

public class ViewSwitch : MonoBehaviour
{
    public GameObject[] views;

    public void ShowView(int index)
    {
        for (int i = 0; i < views.Length; i++)
        {
            views[i].SetActive(i == index);
        }
    }

    private void Start()
    {
        ShowView(1);
    }
}