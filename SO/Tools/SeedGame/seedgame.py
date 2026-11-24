#!/usr/bin/env python3
"""씨앗 게임 1단계: 씨앗 하나("공짜 수리")를 끝까지 돌려 보는 화면을 만든다.

  python seedgame.py build [--open]        지도 모델을 불러 자동층을 만들고 사이드카를 합쳐 검사한 뒤 seedgame.html을 쓴다
  python seedgame.py watch                 원본이 바뀌면 seedgame.html을 다시 만든다 (Ctrl+C로 끝낸다)
  python seedgame.py fingerprint 묶음 제목  그 씨앗의 지문을 낸다 (사이드카의 "fingerprint"에 붙이는 값)

읽는 곳은 씨앗 지도(Tools/SeedMap/seedmap.py)가 읽는 문서 전부와 sidecar/*.json. 문서는 읽기만 한다.
종료 코드: 0 성공(낡은 사이드카가 있어도 0, 출력 맨 위에 크게 냄), 1 내보내기 실패(닫힌 목록 밖 이름 등).
Python 3.8 이상. 다른 패키지는 필요 없다.
"""
import sys

sys.dont_write_bytecode = True  # Tools/SeedMap/__pycache__에 안 쓰게 seedmap을 부르기 전에 지정한다

import argparse  # noqa: E402
import json  # noqa: E402
import os  # noqa: E402
import pathlib  # noqa: E402
import re  # noqa: E402
import time  # noqa: E402
import traceback  # noqa: E402
import webbrowser  # noqa: E402

import seeddata  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
SEEDMAP = seeddata.seedmap
ENGINE = os.path.join(HERE, 'engine.js')
TEMPLATE = os.path.join(HERE, 'template.html')
DEST = os.path.join(HERE, 'seedgame.html')
PY_FILES = [os.path.join(HERE, 'seedgame.py'), os.path.join(HERE, 'seeddata.py'), SEEDMAP.__file__]


def read(path):
    with open(path, encoding='utf-8-sig') as f:
        return f.read().replace('\r\n', '\n')


def render(data):
    """템플릿의 엔진 자리와 데이터 자리를 채운 HTML 글을 돌려준다."""
    tpl = read(TEMPLATE)
    engine = read(ENGINE)
    for token in ('/*__ENGINE__*/', '/*__DATA__*/null'):
        if tpl.count(token) != 1:
            raise seeddata.DataError(['template.html에 자리 표시 "%s"가 %d개 있음 (하나여야 함)' % (token, tpl.count(token))])
    if '</script' in engine.lower():
        raise seeddata.DataError(['engine.js에 "</script"가 들어 있어 <script> 안에 못 끼움'])
    for name, mine in (('LIFECYCLE', seeddata.STATES), ('PLAIN_CATS', seeddata.PLAIN_CATS),
                       ('LIFECYCLE_CATS', seeddata.LIFECYCLE_CATS), ('LIFECYCLE_GALRAE', seeddata.LIFECYCLE_GALRAE)):
        found = re.search(r"const %s = \[(.*?)\];" % name, engine)
        if found is None or tuple(re.findall(r"'([^']+)'", found.group(1))) != mine:
            raise seeddata.DataError(['engine.js의 %s이 seeddata.py의 같은 이름 목록과 다름 (한쪽을 고쳤으면 다른 쪽도 고칠 것)' % name])
    blob = json.dumps(data, ensure_ascii=False).replace('</', '<\\/').replace('<!--', '<\\!--')
    return tpl.replace('/*__ENGINE__*/', engine).replace('/*__DATA__*/null', blob)


def cmd_build(open_it):
    try:
        c = seeddata.collect()
        html = render(dict(c['payload'], report='\n'.join(c['banner'] + c['report'])))
    except seeddata.DataError as ex:
        print('!' * 70)
        print('!!! 내보내기 실패: %d건. seedgame.html을 쓰지 않았다.' % len(ex.problems))
        for p in ex.problems:
            print('  - ' + p)
        print('!' * 70)
        return 1
    with open(DEST, 'w', encoding='utf-8', newline='\n') as f:
        f.write(html)
    print('\n'.join(c['banner'] + c['report']))
    print('\n→ ' + DEST)
    if open_it:
        webbrowser.open(pathlib.Path(DEST).as_uri())
    return 0


