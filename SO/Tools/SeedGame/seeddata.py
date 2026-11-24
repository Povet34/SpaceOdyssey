"""씨앗 게임 1단계의 데이터층. 지도 모델(seedmap)을 불러 후처리, 통계, 사이드카 검사, 내보내기 보고를 만든다.

새 파서는 없다. 문서 읽기는 전부 seedmap이 한다(build_model, parse_bundle_tables).
못 읽은 것, 모르는 것은 이름을 대고 실패 목록에 올리거나 DataError로 내보내기를 멈춘다. 조용히 넘기지 않는다.
"""
import collections
import hashlib
import json
import os
import re
import sys

sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.normpath(os.path.join(HERE, '..', 'SeedMap')))
import seedmap  # noqa: E402

SIDECAR_DIR = os.path.join(HERE, 'sidecar')
PLAY_KEY = ('06', '공짜 수리')  # 1단계는 씨앗 하나만 끝까지 돈다
TONES = ('나빠짐', '섞임', '잦아듦', '좋아짐')
STATES = ('없음', '미분석', '연구 중', '연구 완료', '가동', '배양 중', '낡음', '고장', '대체됨')  # engine.js의 LIFECYCLE과 같아야 함
# 수단 갈래 판정. engine.js의 PLAIN_CATS, LIFECYCLE_CATS, LIFECYCLE_GALRAE와 같아야 함(build가 대조함)
PLAIN_CATS = ('재료', '물건', '물체')
LIFECYCLE_CATS = ('추진', '제조', '에너지', '감지', '몸', '일손', '사이오닉', '기르는 것', '풀 후보')
LIFECYCLE_GALRAE = ('테크', '일손', '기르는 것')
GAUGES = ('나', '배', '일손', '앎')
CHECKS = ('안 됨', '됨', '보류')
BOOK_CATS = ('추진', '제조', '에너지', '감지', '몸', '일손', '사이오닉', '재료', '기르는 것')  # 00_Means.md 본표 갈래(물건은 따로 센다)
SUPPORT_SECTION = '4. 감지와 통신과 앎'  # 00_TechPool.md의 사다리 후보 장
# 시험용 프리셋 "능력 있음 예시". 5길 중 3길이 채워져 "채운 길만 보임"이 드러난다.
ABILITY_PRESET = {'비파괴 판독기': '가동', '나노 자동 제조기': '가동', '함선 AI': '가동', '침투 탐침': '가동', '손에 맞게 닳은 공구': '계속 씀'}
AUTO_HUMAN = {'surface': None, 'review': {'means': [], 'status': '사이드카 없음(자동층만)'}, 'signs2': '능력 검토'}
EFFECT_FIELDS = {
    '얻음': ('종류', '이름'), '잃음': ('종류', '이름'), '플래그': ('이름',), '게이지': ('축', '증감'),
    '후속 예약': ('씨앗',), '상태 바뀜': ('이름', '상태'), '내력 바뀜': ('이름', '내력'),
}


class DataError(Exception):
    def __init__(self, problems):
        Exception.__init__(self, '\n'.join(problems))
        self.problems = problems


def label(s):
    return '%s %s' % (s['bundle'], s['title'])


def fingerprint(seed):
    """제목, 길 이름, 요구, 길 본문을 이어 만든 해시. 이 넷이 바뀌면 사이드카는 낡은 것이다."""
    parts = [seed['title']]
    for p in seed['paths']:
        parts.append(p['name'])
        parts.append('+'.join('%s:%s' % (r['name'], r['state']) for r in p['means']))
        parts.append(p['text'])
    return hashlib.sha256('\x1f'.join(parts).encode('utf-8')).hexdigest()[:16]


# ---- 후처리 ----

def split_signs(raw):
    """징후 줄을 "며칠"이 든 첫 문장에서 자른다. (징후 1, 징후 2, "며칠" 수)"""
    n = raw.count('며칠')
    if n == 0:
        return raw.strip(), '', 0
    idx = raw.index('며칠')
    cut = 0
    for mm in re.finditer(r'[.!?]["\')\]”’」]*\s+', raw[:idx]):
        cut = mm.end()
    return raw[:cut].strip(), raw[cut:].strip(), n


def split_path(text):
    """길 본문을 (과정, 결과, 남는 것)으로. "→"나 "남는 것:"이 없으면 None."""
    if '→' not in text:
        return None
    process, _, rest = text.partition('→')
    if '남는 것:' not in rest:
        return None
    result, _, left = rest.partition('남는 것:')
    return process.strip(), result.strip(), left.strip()


