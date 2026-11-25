# 이어서 하기 (인수 문서)

전면 개정 2026-11-25. 새 컴퓨터, 새 세션, 새 호스트(GitLab)에서 이 프로젝트를 이어 갈 때 **가장 먼저 읽는 문서**다. 이전 대화 기록과 세션 메모리는 저장소에 들어 있지 않으므로 필요한 내용을 이 문서와 아래 문서들에 옮겼다. 이 문서는 "지금 어디까지 왔고, 무엇이 정해졌고, 무엇을 기다리는가"만 적고, 이유와 세부는 각 문서가 가리키는 곳에 있다.

## 1. 프로젝트 한 줄

인류 한 명이 우주선 한 척을 타고 동면을 반복하며 우주의 종말을 보러 가는 1인 생존 게임이다. 캐치프레이즈는 "죽기 위해 생존한다". Unity 6(URP) 프로젝트이고, 저장소 루트 아래 `SO` 폴더가 Unity 프로젝트다.

지금은 기획과 시험 단계다. 게임 본편은 없다. 있는 것은 (1) 퀘스트 씨앗과 그 도구, (2) 사건 처리를 시험하는 웹 게임, (3) Unity 프로토타입, (4) 우주선 블록아웃이다.

## 2. 지금 상태 (2026-11-25)

| 줄기 | 상태 | 기준 문서와 도구 |
|---|---|---|
| 퀘스트 씨앗 | 8묶음 씨앗 306개, 수단 목록, 테크 후보 풀 227개가 있다. 독립 리뷰를 여러 번 거쳤고 후보 풀의 근거 검증(105개)도 끝났다 | [QuestSeeds/README.md](QuestSeeds/README.md), `00_Means.md`, `00_TechPool.md`, `REVIEW_*.md` |
| 큰 테크 확장 | 시기마다 쓸 수 있는 큰 테크 10개 이상을 목표로 진행 중이다. 출발 시기까지 초안과 리뷰를 마쳤고 나머지 시기는 사용량 때문에 멈춰 있다 | [QuestSeeds/BigTechPlan.md](QuestSeeds/BigTechPlan.md) |
| 일지 말투 | 사건 문장을 플레이어의 1인칭 일지로 바꾸는 시범(두 말투)을 썼고 사용자 판정을 기다린다 | [QuestSeeds/JournalPilot.md](QuestSeeds/JournalPilot.md) |
| 씨앗 웹 게임 | 1단계("씨앗 하나 완주")가 끝나 시험 중이다. 사건을 처리하는 규칙(사건 엔진)을 Unity에 옮기기 전에 웹에서 먼저 굳힌다 | [SeedGamePlan.md](SeedGamePlan.md), `Tools/SeedGame/` |
| Unity 프로토타입 | 걷기, 상호작용, 예약, 동면 한 바퀴가 도는 시연이 있다. 웹 1단계 판정과 우주선 판정을 기다리며 멈춰 있다 | [PrototypePlan.md](PrototypePlan.md), `Assets/Scenes/Prototype_SeedShip.unity` |
| 우주선 | 블록 격자 모델(N1b)까지 구현과 리뷰를 마쳤고 다음은 N2(붙이기와 떼기)다 | [ShipStatus.md](ShipStatus.md), [ShipBlockGridPlan.md](ShipBlockGridPlan.md) |
| 컨셉 | 전체 컨셉과 시간 모델, 정보 단계 공개 원칙을 18장에 확정과 제안으로 나눠 적었다 | [GameConcept.md](GameConcept.md) |

## 3. 정해진 것 (사용자 확정)

세부와 이유는 괄호의 문서에 있다. "제안"은 확정된 것처럼 다루지 않는다.

**용어** (2026-11-25 확정, [GameConcept.md](GameConcept.md) 18장)
- 배 전체는 **선체**. 선체는 **블록**(붙이고 떼는 덩어리)들이 **접합부**로 이어진 것이다. 블록 안은 **가압구역**(걷는 영역)과 **설비구역**(못 걷는 내부)으로 나뉜다. 옛 이름 "세그먼트", "구조 칸"은 쓰지 않는다.
- 총칭은 **수단**이다. 하위어로 **테크**(연구로 얻는 지식), **모듈**(선체에 놓이는 설비), **능력**(사건이 터진 순간 가동 중인 수단)을 구분한다.
- 사건을 띄우고 검토하고 대응하는 시스템은 **사건 엔진**이다. 단위는 씨앗(문서), 사건(플레이에서 뜨는 것), 줄기(연쇄)다.

