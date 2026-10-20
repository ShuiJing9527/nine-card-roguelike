using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Qiling.Editor
{
    public static class DemoSetup
    {
        public const string CatalogPath = "Assets/Data/BattleCatalog.asset";
        public const string ScenePath = "Assets/Scenes/BattleDemo.unity";
        [MenuItem("器灵战斗/生成或打开Demo")]
        public static void OpenDemo()
        {
            EnsureAssets(); EditorSceneManager.OpenScene(ScenePath);
        }
        [MenuItem("器灵战斗/从规则快照重建数据库（覆盖Inspector修改）")]
        public static void Reimport()
        {
            var data = AssetDatabase.LoadAssetAtPath<BattleCatalog>(CatalogPath);
            if (data == null) data = CreateCatalog();
            else { JsonUtility.FromJsonOverwrite(File.ReadAllText("Assets/Data/InitialRules.json"), data); EditorUtility.SetDirty(data); }
            AssetDatabase.SaveAssets();
        }
        static BattleCatalog CreateCatalog()
        {
            if (File.Exists(CatalogPath)) AssetDatabase.DeleteAsset(CatalogPath);
            var data = ScriptableObject.CreateInstance<BattleCatalog>();
            JsonUtility.FromJsonOverwrite(File.ReadAllText("Assets/Data/InitialRules.json"), data);
            AssetDatabase.CreateAsset(data, CatalogPath); return data;
        }
        public static void EnsureAssets()
        {
            var data = AssetDatabase.LoadAssetAtPath<BattleCatalog>(CatalogPath) ?? CreateCatalog();
            if (data.weapons.Count == 0) { JsonUtility.FromJsonOverwrite(File.ReadAllText("Assets/Data/InitialRules.json"), data); EditorUtility.SetDirty(data); }
            var snapshot = ScriptableObject.CreateInstance<BattleCatalog>();
            JsonUtility.FromJsonOverwrite(File.ReadAllText("Assets/Data/InitialRules.json"), snapshot);
            foreach (var trait in snapshot.traits) if (!data.traits.Exists(t => t.id == trait.id)) { data.traits.Add(trait); EditorUtility.SetDirty(data); }
            Object.DestroyImmediate(snapshot);
            if (data.weapons.Count != 5 || data.cards.Count != 45) throw new System.InvalidOperationException("Rule snapshot import incomplete");
            if (!File.Exists(ScenePath))
            {
                Directory.CreateDirectory("Assets/Scenes");
                Scene previous = SceneManager.GetActiveScene(); string previousPath = previous.path;
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var host = new GameObject("Qiling Battle Demo").AddComponent<BattleDemo>(); host.catalog = data;
                EditorSceneManager.SaveScene(scene, ScenePath);
                if (!string.IsNullOrEmpty(previousPath)) EditorSceneManager.OpenScene(previousPath);
            }
            else
            {
                string previousPath = SceneManager.GetActiveScene().path;
                var scene = EditorSceneManager.OpenScene(ScenePath);
                var host = Object.FindFirstObjectByType<BattleDemo>();
                if (host.catalog != data) { host.catalog = data; EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); }
                if (!string.IsNullOrEmpty(previousPath) && previousPath != ScenePath) EditorSceneManager.OpenScene(previousPath);
            }
            // Preserve the original sample scene and 3D project configuration.
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true), new EditorBuildSettingsScene("Assets/Scenes/SampleScene.unity", true) };
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            Debug.Log("Qiling Demo ready: " + ScenePath);
        }
        [MenuItem("器灵战斗/选择战斗数据库")]
        public static void SelectCatalog() { Selection.activeObject = AssetDatabase.LoadAssetAtPath<BattleCatalog>(CatalogPath); }
    }
}
