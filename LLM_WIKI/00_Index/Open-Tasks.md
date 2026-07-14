---
type: task-index
project: AutoCrawler
updated: 2026-07-14
---

# Open Tasks

## Next

- **BT-002 Tactic Board Runtime Extension — proposed, Step 0 설계 리뷰 대기**([[BT-002-Tactic-Board-Runtime-Extension]]): 택틱보드 compiler/UI 전에 기존 BT에 엄격 행 우선순위, `Running` 행 고정, 조건 후보 교집합·문맥 대상, commit 후 fallback 금지, `Immediate`/`ApproachAllowed`, 최종 wait를 추가한다. 전역 `BtStatus`/commit seam/turn reset/path feasibility의 실제 코드 경계를 Step 0에서 먼저 확정하며, 승인 전 제품 코드는 수정하지 않는다.

- **BS-001 후속 — U3 등반 루프 연결 + request/roster 확장**([[BS-001-Battle-Session]], [[Battle-Session-System]]): BS-001 본체는 완료됐다. BS-002가 로비→전투 단방향 entry만 먼저 고정한 뒤, `EncounterDefinition`/`EncounterModifier`/`PartySnapshot`으로 `BattleRequest` 확장(`PlayerPath`→안정 `unit_id`), 명시적 Faction/`BattleRoster`, `CombatContext`/`CombatRng` 이동 + `BattleRecorder` 추출, U3 DungeonRun의 `BattleResult` 소비·로비 복귀·층 순회·HP/마나/ammo 이월·보상/XP/주차 정산, 한계 턴/수동 후퇴, legacy seam 삭제와 GameLog live 배선을 처리한다.

- **SK-002 후속 — ammo UI/영속/loadout 실효화**([[SK-002-Skill-Ammo-System]], [[Skill-System]]): SK-002 본체는 완료됐다. 후속은 ammo HUD 표시(`SkillAmmoState.TryGetAmmo` 소비), save/load 영속, Expedition 실이월(등반 lifecycle 도입 시), 플레이어 loadout/장착 UI다. `TurnAction_Skill` 배선 자체는 SK-003/SK-004에서 검증됐다. ammo HUD/저장/loadout 및 기존 3종 하드코딩 TurnAction의 데이터 스킬 재배선(아래 SK-001 후속)은 여전히 후속이다.
- **회귀 rebaseline — SK-001/CB-001 다중 상대 테스트**(SK-002와 무관, 스킬 개편 워크스트림 소관): 커밋 `e5d339b "스킬 개편 2"`가 `battle_field.tscn` 상대를 4명(구 타입 `11_hv0hd`) → 1명(신 타입 `14_7o1qe`, 노드명 `Character2`)으로 의도적 교체 → 두 갈래로 실패한다. (a) **셋업 NPE**: `sk001_step1_skill_system_test`(`PrepareAdjacentTarget:275`), `sk001_step3_mana_hit_test`(`TestManaGate:78~80`), `cb001_step4_determinism_test`(`BuildMeleeBehaviorTree:96~97`)가 `Articles/Opponent/Character`(현 `Character2`)를 GetNode해 null→NPE. (b) **다중 상대 단언**: `sk001_step2/4/4b/5`, `sk001_step6 D.ChainHitsThree`, `cb001_step4 D.deaths_recorded`. (2026-07-13 BS-001에서 clean-baseline 동일 재현, 제품 회귀 아님.) 1명 로스터는 의도된 상태(오너 확정)이므로 씬을 되돌리지 말고, (a)는 고정 경로를 현재 `Character2`로 바꾸거나 테스트 자체 fixture로 전환하고, (b)는 각 테스트가 필요한 상대를 스폰/복제하는 self-sufficient로 고친다. CB-001 D는 신 상대가 1v1에서 죽는지(HP/위치/턴 예산)도 함께 점검.
- **WS-002 후속 — workspace 실제 콘텐츠 장착**([[WS-002-Embedded-MDI-Master-Window]],
  [[Workspace-Window-System]]): WS-002 본체는 완료됐고, W0는 placeholder 구조만 둔다. 후속은 World hub 실물화
  (거점 씬 PC 1인, 48주/위험/행동 카드 실제 상태), World battle/replay 장착, BattleSession/전장 scene 연결,
  TacticBoard 실제 보드/읽기·편집 모드, Report 분석 콘텐츠, DemoState/자동 국면 전환, UnitDetail/Save·Load UI다.
  하단 로그 도크는 GL-001 controller가 이식됐지만 live 전투/아웃게임 이벤트 발행은 GameLog 후속과 연결한다.