**시간과 동면** ([GameConcept.md](GameConcept.md) 18장)
- 시계는 초 단위 하나이고 날짜나 "며칠째" 카운터는 없다. 피곤은 쌓이고 동면이 아닌 잠은 피곤을 줄인다. 동면이 유일한 공식 빨리감기다.
- 동면 길이의 상한은 기술이 자라면서 커진다(초반 10년 안팎에서 후반 1000억 년까지). 문서에 적은 동면 장치 수명(저온 포드 100년 등)과 맞지 않는 곳이 있어 **열린 질문**이다.
- 분석, 설치, 배양은 시뮬레이션 시계 위의 타이머다. "설치됨"은 "가동"이 아니다. 사건에 대응할 때는 **그 순간 가동 중인 능력**만 본다.

**정보 단계 공개** (제안 9, 사용자 지시)
- 지도(작성자 보기)는 내용을 다 보이지만 플레이에서는 표면(징후)과 일반 선택지만 먼저 보이고, 검토 능력이 깊을수록 숨은 사정이 드러난다. 방치 결과는 당할 때까지 알려 주지 않는다.

**씨앗 설계 결정** ([QuestSeeds/00_Means.md](QuestSeeds/00_Means.md) "설계 결정" 1~9와 "쓰는 규칙" 1~5)
- 사람은 배에 남지 않는다. 내가 직접 배 밖으로 나가지 않고 선외 작업은 드론이 한다. 수단과 씨앗의 개수는 제한하지 않는다. 중력 구획판은 총합이 보존된다. 탑재 중은 한 사이클이 든다.

**큰 테크** ([QuestSeeds/BigTechPlan.md](QuestSeeds/BigTechPlan.md))
- 세 시험(고유 성질, 파급, 상태)을 통과한 것만 큰 테크다. 시기마다 **쓸 수 있는 큰 테크**를 센다(최소 10, 최대 20, 목표 15). 출발만 정원 6~7로 낮췄다. 시험은 낮추지 않는다.

**우주선** ([ShipStatus.md](ShipStatus.md) 2절, [ShipBlockGridPlan.md](ShipBlockGridPlan.md))
- 블록은 여러 모양과 크기의 테트리스 조각이고 격자에 맞닿게 붙인다(연결을 강제하지 않는다). 모듈은 블록과 별개 층이고 가압구역의 빈 방칸에만 놓는다. 이어지지 않은 가압구역에 모듈을 설치하면 오류다. 모듈은 걸음을 막고(점유한 N×M 칸 전체), 그래서 동면 전에 배치를 생각하는 것이 플레이의 일부다. 칸 크기 2m는 출발값이고 조절 가능하다.

## 4. 사용자 판정을 기다리는 것

| 항목 | 어디서 |
|---|---|
| 일지 말투: A(건조한 항해일지) 또는 B(혼잣말 일지) | [QuestSeeds/JournalPilot.md](QuestSeeds/JournalPilot.md) |
| 출발 큰 테크 조건부 5개를 고쳐 재리뷰할지, 회로 소거 펄스기(함선 AI 기억 소거, 무기 읽힘)와 거울 손 생물을 사건에 쓸지 | [QuestSeeds/BigTechReview_Departure.md](QuestSeeds/BigTechReview_Departure.md) |
| 거대공학, 초월, 별의끝, 암흑 시기의 큰 테크 작성(사용량이 풀린 뒤) | [QuestSeeds/BigTechPlan.md](QuestSeeds/BigTechPlan.md) 8, 9절 |
| 후보 풀 근거 검증의 "부분" 20개와 문구 수정 14개를 풀 문서에 반영할지, 의식 짐작기에 근거 약함 표시를 달지 | [QuestSeeds/REVIEW_TechPoolEvidence.md](QuestSeeds/REVIEW_TechPoolEvidence.md) |
| 웹 게임 1단계 판정(거절의 해석, 길이 0개일 때 닫힘, 고른 뒤 원문 공개 범위, 문구), 4단계(동면) 승인, 2~6단계 결정 | [SeedGamePlan.md](SeedGamePlan.md) |
| 우주선 N2 상세 초안 승인, 칸 크기 값 | [ShipBlockGridPlan.md](ShipBlockGridPlan.md) 10.6절 |
| 프로토타입에 정보 단계 공개를 적용하는 시점(웹 1단계 판정 뒤), 새 배로 옮기는 시점(우주선 판정 뒤) | [PrototypePlan.md](PrototypePlan.md) 10, 11장 |
| 동면 상한 값의 불일치(10년 대 100년) | [GameConcept.md](GameConcept.md) 18장 열린 질문 |
| 컨셉 17장의 미정 열 가지, 퀘스트라인 100개 고르기, 테크 예시 47개 고르기 (11-24 이전의 오래된 미정이고 그대로 남아 있다) | [GameConcept.md](GameConcept.md), [Quests/](Quests/README.md), [TechExamples.md](TechExamples.md) |

