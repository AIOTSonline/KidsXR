using UnityEditor;
using UnityEngine;
using System.IO;

public class BuildAssetBundles
{
    [MenuItem("Tools/Build AssetBundles Android")]
    static void BuildAndroid()
    {
        // ---- DEBUG: check how many bundles are tagged ----
        string[] bundleNames = AssetDatabase.GetAllAssetBundleNames();

        Debug.Log("Total bundles found: " + bundleNames.Length);

        foreach (string name in bundleNames)
            Debug.Log("Bundle tagged: " + name);

        // If nothing is tagged, stop and warn
        if (bundleNames.Length == 0)
        {
            EditorUtility.DisplayDialog(
                "No Bundles Found",
                "No AssetBundles are assigned!\n\n" +
                "Select a Prefab in the Project window,\n" +
                "then at the BOTTOM of the Inspector\n" +
                "assign a name in the AssetBundle dropdown.",
                "OK"
            );
            return;
        }

        // ---- Create output folder if it doesn't exist ----
        string outputPath = Application.dataPath + "/AssetBundles/Android";

        if (!Directory.Exists(outputPath))
        {
            Directory.CreateDirectory(outputPath);
            Debug.Log("Created folder: " + outputPath);
        }

        // ---- Build ----
        Debug.Log("Building bundles to: " + outputPath);

        BuildPipeline.BuildAssetBundles(
            outputPath,
            BuildAssetBundleOptions.None,
            BuildTarget.Android
        );

        // ---- Refresh Project window so files appear ----
        AssetDatabase.Refresh();

        // ---- Open folder so you can see the files ----
        EditorUtility.RevealInFinder(outputPath);

        EditorUtility.DisplayDialog(
            "Done!",
            "Built " + bundleNames.Length + " bundle(s)!\n\nFolder opened automatically.",
            "OK"
        );
    }

    [MenuItem("Tools/Build AssetBundles PC")]
    static void BuildPC()
    {
        string[] bundleNames = AssetDatabase.GetAllAssetBundleNames();

        Debug.Log("Total bundles found: " + bundleNames.Length);

        if (bundleNames.Length == 0)
        {
            EditorUtility.DisplayDialog(
                "No Bundles Found",
                "No AssetBundles are assigned!\n\n" +
                "Select a Prefab in the Project window,\n" +
                "then at the BOTTOM of the Inspector\n" +
                "assign a name in the AssetBundle dropdown.",
                "OK"
            );
            return;
        }

        string outputPath = Application.dataPath + "/AssetBundles/PC";

        if (!Directory.Exists(outputPath))
        {
            Directory.CreateDirectory(outputPath);
            Debug.Log("Created folder: " + outputPath);
        }

        Debug.Log("Building bundles to: " + outputPath);

        BuildPipeline.BuildAssetBundles(
            outputPath,
            BuildAssetBundleOptions.None,
            BuildTarget.StandaloneWindows64
        );

        AssetDatabase.Refresh();
        EditorUtility.RevealInFinder(outputPath);

        EditorUtility.DisplayDialog(
            "Done!",
            "Built " + bundleNames.Length + " bundle(s)!\n\nFolder opened automatically.",
            "OK"
        );
    }
}