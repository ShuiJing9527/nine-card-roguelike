using System;
using System.Collections.Generic;
using System.Linq;
namespace Qiling
{
    public sealed class ConditionalBonus
    {
        public string id;
        public int slot,currentChain;
        public double amount,threshold;
        public bool Applies(Combatant target) => id=="压制" ? target.hp>target.panel.hp*threshold : id=="终结" ? target.hp<target.panel.hp*threshold : target.states.Any(s=>s.negative);
    }
    public sealed class MirrorGroup { public int slot,currentChain; public double strength; }
    public sealed partial class BattleEngine
    {
        static bool IsGain(string id) => id=="连锁收益" || id=="连锁蓄势" || id=="连锁协奏" || id=="同调" || id=="交织";
        bool GainActive(string id,int index,int current,IReadOnlyList<string> cards)
        {
            switch(id)
            {
                case "连锁收益": return current%3==0;
                case "连锁蓄势": return current%2==1;
                case "连锁协奏": return current%2==0;
                case "同调": return index>0 && Data.Card(cards[index]).match==Data.Card(cards[index-1]).match;
                case "交织": return index>0 && Data.Card(cards[index]).match!=Data.Card(cards[index-1]).match;
                default:return false;
            }
        }
        double TraitGain(CardDef card,int index,int current,IReadOnlyList<string> cards,MatchType main,ActionPlan plan)
        {
            double gain=1;
            foreach(var trait in card.traits.Select(Data.Trait).Where(t=>IsGain(t.id) && TraitSupport.Enabled(t) && BattleMath.Matches(t.match,main)))
            {
                bool active=GainActive(trait.id,index,current,cards);
                if(active) gain+=trait.rate;
                plan.traitHints.Add($"{index+1}号位 收益条件{trait.id}〔修正后当前连锁{current}〕：{(active?"触发":"未触发")}，收益增量{(active?trait.rate:0):P2}；不放大收益词条或连锁反应。");
            }
            return gain;
        }
        string MirrorRestriction(SkillDef skill,int chain,double strength)
        {
            if(skill.shieldCost>0 || skill.shieldResource!=0) return "特殊资源消耗/资源转伤的镜像复制未定，不免费复制资源效果";
            if(skill.pattern==Pattern.Counter) return "反击姿态复制的触发与持续规则未定";
            foreach(var effect in skill.effects)
            {
                if(effect.pending || effect.kind==EffectKind.Bleed) return "原技能包含仍待定的状态效果";
                if(effect.kind==EffectKind.Freeze || effect.kind==EffectKind.Guard || effect.kind==EffectKind.DebuffImmune || effect.kind==EffectKind.Cleanse)
                    return "非数值控制/守护/免疫/驱散的镜像复制未定："+effect.kind;
                if(effect.HasDuration && Math.Abs(strength-1)>1e-9) return "持续回合随镜像强度缩放及取整未定（100%基础复制可用）";
                if(effect.HasDuration && !effect.stacking && effect.kind!=EffectKind.Burn && effect.kind!=EffectKind.Evade && effect.kind!=EffectKind.CritUp)
                    return "同名限时效果重复幅度未定，镜像不额外叠加该组合";
                double value=effect.value.At(chain)*strength;
                if((effect.stacking || effect.kind==EffectKind.Block) && Math.Abs(value-Math.Round(value))>1e-9)
                    return "状态层数/格挡次数的镜像缩放产生小数，取整与最低值未定";
            }
            return null;
        }
        ActionPlan MirrorPlan(Combatant actor,ActionPlan source,double strength)
        {
            var original=source.skill; int n=source.chain;
            var skill=new SkillDef { id=original.id,title="镜像·"+original.title,description=original.description,note=original.note,presentation=original.presentation,
                damageType=original.damageType,reach=original.reach,target=original.target,attribute=original.attribute,pattern=original.pattern,
                coefficient=new Linear { basis=original.coefficient.At(n)*strength },segments=new Linear { basis=original.segments.At(n) },
                penetration=new Linear { basis=original.penetration.At(n)*strength },critBonus=new Linear { basis=original.critBonus.At(n)*strength },
                bounceCoefficient=original.bounceCoefficient*strength,landingCoefficient=original.landingCoefficient*strength,duration=original.duration,durationPerChain=original.durationPerChain };
            foreach(var e in original.effects)
            {
                double value=e.value.At(n)*strength;
                // Restrictions already excluded fractional discrete values. Normalize floating-point noise only.
                if(e.stacking || e.kind==EffectKind.Block) value=Math.Round(value);
                skill.effects.Add(new EffectDef { kind=e.kind,attribute=e.attribute,value=new Linear { basis=value },duration=e.duration,durationPerChain=e.durationPerChain,
                    onHit=e.onHit,stacking=e.stacking,percent=e.percent,rangedOnly=e.rangedOnly,negative=e.negative,pending=e.pending,note=e.note });
            }
            int segments=Math.Max(1,(int)skill.segments.At(n));
            if(skill.pattern==Pattern.Bounce) segments=LegalTargets(actor,skill).Count>=2?1+n:1;
            return new ActionPlan { skill=skill,chain=n,isMirror=true,nativeCoefficient=skill.coefficient.At(n),nativeSegments=segments,
                nativeTotal=skill.pattern==Pattern.Bounce?skill.coefficient.At(n)+(segments-1)*skill.bounceCoefficient:skill.coefficient.At(n)*segments,
                crit=BattleMath.Clamp(actor.panel.crit+actor.Sum(EffectKind.CritUp)+skill.critBonus.At(n),0,Data.rules.critCap),critDamage=actor.panel.critDamage,
                physicalPen=skill.damageType==DamageType.Physical?BattleMath.Clamp(skill.penetration.At(n),0,Data.rules.penetrationCap):0,
                specialPen=skill.damageType==DamageType.Special?BattleMath.Clamp(skill.penetration.At(n),0,Data.rules.penetrationCap):0 };
        }
        void ExecuteMirrors(Combatant actor,ActionPlan source,string targetId,CastContext context)
        {
            if(source.isMirror) return;
            foreach(var group in source.mirrors)
            {
                if(!actor.Alive) break;
                string restriction=MirrorRestriction(source.skill,source.chain,group.strength);
                if(restriction!=null) { Write("镜像不执行："+restriction); continue; }
                var mirror=MirrorPlan(actor,source,group.strength);
                var legal=LegalTargets(actor,mirror.skill); var target=legal.Find(u=>u.id==targetId)??RandomTarget(legal);
                if(target==null) { Write("镜像停止：无合法目标"); break; }
                // Undefined repeated fixed buff magnitudes stay restricted just as in normal Validate.
                var repeated=mirror.skill.effects.FirstOrDefault(e=>e.HasDuration && !e.stacking && e.kind!=EffectKind.Burn && e.kind!=EffectKind.Evade && e.kind!=EffectKind.CritUp && target.states.Any(s=>s.key==mirror.skill.id+":"+e.kind));
                if(repeated!=null) { Write("镜像不执行：同名限时效果重复幅度未定："+repeated.kind); continue; }
                Write($"镜像[{group.slot}号位/当前连锁{group.currentChain}] 强度{group.strength:P2}；复制基础{source.skill.title}，原生{mirror.nativeSegments}段，每段{mirror.nativeCoefficient:P3}；基础内置穿透/暴击率加值按强度缩放；不复制任何词条、追击、属性加值或其他镜像。持续与段数结构不额外放大。");
                var mirrorContext=new CastContext { reacted=context.reacted,pushed=context.pushed };
                ExecuteCore(actor,mirror,target.id,mirrorContext);
            }
        }
    }
}
