"""유니티 에디터 교통정리 도구. 에디터는 SO 프로젝트(SO@852e7e0c) 하나를 여러 세션이 나눠 쓴다.

쓰는 법 (상태 파일은 .claude/editor-lock.json, 커밋하지 않음. .git/info/exclude로 막아 둠):
  python Tools/EditorLock/editorlock.py status
  python Tools/EditorLock/editorlock.py grant <누가> <무엇을 하러> [--minutes 30]   (같은 사람이 다시 부르면 연장)
  python Tools/EditorLock/editorlock.py release <누가> [--note "끝낸 상태"]
  python Tools/EditorLock/editorlock.py queue <누가> <무엇을 하러>
  python Tools/EditorLock/editorlock.py dequeue <누가>
  python Tools/EditorLock/editorlock.py log

규칙: grant/release/queue/dequeue는 본부 세션만 쓴다. 다른 세션은 status만 읽고, 쓰고 싶으면 본부에 메시지를 보낸다.
만료 시각이 지나도 자동으로 풀지 않는다(에디터가 작업 중일 수 있어서). status가 "만료 의심"으로 알릴 뿐이다.
쓰기 명령은 .lock 파일로 직렬화한다. 5초 기다려도 안 되면 에러로 끝내고, 남은 .lock 파일은 사람이 지운다.
"""
import argparse
import json
import os
import sys
import time
from contextlib import contextmanager
from datetime import datetime, timedelta

PATH = r'C:\UnityContents\SpaceOdyssey\SO\.claude\editor-lock.json'
FMT = '%Y-%m-%d %H:%M:%S'


def now():
    return datetime.now()


def load():
    # 최초 실행 때는 파일이 없는 것이 정상이다(빈 상태로 시작).
    if not os.path.exists(PATH):
        return {'holder': None, 'queue': [], 'history': []}
    try:
        with open(PATH, encoding='utf-8') as f:
            return json.load(f)
    except json.JSONDecodeError as e:
        # 손상된 상태를 빈 상태로 덮으면 이중 부여가 되므로 멈추고 사람에게 알린다.
        sys.exit(f'상태 파일 손상: {PATH} ({e}). 본부가 수동 복구 필요')


def save(s):
    s['history'] = s['history'][-60:]
    tmp = f'{PATH}.{os.getpid()}.tmp'
    with open(tmp, 'w', encoding='utf-8') as f:
        json.dump(s, f, ensure_ascii=False, indent=1)
    os.replace(tmp, PATH)


@contextmanager
def write_lock():
    lock = PATH + '.lock'
    for _ in range(50):
        try:
            fd = os.open(lock, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
            os.write(fd, f'{os.getpid()} {now().strftime(FMT)}'.encode())  # 남은 lock이 죽은 것인지 사람이 가려 보게
            break
        except FileExistsError:
            time.sleep(0.1)
    else:
        sys.exit(f'다른 쓰기가 진행 중이거나 남은 잠금 파일임: {lock} (5초 기다림). 파일 안에 만든 프로세스 번호와 시각이 있음. 죽은 것이면 수동 삭제')
    try:
        yield
    finally:
        os.close(fd)
        os.remove(lock)


def log(s, text):
    s['history'].append(f'{now().strftime(FMT)} {text}')


def show(s):
    h = s['holder']
    if not h:
        print('에디터: 비어 있음 (잠금 없음)')
    else:
        since = datetime.strptime(h['since'], FMT)
        until = datetime.strptime(h['until'], FMT)
        mins = int((now() - since).total_seconds() // 60)
        flag = ' <- 만료 의심, 본부가 상태 확인 필요' if now() > until else ''
        print(f"에디터: 잠금 중 - {h['who']} / {h['purpose']}")
        print(f"  시작 {h['since']} ({mins}분 경과), 만료 예정 {h['until']}{flag}")
    if s['queue']:
        print('대기:')
        for i, q in enumerate(s['queue'], 1):
            print(f"  {i}. {q['who']} / {q['purpose']} ({q['at']})")


def refuse(msg):
    print('거부: ' + msg)
    sys.exit(1)


def main():
    # 셸 기본 인코딩(cp949)에서 기호가 죽거나 글자가 깨지는 것을 막음(에러 메시지는 stderr로 나감)
    sys.stdout.reconfigure(encoding='utf-8')
    sys.stderr.reconfigure(encoding='utf-8')
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest='cmd', required=True)
    sub.add_parser('status')
    sub.add_parser('log')
    g = sub.add_parser('grant')
    g.add_argument('who')
    g.add_argument('purpose')
    g.add_argument('--minutes', type=int, default=30)
    r = sub.add_parser('release')
    r.add_argument('who')
    r.add_argument('--note', default='')
    q = sub.add_parser('queue')
    q.add_argument('who')
    q.add_argument('purpose')
    d = sub.add_parser('dequeue')
    d.add_argument('who')
    a = ap.parse_args()
    if a.cmd == 'grant' and not 1 <= a.minutes <= 1440:
        ap.error('--minutes는 1~1440 사이여야 함')

    if a.cmd == 'status':
        show(load())
        return
    if a.cmd == 'log':
        print('\n'.join(load()['history'][-20:]) or '(기록 없음)')
        return

    with write_lock():
        s = load()
        h = s['holder']
        if a.cmd == 'grant':
            if h and h['who'] != a.who:
                refuse(f"사용 중 - {h['who']} ({h['purpose']}). queue로 줄을 세우거나 먼저 release.")
            until = (now() + timedelta(minutes=a.minutes)).strftime(FMT)
            if h:  # 같은 사람이 다시 부르면 연장: 시작 시각은 그대로 둔다
                s['holder'].update(purpose=a.purpose, until=until)
                log(s, f'EXTEND {a.who} - {a.purpose} ({a.minutes}분)')
            else:
                s['holder'] = {'who': a.who, 'purpose': a.purpose, 'since': now().strftime(FMT), 'until': until}
                log(s, f'GRANT {a.who} - {a.purpose} ({a.minutes}분)')
            s['queue'] = [x for x in s['queue'] if x['who'] != a.who]
        elif a.cmd == 'release':
            if not h or h['who'] != a.who:
                refuse(f"지금 잠금 주인은 {h['who'] if h else '없음'}. {a.who}의 것이 아님.")
            s['holder'] = None
            log(s, f'RELEASE {a.who} {a.note}'.rstrip())
        elif a.cmd == 'queue':
            if h and h['who'] == a.who:
                refuse(f'이미 잠금을 가짐 - {a.who}. 연장은 grant를 다시 부름.')
            mine = [x for x in s['queue'] if x['who'] == a.who]
            if mine:  # 줄 서 있으면 순서는 두고 용건만 갱신
                mine[0]['purpose'] = a.purpose
                log(s, f'QUEUE-UPDATE {a.who} - {a.purpose}')
            else:
                s['queue'].append({'who': a.who, 'purpose': a.purpose, 'at': now().strftime(FMT)})
                log(s, f'QUEUE {a.who} - {a.purpose}')
        elif a.cmd == 'dequeue':
            if not any(x['who'] == a.who for x in s['queue']):
                refuse(f'대기열에 없음 - {a.who}.')
            s['queue'] = [x for x in s['queue'] if x['who'] != a.who]
            log(s, f'DEQUEUE {a.who}')
        save(s)
        show(s)


if __name__ == '__main__':
    main()
