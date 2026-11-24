#!/usr/bin/env python3
"""씨앗 지도: 씨앗, 퀘스트라인, 수단을 한 그림으로 잇는다.

  python seedmap.py build [--open]   seedmap.html을 만든다 (브라우저로 연다)
  python seedmap.py check            검사와 엮임 보강 후보를 글로 낸다 (오류가 있으면 종료 코드 1)
  python seedmap.py watch            원본이 바뀌면 seedmap.html을 자동으로 다시 만든다 (Ctrl+C로 끝낸다)

읽는 곳: SO/Docs/QuestSeeds/0N_*.md (씨앗), SO/Docs/QuestSeeds/00_Means.md (수단 목록), SO/Docs/Quests/ (퀘스트라인)
Python 3.8 이상. 다른 패키지는 필요 없다.
"""
import argparse
import collections
import json
import os
import pathlib
import re
import sys
import time
import traceback
import webbrowser

HERE = os.path.dirname(os.path.abspath(__file__))
DOCS = os.path.normpath(os.path.join(HERE, '..', '..', 'Docs'))
SEEDS = os.path.join(DOCS, 'QuestSeeds')
QUESTS = os.path.join(DOCS, 'Quests')

BUNDLES = {
    '01': '깊은 시간', '02': '위상 지성', '03': '신앙', '04': '사이오닉',
    '05': '물체', '06': '스페이스 오페라', '07': '문명', '08': '몸과 AI',
}
QUIET_SECTIONS = ('시간표', '물체', '새로 필요한 수단', '근거 메모', '근거로 본 자료', '유형 색인', '이 묶음이 다루는 것')
DROPPED = []  # (묶음, 제목, 장): 씨앗 모양이 아니라 건너뛴 ### 블록
ERAS = ['출발', '성간', '거대공학', '초월', '별의끝', '암흑']
BIG = ['하이퍼 드라이브 엔진', '프로그래밍 가능한 물질', '나노 자동 제조기',
       '플라즈마 도관', '반물질 촉매 폭발탄', '사이킥 자가발전체']
CAT_BY_HEAD = {
    '추진과 항법': '추진', '물질과 제조': '제조', '에너지와 배선': '에너지', '감지와 앎': '감지',
    '몸과 정신': '몸', '일손': '일손', '사이오닉': '사이오닉', '재료': '재료', '물건': '물건', '기르는 것': '기르는 것',
}
# 사람이 배에 남는다는 뜻의 낱말. 오탐이 있어 경고로만 쓴다.
PASSENGER = re.compile(r'동행|식구|말 상대가 (?:생|는|하나)|같이 간다|배에 사람|배에 사는|배에서 자란다|선원이 된다|함께 산다|같이 산다')
LETTER_BAD = re.compile(r'인장|권한|명령서|서명|화물 목록')
OUTSIDE_SHIP = re.compile(r'배 밖이라|배 안이 아니|배 밖이므로|배 안이 아니라')
FIELDS = ['징후', '걸림돌', '맨손', '방치', '물체', '엮임', '뿌리']


def read(path):
    with open(path, encoding='utf-8-sig') as f:
        return f.read().replace('\r\n', '\n')


def split_cells(line):
    return [c.strip() for c in line.strip().strip('|').split('|')]


def parse_means_ref(inner):
    """'[A + B: 상태] 이름' 안쪽 'A + B: 상태'를 [(이름, 상태)]로."""
    out = []
    for part in inner.split(' + '):
        name, _, state = part.partition(':')
        out.append((name.strip(), state.strip()))
    return out


