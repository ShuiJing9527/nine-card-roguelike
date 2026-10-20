using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
namespace Qiling
{
    public static class ViewComponents
    {
        public static T GetOrAdd<T>(this GameObject go) where T : Component { var component = go.GetComponent<T>(); return component != null ? component : go.AddComponent<T>(); }
    }
    public partial class BattleDemo
    {
        [Header("Editor-authored views; edit these Scene/Prefab objects outside Play")]
        public BattleViewConfig viewConfig;
        public Canvas authoredCanvas;
        public GameObject authoredSelection, authoredBattle, authoredStage;
        public Transform[] unitPositions;
        bool authoringLayout;
        readonly Dictionary<string,int> bindCounts = new Dictionary<string,int>();
        readonly HashSet<Transform> freshElements = new HashSet<Transform>();
        string nextBindingKey;
        static T Component<T>(GameObject go) where T : UnityEngine.Component => go.GetOrAdd<T>();
        bool Fresh(Transform t) => authoringLayout || freshElements.Contains(t);
        RectTransform FindAuthored(Transform parent, string name)
        {
            string id = parent.GetInstanceID() + ":" + name;
            bindCounts.TryGetValue(id, out int number); bindCounts[id] = number + 1;
            nextBindingKey = name + "#" + number;
            if (authoringLayout) return null;
            foreach(Transform child in parent)
            {
                var key = child.GetComponent<BattleUiElement>();
                if(key != null && key.bindingKey == nextBindingKey) { child.gameObject.SetActive(true); return child as RectTransform; }
            }
            if(viewConfig != null) Debug.LogWarning("UI binding missing: " + parent.name + "/" + nextBindingKey + ". Using configured element fallback.");
            return null;
        }
        void RegisterElement(GameObject go, string name)
        {
            go.GetOrAdd<BattleUiElement>().bindingKey = nextBindingKey;
            freshElements.Add(go.transform);
        }
        void ResetBoundTree(GameObject page)
        {
            freshElements.Clear();
            // Hide unused enemy views in single-boss encounters. Only bound views are re-enabled.
            foreach(Transform child in page.transform)
                if(child.name.StartsWith("Unit ") || child.name.StartsWith("Intent ") || child.name.StartsWith("Action Avatar ")) child.gameObject.SetActive(false);
        }
        void Choice(GameObject obj,bool selected)
        {
            var outline=obj.GetOrAdd<Outline>(); outline.enabled=selected; outline.effectColor=gold;
            float width=viewConfig!=null?viewConfig.choiceBorderWidth:2; outline.effectDistance=new Vector2(width,-width);
        }
        void Icon(Transform parent,int index,Sprite sprite,string text,Color tint)
        {
            if(viewConfig?.statusIconPrefab==null) return;
            while(parent.childCount<=index) { var icon=Instantiate(viewConfig.statusIconPrefab,parent); icon.name="Icon "+(parent.childCount-1); icon.transform.localPosition=Vector3.zero; }
            var child=parent.GetChild(index); child.gameObject.SetActive(true);
            var rt=child.GetComponent<RectTransform>(); rt.anchoredPosition=new Vector2(index*27,0);
            var image=child.GetComponent<Image>(); image.sprite=sprite; image.color=tint;
            var label=child.GetComponentInChildren<Text>(true); if(label!=null) { label.text=text; label.gameObject.SetActive(sprite==null); }
        }
        void RefreshStatusIcons(RectTransform row,Combatant unit)
        {
            if(row==null) return; foreach(Transform child in row) child.gameObject.SetActive(false); int index=0;
            if(unit.shield>0) Icon(row,index++,viewConfig?.shieldIcon,"盾",blue);
            foreach(var state in unit.states)
            {
                var style=viewConfig?.statuses.Find(s=>s.id==state.kind.ToString());
                Icon(row,index++,style?.icon,KindName(state.kind).Substring(0,1),style!=null?style.color:gold);
            }
        }
        void RefreshTraitIcons(RectTransform row,CardDef card)
        {
            if(row==null) return; foreach(Transform child in row) child.gameObject.SetActive(false); int index=0;
            foreach(var id in card.traits)
            {
                var trait=catalog.Trait(id); var style=viewConfig?.traits.Find(s=>s.id==id);
                Icon(row,index++,trait.icon!=null?trait.icon:style?.icon,trait.title.Substring(0,1),style!=null?style.color:gold);
            }
        }
        RectTransform ActionRail => uiPage.transform.Find("Shared Action Rail") as RectTransform;
        RectTransform CreateAuthoredModal(RectTransform page, string title)
        {
            HideHover(); bindCounts.Clear(); freshElements.Clear();
            var prefab = !authoringLayout && viewConfig != null ? viewConfig.Panel(title) : null;
            if(prefab != null)
            {
                var obj = Instantiate(prefab, page); obj.name = title; obj.SetActive(true);
                return obj.GetComponent<RectTransform>();
            }
            return Box(page, title, 0, 0, page.rect.width, page.rect.height, new Color(.012f,.022f,.04f,.96f));
        }
        static void DisposeView(GameObject obj)
        {
            if(obj == null) return;
            if(Application.isPlaying) Destroy(obj); else DestroyImmediate(obj);
        }
#if UNITY_EDITOR
        public void PreviewEditorMounts()
        {
            canvas=authoredCanvas; root=canvas.GetComponent<RectTransform>(); stage=authoredStage; uiPage=authoredBattle;
            selectedWeapon=selectedEncounter=0; battle=new BattleEngine(catalog,20261020); battle.Start(0,0);
            authoredSelection.SetActive(false); authoredBattle.SetActive(true); unitViews.Clear();
            for(int i=0;i<battle.Units.Count;i++)
            {
                var avatar=uiPage.transform.Find("Action Avatar "+i).GetComponent<RectTransform>();
                unitViews.Add(new UnitView { body=uiPage.transform.Find("Unit "+i).GetComponent<RectTransform>(),below=uiPage.transform.Find("Unit Info "+i).GetComponent<RectTransform>(),above=uiPage.transform.Find("Intent "+i).GetComponent<RectTransform>(),avatar=avatar,
                    statusIcons=uiPage.transform.Find("Unit Info "+i+"/Status Icons").GetComponent<RectTransform>(),progress=avatar.GetComponentsInChildren<Text>(true).First(t=>t.transform.parent==avatar && t.GetComponent<BattleUiElement>().bindingKey=="Text#1") });
            }
            SpawnUnits(); canvas.GetComponent<CanvasScaler>().SendMessage("Update",SendMessageOptions.DontRequireReceiver); Canvas.ForceUpdateCanvases(); LateUpdate(); ClearUnitVisuals(); battle=null; unitViews.Clear();
        }
        // Explicit one-time migration; never automatically rebuilds or overwrites edited Prefabs.
        public void BuildEditorViews(Action<string,GameObject> save)
        {
            authoringLayout = true;
            font = viewConfig.font;
            debugSession = new DebugSession(); selectedWeapon = selectedEncounter = 0;
            foreach(var weapon in catalog.weapons) debugSession.ForWeapon(catalog,weapon.id);
            BuildCanvas(); authoredCanvas = canvas; BuildStage(); authoredStage = stage;
            ShowSelection(); authoredSelection = uiPage; authoredSelection.name = "Selection Page"; save("SelectionPage",uiPage);
            battle = new BattleEngine(catalog,20261020,CurrentProfile); battle.Start(0,0);
            sequence = new List<string>(); BuildBattlePage(); authoredBattle = uiPage; authoredBattle.name = "Combat Page";
            save("BattlePage",uiPage);
            OpenSettings(); save("Settings Overlay",settingsPanel); CloseSettings();
            OpenPreview(); save("Preview Overlay",previewPanel); ClosePreview();
            ShowLog(); var log = uiPage.transform.Find("Log Overlay"); save("Log Overlay",log.gameObject); DisposeView(log.gameObject); showAllLogs=false;
            OpenTraitEditor(); save("Trait Test Overlay",traitOverlay); CloseTraitEditor();
            // Keep both main page instances available in the scene for editing.
            battle = null; uiPage = authoredSelection; authoredSelection.SetActive(true); authoredBattle.SetActive(false);
            authoringLayout = false;
        }
#endif
    }
}
