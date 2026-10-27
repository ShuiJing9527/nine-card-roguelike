using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
namespace Qiling.Tests
{
    public class ChainGrowthBalanceTests
    {
        const double Horizon=180;
        static readonly int[] Seeds={11,29,47,71,101,137,173,211};
        static readonly string[][] Builds={new[]{"锋锐"},new[]{"追击"},new[]{"追击","锋锐","延续"},new[]{"强化","增幅","延续"}};
        static readonly string[] BuildNames={"纯锋锐","纯追击","追击+锋锐+延续","强化+增幅+延续"};
        sealed class Row
        {
            public string weapon,buff,build,scenario,strategy;
            public int seed,damage,calculated,shield,lifeCost,playerActions,buffCasts,attackCasts;
            public double covered,alive;
            public int hp;
            public string Csv() => string.Join(",",new[]{weapon,buff,build,scenario,strategy,seed.ToString(),damage.ToString(),calculated.ToString(),shield.ToString(),lifeCost.ToString(),hp.ToString(),alive.ToString("F3",CultureInfo.InvariantCulture),(covered/Horizon*100).ToString("F3",CultureInfo.InvariantCulture),(covered/Math.Max(.001,alive)*100).ToString("F3",CultureInfo.InvariantCulture),playerActions.ToString(),buffCasts.ToString(),attackCasts.ToString()});
        }
        [Test] public void CompareRepeatedAttacksAndBuffRenewalOverEqualNaturalTimeForEveryWeapon()
        {
            var source=AssetDatabase.LoadAssetAtPath<BattleCatalog>("Assets/Data/BattleCatalog.asset");
            var rows=new List<Row>();
            foreach(var weapon in source.weapons)
            foreach(var card in weapon.cards.Where(id=>ChainGrowthTests.Buffs.Contains(source.Card(id).skill)))
            for(int build=0;build<Builds.Length;build++)
            foreach(bool pressure in new[]{false,true})
            foreach(bool renew in new[]{false,true})
            foreach(int seed in Seeds) rows.Add(Run(source,weapon,card,build,pressure,renew,seed));
            // Expansion effects are bound only in isolated simulation copies; the authored nine-card decks stay intact.
            foreach(var weapon in source.weapons)
            foreach(string skill in new[]{"战意","护身之风","疾风步","坚岩加护","盾墙号令"})
            {
                if(skill!="战意" && !((weapon.id=="猎弓" && (skill=="护身之风" || skill=="疾风步")) || (weapon.id=="法杖" && skill=="坚岩加护") || (weapon.id=="盾牌" && skill=="盾墙号令"))) continue;
                for(int build=0;build<Builds.Length;build++)
                foreach(bool pressure in new[]{false,true})
                foreach(bool renew in new[]{false,true})
                foreach(int seed in Seeds) rows.Add(Run(source,weapon,weapon.cards[8],build,pressure,renew,seed,skill));
            }
            string output=Environment.GetEnvironmentVariable("QILING_AUDIT_OUTPUT")??Path.GetFullPath("../Validation/ChainGrowth"); Directory.CreateDirectory(output);
            File.WriteAllLines(Path.Combine(output,"battle-benefit-raw.csv"),new[]{"weapon,buff,build,scenario,strategy,seed,hp_damage,calculated_damage,shield_absorbed,player_hp_lost,remaining_hp,alive_seconds,coverage_full_window_pct,coverage_while_alive_pct,player_actions,buff_casts,attack_casts"}.Concat(rows.Select(r=>r.Csv())),new System.Text.UTF8Encoding(true));
            var summary=new List<string>{"# 等战斗时间增益收益对照","","180秒自然战斗时间；每组8个相同配对种子。单Boss耐久靶仅将Boss生命改为1000000以避免提前胜利，保留双方原面板、速度、技能、双防换相。无反击靶跳过敌方施放但仍行动/换相；压力靶正常施放技能。玩家使用原生命，死亡后剩余窗口伤害记0。动画耗时不计入自然时间。","","所有构筑对九张实体卡统一配置；攻击使用该武器1号预组攻击技能+初始顺序补满五卡，增益轮将被测增益放1号位并补满五卡。策略为持续攻击，或首次施放增益、状态完全到期后的下一次自身行动补增益。逐段计算、穿透、暴击、护盾、状态、Boss相位和计时均走正式BattleEngine。覆盖率按自然时间积分，非按排谱次数。","","护盾强化配持续攻击没有护盾来源，因此测试它的独立准备收益预期为0；此处不自动补盾或编造防御收益。","","|武器|增益|构筑|场景|直接伤害均值|准备后伤害均值|伤害差|准备后覆盖率(全180秒)|直接/准备存活秒|直接/准备失血|","|---|---|---|---|---:|---:|---:|---:|---:|---:|"};
            summary.Insert(6,"包含8个预组增益配置和9个适用武器/扩展增益组合（战意用于五武器，其余四种按原适用武器），共2176场。扩展增益仅在独立测试数据副本中绑定到第9张实体卡，原预组配置不变；每场仍为五张连续排谱，正常行动按原规则计时。");
            foreach(var group in rows.GroupBy(r=>new{r.weapon,r.buff,r.build,r.scenario}))
            {
                var direct=group.Where(r=>r.strategy=="持续攻击").ToArray(); var prep=group.Where(r=>r.strategy=="到期补增益").ToArray();
                double d=direct.Average(r=>r.damage),p=prep.Average(r=>r.damage);
                summary.Add(FormattableString.Invariant($"|{group.Key.weapon}|{group.Key.buff}|{group.Key.build}|{group.Key.scenario}|{d:F1}|{p:F1}|{(p/d-1)*100:F1}%|{prep.Average(r=>r.covered)/Horizon*100:F1}%|{direct.Average(r=>r.alive):F1}/{prep.Average(r=>r.alive):F1}|{direct.Average(r=>r.lifeCost):F1}/{prep.Average(r=>r.lifeCost):F1}|"));
            }
            File.WriteAllLines(Path.Combine(output,"战斗收益对照.md"),summary,new System.Text.UTF8Encoding(true));
            Assert.AreEqual(17*4*2*2*Seeds.Length,rows.Count); Assert.IsTrue(rows.All(r=>r.alive>0 && r.alive<=Horizon+.001)); Assert.IsTrue(rows.All(r=>r.damage>0));
            TestContext.WriteLine($"Completed {rows.Count} full-window simulations. Output: {output}");
        }
        static Row Run(BattleCatalog source,WeaponDef weapon,string buffCard,int build,bool pressure,bool renew,int seed,string extension=null)
        {
            var data=UnityEngine.Object.Instantiate(source);
            try
            {
                var profile=DebugLoadout.Initial(data,weapon.id);
                if(extension!=null)
                {
                    data.Card(buffCard).skill=extension;
                    data.Card(buffCard).match=extension=="护身之风"?MatchType.Survival:MatchType.Status;
                }
                foreach(var card in profile.cards) card.traits=Builds[build].ToList();
                Assert.IsNull(profile.Validate(data));
                var e=new BattleEngine(data,seed,profile); e.Start(data.weapons.FindIndex(w=>w.id==weapon.id),1);
                e.Units[1].hp=e.Units[1].panel.hp=1000000;
                string buff=data.Card(buffCard).skill;
                var attackCards=weapon.cards.Take(5).ToArray();
                var buffCards=new[]{buffCard}.Concat(weapon.cards.Where(id=>id!=buffCard).Take(4)).ToArray();
                var row=new Row {weapon=weapon.title,buff=buff,build=BuildNames[build],scenario=pressure?"Boss压力靶":"无反击耐久靶",strategy=renew?"到期补增益":"持续攻击",seed=seed};
                int guard=0;
                while(e.NaturalTime<Horizon-1e-8 && !e.Finished)
                {
                    Assert.Less(++guard,100000);
                    if(e.Active==null)
                    {
                        bool covered=e.Player.states.Any(s=>s.key.StartsWith(buff+":",StringComparison.Ordinal)); double before=e.NaturalTime;
                        e.Advance(Math.Min(.25,Horizon-before)); if(covered) row.covered+=e.NaturalTime-before;
                    }
                    if(e.NaturalTime>=Horizon-1e-8) break;
                    if(e.Active==null) continue;
                    int hpBefore=e.Player.hp;
                    if(e.Active.player)
                    {
                        bool castBuff=renew && !e.Player.states.Any(s=>s.key.StartsWith(buff+":",StringComparison.Ordinal));
                        var cards=castBuff?buffCards:attackCards;
                        string target=castBuff?"player":"enemy0";
                        Assert.IsNull(e.Confirm(cards,target),weapon.id+" / "+buff);
                        if(castBuff) row.buffCasts++; else row.attackCasts++;
                        row.playerActions++;
                    }
                    else if(pressure) e.EnemyAction(); else e.EndAction(e.Active);
                    row.lifeCost+=Math.Max(0,hpBefore-e.Player.hp);
                    e.Log.Clear(); e.VisualEvents.Clear(); // Reporting does not need cosmetic snapshots; no combat state is changed.
                }
                row.alive=e.NaturalTime; row.hp=e.Player.hp;
                var damage=e.Damage.Where(d=>d.source=="player" && d.target!="player").ToArray();
                row.damage=damage.Sum(d=>d.hpLost); row.calculated=damage.Sum(d=>d.calculated); row.shield=damage.Sum(d=>d.absorbed);
                return row;
            }
            finally { UnityEngine.Object.DestroyImmediate(data); }
        }
    }
}