def parse_seed_file(path, bundle):
    text = read(path)
    seeds = []
    h2 = ''
    cur = None
    mode = None
    lines = text.split('\n')
    for ln in lines:
        if ln.startswith('## '):
            h2 = ln[3:].strip()
            if cur:
                seeds.append(cur)
                cur = None
            continue
        if ln.startswith('### '):
            if cur:
                seeds.append(cur)
            cur = dict(bundle=bundle, section=h2, title=ln[4:].strip(), eras=[], targets=[], kinds=[],
                       summary='', paths=[], raw={}, has=set(), body=0)
            mode = None
            continue
        if cur is None:
            continue
        if ln.strip():
            cur['body'] += 1
        m = re.match(r'^시기:\s*(.*?)\s*·\s*노리는 곳:\s*(.*?)\s*·\s*결:\s*(.*)$', ln)
        if m:
            cur['eras_raw'] = m.group(1)
            for tok in m.group(1).split(' · '):
                tok = tok.strip()
                hit = [e for e in ERAS + ['전시기'] if tok.startswith(e)]
                if hit:
                    cur['eras'].append(hit[0])
            cur['targets'] = [x.strip() for x in m.group(2).split(',') if x.strip()]
            cur['kinds'] = [x.strip() for x in m.group(3).split(',') if x.strip()]
            continue
        if ln.startswith('사정:'):
            cur['summary'] = ln[3:].strip()
            cur['has'].add('사정')
            mode = None
            continue
        m = re.match(r'^- (징후|걸림돌|푸는 길|맨손|방치|물체|엮임|뿌리):\s*(.*)$', ln)
        if m:
            key = m.group(1)
            cur['has'].add(key)
            mode = key
            if key != '푸는 길':
                cur['raw'][key] = m.group(2).strip()
            continue
        m = re.match(r'^  - \*\*(.*?)\*\*\s*(.*)$', ln)
        if m and mode == '푸는 길':
            inner, rest = m.group(1), m.group(2).lstrip('—').strip()
            mm = re.match(r'^\[(.*?)\]\s*(.*)$', inner)
            if mm:
                refs = parse_means_ref(mm.group(1))
                name = mm.group(2).strip()
            else:
                refs, name = [], inner.strip()
            cur['paths'].append(dict(name=name, means=[dict(name=a, state=b) for a, b in refs], text=rest))
            continue
    if cur:
        seeds.append(cur)
    # '###' 소제목이어도 사정과 푸는 길이 없으면 씨앗이 아니다 (시간표의 소절 등)
    kept = []
    for s in seeds:
        if '사정' in s['has'] or '푸는 길' in s['has']:
            kept.append(s)
        elif s['body'] >= 3 and s['section'] not in QUIET_SECTIONS:
            DROPPED.append((bundle, s['title'], s['section']))
    seeds = kept
    for s in seeds:
        s['has'] = sorted(s['has'])
        ig = s['raw'].get('방치', '')
        s['tone'] = ig.split('.')[0].strip() if ig else ''
    return seeds


def parse_bundle_tables(path, bundle):
    """묶음 파일의 표에서 수단 후보(이름)를 모은다. 갈래 칸이 있으면 새 수단, 아니면 물체."""
    text = read(path)
    found = {}
    sec = ''
    lines = text.split('\n')
    i = 0
    while i < len(lines):
        ln = lines[i]
        if ln.startswith('## '):
            sec = ln[3:].strip()
        if ln.startswith('| ') and i + 1 < len(lines) and lines[i + 1].startswith('|--'):
            head = split_cells(ln)
            j = i + 2
            rows = []
            while j < len(lines) and lines[j].startswith('|'):
                rows.append(split_cells(lines[j]))
                j += 1
            if '이름' in head:
                idx = head.index('이름')
                is_new = '갈래' in head
                for r in rows:
                    if len(r) <= idx or not r[idx]:
                        continue
                    cells = {h: (r[k] if k < len(r) else '') for k, h in enumerate(head)}
                    if r[idx] not in found:  # 같은 이름이 뒤쪽 표(근거가 약한 항목 등)에 또 나와도 먼저 나온 행이 이긴다
                        found[r[idx]] = dict(cat='새 수단' if is_new else '물체', bundle=bundle, cells=cells, section=sec)
            i = j
            continue
        i += 1
    return found


