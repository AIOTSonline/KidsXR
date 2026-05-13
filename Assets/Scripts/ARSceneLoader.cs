using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.SceneManagement;
using System.Collections;

public class ARSceneLoader : MonoBehaviour
{
    [SerializeField] private ARSession arSession;

    public void GoToMainMenu()
    {
        StartCoroutine(ExitRoutine("Menu_1"));
    }

    public void GoToScene(string sceneName)
    {
        StartCoroutine(ExitRoutine(sceneName));
    }

    private IEnumerator ExitRoutine(string sceneName)
    {
        // Disable AR session first, give it a frame to shut down cleanly
        arSession.enabled = false;
        yield return null; // wait one frame
        yield return null; // two frames to be safe

        SceneManager.LoadScene(sceneName);
    }
}