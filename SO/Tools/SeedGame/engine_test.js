// engine.js 규칙 시험. 실행: 먼저 python seedgame.py build, 그다음 node engine_test.js (외부 패키지 없음). 실패가 하나라도 있으면 종료 코드 1.
'use strict';
const assert = require('assert');
const E = require('./engine.js');

let passed = 0;
let failed = 0;
function test(name, fn) {
  try {
    fn();
    passed++;
  } catch (e) {
    failed++;
    console.log('실패: ' + name + '\n  ' + e.message);
  }
}

// 공짜 수리 모양을 줄여 만든 시험용 씨앗. 글은 숨김 시험을 위해 서로 겹치지 않는 표지를 넣었다.
const meta = {
  '나노 자동 제조기': { name: '나노 자동 제조기', cat: '제조', galrae: '' },
  '수리 드론 3형': { name: '수리 드론 3형', cat: '일손', galrae: '' },
  '죽은 드론의 부품': { name: '죽은 드론의 부품', cat: '재료', galrae: '' },
  '플라즈마 도관': { name: '플라즈마 도관', cat: '에너지', galrae: '' },
  '함선 AI': { name: '함선 AI', cat: '일손', galrae: '' },
  '침투 탐침': { name: '침투 탐침', cat: '새 수단', galrae: '일손' },
  '손에 맞게 닳은 공구': { name: '손에 맞게 닳은 공구', cat: '물건', galrae: '' },
  '비파괴 판독기': { name: '비파괴 판독기', cat: '제조', galrae: '' },
  '분광 분석기': { name: '분광 분석기', cat: '감지', galrae: '' },
  '둔한 깊이 한 병': { name: '둔한 깊이 한 병', cat: '새 수단', galrae: '재료' },
};
const seed = {
  title: '공짜 수리', sign1: 'SIGN1표지', sign2: 'SIGN2표지', sajeong: 'SAJEONG표지', obstacle: 'OBSTACLE표지',
  bare: 'BARE표지', ignore: 'IGNORE표지 전체 본문', tone: '나빠짐',
  paths: [
    { name: '걸쳐 놓고', means: [{ name: '나노 자동 제조기', state: '탑재 중' }], process: 'P1', result: 'R1', left: 'L1' },
    { name: '다시 고친다', means: [{ name: '수리 드론 3형', state: '' }, { name: '죽은 드론의 부품', state: '' }], process: 'P2', result: 'R2', left: 'L2' },
    { name: '새는 불', means: [{ name: '플라즈마 도관', state: '고장' }], process: 'P3', result: 'R3', left: 'L3' },
    { name: '가짜로 치른다', means: [{ name: '함선 AI', state: '' }, { name: '침투 탐침', state: '' }], process: 'P4', result: 'R4', left: 'L4' },
    { name: '솜씨를 판다', means: [{ name: '손에 맞게 닳은 공구', state: '계속 씀' }], process: 'P5', result: 'R5', left: 'L5' },
  ],
};
const human = {
  surface: '공짜로 수리해 준다고 한다. 왜 그런 거지?', surface_lines: ['LINE1표지', 'LINE2표지'], after_accept: 'ACCEPT표지',
  decline_result: 'DECLINE표지', discovery: 'DISCOVERY표지', path_labels: { '가짜로 치른다': 'ALIAS표지' },
  review: { means: ['비파괴 판독기'], status: '제안, 사용자 확정 전' },
};
const REVIEW2 = { means: ['비파괴 판독기', '분광 분석기'], status: '' }; // 규칙 시험용: 수단이 둘인 목록
const effects = { '걸쳐 놓고': [{ '효과': '얻음', '종류': '물체', '이름': '정거장의 알갱이', '근거': 'x' }] };
function sidecar(checked) { return { state: '정상', checked: checked, effects: Object.assign({ '다시 고친다': [], '새는 불': [], '가짜로 치른다': [], '솜씨를 판다': [] }, effects) }; }
function hold(over) {
  const h = {};
  Object.keys(meta).forEach(function (n) { h[n] = '없음'; });
  return Object.assign(h, over);
}
const ABILITY = { '비파괴 판독기': '가동', '나노 자동 제조기': '가동', '함선 AI': '가동', '침투 탐침': '가동', '손에 맞게 닳은 공구': '계속 씀' };
function ctxOf(holdings, checked = '안 됨') {
  return { seed: seed, human: human, sidecar: sidecar(checked), meta: meta, holdings: holdings };
}
function run(ctx, acts) {
  return acts.reduce(function (st, a) { return E.applyAction(st, ctx, a); }, E.start());
}

