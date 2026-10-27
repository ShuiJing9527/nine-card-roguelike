using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
namespace Qiling.Editor
{
    /// <summary>Add only the new trait status views; preserve previously authored layout and styles.</summary>
    public static class ChainReactionUiSetup
    {
        [MenuItem("器灵战斗/刷新词条状态文字")]
        public static void RefreshStatusText()
        {
            var config=AssetDatabase.LoadAssetAtPath<BattleViewConfig>("Assets/Presentation/BattleViewConfig.asset");
            var host=new GameObject("Trait Status Text Builder");
            try
            {
                var demo=host.AddComponent<BattleDemo>(); demo.catalog=AssetDatabase.LoadAssetAtPath<BattleCatalog>(DemoSetup.CatalogPath); demo.viewConfig=config;
                string report=(string)typeof(BattleDemo).GetMethod("TraitStatusReport",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(demo,null);
                foreach(var name in new[]{"TraitStatusOverlay","TraitTestOverlay"})
                {
                    string path="Assets/Presentation/UI/"+name+".prefab"; var root=PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        foreach(var text in root.GetComponentsInChildren<Text>(true))
                        {
                            if(text.text.StartsWith("自由重铸不使用正式"))
                            {
                                text.text=report; text.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,text.preferredHeight);
                                text.transform.parent.GetComponent<RectTransform>().SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,text.preferredHeight+22);
                            }
                            if(text.text.StartsWith("词条库：")) text.text=$"词条库：已实现{demo.catalog.traits.Count(TraitSupport.Enabled)} / 未实现{demo.catalog.traits.Count(t=>!t.pending && !TraitSupport.Enabled(t))} / 待定{demo.catalog.traits.Count(t=>t.pending)}";
                        }
                        if(name=="TraitTestOverlay") foreach(var button in root.GetComponentsInChildren<Button>(true))
                        {
                            var trait=demo.catalog.traits.FirstOrDefault(t=>button.name.StartsWith(t.title+"〔"));
                            if(trait==null) continue;
                            string type=(string)typeof(BattleDemo).GetMethod("MatchName",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{trait.match});
                            button.name=trait.title+"〔"+type+"〕"+(TraitSupport.Enabled(trait)?"":trait.pending?" 待定":" 未实现");
                            button.GetComponentInChildren<Text>(true).text=button.name;
                        }
                        PrefabUtility.SaveAsPrefabAsset(root,path);
                    }
                    finally { PrefabUtility.UnloadPrefabContents(root); }
                }
            }
            finally { Object.DestroyImmediate(host); }
            AssetDatabase.SaveAssets();
        }
        [MenuItem("器灵战斗/补齐词条状态面板")]
        public static void UpdateTemplates()
        {
            const string directory="Assets/Presentation/UI/";
            var config=AssetDatabase.LoadAssetAtPath<BattleViewConfig>("Assets/Presentation/BattleViewConfig.asset");
            var root=PrefabUtility.LoadPrefabContents(directory+"TraitTestOverlay.prefab");
            try
            {
                var panel=root.GetComponentsInChildren<RectTransform>(true).Single(t=>t.name=="Trait Test Editor");
                if(!panel.GetComponentsInChildren<BattleUiElement>(true).Any(v=>v.transform.parent==panel && v.bindingKey=="Button#40"))
                {
                    var original=panel.GetComponentsInChildren<Button>(true).Single(b=>b.name=="恢复本武器初始预组词条");
                    var added=Object.Instantiate(original.gameObject,panel); added.name="词条实现状态";
                    added.GetComponent<BattleUiElement>().bindingKey="Button#40";
                    var rt=added.GetComponent<RectTransform>(); rt.anchoredPosition=new Vector2(1408,-951); rt.sizeDelta=new Vector2(167,40);
                    var text=added.GetComponentInChildren<Text>(true); text.text=added.name; text.fontSize=17;
                    text.rectTransform.anchoredPosition=new Vector2(8,-5); text.rectTransform.sizeDelta=new Vector2(151,30);
                    added.GetComponent<Button>().onClick.RemoveAllListeners();
                    PrefabUtility.SaveAsPrefabAsset(root,directory+"TraitTestOverlay.prefab");
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(directory+"TraitStatusOverlay.prefab");
            if(prefab==null)
            {
                var host=new GameObject("Trait Status Template Builder");
                var holder=new GameObject("Trait Status Template Holder",typeof(RectTransform)); holder.layer=5;
                holder.GetComponent<RectTransform>().sizeDelta=new Vector2(1920,1080);
                try
                {
                    var demo=host.AddComponent<BattleDemo>(); demo.catalog=AssetDatabase.LoadAssetAtPath<BattleCatalog>(DemoSetup.CatalogPath); demo.viewConfig=config;
                    const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
                    typeof(BattleDemo).GetField("authoringLayout",flags).SetValue(demo,true);
                    typeof(BattleDemo).GetField("font",flags).SetValue(demo,config.font);
                    typeof(BattleDemo).GetField("traitOverlay",flags).SetValue(demo,holder);
                    typeof(BattleDemo).GetMethod("OpenTraitStatus",flags).Invoke(demo,null);
                    prefab=PrefabUtility.SaveAsPrefabAsset(holder.transform.GetChild(0).gameObject,directory+"TraitStatusOverlay.prefab");
                }
                finally { Object.DestroyImmediate(host); Object.DestroyImmediate(holder); }
            }
            if(!config.panels.Any(p=>p.id=="Trait Status Overlay"))
            {
                config.panels.Add(new ViewTemplate { id="Trait Status Overlay",prefab=prefab }); EditorUtility.SetDirty(config);
            }
            AssetDatabase.SaveAssets(); Debug.Log("Trait status Prefabs are editable and registered; existing views preserved.");
        }
    }
}