def cmd_fingerprint(bundle, title):
    m = SEEDMAP.build_model()
    found = [s for s in m['seeds'] if s['bundle'] == bundle and s['title'] == title]
    if len(found) != 1:
        print('열쇠 %s %s에 맞는 씨앗이 %d개 (하나여야 함)' % (bundle, title, len(found)), file=sys.stderr)
        return 1
    print(seeddata.fingerprint(found[0]))
    return 0


def watch_stamp():
    """만들 때 읽는 파일들의 수정 시각. 이 값이 바뀌면 다시 만든다."""
    paths = PY_FILES + [ENGINE, TEMPLATE]
    side = seeddata.SIDECAR_DIR
    if os.path.isdir(side):
        paths += [os.path.join(side, f) for f in os.listdir(side) if f.endswith('.json')]
    for d in (SEEDMAP.SEEDS, SEEDMAP.QUESTS):
        if os.path.isdir(d):
            paths += [os.path.join(d, f) for f in os.listdir(d) if f.endswith('.md')]
    return {p: os.path.getmtime(p) for p in paths}


def cmd_watch(interval=2.0, settle=3.0):
    """원본이 바뀌면 다시 만든다. 여러 파일을 연달아 고치는 동안은 기다렸다가 마지막 변경 뒤 settle초에 한 번만 만든다.
    파이썬 파일이 바뀌면 새 코드로 다시 시작한다."""
    print('[감시 시작] 씨앗 문서, 사이드카, 엔진, 템플릿이 바뀌면 seedgame.html을 다시 만든다. 끝내려면 Ctrl+C.')
    cmd_build(False)
    last = watch_stamp()
    changed_at = None
    missing = None
    while True:
        time.sleep(interval)
        try:
            cur = watch_stamp()
        except FileNotFoundError as ex:
            # 편집기가 저장하며 파일을 잠깐 지웠다 만드는 때나 파일이 정말 사라진 때. 숨기지 않고 출력하고 다음 주기에 다시 본다.
            if str(ex) != missing:
                print('[%s] 감시할 파일이 없음: %s. 다음 주기에 다시 본다.' % (time.strftime('%H:%M:%S'), ex))
                sys.stdout.flush()
                missing = str(ex)
            continue
        missing = None
        if cur != last:
            if any(cur.get(p) != last.get(p) for p in PY_FILES):
                print('[%s] 파이썬 파일이 바뀌어 새 코드로 다시 시작한다.' % time.strftime('%H:%M:%S'))
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
                # 문서를 편집하는 도중에 읽으면 파싱이 실패할 수 있다. 숨기지 않고 오류를 출력하고,
                # 이전 seedgame.html은 그대로 둔 채 다음 변경을 기다린다.
                traceback.print_exc()
                print('[%s] 다시 만들지 못했다. 이전 화면을 유지하고 다음 변경을 기다린다.' % time.strftime('%H:%M:%S'))
            sys.stdout.flush()


def main():
    ap = argparse.ArgumentParser(description='씨앗 게임 1단계')
    sub = ap.add_subparsers(dest='cmd')
    b = sub.add_parser('build')
    b.add_argument('--open', action='store_true')
    sub.add_parser('watch', help='원본이 바뀌면 seedgame.html을 자동으로 다시 만든다')
    f = sub.add_parser('fingerprint', help='씨앗의 지문을 낸다')
    f.add_argument('bundle', help='묶음 번호, 예: 06')
    f.add_argument('title', nargs='+', help='씨앗 제목, 예: 공짜 수리')
    args = ap.parse_args()
    if args.cmd == 'watch':
        try:
            cmd_watch()
        except KeyboardInterrupt:
            print('[감시 끝]')
        return 0
    if args.cmd == 'fingerprint':
        return cmd_fingerprint(args.bundle, ' '.join(args.title))
    return cmd_build(getattr(args, 'open', False))


if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    sys.stderr.reconfigure(encoding='utf-8')
    sys.exit(main())