## 5. 도구와 쓰는 법

모두 Python 3.8 이상(표준 라이브러리만)이고, 웹 게임 규칙 시험만 Node.js가 필요하다. 저장소 루트 아래 `SO/`에서 실행한다.

| 도구 | 하는 일 | 쓰는 법 |
|---|---|---|
| `Tools/SeedMap/seedmap.py` | 씨앗, 퀘스트라인, 수단을 한 그림으로 잇는 지도를 만들고 규칙을 검사한다. 출력 `seedmap.html`은 git이 무시한다 | `python seedmap.py check --suggest`(`씨앗검사.bat`), `build --open`(`씨앗지도열기.bat`), `watch`(`씨앗지도감시.bat`: 원본이 바뀌면 자동으로 다시 만든다) |
| `Tools/SeedGame/` | 씨앗 웹 게임. `seedgame.py build --open`(`씨앗게임열기.bat`)으로 만들고 연다. 규칙 시험은 `python seedgame.py build` 뒤 `node engine_test.js` | 데이터는 씨앗 문서에서 자동으로 뽑고, 효과층은 `sidecar/`에 둔다 |
| `Tools/EditorLock/editorlock.py` | 유니티 에디터 하나를 여러 세션이 나눠 쓸 때의 교통정리. `status`, `grant`, `release`, `queue`, `dequeue`, `log` | 상태 파일은 `.claude/editor-lock.json`(로컬, 커밋 안 함). `grant`와 `release`는 조정하는 세션 하나만 쓴다 |
| `Tools/QuestMap/` | 옛 형식(낱장 사건 JSON, `SO/Content/base`) 검사와 지도 | 초기 자료용이다. 씨앗 지도가 이어받았다 |

씨앗을 쓰거나 고칠 때의 순서는 [QuestSeeds/README.md](QuestSeeds/README.md) "씨앗을 추가하거나 고칠 때"다. 요약하면 검사, 엮임 보강, 지도 재생성, 커밋이다.

**에디터를 쓰는 규칙** (여러 AI 세션이 에디터 하나를 나눌 때)
- 에디터 접속 이름(MCP 인스턴스)은 컴퓨터마다 다르다. 이 프로젝트는 Unity 6000.6.10f1, URP 17.6, Input System만 쓴다.
- 에디터를 쓰기 전에 `editorlock.py status`로 비었는지 보고, 조정하는 세션에 요청해 `grant`를 받는다. 끝나면 "에디터 반납"에 Play 아님, 컴파일 에러 없음, 콘솔 에러 없음, 열린 씬 저장 여부를 적어 알린다. 조정하는 세션이 확인한 뒤 `release`한다.
- 에디터가 Play 중이면 사용자가 직접 보고 있을 수 있으니 끄지 말고 확인한다. 에디터 창이 비활성이면 Play 루프가 멈추거나 상태 표시가 늦을 수 있다.

## 6. 작업 규칙

사용자와 일하면서 정해진 것이다.

- 한국어로 대화한다. 영어로 쓴 말에만 교정을 붙인다.
- **자기 코드를 자기가 승인하지 않는다.** 새 스크립트, 큰 변경, "완료"라고 말하기 직전에는 작성자와 다른 컨텍스트의 독립 리뷰를 거친다. 리뷰 결과는 심각도와 함께 그대로 보고하고, 안 고치기로 했으면 이유를 밝힌다.
- **에러를 가드로 덮지 않는다.** 원인을 한 문장으로 못 답하면 고치지 말고 모른다고 보고한다. `null` 체크로 조용히 빠져나가기, 예외 삼키기, 기본값 대입, 테스트 건너뛰기는 금지다. 정당한 방어 코드는 신뢰 경계(외부 입력, 파일, 네트워크)이거나 계약 위반을 시끄럽게 드러내는 코드이고 이유를 한 줄 주석으로 단다.
- 증거 없이 완료라고 하지 않는다. 실행한 수치나 스크린샷을 댄다. 안 해 봤으면 안 했다고 말한다. Unity는 Play와 콘솔 확인이 증거다.
- 커밋은 작업 단위가 끝날 때 **경로를 하나씩 지정해서** 한다(`git add .` 금지). `Assets/Imports`, `Packages`, `ProjectSettings`, 세션 상태 폴더는 건드리지 않는다. 푸시는 사용자가 말할 때만 한다.
- 취향이 정답인 일(재미, 말투, 난이도, 예쁨)은 사람이 판정한다. AI는 레퍼런스가 없으면 무난한 기본값을 낸다. 판정은 한 단어면 충분하다. 큰 변경 전에는 커밋됐는지만 확인한다.
- 사람의 읽기 속도를 넘지 않는다. 큰 작업 뒤에는 코드가 아니라 **구조 노트**(문제, 후보, 선택과 이유, 구현 방식, 터진 것, 조절 가능한 것의 의미)를 저장소에 남긴다. 코드 인용과 구체적 수치는 쓰지 않는다.
- 제안하지 않는 것: 전투 중심, 여러 명의 승무원, 처음부터 보이는 테크트리, 수명 제한, 플레이어가 직접 채굴하는 방식, 선택지 창 위주의 이벤트, 시대를 챕터로 나누는 구조.
- 컨셉 문서의 "제안"은 확정된 것처럼 다루지 않는다. 사용자는 느낌을 잡는 대화를 반복하면서 기획을 풀어 간다.