// ---- 요구 채움: 상태별 ----
const LIFE = ['없음', '미분석', '연구 중', '연구 완료', '가동', '배양 중', '낡음', '고장', '대체됨'];
LIFE.forEach(function (s) {
  test('상태 표기 없는 요구는 가동만 채움: ' + s, function () {
    const f = E.fillRequirement({ name: '함선 AI', state: '' }, meta['함선 AI'], hold({ '함선 AI': s }));
    assert.strictEqual(f.ok, s === '가동');
    assert.strictEqual(f.mount, false);
  });
});
test('낡음은 일반 요구를 못 채우고 이유에 낡음이 적힘', function () {
  const f = E.fillRequirement({ name: '함선 AI', state: '' }, meta['함선 AI'], hold({ '함선 AI': '낡음' }));
  assert.strictEqual(f.ok, false);
  assert.ok(f.reason.includes('낡음'));
});
['연구 중', '낡음', '고장', '대체됨', '배양 중', '미분석', '연구 완료'].forEach(function (s) {
  test('상태 요구는 같은 상태만 채움: ' + s, function () {
    const req = { name: '플라즈마 도관', state: s };
    assert.strictEqual(E.fillRequirement(req, meta['플라즈마 도관'], hold({ '플라즈마 도관': s })).ok, true);
    assert.strictEqual(E.fillRequirement(req, meta['플라즈마 도관'], hold({ '플라즈마 도관': '가동' })).ok, false);
  });
});
test('상태 있는 수단의 요구 상태가 상태 사전에 없으면 길을 숨기지 않고 수단과 상태 이름을 대고 던짐', function () {
  assert.throws(function () {
    E.fillRequirement({ name: '함선 AI', state: '친밀 높음' }, meta['함선 AI'], hold({ '함선 AI': '가동' }));
  }, /함선 AI.*친밀 높음/);
  // 물건이나 재료의 상태 표기는 내력이라 사전 밖이어도 정상
  assert.strictEqual(E.fillRequirement({ name: '둔한 깊이 한 병', state: '다 씀' }, meta['둔한 깊이 한 병'], hold({ '둔한 깊이 한 병': '다 씀' })).ok, true);
});

// ---- 탑재 중 예외 ----
test('탑재 중 요구는 가동이면 채우고 탑재 표시가 붙음', function () {
  const f = E.fillRequirement({ name: '나노 자동 제조기', state: '탑재 중' }, meta['나노 자동 제조기'], hold({ '나노 자동 제조기': '가동' }));
  assert.deepStrictEqual([f.ok, f.mount], [true, true]);
});
['없음', '미분석', '연구 중', '연구 완료', '배양 중', '낡음', '고장', '대체됨'].forEach(function (s) {
  test('탑재 중 요구는 가동이 아니면 못 채움: ' + s, function () {
    assert.strictEqual(E.fillRequirement({ name: '나노 자동 제조기', state: '탑재 중' }, meta['나노 자동 제조기'], hold({ '나노 자동 제조기': s })).ok, false);
  });
});

// ---- 물건 내력 ----
test('물건: 내력이 같으면 채움', function () {
  assert.strictEqual(E.fillRequirement({ name: '손에 맞게 닳은 공구', state: '계속 씀' }, meta['손에 맞게 닳은 공구'], hold({ '손에 맞게 닳은 공구': '계속 씀' })).ok, true);
});
test('물건: 내력이 다르거나 내력 지정이 없으면 못 채움', function () {
  const req = { name: '손에 맞게 닳은 공구', state: '계속 씀' };
  assert.strictEqual(E.fillRequirement(req, meta['손에 맞게 닳은 공구'], hold({ '손에 맞게 닳은 공구': '버림' })).ok, false);
  assert.strictEqual(E.fillRequirement(req, meta['손에 맞게 닳은 공구'], hold({ '손에 맞게 닳은 공구': '있음' })).ok, false);
});
test('물건: 요구에 내력이 없으면 가졌으면 채움', function () {
  const req = { name: '손에 맞게 닳은 공구', state: '' };
  assert.strictEqual(E.fillRequirement(req, meta['손에 맞게 닳은 공구'], hold({ '손에 맞게 닳은 공구': '있음' })).ok, true);
  assert.strictEqual(E.fillRequirement(req, meta['손에 맞게 닳은 공구'], hold({ '손에 맞게 닳은 공구': '계속 씀' })).ok, true);
  assert.strictEqual(E.fillRequirement(req, meta['손에 맞게 닳은 공구'], hold({})).ok, false);
});

// ---- 재료 ----
test('재료: 있음이면 상태 표기 없는 요구를 채우고 없음이면 못 채움', function () {
  const req = { name: '죽은 드론의 부품', state: '' };
  assert.strictEqual(E.fillRequirement(req, meta['죽은 드론의 부품'], hold({ '죽은 드론의 부품': '있음' })).ok, true);
  assert.strictEqual(E.fillRequirement(req, meta['죽은 드론의 부품'], hold({})).ok, false);
});
test('새 수단 표의 갈래가 재료면 재료처럼, 일손이면 상태 수단처럼 다룸', function () {
  assert.strictEqual(E.kindOf(meta['둔한 깊이 한 병']), 'plain');
  assert.strictEqual(E.kindOf(meta['침투 탐침']), 'lifecycle');
  assert.strictEqual(E.kindOf({ name: 'x', cat: '새 수단', galrae: '물건 (직접 만듦)' }), 'plain');
  assert.strictEqual(E.fillRequirement({ name: '둔한 깊이 한 병', state: '' }, meta['둔한 깊이 한 병'], hold({ '둔한 깊이 한 병': '있음' })).ok, true);
});

