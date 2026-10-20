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
put('迅攻架势',effects=[eff('AttackUp',p=1,duration=2),eff('SpeedUp',p=1,duration=2)])
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
rates={'追击':.15,'锋锐':.1,'穿甲':.05,'壁障':1,'增幅':.05,'灵动':.6,'深化':1,'延续':.1,'强化':.08,'破势':.2,'顽强':.03,'破魔':.05}
traits=[]
for row in sheet('15'):
 c=cells(row)
 if c.get('B') in match and c.get('E'): traits.append(dict(id=c['A'],title=c['A'],match=match[c['B']],rate=rates.get(c['A'],0),pending=c.get('C')=='待配置',description=c['E'],note=c.get('F','')))
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
pending=['中毒每层2点、灼伤5点均为本轮测试参数。','流血仅记录层数：持续/衰减/位移伤害系数待定。','拒敌架势原生词条待定，不产生词条收益。','挑空刺第二段默认使用第13页165%测试值；与其他页150%冲突，可关闭第二段测试。碰撞落点/另一个敌人不模拟。','猎弓即时再动触发条件与其余武器固有能力待定，未启用。','同名固定属性增益、招架、反击姿态的重复幅度未定：有效期间拒绝重复施放，避免自行决定叠加。','致死反伤先后待定：默认测试倒下后仍返还已承受伤害，可在Rules中关闭。','治疗溢出待定：Demo仅恢复至最大生命，溢出不转换其他收益。','多种护盾池、镜像、扩展词条、强控卡与奇物不在本版卡池；数据结构保留接口。']
out=dict(sourceVersion='2026-10-20 测试配置（规则书文件名2026-10-16）',weapons=weapons,cards=cards,skills=list(S.values()),traits=traits,monsters=monsters,encounters=encounters,pending=pending)
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
(ROOT/'nine-card-roguelike/Assets/Data/InitialRules.json').write_text(json.dumps(out,ensure_ascii=False,indent=2),encoding='utf-8')
print('Generated',len(weapons),'weapons',len(cards),'cards',len(S),'skills',len(traits),'traits')
