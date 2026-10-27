using UnityEditor;
using UnityEngine;
namespace Qiling.Editor
{
    public static class SegmentPresentationSetup
    {
        public static void SaveConfiguration()
        {
            var config=AssetDatabase.LoadAssetAtPath<BattleViewConfig>("Assets/Presentation/BattleViewConfig.asset");
            var motion=AssetDatabase.LoadAssetAtPath<SkillPresentation>("Assets/Presentation/Skills/DefaultAttack.asset");
            EditorUtility.SetDirty(config); EditorUtility.SetDirty(motion); AssetDatabase.SaveAssets();
            Debug.Log("Per-segment feedback parameters saved in existing editable presentation assets.");
        }
    }
}
