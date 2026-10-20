using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
namespace Qiling.Editor
{
    public static class DemoAuthoringSetup
    {
        const string Base="Assets/Presentation";
        public const string ConfigPath=Base+"/BattleViewConfig.asset";
        static BattleViewConfig config;
        [MenuItem("器灵战斗/外置界面与表现（首次迁移，不覆盖已有配置）")]
        public static void Migrate()
        {
            EditorSceneManager.OpenScene(DemoSetup.ScenePath);
            var demo=UnityEngine.Object.FindFirstObjectByType<BattleDemo>();
            if(demo.viewConfig!=null) { Debug.Log("Already migrated. Edit Scene and Prefabs directly; migration does not overwrite them."); return; }
            foreach(var sub in new[]{"UI","UI/Components","Units","Effects","Materials","Skills"}) Directory.CreateDirectory(Base+"/"+sub);
            AssetDatabase.Refresh();
            config=ScriptableObject.CreateInstance<BattleViewConfig>(); AssetDatabase.CreateAsset(config,ConfigPath);
            var font=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei","SimHei","Noto Sans CJK SC","Arial"},20);
            SaveFont(font); config.font=font;
            var motion=ScriptableObject.CreateInstance<SkillPresentation>(); AssetDatabase.CreateAsset(motion,Base+"/Skills/DefaultAttack.asset"); config.defaultSkill=motion;
            var playerMaterial=Material("Player",new Color(.26f,.72f,.88f));
            var slimeMaterial=Material("Slime",new Color(.4f,.78f,.47f));
            var bossMaterial=Material("RainbowBoss",new Color(.64f,.5f,.92f));
            var lavaMaterial=Material("Lava",new Color(.95f,.3f,.12f));
            var poisonMaterial=Material("Poison",new Color(.46f,.67f,.24f));
            var player=Unit("PlayerPlaceholder",PrimitiveType.Capsule,playerMaterial,Vector3.one,motion);
            var slime=Unit("SlimePlaceholder",PrimitiveType.Sphere,slimeMaterial,new Vector3(1.45f,1,1.4f),motion);
            var boss=Unit("RainbowBossPlaceholder",PrimitiveType.Sphere,bossMaterial,new Vector3(2.2f,1.8f,1.4f),motion);
            var lava=Unit("LavaPlaceholder",PrimitiveType.Sphere,lavaMaterial,new Vector3(1.45f,1,1.4f),motion);
            var poison=Unit("PoisonPlaceholder",PrimitiveType.Sphere,poisonMaterial,new Vector3(1.45f,1,1.4f),motion);
            config.playerPlaceholder=player; config.enemyPlaceholder=slime;
            foreach(var w in demo.catalog.weapons) if(w.visualPrefab==null) w.visualPrefab=player;
            foreach(var m in demo.catalog.monsters) if(m.visualPrefab==null) m.visualPrefab=m.id=="彩虹史莱姆"?boss:m.id=="熔岩史莱姆"?lava:m.id=="毒素史莱姆"?poison:slime;
            foreach(var type in Enum.GetValues(typeof(MatchType)).Cast<MatchType>()) config.types.Add(new NamedVisual { id=type.ToString(),color=type==MatchType.Offense?config.gold:type==MatchType.Survival?new Color(.31f,.79f,.72f):new Color(.74f,.52f,.95f) });
            foreach(var t in demo.catalog.traits) config.traits.Add(new NamedVisual { id=t.id,color=config.gold });
            foreach(var state in Enum.GetValues(typeof(EffectKind)).Cast<EffectKind>()) config.statuses.Add(new NamedVisual { id=state.ToString(),color=config.gold });
            config.elementPrefab=SaveSimple("UI/Components/Element",new GameObject("Element",typeof(RectTransform)));
            var icon=Rect("Status Icon",26,26); var image=icon.AddComponent<Image>(); image.color=config.gold; image.raycastTarget=false;
            var text=Text(icon.transform,"字",20); text.alignment=TextAnchor.MiddleCenter; Stretch(text.rectTransform);
            config.statusIconPrefab=SaveSimple("UI/Components/StatusIcon",icon);
            var floating=Rect("Floating Text",240,45); var ft=Text(floating.transform,"伤害",28); ft.color=config.gold; ft.alignment=TextAnchor.MiddleCenter; Stretch(ft.rectTransform);
            config.floatingTextPrefab=SaveSimple("UI/Components/FloatingText",floating);
            var projectile=GameObject.CreatePrimitive(PrimitiveType.Sphere); projectile.name="Projectile Placeholder"; projectile.transform.localScale=Vector3.one*.1f; projectile.GetComponent<Renderer>().sharedMaterial=Material("Projectile",config.gold); UnityEngine.Object.DestroyImmediate(projectile.GetComponent<Collider>());
            motion.projectile=SaveSimple("Effects/Projectile",projectile); motion.projectileScale=Vector3.one*.1f;
            var effect=new GameObject("Hit Placeholder",typeof(ParticleSystem)); var particles=effect.GetComponent<ParticleSystem>(); particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=particles.main; main.duration=.3f; main.loop=false; main.startLifetime=.2f; main.startSpeed=.7f; main.startSize=.07f; main.startColor=config.gold;
            var emission=particles.emission; emission.rateOverTime=0; emission.SetBursts(new[]{new ParticleSystem.Burst(0,8)});
            effect.GetComponent<ParticleSystemRenderer>().sharedMaterial=Material("HitParticles",config.gold);
            motion.impactEffect=SaveSimple("Effects/HitEffect",effect);
            demo.viewConfig=config;
            demo.BuildEditorViews((id,obj)=>SaveView(id,obj));
            // Scene anchors are ordinary editable transforms. Unit prefabs contain their own mounts.
            demo.unitPositions=new Transform[3];
            for(int i=0;i<3;i++) { var anchor=new GameObject(i==0?"Player Spawn":"Enemy Spawn "+i).transform; anchor.SetParent(demo.authoredStage.transform); anchor.position=i==0?new Vector3(-5,1.1f,2):new Vector3(2.4f+(i-1)*3.1f,.8f,i==2?3:2); demo.unitPositions[i]=anchor; }
            var floor=demo.authoredStage.transform.Find("3D Background Replacement Anchor");
            if(floor!=null)
            {
                floor.GetComponent<Renderer>().sharedMaterial=Material("Floor",new Color(.075f,.115f,.16f));
                PrefabUtility.SaveAsPrefabAssetAndConnect(floor.gameObject,Base+"/Units/Background.prefab",InteractionMode.AutomatedAction);
            }
            EditorUtility.SetDirty(config); EditorUtility.SetDirty(demo.catalog); EditorUtility.SetDirty(demo); EditorUtility.SetDirty(motion);
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(demo.gameObject.scene); EditorSceneManager.SaveScene(demo.gameObject.scene);
            AssetDatabase.Refresh(); Debug.Log("Editable BattleDemo migration complete: "+ConfigPath);
        }
        static void SaveFont(Font font)
        {
            font.RequestCharactersInTexture("器灵战斗 abc0123",20);
            var material=font.material; var texture=material.mainTexture;
            AssetDatabase.CreateAsset(font,Base+"/DemoFont.asset");
            if(material!=null) AssetDatabase.AddObjectToAsset(material,font);
            if(texture!=null) AssetDatabase.AddObjectToAsset(texture,font);
            EditorUtility.SetDirty(font);
        }
        public static void FinishMigration()
        {
            EditorSceneManager.OpenScene(DemoSetup.ScenePath);
            config=AssetDatabase.LoadAssetAtPath<BattleViewConfig>(ConfigPath);
            var font=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei","SimHei","Noto Sans CJK SC","Arial"},20); SaveFont(font); config.font=font;
            var demo=UnityEngine.Object.FindFirstObjectByType<BattleDemo>(); demo.gameObject.GetOrAdd<AudioSource>().playOnAwake=false;
            EditorUtility.SetDirty(config); AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(demo.gameObject.scene); EditorSceneManager.SaveScene(demo.gameObject.scene);
        }
        public static void SanitizePrefabBindings()
        {
            // Converting a textless hit region from the generic Button can inherit its caption.
            // Strip those inherited captions and keep precisely one bound label on actual buttons.
            var paths=Directory.GetFiles(Base+"/UI/Components","*.prefab").Concat(Directory.GetFiles(Base+"/UI","*.prefab")).ToArray();
            foreach(var path in paths)
            {
                var root=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    foreach(var button in root.GetComponentsInChildren<Button>(true).ToArray())
                    {
                        var labels=button.GetComponentsInChildren<Text>(true).Where(t=>t.transform.parent==button.transform).ToArray();
                        if(button.GetComponent<HandCardInteraction>()!=null) { foreach(var t in labels) UnityEngine.Object.DestroyImmediate(t.gameObject); continue; }
                        if(labels.Length==0) continue;
                        string caption=button.name=="Pause Battle"?"暂停":button.name;
                        var keep=labels.FirstOrDefault(t=>t.text==caption) ?? labels[0];
                        foreach(var t in labels) if(t!=keep) UnityEngine.Object.DestroyImmediate(t.gameObject);
                        keep.text=path.EndsWith("/Button.prefab")?"按钮":caption;
                    }
                    PrefabUtility.SaveAsPrefabAsset(root,path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            config=AssetDatabase.LoadAssetAtPath<BattleViewConfig>(ConfigPath); config.selectPulse=.98f;
            var data=AssetDatabase.LoadAssetAtPath<BattleCatalog>(DemoSetup.CatalogPath);
            foreach(var w in data.weapons) config.units.Add(new NamedVisual { id=w.title,color=config.player });
            foreach(var m in data.monsters) config.units.Add(new NamedVisual { id=m.id,color=m.id=="彩虹史莱姆"?new Color(.67f,.48f,.85f):m.id=="熔岩史莱姆"?new Color(.88f,.36f,.18f):new Color(.3f,.72f,.48f) });
            EditorUtility.SetDirty(config); AssetDatabase.SaveAssets();
            Debug.Log("Sanitized inherited button captions in editable UI prefabs.");
        }
        public static void SaveEditorMountPreview()
        {
            EditorSceneManager.OpenScene(DemoSetup.ScenePath); var demo=UnityEngine.Object.FindFirstObjectByType<BattleDemo>(); demo.PreviewEditorMounts();
            PrefabUtility.ApplyPrefabInstance(demo.authoredBattle,InteractionMode.AutomatedAction);
            demo.authoredBattle.SetActive(false); demo.authoredSelection.SetActive(true);
            EditorSceneManager.MarkSceneDirty(demo.gameObject.scene); EditorSceneManager.SaveScene(demo.gameObject.scene); AssetDatabase.SaveAssets();
        }
        static void SaveView(string id,GameObject root)
        {
            // Nested reusable components retain all local property overrides.
            foreach(var b in root.GetComponentsInChildren<Button>(true).Where(b=>b.GetComponent<HandCardInteraction>()==null).ToArray()) Nest(b.gameObject,"Button");
            if(id=="BattlePage")
            {
                foreach(Transform child in root.transform.Find("Hand Faces")) Nest(child.gameObject,"Card");
                foreach(Transform child in root.transform.Find("Hand Hit Regions")) Nest(child.gameObject,"CardHitRegion");
                foreach(Transform child in root.transform)
                {
                    if(child.name.StartsWith("Slot ")) Nest(child.gameObject,"SequenceSlot");
                    if(child.name.StartsWith("Unit Info ")) { var health=child.Find("Health Bar"); if(health!=null) Nest(health.gameObject,"HealthBar"); Nest(child.gameObject,"UnitHud"); }
                    if(child.name.StartsWith("Action Avatar ")) Nest(child.gameObject,"ActionAvatar");
                    if(child.name.StartsWith("Intent ")) Nest(child.gameObject,"EnemyIntent");
                }
            }
            string assetName=id.Replace(" ","");
            var prefab=PrefabUtility.SaveAsPrefabAssetAndConnect(root,Base+"/UI/"+assetName+".prefab",InteractionMode.AutomatedAction);
            if(id=="SelectionPage") config.selectionPage=prefab;
            else if(id=="BattlePage") config.battlePage=prefab;
            else config.panels.Add(new ViewTemplate { id=id,prefab=prefab });
        }
        static void Nest(GameObject go,string name)
        {
            if(PrefabUtility.IsAnyPrefabInstanceRoot(go)) PrefabUtility.UnpackPrefabInstance(go,PrefabUnpackMode.OutermostRoot,InteractionMode.AutomatedAction);
            string path=Base+"/UI/Components/"+name+".prefab";
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(prefab==null) PrefabUtility.SaveAsPrefabAssetAndConnect(go,path,InteractionMode.AutomatedAction);
            else PrefabUtility.ConvertToPrefabInstance(go,prefab,new ConvertToPrefabInstanceSettings { objectMatchMode=ObjectMatchMode.ByHierarchy,recordPropertyOverridesOfMatches=true,gameObjectsNotMatchedBecomesOverride=true,componentsNotMatchedBecomesOverride=true,changeRootNameToAssetName=false },InteractionMode.AutomatedAction);
        }
        static Material Material(string name,Color color)
        {
            var material=new Material(Shader.Find("Universal Render Pipeline/Lit")); material.color=color;
            AssetDatabase.CreateAsset(material,Base+"/Materials/"+name+".mat"); return material;
        }
        static GameObject Unit(string name,PrimitiveType shape,Material material,Vector3 scale,SkillPresentation motion)
        {
            var root=new GameObject(name); var rig=root.AddComponent<UnitPresentation>(); rig.motion=motion;
            var visual=new GameObject("Visual Root"); visual.transform.SetParent(root.transform,false); rig.visualRoot=visual.transform;
            var mesh=GameObject.CreatePrimitive(shape); mesh.name="Replace Model Or Sprite Here"; mesh.transform.SetParent(visual.transform,false); mesh.transform.localScale=scale; mesh.GetComponent<Renderer>().sharedMaterial=material; UnityEngine.Object.DestroyImmediate(mesh.GetComponent<Collider>());
            rig.healthAnchor=Anchor(visual.transform,"Health Anchor",new Vector3(0,-scale.y*.5f,0));
            rig.launchAnchor=Anchor(visual.transform,"Launch Anchor",new Vector3(0,.1f,-.4f));
            rig.hitAnchor=Anchor(visual.transform,"Hit Anchor",Vector3.zero);
            rig.groundAnchor=Anchor(visual.transform,"Ground Anchor",new Vector3(0,-scale.y*.5f,0));
            Anchor(visual.transform,"Status Anchor (assign if needed)",new Vector3(0,-scale.y*.5f-.4f,0));
            return SaveSimple("Units/"+name,root);
        }
        static Transform Anchor(Transform parent,string name,Vector3 position) { var go=new GameObject(name); go.transform.SetParent(parent,false); go.transform.localPosition=position; return go.transform; }
        static GameObject SaveSimple(string path,GameObject go) { var prefab=PrefabUtility.SaveAsPrefabAsset(go,Base+"/"+path+".prefab"); UnityEngine.Object.DestroyImmediate(go); return prefab; }
        static GameObject Rect(string name,float width,float height) { var go=new GameObject(name,typeof(RectTransform)); go.layer=5; var rt=go.GetComponent<RectTransform>(); rt.anchorMin=rt.anchorMax=new Vector2(0,1); rt.pivot=new Vector2(.5f,.5f); rt.sizeDelta=new Vector2(width,height); return go; }
        static Text Text(Transform parent,string value,int size) { var go=Rect("Label",100,30); go.transform.SetParent(parent,false); var text=go.AddComponent<Text>(); text.font=config.font; text.text=value; text.fontSize=size; text.color=config.text; text.raycastTarget=false; return text; }
        static void Stretch(RectTransform rt) { rt.anchorMin=Vector2.zero;rt.anchorMax=Vector2.one;rt.offsetMin=rt.offsetMax=Vector2.zero; }
    }
}