// ---- 둘 이상의 수단 ----
test('A + B 요구는 둘 다 채워야 하고 모자란 쪽 이름이 이유에 나옴', function () {
  const p = seed.paths[1];
  assert.strictEqual(E.fillPath(p, meta, hold({ '수리 드론 3형': '가동', '죽은 드론의 부품': '있음' })).ok, true);
  const half = E.fillPath(p, meta, hold({ '수리 드론 3형': '가동' }));
  assert.strictEqual(half.ok, false);
  assert.ok(half.parts.find(function (x) { return !x.ok; }).reason.includes('죽은 드론의 부품'));
  assert.strictEqual(E.fillPath(p, meta, hold({ '죽은 드론의 부품': '있음' })).ok, false);
});

// ---- 이름을 대고 실패 ----
test('모르는 갈래, 정보 없는 수단, 가진 것에 없는 수단, 사전에 없는 가진 상태는 이름을 대고 던짐', function () {
  assert.throws(function () { E.kindOf({ name: '이상한 것', cat: '미정의', galrae: '' }); }, /이상한 것/);
  assert.throws(function () { E.kindOf({ name: '새것', cat: '새 수단', galrae: '이상한 갈래' }); }, /이상한 갈래/);
  assert.throws(function () { E.fillPath({ means: [{ name: '없는 수단', state: '' }] }, meta, hold({})); }, /없는 수단/);
  assert.throws(function () { E.fillRequirement({ name: '함선 AI', state: '' }, meta['함선 AI'], {}); }, /함선 AI/);
  assert.throws(function () { E.fillRequirement({ name: '함선 AI', state: '' }, meta['함선 AI'], hold({ '함선 AI': '반쯤 가동' })); }, /반쯤 가동/);
});

// ---- 검토 능력 ----
test('검토 능력: 하나라도 가동이면 열리고, 둘 다 아니면 닫히고, 목록이 비면 영영 안 열림', function () {
  assert.strictEqual(E.reviewOpen(REVIEW2, meta, hold({ '분광 분석기': '가동' })).open, true);
  assert.deepStrictEqual(E.reviewOpen(REVIEW2, meta, hold({ '비파괴 판독기': '가동', '분광 분석기': '가동' })).by, ['비파괴 판독기', '분광 분석기']);
  assert.strictEqual(E.reviewOpen(REVIEW2, meta, hold({ '비파괴 판독기': '낡음' })).open, false);
  assert.strictEqual(E.reviewOpen({ means: [], status: '' }, meta, hold(ABILITY)).open, false);
});
test('검토 수단이 상태 없는 수단이면 던짐', function () {
  assert.throws(function () { E.reviewOpen({ means: ['죽은 드론의 부품'], status: '' }, meta, hold({ '죽은 드론의 부품': '있음' })); }, /죽은 드론의 부품/);
});

// ---- 받는다 / 거절한다, 공개 단계, 씨앗 생애 ----
// 숨긴 글 표지: 사정, 징후, 걸림돌, 맨손, 방치 원문, 길 과정/결과/남는 것 표지, 길 이름(사이드카 별칭이 있으면 원래 이름)
const RAW = ['SIGN1표지', 'SIGN2표지', 'SAJEONG표지', 'OBSTACLE표지', 'BARE표지', 'IGNORE표지'];
const PROCESS = ['P1', 'P2', 'P3', 'P4', 'P5'];
const OUTCOME_MARKS = ['R1', 'R2', 'R3', 'R4', 'R5', 'L1', 'L2', 'L3', 'L4', 'L5'];
function absent(dump, words) {
  words.forEach(function (w) { assert.ok(!dump.includes(w), '새어 나옴: ' + w); });
}
function labelsOf(v) { return v.options.map(function (o) { return o.label; }); }
function texts(st) { return st.log.map(function (l) { return l.text; }); }