**사용량 운영 메모**: 에이전트 하나가 주간 한도의 1~2%를 쓴다. 큰 일은 한 덩어리씩 하고, 끝날 때마다 사용량을 확인하고 이어 간다.

## 7. 문서 지도와 상태

상태: **기준**(지금 맞는 것), **옛**(일부만 맞음, 충돌하면 기준이 이긴다), **동결**(구현은 남았고 더 고치지 않음), **기록**(결정과 리뷰의 역사).

| 문서 | 상태 | 내용 |
|---|---|---|
| [GameConcept.md](GameConcept.md) | 기준 | 전체 컨셉. 18장이 최신(시간, 공개 단계, 모듈 배치, 용어) |
| [QuestSeeds/README.md](QuestSeeds/README.md) | 기준 | 씨앗 형식, 시기 표, 갱신 절차 |
| [QuestSeeds/00_Means.md](QuestSeeds/00_Means.md), `00_TechPool.md` | 기준 | 수단 목록과 설계 결정, 테크 후보 풀 |
| [QuestSeeds/](QuestSeeds/README.md) `01~08_*.md` | 기준 | 씨앗 8묶음 |
| [QuestSeeds/BigTechPlan.md](QuestSeeds/BigTechPlan.md) 외 `BigTech*.md`, `JournalPilot.md` | 기준(진행 중) | 큰 테크 계획, 초안, 리뷰, 일지 시범 |
| `QuestSeeds/REVIEW_*.md` | 기록 | 씨앗 리뷰와 풀 근거 검증 |
| [SeedGamePlan.md](SeedGamePlan.md) | 기준 | 웹 게임 계획(1~6단계, 선택 7단계) |
| [PrototypePlan.md](PrototypePlan.md) | 기준 | Unity 프로토타입 계획과 웹 연동 제안 |
| [ShipStatus.md](ShipStatus.md) | 기준 | 우주선 줄기의 상세 인수 문서 |
| [ShipBlockGridPlan.md](ShipBlockGridPlan.md) | 기준 | 우주선 새 모델(블록 격자). 블록 크기와 통로 모델의 기준 |
| [ShipLayoutNetworkPlan.md](ShipLayoutNetworkPlan.md) | 옛 | 통로 그물 계획. 용어, 모듈 별개 층, 규칙은 이어받았고 블록 크기와 통로 모델은 위 문서가 대체했다 |
| [ShipBuildDesign.md](ShipBuildDesign.md) | 옛 | 초기 설계. 0.1~0.6절만 결정 기록이고 본문 1~12장은 옛 기준이다 |
| [StructureLayerPlan.md](StructureLayerPlan.md), [StructureNotes_Ship.md](StructureNotes_Ship.md) | 동결 | 구조층(설비구역의 전신) 1차 계획과 구조 노트. 코드와 씬(`ShipBlockout_Structure`)이 남아 있다 |
| [StructureNotes_Network.md](StructureNotes_Network.md) | 기록 | 통로 그물 A0, A1의 구조 노트 |
| [QuestSystemDesign.md](QuestSystemDesign.md), [Quests/](Quests/README.md), [TechExamples.md](TechExamples.md) | 옛(초기 자료) | 초기 퀘스트 체계 설계, 퀘스트라인 100개, 테크 예시. **`Quests/`는 씨앗 지도가 읽으므로 지우면 지도가 깨진다** |
| `Blockout/` | 기록 | 스크린샷. 파일 이름 앞부분으로 출처를 안다: `structure_`(구조층), `network_`(통로 그물과 블록), `large_`(옛 시안), `proto_`(프로토타입) |

