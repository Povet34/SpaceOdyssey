#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""SpaceOdyssey 퀘스트 검사 도구와 지도 서버.

    python questmap.py check            자료를 검사하고 문제를 출력한다
    python questmap.py serve            지도 화면을 연다 (http://127.0.0.1:8765)
    python questmap.py format           자료 파일을 표준 모양으로 다시 쓴다

파이썬 기본 기능만 쓴다. 형식 설명은 Docs/QuestSystemDesign.md에 있다.
"""
import argparse
import json
import math
import os
import re
import sys
import threading
import webbrowser
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_ROOT = os.path.normpath(os.path.join(HERE, '..', '..', 'Content'))

DRAWS = ('opener', 'followup')
PHASES = ('wake', 'awake', 'sleep')
TONES = ('worse', 'fade', 'better', 'mixed')
STATE_TYPES = ('flag', 'int', 'number', 'enum')
COMMANDS = ('테크얻음', '테크잃음', '유물얻음', '일지', '엔딩')
EVENT_KEYS = ('id', 'thread', 'title', 'beat', 'draw', 'phase', 'place', 'targets', 'when', 'weight',
              'weightMods', 'load', 'after', 'repeat', 'note', 'signs', 'responses', 'ignore', 'override')
OUTCOME_KEYS = ('label', 'needs', 'cost', 'tone', 'text', 'effects', 'rolls', 'ends')
STATE_KEYS = ('id', 'name', 'type', 'min', 'max', 'values', 'default', 'owner', 'roll', 'keep', 'desc')
THREAD_KEYS = ('id', 'name', 'color', 'source', 'note')
PALETTE = ['#e8b84a', '#5db8e8', '#9aa7b8', '#f08a4b', '#6fd38a', '#b58be8', '#e86fa5', '#4fd0c8',
           '#d3d36f', '#e87f6f', '#7f9be8', '#8fd35d', '#d38fd3', '#5dd3b0', '#c9a27a', '#a0a0e8']

# ---------------------------------------------------------------- 글 문법

UNITS = {'만': 1e4, '억': 1e8, '조': 1e12}
NUM_RE = re.compile(r'^(-?\d+(?:\.\d+)?(?:[eE]\d+)?)(만|억|조)?$')
IDENT = r'[^\W\d]\w*(?:\.\w+)*'
IDENT_RE = re.compile('^' + IDENT + '$')
OPS = ('==', '!=', '>=', '<=', '>', '<')
CMP_RE = re.compile(r'^(' + IDENT + r')\s*(==|!=|>=|<=|>|<)\s*(\S+)$')
TAG_RE = re.compile(r'^꼬리표\((.+?)\)\s*(==|!=|>=|<=|>|<)\s*(\S+)$')
TECH_RE = re.compile(r'^(!?)\s*테크\((.+?)\)$')
FLAG_RE = re.compile(r'^(!?)\s*(' + IDENT + r')$')
EFFECT_RE = re.compile(r'^(' + IDENT + r')\s*(\+=|-=|=)\s*(\S+)$')
CMD_RE = re.compile(r'^(\w+)\((.*)\)$')
BOOLS = {'true': True, '참': True, 'false': False, '거짓': False}


def parse_value(tok):
    m = NUM_RE.match(tok)
    if m:
        v = float(m.group(1)) * UNITS.get(m.group(2), 1)
        return ('num', int(v) if v == int(v) else v)
    if tok in BOOLS:
        return ('bool', BOOLS[tok])
    if IDENT_RE.match(tok):
        return ('ident', tok)
    raise ValueError("값 '%s'을 읽을 수 없다" % tok)


def parse_clause(text):
    """조건 한 줄을 읽는다. '또는'으로 나눈 것 가운데 하나만 맞으면 된다."""
    if not isinstance(text, str) or not text.strip():
        raise ValueError('빈 조건')
    atoms = []
    for part in re.split(r'\s+또는\s+', text.strip()):
        part = part.strip()
        m = TECH_RE.match(part)
        if m:
            atoms.append({'k': 'tech', 'neg': bool(m.group(1)), 'id': m.group(2).strip()})
            continue
        m = TAG_RE.match(part)
        if m:
            atoms.append({'k': 'tag', 'tag': m.group(1).strip(), 'op': m.group(2), 'val': parse_value(m.group(3))})
            continue
        m = CMP_RE.match(part)
        if m:
            atoms.append({'k': 'cmp', 'state': m.group(1), 'op': m.group(2), 'val': parse_value(m.group(3))})
            continue
        m = FLAG_RE.match(part)
        if m:
            atoms.append({'k': 'flag', 'neg': bool(m.group(1)), 'state': m.group(2)})
            continue
        raise ValueError("조건 '%s'을 읽을 수 없다" % part)
    return atoms


def parse_effect(text):
    if not isinstance(text, str) or not text.strip():
        raise ValueError('빈 결과')
    text = text.strip()
    m = EFFECT_RE.match(text)
    if m:
        kind = {'=': 'set', '+=': 'add', '-=': 'sub'}[m.group(2)]
        return {'k': kind, 'state': m.group(1), 'val': parse_value(m.group(3))}
    m = CMD_RE.match(text)
    if m:
        return {'k': 'cmd', 'name': m.group(1), 'arg': m.group(2).strip()}
    raise ValueError("결과 '%s'을 읽을 수 없다" % text)


def compare(a, op, b):
    try:
        if op == '==':
            return a == b
        if op == '!=':
            return a != b
        if op == '>=':
            return a >= b
        if op == '<=':
            return a <= b
        if op == '>':
            return a > b
        if op == '<':
            return a < b
    except TypeError:
        return False
    return False


# ---------------------------------------------------------------- 읽기

def read_json(path):
    with open(path, encoding='utf-8-sig') as f:
        return json.load(f)


def _inline(obj):
    return json.dumps(obj, ensure_ascii=False, separators=(', ', ': '))


def dumps(obj, level=0, width=110):
    """사람이 읽기 좋은 모양으로 쓴다. 짧은 목록과 묶음은 한 줄에 둔다."""
    pad = '  ' * level
    if isinstance(obj, dict):
        if not obj:
            return '{}'
        flat = all(not isinstance(v, (dict, list)) or (isinstance(v, list) and all(not isinstance(x, (dict, list)) for x in v))
                   for v in obj.values())
        if flat and level > 0:
            body = ', '.join('%s: %s' % (json.dumps(k, ensure_ascii=False), _inline(v)) for k, v in obj.items())
            one = '{ ' + body + ' }'
            if len(one) + len(pad) <= width + 60 and ('id' in obj or len(one) <= 70):
                return one
        items = ['%s  %s: %s' % (pad, json.dumps(k, ensure_ascii=False), dumps(v, level + 1, width)) for k, v in obj.items()]
        return '{\n' + ',\n'.join(items) + '\n' + pad + '}'
    if isinstance(obj, list):
        if not obj:
            return '[]'
        if all(not isinstance(x, (dict, list)) for x in obj):
            one = _inline(obj)
            if len(one) + len(pad) <= width:
                return one
        items = ['%s  %s' % (pad, dumps(v, level + 1, width)) for v in obj]
        return '[\n' + ',\n'.join(items) + '\n' + pad + ']'
    return json.dumps(obj, ensure_ascii=False)


def write_json(path, obj):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    tmp = path + '.tmp'
    with open(tmp, 'w', encoding='utf-8', newline='\n') as f:
        f.write(dumps(obj) + '\n')
    os.replace(tmp, path)


def clean(obj):
    """도구가 붙인 밑줄 항목을 떼어 낸다."""
    return {k: v for k, v in obj.items() if not k.startswith('_')}


def ordered(obj, keys):
    out = {k: obj[k] for k in keys if k in obj}
    out.update({k: v for k, v in obj.items() if k not in out})
    return out


class Content:
    def __init__(self, root):
        self.root = root
        self.packs = []
        self.threads = {}
        self.states = {}
        self.techs = {}
        self.events = {}
        self.directors = []
        self.problems = []

    def problem(self, level, code, kind, target, msg, file=None):
        self.problems.append({'level': level, 'code': code, 'kind': kind, 'target': target, 'msg': msg, 'file': file})


def pack_dirs(root):
    dirs = []
    base = os.path.join(root, 'base')
    if os.path.isdir(base):
        dirs.append(base)
    mods = os.path.join(root, 'mods')
    if os.path.isdir(mods):
        for name in sorted(os.listdir(mods)):
            d = os.path.join(mods, name)
            if os.path.isfile(os.path.join(d, 'pack.json')):
                dirs.append(d)
    return dirs


def load(root):
    c = Content(root)
    if not pack_dirs(root):
        c.problem('error', 'no-pack', 'file', root, "자료 폴더에 base 묶음이 없다")
    for d in pack_dirs(root):
        rel = lambda p: os.path.relpath(p, root).replace('\\', '/')

        def get(path, key):
            if not os.path.isfile(path):
                return []
            try:
                data = read_json(path)
            except (ValueError, OSError) as e:
                c.problem('error', 'json', 'file', rel(path), '파일을 읽을 수 없다: %s' % e, rel(path))
                return []
            items = data.get(key) if isinstance(data, dict) else None
            if not isinstance(items, list):
                c.problem('error', 'json', 'file', rel(path), "맨 위에 \"%s\" 목록이 있어야 한다" % key, rel(path))
                return []
            return [x for x in items if isinstance(x, dict)]

        pack = {'id': os.path.basename(d), 'name': os.path.basename(d)}
        pj = os.path.join(d, 'pack.json')
        if os.path.isfile(pj):
            try:
                pack.update(read_json(pj))
            except (ValueError, OSError) as e:
                c.problem('error', 'json', 'file', rel(pj), '파일을 읽을 수 없다: %s' % e, rel(pj))
        pack['_dir'] = rel(d)
        c.packs.append(pack)
        pid = pack['id']

        def add(table, kind, item, path):
            iid = item.get('id')
            if not isinstance(iid, str) or not iid:
                c.problem('error', 'no-id', 'file', rel(path), '%s에 id가 없다' % kind, rel(path))
                return
            if iid in table and not item.get('override'):
                c.problem('error', 'dup-id', kind, iid,
                          "id '%s'가 두 번 나온다 (%s, %s). 일부러 덮어쓰려면 \"override\": true를 넣는다"
                          % (iid, table[iid]['_file'], rel(path)), rel(path))
                return
            item['_pack'] = pid
            item['_file'] = rel(path)
            table[iid] = item

        for t in get(os.path.join(d, 'threads.json'), 'threads'):
            add(c.threads, 'thread', t, os.path.join(d, 'threads.json'))
        for s in get(os.path.join(d, 'states.json'), 'states'):
            add(c.states, 'state', s, os.path.join(d, 'states.json'))
        for t in get(os.path.join(d, 'techs.json'), 'techs'):
            add(c.techs, 'tech', t, os.path.join(d, 'techs.json'))
        for x in get(os.path.join(d, 'directors.json'), 'directors'):
            x['_pack'] = pid
            c.directors.append(x)
        evdir = os.path.join(d, 'events')
        if os.path.isdir(evdir):
            for dirpath, _, files in os.walk(evdir):
                for name in sorted(files):
                    if name.endswith('.json'):
                        p = os.path.join(dirpath, name)
                        for e in get(p, 'events'):
                            add(c.events, 'event', e, p)
        for eid in pack.get('disable') or []:
            if eid in c.events:
                del c.events[eid]
            else:
                c.problem('warn', 'disable-missing', 'file', rel(pj), "끄려는 사건 '%s'가 없다" % eid, rel(pj))
    for i, t in enumerate(c.threads.values()):
        t.setdefault('color', PALETTE[i % len(PALETTE)])
    return c


# ---------------------------------------------------------------- 검사

def outcomes(ev):
    """사건의 모든 결과를 (종류, 번호, 내용) 으로 돌려준다."""
    out = []
    for i, r in enumerate(ev.get('responses') or []):
        if isinstance(r, dict):
            out.append(('response', i, r))
    ig = ev.get('ignore')
    if isinstance(ig, dict):
        out.append(('ignore', 0, ig))
    return out


def outcome_effects(o):
    effs = list(o.get('effects') or [])
    for roll in o.get('rolls') or []:
        if isinstance(roll, dict):
            effs.extend(roll.get('effects') or [])
    return effs


def is_numeric(st):
    return st is not None and st.get('type') in ('int', 'number')


def validate(c):
    P = c.problem
    S = c.states
    parsed = {}      # 사건 id -> {'when': [[atom]], 'out': [(kind, idx, o, needs, effects)]}
    reads, writes = {}, {}

    # 상태값 선언
    for sid, st in S.items():
        for k in st:
            if not k.startswith('_') and k not in STATE_KEYS:
                P('warn', 'unknown-key', 'state', sid, "모르는 항목 '%s'" % k)
        if not re.match(r'^[^\W\d]\w*\.\w+$', sid):
            P('warn', 'state-name', 'state', sid, "이름은 '영역.이름' 꼴이어야 한다")
        t = st.get('type')
        if t not in STATE_TYPES:
            P('error', 'state-type', 'state', sid, "type은 %s 가운데 하나여야 한다" % ', '.join(STATE_TYPES))
            continue
        d = st.get('default')
        if t == 'enum':
            vals = st.get('values')
            if not isinstance(vals, list) or not vals:
                P('error', 'state-values', 'state', sid, 'enum은 values 목록이 있어야 한다')
            elif d not in vals:
                P('error', 'state-default', 'state', sid, "기본값 '%s'이 values에 없다" % d)
        elif t == 'flag':
            if not isinstance(d, bool):
                P('error', 'state-default', 'state', sid, 'flag의 기본값은 true나 false여야 한다')
        else:
            if not isinstance(d, (int, float)) or isinstance(d, bool):
                P('error', 'state-default', 'state', sid, '기본값이 숫자가 아니다')
            elif t == 'int' and ('min' not in st or 'max' not in st):
                P('warn', 'state-range', 'state', sid, 'int는 min과 max를 적어야 한다')
            elif ('min' in st and d < st['min']) or ('max' in st and d > st['max']):
                P('error', 'state-default', 'state', sid, '기본값이 범위를 벗어난다')

    def check_atom(eid, a, where):
        if a['k'] == 'tech':
            if a['id'] not in c.techs:
                P('error', 'unknown-tech', 'event', eid, "%s: 테크 '%s'가 techs.json에 없다" % (where, a['id']))
            return
        if a['k'] == 'tag':
            if not any(a['tag'] in (t.get('tags') or []) for t in c.techs.values()):
                P('warn', 'unknown-tag', 'event', eid, "%s: 꼬리표 '%s'가 붙은 테크가 없다" % (where, a['tag']))
            if a['val'][0] != 'num':
                P('error', 'type', 'event', eid, "%s: 꼬리표 개수는 숫자와 견준다" % where)
            return
        st = S.get(a['state'])
        if st is None:
            P('error', 'unknown-state', 'event', eid, "%s: 상태값 '%s'가 선언되지 않았다" % (where, a['state']))
            return
        reads.setdefault(a['state'], set()).add(eid)
        t = st.get('type')
        if a['k'] == 'flag':
            if t != 'flag':
                P('error', 'type', 'event', eid, "%s: '%s'는 flag가 아니라서 비교가 있어야 한다" % (where, a['state']))
            return
        vk, vv = a['val']
        if t == 'flag':
            if vk != 'bool' or a['op'] not in ('==', '!='):
                P('error', 'type', 'event', eid, "%s: flag '%s'는 true나 false와만 견준다" % (where, a['state']))
        elif t == 'enum':
            if a['op'] not in ('==', '!='):
                P('error', 'type', 'event', eid, "%s: '%s'는 ==나 !=로만 견준다" % (where, a['state']))
            elif vk != 'ident' or vv not in (st.get('values') or []):
                P('error', 'enum-value', 'event', eid, "%s: '%s'에 '%s'라는 값이 없다" % (where, a['state'], vv))
        elif t in ('int', 'number'):
            if vk == 'ident':
                if not is_numeric(S.get(vv)):
                    P('error', 'type', 'event', eid, "%s: '%s'는 숫자 상태값이 아니다" % (where, vv))
                else:
                    reads.setdefault(vv, set()).add(eid)
            elif vk != 'num':
                P('error', 'type', 'event', eid, "%s: '%s'는 숫자와 견준다" % (where, a['state']))

    def check_effect(eid, e, where):
        if e['k'] == 'cmd':
            if e['name'] not in COMMANDS:
                P('error', 'unknown-command', 'event', eid, "%s: 모르는 명령 '%s'. 쓸 수 있는 것: %s"
                  % (where, e['name'], ', '.join(COMMANDS)))
            elif e['name'].startswith('테크') and e['arg'] not in c.techs:
                P('error', 'unknown-tech', 'event', eid, "%s: 테크 '%s'가 techs.json에 없다" % (where, e['arg']))
            return
        st = S.get(e['state'])
        if st is None:
            P('error', 'unknown-state', 'event', eid, "%s: 상태값 '%s'가 선언되지 않았다" % (where, e['state']))
            return
        writes.setdefault(e['state'], set()).add(eid)
        t = st.get('type')
        vk, vv = e['val']
        if e['k'] in ('add', 'sub') and t not in ('int', 'number'):
            P('error', 'type', 'event', eid, "%s: '%s'는 숫자가 아니라서 더하거나 뺄 수 없다" % (where, e['state']))
            return
        if t == 'flag':
            if vk != 'bool':
                P('error', 'type', 'event', eid, "%s: flag '%s'에는 true나 false만 넣는다" % (where, e['state']))
        elif t == 'enum':
            if vk != 'ident' or vv not in (st.get('values') or []):
                P('error', 'enum-value', 'event', eid, "%s: '%s'에 '%s'라는 값이 없다" % (where, e['state'], vv))
        else:
            if vk == 'ident':
                if not is_numeric(S.get(vv)):
                    P('error', 'type', 'event', eid, "%s: '%s'는 숫자 상태값이 아니다" % (where, vv))
                else:
                    reads.setdefault(vv, set()).add(eid)
            elif vk != 'num':
                P('error', 'type', 'event', eid, "%s: '%s'에는 숫자를 넣는다" % (where, e['state']))
            elif e['k'] == 'set' and (('min' in st and vv < st['min']) or ('max' in st and vv > st['max'])):
                P('error', 'range', 'event', eid, "%s: %s는 '%s'의 범위를 벗어난다" % (where, vv, e['state']))

    def parse_list(eid, items, fn, where):
        out = []
        if items is None:
            return out
        if not isinstance(items, list):
            P('error', 'shape', 'event', eid, '%s: 목록이어야 한다' % where)
            return out
        for text in items:
            try:
                out.append(fn(text))
            except ValueError as e:
                P('error', 'syntax', 'event', eid, '%s: %s' % (where, e))
        return out

    # 사건 한 장씩
    for eid, ev in c.events.items():
        for k in ev:
            if not k.startswith('_') and k not in EVENT_KEYS:
                P('warn', 'unknown-key', 'event', eid, "모르는 항목 '%s'" % k)
        if ev.get('thread') not in c.threads:
            P('error', 'unknown-thread', 'event', eid, "줄기 '%s'가 threads.json에 없다" % ev.get('thread'))
        if not ev.get('title'):
            P('error', 'no-title', 'event', eid, '제목이 없다')
        draw = ev.get('draw')
        if draw not in DRAWS:
            P('error', 'draw', 'event', eid, "draw는 opener(새로 뽑힘)나 followup(이어짐)이어야 한다")
        if ev.get('phase') not in PHASES:
            P('error', 'phase', 'event', eid, "phase는 wake(동면에서 깨면 이미 벌어져 있다), awake(깨어서 지내는 사이에 생긴다), sleep(동면 중에 생겨서 깨운다) 가운데 하나여야 한다")
        when = parse_list(eid, ev.get('when'), parse_clause, '조건')
        for clause in when:
            for a in clause:
                check_atom(eid, a, '조건')
        if draw == 'opener' and not (isinstance(ev.get('weight'), (int, float)) and ev['weight'] > 0):
            P('error', 'weight', 'event', eid, '새로 뽑히는 사건은 0보다 큰 weight가 있어야 한다')
        if draw == 'followup' and not when:
            P('error', 'no-when', 'event', eid, '이어지는 사건인데 조건이 없다. 무엇 뒤에 오는지 알 수 없다')
        signs = ev.get('signs')
        if not isinstance(signs, dict) or not (signs.get('see') or signs.get('hear')):
            P('error', 'no-sign', 'event', eid, '징후가 없다. 보이는 것(see)이나 들리는 것(hear)이 하나는 있어야 한다')
        for key, shape in (('after', ('wakes', 'years')), ('repeat', ('cooldownWakes',))):
            v = ev.get(key)
            if v is not None and (not isinstance(v, dict) or any(k not in shape for k in v)):
                P('error', 'shape', 'event', eid, "%s는 %s만 가질 수 있다" % (key, ', '.join(shape)))
        for i, mod in enumerate(ev.get('weightMods') or []):
            if not isinstance(mod, dict) or 'if' not in mod or 'mul' not in mod:
                P('error', 'shape', 'event', eid, 'weightMods는 {"if": 조건, "mul": 배수} 꼴이어야 한다')
                continue
            for clause in parse_list(eid, [mod['if']], parse_clause, '가중치 조건'):
                for a in clause:
                    check_atom(eid, a, '가중치 조건')
        resp = ev.get('responses')
        if not isinstance(resp, list):
            P('error', 'shape', 'event', eid, 'responses 목록이 없다. 대응이 없으면 빈 목록 []을 둔다')
        if not isinstance(ev.get('ignore'), dict):
            P('error', 'no-ignore', 'event', eid, '방치 결과(ignore)가 없다. 놓치고 자면 어떻게 되는지 적어야 한다')
        outs = []
        for kind, i, o in outcomes(ev):
            where = '방치' if kind == 'ignore' else "대응 '%s'" % (o.get('label') or i + 1)
            for k in o:
                if k not in OUTCOME_KEYS:
                    P('warn', 'unknown-key', 'event', eid, "%s: 모르는 항목 '%s'" % (where, k))
            if kind == 'response' and not o.get('label'):
                P('error', 'no-label', 'event', eid, '%d번째 대응에 이름(label)이 없다' % (i + 1))
            if kind == 'ignore' and o.get('tone') not in TONES:
                P('error', 'tone', 'event', eid, "방치의 tone은 worse(나빠짐), fade(잦아듦), better(좋아짐), mixed(섞임) 가운데 하나여야 한다")
            if not o.get('text'):
                P('warn', 'no-text', 'event', eid, '%s: 무슨 일이 일어나는지 글(text)이 없다' % where)
            needs = parse_list(eid, o.get('needs'), parse_clause, where + '의 요구')
            for clause in needs:
                for a in clause:
                    check_atom(eid, a, where + '의 요구')
            effs = parse_list(eid, outcome_effects(o), parse_effect, where + '의 결과')
            for e in effs:
                check_effect(eid, e, where + '의 결과')
            if o.get('ends') and not effs and not (kind == 'ignore' and o.get('tone') == 'fade'):
                P('warn', 'ends-empty', 'event', eid, '%s: 줄기가 끝나는데 남는 것이 없다' % where)
            outs.append((kind, i, o, needs, effs))
        parsed[eid] = {'when': when, 'out': outs}

    # 도달할 수 있는가: 기본값에서 시작해 일어날 수 있는 사건의 결과를 쌓아 간다
    poss = {sid: {st.get('default')} for sid, st in S.items()}
    open_ = {sid: st.get('owner') == 'game' for sid, st in S.items()}

    def sat(a):
        if a['k'] in ('tech', 'tag'):
            return True
        st = S.get(a['state'])
        if st is None:
            return True
        if a['k'] == 'flag':
            return open_[a['state']] or ((not a['neg']) in poss[a['state']])
        vk, vv = a['val']
        if vk == 'ident' and st.get('type') in ('int', 'number'):
            return True
        if open_[a['state']]:
            if st.get('type') in ('int', 'number') and vk == 'num':
                lo, hi = st.get('min', -math.inf), st.get('max', math.inf)
                return {'>=': hi >= vv, '>': hi > vv, '<=': lo <= vv, '<': lo < vv,
                        '==': lo <= vv <= hi, '!=': True}[a['op']]
            return True
        return any(compare(v, a['op'], vv) for v in poss[a['state']])

    def all_sat(clauses):
        return all(any(sat(a) for a in clause) for clause in clauses)

    reached, applied = set(), set()
    changed = True
    while changed:
        changed = False
        for eid, p in parsed.items():
            if eid not in reached:
                if not all_sat(p['when']):
                    continue
                reached.add(eid)
                changed = True
            for kind, i, o, needs, effs in p['out']:
                if (eid, kind, i) in applied or not all_sat(needs):
                    continue
                applied.add((eid, kind, i))
                changed = True
                for e in effs:
                    if e['k'] == 'cmd' or e['state'] not in S:
                        continue
                    if e['k'] == 'set' and e['val'][0] != 'ident' or (e['k'] == 'set' and S[e['state']].get('type') == 'enum'):
                        poss[e['state']].add(e['val'][1])
                    else:
                        open_[e['state']] = True

    for eid, p in parsed.items():
        if eid not in reached:
            bad = next((cl for cl in p['when'] if not any(sat(a) for a in cl)), None)
            text = ''
            if bad is not None:
                idx = p['when'].index(bad)
                text = (c.events[eid].get('when') or [''])[idx] if idx < len(c.events[eid].get('when') or []) else ''
            P('warn', 'unreachable', 'event', eid, "도달할 수 없다. 조건 '%s'을 맞춰 주는 결과가 어디에도 없다" % text)
            continue
        for kind, i, o, needs, effs in p['out']:
            if (eid, kind, i) not in applied:
                P('warn', 'dead-response', 'event', eid, "대응 '%s'의 요구를 맞출 방법이 없다" % o.get('label'))

    # 연결
    graph = build_graph(c, parsed)
    fed = set((e['from'], e['via'], e['idx']) for e in graph['raw'])
    per_thread = {}
    for ev in c.events.values():
        per_thread[ev.get('thread')] = per_thread.get(ev.get('thread'), 0) + 1
    for eid, p in parsed.items():
        ev = c.events[eid]
        if per_thread.get(ev.get('thread'), 0) < 2 or ev.get('repeat'):
            continue
        for kind, i, o, needs, effs in p['out']:
            if not o.get('ends') and (eid, kind, i) not in fed:
                where = '방치' if kind == 'ignore' else "대응 '%s'" % o.get('label')
                P('warn', 'dead-end', 'event', eid, "%s 뒤에 이어지는 사건이 없다. 여기서 끝나는 것이면 '줄기가 끝난다'를 켠다 (파일에서는 \"ends\": true)" % where)

    for tid in c.threads:
        if not per_thread.get(tid):
            P('warn', 'empty-thread', 'thread', tid, '이 줄기에 사건이 하나도 없다')
    for sid, st in S.items():
        if st.get('owner') == 'game':
            continue
        r, w = sid in reads, sid in writes
        if not r and not w:
            P('warn', 'unused-state', 'state', sid, '어느 사건도 쓰지 않는 상태값이다')
        elif w and not r and not st.get('keep'):
            P('warn', 'write-only', 'state', sid, '바꾸기만 하고 읽는 사건이 없다. 나중을 위해 남기는 것이면 "남겨 둔다"를 켠다 (파일에서는 "keep": true)')
        elif r and not w:
            P('warn', 'read-only', 'state', sid, '읽기만 하고 바꾸는 사건이 없다. 늘 기본값이다')
    return graph


def build_graph(c, parsed):
    """사건과 상태값의 연결을 만든다."""
    S = c.states
    readers = {}   # state -> [(event, 'when'|'need', atom, text)]
    for eid, p in parsed.items():
        ev = c.events[eid]
        for clause, text in zip(p['when'], ev.get('when') or []):
            for a in clause:
                if a['k'] in ('cmp', 'flag'):
                    readers.setdefault(a['state'], []).append((eid, 'when', a, text))
        for kind, i, o, needs, effs in p['out']:
            for clause in needs:
                for a in clause:
                    if a['k'] in ('cmp', 'flag'):
                        readers.setdefault(a['state'], []).append((eid, 'need', a, o.get('label') or ''))

    def feeds(e, a):
        """결과 e가 조건 a를 맞춰 줄 수 있는가. (이어짐, 순서를 정함)"""
        if e['k'] != 'set' or (e['val'][0] == 'ident' and S[e['state']].get('type') != 'enum'):
            return True, False
        v = e['val'][1]
        if a['k'] == 'flag':
            return (v is (not a['neg'])), True
        if a['val'][0] == 'ident' and S[a['state']].get('type') != 'enum':
            return True, False
        return compare(v, a['op'], a['val'][1]), True

    raw = []
    for eid, p in parsed.items():
        ev = c.events[eid]
        for kind, i, o, needs, effs in p['out']:
            for e in effs:
                if e['k'] == 'cmd' or e['state'] not in S or S[e['state']].get('owner') == 'game':
                    continue
                for (rid, rkind, a, text) in readers.get(e['state'], []):
                    if rid == eid:
                        continue
                    ok, order = feeds(e, a)
                    if not ok:
                        continue
                    raw.append({'from': eid, 'to': rid, 'state': e['state'], 'via': kind, 'idx': i,
                                'kind': rkind, 'order': order and rkind == 'when',
                                'tone': o.get('tone') if kind == 'ignore' else None,
                                'label': '방치' if kind == 'ignore' else (o.get('label') or ''),
                                'cross': ev.get('thread') != c.events[rid].get('thread')})
    hubs = sorted(set(r['state'] for r in raw if r['cross']))
    edges, seen = [], {}

    def merge(key, edge, label):
        if key in seen:
            e = seen[key]
            if label and label not in e['labels']:
                e['labels'].append(label)
            e['order'] = e['order'] or edge['order']
            if edge.get('state') and edge['state'] not in e.get('states', []):
                e.setdefault('states', []).append(edge['state'])
            return
        edge['labels'] = [label] if label else []
        seen[key] = edge
        edges.append(edge)

    for r in raw:
        if r['state'] in hubs:
            hid = 'state:' + r['state']
            merge((r['from'], hid, r['via'], r['tone']),
                  {'from': r['from'], 'to': hid, 'via': r['via'], 'tone': r['tone'], 'kind': 'write', 'order': r['order']}, r['label'])
            merge((hid, r['to'], r['kind']),
                  {'from': hid, 'to': r['to'], 'via': 'state', 'tone': None, 'kind': r['kind'], 'order': r['order']}, '')
        else:
            merge((r['from'], r['to'], r['via'], r['kind'], r['tone']),
                  {'from': r['from'], 'to': r['to'], 'via': r['via'], 'tone': r['tone'], 'kind': r['kind'],
                   'order': r['order'], 'states': [r['state']]}, r['label'])
    return {'hubs': hubs, 'edges': edges, 'raw': raw}


def analyse(root):
    c = load(root)
    graph = validate(c)
    return c, graph


def as_payload(c, graph):
    counts = {}
    for p in c.problems:
        key = (p['kind'], p['target'])
        counts.setdefault(key, {'error': 0, 'warn': 0})[p['level']] += 1
    return {
        'root': c.root,
        'packs': [clean(p) | {'dir': p['_dir']} for p in c.packs],
        'threads': list(c.threads.values()),
        'states': list(c.states.values()),
        'techs': list(c.techs.values()),
        'directors': c.directors,
        'events': list(c.events.values()),
        'problems': c.problems,
        'graph': graph,
        'commands': list(COMMANDS),
    }


# ---------------------------------------------------------------- 고치기

def safe_name(text):
    return re.sub(r'[\\/:*?"<>|\s]+', '_', text).strip('._') or 'events'


def pack_dir(c, pack_id):
    for p in c.packs:
        if p['id'] == pack_id:
            return os.path.join(c.root, p['_dir'])
    return os.path.join(c.root, c.packs[0]['_dir'])


def edit_list(path, key, fn):
    data = read_json(path) if os.path.isfile(path) else {key: []}
    data[key] = fn(data.get(key) or [])
    write_json(path, data)


def replace_in(items, original_id, new_item):
    out, done = [], False
    for x in items:
        if isinstance(x, dict) and x.get('id') == original_id:
            if new_item is not None:
                out.append(new_item)
            done = True
        else:
            out.append(x)
    if not done and new_item is not None:
        out.append(new_item)
    return out


def save_item(root, table_name, body):
    """table_name: events, states, threads. body: {original_id, item} 또는 {id, delete: true}"""
    c = load(root)
    table = getattr(c, table_name)
    keys = {'events': EVENT_KEYS, 'states': STATE_KEYS, 'threads': THREAD_KEYS}[table_name]
    original = body.get('original_id')
    if body.get('delete'):
        old = table.get(body.get('id'))
        if old is None:
            raise ValueError('지울 것을 찾지 못했다')
        edit_list(os.path.join(root, old['_file']), table_name, lambda items: replace_in(items, old['id'], None))
        return
    item = body.get('item')
    if not isinstance(item, dict) or not isinstance(item.get('id'), str) or not item['id'].strip():
        raise ValueError('id가 비어 있다')
    item = ordered(clean(item), keys)
    item['id'] = item['id'].strip()
    old = table.get(original) if original else None
    if item['id'] in table and (old is None or item['id'] != original):
        raise ValueError("id '%s'가 이미 있다" % item['id'])
    if old is not None and table_name == 'events' and old.get('thread') != item.get('thread'):
        # 줄기를 옮기면 옛 파일에서 빼고 새 줄기의 파일에 넣는다
        edit_list(os.path.join(root, old['_file']), 'events', lambda items: replace_in(items, original, None))
        body = dict(body, pack=old['_pack'])
        del c.events[original]
        old = None
    if old is not None:
        path = os.path.join(root, old['_file'])
    else:
        pdir = pack_dir(c, body.get('pack'))
        if table_name == 'events':
            siblings = [e for e in c.events.values() if e.get('thread') == item.get('thread') and e['_pack'] == os.path.basename(pdir)]
            path = os.path.join(root, siblings[0]['_file']) if siblings else os.path.join(pdir, 'events', safe_name(str(item.get('thread') or 'events')) + '.json')
        else:
            path = os.path.join(pdir, table_name + '.json')
    if not os.path.abspath(path).startswith(os.path.abspath(root)):
        raise ValueError('자료 폴더 밖에는 쓸 수 없다')
    edit_list(path, table_name, lambda items: replace_in(items, original if old is not None else item['id'], item))
    if table_name == 'threads' and old is not None and original != item['id']:
        # 줄기 이름이 바뀌면 그 줄기의 사건도 따라 고친다
        for ev in c.events.values():
            if ev.get('thread') == original:
                ev2 = clean(ev)
                ev2['thread'] = item['id']
                edit_list(os.path.join(root, ev['_file']), 'events', lambda items, ev2=ev2: replace_in(items, ev2['id'], ev2))


# ---------------------------------------------------------------- 서버

class Handler(BaseHTTPRequestHandler):
    root = DEFAULT_ROOT

    def log_message(self, fmt, *args):
        pass

    def send(self, code, body, ctype='application/json; charset=utf-8'):
        if not isinstance(body, bytes):
            body = json.dumps(body, ensure_ascii=False).encode('utf-8')
        self.send_response(code)
        self.send_header('Content-Type', ctype)
        self.send_header('Content-Length', str(len(body)))
        self.send_header('Cache-Control', 'no-store')
        self.end_headers()
        self.wfile.write(body)

    def data(self):
        c, graph = analyse(self.root)
        return as_payload(c, graph)

    def do_GET(self):
        path = self.path.split('?')[0]
        if path in ('/', '/index.html'):
            with open(os.path.join(HERE, 'index.html'), 'rb') as f:
                self.send(200, f.read(), 'text/html; charset=utf-8')
        elif path == '/api/data':
            self.send(200, self.data())
        else:
            self.send(404, {'error': '없는 주소'})

    def do_POST(self):
        host = self.headers.get('Host', '')
        origin = self.headers.get('Origin')
        if origin and origin.split('//')[-1] != host:
            return self.send(403, {'error': '다른 곳에서 온 요청은 받지 않는다'})
        try:
            body = json.loads(self.rfile.read(int(self.headers.get('Content-Length') or 0)).decode('utf-8') or '{}')
            table = {'/api/event': 'events', '/api/state': 'states', '/api/thread': 'threads'}.get(self.path)
            if table is None:
                return self.send(404, {'error': '없는 주소'})
            save_item(self.root, table, body)
            self.send(200, self.data())
        except (ValueError, OSError, KeyError) as e:
            self.send(400, {'error': str(e)})


def serve(root, port, open_browser):
    Handler.root = root
    server = None
    for p in range(port, port + 20):
        try:
            server = ThreadingHTTPServer(('127.0.0.1', p), Handler)
            break
        except OSError:
            continue
    if server is None:
        print('빈 포트를 찾지 못했다')
        return 1
    url = 'http://127.0.0.1:%d/' % server.server_address[1]
    print('퀘스트 지도: %s' % url)
    print('자료 폴더: %s' % root)
    print('끄려면 이 창을 닫거나 Ctrl+C를 누른다.')
    if open_browser:
        threading.Timer(0.5, lambda: webbrowser.open(url)).start()
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    return 0


# ---------------------------------------------------------------- 명령

def cmd_check(root, strict):
    c, _ = analyse(root)
    errors = [p for p in c.problems if p['level'] == 'error']
    warns = [p for p in c.problems if p['level'] == 'warn']
    kinds = {'event': '사건', 'state': '상태값', 'thread': '줄기', 'file': '파일', 'tech': '테크'}
    for p in errors + warns:
        print('[%s] %s %s: %s' % ('오류' if p['level'] == 'error' else '주의', kinds.get(p['kind'], p['kind']), p['target'], p['msg']))
    print('묶음 %d, 줄기 %d, 사건 %d, 상태값 %d  /  오류 %d, 주의 %d'
          % (len(c.packs), len(c.threads), len(c.events), len(c.states), len(errors), len(warns)))
    if errors or (strict and warns):
        print('검사 실패')
        return 1
    print('검사 통과')
    return 0


def cmd_format(root):
    n = 0
    for d in pack_dirs(root):
        for dirpath, _, files in os.walk(d):
            for name in files:
                if name.endswith('.json'):
                    p = os.path.join(dirpath, name)
                    write_json(p, read_json(p))
                    n += 1
    print('%d개 파일을 다시 썼다' % n)
    return 0


def main():
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(errors='replace')
        except (AttributeError, ValueError):
            pass
    ap = argparse.ArgumentParser(description='SpaceOdyssey 퀘스트 검사와 지도')
    ap.add_argument('command', nargs='?', default='serve', choices=['check', 'serve', 'format'])
    ap.add_argument('--root', default=DEFAULT_ROOT, help='자료 폴더 (기본: SO/Content)')
    ap.add_argument('--port', type=int, default=8765)
    ap.add_argument('--no-open', action='store_true', help='브라우저를 열지 않는다')
    ap.add_argument('--strict', action='store_true', help='주의도 실패로 친다')
    a = ap.parse_args()
    root = os.path.abspath(a.root)
    if a.command == 'check':
        return cmd_check(root, a.strict)
    if a.command == 'format':
        return cmd_format(root)
    return serve(root, a.port, not a.no_open)


if __name__ == '__main__':
    sys.exit(main())
