using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(IslandGenerator))]
public class IslandGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(8f);
        if (GUILayout.Button("Generate Island", GUILayout.Height(30f)))
        {
            ((IslandGenerator)target).GenerateIsland();
            EditorUtility.SetDirty(target);
        }

        if (GUILayout.Button("Generate All (New Seed)", GUILayout.Height(30f)))
        {
            ((IslandGenerator)target).GenerateAll();
            EditorUtility.SetDirty(target);
        }

    }
}

[CustomEditor(typeof(TestTreeGen))]
public class TestTreeGenEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(8f);
        if (GUILayout.Button("Generate Trees", GUILayout.Height(30f)))
        {
            ((TestTreeGen)target).GenerateTrees();
            EditorUtility.SetDirty(target);
        }

        if (GUILayout.Button("New Random Tree Seed", GUILayout.Height(24f)))
        {
            ((TestTreeGen)target).RandomizeSeed();
            EditorUtility.SetDirty(target);
        }
    }
}

[CustomEditor(typeof(TestPlantGen))]
public class TestPlantGenEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(8f);
        if (GUILayout.Button("Generate Plants", GUILayout.Height(30f)))
        {
            ((TestPlantGen)target).GeneratePlants();
            EditorUtility.SetDirty(target);
        }

        if (GUILayout.Button("New Random Plant Seed", GUILayout.Height(24f)))
        {
            ((TestPlantGen)target).RandomizeSeed();
            EditorUtility.SetDirty(target);
        }
    }
}

[CustomEditor(typeof(TestWaterGen))]
public class TestWaterGenEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(8f);
        if (GUILayout.Button("Generate Water", GUILayout.Height(30f)))
        {
            ((TestWaterGen)target).GenerateWater();
            EditorUtility.SetDirty(target);
        }

        if (GUILayout.Button("Generate Fish", GUILayout.Height(24f)))
        {
            ((TestWaterGen)target).GenerateFish();
            EditorUtility.SetDirty(target);
        }

        if (GUILayout.Button("New Random Fish Seed", GUILayout.Height(24f)))
        {
            ((TestWaterGen)target).RandomizeFishSeed();
            EditorUtility.SetDirty(target);
        }
    }
}

[CustomEditor(typeof(TestBeachGen))]
public class TestBeachGenEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(8f);
        if (GUILayout.Button("Generate Beach", GUILayout.Height(30f)))
        {
            ((TestBeachGen)target).GenerateBeach();
            EditorUtility.SetDirty(target);
        }
    }
}