## 8. 저장소와 이전(GitLab)

- 원격은 지금 GitHub(`origin`)이다. 사용자가 GitLab으로 옮길 계획이다. 원격 작업(원격 추가, 푸시, GitHub 정리)은 사용자가 직접 하거나 말할 때만 한다.
- 저장소 루트는 `SpaceOdyssey/`이고 Unity 프로젝트는 그 아래 `SO/`다. 루트의 `.gitignore`는 세션 상태 폴더(`.omc/`)만 막고, Unity용 무시 규칙은 `SO/.gitignore`에 있다.
- **에셋 팩 폴더**: `SO/Assets/Imports`는 3.4GB이고 git에 올라간 적이 없으며 프로젝트의 어느 파일도 참조하지 않는다(GUID와 경로 모두 0). 사용자 결정(2026-11-25)으로 `SO/.gitignore`에 넣어 저장소에서 뺐다. 폴더는 그대로 남아 있다. 새 환경에서는 보유한 팩을 같은 자리에 다시 받는다. 에셋 규칙은 보유한 팩만 쓰는 것이고(AI 생성과 유료 팩 불가), 원본은 수정하지 않고 쓸 것만 복사한다. 이 폴더에는 SF 팩(`Sci-Fi ToiletMech`, `Data/Sci-fi_assets`, `SciFiProps`, `Sci-Fi Styled Modular Pack` 등)과 판타지 팩(`Off Axis Studios/Polyworks` 1.1GB)이 섞여 있다.
- **Git LFS**: `SO/.gitattributes`가 이미지, 모델, 오디오, 영상, 압축 파일(png, jpg, tga, psd, fbx, obj, wav, mp3, ogg, mp4, mov, zip 등)을 LFS로 저장하게 한다. 기존 PNG 61개는 포인터로 바꿨고 프로토타입 스크린샷 110장도 LFS로 들어갔다. 새 환경은 `git-lfs`를 설치한 뒤 clone한다. GitLab 쪽에서 LFS를 켜야 한다. 이력 속의 옛 PNG(약 22MB)는 일반 blob으로 남아 있다. 이력까지 LFS로 바꾸려면 `git lfs migrate import`(이력 재작성)를 쓰는데, 저장소가 작아서 급하지 않고 사용자 결정이 필요하다.
- **커밋 상태**(2026-11-25): 이 문서, `GameConcept.md` 18장, `PrototypePlan.md`, 프로토타입 스크립트와 씬, `Tools/EditorLock/`, 프로토타입 스크린샷을 커밋했다. 아직 커밋 안 한 것은 `Packages/`와 `ProjectSettings/`의 수정뿐이고, 에디터가 바꾼 것이라 의도를 확인한 뒤 커밋한다. 푸시는 안 했다.
- 로컬 전용이라 이전하지 않는 것: `.omc/`, `SO/.claude/`, 세션 대화와 메모리, 에디터 잠금 상태 파일.
- 새 환경에서 시작하는 법
  1. 저장소를 받는다.
  2. Unity Hub에서 `SO` 폴더를 연다(Unity 6000.6.10f1).
  3. Unity MCP를 쓴다면 에디터의 `Window > MCP for Unity`에서 연결을 켠다.
  4. Python 3.8 이상을 설치한다. 웹 게임 규칙 시험을 돌리려면 Node.js도 필요하다.
  5. 새 세션에 이 문서와 [GameConcept.md](GameConcept.md)를 먼저 읽게 한다. 줄기를 맡길 때는 그 줄기의 기준 문서(2절 표)를 읽게 한다.

## 9. 알려진 문제와 주의

- 계획서와 도구의 숫자는 곧 바뀐다. 이 문서의 개수(씨앗 306개 등)는 2026-11-25 기준이고, 정확한 값은 `seedmap.py check`가 낸다.
- 큰 테크의 근거 검증은 웹 검색 요약에 기댄 곳이 있다. 각 표의 근거 칸에 표시했다.
- 웹 게임의 브라우저 클릭 확인은 사람이 해야 한다. 자동 확인은 DOM과 스크린샷까지다.
- Unity 프로토타입과 우주선 블록아웃은 Play와 사람의 눈 판정이 필요하다(걷는 감각, 키 입력).
- 씨앗 지도 도구는 좁은 창에서 처음에 지도를 작게 그린 적이 있다. 보통 크기의 브라우저에서 같은지 확인하지 않았다.
