using UnityEngine;
using UnityEngine.XR.ARFoundation;
using System.Collections;

public class ARSceneEntry : MonoBehaviour
{
    private ARSession arSession;
    private ARCameraBackground arCameraBackground;

    void Awake()
    {
        arSession = GetComponent<ARSession>();
        arCameraBackground = FindAnyObjectByType<ARCameraBackground>();
    }

    IEnumerator Start()
    {
        arSession.enabled = false;
        arCameraBackground.enabled = false;

        yield return null;
        yield return null;

        arSession.Reset();
        yield return new WaitForSeconds(0.2f);

        arSession.enabled = true;
        yield return new WaitForSeconds(0.2f);

        arCameraBackground.enabled = true;
    }
}