test('처음(offer): 질문과 서브 텍스트와 받는다/거절한다뿐이고 받은 뒤의 글과 숨긴 글이 값에 없음', function () {
  const v = E.publicView(E.start(), ctxOf(hold(ABILITY)));
  assert.strictEqual(v.question, human.surface);
  assert.deepStrictEqual(v.lines, human.surface_lines);
  assert.deepStrictEqual(labelsOf(v), ['받는다', '거절한다']);
  assert.strictEqual(v.afterAccept, null);
  assert.strictEqual(v.discovery, null);
  absent(JSON.stringify(v), RAW.concat(PROCESS, OUTCOME_MARKS, ['ACCEPT표지', 'DISCOVERY표지', 'DECLINE표지', '겉으로는', '걸쳐 놓고', '다시 고친다', '새는 불', '가짜로 치른다', '솜씨를 판다', 'ALIAS표지']));
});
test('받는다: after_accept가 뜨고 선택지가 검토한다/그냥 둔다가 되며 일지에 받는다가 남음', function () {
  const c = ctxOf(hold({}));
  const st = run(c, ['accept']);
  const v = E.publicView(st, c);
  assert.strictEqual(v.afterAccept, 'ACCEPT표지');
  assert.deepStrictEqual(labelsOf(v), ['검토한다', '그냥 둔다']);
  assert.deepStrictEqual(texts(st), ['받는다.']);
  assert.strictEqual(st.status, '열림');
});
test('거절한다: 닫힘, 상태 "거절", 본문은 decline_result, 방치 결과는 적용 안 함', function () {
  const c = ctxOf(hold(ABILITY));
  const st = run(c, ['decline']);
  const v = E.publicView(st, c);
  assert.strictEqual(st.status, '거절');
  assert.deepStrictEqual([v.outcome.title, v.outcome.result, v.options.length], ['거절', 'DECLINE표지', 0]);
  assert.deepStrictEqual(texts(st), ['거절한다.', '거절: DECLINE표지']);
  assert.ok(!texts(st).join('\n').includes('방치') && !texts(st).join('\n').includes('IGNORE표지'));
  absent(JSON.stringify(v), RAW.concat(['ACCEPT표지', 'DISCOVERY표지']));
  ['accept', 'decline', 'review', 'ignore', 'close'].forEach(function (a) {
    assert.throws(function () { E.applyAction(st, c, a); }, /닫힌 일/, a);
  });
});
test('받기 전에는 검토, 그냥 둔다, 수동 방치로 닫기가 던지고 길과 맨손도 못 고름. 받은 뒤에는 거절과 두 번 받기가 던짐', function () {
  const c = ctxOf(hold(ABILITY));
  ['review', 'ignore', 'close'].forEach(function (a) {
    assert.throws(function () { E.applyAction(E.start(), c, a); }, /받기 전에는 할 수 없음/, a);
  });
  assert.throws(function () { E.applyAction(E.start(), c, 'take:1'); }, /능력 검토 전/);
  assert.throws(function () { E.applyAction(E.start(), c, 'bare'); }, /능력 검토 전/);
  const st = run(c, ['accept']);
  assert.throws(function () { E.applyAction(st, c, 'decline'); }, /이미 받았음/);
  assert.throws(function () { E.applyAction(st, c, 'accept'); }, /이미 받았음/);
});
test('검토 능력이 없으면 일반 검토: 고정 문구에서 멈추고 숨긴 글도 discovery도 안 보임', function () {
  const c = ctxOf(hold({}));
  const st = run(c, ['accept', 'review']);
  const v = E.publicView(st, c);
  assert.strictEqual(st.level, 1);
  assert.strictEqual(v.general, '겉으로는 잘 되어 있는 것 같다.');
  assert.strictEqual(v.discovery, null);
  assert.deepStrictEqual(labelsOf(v), ['검토한다', '그냥 둔다']);
  absent(JSON.stringify(v), RAW.concat(PROCESS, OUTCOME_MARKS, ['DISCOVERY표지', '걸쳐 놓고', '다시 고친다', '새는 불', '가짜로 치른다', '솜씨를 판다']));
  assert.ok(texts(st).includes('일반 검토: 겉으로는 잘 되어 있는 것 같다.'));
});
test('검토 능력은 review.means 중 하나가 가동일 때뿐: 분광 분석기처럼 목록에 없는 수단은 소용없음', function () {
  const c = ctxOf(hold({ '분광 분석기': '가동' }));
  assert.strictEqual(run(c, ['accept', 'review']).level, 1);
});
test('일반 검토 뒤 능력이 생기면 다시 검토해 능력 검토가 열림', function () {
  const c = ctxOf(hold({}));
  const st1 = run(c, ['accept', 'review']);
  c.holdings = hold(ABILITY);
  const st2 = E.applyAction(st1, c, 'review');
  assert.strictEqual(st2.level, 2);
  assert.ok(texts(st2).includes('능력 검토 성공. (가동: 비파괴 판독기)'));
});
test('능력 검토: discovery 한 덩어리만 보이고 사정, 징후, 걸림돌, 맨손 원문과 길 과정은 안 보이며 채운 길은 이름(별칭 적용)만 선택지가 됨', function () {
  const c = ctxOf(hold(ABILITY));
  const v = E.publicView(run(c, ['accept', 'review']), c);
  assert.strictEqual(v.discovery, 'DISCOVERY표지');
  assert.strictEqual(v.afterAccept, 'ACCEPT표지');
  assert.strictEqual('reveal' in v, false);
  assert.deepStrictEqual(labelsOf(v), ['걸쳐 놓고', 'ALIAS표지', '솜씨를 판다', '맨손으로 버틴다', '그냥 둔다']);
  assert.strictEqual(v.options[0].mount, '하루 더, 본래 쓰임 멈춤: 나노 자동 제조기');
  assert.strictEqual(v.options[1].mount, '');
  assert.ok(v.options.every(function (o) { return Object.keys(o).sort().join() === 'act,label,mount'; }), '선택지에 과정 글 같은 필드가 없음');
  absent(JSON.stringify(v), RAW.concat(PROCESS, OUTCOME_MARKS, ['다시 고친다', '새는 불', '가짜로 치른다']));
});
test('길을 고르면 풀림, 여파에 길 이름(별칭), 과정, 결과, 남는 것이 처음 보이고 일지에도 남음', function () {
  const c = ctxOf(hold(ABILITY));
  const st = run(c, ['accept', 'review', 'take:4']);
  const v = E.publicView(st, c);
  assert.strictEqual(st.status, '풀림');
  assert.deepStrictEqual([v.outcome.title, v.outcome.process, v.outcome.result, v.outcome.left, v.options.length], ['ALIAS표지', 'P4', 'R4', 'L4', 0]);
  ['길을 고른다: ALIAS표지', '과정: P4', '결과: R4', '남는 것: L4'].forEach(function (t) { assert.ok(texts(st).includes(t), t); });
  assert.throws(function () { E.applyAction(st, c, 'ignore'); }, /닫힌 일/);
  assert.throws(function () { E.applyAction(st, c, 'take:1'); }, /닫힌 일/);
});
test('탑재 중 길을 고르면 일지에 하루 더 표시가 남음', function () {
  const st = run(ctxOf(hold(ABILITY)), ['accept', 'review', 'take:1']);
  assert.ok(texts(st).some(function (l) { return l.includes('하루 더, 본래 쓰임 멈춤: 나노 자동 제조기'); }));
});
test('못 채운 길이나 검토 전이나 없는 번호를 고르면 던짐', function () {
  const c = ctxOf(hold(ABILITY));
  const st = run(c, ['accept']);
  assert.throws(function () { E.applyAction(st, c, 'take:1'); }, /검토 전/);
  const st2 = E.applyAction(st, c, 'review');
  assert.throws(function () { E.applyAction(st2, c, 'take:2'); }, /요구를 못 채움.*(죽은 드론의 부품|수리 드론 3형)/);
  assert.throws(function () { E.applyAction(st2, c, 'take:9'); }, /길 번호 9/);
  assert.throws(function () { E.applyAction(st2, c, 'review'); }, /이미 능력 검토/);
});
test('맨손: 능력 검토 뒤에만, 선택지에는 라벨만 있고 맨손 원문은 고른 뒤 여파에서 보임', function () {
  const c = ctxOf(hold(ABILITY));
  const st = run(c, ['accept', 'review', 'bare']);
  assert.strictEqual(st.status, '풀림');
  assert.deepStrictEqual([E.publicView(st, c).outcome.title, E.publicView(st, c).outcome.result], ['맨손으로 버틴다', 'BARE표지']);
  assert.ok(texts(st).includes('맨손: BARE표지'));
});
// 방치는 당할 때까지 알려주지 않는다(사용자 원칙): 닫혀도 플레이 뷰와 일지에는 고정 문구뿐이고 원문과 결 낱말은 작성자 쪽에만 있다.
function assertNeglectHidden(c, st, status) {
  assert.strictEqual(st.status, status);
  const pub = texts(st).join('\n');
  const view = E.publicView(st, c);
  absent(pub + JSON.stringify(view), ['IGNORE표지', '나빠짐', 'SAJEONG표지']);
  assert.ok(pub.includes(E.NEGLECT_TEXT), '일지에 고정 문구가 없음');
  assert.strictEqual(view.outcome.result.includes(E.NEGLECT_TEXT), true);
  assert.strictEqual(view.outcome.dim, true);
  assert.strictEqual(st.log[st.log.length - 1].dim, true, '일지의 닫힘 줄은 흐리게');
  const au = st.log.map(function (l) { return l.author; }).join('\n');
  assert.ok(au.includes(E.NEGLECT_NOTE_LABEL) && au.includes('IGNORE표지 전체 본문') && au.includes('방치 결: 나빠짐'), '작성자 줄에 방치 원문이 없음');
  assert.deepStrictEqual(E.neglectNote(st, c), { label: E.NEGLECT_NOTE_LABEL, tone: '나빠짐', text: 'IGNORE표지 전체 본문' });
}
test('길이 공개되기 전에 닫으면 놓침(못 알아봄): 방치 원문, 결 낱말, 사정이 뷰와 일지에 없고 고정 문구만, 사정과 원문은 작성자 줄에만', function () {
  const c = ctxOf(hold({}));
  ['ignore', 'close'].forEach(function (act) {
    [['accept', act], ['accept', 'review', act]].forEach(function (acts) {
      const st = run(c, acts);
      assertNeglectHidden(c, st, '놓침');
      assert.ok(texts(st).join('\n').includes('무엇이었는지 모른 채 닫힘.'));
      assert.ok(st.log.some(function (l) { return l.author.includes('사정: SAJEONG표지'); }));
    });
  });
});
test('길이 공개된 뒤 아무것도 안 고르고 닫아도(그냥 둔다, 수동 닫기 둘 다) 방치 원문과 결 낱말은 안 보이고 고정 문구만', function () {
  const c = ctxOf(hold(ABILITY));
  const st = run(c, ['accept', 'review', 'ignore']);
  assertNeglectHidden(c, st, '방치');
  assert.ok(texts(st).includes('방치: ' + E.NEGLECT_TEXT));
  const manual = run(c, ['accept', 'review', 'close']);
  assertNeglectHidden(c, manual, '방치');
  assert.ok(texts(manual).includes('방치로 닫기(수동).'));
});
test('고정 문구는 엔진 상수이고 씨앗마다 다르지 않으며, 방치 원문 메모는 방치나 놓침일 때만 있음', function () {
  assert.strictEqual(E.NEGLECT_TEXT, '그냥 두고 지나간다. 무슨 일이 생길지는 아직 모른다.');
  const c = ctxOf(hold(ABILITY));
  [[], ['accept'], ['accept', 'review'], ['accept', 'review', 'take:1'], ['accept', 'review', 'bare'], ['decline']].forEach(function (acts) {
    assert.strictEqual(E.neglectNote(run(c, acts), c), null, acts.join(','));
  });
});
test('닫힘 해석(사용자 판정 대기): 능력 검토가 열렸는데 채운 길이 0개일 때 닫으면 방치. 계획서 3.2의 "길이 하나도 공개되지 않은 채 닫힘 = 못 알아봄"과 글자 그대로는 다르게 읽힘', function () {
  const c = ctxOf(hold({ '비파괴 판독기': '가동' }));
  const st = run(c, ['accept', 'review']);
  assert.deepStrictEqual(labelsOf(E.publicView(st, c)), ['맨손으로 버틴다', '그냥 둔다']);
  assert.strictEqual(E.applyAction(st, c, 'ignore').status, '방치');
});
test('거절 해석(사용자 판정 대기): 거절은 방치로 치지 않고 해가 없는 닫힘이며 못 알아봄과도 다름', function () {
  const c = ctxOf(hold({}));
  const st = run(c, ['decline']);
  assert.notStrictEqual(st.status, '방치');
  assert.notStrictEqual(st.status, '놓침');
});
test('상태는 값이라 행동해도 이전 상태가 안 바뀜', function () {
  const c = ctxOf(hold(ABILITY));
  const st0 = E.start();
  E.applyAction(st0, c, 'accept');
  E.applyAction(st0, c, 'decline');
  assert.deepStrictEqual(st0, { status: '열림', accepted: false, level: 0, chosen: null, log: [] });
});