- **Workspace 후속 — layout/theme/legacy cleanup**([[Workspace-Window-System]]): custom preset override 저장, 자체 slot snap UX 고도화
  (창 가장자리/마스터 가장자리 스냅, Windows Snap Assist trade-off 보완, modifier UX), 레트로 Theme 후속 정리
  (`RetroWin98Theme.tres` 폰트/아이콘/컴포넌트 세트, PopupMenu 밀도, 조조전풍 실제 asset), 레거시
  `Assets/Script/WindowManager.cs`/`Assets/Script/UI/Window/GameWindow.cs` 정리 여부,
  `Assets/UI/Window/field_window.tscn` 삭제/수정 여부, `run/main_scene` 전환은 WS-002 범위 밖 후속이다.

- **display 설정 변경 후속 결정 — root window stretch mode**(WS-001 OD6/WS-002 OD5에서 파생):
  `resizable`/`minimize_disabled`/`maximize_disabled`를 해제했고, WS-002는 workspace 내부 content/log dock rect를
  stretch와 무관하게 처리했다. 프로젝트에 `display/window/stretch/*`
  설정이 없어, `battle_field.tscn`에서 창을 키우거나 최대화하면 전투 화면이 따라 커지지 않고 빈 공간만 생긴다
  (수동 확인, 크래시/스크립트 에러 없음). `stretch/mode = canvas_items` 도입 여부와 그 경우의 aspect 정책을
  결정해야 한다. 결정론(CB-001)에는 영향 없다.
- **SK-001 Data-Driven Skill System — 전체 완료(Step 0~6)**. 후속 cleanup/기능 Task 후보:
  - 기존 3종 하드코딩 TurnAction(`Attack`→TempArticle2, `ChainLightning`→PrincessKnight/TempArticle3) BT 재배선을 데이터 스킬(`sword_slash`/`staff_chainlightning` 등)로 하고 스크립트 삭제. 밸런스 스왑(명중 95%·마나)·CB-001 결정론 회귀 재검증 필요.
  - `DamageBlock.ApplyDamage`의 legacy Damage/UI 결합 제거(headless 피해 result API) — 마나 흡수 정확한 50%·조준 사격 크리 보너스도 이때.
  - ~~무대상 fail-closed/마나 환불 정책 확정~~ → **SK-002/[[ADR-021-Skill-Ammo-System]] §3에서 확정**: 무대상은 커밋 이전 무소모 `Failure`, 마나는 대상 락온 후 커밋 소모, 커밋 후 무환불.
  - 마법 대미지 min 반영 시 마탄/연쇄 뇌격 위력 범위 부여(07 수치표 F-4).

## Later

- Workspace UI cleanup 후속:
  - `Assets/UI/Window/field_window.tscn`은 `Window` 루트가 아닌 `Node` 루트이고 자기 자신을 `ext_resource`로 재귀 인스턴스한다. WS-001에서는 재사용하지 않지만, 혼동 방지를 위해 별도 cleanup에서 삭제/수정 여부를 결정한다.

- ST-001 잔여 P3:
  - `battle_field.tscn`의 per-instance `ArticleStatus` override 제거. 순수 중복은 아니고 인카운터 튜닝이다
    (Opponent = Puppet인데 `MaxHealth`만 1000 → 200). Ally override는 원본과 동일해 삭제 가능하고,
    Opponent 4개는 `Puppet.tscn`의 `MaxHealth`를 200으로 내리면 삭제 가능하다(Puppet은 battle_field에서만 사용).
    단, 나머지 필드 대조 + `.tscn` 재직렬화 위험 + CB-001 결정론 회귀 재검증이 필요해 별도 Task가 맞다.
  - `TurnStartOrder` 상수 집중화. 스탯이 2개뿐이라 실익이 작다. 세 번째 turn-start 스탯 추가 시 함께 한다.
  - 전투 UI 마나 바/회복량 표시 미구현.
  - ManaRegen production 수치 기획-코드 충돌 확인: 기획서 07/F-1, B-5 §5는 전투 중 플레이어 마나 자연 회복 없음이 전제인데,
    ST-001 완료 상태는 production 캐릭터 씬 4종에 `ManaRegen` 배선을 포함한다. 권장 결정은 시스템은 적/특성용 부품으로 보존하고,
    플레이어 production 수치는 0으로 두는 것. 별도 Task에서 기획/수치/씬 배선을 함께 정리한다.