def parse_means_catalog(path):
    text = read(path)
    means = {}
    head_name = ''
    lines = text.split('\n')
    i = 0
    while i < len(lines):
        ln = lines[i]
        if ln.startswith('## '):
            head_name = ln[3:].strip()
        if head_name in CAT_BY_HEAD and ln.startswith('| ') and i + 1 < len(lines) and lines[i + 1].startswith('|--'):
            head = split_cells(ln)
            j = i + 2
            while j < len(lines) and lines[j].startswith('|'):
                r = split_cells(lines[j])
                if r and r[0]:
                    means[r[0]] = dict(cat=CAT_BY_HEAD[head_name], cells={h: (r[k] if k < len(r) else '') for k, h in enumerate(head)})
                j += 1
            i = j
            continue
        i += 1
    return means


def parse_quests():
    out = []
    if not os.path.isdir(QUESTS):
        return out
    for fn in sorted(os.listdir(QUESTS)):
        if not re.match(r'^\d\d_.*\.md$', fn):
            continue
        cur = None
        for ln in read(os.path.join(QUESTS, fn)).split('\n'):
            m = re.match(r'^### (\d{3})\.\s*(.*)$', ln)
            if m:
                cur = dict(num=m.group(1), title=m.group(2).strip(), file=fn, steps=[], cond='')
                out.append(cur)
                continue
            if cur is None:
                continue
            if ln.startswith('조건:'):
                cur['cond'] = ln.strip()
            elif re.match(r'^\d\. ', ln):
                cur['steps'].append(ln)
    return out


def era_pos(s):
    idx = [ERAS.index(e) for e in s['eras'] if e in ERAS]
    return sum(idx) / len(idx) if idx else 2.5


