using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[CustomEditor(typeof(UltimateWorldGenerator))]
public sealed class UltimateWorldGeneratorEditor : UnityEditor.Editor
{
    private readonly Dictionary<MonoBehaviour, UnityEditor.Editor> editors = new();
    private readonly Dictionary<MonoBehaviour, bool> expanded = new();

    public override void OnInspectorGUI()
    {
        UltimateWorldGenerator generator = (UltimateWorldGenerator)target;
        serializedObject.Update();
        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));

        if (GUILayout.Button("Gesamte Welt generieren (neuer Seed)", GUILayout.Height(32f)))
            generator.GenerateAll();

        EditorGUILayout.Space(6f);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("showIndividualComponents"),
            new GUIContent("Einzelne Komponenten anzeigen"));
        if (serializedObject.ApplyModifiedProperties())
            UltimateWorldGeneratorIntegration.ApplyVisibility(generator);

        foreach (MonoBehaviour module in generator.GetModules())
        {
            if (!expanded.TryGetValue(module, out bool isExpanded))
                isExpanded = module is UltimateWorldGenerator.IslandModule;
            isExpanded = EditorGUILayout.Foldout(isExpanded, Label(module), true, EditorStyles.foldoutHeader);
            expanded[module] = isExpanded;
            if (!isExpanded) continue;

            EditorGUI.indentLevel++;
            bool enabled = EditorGUILayout.Toggle("Komponente aktiv", module.enabled);
            if (enabled != module.enabled)
            {
                Undo.RecordObject(module, "Generator aktivieren");
                module.enabled = enabled;
                PrefabUtility.RecordPrefabInstancePropertyModifications(module);
                EditorUtility.SetDirty(module);
            }

            if (!editors.TryGetValue(module, out UnityEditor.Editor inspector) || inspector == null)
            {
                inspector = UnityEditor.Editor.CreateEditor(module);
                editors[module] = inspector;
            }
            // Edit the original serialized objects directly: no copied settings or references.
            inspector.OnInspectorGUI();
            DrawAdditionalActions(module);
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(8f);
        }
    }

    private static string Label(MonoBehaviour module)
    {
        if (module is UltimateWorldGenerator.IslandModule) return "Insel und Spieler-Spawn";
        if (module is UltimateWorldGenerator.LayeredTreeModule) return "Bäume und Holzfällen";
        if (module is UltimateWorldGenerator.PlantModule) return "Pflanzen";
        if (module is UltimateWorldGenerator.WaterModule) return "Wasser und Fische";
        if (module is UltimateWorldGenerator.BeachModule) return "Strand";
        if (module is UltimateWorldGenerator.GrassIslandModule) return "Insel aus Gras-Tiles";
        if (module is UltimateWorldGenerator.AlternatingIslandModule) return "Insel aus gedrehten Tiles";
        if (module is UltimateWorldGenerator.GrassOverlayModule) return "Gras-Ränder";
        return "Bäume aus Prefabs und Sprites";
    }

    private static void DrawAdditionalActions(MonoBehaviour module)
    {
        if (module is GrassIslandGenerator grass && GUILayout.Button("Generate Island")) grass.GenerateIsland();
        if (module is AlternatingTileWorldGenerator world && GUILayout.Button("Generate Island")) world.GenerateWorld();
        if (module is GrassEdgeOverlay overlay && GUILayout.Button("Generate Overlay")) overlay.GenerateOverlay();
        if (module is BeachGenerator beach && module is not TestBeachGen && GUILayout.Button("Generate Beach")) beach.GenerateBeach();
        if (module is TreeGenerator trees && GUILayout.Button("Generate Trees")) trees.GenerateTrees();
    }

    private void OnDisable()
    {
        foreach (UnityEditor.Editor inspector in editors.Values)
        {
            if (inspector != null) DestroyImmediate(inspector);
        }
        editors.Clear();
    }
}

[InitializeOnLoad]
public static class UltimateWorldGeneratorIntegration
{
    private static bool queued;

    static UltimateWorldGeneratorIntegration()
    {
        EditorApplication.hierarchyChanged += QueueInstall;
        EditorSceneManager.sceneOpened += OnSceneOpened;
        QueueInstall();
    }

    private static void OnSceneOpened(Scene scene, OpenSceneMode mode) => QueueInstall();

    private static void QueueInstall()
    {
        if (queued) return;
        queued = true;
        EditorApplication.delayCall += InstallInOpenScenes;
    }

    [MenuItem("Tools/Island Survival/Generator zusammenfassen")]
    public static void InstallInOpenScenes()
    {
        queued = false;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling
            || EditorApplication.isUpdating) return;

        foreach (MonoBehaviour module in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
        {
            if (module == null || !UltimateWorldGenerator.IsGeneratorModule(module)
                || EditorUtility.IsPersistent(module) || !module.gameObject.scene.IsValid()
                || !module.gameObject.scene.isLoaded || EditorSceneManager.IsPreviewSceneObject(module))
                continue;

            UltimateWorldGenerator generator = module.GetComponent<UltimateWorldGenerator>();
            if (generator == null)
                generator = Undo.AddComponent<UltimateWorldGenerator>(module.gameObject);
            ApplyVisibility(generator);
        }
    }

    public static void ApplyVisibility(UltimateWorldGenerator generator)
    {
        bool changed = false;
        foreach (MonoBehaviour module in generator.GetModules())
        {
            HideFlags flags = generator.ShowIndividualComponents
                ? module.hideFlags & ~HideFlags.HideInInspector
                : module.hideFlags | HideFlags.HideInInspector;
            if (module.hideFlags == flags) continue;
            Undo.RecordObject(module, "Generator-Ansicht zusammenfassen");
            module.hideFlags = flags;
            EditorUtility.SetDirty(module);
            changed = true;
        }
        if (changed && generator.gameObject.scene.IsValid() && !EditorSceneManager.IsPreviewSceneObject(generator))
            EditorSceneManager.MarkSceneDirty(generator.gameObject.scene);
    }
}