- CB-001 프로덕션 전투 seed 생성/기록 정책: 현재 Step 2의 `_combatSeed=1` 기본값은 재현 테스트에 적합하지만,
  실제 새 전투마다 seed를 생성하고 로그/리플레이 입력에 기록하는 경로는 Step 4 또는 Step 5에서 확정한다.

- CB-001 2단계 — 전투 시뮬레이션/프레젠테이션 분리: 순수 `TurnResolver` + 이벤트 로그
  (`Moved`/`Attacked`/`Damaged`/`Died`)로 판정을 확정하고 연출 계층은 이벤트 재생만 담당.
  리플레이 포맷 `{시드, 초기 배치, 스탯}`, 헤드리스 빨리감기/결과 검증 가능.
  선행 과제: `Damage.ApplyImmediately`의 `DamageFloater`/`Hit()` UI 직접 호출 제거(이벤트 발행 전환),
  `TurnAction_ChainLightning`의 재생 중 판정(체인 타겟을 IgnitionPhase에서 선택) 패턴을 판정 시점
  일괄 확정으로 변경. 엔진 버전 간 리플레이 호환이 필요해지면 `AStarGrid2D` 대신 자체 결정론
  경로탐색 검토([[CB-001-Deterministic-Combat-Resolution]] Follow-ups).
- DT-010 옵션 C: 에디터 debug Play preview에서 고정 example schema 대신 게임 schema 경로를 debug 설정으로
  주입하는 toggle. parse-safe하게 구현 가능(autoload는 `get_node_or_null` 런타임 lookup,
  [[ADR-012-Dialogue-Debug-Preview-Provider]] D1/D2). 현재는 game schema key가 preview에서
  state_missing/unknown_key로 fail-closed됨([[DT-010-Dialogue-Debug-WorldState-Preview]] Step 3 한계).
- 노드 display name/alias 시스템: 현재 노드 목록/그래프 타이틀은 `class_name`에서 "Def"를 떼어 도출하므로
  공백 포함 표시 이름(예: "State Read")이 불가하다. 노드별 표시 이름을 Definition이 선언하게 하면
  `WorldStateRead` → "State Read"처럼 ADR 개념 명칭과 사용자 라벨을 일치시킬 수 있다(DT-013 Step 2 P3 후속).
- schema-aware key/operator picker, inline ConditionSet tree editor, condition trace inspector —
  DT-012 후속(현재는 외부 `.tres`/inline ConditionSet 지정 + provider-free readable summary 표시까지.
  편집 UI·schema 연동·평가 trace 시각화는 범위 밖).
- 조건 평가 trace inspector UI와 disabled-choice + reason UI — DT-008 `condition_evaluated` seam 소비.
- Response Selector와 weighted/random response
- DialogueHistory 및 State Inspector
- Portrait transition 애니메이션(fade/slide 등) — DT-002 MVP 이후.
- Portrait Focus와 비활성 Portrait dim 처리 — DT-002 MVP 이후.
- actor database 및 actor/expression -> Texture resolver — DT-002 MVP 이후.
- speaker 기반 자동 Portrait focus/선택 정책 — DT-002 MVP 이후.
- 기존 `SayDef.portrait` 데이터의 명시적 마이그레이션 도구.
- Set Variable 노드
- Compare 노드
- Random Branch 노드
- Narration 노드
- Emit Event 노드
- Wait 및 Sound 연출 노드
- Entry Point 또는 named entry 지원
- SaveGame 후속(SG-001~003 범위 밖): 실제 production save menu UI scene(SG-003은 host integration contract
  문서 + test-only fake host flow까지만, 실제 위젯/theme/localization/input focus는 host 소유),
  autosave/quicksave 구현, thumbnail/capture image, 다세대 백업 history, compression/encryption,
  schema/section version migration registry, Dialogue SaveEffect(저장 트리거는 game/event layer 우선).

