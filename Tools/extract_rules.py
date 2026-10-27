"""Read-only XLSX extraction plus explicit, reviewable v1 combat mapping. No runtime Excel dependency."""
import json, re, pathlib
ROOT = pathlib.Path(__file__).resolve().parents[1]
source = json.loads((ROOT / 'Tools/RulesSnapshot.json').read_text(encoding='utf-8'))
def sheet(prefix): return next(x['rows'] for x in source if x['sheet'].startswith(prefix))
def cells(row): return {m[0]:m[1] for m in re.findall(r'([A-Z]+)\d+: (.*?)(?= \| [A-Z]+\d+: |$)',row)}
def lin(b=0,p=0): return dict(basis=b,perChain=p)
def eff(kind,b=0,p=0,attribute='Attack',duration=0,**kw):
 return dict(kind=kind,attribute=attribute,value=lin(b,p),duration=duration,**kw)
def skill(id,b=0,p=0,attribute='Attack',pattern='Single',**kw):
 return dict(id=id,title=id,attribute=attribute,pattern=pattern,coefficient=lin(b,p),segments=lin(1),effects=[],**kw)
S={}
def put(id,b=0,p=0,attribute='Attack',pattern='Single',effects=None,**kw):
 s=skill(id,b,p,attribute,pattern,**kw)
 if effects: s['effects']=effects
 S[id]=s