def first_word(raw):
    mm = re.match(r'[가-힣]+', raw)
    return mm.group(0) if mm else ''


def is_lifecycle(v):
    """engine.js kindOf와 같은 판정: 가동 같은 상태가 있는 수단이면 True, 재료나 물건 꼴이면 False. 모르는 갈래는 이름을 대고 멈춘다."""
    if v['cat'] in PLAIN_CATS:
        return False
    if v['cat'] in LIFECYCLE_CATS:
        return True
    if v['cat'] == '새 수단':
        g = v['cells']['갈래']
        if g == '재료' or g.startswith('물건'):
            return False
        if g in LIFECYCLE_GALRAE:
            return True
    raise DataError(['수단 "%s"의 갈래(%s %s)를 상태 판정에 쓸 수 없음' % (v['name'], v['cat'], v['cells'].get('갈래', ''))])


def analyze(m):
    """씨앗마다 징후 자르기, 길 가르기, 방치 결 읽기. 처리 못 한 씨앗은 fails에 이름으로 올린다."""
    fails = dict(sign1_empty=[], sign_multi=[], no_sign=[], path_format=[], tone=[], undefined_means=[], dup_title=[], state_outside=[], sign_single=0)
    info = {}
    for s in m['seeds']:
        raw_sign = s['raw'].get('징후', '')
        sign1, sign2, n = split_signs(raw_sign)
        if not raw_sign:
            fails['no_sign'].append(label(s))
        elif n == 0:
            fails['sign_single'] += 1
        elif not sign1:
            fails['sign1_empty'].append(label(s))
        if n >= 2:
            fails['sign_multi'].append(label(s))
        paths = []
        for p in s['paths']:
            parts = split_path(p['text'])
            if parts is None:
                fails['path_format'].append('%s / %s' % (label(s), p['name']))
                parts = ('', '', '')
            paths.append(dict(name=p['name'], means=p['means'], process=parts[0], result=parts[1], left=parts[2]))
        tone = first_word(s['raw'].get('방치', ''))
        if tone not in TONES:
            fails['tone'].append('%s (첫 낱말 "%s")' % (label(s), tone))
        info[s['id']] = dict(sign1=sign1, sign2=sign2, paths=paths, tone=tone)
    uses = collections.Counter()
    for s in m['seeds']:
        for n in {r['name'] for p in s['paths'] for r in p['means']}:
            if not m['means'][n]['known']:
                uses[n] += 1
    fails['undefined_means'] = ['%s (씨앗 %d개에 쓰임)' % (n, c) for n, c in sorted(uses.items())]
    outside = collections.defaultdict(set)  # (수단, 상태) -> 쓰인 씨앗들. 상태가 있는 수단에 붙은 상태 사전 밖 꼬리표
    for s in m['seeds']:
        for p in s['paths']:
            for r in p['means']:
                v = m['means'][r['name']]
                if v['known'] and r['state'] and r['state'] != '탑재 중' and r['state'] not in STATES and is_lifecycle(v):
                    outside[(r['name'], r['state'])].add(s['id'])
    fails['state_outside'] = ['%s: %s (씨앗 %d개)' % (n, st, len(ids)) for (n, st), ids in sorted(outside.items())]
    by_key = collections.Counter((s['bundle'], s['title']) for s in m['seeds'])
    fails['dup_title'] = ['%s %s (%d개)' % (b, t, c) for (b, t), c in sorted(by_key.items()) if c > 1]
    return dict(fails=fails, info=info)


# ---- 통계: 출발 장비로 열리는 씨앗, 사다리가 있는 씨앗 ----

def start_kit(m):
    """출발 장비 = 00_Means.md 본표 수단 중 시기가 출발이나 전시기로 시작하는 것 + 물건 13개."""
    for n, v in m['means'].items():
        if v['cat'] in BOOK_CATS and '시기' not in v['cells']:
            raise DataError(['본표 수단 "%s"(%s)에 시기 칸이 없음 (00_Means.md 표 머리가 바뀌었나)' % (n, v['cat'])])
    book = {n for n, v in m['means'].items() if v['cat'] in BOOK_CATS and v['cells']['시기'].startswith(('출발', '전시기'))}
    items = {n for n, v in m['means'].items() if v['cat'] == '물건'}
    return book, items


