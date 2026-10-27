"""Generate a complete, explicit coverage audit from source definitions and the current runtime snapshot."""
import csv, json, re
from pathlib import Path
from chain_growth_rules import REGULAR_BUFFS,KINDS
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'Validation/ChainGrowth'; OUT.mkdir(parents=True,exist_ok=True)
data=json.loads((ROOT/'nine-card-roguelike/Assets/Data/InitialRules.json').read_text(encoding='utf-8'))
snapshot=json.loads((ROOT/'Tools/RulesSnapshot.json').read_text(encoding='utf-8'))
skills={s['id']:s for s in data['skills']}
def cells(row): return {m[0]:m[1] for m in re.findall(r'([A-Z]+)\d+: (.*?)(?= \| [A-Z]+\d+: |$)',row)}
def formula(v): return f"{v.get('basis',0):g}+{v.get('perChain',0):g}N"
def duration(e): return f"{e.get('duration',0)}+{e.get('durationPerChain',0)}N"
def summary(s):
    fields=[]
    if s['coefficient']['basis'] or s['coefficient']['perChain']:
        fields += [f"属性{s['attribute']}，单段系数{formula(s['coefficient'])}，每目标段数{formula(s.get('segments',{'basis':1}))}"]
    for e in s.get('effects',[]): fields += [f"{KINDS[e['kind']]}={formula(e['value'])}，基础持续{duration(e)}回合"+('，命中附带' if e.get('onHit') else '')]
    if s.get('duration'): fields += ['姿态基础持续'+duration(s)+'回合']
    if s.get('penetration'): fields += ['内置穿透'+formula(s['penetration'])]
    if s.get('critBonus'): fields += ['内置暴击率'+formula(s['critBonus'])]
    if s.get('bounceCoefficient'): fields += [f"弹射每段{s['bounceCoefficient']:g}"]
    if s.get('landingCoefficient'): fields += [f"落地独立第二段{s['landingCoefficient']:g}（测试开关控制，不补偿到首段）"]
    if s.get('shieldCost'): fields += [f"一次耗盾{s['shieldCost']:.0%}，已耗资源×{s['shieldResource']:g}仅原生"]
    return '；'.join(fields)
rows=[]
for prefix,scope in [('13','预组'),('14','扩展')]:
    sheet=next(s for s in snapshot if s['sheet'].startswith(prefix))
    for raw in sheet['rows']:
        c=cells(raw)
        name=c.get('C') if scope=='预组' else c.get('B')
        if scope=='预组' and (not c.get('B','').isdigit()): continue
        if scope=='扩展' and (not c.get('A') or c.get('C') not in ['物理','特殊','变化']): continue
        desc=c.get('G','') if scope=='预组' else c.get('F','')
        note=c.get('K','') if scope=='预组' else c.get('G','')
        s=skills.get(name)
        if s is None:
            category='仍有未定义边界'; coverage='规则已定义、运行时未接入'
            finding='尚无运行技能、预览或结算；不能用空技能/固定值伪装已实现。'+('；原文另有交互待定' if '待' in note else '')
            if name=='雷霆附锋': finding+='；附魔2回合与所施感电N回合为两个计时字段，均未接入，不能混成一个持续时间'
            if name in ['蓄力斩','蓄力狙击']: finding+='；等待1回合不接延续，快照/中断/目标失效边界待定'
        elif name in REGULAR_BUFFS:
            category='本次修改'; coverage='结算/预览/日志已接入'+('；扩展获取换卡流程未接入' if scope=='扩展' else '')
            finding='强度保留；基础D=N；再汇总所有有效延续，D+ceil(D×Σ延续)。强化不改时长；实际N不受连锁反应改变。'
            if name=='狂战之怒': finding+='；物攻与双防代价均D=N'
            if name=='疾风步': finding+='；速度3%N，自推条30%独立增幅/上限'
        elif name in ['裂伤刺','挺身守护','挑空刺','拒敌架势']:
            category='仍有未定义边界'; coverage='已接入主体，存在明确限制'
            finding={'裂伤刺':'直接伤害已接入；流血只记层，系数/衰减待定',
                     '挺身守护':'单角色无另一己方目标时拒绝施放；多人代承伤接口未接入',
                     '挑空刺':'首段与165%落地段均真实结算；落地仅首段命中且目标存活时测试，不模拟碰撞另一敌人，碰撞/死亡目标交互待定',
                     '拒敌架势':'近战减伤已接入，原生待定词条仍占位，不构造收益'}[name]
        else:
            category='原实现正确'; coverage='已接入'
            finding='逐字段保留现有强度/段数/持续公式；实际总连锁与词条当前连锁分离，使用共用正式计算与预览。'
            if name in ['威慑','磐石之躯','钢铁意志']: finding+='；共享技能只注册一次'
        rows.append(dict(范围=scope,武器=c.get('A',''),技能=name,分类=category,实际接入=coverage,
                         当前公式=summary(s) if s else '未注册，历史定义：'+desc,
                         当前说明=s['description'] if s else desc,历史定义=desc,核查结论=finding,原交互备注=note))
for s in data['skills']:
    if not any(r['技能']==s['id'] for r in rows): rows.append(dict(范围='敌人',武器='敌人',技能=s['id'],分类='原实现正确',实际接入='已接入',当前公式=summary(s),当前说明=s['description'],历史定义=s['description'],核查结论='敌人不使用玩家排谱，总连锁0；保留固定系数、原有相位和状态时间。',原交互备注=s.get('note','')))
with (OUT/'逐技能连锁成长核查.csv').open('w',encoding='utf-8-sig',newline='') as f:
    writer=csv.DictWriter(f,fieldnames=rows[0].keys()); writer.writeheader(); writer.writerows(rows)
def clean(s): return str(s).replace('|','／').replace('\n',' ')
lines=['# 全技能连锁成长核查表','','N=主技能本次实际总连锁数（1～5）；X=所属卡经连锁反应修正后的当前连锁数。两者不混用。此表根据项目历史快照及当前运行数据生成，最新用户确认的规则以 Tools/chain_growth_rules.py 与当前配置为准。','','“仍有未定义边界”中的未接入行明确区分实现缺口与规则不明确：已有文字定义但未接入的技能没有被标为已实现。表中的逐字段公式用于核查数据，运行校验范围及边界见本目录交付说明。','','|范围/武器|技能|分类|实际接入|当前公式|核查结论|','|---|---|---|---|---|---|']
for r in rows: lines.append('|'+ '|'.join(clean(v) for v in [r['范围']+'/'+r['武器'],r['技能'],r['分类'],r['实际接入'],r['当前公式'],r['核查结论']])+'|')
lines+=['','## 全词条核查','','追击每段固定50%，无槽位伤害衰减；追加次数读取X并按已确认适用收益倍率计算。','', '|词条|匹配类型|当前系数|实际状态与核查说明|','|---|---|---:|---|']
for t in data['traits']:
    explanation=t.get('description','')+' '+t.get('note','')
    lines.append('|'+ '|'.join(clean(v) for v in [t['id'],t['match'],t['rate'],'规则待定、保留占位' if t.get('pending') else explanation])+'|')
(OUT/'逐技能连锁成长核查.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
print('Audit rows:',len(rows),'runtime skills:',len(skills),'unique expansion definitions:',len({r['技能'] for r in rows if r['范围']=='扩展'}),'unregistered expansion:',len({r['技能'] for r in rows if r['范围']=='扩展' and r['实际接入'].startswith('规则已定义')}))
