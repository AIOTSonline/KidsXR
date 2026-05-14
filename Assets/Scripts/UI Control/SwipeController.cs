using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections;

public class SwipeController : MonoBehaviour,
    IBeginDragHandler,
    IEndDragHandler
{
    [SerializeField] private ScrollRect scrollRect;

    [SerializeField] private int totalPages = 3;

    [SerializeField] private float snapDuration = 0.25f;

    private int currentPage = 0;

    private float[] pagePositions;

    private Coroutine snapCoroutine;

    private void Start()
    {
        pagePositions = new float[totalPages];

        for (int i = 0; i < totalPages; i++)
        {
            pagePositions[i] =
                (float)i / (totalPages - 1);
        }

        // Start on first page
        scrollRect.horizontalNormalizedPosition =
            pagePositions[0];
    }

    public void Next()
    {
        if (currentPage < totalPages - 1)
        {
            currentPage++;
            SnapToPage(currentPage);
        }
    }

    public void Previous()
    {
        if (currentPage > 0)
        {
            currentPage--;
            SnapToPage(currentPage);
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (snapCoroutine != null)
        {
            StopCoroutine(snapCoroutine);
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        StartCoroutine(SnapAfterDelay());
    }

    void SnapToPage(int page)
    {
        if (snapCoroutine != null)
        {
            StopCoroutine(snapCoroutine);
        }

        snapCoroutine =
            StartCoroutine(SmoothSnapTo(pagePositions[page]));
    }

    IEnumerator SmoothSnapTo(float target)
    {
        float start =
            scrollRect.horizontalNormalizedPosition;

        float time = 0f;

        while (time < snapDuration)
        {
            time += Time.deltaTime;

            float t =
                Mathf.SmoothStep(0f, 1f,
                time / snapDuration);

            scrollRect.horizontalNormalizedPosition =
                Mathf.Lerp(start, target, t);

            yield return null;
        }

        scrollRect.horizontalNormalizedPosition =
            target;
    }
    IEnumerator SnapAfterDelay()
{
    // wait for inertia movement
    yield return new WaitForSeconds(0.1f);

    float currentPos =
        scrollRect.horizontalNormalizedPosition;

    int nearestPage = Mathf.RoundToInt(
        currentPos * (totalPages - 1)
    );

    nearestPage = Mathf.Clamp(
        nearestPage,
        0,
        totalPages - 1
    );

    currentPage = nearestPage;

    SnapToPage(currentPage);
}
}