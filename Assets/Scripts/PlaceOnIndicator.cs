using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

[RequireComponent(typeof(ARRaycastManager))]
public class PlaceOnIndicator : MonoBehaviour
{
    [SerializeField] GameObject placementIndicator;
    [SerializeField] GameObject placedPrefab;
    [SerializeField] GameObject placeButton;

    GameObject spawnedObject;
    ARRaycastManager arRaycastManager;

    List<ARRaycastHit> hits = new List<ARRaycastHit>();

    bool objectPlaced = false;

    void Awake()
    {
        arRaycastManager = GetComponent<ARRaycastManager>();
        placementIndicator.SetActive(false);
    }

    void Update()
    {
        if (objectPlaced) return;

        if (arRaycastManager.Raycast(
            new Vector2(Screen.width / 2, Screen.height / 2),
            hits,
            TrackableType.PlaneWithinPolygon))
        {
            Pose hitPose = hits[0].pose;

            placementIndicator.transform.SetPositionAndRotation(
                hitPose.position,
                hitPose.rotation
            );

            if (!placementIndicator.activeSelf)
                placementIndicator.SetActive(true);
        }
    }

    public void PlaceObject()
    {
        if (objectPlaced) return;

        if (!placementIndicator.activeSelf) return;

        spawnedObject = Instantiate(
            placedPrefab,
            placementIndicator.transform.position,
            placementIndicator.transform.rotation
        );

        objectPlaced = true;

        placementIndicator.SetActive(false);
        placeButton.SetActive(false);

        // Stop raycasting completely
        arRaycastManager.enabled = false;
    }
}