def build_model():
    seeds = []
    extra_means = {}
    extra_by_bundle = {}
    for b in sorted(BUNDLES):
        files = sorted(f for f in os.listdir(SEEDS) if f.startswith(b + '_') and f.endswith('.md'))
        if not files:
            continue
        p = os.path.join(SEEDS, files[0])
        for s in parse_seed_file(p, b):
            s['file'] = files[0]
            seeds.append(s)
        found = parse_bundle_tables(p, b)
        extra_by_bundle[b] = found
        extra_means.update({k: v for k, v in found.items() if k not in extra_means})
    catalog = parse_means_catalog(os.path.join(SEEDS, '00_Means.md'))
    pool = {}  # 00_TechPool.md의 후보. 씨앗이 쓰기 전에는 지도에 안 나오고 수단 탭에만 있다
    pool_path = os.path.join(SEEDS, '00_TechPool.md')
    if os.path.exists(pool_path):
        for k, v in parse_bundle_tables(pool_path, 'TP').items():
            if '비틀어 쓰기' in v['cells'] and k not in catalog:
                pool[k] = dict(cat='풀 후보', cells=v['cells'])
    weak = {}  # 00_TechPool.md '근거가 약하거나 지어 넣은 항목' 표: 이름 -> 뿌리 칸의 사정
    if os.path.exists(pool_path):
        in_weak = False
        for ln in open(pool_path, encoding='utf-8').read().split('\n'):
            if ln.startswith('## '):
                in_weak = ln.startswith('## 근거가 약하거나')
                continue
            if in_weak and ln.startswith('|'):
                cells = [x.strip() for x in ln.strip().strip('|').split('|')]
                if len(cells) >= 3 and cells[0].isdigit():
                    weak[cells[1]] = cells[2]
    quests = parse_quests()

    for i, s in enumerate(seeds):
        s['id'] = 's%d' % i
        s['x'] = era_pos(s)
    by_title = collections.defaultdict(list)
    for s in seeds:
        by_title[s['title']].append(s)
    quest_by_num = {q['num']: q for q in quests}
    quest_by_title = {q['title']: q for q in quests}

    # 수단 노드
    means = {}
    unknown = collections.Counter()
    for s in seeds:
        for p in s['paths']:
            for r in p['means']:
                n = r['name']
                if n not in means:
                    if n in catalog:
                        means[n] = dict(name=n, cat=catalog[n]['cat'], cells=catalog[n]['cells'], known=True)
                    elif n in pool:
                        means[n] = dict(name=n, cat='풀 후보', cells=pool[n]['cells'], known=True)
                    elif n in extra_means:
                        em = extra_means[n]
                        means[n] = dict(name=n, cat=em['cat'], cells=em['cells'], known=True, bundle=em['bundle'])
                    else:
                        means[n] = dict(name=n, cat='미정의', cells={}, known=False)
                        unknown[n] += 1
                means[n]['big'] = n in BIG
    for n, v in pool.items():
        if n not in means:
            means[n] = dict(name=n, cat='풀 후보', cells=v['cells'], known=True)
    for n in means:
        means[n]['weak'] = weak.get(n, '')
    # 수단 간선 (씨앗-수단당 하나)
    uses = collections.defaultdict(lambda: collections.defaultdict(list))
    for s in seeds:
        for p in s['paths']:
            for r in p['means']:
                uses[r['name']][s['id']].append(dict(state=r['state'], path=p['name']))
    medges = []
    for n, d in uses.items():
        for sid, lst in d.items():
            states = sorted({x['state'] for x in lst if x['state']})
            medges.append(dict(s=sid, t='m:' + n, kind='means', states=states, paths=[x['path'] for x in lst]))
    for n in means:
        means[n]['uses'] = sum(1 for _ in uses[n])
        means[n]['seeds'] = sorted(uses[n].keys(), key=lambda x: int(x[1:]))

    # 엮임 간선
    ledges = []
    unresolved = []
    seen = set()
    for s in seeds:
        raw = s['raw'].get('엮임', '')
        sents = [x.strip() for x in re.split(r'(?<=[.!?])\s+', raw) if x.strip()]
        for sent in sents:
            for q in re.findall(r'"([^"]{2,30})"', sent):
                tgt = None
                allc = by_title.get(q, [])
                if allc:
                    cands = [c for c in allc if c['id'] != s['id']]
                    same = [c for c in cands if c['bundle'] == s['bundle']]
                    tgt = (same or cands or [None])[0]
                    if tgt:
                        key = (s['id'], tgt['id'])
                        if key not in seen:
                            seen.add(key)
                            ledges.append(dict(s=s['id'], t=tgt['id'], kind='link', label=sent[:200]))
                    continue
                if q in quest_by_title:
                    key = (s['id'], 'q' + quest_by_title[q]['num'])
                    if key not in seen:
                        seen.add(key)
                        ledges.append(dict(s=s['id'], t='q' + quest_by_title[q]['num'], kind='quest', label=sent[:200]))
                    continue
                unresolved.append((s, q))
        text_all = ' '.join([s['summary']] + [s['raw'].get(k, '') for k in FIELDS] + [p['text'] for p in s['paths']])
        for num in re.findall(r'\((\d{3})\)', text_all):
            if num in quest_by_num:
                key = (s['id'], 'q' + num)
                if key not in seen:
                    seen.add(key)
                    ledges.append(dict(s=s['id'], t='q' + num, kind='quest', label=quest_by_num[num]['title']))
            else:
                unresolved.append((s, '(%s)' % num))

    deg = collections.Counter()
    outd, ind = collections.Counter(), collections.Counter()
    for e in ledges:
        deg[e['s']] += 1
        deg[e['t']] += 1
        outd[e['s']] += 1
        ind[e['t']] += 1
    for s in seeds:
        s['out'] = outd[s['id']]
        s['in'] = ind[s['id']]
        s['deg'] = deg[s['id']]

    # 엮임 보강 후보: 드문 수단을 같이 쓰는 씨앗
    sid_means = {s['id']: {r['name'] for p in s['paths'] for r in p['means']} for s in seeds}
    linked = set()
    for e in ledges:
        linked.add((e['s'], e['t']))
        linked.add((e['t'], e['s']))
    for s in seeds:
        sc = []
        for o in seeds:
            if o['id'] == s['id'] or (s['id'], o['id']) in linked:
                continue
            shared = sorted((n for n in sid_means[s['id']] & sid_means[o['id']] if means[n]['uses'] <= 25),
                            key=lambda n: (means[n]['uses'], n))
            score = sum(1.0 / means[n]['uses'] for n in shared)
            if not shared:
                continue
            if set(s['kinds']) & set(o['kinds']):
                score += 0.05
            if abs(era_pos(s) - era_pos(o)) <= 1:
                score += 0.05
            sc.append((score, o['id'], shared))
        sc.sort(key=lambda t: (-t[0], t[1]))
        s['suggest'] = [dict(id=i, shared=sh[:3]) for _, i, sh in sc[:3]]

    return dict(seeds=seeds, means=means, quests=quests, ledges=ledges, medges=medges, unresolved=unresolved,
                extra_means=extra_means, extra_by_bundle=extra_by_bundle, unknown=unknown, by_title=by_title, pool=pool, weak=weak)