def ladder_names(m):
    """사다리(엄격) = 지도 모델 갈래가 감지인 수단 + 테크 풀 "감지와 통신과 앎" 장의 후보."""
    pool = seedmap.parse_bundle_tables(os.path.join(seedmap.SEEDS, '00_TechPool.md'), 'TP')
    support = {n for n, v in pool.items() if v['section'].startswith(SUPPORT_SECTION)}
    if not support:
        raise DataError(['테크 풀에서 "%s" 장의 후보를 못 찾음 (장 제목이 바뀌었나)' % SUPPORT_SECTION])
    sensing = {n for n, v in m['means'].items() if v['cat'] == '감지'}
    return sensing | support, len(sensing), len(support)


def path_open(p, book, items):
    """길이 열린다 = 길의 모든 수단이 출발 장비에 있고, 상태 표기가 있는 요구는 물건일 때만 허용."""
    for r in p['means']:
        if r['name'] in items:
            continue  # 물건은 내력 아무거나 허용
        if r['name'] not in book or r['state']:
            return False
    return True


def compute_stats(m):
    book, items = start_kit(m)
    ladder, n_sensing, n_support = ladder_names(m)
    rows = collections.OrderedDict((b, dict(name=seedmap.BUNDLES[b], seeds=0, open=0, ladder=0)) for b in sorted(seedmap.BUNDLES))
    paths_total = paths_open = 0
    for s in m['seeds']:
        r = rows[s['bundle']]
        r['seeds'] += 1
        opened = [path_open(p, book, items) for p in s['paths']]
        paths_total += len(opened)
        paths_open += sum(opened)
        if any(opened):
            r['open'] += 1
        if any(rr['name'] in ladder for p in s['paths'] for rr in p['means']):
            r['ladder'] += 1
    return dict(rows=rows, paths_total=paths_total, paths_open=paths_open, kit=sorted(book | items),
                n_book=len(book), n_items=len(items), n_sensing=n_sensing, n_support=n_support)


# ---- 사이드카 ----

def seed_objects(s):
    return [x.strip() for x in s['raw'].get('물체', '').split('.') if x.strip()]


def item_tags(m):
    """수단마다 지도 모델 전체 씨앗의 요구에 나오는 상태 표기(물건이면 내력) 값들."""
    tags = collections.defaultdict(set)
    for s in m['seeds']:
        for p in s['paths']:
            for r in p['means']:
                if r['state']:
                    tags[r['name']].add(r['state'])
    return tags


def check_effect(where, e, ctx, seed, parsed):
    """효과 낱말 하나. seed는 이 사이드카가 정상일 때만 오고, 씨앗 글에 기대는 검사는 그때만 한다."""
    out = []
    if not isinstance(e, dict) or '효과' not in e:
        return ['%s: 효과 항목에 "효과" 낱말이 없음: %r' % (where, e)]
    word = e['효과']
    if word not in EFFECT_FIELDS:
        return ['%s: 닫힌 목록에 없는 효과 낱말 "%s" (허용: %s)' % (where, word, ', '.join(EFFECT_FIELDS))]
    want = set(EFFECT_FIELDS[word]) | {'효과', '근거'}
    if set(e) != want:
        return ['%s: "%s" 효과의 필드가 %s여야 함, 실제 %s' % (where, word, sorted(want), sorted(e))]
    if not isinstance(e['근거'], str) or not e['근거']:
        return ['%s: "근거"는 비지 않은 글이어야 함: %r' % (where, e['근거'])]
    means, titles = ctx['means'], ctx['titles']
    if word in ('얻음', '잃음'):
        if e['종류'] not in ('수단', '물체'):
            out.append('%s: 종류 "%s"는 수단이나 물체여야 함' % (where, e['종류']))
        elif e['종류'] == '수단' and e['이름'] not in means:
            out.append('%s: 지도 모델의 수단 목록에 없는 수단 "%s"' % (where, e['이름']))
        elif e['종류'] == '물체' and seed is not None and e['이름'] not in seed_objects(seed):
            out.append('%s: 씨앗 "물체" 줄에 없는 물체 "%s" (줄: %s)' % (where, e['이름'], seed_objects(seed)))
    elif word == '플래그':
        if not isinstance(e['이름'], str) or not e['이름']:
            out.append('%s: 플래그 이름이 비었음' % where)
    elif word == '게이지':
        if e['축'] not in GAUGES:
            out.append('%s: 게이지 축 "%s"는 %s 중 하나여야 함' % (where, e['축'], '/'.join(GAUGES)))
        if not isinstance(e['증감'], int) or isinstance(e['증감'], bool) or e['증감'] == 0:
            out.append('%s: 게이지 증감은 0이 아닌 정수여야 함: %r' % (where, e['증감']))
    elif word == '후속 예약':
        if e['씨앗'] not in titles:
            out.append('%s: 지도 모델에 없는 후속 씨앗 제목 "%s"' % (where, e['씨앗']))
    elif word == '상태 바뀜':
        if e['이름'] not in means:
            out.append('%s: 지도 모델의 수단 목록에 없는 수단 "%s"' % (where, e['이름']))
        if e['상태'] not in STATES:
            out.append('%s: 상태 사전에 없는 상태 "%s"' % (where, e['상태']))
    elif word == '내력 바뀜':
        if e['이름'] not in means or means[e['이름']]['cat'] != '물건':
            out.append('%s: 물건이 아니거나 목록에 없는 "%s"' % (where, e['이름']))
        elif e['내력'] not in ctx['tags'][e['이름']]:
            out.append('%s: "%s"의 내력으로 모델에 나온 적 없는 값 "%s"' % (where, e['이름'], e['내력']))
    if seed is not None and not out:
        left = parsed['left']
        src = [left, seed['raw'].get('엮임', '')]
        if not any(e['근거'] in t for t in src):
            out.append('%s: 근거 "%s"가 이 길의 "남는 것"이나 씨앗의 "엮임"에 없음' % (where, e['근거']))
    return out