## Recently Completed

- **BS-003 Battle Completion and Lobby Return 전체 완료(Step 0~3, 오너 GUI smoke 사인오프)**
  ([[BS-003-Battle-Completion-Lobby-Return]], [[BS-003-Battle-Completion-Lobby-Return-Completion-Review]] 판정: 완료).
  BS-002 진입 뒤에 `BattleResult` 소비 → outgame/hub 복귀 → 같은 버튼 재전투 v0 왕복을 닫았다.
  `LobbyBattleEntry.OnBattleCompleted`이 outcome 무관 순서(result 캡처 → cleanup → `BattleMount` 숨김 → outgame
  preset → 결과 라벨)로 복귀하고 `LastResult`를 보존한다. `workspace.tscn`에 `HubPanels/ResultLabel` 배선. 검증:
  `bs003_step1`(30)/`bs003_step2`(23, 2회 연속 재전투·정적/Node 격리) ALL PASS, bs002/bs001/ws 회귀 유지. 사실은
  [[Workspace-Window-System]] "Lobby Battle Entry" / [[Battle-Session-System]] "Consumers". 후속: 정산·이월·
  DS-001 DemoState·DL-001 U3 DungeonRun·스토리.

- **BS-002 Lobby-to-Battle Entry Integration 전체 완료(Step 0~3, 오너 GUI smoke 사인오프)**
  ([[BS-002-Lobby-to-Battle-Entry-Integration]], [[BS-002-Lobby-to-Battle-Entry-Integration-Completion-Review]]): World hub
  `전투 시작` 버튼 → battle preset → World `SubViewport` 전투 표시 → `BattleSession.Running` 단방향 진입을 연결했다.
  headless 계약과 실제 픽셀 표시/자동 턴 진행을 확인했다. 결과 표시·로비 복귀·재전투는 BS-003으로 분리했다.

- **BS-001 BattleSession Runtime Boundary 전체 완료(Step 0~4)**([[BS-001-Battle-Session]], [[BS-001-Battle-Session-Completion-Review]] 판정: 완료, [[ADR-022-Battle-Session-Lifecycle]] accepted). `Assets/Script/Battle/`의 `BattleSession`이 `BattleRequest`로 전투 씬을 생성·소유·시작하고 승패를 판정해 scene-free `BattleResult`를 1회 반환한 뒤 정리·재실행한다(Victory/OpponentsEliminated, Defeat/PlayerDefeated, mutual kill=Defeat, Aborted/InvalidSetup). 완료는 event-identity + deferred teardown + `_ExitTree` 정적 정리 + 2회 연속 실행 격리. `TurnHelper`는 명시적 lifecycle seam으로 축소 + 현재 유닛 사망 커서 결함 수정. 사실은 [[Battle-Session-System]]. 후속은 위 Next의 BS-001 후속 항목으로 분리했다.

- **SK-004 Usable Attack Gate 완료**([[SK-004-Usable-Attack-Gate]]): spent ammo/mana-blocked 주력기가 이동 사거리 계산에 남아 기본기 접근을 막던 문제를 수정했다. `TurnActionBase.CanStart`, `CharacterArticle.HasUsableAttack`, `BehaviorTree_HasUsableAttack`을 추가하고 SK-002/SK-004 회귀를 통과시켰다. 후속은 공격 불가 시 도망/대기 BT 분기.

- **SK-003 Skill-Based Character Scene 완료**([[SK-003-Skill-Based-Character-Scene]]): `Assets/Scenes/Character/SkillCaster.tscn` 신규. `TurnAction_Skill`로 `staff_chainlightning`(Battle ammo 1)과 `staff_magicbolt`(Unlimited fallback)를 BT에 배선했다.