def lint(m):
    items = []  # (level, seed_id or None, where, message)
    seeds = m['seeds']
    means = m['means']
    per_bundle = collections.defaultdict(lambda: collections.Counter())
    dae = collections.Counter()  # (묶음, 수단): 그 수단의 대체됨 길 수

    def add(level, sid, msg):
        items.append(dict(level=level, id=sid, msg=msg))

    for s in seeds:
        t = '%s %s' % (s['bundle'], s['title'])
        need = ['사정', '징후', '걸림돌', '푸는 길', '맨손', '방치', '물체', '엮임', '뿌리']
        miss = [k for k in need if k not in s['has']]
        if not s.get('eras_raw'):
            miss.append('시기·노리는 곳·결 줄')
        if miss:
            add('error', s['id'], '%s: 필수 줄 빠짐 (%s)' % (t, ', '.join(miss)))
        n = len(s['paths'])
        if not 4 <= n <= 6:
            add('error', s['id'], '%s: 푸는 길이 %d개다 (4~6개)' % (t, n))
        with_means = [p for p in s['paths'] if p['means']]
        if len(with_means) < 3:
            add('error', s['id'], '%s: 수단이 필요한 길이 %d개다 (3개 이상)' % (t, len(with_means)))
        if not any(r['state'] for p in s['paths'] for r in p['means']):
            add('error', s['id'], '%s: 상태나 내력으로 갈리는 길이 없다' % t)
        for p in s['paths']:
            line = p['name'] + ' ' + p['text']
            pm = PASSENGER.search(line)
            if pm:
                add('warn', s['id'], '%s: 승객 의심 낱말 "%s" (길 "%s")' % (t, pm.group(0), p['name']))
            for r in p['means']:
                if r['name'] == '수리 드론 1형' and r['state'].startswith('대체됨'):
                    per_bundle[s['bundle']]['1형 대체됨'] += 1
                if r['name'] == '접힌 창고' and OUTSIDE_SHIP.search(line):
                    add('warn', s['id'], '%s: 접힌 창고를 배 밖으로 쓴 길 "%s" (설계 결정 2번)' % (t, p['name']))
                if r['name'] == '뜯지 않은 편지' and LETTER_BAD.search(line):
                    add('warn', s['id'], '%s: 뜯지 않은 편지를 문서로 쓴 길 "%s" (설계 결정 3번)' % (t, p['name']))
            if any(r['state'].startswith('대체됨') for r in p['means']):
                per_bundle[s['bundle']]['대체됨 길'] += 1
            if '접힌 창고' in {r['name'] for r in p['means']}:
                per_bundle[s['bundle']]['접힌 창고'] += 1
            for nm in {r['name'] for r in p['means'] if r['state'].startswith('대체됨')}:
                dae[(s['bundle'], nm)] += 1
        for p in s['paths']:
            if not ('→' in p['text']):
                add('warn', s['id'], '%s: 길 "%s"에 → 결과가 없다' % (t, p['name']))
    for b, c in per_bundle.items():
        if c['1형 대체됨'] > 2:
            add('error', None, '%s 묶음: 수리 드론 1형 대체됨 %d개 (2개 이하)' % (b, c['1형 대체됨']))
        if c['접힌 창고'] > 1:
            add('warn', None, '%s 묶음: 접힌 창고 길이 %d개 (설계 결정 2번: 묶음당 1개 이하)' % (b, c['접힌 창고']))
        if c['대체됨 길'] > 8:
            add('warn', None, '%s 묶음: 대체됨 길 %d개 (8개 이하)' % (b, c['대체됨 길']))
    for (b, nm), c in sorted(dae.items()):
        if c > 3:
            add('warn', None, '%s 묶음: %s의 대체됨 길이 %d개 (같은 수단 3개 이하)' % (b, nm, c))
    # 새 수단
    for b, found in sorted(m['extra_by_bundle'].items()):
        names = sorted(n for n, v in found.items() if v['cat'] == '새 수단')
        if len(names) > 6:
            add('warn', None, '%s 묶음: 새 수단 표가 %d행이다 (6개 이하)' % (b, len(names)))
        for n in names:
            used = sum(1 for s in seeds if s['bundle'] == b and any(r['name'] == n for p in s['paths'] for r in p['means']))
            if used < 3:
                add('warn', None, '%s 묶음: 새 수단 "%s"가 그 묶음 씨앗 %d곳에서만 쓰인다 (3곳 이상)' % (b, n, used))
    for n, c in m['unknown'].items():
        add('warn', None, '정의 없는 수단 "%s" (수단 목록, 묶음 표 어디에도 없다)' % n)
    # 퀘스트 번호
    uniq = {(s['id'], q): (s, q) for s, q in m['unresolved']}
    for s, q in sorted(uniq.values(), key=lambda x: (int(x[0]['id'][1:]), x[1])):
        if q.startswith('('):
            add('error', s['id'], '%s %s: 퀘스트라인 번호 %s가 없다' % (s['bundle'], s['title'], q))
        else:
            add('info', s['id'], '%s %s: 엮임의 "%s"가 씨앗이나 퀘스트라인 제목과 안 맞는다' % (s['bundle'], s['title'], q))
    for title, lst in sorted(m['by_title'].items()):
        if len(lst) > 1:
            add('warn', None, '같은 제목의 씨앗이 %d개다: "%s" (%s) — 엮임 따옴표가 어느 쪽인지 모호하다' % (len(lst), title, ', '.join(x['bundle'] for x in lst)))
    for b, title, sec in DROPPED:
        add('info', None, '%s 묶음 "%s"(장: %s): 사정과 푸는 길이 인식되지 않아 씨앗으로 안 읽었다. 줄 모양을 확인한다' % (b, title, sec))
    # 연결
    for s in seeds:
        if s['deg'] == 0:
            add('info', s['id'], '%s %s: 엮임으로 이어진 곳이 없다' % (s['bundle'], s['title']))
        elif s['in'] == 0:
            add('info', s['id'], '%s %s: 아무도 이 씨앗을 가리키지 않는다' % (s['bundle'], s['title']))
    # 쏠림
    big_by_bundle = collections.defaultdict(collections.Counter)
    for s in seeds:
        for p in s['paths']:
            for r in p['means']:
                if r['name'] in BIG:
                    big_by_bundle[s['bundle']][r['name']] += 1
    for b in sorted(BUNDLES):
        absent = [x for x in BIG if big_by_bundle[b][x] == 0]
        if absent and b in {s['bundle'] for s in seeds}:
            add('info', None, '%s 묶음: 큰 테크 %d종이 한 번도 안 쓰였다 (%s)' % (b, len(absent), ', '.join(absent)))
    return items