def check_entry(fn, entry, ctx, seed, an):
    """사이드카 항목 하나의 검사. 닫힌 목록 밖 낱말, 모르는 이름은 problems로 올려 내보내기를 멈춘다."""
    where0 = '%s "%s"' % (fn, entry.get('title', '?'))
    out = []
    for k in ('title', 'fingerprint', 'human', 'effects', '검토'):
        if k not in entry:
            return ['%s: 필수 키 "%s"가 없음' % (where0, k)]
    h = entry['human']
    for k in ('surface', 'review', 'signs2'):
        if k not in h:
            return ['%s: human에 필수 키 "%s"가 없음' % (where0, k)]
    if not isinstance(h['surface'], str) or not h['surface']:
        out.append('%s: human.surface는 비지 않은 글이어야 함' % where0)
    if h['signs2'] != '능력 검토':
        out.append('%s: human.signs2 "%s"는 1단계에서 "능력 검토"뿐임' % (where0, h['signs2']))
    rv = h['review']
    if not isinstance(rv, dict) or not isinstance(rv.get('means'), list) or not isinstance(rv.get('status'), str):
        return out + ['%s: human.review는 {"means": [수단...], "status": 글}이어야 함' % where0]
    for n in rv['means']:
        if n not in ctx['means']:
            out.append('%s: 검토 능력의 수단 "%s"가 지도 모델의 수단 목록에 없음' % (where0, n))
    if entry['검토'] not in CHECKS:
        out.append('%s: 검토 "%s"는 %s 중 하나여야 함' % (where0, entry['검토'], '/'.join(CHECKS)))
    if not isinstance(entry['effects'], dict):
        return out + ['%s: effects는 {길 이름: [효과...]}여야 함' % where0]
    if seed is not None:
        names = [p['name'] for p in an['info'][seed['id']]['paths']]
        if len(set(names)) != len(names):
            out.append('%s: 씨앗 안에 같은 이름의 길이 둘 이상이라 길 이름으로 효과를 못 붙임' % where0)
        for k in entry['effects']:
            if k not in names:
                out.append('%s: effects의 길 이름 "%s"가 씨앗의 길 이름과 글자 그대로 같지 않음 (길: %s)' % (where0, k, names))
        for n in names:
            if n not in entry['effects']:
                out.append('%s: effects에 길 "%s"가 없음 (효과가 없으면 빈 목록으로 적는다)' % (where0, n))
    parsed_by_name = {} if seed is None else {p['name']: p for p in an['info'][seed['id']]['paths']}
    for path_name, effs in entry['effects'].items():
        if not isinstance(effs, list):
            out.append('%s: effects["%s"]는 목록이어야 함' % (where0, path_name))
            continue
        for i, e in enumerate(effs):
            parsed = parsed_by_name.get(path_name)  # 길 이름이 안 맞으면 위에서 이미 문제로 올렸고, 글에 기대는 검사만 건너뜀
            out += check_effect('%s 길 "%s" 효과 %d' % (where0, path_name, i + 1), e, ctx, seed if parsed else None, parsed)
    return out