- **SK-002 Skill Ammo System 전체 완료(Step 0~4)**([[SK-002-Skill-Ammo-System]], [[SK-002-Skill-Ammo-System-Review]] 판정: 완료, [[ADR-021-Skill-Ammo-System]] accepted). 스킬별 고정 보장 사용 횟수 ammo를 `SkillDefinition.AmmoResetScope`/`Ammo` + `CharacterArticle.SkillAmmoState`(순수 C#, Resource 미저장)로 구현하고, `TurnHelper._Ready` battle-start charge에 배선했다. 사실은 [[Skill-System]] "Ammo" 절이 보존한다. 후속은 위 Next의 SK-002 후속 항목으로 분리했다.

- **GL-001 GameLog Foundation 전체 완료(Step 0~4)**([[GL-001-GameLog-Foundation-Completion-Review]] 판정: 완료).
  전역 로그 기반: `Assets/Script/GameLog` 순수 C# 도메인(`GameLogEntry`/enum/`GameLogModel` append·200 trim·필터),
  `LogWindowController`가 `workspace.tscn` Log 창 placeholder를 읽기 전용 UI로 교체(탭·Detail/importance 시각 구분·
  대화 아카이브 접힘·200 trim 반영), `GameLogService` autoload([[ADR-020-GameLog-Service-Lifetime]])가 모델 소유,
  `SkillReportToGameLogAdapter`가 raw report 9종을 entry로 변환(cast_cancel 진영 분기·`TitleKey`/`Args`/fallback·
  fail-closed). 사실은 [[GameLog-System]]. 후속: **adapter live 전투 배선 + kill/death 발행**, SaveSection 저장,
  interaction handler 실제 연결, 던전 크롤식 집계, 구조화 `SkillReport` 마이그레이션, 현지화 renderer, 아웃게임 이벤트 자동 생성.

- **`ArticleStatus.ApplyAffectingStatuses()` 열거 중 리스트 수정 결함 수정**(ST-001 후속, 단독 수정):
  만료(`Cost == 0`)된 `StatusAffect`가 `OnAffectedEnd` → `RemoveAffectStatus()`로 순회 중인 리스트를 수정해
  첫 `IAffectedOnlyMyTurn`/`IAffectedUntilTheEnd` production 구현에서 `InvalidOperationException`이 날 수 있었다.
  `AffectingStatusesList.ToArray()` snapshot 순회로 수정. 회귀 테스트 `st001_step1_natural_regen_test` `[H]` headless PASS(2026-07-08).

- **`ArticleBase.IsAlive` indexer 예외 / `HasLivingHealth()` 개념 중복 수정**(ST-001 P3, 단독 수정):
  `IsAlive`가 `StatusElementsDictionary[typeof(Health)]` 인덱서를 써서 `Health` 없는 Article에서 `KeyNotFoundException`을 던졌다.
  `ArticleStatus.HasLivingHealth()`에 위임하도록 바꿔 생존 판정 진실을 한 곳으로 모았다. 호출부 17곳 무변경.
  회귀 테스트 `st001_step1_natural_regen_test` `[I]` headless PASS(2026-07-08).

- **ST-001 Natural Regen Stats 완료**(Step 0~3, [[ST-001-Natural-Regen-Stats-Review]] 판정: 완료):
  체력/마나 자연 회복을 `HealthRegen`/`ManaRegen : StatusElement`로 표현하고, `ITurnStartStatusElement`
  (`TurnStartOrder`/`ApplyTurnStart`) 계약 기반 dispatch로 유닛 턴 시작 1회에 적용한다. `ArticleStatus`는
  보관/조회/생존 guard/dispatch만 담당한다. production 캐릭터 씬 4종과 `battle_field.tscn` override 5개에
  `Mana`/`ManaRegen`/`HealthRegen` 배선 완료. 결정 [[ADR-019-Natural-Regen-Stats]], 사실
  [[Article-Status-System]]/[[Turn-System]]. 잔여 P3와 `ApplyAffectingStatuses()` 결함은 위 항목 참고.

- **BT-001 BehaviorTree Graph Editor and Debugger 완료**(Step 1~5, [[BT-001-BehaviorTree-Graph-Editor-Debugger-Review]] 판정: 완료):
  Node tree source-of-truth 기반 GraphEdit viewer/authoring/Inspector 연동과 원격 디버그 채널을 완료했다.
  Step 5에서 battle runtime register discovery, target selector, session 재수집, `tree_path`별 remote tab routing,
  tab close stop 격리, stale 정지를 구현했다. 리뷰 P2로 발견된 `elapsed_time` fractional delta truncation은
  `_elapsedTime`/`BehaviorLog.Time` double 전환 및 실제 `Running` action 0.016초 누적 회귀로 수정 완료.
  검증: `dotnet build` 경고/오류 0, `bt_validation_test.tscn` A~T ALL PASS, `battle_field.tscn` F5 smoke.
완료 작업의 상세 사실/판정은 Current-State와 각 Review가 보존한다. 여기는 최근 완료 포인터만 둔다.

- **CB-001 Deterministic Combat Resolution 완료**(Step 0~5,
  [[CB-001-Deterministic-Combat-Resolution-Review]] 판정: 완료): 전투를 "같은 시드 + 같은 초기 배치 →
  같은 결과"의 결정론 시스템으로 전환(1단계, 현 구조 유지). 턴당 1회 지속 효과(프레임/배속 종속 버그
  수정), `TurnHelper` 소유 시드 `CombatRng` + 금지 난수 API 정적 가드, ADR-017 정규 순서(타겟/체인/턴
  순서/이동 동점자), 실제 `battle_field.tscn` 결정론 회귀(같은 시드 완전 일치·배속 불변·다른 시드
  스모크) 고정. 결정 [[ADR-017-Deterministic-Combat-Resolution]], 사실 [[Turn-System]]/
  [[Article-Status-System]]. 2단계(시뮬/프레젠테이션 분리)와 잔여 P3는 위 Later/Task Follow-ups 참고.