def payload(m, items):
    seeds_out = []
    for s in m['seeds']:
        seeds_out.append(dict(
            id=s['id'], bundle=s['bundle'], file=s['file'], section=s['section'], title=s['title'],
            eras=s['eras'], eras_raw=s.get('eras_raw', ''), targets=s['targets'], kinds=s['kinds'],
            x=round(s['x'], 2), summary=s['summary'], raw=s['raw'], paths=s['paths'], tone=s['tone'],
            out=s['out'], inn=s['in'], suggest=s['suggest']))
    means_out = []
    for n, v in sorted(m['means'].items()):
        means_out.append(dict(id='m:' + n, name=n, cat=v['cat'], big=v.get('big', False), known=v['known'],
                              cells=v['cells'], uses=v['uses'], seeds=v['seeds'], bundle=v.get('bundle'), weak=v.get('weak', '')))
    quests_out = [dict(id='q' + q['num'], num=q['num'], title=q['title'], cond=q['cond'], steps=q['steps'], file=q['file'])
                  for q in m['quests']]
    return dict(seeds=seeds_out, means=means_out, quests=quests_out, edges=m['ledges'] + m['medges'],
                lint=items, bundles=BUNDLES, eras=ERAS, big=BIG)


def weak_report(m):
    weak = m['weak']
    missing = sorted(n for n in weak if n not in m['pool'])
    used = sorted(n for n in weak if n in m['pool'] and m['means'].get(n, {}).get('uses'))
    s = '근거 약한 풀 후보 %d개 중 씨앗에 쓰인 것 %d개' % (len(weak) - len(missing), len(used))
    if missing:
        s += ' (표에는 있는데 풀에 없는 이름: %s)' % ', '.join(missing)
    return s