def check_sidecars(m, an):
    """사이드카를 읽어 상태를 정하고 검사한다. 반환: (항목 상태 목록, 문제 목록)."""
    ctx = dict(means=m['means'], titles={s['title'] for s in m['seeds']}, tags=item_tags(m))
    entries, problems = [], []
    seen = {}  # (묶음, 제목) -> 처음 나온 파일. 파일을 가로질러 하나로 쓴다
    if not os.path.isdir(SIDECAR_DIR):
        return entries, problems
    for fn in sorted(os.listdir(SIDECAR_DIR)):
        if not fn.endswith('.json'):
            continue
        with open(os.path.join(SIDECAR_DIR, fn), encoding='utf-8-sig') as f:
            try:
                doc = json.load(f)
            except ValueError as ex:
                problems.append('%s: JSON을 읽지 못함: %s' % (fn, ex))
                continue
        if not isinstance(doc, dict) or doc.get('bundle') not in seedmap.BUNDLES or not isinstance(doc.get('seeds'), list):
            problems.append('%s: {"bundle": 묶음 번호, "seeds": [...]} 꼴이 아님' % fn)
            continue
        if os.path.splitext(fn)[0].split('_')[0] != doc['bundle']:
            problems.append('%s: 파일 이름의 묶음 "%s"와 안의 bundle "%s"가 다름' % (fn, os.path.splitext(fn)[0].split('_')[0], doc['bundle']))
        for entry in doc['seeds']:
            if not isinstance(entry, dict):
                problems.append('%s: seeds 안에 항목이 아닌 값이 있음: %r' % (fn, entry))
                continue
            title = entry.get('title', '?')
            key = (doc['bundle'], title)
            if key in seen:
                where = '%s에 두 번 나옴' % fn if seen[key] == fn else '%s와 %s에 모두 있음' % (seen[key], fn)
                problems.append('열쇠 %s %s가 %s (씨앗 하나에 사이드카 항목은 하나)' % (key[0], key[1], where))
                continue
            seen[key] = fn
            owners = [s for s in m['seeds'] if s['bundle'] == key[0] and s['title'] == title]
            if len(owners) > 1:
                problems.append('%s: 열쇠 %s %s에 맞는 씨앗이 %d개라 주인을 못 정함' % (fn, key[0], title, len(owners)))
                continue
            seed = owners[0] if owners else None
            if seed is None:
                state = '주인 없음'
            elif fingerprint(seed) != entry.get('fingerprint'):
                state = '낡음'
            else:
                state = '정상'
            problems += check_entry(fn, entry, ctx, seed if state == '정상' else None, an)
            entries.append(dict(file=fn, bundle=key[0], title=title, state=state, entry=entry,
                                seed=seed, doc_fp=fingerprint(seed) if seed else None))
    return entries, problems


# ---- 플레이할 씨앗과 화면 데이터 ----

def find_play_seed(m, an):
    found = [s for s in m['seeds'] if (s['bundle'], s['title']) == PLAY_KEY]
    if len(found) != 1:
        raise DataError(['플레이할 씨앗 %s %s가 문서에 %d개 있음 (하나여야 함)' % (PLAY_KEY[0], PLAY_KEY[1], len(found))])
    s = found[0]
    info = an['info'][s['id']]
    problems = []
    for k in ('걸림돌', '맨손', '방치', '엮임', '물체'):
        if not s['raw'].get(k):
            problems.append('플레이할 씨앗 %s: "%s" 줄이 없음' % (label(s), k))
    if not info['sign1']:
        problems.append('플레이할 씨앗 %s: 징후 1이 비었음 (후처리 실패)' % label(s))
    if info['tone'] not in TONES:
        problems.append('플레이할 씨앗 %s: 방치 첫 낱말 "%s"를 못 읽음' % (label(s), info['tone']))
    for p in info['paths']:
        if not p['result']:
            problems.append('플레이할 씨앗 %s: 길 "%s" 본문을 "→"와 "남는 것:"으로 못 가름' % (label(s), p['name']))
    if problems:
        raise DataError(problems)
    return s


