// 씨앗 게임 엔진 (1단계). 규칙만 있고 화면이 없다. 브라우저에 끼워 넣어도, node로 불러도 돈다. 의존성 없음.
// 시계, 타이머, 시간 비용은 1단계에 없다. 상태는 값(JSON)이고 행동은 새 상태를 돌려준다.
// presetHoldings와 panelNames가 다루는 가진 것 패널과 프리셋은 1단계 시험 장치다(2단계의 능력 목록 화면이 아님).
(function (root, factory) {
  const engine = factory();
  if (typeof module === 'object' && module.exports) module.exports = engine;
  else root.Engine = engine;
})(this, function () {
  'use strict';

  // 상태가 있는 수단(테크, 일손, 기르는 것 등)이 가질 수 있는 꼴. seeddata.py의 STATES와 같아야 한다(build가 대조함).
  const LIFECYCLE = ['없음', '미분석', '연구 중', '연구 완료', '가동', '배양 중', '낡음', '고장', '대체됨'];
  const GENERAL_REVIEW_TEXT = '겉으로는 잘 되어 있는 것 같다.';
  const MOUNT_NOTE = '하루 더, 본래 쓰임 멈춤';
  const BARE_LABEL = '맨손으로 버틴다';
  const PLAIN_CATS = ['재료', '물건', '물체'];
  const LIFECYCLE_CATS = ['추진', '제조', '에너지', '감지', '몸', '일손', '사이오닉', '기르는 것', '풀 후보'];
  const LIFECYCLE_GALRAE = ['테크', '일손', '기르는 것'];

  function metaOf(name, meta) {
    if (!Object.prototype.hasOwnProperty.call(meta, name)) throw new Error('수단 정보에 "' + name + '"이 없음');
    return meta[name];
  }

  function holdingOf(name, holdings) {
    if (!Object.prototype.hasOwnProperty.call(holdings, name)) throw new Error('가진 것 목록에 "' + name + '"이 없음');
    return holdings[name];
  }

  // 'lifecycle'은 가동 같은 상태가 있는 수단, 'plain'은 재료나 물건처럼 있고 없고(와 물건의 내력)만 있는 수단.
  function kindOf(meta) {
    if (PLAIN_CATS.includes(meta.cat)) return 'plain';
    if (LIFECYCLE_CATS.includes(meta.cat)) return 'lifecycle';
    if (meta.cat === '새 수단') {
      if (meta.galrae === '재료' || meta.galrae.startsWith('물건')) return 'plain';
      if (LIFECYCLE_GALRAE.includes(meta.galrae)) return 'lifecycle';
      throw new Error('새 수단 "' + meta.name + '"의 갈래 "' + meta.galrae + '"를 모름');
    }
    throw new Error('수단 "' + meta.name + '"의 갈래 "' + meta.cat + '"를 모름');
  }

  // 가진 것 선택지. plain은 없음, 있음(내력 지정 없음), 그리고 모델 전체 씨앗에 나오는 내력들.
  function holdOptions(meta, tags) {
    return kindOf(meta) === 'lifecycle' ? LIFECYCLE.slice() : ['없음', '있음'].concat(tags);
  }

  // 요구 하나(수단 + 상태 표기)가 지금 가진 것으로 채워지나.
  function fillRequirement(req, meta, holdings) {
    const kind = kindOf(meta);
    const held = holdingOf(req.name, holdings);
    if (kind === 'plain') {
      if (req.state === '') {
        return held !== '없음' ? { ok: true, mount: false } : { ok: false, mount: false, reason: req.name + ': 가진 것이 없음' };
      }
      return held === req.state
        ? { ok: true, mount: false }
        : { ok: false, mount: false, reason: req.name + ': 내력 "' + req.state + '" 필요, 지금 "' + held + '"' };
    }
    if (!LIFECYCLE.includes(held)) throw new Error('"' + req.name + '"의 가진 상태 "' + held + '"가 상태 사전에 없음');
    if (req.state === '' || req.state === '탑재 중') {
      // 탑재 중 요구는 가동이면 채운다. 고르면 하루가 더 들고 그동안 본래 쓰임이 멈춘다.
      return held === '가동'
        ? { ok: true, mount: req.state === '탑재 중' }
        : { ok: false, mount: false, reason: req.name + ': 가동 필요, 지금 ' + held };
    }
    if (!LIFECYCLE.includes(req.state)) {
      // 다 자람, 친밀 높음 같은 꼬리표는 상태 사전에 없다. 길을 조용히 숨기지 않고 이름을 대고 멈춘다(seeddata.py의 후처리 실패 목록 ⑥이 미리 셈).
      throw new Error('수단 "' + req.name + '"의 요구 상태 "' + req.state + '"는 1단계 상태 사전에 없음');
    }
    return held === req.state
      ? { ok: true, mount: false }
      : { ok: false, mount: false, reason: req.name + ': ' + req.state + ' 필요, 지금 ' + held };
  }

  // 길 하나. 요구가 둘 이상(A + B)이면 전부 채워야 한다.
  function fillPath(path, meta, holdings) {
    const parts = path.means.map(function (r) {
      const f = fillRequirement(r, metaOf(r.name, meta), holdings);
      return { name: r.name, state: r.state, ok: f.ok, reason: f.ok ? '' : f.reason, mount: f.ok && f.mount };
    });
    return {
      ok: parts.every(function (p) { return p.ok; }),
      parts: parts,
      mounts: parts.filter(function (p) { return p.mount; }).map(function (p) { return p.name; }),
    };
  }

  // 검토 능력: review.means 중 하나라도 가동이면 열린다. 목록이 비면 열리지 않는다(막힘이 기본).
  function reviewOpen(review, meta, holdings) {
    const on = review.means.filter(function (n) {
      if (kindOf(metaOf(n, meta)) !== 'lifecycle') throw new Error('검토 수단 "' + n + '"은 상태가 있는 수단이어야 함');
      return holdingOf(n, holdings) === '가동';
    });
    return { open: on.length > 0, by: on };
  }

  function requirementText(path) {
    return path.means.map(function (r) { return r.state ? r.name + ': ' + r.state : r.name; }).join(' + ');
  }

  // 게임은 검토가 "됨"인 효과만 쓴다. 안 됨과 보류는 초안일 뿐이다.
  function usableEffects(sidecar, pathName) {
    if (sidecar.checked !== '됨') return [];
    if (!Array.isArray(sidecar.effects[pathName])) throw new Error('사이드카 effects에 길 "' + pathName + '"가 없음');
    return sidecar.effects[pathName];
  }

  function describeEffect(e) {
    switch (e['효과']) {
      case '얻음': return '얻음: ' + e['종류'] + ' ' + e['이름'];
      case '잃음': return '잃음: ' + e['종류'] + ' ' + e['이름'];
      case '플래그': return '플래그: ' + e['이름'];
      case '게이지': return '게이지: ' + e['축'] + ' ' + (e['증감'] > 0 ? '+' : '') + e['증감'];
      case '후속 예약': return '후속 예약: ' + e['씨앗'];
      case '상태 바뀜': return '상태 바뀜: ' + e['이름'] + ' → ' + e['상태'];
      case '내력 바뀜': return '내력 바뀜: ' + e['이름'] + ' → ' + e['내력'];
      default: throw new Error('모르는 효과 낱말 "' + e['효과'] + '"');
    }
  }

  function entry(text, author = '') {
    return { text: text, author: author };
  }

  function mustBeOpen(st, what) {
    if (st.status !== '열림') throw new Error(what + ': 이미 닫힌 일임(' + st.status + ')');
  }

  // 받기 전(offer)에는 받는다와 거절한다만 된다. 검토, 그냥 둔다, 방치로 닫기는 받은 뒤에만.
  function mustBeAccepted(st, what) {
    mustBeOpen(st, what);
    if (!st.accepted) throw new Error(what + ': 받기 전에는 할 수 없음 (받기 전에는 방치가 일어나지 않음)');
  }

  function start() {
    return { status: '열림', accepted: false, level: 0, chosen: null, log: [] };
  }

  function accept(st) {
    mustBeOpen(st, '받는다');
    if (st.accepted) throw new Error('받는다: 이미 받았음');
    return Object.assign({}, st, { accepted: true, log: st.log.concat([entry('받는다.')]) });
  }

  // 거절한다 = 닫힘, 상태 "거절". 받지 않았으니 방치 결과는 적용하지 않는다(해가 없음).
  // 해석 주의: 거절을 방치와 같게 보지 않고 해가 없다고 보는 것은 사용자 판정 대기.
  function decline(st, ctx) {
    mustBeOpen(st, '거절한다');
    if (st.accepted) throw new Error('거절한다: 이미 받았음');
    return Object.assign({}, st, {
      status: '거절',
      log: st.log.concat([entry('거절한다.'), entry('거절: ' + ctx.human.decline_result)]),
    });
  }

  // 검토한다(받은 뒤에만). 능력이 있으면 능력 검토(level 2), 없으면 일반 검토(level 1, 닫히기 전에는 다시 검토할 수 있음).
  function review(st, ctx) {
    mustBeAccepted(st, '검토');
    if (st.level === 2) throw new Error('검토: 이미 능력 검토까지 열렸음');
    const r = reviewOpen(ctx.human.review, ctx.meta, ctx.holdings);
    const log = st.log.concat([entry('검토한다.')]);
    if (r.open) {
      log.push(entry('능력 검토 성공. (가동: ' + r.by.join(', ') + ')'));
      return Object.assign({}, st, { level: 2, log: log });
    }
    log.push(entry('일반 검토: ' + GENERAL_REVIEW_TEXT, '검토 능력 중 가동인 것이 없음: ' + (ctx.human.review.means.join(', ') || '(목록이 빔)')));
    return Object.assign({}, st, { level: 1, log: log });
  }

  // 닫는다(받은 뒤에만). 길이 하나도 공개되지 않은 채(level < 2) 닫으면 놓침(못 알아봄), 공개된 뒤면 방치.
  // 해석 주의: 능력 검토가 열려 이야기와 맨손이 보였으면 채운 길이 0개여도 공개된 것으로 보아 방치로 닫는다.
  // 계획서 3.2 정의("길이 하나도 공개되지 않은 채 닫힘 = 못 알아봄")와 글자 그대로는 다르게 읽힘, 사용자 판정 대기.
  function ignore(st, ctx, by) {
    mustBeAccepted(st, by);
    const log = st.log.concat([entry(by + '.')]);
    if (st.level < 2) {
      log.push(entry('놓침(못 알아봄): 무엇이었는지 모른 채 닫힘. 방치 결: ' + ctx.seed.tone + '.', '사정: ' + ctx.seed.sajeong));
      return Object.assign({}, st, { status: '놓침', log: log });
    }
    log.push(entry('방치: ' + ctx.seed.ignore));
    return Object.assign({}, st, { status: '방치', log: log });
  }

  // 플레이 보기에서 길 이름. 사정을 흘리는 이름은 사이드카 path_labels가 바꿔 보인다(표에 없는 길은 원래 이름).
  function pathLabel(ctx, path) {
    return Object.prototype.hasOwnProperty.call(ctx.human.path_labels, path.name) ? ctx.human.path_labels[path.name] : path.name;
  }

  // n은 씨앗 안의 길 번호(1부터). 화면은 번호를 보이지 않고, 주소 조각과 작성자 보기가 쓴다.
  function take(st, ctx, n) {
    mustBeOpen(st, '길 고르기');
    if (st.level < 2) throw new Error('길 고르기: 능력 검토 전이라 길이 안 보임');
    if (!Number.isInteger(n) || n < 1 || n > ctx.seed.paths.length) {
      throw new Error('길 번호 ' + n + '은 1~' + ctx.seed.paths.length + '이어야 함');
    }
    const path = ctx.seed.paths[n - 1];
    const f = fillPath(path, ctx.meta, ctx.holdings);
    if (!f.ok) {
      throw new Error('길 ' + n + ' "' + path.name + '": 요구를 못 채움 (' +
        f.parts.filter(function (p) { return !p.ok; }).map(function (p) { return p.reason; }).join('; ') + ')');
    }
    const log = st.log.concat([
      entry('길을 고른다: ' + pathLabel(ctx, path) + (f.mounts.length ? ' (' + MOUNT_NOTE + ': ' + f.mounts.join(', ') + ')' : '')),
      entry('과정: ' + path.process),
      entry('결과: ' + path.result),
      entry('남는 것: ' + path.left),
    ]);
    usableEffects(ctx.sidecar, path.name).forEach(function (e) { log.push(entry('효과: ' + describeEffect(e))); });
    return Object.assign({}, st, { status: '풀림', chosen: { kind: 'path', index: n - 1 }, log: log });
  }

  function bare(st, ctx) {
    mustBeOpen(st, '맨손');
    if (st.level < 2) throw new Error('맨손: 능력 검토 전이라 안 보임');
    return Object.assign({}, st, {
      status: '풀림',
      chosen: { kind: 'bare' },
      log: st.log.concat([entry(BARE_LABEL + '.'), entry('맨손: ' + ctx.seed.bare)]),
    });
  }

  // 주소 조각의 행동 이름 하나를 실행한다.
  function applyAction(st, ctx, act) {
    if (act === 'accept') return accept(st);
    if (act === 'decline') return decline(st, ctx);
    if (act === 'review') return review(st, ctx);
    if (act === 'ignore') return ignore(st, ctx, '그냥 둔다');
    if (act === 'close') return ignore(st, ctx, '방치로 닫기(수동)');
    if (act === 'bare') return bare(st, ctx);
    const m = /^take:(\d+)$/.exec(act);
    if (m) return take(st, ctx, Number(m[1]));
    throw new Error('모르는 행동 "' + act + '" (accept, decline, review, ignore, close, bare, take:<길 번호>)');
  }

  // 플레이 보기. 공개 단계가 허락한 글만 담는다. 화면은 이 값만 그리므로 숨긴 글은 DOM에 갈 길이 없다.
  // 선택 전 뷰에는 사정, 징후, 걸림돌, 맨손 원문과 길의 과정 글을 담지 않는다. 능력 검토에서는 사람이 쓴 discovery 한 덩어리만 보인다.
  // 길의 과정과 결과, 맨손 원문, 방치 원문은 고르거나 닫은 뒤 outcome에서 처음 보인다(선택의 결과라서. 방치 원문은 길이 공개된 뒤
  // 닫을 때만 담기고, 길이 하나도 공개되지 않은 채 닫히면 방치 결 낱말만 담김). 이 해석은 사용자 판정 대기다.
  function publicView(st, ctx) {
    const human = ctx.human;
    const open = st.status === '열림';
    const v = {
      status: st.status, level: st.level, accepted: st.accepted, open: open,
      question: human.surface, lines: human.surface_lines,
      afterAccept: st.accepted ? human.after_accept : null,
      general: st.level === 1 ? GENERAL_REVIEW_TEXT : null,
      discovery: st.level === 2 ? human.discovery : null,
      options: [], outcome: null,
    };
    if (open && !st.accepted) {
      v.options.push({ act: 'accept', label: '받는다', mount: '' }, { act: 'decline', label: '거절한다', mount: '' });
    } else if (open) {
      if (st.level === 2) {
        ctx.seed.paths.forEach(function (p, i) {
          const f = fillPath(p, ctx.meta, ctx.holdings);
          if (f.ok) v.options.push({ act: 'take:' + (i + 1), label: pathLabel(ctx, p), mount: f.mounts.length ? MOUNT_NOTE + ': ' + f.mounts.join(', ') : '' });
        });
        v.options.push({ act: 'bare', label: BARE_LABEL, mount: '' });
      } else {
        v.options.push({ act: 'review', label: '검토한다', mount: '' });
      }
      v.options.push({ act: 'ignore', label: '그냥 둔다', mount: '' });
    } else if (st.status === '풀림') {
      const p = ctx.seed.paths[st.chosen.index];
      v.outcome = st.chosen.kind === 'bare'
        ? { title: BARE_LABEL, process: '', result: ctx.seed.bare, left: '' }
        : { title: pathLabel(ctx, p), process: p.process, result: p.result, left: p.left };
    } else if (st.status === '방치') {
      v.outcome = { title: '방치', process: '', result: ctx.seed.ignore, left: '' };
    } else if (st.status === '놓침') {
      v.outcome = { title: '못 알아봄', process: '', result: '무엇이었는지 모른 채 닫힘. 방치 결: ' + ctx.seed.tone + '.', left: '' };
    } else if (st.status === '거절') {
      v.outcome = { title: '거절', process: '', result: human.decline_result, left: '' };
    } else {
      throw new Error('모르는 상태 "' + st.status + '"');
    }
    return v;
  }

  // 가진 것 패널에 그릴 수단. 플레이 보기는 검토 능력 수단만, 작성자 보기는 길 요구에 나오는 수단 전부.
  // 길 요구의 수단 이름은 숨긴 길을 가리키므로 공개 단계 전에는 플레이 보기에 그리지 않는다.
  function panelNames(data, author) {
    return author ? data.panel : data.human.review.means;
  }

  // 작성자 보기에 쓰는 값: 길 전부의 요구와 채움 여부, 검토 능력이 지금 열리나.
  function authorView(ctx) {
    return {
      paths: ctx.seed.paths.map(function (p, i) {
        const f = fillPath(p, ctx.meta, ctx.holdings);
        return { n: i + 1, name: p.name, label: pathLabel(ctx, p), requirement: requirementText(p), ok: f.ok, parts: f.parts, mounts: f.mounts };
      }),
      review: Object.assign({ means: ctx.human.review.means }, reviewOpen(ctx.human.review, ctx.meta, ctx.holdings)),
    };
  }

  // 프리셋 이름 하나를 가진 것 전체(패널의 모든 수단)로 바꾼다. data는 DATA(panel, meta, tags, kit, ability).
  function presetHoldings(name, data) {
    const out = {};
    data.panel.forEach(function (n) { out[n] = '없음'; });
    if (name === 'none') return out;
    if (name === 'kit') {
      data.panel.forEach(function (n) {
        if (data.kit.includes(n)) out[n] = kindOf(metaOf(n, data.meta)) === 'lifecycle' ? '가동' : '있음';
      });
      return out;
    }
    if (name === 'ability') {
      Object.keys(data.ability).forEach(function (n) {
        if (!data.panel.includes(n)) throw new Error('프리셋 능력 있음 예시의 "' + n + '"이 패널에 없음');
        if (!holdOptions(metaOf(n, data.meta), data.tags[n]).includes(data.ability[n])) {
          throw new Error('프리셋 능력 있음 예시: "' + n + '"에 "' + data.ability[n] + '"는 고를 수 없는 값');
        }
        out[n] = data.ability[n];
      });
      return out;
    }
    throw new Error('모르는 프리셋 "' + name + '" (kit, none, ability)');
  }

  // #preset=kit|none|ability&do=accept,review,take:2,close,ignore&author=1
  function parseLink(hash) {
    const out = { preset: 'none', actions: [], author: false };
    const body = hash.replace(/^#/, '');
    if (body === '') return out;
    body.split('&').forEach(function (part) {
      const i = part.indexOf('=');
      if (i < 0) throw new Error('주소 조각 "' + part + '"에 =가 없음');
      const k = part.slice(0, i);
      const v = part.slice(i + 1);
      if (k === 'preset') out.preset = v;
      else if (k === 'do') out.actions = v === '' ? [] : v.split(',');
      else if (k === 'author') {
        if (v !== '0' && v !== '1') throw new Error('author는 0이나 1이어야 함: "' + v + '"');
        out.author = v === '1';
      } else throw new Error('모르는 주소 키 "' + k + '" (preset, do, author)');
    });
    return out;
  }

  return {
    LIFECYCLE: LIFECYCLE, GENERAL_REVIEW_TEXT: GENERAL_REVIEW_TEXT, MOUNT_NOTE: MOUNT_NOTE,
    kindOf: kindOf, holdOptions: holdOptions, fillRequirement: fillRequirement, fillPath: fillPath,
    reviewOpen: reviewOpen, requirementText: requirementText, usableEffects: usableEffects, describeEffect: describeEffect,
    start: start, accept: accept, decline: decline, review: review, ignore: ignore, take: take, bare: bare, applyAction: applyAction,
    publicView: publicView, authorView: authorView, presetHoldings: presetHoldings, panelNames: panelNames, parseLink: parseLink,
  };
});