put('连射',.6,pattern='Multi'); S['连射']['segments']=lin(2,1)
put('破风箭',3,.1,penetration=lin(0,.06))
put('多重扫射',1,.1,pattern='Sweep')
put('疾风护盾',effects=[eff('Shield',2,.2)])
put('灵巧闪避',effects=[eff('Evade',p=.1,duration=1,percent=True)])
put('风之驱散',effects=[eff('Cleanse',p=1),eff('Heal',1,attribute='Speed')])
put('毒箭矢',.5,effects=[eff('Poison',p=1,onHit=True,stacking=True,negative=True)])
put('扰乱射击',1,effects=[eff('Push',p=.05,onHit=True,percent=True)])
put('鹰眼校准',effects=[eff('AttackUp',p=1,duration=2)])
put('劈山斩',4,.15)
put('横扫千军',1.35,.15,pattern='Sweep')
put('迎击架势',2,.15,pattern='Counter',duration=1)
put('架剑防御',effects=[eff('Shield',1.5,.2,attribute='Defense'),eff('Block',1,duration=1)])
put('重整旗鼓',effects=[eff('Heal',.2,.08,attribute='LostHp')])
put('破缚',effects=[eff('Cleanse',p=1),eff('Block',1,duration=1)])
put('磐石之躯',effects=[eff('DefensePercent',p=.1,duration=2,percent=True),eff('SpecialDefensePercent',p=.1,duration=2,percent=True)])
put('威慑',effects=[eff('Weakness',p=1,duration=2,stacking=True,negative=True)])
put('狂战之怒',effects=[eff('AttackUp',p=2,duration=2),eff('DefensePercent',-.3,duration=2,percent=True,negative=True),eff('SpecialDefensePercent',-.3,duration=2,percent=True,negative=True)])
put('魔力飞弹',3.9,.14,attribute='SpecialAttack')
put('魔力震荡',1.2,.12,attribute='SpecialAttack',pattern='Sweep',effects=[eff('Push',p=.01,onHit=True,percent=True)])
put('附魔杖击',2.4,.12,attribute='BothAttack')
put('魔力屏障',effects=[eff('Shield',2,.2,attribute='SpecialAttack')])
put('治愈术',effects=[eff('Heal',2.2,.25,attribute='SpecialDefense')])
put('净化术',effects=[eff('Cleanse',p=1),eff('DebuffImmune',1,duration=1)])
put('魔力灌注',effects=[eff('SpecialAttackUp',p=2,duration=2)])
put('轻羽术',effects=[eff('SpeedPercent',p=.1,duration=2,percent=True)])
put('贯心刺',4.3,critBonus=lin(0,.08))
put('连环突刺',1,pattern='Multi'); S['连环突刺']['segments']=lin(0,1)
put('挑空刺',1.65,.11,landingCoefficient=1.65,note='测试：第13页第二段165%；第08/18页有150%冲突。落点/碰撞额外目标未实现。')
put('旋矛御箭',effects=[eff('Block',p=2,duration=1,rangedOnly=True)])
put('调息',effects=[eff('Heal',1.5,.2,attribute='Speed')])
put('拒敌架势',effects=[eff('MeleeReduction',p=.1,duration=1,percent=True)])
put('裂伤刺',.5,effects=[eff('Bleed',p=1,onHit=True,stacking=True,negative=True,pending=True,note='待定：流血持续/衰减/位移伤害系数，当前仅记录层数，不造成流血伤害。')])
put('压阵',effects=[eff('Push',p=.03,percent=True)])
put('迅攻架势',effects=[eff('AttackUp',p=1,duration=2),eff('SpeedPercent',p=.03,duration=2,percent=True)])
put('盾击',3.75,.15,attribute='Defense')
put('回旋飞盾',1.5,attribute='Defense',pattern='Bounce',bounceCoefficient=1.05)
put('碎盾冲击',3.75,.15,shieldCost=.5,shieldResource=3)
put('坚盾',effects=[eff('Shield',2.5,.3,attribute='Defense')])
put('挺身守护',effects=[eff('Guard',p=.04,duration=1,percent=True)])
put('护盾治疗',effects=[eff('Heal',.2,.08,attribute='Shield')])
put('钢铁意志',effects=[eff('DefenseUp',p=2,duration=2),eff('SpecialDefenseUp',p=1,duration=2)])
put('棘甲壁垒',effects=[eff('Reflect',p=.1,duration=2)])
put('护盾强化',effects=[eff('ShieldBoost',p=1,duration=2,stacking=True)])
weapons=[]; cards=[]
targetAlly={'魔力屏障','治愈术','净化术','魔力灌注','轻羽术'}
enemyChange={'威慑','压阵'}
match={'攻势':'Offense','生存':'Survival','状态':'Status','通用':'Universal','攻势／生存':'OffenseSurvival','待定':'Pending'}
damage={'物理':'Physical','特殊':'Special','变化':'Change'}
reach={'近战':'Melee','远程':'Ranged','—':'None'}
for row in sheet('12'):
 c=cells(row)
 if c.get('A') in ['猎弓','大剑','法杖','长矛','盾牌'] and c.get('B','').isdigit():
  name=c['A']; weapons.append(dict(id=name,title=name,panel=dict(hp=int(c['C']),attack=float(c['D']),specialAttack=float(c['E']),defense=float(c['F']),specialDefense=float(c['G']),speed=float(c['H']),crit=float(c['I']),critDamage=float(c['J'])),cards=[],note='固有能力待定，首版不启用即时再动。'))
for row in sheet('13'):
 c=cells(row)
 if not c.get('B','').isdigit(): continue
 name=c['C']; s=S[name]; s.update(description=c['G'],damageType=damage[c['D']],reach=reach[c['E']])
 s['target']='OtherAlly' if name=='挺身守护' else 'Ally' if name in targetAlly else 'FrontRow' if name in ['横扫千军','压阵'] else 'AllEnemies' if s['pattern']=='Sweep' else 'Enemy' if s['coefficient']['basis'] or name in enemyChange else 'Self'
 if name=='迎击架势': s['target']='Self'
 card=dict(id=c['A']+'_'+c['B'],skill=name,match=match[c['F']],traits=[c['H']]); cards.append(card)
 next(w for w in weapons if w['id']==c['A'])['cards'].append(card['id'])
