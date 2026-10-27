"""Confirmed overrides after the historical workbook snapshot; no balance changes outside this list."""
import json, re
from pathlib import Path

REGULAR_BUFFS = ['鹰眼校准','磐石之躯','狂战之怒','魔力灌注','轻羽术','迅攻架势','钢铁意志','护盾强化',
                 '战意','护身之风','疾风步','坚岩加护','盾墙号令']
KINDS = 'Shield Heal Cleanse Poison Burn Bleed Weakness AttackUp SpecialAttackUp DefenseUp SpecialDefenseUp SpeedUp DefensePercent SpecialDefensePercent SpeedPercent Evade CritUp DebuffImmune Block MeleeReduction Reflect ShieldBoost Push Guard Freeze Advance'.split()
def effect(kind, per_chain=0, basis=0, percent=False):
    return dict(kind=KINDS.index(kind),attribute=0,value=dict(basis=basis,perChain=per_chain),duration=0,percent=percent)
def extension_buffs():
    entries = [
        ('战意',3,[effect('CritUp',.05,percent=True)],'自身暴击率提高 总连锁数×5%，持续总连锁数回合。'),
        ('护身之风',3,[effect('DefensePercent',.1,percent=True),effect('SpecialDefensePercent',.1,percent=True)],'自身物防与特防提高 总连锁数×10%，持续总连锁数回合。'),
        ('疾风步',3,[effect('SpeedPercent',.03,percent=True),effect('Advance',basis=.3,percent=True)],'自身速度提高 总连锁数×3%，持续总连锁数回合，并立即推进自身行动条30%。'),
        ('坚岩加护',4,[effect('DefenseUp',2),effect('SpecialDefenseUp',2)],'使一名己方单位的物防与特防提高 总连锁数×2，持续总连锁数回合。'),
        ('盾墙号令',6,[effect('DefenseUp',1),effect('SpecialDefenseUp',1)],'使己方全体的物防与特防各提高 总连锁数，持续总连锁数回合。')]
    return [dict(id=name,title=name,attribute=0,pattern=0,coefficient=dict(basis=0,perChain=0),segments=dict(basis=1,perChain=0),
                 damageType=2,reach=0,target=target,effects=effects,description=description,
                 note='扩展技能数据已接入；正式扩展卡获取/换卡流程尚未开放。重复施加沿用既有非叠加/限制规则。')
            for name,target,effects,description in entries]

def apply_confirmed_growth(data):
    for extension in extension_buffs():
        if not any(s['id']==extension['id'] for s in data['skills']): data['skills'].append(extension)
    for skill in data['skills']:
        if skill['id'] not in REGULAR_BUFFS: continue
        for e in skill['effects']:
            if e['kind']==KINDS.index('Advance'): continue
            e['duration']=0; e['durationPerChain']=1
        skill['description']=re.sub(r'持续\s*[12]回合', '持续总连锁数回合', skill['description'])
    spear=next(s for s in data['skills'] if s['id']=='迅攻架势')
    spear['description']='自身物攻提高 总连锁数点；速度提高 总连锁数×3%，两者均持续总连锁数回合。物攻为固定点数，速度为百分比增益。'
    continuation=next(t for t in data['traits'] if t['id']=='延续')
    continuation['note']='先由主技能实际总连锁计算基础时长D；每条延续读取本卡修正后当前连锁数，计入适用收益放大，全部加算。最终持续D＋ceil(D×累计延长比例)，只取整一次；强化不延长时间。D=5时：无延续5回合；当前连锁5的一条延续8回合；当前连锁10的一条延续10回合；五条当前连锁1～5的延续13回合。不增加格挡次数，不延长无时限护盾或冻结，不修改尚未接入的蓄力等待。已允许刷新的效果取较长剩余时间，不累加回合；重复幅度未定组合仍按原有限制。'

def quote(value): return json.dumps(value,ensure_ascii=False)
def scalar(value):
    if isinstance(value,bool): return '1' if value else '0'
    if isinstance(value,str): return quote(value)
    return str(value)
def dump_skill(s):
    lines=['  - icon: {fileID: 0}','    presentation: {fileID: 0}']
    for key,value in s.items():
        if key=='effects':
            lines.append('    effects:')
            for e in value:
                lines.append('    - kind: '+str(e['kind']))
                for k,v in e.items():
                    if k=='kind': continue
                    if isinstance(v,dict):
                        lines.append('      '+k+':')
                        lines += ['        '+a+': '+scalar(b) for a,b in v.items()]
                    else: lines.append('      '+k+': '+scalar(v))
        elif isinstance(value,dict):
            lines.append('    '+key+':'); lines += ['      '+a+': '+scalar(b) for a,b in value.items()]
        else: lines.append('    '+key+': '+scalar(value))
    return '\n'.join(lines)+'\n'
def migrate(root):
    path=root/'nine-card-roguelike/Assets/Data/InitialRules.json'
    data=json.loads(path.read_text(encoding='utf-8')); apply_confirmed_growth(data)
    path.write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    path=root/'nine-card-roguelike/Assets/Data/BattleCatalog.asset'; text=path.read_text(encoding='utf-8')
    start=text.index('  skills:\n'); end=text.index('  traits:\n',start)
    part=text[start:end]
    for skill in data['skills']:
        if skill['id'] not in REGULAR_BUFFS: continue
        escaped=''.join('\\u%04X'%ord(c) for c in skill['id'])
        blocks=re.findall(r'  - icon:.*?(?=\n  - icon:|\Z)',part,re.S)
        old=next((b for b in blocks if ('    id: "'+escaped+'"') in b or ('    id: '+quote(skill['id'])) in b),None)
        if old is None: part+=dump_skill(skill); continue
        new=old
        # Preserve authored resources and all unrelated numeric fields.
        new=re.sub(r'    description:.*?(?=\n    \w)',lambda m:'    description: '+quote(skill['description']),new,flags=re.S)
        chunks=re.split(r'(?=    - kind:)',new)
        for i in range(1,len(chunks)):
            if chunks[i].startswith('    - kind: '+str(KINDS.index('Advance'))+'\n'): continue
            chunks[i]=re.sub(r'      duration: \d+','      duration: 0',chunks[i],count=1)
            if '      durationPerChain:' in chunks[i]: chunks[i]=re.sub(r'      durationPerChain: \d+','      durationPerChain: 1',chunks[i],count=1)
            else: chunks[i]=chunks[i].replace('      duration: 0','      duration: 0\n      durationPerChain: 1',1)
        new=''.join(chunks); part=part.replace(old,new)
    text=text[:start]+part+text[end:]
    # The continuation note may span YAML lines. Limit replacement to its own trait block.
    start=text.index('  traits:\n'); escaped=''.join('\\u%04X'%ord(c) for c in '延续')
    for block in re.findall(r'  - icon:.*?(?=\n  - icon:|\n  monsters:|\Z)',text[start:],re.S):
        if '    id: "'+escaped+'"' not in block: continue
        note=next(t['note'] for t in data['traits'] if t['id']=='延续')
        updated=re.sub(r'    note:.*?(?=\n    \w|\Z)',lambda m:'    note: '+quote(note),block,flags=re.S)
        text=text.replace(block,updated)
    path.write_text(text,encoding='utf-8')

if __name__=='__main__': migrate(Path(__file__).resolve().parents[1])