// ---- 효과층: 검토가 됨인 것만 씀 ----
test('효과는 검토가 됨일 때만 일지에 쓰이고 안 됨이나 보류면 길 본문만 남음', function () {
  ['안 됨', '보류'].forEach(function (chk) {
    const c = ctxOf(hold(ABILITY), chk);
    assert.ok(!run(c, ['accept', 'review', 'take:1']).log.some(function (l) { return l.text.startsWith('효과'); }), chk);
  });
  const done = run(ctxOf(hold(ABILITY), '됨'), ['accept', 'review', 'take:1']);
  assert.ok(texts(done).includes('효과: 얻음: 물체 정거장의 알갱이'));
});
test('닫힌 목록 밖 효과 낱말은 던짐', function () {
  assert.throws(function () { E.describeEffect({ '효과': '창조' }); }, /창조/);
});
test('효과 검토가 됨인데 길의 효과 목록이 없으면 길 이름을 대고 던짐', function () {
  assert.throws(function () { E.usableEffects({ checked: '됨', effects: {} }, '없는 길'); }, /없는 길/);
  assert.deepStrictEqual(E.usableEffects({ checked: '안 됨', effects: {} }, '없는 길'), []);
});

// ---- 작성자 보기, 프리셋, 주소 ----
test('작성자 보기 값: 길 전부의 요구와 채움 여부, 안 채운 길은 모자란 것을 이름으로 댐', function () {
  const a = E.authorView(ctxOf(hold(ABILITY)));
  assert.deepStrictEqual(a.paths.map(function (p) { return p.ok; }), [true, false, false, true, true]);
  assert.strictEqual(a.paths[1].requirement, '수리 드론 3형 + 죽은 드론의 부품');
  assert.strictEqual(a.paths[0].requirement, '나노 자동 제조기: 탑재 중');
  assert.ok(a.paths[2].parts[0].reason.includes('고장'));
  assert.strictEqual(a.review.open, true);
});
const data = {
  panel: Object.keys(meta), meta: meta, tags: { '죽은 드론의 부품': [], '손에 맞게 닳은 공구': ['계속 씀'] },
  kit: ['분광 분석기', '죽은 드론의 부품', '손에 맞게 닳은 공구'], ability: ABILITY,
};
Object.keys(meta).forEach(function (n) { if (!(n in data.tags)) data.tags[n] = []; });
test('프리셋: none은 전부 없음, kit은 출발 장비만(상태 수단은 가동, 재료와 물건은 있음), ability는 지정한 것만', function () {
  assert.ok(Object.values(E.presetHoldings('none', data)).every(function (v) { return v === '없음'; }));
  const k = E.presetHoldings('kit', data);
  assert.deepStrictEqual([k['분광 분석기'], k['죽은 드론의 부품'], k['손에 맞게 닳은 공구'], k['함선 AI']], ['가동', '있음', '있음', '없음']);
  const a = E.presetHoldings('ability', data);
  assert.deepStrictEqual([a['비파괴 판독기'], a['손에 맞게 닳은 공구'], a['수리 드론 3형']], ['가동', '계속 씀', '없음']);
});
test('프리셋: 모르는 이름이나 패널 밖 이름이나 고를 수 없는 값은 던짐', function () {
  assert.throws(function () { E.presetHoldings('몰라', data); }, /몰라/);
  assert.throws(function () { E.presetHoldings('ability', Object.assign({}, data, { ability: { '없는 수단': '가동' } })); }, /없는 수단/);
  assert.throws(function () { E.presetHoldings('ability', Object.assign({}, data, { ability: { '함선 AI': '계속 씀' } })); }, /계속 씀/);
});
test('주소 조각: 프리셋과 행동 순서를 읽고 모르는 키나 모양은 던짐', function () {
  assert.deepStrictEqual(E.parseLink('#preset=ability&do=review,take:2,close,ignore'), { preset: 'ability', actions: ['review', 'take:2', 'close', 'ignore'], author: false });
  assert.deepStrictEqual(E.parseLink(''), { preset: 'none', actions: [], author: false });
  assert.strictEqual(E.parseLink('#author=1').author, true);
  assert.throws(function () { E.parseLink('#preset'); }, /=가 없음/);
  assert.throws(function () { E.parseLink('#presets=kit'); }, /presets/);
  assert.throws(function () { E.parseLink('#author=2'); }, /author/);
});
test('모르는 행동 이름은 던짐', function () {
  assert.throws(function () { E.applyAction(E.start(), ctxOf(hold({})), 'dance'); }, /dance/);
  assert.throws(function () { E.applyAction(E.start(), ctxOf(hold({})), 'take:'); }, /take:/);
});