- **BT-001 BehaviorTree Graph Editor and Debugger Step 4b 완료**(Step 4b, 판정: 완료):
  수신한 원격 payload 구조를 기반으로 동적으로 디버그 그래프를 구축하고, 노드 타입 색상 캐싱, 실시간 상태 하이라이트 전환, 잔상 차단을 위한 stale clear 및 unregister 시의 Stale 회색 잠금을 구현하였으며, 탭 라이프사이클 세션 제어 및 임시 선택 UI(TEMP)를 통해 F5 플레이 연동 스모크 테스트를 완수함. C# 헤드리스 단위 테스트(O~Q 케이스) 검증 완료.
  - 2026-06-20 P1 수정: `BehaviorTreeEditor.HandleDebugMessage`가 tick만 넘기던 배선을 고쳐
    `register`/`unregister`/`structure`/`tick`을 모두 `DebuggerWindow.HandleDebugMessage`로 위임한다.
    원격 메시지 수신 시 창이 없으면 자동 생성하고 최초 `register`에서 표시한다. 회귀 테스트 R 추가,
    A~R ALL PASS. 자동 GUI 관찰 한계로 F5 스모크는 플레이 프로세스 생성 및 창 자동 표시까지 확인,
    `Start Debugging` 이후 시각 확인은 `walkthrough.md`에 수동 후속 절차를 남김.
- **BT-001 BehaviorTree Graph Editor and Debugger Step 4a 완료**(Step 4a, 판정: 완료):
  에디터 디버거 세션 및 런타임 Registry/Gating 메커니즘을 구축하여 양방향 원격 디버그 채널 프로토콜 및 Zero Allocation 게이트 단락 처리를 완벽하게 연동함. 헤드리스 C# 단위 테스트(K~N 케이스) 검증 완료.
- **BT-001 BehaviorTree Graph Editor and Debugger Step 3 완료**(Step 3, 판정: 완료):
  선택한 노드를 Godot 메인 인스펙터에 연동하고 노드 명칭 변경 및 `bt_graph_position` 메타데이터 저장을 안정화함(Step 3). `TempArticle3.tscn`이 에디터 버전 드리프트로 인해 광범위하게 재직렬화되었던 오류(P1-1)를 씬 revert 후 메타데이터(`bt_graph_position`) 필드만 선택적으로 정규화 이식하는 방식으로 해결하여 포맷 드리프트를 완전히 제거함.
- **BT-001 BehaviorTree Graph Editor and Debugger Step 2 완료**(Step 2, 판정: 완료):
  GraphEdit 상에서 노드 생성/삭제(자식 노드 구출 포함), 연결/해제 및 Sibling Order Up/Down 조정을 실제 Node 트리와 동기화하고 씬 저장을 성공적으로 연동함. 유효성 검사 차단 규칙(Decorator 1자식, Action 자식 금지, Cycle 차단, 다중 부모 차단) 구현 완료.