def cmd_build(open_it):
    m = build_model()
    items = lint(m)
    data = payload(m, items)
    tpl = read(os.path.join(HERE, 'template.html'))
    blob = json.dumps(data, ensure_ascii=False).replace('</', '<\\/').replace('<!--', '<\\!--')
    out = tpl.replace('/*__DATA__*/null', blob)
    dest = os.path.join(HERE, 'seedmap.html')
    with open(dest, 'w', encoding='utf-8', newline='\n') as f:
        f.write(out)
    counts = collections.Counter(i['level'] for i in items)
    unused = sum(1 for v in m['means'].values() if v['cat'] == '풀 후보' and not v['uses'])
    print('씨앗 %d, 수단 %d (테크 풀 후보 %d개 중 아직 안 쓰인 것 %d), 퀘스트라인 %d, 엮임 %d, 수단 연결 %d' % (
        len(m['seeds']), len(m['means']), len(m['pool']), unused, len(m['quests']),
        len(m['ledges']), len(m['medges'])))
    print('오류 %d, 주의 %d, 참고 %d  → %s' % (counts['error'], counts['warn'], counts['info'], dest))
    print(weak_report(m))
    if open_it:
        webbrowser.open(pathlib.Path(dest).as_uri())


def watch_stamp():
    """지도가 읽는 원본 파일들의 수정 시각. 이 값이 바뀌면 다시 만든다."""
    paths = [os.path.join(HERE, 'seedmap.py'), os.path.join(HERE, 'template.html')]
    for d in (SEEDS, QUESTS):
        if os.path.isdir(d):
            paths += [os.path.join(d, f) for f in os.listdir(d) if f.endswith('.md')]
    return {p: os.path.getmtime(p) for p in paths}


