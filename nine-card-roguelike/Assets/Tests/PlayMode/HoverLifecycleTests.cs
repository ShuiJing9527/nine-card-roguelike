using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
namespace Qiling.Tests
{
    public class HoverLifecycleTests
    {
        BattleDemo demo;
        GameObject Popup => demo.authoredBattle.transform.Find("Hover Details").gameObject;
        string Details => Popup.GetComponentInChildren<ScrollRect>(true).content.GetComponentInChildren<Text>(true).text;
        [UnitySetUp] public IEnumerator Setup()
        {
            DebugFiles.RootOverride=Path.GetFullPath("../Validation/DebugTestFiles/"+Guid.NewGuid().ToString("N"));
            SceneManager.LoadScene("BattleDemo"); yield return null; yield return null; demo=UnityEngine.Object.FindFirstObjectByType<BattleDemo>();
            DemoFlowTests.Find("开始战斗").onClick.Invoke(); yield return null; demo.clockScale=0;
            var battle=demo.Battle; battle.Waiting.Clear(); battle.ActiveId=null; foreach(var unit in battle.Units) unit.progress=0; battle.Player.progress=100; battle.Advance(0);
            for(int i=0;i<3;i++) Add(i); yield return null;
        }
        [UnityTearDown] public IEnumerator Cleanup() { DebugFiles.RootOverride=null; yield return null; }
        void Add(int index) => GameObject.Find("Hand Card "+index).GetComponent<Button>().onClick.Invoke();
        BattleHover Slot(int index) => demo.authoredBattle.transform.Find("Slot "+index).GetComponent<BattleHover>();
        HandCardInteraction Hand(int index) => demo.authoredBattle.transform.Find("Hand Hit Regions/Hand Card "+index).GetComponent<HandCardInteraction>();
        void EnterSlot(int index) { Slot(index).OnPointerEnter(new PointerEventData(EventSystem.current)); Assert.IsTrue(Popup.activeSelf); }
        void SlotButton(int index,string title) => Slot(index).GetComponentsInChildren<Button>().Single(b=>b.GetComponentInChildren<Text>().text==title).onClick.Invoke();
        void AssertCard(string card) => Assert.AreEqual(demo.catalog.Skill(demo.catalog.Card(card).skill).title,Details.Split('\n')[0]);
        [UnityTest] public IEnumerator SlotRemovalInvalidatesHoveredCardInsteadOfIndexingShorterSequence()
        {
            EnterSlot(2); AssertCard(demo.CurrentSequence[2]); SlotButton(2,"移除");
            yield return null; demo.SendMessage("UpdateHover"); Assert.IsFalse(Popup.activeSelf);
            EnterSlot(1); SlotButton(0,"移除"); yield return null; Assert.IsFalse(Popup.activeSelf); LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator ReorderAndClearNeverRetargetAnOpenSlotTooltipToAnotherCard()
        {
            EnterSlot(0); string original=demo.CurrentSequence[0]; SlotButton(0,"→"); yield return null; Assert.IsFalse(Popup.activeSelf); Assert.AreNotEqual(original,demo.CurrentSequence[0]);
            EnterSlot(0); AssertCard(demo.CurrentSequence[0]); DemoFlowTests.Find("清空").onClick.Invoke(); yield return null; Assert.IsFalse(Popup.activeSelf);
            Add(8); yield return null; Assert.IsFalse(Popup.activeSelf); EnterSlot(0); AssertCard(demo.CurrentSequence[0]); LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator AllHandsStayBoundToTheirPhysicalCardAcrossSequenceEdits()
        {
            DemoFlowTests.Find("清空").onClick.Invoke();
            string[] cards=demo.catalog.weapons[0].cards.ToArray();
            for(int i=0;i<9;i++) { Hand(i).OnPointerEnter(new PointerEventData(EventSystem.current)); AssertCard(cards[i]); Hand(i).OnPointerExit(new PointerEventData(EventSystem.current)); }
            Hand(1).OnPointerEnter(new PointerEventData(EventSystem.current)); var stale=Hand(1).Hovered;
            Add(1); yield return null; Assert.IsFalse(Popup.activeSelf); Assert.IsFalse(Hand(1).gameObject.activeSelf);
            stale(true); Assert.IsFalse(Popup.activeSelf);
            SlotButton(0,"移除"); yield return null; Assert.IsFalse(Popup.activeSelf); Assert.IsTrue(Hand(1).gameObject.activeSelf);
            Hand(1).OnPointerEnter(new PointerEventData(EventSystem.current)); AssertCard(cards[1]); Assert.IsTrue(Details.Contains("尚未排入"));
            DemoFlowTests.Find("清空").onClick.Invoke(); yield return null; Assert.IsFalse(Popup.activeSelf); LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator RetryInvalidatesOldEnterExitAndPanelCallbacksEvenWhenViewsAreReused()
        {
            EnterSlot(1); var oldSlot=Slot(1).Changed; var oldHand=Hand(8).Hovered; var oldPanel=Popup.GetComponent<BattleHover>().Changed;
            demo.Retry(false); demo.clockScale=0; yield return null; Assert.IsFalse(Popup.activeSelf);
            oldSlot(true); oldHand(true); oldPanel(true); Assert.IsFalse(Popup.activeSelf);
            EnterSlot(0); string expected=demo.CurrentSequence[0]; oldSlot(false); oldHand(false); oldPanel(false);
            yield return new WaitForSecondsRealtime(.2f); Assert.IsTrue(Popup.activeSelf); AssertCard(expected); LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator ExitWeaponChangeAndDisabledSourcesCannotResurrectStaleDetails()
        {
            EnterSlot(0); var staleSlot=Slot(0).Changed; var staleHand=Hand(8).Hovered;
            DemoFlowTests.Find("设置").onClick.Invoke(); DemoFlowTests.Find("更换武器").onClick.Invoke(); yield return null; staleSlot(true); staleHand(true); Assert.IsFalse(Popup.activeSelf);
            string weapon=demo.catalog.weapons[1].title; DemoFlowTests.Find(weapon).onClick.Invoke(); DemoFlowTests.Find("开始战斗").onClick.Invoke(); demo.clockScale=0; yield return null;
            staleSlot(true); staleHand(true); Assert.IsFalse(Popup.activeSelf);
            var hand=Hand(0); hand.OnPointerEnter(new PointerEventData(EventSystem.current)); AssertCard(demo.catalog.weapons[1].cards[0]); hand.gameObject.SetActive(false); yield return null; Assert.IsFalse(Popup.activeSelf);
            LogAssert.NoUnexpectedReceived();
        }
        void AssertCardZones()
        {
            var order=demo.catalog.weapons[0].cards;
            Assert.AreEqual(9,demo.CurrentHand.Count+demo.CurrentSequence.Count);
            CollectionAssert.AreEquivalent(order,demo.CurrentHand.Concat(demo.CurrentSequence));
            Assert.AreEqual(9,demo.CurrentHand.Concat(demo.CurrentSequence).Distinct().Count());
            CollectionAssert.AreEqual(order.Where(id=>!demo.CurrentSequence.Contains(id)),demo.CurrentHand);
            var active=Enumerable.Range(0,9).Select(Hand).Where(h=>h.gameObject.activeInHierarchy).ToArray();
            CollectionAssert.AreEqual(demo.CurrentHand,active.Select(h=>h.CardId));
            foreach(var hand in Enumerable.Range(0,9).Select(Hand)) Assert.AreEqual(hand.gameObject.activeSelf,hand.face.gameObject.activeSelf);
            for(int i=1;i<active.Length;i++) Assert.Greater(active[i].face.anchoredPosition.x,active[i-1].face.anchoredPosition.x);
        }
        [UnityTest] public IEnumerator TransferCompactsReturnsInOriginalOrderAndRejectsStaleHover()
        {
            AssertCardZones();
            var oldHover=Slot(0).Changed; var oldHandHover=Hand(8).Hovered;
            SlotButton(0,"→"); AssertCardZones(); oldHover(true); oldHandHover(true);
            Assert.AreEqual(3,demo.CurrentSequence.Count); Assert.IsFalse(Popup.activeSelf);
            SlotButton(1,"移除"); AssertCardZones(); Assert.IsTrue(Hand(0).gameObject.activeSelf);
            Add(0); Add(8); AssertCardZones();
            var active=Enumerable.Range(0,9).Select(Hand).Where(h=>h.gameObject.activeSelf).ToArray();
            foreach(var hand in active)
            {
                var screen=RectTransformUtility.WorldToScreenPoint(null,hand.transform.position);
                Assert.AreEqual(1,active.Count(h=>h.IsRaycastLocationValid(screen,null)),"Compacted hit bands must have one owner");
            }
            DemoFlowTests.Find("清空").onClick.Invoke(); AssertCardZones();
            foreach(var hand in active) Assert.AreEqual(hand.AuthoredPosition,hand.face.anchoredPosition);
            DemoFlowTests.Find("清空").onClick.Invoke(); Add(7); Assert.AreEqual(1,demo.CurrentSequence.Count); AssertCardZones();
            yield return null; LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator ConfirmLocksUntilNextNormalPlayerTurnAndRetryRestoresExclusiveZones()
        {
            var committed=demo.CurrentSequence.ToArray(); DemoFlowTests.Find("确认出招").onClick.Invoke();
            Assert.IsTrue(demo.SequenceLocked); AssertCardZones();
            SlotButton(0,"移除"); DemoFlowTests.Find("清空").onClick.Invoke();
            CollectionAssert.AreEqual(committed,demo.CurrentSequence);
            for(int i=0;i<1000 && demo.PresentationBusy;i++) yield return null;
            Assert.IsFalse(demo.PresentationBusy); CollectionAssert.AreEqual(committed,demo.CurrentSequence); Assert.IsTrue(demo.SequenceLocked);
            demo.Retry(false); demo.clockScale=0; yield return null;
            CollectionAssert.AreEqual(committed,demo.CurrentSequence); Assert.IsFalse(demo.SequenceLocked); AssertCardZones();
            var battle=demo.Battle; battle.Waiting.Clear(); battle.ActiveId=null; foreach(var u in battle.Units) u.progress=0; battle.Player.progress=100; battle.Advance(0); yield return null;
            DemoFlowTests.Find("确认出招").onClick.Invoke();
            for(int i=0;i<1000 && demo.PresentationBusy;i++) yield return null;
            battle.Waiting.Clear(); battle.ActiveId=null; foreach(var u in battle.Units) u.progress=0; battle.Player.progress=100; battle.Advance(0); yield return null;
            Assert.IsFalse(demo.SequenceLocked); Assert.AreEqual(0,demo.CurrentSequence.Count); Assert.AreEqual(9,demo.CurrentHand.Count); AssertCardZones();
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator TooltipCanStillBeEnteredAndScrolledThenClosesWhenSourceDisappears()
        {
            EnterSlot(0); var slot=Slot(0); slot.OnPointerExit(new PointerEventData(EventSystem.current)); Popup.GetComponent<BattleHover>().OnPointerEnter(new PointerEventData(EventSystem.current));
            yield return new WaitForSecondsRealtime(.2f); Assert.IsTrue(Popup.activeSelf); AssertCard(demo.CurrentSequence[0]);
            slot.gameObject.SetActive(false); yield return null; Assert.IsFalse(Popup.activeSelf); LogAssert.NoUnexpectedReceived();
        }
    }
}