- **BT-001 BehaviorTree Graph Editor and Debugger Step 1 완료**(Step 1, 판정: 완료):
  선택한 BehaviorTree를 GraphEdit 기반의 read-only viewer로 표시하고 유효성 위반 구조를 시각적 Reason과 함께 노출함. HSplitContainer를 통해 기존 DebuggerTree와 나란히 분할 뷰로 표시하며, C# 헤드리스 단위 테스트(6개 시나리오) 검증 완료.
- **DT-016 DialogueManager Lifecycle Regression 완료**(Step 1~2,
  [[DT-016-DialogueManager-Lifecycle-Regression-Review]] 판정: 완료, 제품 코드 변경 없음): 게임 코드
  진입점 `DialogueManager.play(...)`의 반복 실행/교체/same-frame latest-wins/callback 재진입/stale signal
  차단/provider tuple isolation 계약을 전용 headless matrix(`dt016_step1_manager_lifecycle_test`)로
  고정함. 완료 회귀 4/4 ALL PASS, SCRIPT ERROR 0.
- **DT-015 Dialogue Integrated Regression Graph 완료**(Step 1~2, [[DT-015-Dialogue-Integrated-Regression-Graph-Review]] 판정: 완료): DialogueTool의 기본 대화 조합을 검증하는 canonical regression graph를 작성하고 Step 1(Runtime) 및 Step 2(Editor authored round-trip) 검증을 완료함.
- **WC-001 WorldCore Umbrella Migration 완료**(Step 1~4, [[WC-001-WorldCore-Umbrella-Migration]] 판정: 완료): `DialogueTool`, `WorldState`, `SaveGame` 및 관련 어댑터 모듈을 `addons/world_core/` 하위 sibling 구조로 안전히 이전하고, 모든 프로젝트 경로 및 리소스 내 구 경로 치환 완료. 18종 WorldState/SaveGame 및 23종 DialogueTool 회귀 테스트 ALL PASS.
- **DT-014 Say Line Paging UI Regression 완료**(Step 0~2, [[DT-014-Say-Line-Paging-UI-Regression-Review]] 판정: 완료): 실제 UI `Dialogue_UI.tscn` 클릭 경로에서 DT-003 Say 줄 누적 표시 기능의 headless 회귀 검증. 타이핑 효과의 `set_process(false)` 비활성화로 가변 프레임 델타로 인한 비결정성을 근본적으로 제거하였고, Case 6.2 Choice flow 출력 포트 오배선도 수정함.
- **DT-013 State Read Data 노드 완료**(Step 0~4, [[DT-013-State-Read-Data-Node-Review]] 판정: 완료): 단일 World
  State key 값을 strict typeof로 읽어 Branch/Choice/Expression에 공급하는 `state_read` leaf Data 노드. 주입
  read provider만 소비(fail-closed report + Data error-dominance), editor authoring/저장 validation은
  `StateSchema.KEY_PATTERN` 재사용. 노드 라벨은 "WorldStateRead"(display name/alias 후속은 위 Later).
  결정 [[ADR-015-State-Read-Data-Node]], 사실 [[DialogueTool]]/[[World-State-System]]/[[DialogueTool-User-Guide]].
- **SaveGame SG-001~003 완료**: core(`SaveSection`/`SaveGameManager`, slot save/load/list/delete + 한 세대
  백업/복구 + WorldState adapter) → `SaveFlow` facade(metadata provider + caller override + save gate) →
  host save slot UI integration contract(문서 + test-only fake host flow). 사용법 [[SaveGame-User-Guide]],
  현재 사실 [[SaveGame-System]], 판정 [[SG-001-SaveGame-Core-Section-System-Review]] /
  [[SG-002-SaveFlow-Facade-Metadata-Provider-Review]] / [[SG-003-SaveSlot-UI-Host-Integration-Review]].
  실제 production save menu UI 등 후속은 위 Later 참고.

## Deferred Architecture

- Definition의 Adapter 호출 중계 제거
- NodeTypeRegistry 기반 에디터 노드 팩토리화
- Autoload read와 write/effect 노드의 책임 분리
- SceneFunction 호출 대상, 인자, 반환값, 실패 정책 확정

## Maintenance

- 시스템 문서는 코드 변경 후 현재 사실만 남도록 갱신한다.
- 완료 작업은 Task 문서에 검증 결과를 남기고 이 목록에서 제거한다.
- 새로운 중요한 설계 선택은 ADR을 먼저 작성한다.