def cmd_watch(interval=2.0, settle=3.0):
    """원본이 바뀌면 지도를 다시 만든다. 에이전트가 여러 파일을 연달아 고치는 동안에는 기다렸다가
    마지막 변경 뒤 settle초가 지나면 한 번만 만든다. seedmap.py 자신이 바뀌면 새 코드로 다시 시작한다."""
    print('[감시 시작] 씨앗 문서와 템플릿이 바뀌면 seedmap.html을 다시 만든다. 끝내려면 Ctrl+C.')
    cmd_build(False)
    last = watch_stamp()
    changed_at = None
    while True:
        time.sleep(interval)
        cur = watch_stamp()
        if cur != last:
            if cur.get(os.path.join(HERE, 'seedmap.py')) != last.get(os.path.join(HERE, 'seedmap.py')):
                print('[%s] seedmap.py가 바뀌어 새 코드로 다시 시작한다.' % time.strftime('%H:%M:%S'))
                sys.stdout.flush()
                os.execv(sys.executable, [sys.executable] + sys.argv)
            last = cur
            changed_at = time.time()
            continue
        if changed_at is not None and time.time() - changed_at >= settle:
            changed_at = None
            print('[%s] 원본이 바뀌어 다시 만든다.' % time.strftime('%H:%M:%S'))
            try:
                cmd_build(False)
            except Exception:
                # 파일을 편집하는 도중에 읽으면 파싱이 실패할 수 있다. 숨기지 않고 오류를 출력하고,
                # 이전 seedmap.html은 그대로 둔 채 다음 변경을 기다린다.
                traceback.print_exc()
                print('[%s] 다시 만들지 못했다. 이전 지도를 유지하고 다음 변경을 기다린다.' % time.strftime('%H:%M:%S'))
            sys.stdout.flush()


def cmd_check(show_suggest, verbose=False):
    m = build_model()
    items = lint(m)
    order = {'error': 0, 'warn': 1, 'info': 2}
    label = {'error': '오류', 'warn': '주의', 'info': '참고'}
    shown_info = 0
    for it in sorted(items, key=lambda x: order[x['level']]):
        if it['level'] == 'info' and not verbose:
            shown_info += 1
            if shown_info > 12:
                continue
        print('[%s] %s' % (label[it['level']], it['msg']))
    if not verbose and shown_info > 12:
        print('... 참고 %d개 더 (--verbose로 모두 본다)' % (shown_info - 12))
    counts = collections.Counter(i['level'] for i in items)
    print('\n씨앗 %d, 수단 %d, 퀘스트라인 %d, 엮임 %d, 수단 연결 %d' % (
        len(m['seeds']), len(m['means']), len(m['quests']), len(m['ledges']), len(m['medges'])))
    print('오류 %d, 주의 %d, 참고 %d' % (counts['error'], counts['warn'], counts['info']))
    print(weak_report(m))
    if show_suggest:
        print('\n엮임 보강 후보 (연결이 적은 씨앗과, 드문 수단을 같이 쓰는 씨앗)')
        low = sorted(m['seeds'], key=lambda s: (s['deg'], s['id']))[:15]
        by_id = {s['id']: s for s in m['seeds']}
        for s in low:
            if not s['suggest']:
                continue
            print('- %s %s (연결 %d)' % (s['bundle'], s['title'], s['deg']))
            for sg in s['suggest']:
                o = by_id[sg['id']]
                print('    → %s %s  [같이 쓰는 수단: %s]' % (o['bundle'], o['title'], ', '.join(sg['shared'])))
    return 1 if counts['error'] else 0


def main():
    ap = argparse.ArgumentParser(description='씨앗 지도')
    sub = ap.add_subparsers(dest='cmd')
    b = sub.add_parser('build')
    b.add_argument('--open', action='store_true')
    c = sub.add_parser('check')
    c.add_argument('--suggest', action='store_true', help='엮임 보강 후보도 낸다')
    c.add_argument('--verbose', action='store_true', help='참고 항목을 모두 낸다')
    sub.add_parser('watch', help='원본이 바뀌면 지도를 자동으로 다시 만든다')
    args = ap.parse_args()
    if args.cmd == 'watch':
        try:
            cmd_watch()
        except KeyboardInterrupt:
            print('[감시 끝]')
        return
    if args.cmd == 'check':
        sys.exit(cmd_check(args.suggest, args.verbose))
    cmd_build(getattr(args, 'open', False))


if __name__ == '__main__':
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except Exception:
        # 일부 터미널은 reconfigure가 없다. 출력 인코딩을 못 바꿔도 검사는 돌아야 한다.
        pass
    main()