def play_payload(m, an, stats, seed, side):
    info = an['info'][seed['id']]
    mine = [e for e in side if e['seed'] is seed]
    if mine:
        e = mine[0]
        human = e['entry']['human']
        sidecar = dict(state=e['state'], fingerprintSidecar=e['entry']['fingerprint'], fingerprintDoc=e['doc_fp'],
                       checked=e['entry']['검토'] if e['state'] == '정상' else '안 됨',
                       effects=e['entry']['effects'] if e['state'] == '정상' else {})
        if e['state'] != '정상':
            human = AUTO_HUMAN
    else:
        human = AUTO_HUMAN
        sidecar = dict(state='없음', fingerprintSidecar='', fingerprintDoc=fingerprint(seed), checked='안 됨', effects={})
    panel = list(human['review']['means'])
    for p in seed['paths']:
        for r in p['means']:
            if r['name'] not in panel:
                panel.append(r['name'])
    for n in ABILITY_PRESET:
        if n not in m['means']:
            raise DataError(['프리셋 "능력 있음 예시"의 수단 "%s"가 지도 모델의 수단 목록에 없음' % n])
        if n not in panel:
            panel.append(n)  # 사이드카가 없거나 낡아 검토 능력 목록이 비어도 시험용 프리셋은 그대로 쓸 수 있어야 한다
    tags = item_tags(m)
    # 갈래 칸은 새 수단 표에만 있다. 다른 수단은 갈래 ''로 넘기고 engine.js가 cat으로 판정한다.
    meta = {n: dict(name=n, cat=m['means'][n]['cat'], galrae=m['means'][n]['cells'].get('갈래', '')) for n in panel}
    raw = seed['raw']
    return dict(
        seed=dict(key='%s %s' % PLAY_KEY, bundle=seed['bundle'], title=seed['title'], eras=seed['eras_raw'],
                  targets=seed['targets'], sajeong=seed['summary'], sign1=info['sign1'], sign2=info['sign2'],
                  obstacle=raw['걸림돌'], bare=raw['맨손'], ignore=raw['방치'], tone=info['tone'],
                  objects=seed_objects(seed), links=raw['엮임'],
                  paths=[dict(name=p['name'], means=p['means'], process=p['process'], result=p['result'], left=p['left'])
                         for p in info['paths']]),
        human=human, sidecar=sidecar, meta=meta, panel=panel,
        tags={n: sorted(tags[n]) for n in panel}, kit=stats['kit'], ability=ABILITY_PRESET)


# ---- 보고 ----

def ratio(a, b):
    return '%d/%d (%.0f%%)' % (a, b, 100.0 * a / b)


def names(items):
    return ', '.join(items) if items else '없음'