S['迅攻架势']['description']='自身物攻提高 总连锁数点；速度提高 总连锁数×3%，持续2回合。物攻为固定点数，速度为百分比增益。'
rates={'追击':.5,'锋锐':.1,'穿甲':.05,'壁障':1,'增幅':.05,'灵动':.6,'深化':1,'延续':.1,'强化':.08,'破势':.2,'顽强':.03,'破魔':.05}
rates.update({'专注': 0.05, '镜像': 0.2, '压制': 0.2, '终结': 0.2, '乘隙': 0.15, '裂隙': 0.1, '贯通': 0.05, '连锁收益': 1, '连锁蓄势': 0.5, '连锁协奏': 0.75, '同调': 0.5, '交织': 0.5})
# Column C is rarity/acquisition status. It must not mark a defined effect as rules-pending.
traits=[]
for row in sheet('15'):
 c=cells(row)
 if c.get('B') in match and c.get('E'): traits.append(dict(id=c['A'],title=c['A'],match=match[c['B']],rate=rates.get(c['A'],0),pending=False,description=c['E'],note=c.get('F','')))
# User-confirmed rule override; preserve RulesSnapshot.json as the historical workbook extraction.
next(t for t in traits if t['id']=='追击').update(rate=.5, description='追加 当前连锁数 个同类型直接攻击段，每段伤害系数固定为50%，读取主技能指定的计算属性。不受槽位伤害衰减影响。', note='用户最新确认：所有来源槽位的每段系数均为50%，不乘槽位保留率，也不逐段递减。当前连锁数修正只影响追加次数，来源槽位仅用于标识。计算属性、伤害类型与射程沿用1号位主技能；主技能内置穿透和暴击率加值作用于追击，并沿用现有增伤、穿透、暴击规则。不读取主技能单段或原生总系数，不复制壁障/灵动/顽强加值、碎盾等资源加值、附带状态、控制或再次生成追击。各段独立判定、逐段扣盾扣血，死亡后按合法目标转火；反击追击同样处理。无连锁修正时五张各带一条追击共15段，总系数750%。Rate为每段固定系数0.5。')
for trait in traits:
 if trait['id'] in ['压制','终结']: trait['threshold']=.7 if trait['id']=='压制' else .3