test('가진 것 패널: 플레이 보기는 검토 능력 수단만, 작성자 보기는 길 요구 수단까지', function () {
  const d = Object.assign({ human: human }, data);
  assert.deepStrictEqual(E.panelNames(d, false), ['비파괴 판독기']);
  assert.deepStrictEqual(E.panelNames(d, true), d.panel);
});

// ---- 실제 내보낸 데이터(seedgame.html의 DATA)로 판 ----
const fs = require('fs');
const path = require('path');
let built = null;
function loadBuilt() {
  const htmlPath = path.join(__dirname, 'seedgame.html');
  if (!fs.existsSync(htmlPath)) throw new Error('seedgame.html이 없음: 먼저 build (python seedgame.py build)');
  const html = fs.readFileSync(htmlPath, 'utf8');
  if (!html.includes(fs.readFileSync(path.join(__dirname, 'engine.js'), 'utf8'))) {
    throw new Error('seedgame.html에 든 엔진이 engine.js와 다름: 먼저 build');
  }
  const at = html.indexOf('const DATA = ');
  if (at < 0) throw new Error('seedgame.html에서 DATA를 못 찾음');
  // seedgame.py가 "<!--"를 이스케이프한 것을 되돌린다. "<\/"는 JSON이 그대로 읽는다.
  const blob = html.slice(at + 'const DATA = '.length, html.indexOf('\n', at)).replace(/;$/, '').split('<\\!--').join('<!--');
  return JSON.parse(blob);
}
function realCtx(presetName) {
  if (built === null) throw new Error('seedgame.html을 못 읽음: 먼저 build');
  return { seed: built.seed, human: built.human, sidecar: built.sidecar, meta: built.meta, holdings: E.presetHoldings(presetName, built) };
}
// 사용자가 "이 말이 나오면 사정을 알아 버린다"고 한 낱말과 원문(능력 검토 때 이야기 한 덩어리만 보여야 함)
const REAL_BANNED = ['함대', '심부름', '청구서', '좌표를 바꾼다'];
function realHidden(c) {
  return [c.seed.sajeong, c.seed.sign1, c.seed.sign2, c.seed.obstacle, c.seed.bare, c.seed.ignore]
    .concat(REAL_BANNED, c.seed.paths.map(function (p) { return p.process; }), c.seed.paths.map(function (p) { return p.result; }), c.seed.paths.map(function (p) { return p.left; }));
}
test('실제 데이터: seedgame.html을 읽고 플레이 가능, 사이드카 정상, 길 별칭 키가 길 이름과 같음', function () {
  built = loadBuilt();
  assert.strictEqual(built.playable, true);
  assert.strictEqual(built.sidecar.state, '정상');
  assert.strictEqual(built.seed.paths.length, 5);
  assert.deepStrictEqual(built.human.review.means, ['비파괴 판독기']);
  Object.keys(built.human.path_labels).forEach(function (k) {
    assert.ok(built.seed.paths.some(function (p) { return p.name === k; }), '별칭 키가 길 이름과 다름: ' + k);
  });
});
test('실제 데이터 처음: 받는다/거절한다뿐이고 받은 뒤의 글과 숨긴 글이 view에 없음', function () {
  const c = realCtx('ability');
  const v = E.publicView(E.start(), c);
  assert.deepStrictEqual(labelsOf(v), ['받는다', '거절한다']);
  assert.strictEqual(v.question, '공짜로 수리해 준다고 한다. 왜 그런 거지?');
  const dump = JSON.stringify(v);
  absent(dump, realHidden(c).concat([c.human.after_accept, c.human.discovery, c.seed.paths.map(function (p) { return p.name; })].flat()));
});
test('실제 데이터 거절: 닫힘, 거절, decline_result, 방치 줄이 일지에 없음', function () {
  const c = realCtx('none');
  const st = run(c, ['decline']);
  assert.strictEqual(st.status, '거절');
  assert.strictEqual(E.publicView(st, c).outcome.result, c.human.decline_result);
  assert.ok(!texts(st).join('\n').includes(c.seed.ignore));
});
test('실제 데이터 none: 받고 검토하면 일반 검토 문구에서 멈추고 숨긴 글이 view에 없음', function () {
  const c = realCtx('none');
  const st = run(c, ['accept', 'review']);
  const v = E.publicView(st, c);
  assert.strictEqual(st.level, 1);
  assert.strictEqual(v.general, '겉으로는 잘 되어 있는 것 같다.');
  assert.deepStrictEqual(labelsOf(v), ['검토한다', '그냥 둔다']);
  absent(JSON.stringify(v), realHidden(c).concat([c.human.discovery], c.seed.paths.map(function (p) { return p.name; })));
});
test('실제 데이터 ability: 받고 검토하면 discovery와 3길(별칭)과 맨손 라벨뿐이고 사정 글과 과정 글과 "함대" 같은 낱말이 없음', function () {
  const c = realCtx('ability');
  const st = run(c, ['accept', 'review']);
  const v = E.publicView(st, c);
  assert.strictEqual(st.level, 2);
  assert.strictEqual(v.discovery, c.human.discovery);
  assert.deepStrictEqual(labelsOf(v), ['걸쳐 놓고 속만 가른다', '알갱이에 가짜 신호를 먹인다', '정거장에 내 솜씨를 판다', '맨손으로 버틴다', '그냥 둔다']);
  assert.strictEqual(v.options[0].mount, '하루 더, 본래 쓰임 멈춤: 나노 자동 제조기');
  const dump = JSON.stringify(v);
  absent(dump, realHidden(c).concat(['고친 것을 다시 고친다', '새는 불로 지진다', '심부름을 가짜로 치른다']));
  // 일지도 마찬가지(고르기 전)
  absent(texts(st).join('\n'), realHidden(c));
});
test('실제 데이터 ability 길 선택: 고른 뒤 여파에 별칭 이름과 과정과 결과와 남는 것이 처음 보임', function () {
  const c = realCtx('ability');
  const st = run(c, ['accept', 'review', 'take:4']);
  const o = E.publicView(st, c).outcome;
  const p = c.seed.paths[3];
  assert.deepStrictEqual([o.title, o.process, o.result, o.left], ['알갱이에 가짜 신호를 먹인다', p.process, p.result, p.left]);
  assert.ok(p.process.length > 0 && p.result.length > 0 && p.left.length > 0);
  assert.ok(texts(st).includes('과정: ' + p.process));
});
test('실제 데이터 kit: 출발 장비에는 비파괴 판독기가 없어 받고 검토해도 일반 검토에서 멈춤', function () {
  const c = realCtx('kit');
  const st = run(c, ['accept', 'review']);
  assert.strictEqual(st.level, 1);
  assert.strictEqual(E.publicView(st, c).general, '겉으로는 잘 되어 있는 것 같다.');
  assert.strictEqual(E.reviewOpen(c.human.review, c.meta, c.holdings).open, false);
});
test('실제 데이터: 능력 검토가 열렸는데 채운 길이 0개이면 맨손과 그냥 둔다뿐이고 닫으면 방치', function () {
  const c = realCtx('none');
  c.holdings = Object.assign({}, c.holdings, { '비파괴 판독기': '가동' });
  const st = run(c, ['accept', 'review']);
  assert.deepStrictEqual(labelsOf(E.publicView(st, c)), ['맨손으로 버틴다', '그냥 둔다']);
  assert.strictEqual(E.applyAction(st, c, 'ignore').status, '방치');
});
const NEGLECT_WORDS = ['좌표를 바꾼다', '정거장이 고른 곳', '나빠짐', '알갱이를 둔 채'];
[['ability', ['accept', 'review', 'ignore'], '방치'], ['none', ['accept', 'review', 'close'], '놓침'], ['none', ['accept', 'ignore'], '놓침']].forEach(function (g) {
  test('실제 데이터 ' + g[0] + ' ' + g[1].join(','), function () {
    const c = realCtx(g[0]);
    const st = run(c, g[1]);
    assert.strictEqual(st.status, g[2]);
    const shown = texts(st).join('\n') + JSON.stringify(E.publicView(st, c));
    absent(shown, NEGLECT_WORDS.concat([c.seed.ignore]));
    assert.ok(shown.includes(E.NEGLECT_TEXT));
    const note = E.neglectNote(st, c);
    assert.strictEqual(note.text, c.seed.ignore);
    NEGLECT_WORDS.forEach(function (w) { assert.ok(note.text.includes(w) || note.tone === w, '작성자 메모에 없음: ' + w); });
    assert.ok(st.log.map(function (l) { return l.author; }).join('\n').includes(c.seed.ignore));
  });
});
test('실제 데이터: 플레이 보기 패널에는 검토 능력 수단만, 길 요구 수단 이름은 하나도 없음', function () {
  if (built === null) throw new Error('seedgame.html을 못 읽음: 먼저 build');
  const shown = E.panelNames(built, false);
  assert.deepStrictEqual(shown, built.human.review.means);
  ['플라즈마 도관', '수리 드론 3형', '죽은 드론의 부품', '나노 자동 제조기', '함선 AI', '침투 탐침', '손에 맞게 닳은 공구']
    .forEach(function (n) { assert.ok(!shown.includes(n), '플레이 보기에 보임: ' + n); });
  assert.ok(E.panelNames(built, true).includes('플라즈마 도관'));
});
test('실제 데이터: 받기 전에는 검토, 그냥 둔다, 수동 닫기, 주소 행동 close가 이름 대고 던짐', function () {
  const c = realCtx('ability');
  ['review', 'ignore', 'close'].forEach(function (a) {
    assert.throws(function () { E.applyAction(E.start(), c, a); }, /받기 전에는 할 수 없음/, a);
  });
});

console.log(passed + '개 통과, ' + failed + '개 실패');
process.exit(failed ? 1 : 0);