def report_lines(m, an, stats, side):
    f = an['fails']
    n_paths = sum(len(s['paths']) for s in m['seeds'])
    L = ['씨앗 %d개를 읽었다 (길 %d개, 수단 %d종)' % (len(m['seeds']), n_paths, len(m['means'])), '']
    L.append('후처리 실패 목록 (처리 못 한 씨앗을 "묶음 제목"으로. 숨기지 않는다)')
    L.append('① 징후 자르기')
    L.append('   징후 1이 비는 씨앗(첫 문장에 "며칠") %d개: %s' % (len(f['sign1_empty']), names(f['sign1_empty'])))
    L.append('   "며칠"이 두 번 이상인 씨앗(첫 번째에서 자름) %d개: %s' % (len(f['sign_multi']), names(f['sign_multi'])))
    L.append('   징후 줄이 없는 씨앗 %d개: %s' % (len(f['no_sign']), names(f['no_sign'])))
    L.append('   "며칠"이 없는 씨앗 %d개 (한 단계, 실패 아님)' % f['sign_single'])
    L.append('② 길 본문에 "→"나 "남는 것:"이 없는 길 %d개: %s' % (len(f['path_format']), names(f['path_format'])))
    L.append('③ 방치 첫 낱말이 나빠짐/섞임/잦아듦/좋아짐이 아닌 씨앗 %d개: %s' % (len(f['tone']), names(f['tone'])))
    L.append('④ 지도 모델에서 정의 없는 수단 %d개: %s' % (len(f['undefined_means']), names(f['undefined_means'])))
    L.append('⑤ 같은 묶음 안에서 제목이 겹치는 씨앗 %d개: %s' % (len(f['dup_title']), names(f['dup_title'])))
    L.append('⑥ 상태 사전 밖 요구(상태가 있는 수단에 붙은 꼬리표, 엔진이 만나면 이름을 대고 멈춤) %d종: %s' % (len(f['state_outside']), names(f['state_outside'])))
    L.append('')
    L.append('묶음별 (출발 장비 %d종 = 본표 %d + 물건 %d. 사다리는 엄격 정의: 감지 갈래 %d종 + 풀 "감지와 통신과 앎" 후보 %d종)' % (
        stats['n_book'] + stats['n_items'], stats['n_book'], stats['n_items'], stats['n_sensing'], stats['n_support']))
    L.append('  열림 = 내력 무시, 탑재 중 요구는 닫힘 (화면 엔진 규칙과 다름: 엔진은 탑재 중을 가동이면 채우고 물건은 내력이 같아야 채움)')
    tot = dict(seeds=0, open=0, ladder=0)
    for b, r in stats['rows'].items():
        L.append('  %s %s | 씨앗 %d | 출발 장비로 열림 %s | 사다리 있음 %s' % (b, r['name'], r['seeds'], ratio(r['open'], r['seeds']), ratio(r['ladder'], r['seeds'])))
        for k in tot:
            tot[k] += r[k]
    L.append('  전체 | 씨앗 %d | 출발 장비로 열림 %s | 사다리 있음 %s' % (tot['seeds'], ratio(tot['open'], tot['seeds']), ratio(tot['ladder'], tot['seeds'])))
    L.append('  열리는 길: %s' % ratio(stats['paths_open'], stats['paths_total']))
    rows = list(stats['rows'].items())
    low = min(rows, key=lambda kv: kv[1]['open'] / kv[1]['seeds'])
    low_l = min(rows, key=lambda kv: kv[1]['ladder'] / kv[1]['seeds'])
    small = min(rows, key=lambda kv: kv[1]['seeds'])
    L.append('  열림 비율이 가장 낮은 묶음: %s %s %s / 사다리 비율이 가장 낮은 묶음: %s %s %s / 씨앗이 가장 적은 묶음: %s %s (%d개)' % (
        low[0], low[1]['name'], ratio(low[1]['open'], low[1]['seeds']), low_l[0], low_l[1]['name'],
        ratio(low_l[1]['ladder'], low_l[1]['seeds']), small[0], small[1]['name'], small[1]['seeds']))
    L.append('')
    L.append('사이드카')
    if not side:
        L.append('  사이드카 파일 없음')
    for e in side:
        extra = ''
        if e['state'] == '낡음':
            extra = ' (사이드카 지문 %s, 문서 지문 %s. 자동층으로만 뜸)' % (e['entry']['fingerprint'], e['doc_fp'])
        elif e['state'] == '정상':
            h = e['entry']['human']
            extra = ' (지문 %s, 검토: %s, 검토 능력: %s [%s])' % (
                e['entry']['fingerprint'], e['entry']['검토'], names(h['review']['means']), h['review']['status'])
        L.append('  %s %s %s: %s%s' % (e['file'], e['bundle'], e['title'], e['state'], extra))
    return L


def banner_lines(side):
    stale = ['%s %s' % (e['bundle'], e['title']) for e in side if e['state'] == '낡음']
    lost = ['%s %s' % (e['bundle'], e['title']) for e in side if e['state'] == '주인 없음']
    if not stale and not lost:
        return []
    L = ['#' * 70]
    if stale:
        L.append('!!! 낡은 사이드카 %d개 (지문이 문서와 다름. 사이드카 없이 자동층으로만 뜸): %s' % (len(stale), ', '.join(stale)))
    if lost:
        L.append('!!! 주인 없는 사이드카 %d개 (열쇠에 맞는 씨앗이 없음): %s' % (len(lost), ', '.join(lost)))
    L.append('#' * 70)
    return L


def collect():
    """지도 모델을 읽어 내보낼 것을 전부 모은다. 못 내보내는 문제가 있으면 DataError."""
    m = seedmap.build_model()
    an = analyze(m)
    stats = compute_stats(m)
    side, problems = check_sidecars(m, an)
    if problems:
        raise DataError(problems)
    seed = find_play_seed(m, an)
    return dict(payload=play_payload(m, an, stats, seed, side), report=report_lines(m, an, stats, side),
                banner=banner_lines(side), model=m)