for trait in traits:
 if trait['id'] in {'镜像': '复制原主技能的基础伤害系数和原生段数，按镜像强度缩放，不复制词条、追击、词条加值或其他镜像。护盾消耗和非数值控制等特殊复制规则仍待定。 已接入基础伤害、治疗、护盾及明确的数值效果复制，不复制词条或词条加值。特殊资源、非数值控制、未确认的持续回合缩放、非整数层数/格挡次数取整及同名限时增益重复幅度，只限制相应镜像组合并显示原因，主技能照常执行。', '连锁收益': '先读取连锁反应等修正后的当前连锁数。只放大非收益放大、非连锁数修正词条；不互相递归增强。多条收益放大的增量相加：两条翻倍合计3倍，不是4倍。 当前实现：先合计本卡收益增量；只放大其他有效词条。追击只放大追加次数，每段固定50%不变。次数/层数按项目统一四舍五入；延续的额外回合仍按原规则汇总后向上取整。', '连锁蓄势': '只放大本卡其他非收益放大、非连锁数修正词条；多条收益放大的增量相加。适合1/3/5号位；连锁反应后按修正值判奇偶。 当前实现：先合计本卡收益增量；只放大其他有效词条。追击只放大追加次数，每段固定50%不变。次数/层数按项目统一四舍五入；延续的额外回合仍按原规则汇总后向上取整。', '连锁协奏': '偶数槽位较少，单次收益高于蓄势。只放大本卡其他非收益放大、非连锁数修正词条；增量相加，不互相放大。 当前实现：先合计本卡收益增量；只放大其他有效词条。追击只放大追加次数，每段固定50%不变。次数/层数按项目统一四舍五入；延续的额外回合仍按原规则汇总后向上取整。', '同调': '1号位不触发。只放大本卡其他非收益放大、非连锁数修正词条；不使不匹配的词条生效。多条收益放大增量相加。 当前实现：先合计本卡收益增量；只放大其他有效词条。追击只放大追加次数，每段固定50%不变。次数/层数按项目统一四舍五入；延续的额外回合仍按原规则汇总后向上取整。', '交织': '与同调采用相同收益，分别支持同类型及交替类型排列。1号位不触发；异类型排列不减少总连锁数。只放大本卡其他非收益放大、非连锁数修正词条；增量相加。 当前实现：先合计本卡收益增量；只放大其他有效词条。追击只放大追加次数，每段固定50%不变。次数/层数按项目统一四舍五入；延续的额外回合仍按原规则汇总后向上取整。'}: trait['note']={'镜像': '复制原主技能的基础伤害系数和原生段数，按镜像强度缩放，不复制词条、追击、词条加值或其他镜像。护盾消耗和非数值控制等特殊复制规则仍待定。 已接入基础伤害、治疗、护盾及明确的数值效果复制，不复制词条或词条加值。特殊资源、非数值控制、未确认的持续回合缩放、非整数层数/格挡次数取整及同名限时增益重复幅度，只限制相应镜像组合并显示原因，主技能照常执行。', '连锁收益': '先读取连锁反应等修正后的当前连锁数。只放大非收益放大、非连锁数修正词条；不互相递归增强。多条收益放大的增量相加：两条翻倍合计3倍，不是4倍。 当前实现：先合计本卡收益增量；只放大其他有效词条。追击只放大追加次数，每段固定50%不变。次数/层数按项目统一四舍五入；延续的额外回合仍按原规则汇总后向上取整。', '连锁蓄势': '只放大本卡其他非收益放大、非连锁数修正词条；多条收益放大的增量相加。适合1/3/5号位；连锁反应后按修正值判奇偶。 当前实现：先合计本卡收益增量；只放大其他有效词条。追击只放大追加次数，每段固定50%不变。次数/层数按项目统一四舍五入；延续的额外回合仍按原规则汇总后向上取整。', '连锁协奏': '偶数槽位较少，单次收益高于蓄势。只放大本卡其他非收益放大、非连锁数修正词条；增量相加，不互相放大。 当前实现：先合计本卡收益增量；只放大其他有效词条。追击只放大追加次数，每段固定50%不变。次数/层数按项目统一四舍五入；延续的额外回合仍按原规则汇总后向上取整。', '同调': '1号位不触发。只放大本卡其他非收益放大、非连锁数修正词条；不使不匹配的词条生效。多条收益放大增量相加。 当前实现：先合计本卡收益增量；只放大其他有效词条。追击只放大追加次数，每段固定50%不变。次数/层数按项目统一四舍五入；延续的额外回合仍按原规则汇总后向上取整。', '交织': '与同调采用相同收益，分别支持同类型及交替类型排列。1号位不触发；异类型排列不减少总连锁数。只放大本卡其他非收益放大、非连锁数修正词条；增量相加。 当前实现：先合计本卡收益增量；只放大其他有效词条。追击只放大追加次数，每段固定50%不变。次数/层数按项目统一四舍五入；延续的额外回合仍按原规则汇总后向上取整。'}[trait['id']]
traits.append(dict(id='待定',title='待定',match='Pending',rate=0,pending=True,description='拒敌架势原生词条未定稿。'))
for id,b,dt,rr,effects in [('弹跳撞击',1,'Physical','Melee',[]),('蓄热',0,'Change','None',[]),('熔岩喷吐',1.8,'Special','Ranged',[eff('Burn',1,duration=2,onHit=True,negative=True)]),('毒液喷射',.5,'Special','Ranged',[eff('Poison',2,onHit=True,stacking=True,negative=True)]),('虹光凝聚',0,'Change','None',[]),('虹光喷射',3,'Special','Ranged',[])]:
 put(id,b,attribute='SpecialAttack' if dt=='Special' else 'Attack',effects=effects,damageType=dt,reach=rr,target='Enemy' if b else 'Self',description=id+'（第20页固定系数，不读玩家连锁）')
