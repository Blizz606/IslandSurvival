using System;
using UnityEditor;
using UnityEngine;

internal static class GeneratorInspectorUi
{
    public static void DrawActionButton(UnityEngine.Object target, string label, float height, Action action)
    {
        if (!GUILayout.Button(label, GUILayout.Height(height)))
            return;

        action();
        EditorUtility.SetDirty(target);
    }
}

[CustomEditor(typeof(IslandGenerator))]
public class IslandGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(8f);
        IslandGenerator generator = (IslandGenerator)target;
        GeneratorInspectorUi.DrawActionButton(target, "Generate Island", 30f, generator.GenerateIsland);
        GeneratorInspectorUi.DrawActionButton(target, "Generate All (New Seed)", 30f, generator.GenerateAll);
    }
}

[CustomEditor(typeof(TestTreeGen))]
public class TestTreeGenEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(8f);
        TestTreeGen generator = (TestTreeGen)target;
        GeneratorInspectorUi.DrawActionButton(target, "Generate Trees", 30f, generator.GenerateTrees);
        GeneratorInspectorUi.DrawActionButton(target, "New Random Tree Seed", 24f, generator.RandomizeSeed);
    }
}

[CustomEditor(typeof(TestPlantGen))]
public class TestPlantGenEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(8f);
        TestPlantGen generator = (TestPlantGen)target;
        GeneratorInspectorUi.DrawActionButton(target, "Generate Plants", 30f, generator.GeneratePlants);
        GeneratorInspectorUi.DrawActionButton(target, "New Random Plant Seed", 24f, generator.RandomizeSeed);
    }
}

[CustomEditor(typeof(TestWaterGen))]
public class TestWaterGenEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(8f);
        TestWaterGen generator = (TestWaterGen)target;
        GeneratorInspectorUi.DrawActionButton(target, "Generate Water", 30f, generator.GenerateWater);
        GeneratorInspectorUi.DrawActionButton(target, "Generate Fish", 24f, generator.GenerateFish);
        GeneratorInspectorUi.DrawActionButton(target, "New Random Fish Seed", 24f, generator.RandomizeFishSeed);
    }
}

[CustomEditor(typeof(TestBeachGen))]
public class TestBeachGenEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(8f);
        TestBeachGen generator = (TestBeachGen)target;
        GeneratorInspectorUi.DrawActionButton(target, "Generate Beach", 30f, generator.GenerateBeach);
    }
}