monsters=[]
for row in sheet('20'):
 c=cells(row)
 if c.get('C') not in ['随从','Boss']: continue
 name=c['B']; phases=[]
 if name=='彩虹史莱姆': phases=[dict(title=t,skill=k,defense=d,specialDefense=s) for t,k,d,s in [('透明','弹跳撞击',15,15),('赤色','虹光凝聚',25,5),('蓝色','虹光喷射',5,25)]]
 else:
  for sk in {'普通史莱姆':['弹跳撞击'],'熔岩史莱姆':['蓄热','熔岩喷吐'],'毒素史莱姆':['毒液喷射']}[name]: phases.append(dict(title=sk,skill=sk,defense=float(c['G']),specialDefense=float(c['H'])))
 monsters.append(dict(id=name,title=name,panel=dict(hp=int(c['D']),attack=float(c['E']),specialAttack=float(c['F']),defense=float(c['G']),specialDefense=float(c['H']),speed=float(c['I']),crit=0,critDamage=1.5),phases=phases))
encounters=[dict(title=t,enemies=[dict(monster=m,backRow=b) for m,b in es]) for t,es in [('首次遭遇',[('普通史莱姆',False),('彩虹史莱姆',True)]),('单Boss',[('彩虹史莱姆',True)]),('熔岩测试',[('熔岩史莱姆',False)]),('毒素测试',[('毒素史莱姆',True)])]]
pending=['中毒每层2点、灼伤5点均为本轮测试参数。','流血仅记录层数：持续/衰减/位移伤害系数待定。','拒敌架势原生词条待定，不产生词条收益。','挑空刺第二段默认使用第13页165%测试值；与其他页150%冲突，可关闭第二段测试。碰撞落点/另一个敌人不模拟。','猎弓即时再动触发条件与其余武器固有能力待定，未启用。','同名固定属性增益、招架、反击姿态的重复幅度未定：有效期间拒绝重复施放，避免自行决定叠加。','致死反伤先后待定：默认测试倒下后仍返还已承受伤害，可在Rules中关闭。','治疗溢出待定：Demo仅恢复至最大生命，溢出不转换其他收益。','多种护盾池、完整扩展技能卡池与奇物后续接入；当前词条库已实现25条，镜像特殊边界按技能组合提示限制。']
out=dict(sourceVersion='2026-10-20 测试配置（规则书文件名2026-10-16）',weapons=weapons,cards=cards,skills=list(S.values()),traits=traits,monsters=monsters,encounters=encounters,pending=pending)
next(t for t in traits if t['id']=='延续')['note']='多条有效延续先加算收益；读取本卡经连锁反应修正后的当前连锁数，并沿用本卡收益放大。最终持续时间＝基础持续时间＋ceil（基础持续时间×延续收益总和），最后只向上取整一次。强化不延长持续时间。基础2回合：无延续2回合；当前连锁5的一条延续3回合；当前连锁10的一条延续4回合；当前连锁1～5的五条延续5回合。不延长无时限护盾、冻结，不增加格挡次数；已允许刷新的同名限时效果取较长剩余时间，不累计回合。重复幅度未定的组合仍按既有规则限制。'
enums={
 'match':'Offense Survival Status Universal OffenseSurvival Pending',
 'damageType':'Physical Special Change','reach':'None Melee Ranged',
 'target':'Enemy FrontRow AllEnemies Self Ally OtherAlly',
 'attribute':'Attack SpecialAttack Defense SpecialDefense Speed MaxHp LostHp Shield BothAttack',
 'pattern':'Single Multi Sweep Bounce Counter',
 'kind':'Shield Heal Cleanse Poison Burn Bleed Weakness AttackUp SpecialAttackUp DefenseUp SpecialDefenseUp SpeedUp DefensePercent SpecialDefensePercent SpeedPercent Evade CritUp DebuffImmune Block MeleeReduction Reflect ShieldBoost Push Guard Freeze'}
def encode(x):
 if isinstance(x,list): return [encode(v) for v in x]
 if isinstance(x,dict): return {k:enums[k].split().index(v) if k in enums and isinstance(v,str) else encode(v) for k,v in x.items()}
 return x
out=encode(out)
from chain_growth_rules import apply_confirmed_growth
apply_confirmed_growth(out)
(ROOT/'nine-card-roguelike/Assets/Data/InitialRules.json').write_text(json.dumps(out,ensure_ascii=False,indent=2),encoding='utf-8')
print('Generated',len(weapons),'weapons',len(cards),'cards',len(S),'skills',len(traits),'traits')
