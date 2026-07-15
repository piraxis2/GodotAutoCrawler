---
type: status
project: AutoCrawler
updated: 2026-07-13
---

# Current State

> **Known Regressions (2026-07-10 갱신, 근본 원인 확정).** 아래 본문의 일부 "ALL PASS" 서술은 지금은 사실이 아니다.
> `cb001_step4_determinism_test`, `sk001_step1_skill_system_test`, `sk001_step3_mana_hit_test`은
> `Articles/Opponent/Character`(현 노드명 `Character2`)를 GetNode하는 **셋업 단계에서 NPE**로 실패하고,
> `sk001_step2/4/4b/5`, `sk001_step6_data_pack_test`(`D.ChainHitsThree`)은 다중 상대 의존 단언이 **실패**한다
> (2026-07-13 BS-001 작업에서 clean-baseline 동일 재현 확인 — 제품 회귀 아님). **근본 원인:** 커밋
> `e5d339b "스킬 개편 2"`가 `battle_field.tscn`의 상대를 4명(구 타입 `11_hv0hd`) → 1명(신 타입 `14_7o1qe`,
> 노드명 `Character2`)으로 **의도적으로 교체**했다(드리프트 아님). 1명 로스터는 의도된 상태이며(오너 확정),
> 회귀 테스트를 상대 로스터에 비의존하도록 self-sufficient로 rebaseline하는 별도 Task로 처리한다([[Open-Tasks]]).
> SK-002/WS-001 등과 무관.
> CB-001 step1~3, SK-002 전 스텝, ST-001 step1, BS-001 step1~3은 통과한다.

## Project

- Godot 4.6.3 Mono, C#, .NET 8 기반 자동 턴제 택티컬 RPG 프로토타입이다.
- 전투 핵심은 Article, Status, BehaviorTree, TurnAction, TurnHelper로 구성된다.
- 실제 전투 실험 중심 씬은 `Assets/Scenes/Map/battle_field.tscn`이다.
- `main.tscn`은 UI와 윈도우 실험 성격이 강하다.
- 아웃게임 UI 기반은 독립 Window 작업대 `Assets/Scenes/workspace.tscn`이다(WS-001, 아래 Workspace 절).



## GameLog

- **GL-001 GameLog Foundation Step 1 완료·코드 리뷰 완료**([[GL-001-GameLog-Foundation]], [[GL-001-GameLog-Foundation-Step1-Review]] 판정: 완료).
  `Assets/Script/GameLog`에 Godot UI 미의존 순수 C# 도메인을 신설했다. `GameLogChannel`은 `Combat`/`Story`/`Reward`/`System`만 두고 `All`은 nullable channel 필터로 표현한다.
  `GameLogSeverity`는 `Event`/`Detail`/`Trace`, `GameLogImportance`는 `Normal`/`Warning`/`Critical`로 분리한다.
- `GameLogModel`은 `IGameLogSink.Append`에서 `long` Id를 부여하고 최근 200개만 유지한다. `GetEntries(GameLogChannel? channel, bool includeDetail, bool includeTrace)`로 기본 전체 탭(Event-only), 채널 탭(Event+Detail), debug Trace를 구분한다. `GameLogEntry`는 표시/저장/link 데이터만 담고 interaction handler를 호출하지 않는다.
- 검증: `dotnet build AutoCrawler.sln -c Debug` 경고/오류 0, Godot `--headless --path . --import` exit 0, `gl001_step1_game_log_model_test.tscn` 48/48 assertions ALL PASS.
- **GL-001 GameLog Foundation Step 2 완료·코드 리뷰 완료**([[GL-001-GameLog-Foundation-Step2-Review]] 판정: 완료). `workspace.tscn`의 `Windows/LogWindow` placeholder를 `LogWindowController` 기반 read-only UI로 교체했다. 탭은 전체/전투/이야기/획득·시스템이며, 전체는 Event-only, 채널 탭은 Event+Detail, Trace는 v0에서 미노출이다. 대화 archive 접힘/펼침, Detail 감광, Importance 색 강조, 200 trim UI 반영을 구현했다.
- 검증: `gl001_step2_log_window_test.tscn` 51/51 assertions ALL PASS, WS-001 step1/step3 회귀 PASS. Step 2 P3(`ToggleExpand` public API no-op 계약)는 후속 수정·재검증 완료.
- **GL-001 GameLog Foundation Step 3 완료·코드 리뷰 완료**([[GL-001-GameLog-Foundation-Step3-Review]] 판정: 완료). 선행 결정 3건을 구현했다: `GameLogService` autoload가 `GameLogModel`을 소유하고 `IGameLogSink`를 제공한다([[ADR-020-GameLog-Service-Lifetime]]). `LogWindowController`는 주입 모델 우선, 없으면 service 모델 구독, 없으면 로컬 fallback으로 동작한다. `SkillReportToGameLogAdapter`는 현재 SkillSystem raw report 9종(`damage`/`miss`/`stun`/`stun_miss`/`bind`/`cast_cancel`/`chain`/`knockback`/`manadrain`/`selfbuff`)을 `GameLogEntry`로 변환하고 `TitleKey`/`Args`/fallback title을 보존한다. `cast_cancel`은 `SceneTreeFactionResolver`로 대상 path를 resolve해 enemy target=`Event/Normal`, ally target=`Event/Warning`, unresolved/unknown/malformed는 fail-closed한다.
- 검증: `dotnet build` 경고/오류 0, Godot `--import` exit 0, `gl001_step3_adapter_test.tscn` ALL PASS, GL-001 step1/step2·`sk001_step3_mana_hit_test`·`ws001_step3_persistence_test` 회귀 PASS. adapter live 전투 배선과 kill/death 전투 이벤트 발행은 후속이다.
- **GL-001 Step 4(문서/완료 리뷰) 완료 → GL-001 전체 완료(Step 0~4)**([[GL-001-GameLog-Foundation-Completion-Review]] 판정: 완료). 현재 사실은 [[GameLog-System]]이 보존한다(`20_Systems/GameLog-System.md` 신규). 후속: adapter live 전투 배선 + kill/death 발행, SaveSection 저장(최근 200개), interaction handler 실제 연결, 던전 크롤식 집계, 구조화 `SkillReport` 마이그레이션, 현지화 renderer, 아웃게임 이벤트 자동 생성.

## Workspace

- **WS-002 Embedded MDI Master Window 전체 완료(Step 0~5)**([[WS-002-Embedded-MDI-Master-Window]],
  [[WS-002-Embedded-MDI-Master-Window-Completion-Review]] 판정: 완료). 현재 사실은 [[Workspace-Window-System]]이 보존한다.
- workspace는 WS-002 완료 후 GUI 검증을 거쳐 **native owned child-window 마스터 작업대**로 조정됐다. `workspace.tscn`은 상단 드롭다운 메뉴바,
  content area, 하단 로그 도크를 가진 마스터이고, managed `Window`들은 OS 창으로 떠서 마스터 밖 이동이 가능하다.
  Windows에서는 `WorkspaceNativeWindowOwner`가 Win32 owner 관계를 보강해 하위 창이 마스터 위에 유지되고, 작업표시줄 스택은 마스터 1개에 가깝게 유지된다. `exclusive` modal 입력 잠금은 쓰지 않는다.
  `run/main_scene`은 `workspace.tscn`(`uid://caplqnfi4j3ys`)이다(2026-07-13 확인). 부팅 시 정적
  `BattleFieldScene.BattleField`는 비어 있어 BS-001 세션 가드가 깨끗하게 시작한다([[Battle-Session-System]]).
- 마스터 메뉴는 좌측 패널이 아니라 상단 `MenuButton` 드롭다운이다. `Windows` 메뉴는 `world`/`tacticboard`/`report` toggle check item을 제공하고,
  `Presets` 메뉴는 `outgame`/`battle`/`analysis`, `창 모아오기`, `배치 저장`, `배치 복원`을 제공한다.
- floating managed window는 3종(`world`/`tacticboard`/`report`)이다. `log`는 하단 `LogDock/LogView`
  (`LogWindowController`)로 이동했고, `situation`/`weekly_action`/`calendar`는 독립 창에서 제거되어 World `hub`
  내부 placeholder 패널로 이관됐다. hub 패널은 `battle`/`replay` 모드에서 숨긴다.
- preset 3종은 E-7 정규화 비율을 content area 기준 px로 resolve한다: `outgame` = World(hub) 전면,
  `battle` = World(battle) 64% + TacticBoard(read) 33%, `analysis` = Report 34% + TacticBoard(edit) 36% +
  World(replay) 26%. 로그는 도크가 별도 소유한다.
- `WorkspaceWindowManager`는 content rect(상단 메뉴바 32px + 창 타이틀 inset 32px, 로그 도크 높이 20%, 좌측 메뉴 0px 제외) 기준으로
  gather/preset/slot snap을 처리한다. 슬롯 스냅은 content 짧은 변 6% 거리, Y→X→id tie-break, hidden slot 제외, Alt disable 계약이다.
- persistence는 `user://workspace_layout.cfg` version 2다. floating 3창의 position/size/visible/content_mode만 저장하고,
  WS-001 v1 config는 `UnsupportedVersion` fallback으로 기본 preset을 적용하며 파일은 파괴하지 않는다.
- Workspace 레트로/Win98 계열 스타일은 `Assets/UI/Theme/RetroWin98Theme.tres`가 소유한다. `RetroPanelContainer`는 Theme fallback load만 수행하고,
  `RetroVBoxPanel`은 Theme 색상을 읽어 2px bevel만 custom draw한다. 색/여백/버튼/popup/panel 값은 `.tres`에서 수정한다.
- 최근 검증(2026-07-11): `dotnet build` 경고/오류 0, `ws001_step1_registry_test` ALL PASS,
  `ws002_step2_master_dock_test` 41 passed, `ws002_step4_window_diet_test` 40 passed, `gl001_step2_log_window_test` ALL PASS.
  Known Regressions의 `cb001_step4`/`sk001_step6` 등은 WS-002 범위 밖 기존 항목이다.
- 후속(W0 범위 밖): World hub 실물화, BattleSession/전장 scene 장착, TacticBoard/Report 실제 콘텐츠,
  DemoState/자동 국면 전환, custom preset override 저장, 자체 슬롯 스냅 UX 고도화, 레거시 `WindowManager.cs`/`GameWindow.cs` cleanup,
  레트로 Theme 폰트/아이콘/컴포넌트 세트 정리, `battle_field.tscn` stretch 정책. (`run/main_scene`은 이미
  `workspace.tscn`으로 전환됨.) [[Open-Tasks]] 참고.
## Combat

- **BS-003 Battle Completion and Lobby Return 전체 완료(Step 0~3, 오너 GUI smoke 사인오프)**([[BS-003-Battle-Completion-Lobby-Return]],
  [[BS-003-Battle-Completion-Lobby-Return-Completion-Review]] 판정: 완료). BS-002 진입
  뒤에 `BattleResult` 소비 → outgame/hub 복귀 → 같은 버튼 재전투 v0 왕복을 연결했다. `LobbyBattleEntry.OnBattleCompleted`
  이 outcome 무관 순서(result 캡처 → cleanup → `BattleMount` 숨김 → outgame preset → 결과 라벨)로 로비 복귀하고,
  `LastResult`(scene-free 마지막 완료)를 보존한다. `workspace.tscn`에 `HubPanels/ResultLabel` 추가·배선. 사실은
  [[Workspace-Window-System]] "Lobby Battle Entry". 검증: `bs003_step1`(30)/`bs003_step2`(23) ALL PASS(2회 연속
  재전투·정적/Node 격리·preset 실패 격리 포함), bs002/bs001/ws001/ws002 회귀 유지, `dotnet build` 0/0. 실제 결과
  문구·hub 복귀·재전투 화면 전환은 오너 GUI smoke로 사인오프됨. 정산·이월·DungeonRun·스토리는 후속.
- **BS-002 Lobby-to-Battle Entry 전체 완료(Step 0~3, 오너 GUI smoke 사인오프)**([[BS-002-Lobby-to-Battle-Entry-Integration]],
  [[BS-002-Lobby-to-Battle-Entry-Integration-Completion-Review]] 판정: 완료). workspace 로비(World hub)
  `전투 시작` 버튼에서 고정 `BattleRequest`로 `BattleSession`을 시작해 World 창 `SubViewport`에 전투를 장착하는
  단방향 진입을 연결했다. `Assets/Script/UI/Window/LobbyBattleEntry.cs`(root 노드)가 `TryEnterBattle`
  (active latch·null/mount/preset fail-closed) + 완료 시 session Node cleanup만 소유하고, `workspace.tscn`에 버튼/
  `BattleMount`(SubViewportContainer)/`BattleViewport`/entry export를 배선했다. 사실은
  [[Workspace-Window-System]] "Lobby Battle Entry". 검증: `bs002_step1_entry_test`(29)/`bs002_step2_workspace_test`
  (14) ALL PASS, ws001/ws002·bs001 회귀 유지, `dotnet build` 0/0. 오너가 main scene에서 실제 전투 픽셀 표시와
  자동 턴 진행까지 확인했다. 결과 표시·로비 복귀·재전투는 [[BS-003-Battle-Completion-Lobby-Return]] 후속이다.
- **BS-001 BattleSession Runtime Boundary 전체 완료(Step 0~4)**([[BS-001-Battle-Session]],
  [[BS-001-Battle-Session-Completion-Review]] 판정: 완료, 결정 [[ADR-022-Battle-Session-Lifecycle]] accepted).
  사실은 [[Battle-Session-System]]이 보존한다.
  - `Assets/Script/Battle/`: `BattleSession : Node`가 `BattleRequest`(PackedScene+seed+PlayerPath)로 전투 씬을
    생성·소유하고 명시적 `Start`로 시작해 승패를 판정, scene-free 값 `BattleResult`를 C# event로 정확히 1회
    반환한 뒤 씬·정적 context를 정리한다. 상대 전멸=`Victory/OpponentsEliminated`, 지정 PC 사망(다른 Ally 무관)=
    `Defeat/PlayerDefeated`, mutual kill=Defeat 우선(OD5), 잘못된 구성=`Aborted/InvalidSetup`(deferred 1회).
  - 완료 판정은 event-identity(사망 유닛 `Survived=false`/`FinalHealth=0`, `Health` setter가 clamp 전 `Dead()`
    emit) + 세션 자체 상대 bookkeeping이며, teardown+event는 사망/물리 콜스택 밖 deferred다(재진입/tree 수정
    방지). `BattleFieldScene._ExitTree()`가 `==this`일 때만 정적 context를 null → 완료 handler의 즉시 다음 세션
    재진입이 stale singleton 없이 성립(2회 연속 실행 격리 검증).
  - `TurnHelper`는 명시적 lifecycle seam(`AutoStart`/`Configure`/`StartBattle`/`StopBattle`)으로 축소되고 현재
    유닛 사망 시 커서가 목록 처음으로 리셋되던 결함을 수정했다([[Turn-System]]). 신규 세션 경로의 참가자 준비·
    ammo 충전·승패는 `TurnHelper` 밖(세션)이 소유한다. RNG/FX tick은 U1 임시로 `TurnHelper`에 남는다.
  - 검증: `dotnet build` 경고/오류 0, `--import` exit 0, `bs001_step1`(27)/`bs001_step2`(79)/`bs001_step3`(45)
    ALL PASS, 회귀 cb001_step1~3·sk002_step1/3·st001_step1 ALL PASS. Known Regressions(cb001_step4/sk001)은
    로스터 rebaseline 소관으로 무관. 후속: encounter/party snapshot 확장, Faction/BattleRoster, CombatContext/
    RNG 이동, U3 등반 루프 정산·workspace 전장 scene 장착.
- **CB-001 Deterministic Combat Resolution 완료**(Step 0~5,
  [[CB-001-Deterministic-Combat-Resolution-Review]] 판정: 완료, 결정 [[ADR-017-Deterministic-Combat-Resolution]]):
  전투가 {시드, 초기 배치} 입력만으로 재현된다(1단계, 현 구조 유지).
  - 지속 효과는 `TurnHelper.AdvanceToNextTurn()` 직후 유닛 턴 시작 1회만 적용된다
    (`ITurnAffectedArticle.ApplyTurnStartEffects()` hook). 프레임 수·배속 종속 버그 수정.
  - `TurnHelper`가 seed 가능한 전투 전용 `CombatRng`를 소유하고 `PhysicalDamage` 크리티컬/데미지 롤이
    이를 소비한다. 전투 코드의 `GD.Rand*`/`new Random(`/`Guid.NewGuid` 직접 사용은 정적 가드가 막는다.
  - 전투 의사결정 동점자는 ADR-017 정규 순서(거리 → Y → X, 공용 구현 `SkillUtil`)로 고정:
    `GetTarget`, `GetChainTarget`(미타격 우선 → 정규 순서), `BehaviorTree_Move`/`MultipleMove` 경로
    선택(길이 → 거리² → Y → X). 턴 순서는 `Priority` → `SpawnIndex`(씬 트리 순회 순서) 안정 정렬.
  - 결정론 회귀: 실제 `battle_field.tscn` 이벤트 로그(턴 순서/HP 변화/사망 순서/최종 HP·타일) 기준
    같은 시드 완전 일치, 같은 시드 + 다른 배속(2 vs 6) 완전 일치, 다른 시드 상이(스모크).
  - 검증 자산: `Assets/Script/Tests/cb001_step1~4` 4종 헤드리스 테스트, 전부 ALL PASS.
  - 알려진 사실: 출하된 `battle_field.tscn`의 Opponent Puppet 4기는 BehaviorTree가 비어 있어 근접/단일
    이동/`PhysicalDamage` 경로는 프로덕션 씬에서 미사용(Step 4 테스트가 프로그램적으로 구성해 커버).
    프로덕션 시드는 아직 고정값 1(생성/기록 정책은 Follow-up). 2단계(시뮬/프레젠테이션 분리)는
    [[Open-Tasks]] Later 참고.
- **SK-001 Data-Driven Skill System Step 0 설계 리뷰 완료**([[SK-001-Data-Driven-Skill-System-Review]], 판정: Approved after design fixes).
  데이터 기반 스킬 방향(`SkillDefinition`/`SkillState`/`EffectBlock`/`TurnAction_Skill`), combo 보류,
  `Assets/Script/SkillSystem` 신규 위치, 기존 BT/TurnAction 경로 유지가 승인됐다. Step 1 구현 전 보완으로
  `TurnActionBase.Action()` non-virtual + private `_usedCost`, `BehaviorTree_TurnAction`의 Running Resource 보관,
  `CharacterArticle.CurrentTurnAction` 재개 계약을 깨지 않는 작은 compatibility seam을 Step 1 범위에 포함하기로 했다.
  다음 단계는 Skeleton + DamageBlock + Slash Data 구현이며, 기존 Attack 대비 phase 지점/RNG 소비/크리티컬/HP
  baseline과 SkillState isolation을 검증한다.
- **SK-001 Step 1 구현 완료, 코드 리뷰 완료**([[SK-001-Data-Driven-Skill-System]], [[SK-001-Data-Driven-Skill-System-Review]] 판정: Approved, minor fixes applied).
  `Assets/Script/SkillSystem`에 `SkillDefinition`/`SkillState`/`SkillContext`/`EffectBlock`/`DamageBlock`/`TurnAction_Skill`
  v0를 추가하고, `Assets/SkillData/slash.tres`로 `베기`(range 1, physical 10~20)를 데이터화했다.
  `TurnActionBase`는 `Init`/`Finish`/`Action` virtual seam을 제공하고, `CharacterArticle.CurrentTurnActionState`(`ITurnActionState`)가
  `TurnAction_Skill`의 유닛별 `SkillState`를 보관한다. 기존 legacy 3종 TurnAction은 유지된다. 검증:
  `dotnet build` 경고/오류 0, `sk001_step1_skill_system_test` ALL PASS, CB-001 Step 1~4 ALL PASS, `--import` exit 0.
  리뷰 후 `ITurnActionState` marker, 미지원 target side/selector fail-closed, magical legacy parity 주석을 반영했다. Godot 종료 시 ObjectDB/resource 시 누락 ERROR 로그는 남아 있다.
- **SK-001 Step 2 구현·코드 리뷰 완료**([[SK-001-Data-Driven-Skill-System]], [[SK-001-Data-Driven-Skill-System-Review]] 판정: 수정 후 완료).
  `SkillTargetSelector` 확장(`LowestHp`, `HighestHp`, `Self`) 및 `TargetSide` 필터링을 도입했다. `SkillDefinition`에 `WindupCost`를 추가하고 `TurnAction_Skill`에 `CastingPhase` 삽입을 통한 영창 시스템을 구축했다.
  영창 중 BT 평가는 `CharacterArticle.TurnPlay`를 통해 자연스럽게 스킵되며, 영창 상태는 `ITurnActionState.IsCasting`/`CharacterArticle.IsCasting`으로 SkillSystem 구체 타입 의존 없이 외부에서 구독할 수 있다.
  리뷰 중 P2 2건(LowestHp/HighestHp 동점 처리를 유클리드 거리→ADR-017 정규 순서(`ThenByCanonical`)로 교체, `IsCasting` 인터페이스 노출)·P3 1건(`SkillTargetSelector.Self` 미처리→시전자 확정)을 수정·재검증했다.
  레거시 `TurnAction_MagicBolt`와의 baseline 교차 검증과 유클리드/Manhattan 동점 구분 fixture(`A.TieCanonical`)를 포함한 헤드리스 테스트가 ALL PASS(`sk001_step1`·CB-001 Step 1~4 회귀 포함).
- **SK-001 Step 3 구현·코드 리뷰 완료**([[SK-001-Data-Driven-Skill-System]], [[SK-001-Data-Driven-Skill-System-Review]] 판정: 완료).
  `Mana : StatusElement`와 `SkillDefinition.ManaCost` 지불 게이트를 도입했다. 마나 부족 시 상태 변경 없이 `ActionState.Failure`(신규)를 내고, `BehaviorTree_TurnAction`/`CharacterArticle.TurnPlay`가 `BtStatus.Failure`로 매핑해 `BehaviorTree_Selector`가 다음 행으로 fallback한다. `ManaCost == 0`(slash/magicbolt)은 게이트 없음.
  `DamageBlock.HitChance`를 추가했다. `HitChance < 100`일 때만 `SkillContext.CombatRandRange`(단일 CombatRng)로 명중 판정하며 순서는 `명중 -> 크리티컬 -> 피해`, `HitChance == 100`은 hit roll 생략으로 기존 baseline RNG 스트림 보존. 명중 실패/성공 모두 `SkillState.Reports`(report sink)에 기록. 피해 금액+UI는 여전히 legacy `Damage`(migration 경계 `DamageBlock.ApplyDamage`에 문서화).
  `sk001_step3_mana_hit_test`(20 assertions) 및 `sk001_step1`/`sk001_step2`/CB-001 Step 1~4 회귀 전부 ALL PASS. 당시 `Mana`의 production scene 배선은 후속이었고, 현재는 ST-001 Step 2에서 완료됐다([[ST-001-Natural-Regen-Stats]]). 무대상 fail-closed와 마나 환불 정책은 이후 SK-002/[[ADR-021-Skill-Ammo-System]]에서 target-first 무소모 Failure/커밋 후 무환불로 확정됐다.
- **SK-001 Step 4a 구현·코드 리뷰 완료**([[SK-001-Data-Driven-Skill-System]], [[SK-001-Data-Driven-Skill-System-Review]] 판정: 완료). 사용자 승인으로 Step 4를 4a(이번)/4b(KnockbackBlock)로 분할.
  제어/버프 상태를 유닛별 `StatusController`(턴 카운터)로 도입(`StunTurns`/`BindTurns`·`BoundThisTurn`/`DamageDealtMultiplier`/`DamageTakenMultiplier`). 기존 `StatusAffect`(1턴 제어 apply/expire 붕괴) 대신 사용. `StunBlock`/`BindBlock`/`SelfBuffBlock`/`ManaDrainBlock` 4종 EffectBlock 추가.
  Stun=`TurnPlay` 시작 소비→즉시 스킵+영창 취소(무환불), Bind=`OnTurnStart` 틱→이동 노드(`BehaviorTree_Move`/`MultipleMove`) 차단, SelfBuff=주는 피해 배율, ManaDrain=마나 흡수. 피해 배율 hook은 `ArticleBase` virtual(기본 1.0, 롤 이후 곱셈→RNG 무이동)을 `CharacterArticle`이 `StatusController`로 오버라이드.
  `sk001_step4_status_control_test`(41 assertions, 컨트롤러 순수+통합 6종) 및 `sk001_step1~3`/CB-001 Step 1~4 회귀 전부 ALL PASS. 외부 리뷰 후 minor fix 2건 반영: `turns<=0` 버프 영구화 방지(early-return), 스턴 취소 테스트의 "Cast" 애니메이션 ERROR 제거.
- **SK-001 Step 4b(KnockbackBlock) 구현·코드 리뷰 완료**([[SK-001-Data-Driven-Skill-System]], [[SK-001-Data-Driven-Skill-System-Review]] 판정: 완료). Step 4(4a+4b) 전체 완료.
  `KnockbackBlock`: 대상을 시전자 반대 4방향(더 큰 축, 동률 X)으로 최대 `Distance`칸 밀되 맵 경계(`GetUsedRect().HasPoint`)·점유 칸(`GetArticle`)에서 중단. 최종 위치를 `TilePosition`으로 1회 설정 → `OnMove` → tilemap `_placedArticles` → (다음)`UpdateAStar` 갱신(별도 점유 테이블 없이 기존 파이프라인 재사용).
  `sk001_step4b_knockback_test`(17 assertions: 기본 넉백+OnMove+AStar/점유, 맵 경계 중단, 점유 충돌, 방향 카디널) 및 `sk001_step1~4`/CB-001 회귀 전부 ALL PASS. 외부 리뷰 후 테스트 격리를 맵 안 유효 칸(`ParkUnusedOpponents`)으로 바꿔 UpdateAStar out-of-bounds ERROR 제거.
- **SK-001 Step 5(ChainBlock 이관) 구현·코드 리뷰 완료**([[SK-001-Data-Driven-Skill-System]], [[SK-001-Data-Driven-Skill-System-Review]] 판정: 완료).
  `ChainBlock`: `TurnAction_ChainLightning`의 연쇄 대상 선택/피해를 데이터 블록으로 이관. 첫 대상=`ConfirmedTargets[0]`(=legacy `GetTarget`), 홉마다 `미타격 우선 -> ADR-017 정규 순서`(legacy `GetChainTarget` 동일 코드). 고정 `MagicalDamage`(RNG 미소비)를 DamageBlock처럼 단일 phase에 적용(legacy 다중 프레임 FX phase는 권위 상태 무변경이라 결과 동일).
  `sk001_step5_chain_test`: legacy ChainLightning과 ChainBlock의 대상별 HP가 완전 일치(opp1~3 각 32 피해, opp4 무피해), 미타격 우선 검증. `sk001_step1~4b`/CB-001 회귀 전부 ALL PASS. `TurnAction_ChainLightning`은 TempArticle3/PrincessKnight BT 배선이 남아 삭제 안 함(Step 6). 다음은 Step 6 Phase1 12종 `.tres`.
- **SK-001 Step 6(Phase1 데이터 팩) 구현·코드 리뷰 완료 → SK-001 전체 완료**([[SK-001-Data-Driven-Skill-System]], [[SK-001-Data-Driven-Skill-System-Review]] 판정: 완료).
  `Assets/SkillData/Phase1/`에 12종 `.tres`(검/격투/활/지팡이 × 3) 입력. 블록 확장 2건: `StunBlock.Chance`(강타 20%·소닉블로 35%, <100일 때만 CombatRng 1회), `SelfBuffBlock.Kind`(금강체 받는피해 0.5). 기본값 무효과라 Step 4a 회귀 유지.
  `sk001_step6_data_pack_test`: 12종 로드/검증 + 직렬화 round-trip(스크래치) + 기본기(베기)/강기(강타+넉백)/제어(금강체 Taken 0.5·속박 Bind3)/흡수(마나 흡수 ±16) 실행 + StunBlock Chance 0/100 결정론 + 연쇄 뇌격 3체. `sk001_step1~5`/CB-001 Step 1~4 회귀 전부 ALL PASS.
  근사/후속: 조준 사격 크리 보너스·연쇄 뇌격 per-hit 명중·마나 흡수 정확한 50%는 damage-result API 등 후속. 기존 3종 하드코딩 TurnAction 대체는 가능하나 BT 재배선/삭제는 별도 cleanup(밸런스 스왑·CB-001 회귀 재검증 필요).
- **SK-003 Skill-Based Character Scene 완료**([[SK-003-Skill-Based-Character-Scene]]). `Assets/Scenes/Character/SkillCaster.tscn`을 추가해 Phase1 지팡이 데이터 스킬을 실제 BT에 배선했다. 제한 스킬 `staff_chainlightning`(Battle ammo 1)을 먼저 시도하고, ammo/마나/무대상 Failure 시 무제한 `staff_magicbolt`로 fallback한다. 후속 수정으로 `TurnAction_Skill` 종료/Failure 시 stale `CurrentTurnAction`도 함께 정리한다(`D.current_action_cleared`). production 배치 여부와 별개로 `TurnAction_Skill` 종료/Failure 정리 계약은 회귀 테스트로 고정했다. 검증: `dotnet build` 0경고/0오류, Godot `--headless --path . --import` exit 0(기존 종료 시 resource-in-use 로그 유지).
- **SK-004 Usable Attack Gate 완료**([[SK-004-Usable-Attack-Gate]]). ammo/mana가 소진된 주력기가 `CharacterArticle.CalculatedAttackRange`에 계속 남아 기본기 사거리 접근을 막던 문제를 수정했다. `TurnActionBase.CanStart`/`TurnAction_Skill.CanStart`를 추가하고, `CharacterArticle.HasUsableAttack` 및 동적 usable attack range로 이동 판단이 현재 시작 가능한 공격만 보게 했다. `BehaviorTree_HasUsableAttack` decorator와 graph menu 항목을 추가해 공격 불가 분기(예: 도망 노드)를 붙일 준비를 마쳤다. 검증: `dotnet build` 0경고/0오류, `sk004_step1_usable_attack_test` ALL PASS, `sk002_step1_ammo_test`/`sk002_step3_reset_test` ALL PASS.
- **SK-002 Skill Ammo System 전체 완료(Step 0~4)**([[SK-002-Skill-Ammo-System]], [[SK-002-Skill-Ammo-System-Review]] 판정: 완료, 결정 [[ADR-021-Skill-Ammo-System]] accepted). 사실은 [[Skill-System]] "Ammo" 절이 보존한다.
  스킬별 **고정 보장 사용 횟수** ammo 도입. `SkillDefinition.AmmoResetScope`(`Unlimited`/`Battle`/`Expedition` 예약)+`Ammo`(기본 `Unlimited`/0 → 기존 `.tres` 무제한 로드, backward-compatible). remaining은 `CharacterArticle.SkillAmmoState`(순수 C#, `StatusController` 동형, `.tres` 미직렬화)가 소유 — Resource 미저장, per-unit 격리.
  소모 정책(ADR-021 §3): 유효성은 진입 전제조건(실패=상태 없이 `End`), 런타임 게이트(ammo/마나 `CanAfford`/무대상)만 무소모 `Failure`. 대상 락온(=커밋)에서 ammo 1+마나 동시 소모, 커밋 후 빗나감·스턴 취소는 무환불(의도된 전황, 오너 확정).
  battle-start 배선: `TurnHelper._Ready()`가 유닛마다 `ChargeBattleAmmo()`(BT raw 워크로 `TurnAction_Skill.Definition` 순회 → `Charge`). `Battle`=매 전투 full charge(이월 없음), `Expedition`=이월 없는 battle 처리. `TurnAction_Skill`을 배선한 유닛은 battle-start에서 제한 스킬이 full charge된다.
  Phase1 12종 `.tres`에 ammo 입력(기본기 무제한, 제한 8종 Battle+수치표). 검증: `sk002_step1_ammo_test`(40)·`sk002_step3_reset_test`(14) ALL PASS, `sk001_step1/3`·`cb001_step1`·`sk001_step6` A/B/C 회귀 유지, 빌드/`--import` clean. 후속: ammo HUD·save 영속·Expedition 실이월·loadout UI·기존 3종 하드코딩 TurnAction의 데이터 스킬 재배선.
- **ST-001 Natural Regen Stats 전체 완료(Step 0~3)**([[ST-001-Natural-Regen-Stats]], [[ST-001-Natural-Regen-Stats-Review]] 판정: 완료, 결정 [[ADR-019-Natural-Regen-Stats]] accepted). 사실은 [[Article-Status-System]]/[[Turn-System]]이 보존한다.
  `HealthRegen`/`ManaRegen`(`[GlobalClass, Tool]`, 정수 `Value`, 기본 0)은 `StatusElement`이자 `ITurnStartStatusElement`(`TurnStartOrder` 100/110, `ApplyTurnStart(ArticleStatus)`)다. 각자 `ArticleStatus.TryGetStatusElement<T>()`로 `Health`/`Mana`를 찾아 setter를 통해 회복을 적용한다(clamp·signal·HealthBar·사망 처리 재사용).
  `ArticleStatus.ApplyTurnStartStatusElements()`는 `TurnStartOrder` 오름차순(동점자는 타입 `FullName` ordinal)으로 실행하고 각 실행 전 `HasLivingHealth()`로 사망 유닛을 차단한다. 구체 regen 타입 지식이 없어 새 turn-start 스탯은 인터페이스 구현만으로 추가된다.
  `CharacterArticle.ApplyTurnStartEffects()` = `StatusController.OnTurnStart()` → turn-start 스탯 dispatch → 살아 있으면 `ApplyAffectingStatuses()`. 누락 스탯과 `Value == 0`은 no-op, 음수는 감소, 사망을 같은 hook 안에서 되돌리지 않는다. RNG 미소비.
  production 배선: `PrincessKnight` 60/8/0, `Puppet` 30/3/0, `TempArticle2` 40/5/0, `TempArticle3` 60/8/0(MaxMana/ManaRegen/HealthRegen). `battle_field.tscn`은 인스턴스별 `ArticleStatus`를 로컬 override하므로 그 5개(Ally=TempArticle3 값, Opponent 4명=Puppet 값)도 함께 배선했다. `HealthRegen = 0`이라 CB-001 결정론 회귀는 유지된다. 전투 UI 마나 바는 없다.
  검증: `dotnet build`, `--import`, `st001_step1_natural_regen_test`, `st001_step2_production_wiring_test`, CB-001 Step 1~4, SK-001 Step 3/6 ALL PASS.
## DialogueTool

- Step 1~8 구현 및 리뷰가 완료됐다.
- 에디터 그래프는 `nodes/connections`, 런타임은 `runtime_nodes/runtime_connections`를 사용한다.
- 런타임 실행기는 Start, Say, Choice, Branch, End, Variable, Expression을 처리한다.
- 에디터 UI 구현은 Editor Adapter로 이동했다.
- `current_node_changed`와 원격 디버거 메시지를 통해 실행 노드 하이라이트가 가능하다.
- `DialogueManager.play(resource)`로 게임 코드에서 대화를 실행할 수 있다.
- 연속 대화와 이전 UI의 지연 종료 signal에 대한 재진입 방어가 적용됐다.
- Say 텍스트에 줄바꿈이 있으면 이전 줄을 같은 대화창에 유지하면서 한 줄씩 누적 공개하고,
  마지막 줄 이후에만 다음 Flow로 진행한다([[DT-003-Say-Line-Paging]]).
- **DT-014 Step 1~2 구현 및 리뷰 완료(판정: 완료)**([[DT-014-Say-Line-Paging-UI-Regression-Review]]):
  위 Say 줄 누적 표시(DT-003)를 실제 `Dialogue_UI.tscn` 클릭 경로에서 검증하는 headless 테스트(`dt014_step1_say_paging_ui_test`)를 추가했다.
  실제 UI `ui.play(resource)` + `Button.pressed.emit()` 클릭 구동 방식을 사용했다.
  1차 리뷰 시 가변 프레임 델타로 발생하던 타이핑 처리 비결정성 문제(visible_ratio 변동)는 `ui.say.set_process(false)`로 타이핑 자동 연출을 중지시킴으로써 근본적으로 해결했다. 또한 Case 6.2 Choice flow 출력 포트 오배선(`_c(2, 0, 3, 0)`)과 주석 불일치도 완벽히 수정하였다.
  수정 후 연속 5회 루프 테스트 통과를 통해 결정성을 입증했고, 7가지 검증 케이스 모두 PASS 및 지정 회귀 GREEN을 확인했다.

## World State

- 타입 안전 World State 기반 DT-005 Step 1~6이 완료됐다([[DT-005-WorldState-Review]], 판정: 완료).
- `addons/world_core/world_state/`: `StateDefinition`/`StateSchema`(선언·validation·lookup),
  `WorldStateStore`(read/write/reset/`value_changed`, SAVE/SESSION lifetime + `reset_lifetime`,
  JSON snapshot export/import replace-load, atomic `apply_batch`, read/mutation provider facade).
- `DialoguePlayer`는 read 상태 provider를 주입받는다(`DialogueManager`→`DialogueUI`→`DialoguePlayer`).
  `/root`/PlayerData/save를 직접 조회하지 않고 주입 provider로만 상태를 읽는다.
- 허용 타입은 bool/int/float/String/StringName, snapshot INT는 JSON-safe `±(2^53-1)`로 제한.
- 헤드리스 테스트 `addons/world_core/world_state/tests/dt005_step1~6_*`로 검증(ALL PASS).
- 실제 Schema 작성과 Store API 사용법은 [[World-State-User-Guide]]에 정리돼 있다.
- 런타임 통합 DT-006 Step 0~5 완료([[DT-006-WorldState-Runtime-Review]], 판정: 완료).
  - `WorldState` autoload(`/root/WorldState`): 유효 6-key bootstrap Schema(`world_state_schema.tres`)로
    부팅 시 ready.
  - `WorldStateRuntime` autoload(`/root/WorldStateRuntime`): new game/load lifecycle coordinator.
    `start_new_game()`/`restore_game()`(=`restore_world_state()`)는 transactional(envelope pre-validation,
    실패 시 기존 상태 보존), `capture_world_state()`는 SAVE-only. SESSION은 새 게임/load에서만 default.
  - 결정: [[ADR-007-WorldState-Runtime-Lifecycle]]. autoload 이름은 class_name 충돌 회피로 `WorldState`.
- State Condition Dialogue 통합은 DT-008에서 완료됐다(아래 DT-008 항목,
  [[DT-008-State-Condition-Dialogue-Integration]]). State Set/Add Effect와 명시적 mutation provider는
  DT-009에서 완료됐다([[DT-009-State-Mutation-Dialogue-Effects]], [[DT-009-State-Mutation-Review]],
  [[ADR-010-State-Mutation-Dialogue-Effects]] accepted). 단일 key 값을 Data로 읽는 State Read Dialogue 노드는
  DT-013에서 완료됐다(아래 DT-013 항목). 미구현(후속): 실제 SaveGame file/slot 시스템(DT-006 adapter 소비).
  Step 1 구현·리뷰 완료: `WorldStateStore.add_state(key, delta) -> Dictionary`
  보고형 원자 Add API. INT/FLOAT strict, JSON-safe 도메인·overflow 거부, 값/signal 불변 실패, 실제 변경 시
  `value_changed` 1회. `dt009_step1_add_state_test` 20케이스 ALL PASS.
  Step 2 구현·리뷰 완료: `DialogueManager/UI.play(resource, read, mutation)` 세 번째 선택 인자 +
  `DialoguePlayer._mutation_state_provider`(read 권한 자동 승격 없음). `_run_effects`가 타입 디스패치로
  `portrait_*`는 UI 요청, `state_set`/`state_add`는 mutation provider 호출 + `state_mutation_evaluated`
  report signal(commit 후 1회, deep copy). provider 계약 = `{apply_state_batch, add_state}` duck-type 검증,
  누락(genuine null)은 `provider_missing`, 공급됐지만 못 쓰는 provider(freed/non-Object/arity/반환형 위반)는
  `provider_contract_invalid`, 모두 Flow 계속(SCRIPT ERROR 없음 — 호출 전 typeof/is_instance_valid/reflection
  arity+인자타입+typed array 원소타입(`Array[Dictionary]`만), 호출 후 반환 Dictionary+스키마 검증). mutation
  provider는 effect chain 시작 시 고정(listener 교체 무영향). Set은 `apply_state_batch` 재사용(authoritative diff),
  Add는 `add_state`. `EFFECT_TARGET_TYPES`에 `state_set/state_add` 추가(런타임·에디터 공유).
  `dt009_step2_runtime_mutation_test` 22케이스 ALL PASS, DT-004/005/006/008 회귀 ALL PASS.
  Step 3 구현·리뷰 완료: `StateSetDef`/`StateAddDef`(공통 추상 `StateEffectDef`, 비대기 leaf Effect
  노드) + 공유 `state_effect_editor_adapter`(key + type OptionButton + value/delta LineEdit + Effect 입력 포트).
  StateSet=5타입, StateAdd=INT/FLOAT만. literal은 capture에서 엄격 파싱(잘못된 입력은 String으로 보존),
  저장 검증이 타입 불일치를 차단하고 런타임은 변환 없이 Store가 `type_mismatch` 거부(조용한 0/false 변환 없음).
  `node_type_registry` 등록, 노드 목록 자동 노출("StateSet"/"StateAdd"). `dt009_step3_editor_roundtrip_test`
  (실제 `dialoguetool_main.tscn` fixture, A~F) ALL PASS: 목록 노출, 포트, capture→save→reload→recapture에서
  Definition typeof 직접 단언/연결(Say 소스 포함) 보존, authored 그래프 런타임 실행(gold 100→set 200→add 205),
  잘못된 literal 저장 차단+런타임 거부.
  Step 3b 구현·리뷰 완료: **Choice 항목별 Effect authoring**. Choice 출력 포트 = flow + 항목별 effect +
  **전용 공통 effect 포트**(flow/data index 보존). 항목별 연결만 `choice_index` 보존, 선택 시 해당 항목 + 공통
  Effect만 실행. 공통 연결은 전용 공통 포트로 정규화돼 저장/재로드/재캡처 후에도 choice_index 없이 유지(항목0
  오염 방지 — Step 3b 리뷰 P1 수정), 잘못된 choice_index는 fallback 없이 건너뜀. resize 시 항목별·공통 연결
  모두 remap 보존. `dt009_step3b_per_choice_effect_test`(A~G) ALL PASS, DT-004/DT-008 Choice 회귀 유지.
  Step 4 구현·리뷰 완료(제품 코드 변경 없음): 실제 `DialogueManager→UI→Player→WorldStateStore`
  전체 경로 e2e(`dt009_step4_e2e_completion_test` A~G)로 Choice 선택→항목별 mutation→Branch(state_condition)가
  변경값을 즉시 읽는 흐름, 반복/latest-wins(폐기 provider mutation 0회)/provider 누락/read-only 실패(값 불변+Flow
  계속)/에디터 authored 왕복 실행을 검증. 전체 회귀 matrix 30 scene ALL PASS(DT-004~009), `--import` 0 오류.
- DT-007 Step 1~4 구현·검증·리뷰 완료([[DT-007-Condition-Review]], 판정: 수정 후 완료).
  - `addons/world_core/world_state/condition/`: `ConditionClause`(@abstract base),
    `StateCondition`(leaf), `ConditionGroup`(ALL/ANY/NOT, recursive `Array[ConditionClause]`),
    `ConditionSet`(top-level asset), `ConditionValidator`(구조 검증), `ConditionEvaluator`(pure-read 평가).
  - Step 1 validator는 iterative DFS로 null/unknown/empty/NOT arity/cycle/alias/depth(64)/node(4096)/key/
    operator/expected/ordered 타입을 검사하고 `{valid, errors[{code,path,key,message}], error_codes,
    node_count}` deep copy를 반환한다. provider read 없음. spike로 `@abstract` 인식 + 재귀 `.tres` 왕복 확인.
    추가 코드 `logic_invalid` 코드 리뷰 비준됨. 판정: Step 1 완료.
  - Step 2 evaluator는 2단계 평가다: validator(read 0)를 먼저 통과해야 주입 provider의 `has_state`/
    `read_state`만으로 트리를 재귀 평가한다. strict typeof 비교(암시적 변환 없음), evaluation-local key
    cache(miss 포함 1회 read), non-short-circuit 전체 trace, fail-closed errored 전파(NOT/ANY가 errored
    child를 pass로 안 바꿈), `{passed, valid, errors, trace, read_count}` deep copy. mutation/autoload 미접근.
  - Step 3은 실제 `WorldStateStore`를 read provider로 주입하는 통합 검증(제품 코드 변경 없음). Step 4는
    end-to-end 완료 검증: 대표 RPG `.tres`를 재로드해 in-memory set과 **동일한 report**(passed/valid/
    errors/trace/read_count 문자열 표현 일치)를 실제 Store에서 내고, load lifecycle(restore_world_state로
    SESSION reset 직접 단언)·성능 sanity(node 4096, 같은 key read 1)·fail-closed Store 불변을 확인했다.
  - 검증: `condition/tests` `dt007_step1`(24)/`step2`(23)/`step3`(11)/`step4`(e2e)/`spike` ALL PASS.
    전체 회귀 DT-004(5)+DT-005(6)+DT-006(5) ALL PASS, editor `--import` 0 오류 — 합계 21 headless.
  - 후속 State Condition Dialogue node 입력 계약은 [[DT-007-Condition-Review]]에 문서화했다.
    [[DT-008-State-Condition-Dialogue-Integration]] Step 0은 Approved after design fixes로 승인됐고,
    [[ADR-009-State-Condition-Dialogue-Consumption]]을 accepted로 확정했다.
- DT-008 Step 1(Runtime State Condition Data Node) 구현·리뷰 완료(판정: 수정 후 완료)
  ([[DT-008-State-Condition-Dialogue-Integration]] Step 1 결과).
  - `WorldStateConditionDef`(Data Definition, runtime type `state_condition`)가 `ConditionSet`을
    runtime params로 보존한다. `DialoguePlayer._get_data_value(node_id, consumer_node_id, visited)`의
    `state_condition` 분기가 주입된 원본 `_read_state_provider`를 `ConditionEvaluator.evaluate`에 직접
    전달하고(facade 재포장 금지) `report.passed`를 boolean Data로 반환한다.
  - `condition_evaluated(condition_node_id, consumer_node_id, report)` signal을 평가당 1회 발행한다.
    consumer는 입력 포트를 직접 소유한 노드(Branch=branch id, expression 중첩=expression id)다.
    동기 signal listener가 분기를 못 바꾸도록 `passed`를 발행 전에 캡처하고 signal에는 `report.duplicate(true)`
    deep copy를 넘긴다(1차 리뷰 P1 수정). provider 미지정/null·invalid set/missing key/타입 오류는 모두
    false이고 구조 오류는 read_count==0이다.
  - 헤드리스 `addons/world_core/dialogtool/RunTime/tests/dt008_step1_state_condition_test`(15 사례, P1 회귀 O 포함)
    ALL PASS.
- DT-008 Step 2(Editor Authoring and Resource Round-trip) 구현·리뷰 완료(판정: 수정 후 완료).
  - `WorldStateConditionNode`(전용 GraphNode 씬) + `condition_set_picker`(ConditionSet `.tres` 드롭) +
    `world_state_condition_editor_adapter`(boolean output 슬롯 + capture/apply). `node_type_registry`에
    `state_condition` 어댑터 등록. boolean output은 Branch boolean 조건 입력과 동일 타입이고 editor.gd의
    `data↔boolean` 교차 호환으로 data 입력에도 연결된다.
  - 선행 F4 spike(`dt008_step2_snapshot_spike`)로 `runtime_nodes` Dictionary에 2중 중첩된 ConditionSet이
    external(`ext_resource`)/inline(`sub_resource`) 양쪽에서 `.tres` 왕복 보존됨을 확인(Design Deviation
    없음). 에디터 왕복 `dt008_step2_editor_roundtrip_test`로 외부 참조/연결 capture→save→재로드 보존,
    null 저장+런타임 fail-closed를 검증. 1차 리뷰 P2(빈 노드 노출)는 정식 노드 등록으로 해소.
  - 전체 회귀(DT-008 step1, DT-004 step1~4, DT-005 step5, DT-007 step1~4) + editor import ALL PASS.
    Branch e2e는 Step 3, 조건부 Choice는 Step 4~5 범위다.
- DT-008 Step 3(Branch End-to-End Integration) 구현·리뷰 완료(판정: 수정 후 완료). **제품 코드 변경 없음**(통합
  검증 단계). 실제 `DialogueManager.play(resource, store)` → UI → Player provider 주입 경로에서 통합
  그래프 `Start → Branch(state_condition) → Say TRUE/FALSE → End`가 실제 `WorldStateStore` 값에 따라
  분기함을 검증했다(`dt008_step3_branch_e2e_test`): set/reset/snapshot restore 후 Store 최종값 일치,
  provider 미지정/조건 오류 false Flow(크래시·자동 true 없음), condition_evaluated node/consumer/report
  검증, 반복·같은 프레임 교체(latest-wins) provider 미혼입. DT-004/005/006/007/008 전체 24 headless
  ALL PASS.
- DT-008 Step 4(Conditional Choice Runtime Mapping) 구현·리뷰 완료(판정: 수정 후 완료).
  `DialoguePlayer`가 Choice 진입 시 항목별 Data 입력(port i+1)을 조건으로 평가해 visible list와
  `_choice_visible_map`(visible_index → 원래 항목 index = 원래 flow 출력 port)을 구성한다. Data 입력 없으면
  항상 표시(레거시 호환), 조건은 진입 시 1회만 평가하고 대기 중 재평가하지 않으며 재진입에서만 갱신.
  `select_choice(visible_index)`는 mapping 범위를 먼저 검증해 범위 밖이면 대기 유지(Flow 불변), 통과 시에만
  원래 port로 effects/Flow 커밋(F5). all-hidden 명시 종료, invalid/error 조건 숨김, no-input 레거시는 identity.
  내부 Data 평가는 `_eval_data → {value, errored}`로 전파되어, errored 조건이 Expression(`not c`/`c or true`)을
  통해 true로 뒤집히지 못하고 Branch/Choice에서 fail-closed된다(1차 리뷰 P1 수정, ADR-008 error-dominance).
  헤드리스 `dt008_step4_conditional_choice_test`(L1~L4 error-dominance 포함) + DT-004 Choice 회귀 ALL PASS.
- **DT-008 Step 0~5 완료**([[DT-008-Choice-Integration-Review]], 판정: 완료 — Step 1~4 수정 후 완료,
  Step 5 Approved after design fixes). State Condition Data 노드(`state_condition`)가 boolean Data로 Branch와
  조건부 Choice를 같은 `ConditionSet`/`ConditionEvaluator` 계약으로 제어한다.
- DT-008 Step 5(Conditional Choice Editor and Completion Review) 완료.
  **제품 코드 변경 없음**(검증 + 문서). 실제 `dialoguetool_main.tscn` fixture로 (1) State Condition boolean
  output ↔ Choice 항목별 Data 입력 연결이 저장/재로드 후 동일하고 Choice resize(3→2)가 남은 항목의 조건/Flow
  연결을 잘못 재배치하지 않으며 사라진 항목 연결만 드롭함을 검증하고, (2) 복합 `Branch(state_condition) +
  conditional Choice`가 실제 `WorldStateStore` 상태에 따라 같은 evaluator 계약으로 동작함을 e2e로 확인했다
  (`dt008_step5_completion_test`). 전체 회귀 26/26 GREEN(DT-004~008), editor `--import` 0 오류.
  Step 1~4 판정 수정 후 완료, Step 5 Approved after design fixes([[DT-008-Choice-Integration-Review]]).
- **DT-010 Step 0 설계 리뷰 완료(판정: Approved after design fixes,
  [[ADR-012-Dialogue-Debug-Preview-Provider]] accepted).**
  현재 에디터 Play는 `dialoguetool_main.gd`가 별도 Godot 프로세스를 띄우고 서브프로세스의
  `DialoguePlayer._ready()`(debug-hint)가 `start_dialogue.call_deferred`를 **직접** 호출한다
  (`DialogueManager`/`DialogueUI` 미경유 → read/mutation provider 미주입). 확정한 설계:
  provider source=**addon example store**(후보 A, `examples/world_state_schema_example.tres`로
  `WorldStateStore` 구성해 read·mutation 양쪽 주입), parse-safe(`class_name`만 참조, `WorldState`
  bare 식별자 금지 — fresh 프로젝트 parse error 방지), 주입 위치=`DialoguePlayer._ready` debug 분기
  (`DialogueManager.play` 미사용 — 이중 UI 회피), lifecycle=프로세스 격리 의존(Play=새 프로세스=default,
  coordinator 없음). 고정 example schema 한계(게임 schema key 미해결)는 옵션 C로 보완 예정
  ([[DT-010-Dialogue-Debug-WorldState-Preview]]).
- **DT-010 Step 1 구현·리뷰 완료(판정: 완료).** debug Play 서브프로세스에서 preview WorldState provider를
  주입하는 코드 경로 추가. 신규 `addons/world_core/dialogtool/RunTime/dialogue_debug_preview_provider.gd`
  (`DialogueDebugPreviewProvider.make_preview_store(schema_path)` static helper, ADR-012 D5 = 신규 helper
  파일 확정)가 `addons/world_core/world_state/examples/world_state_schema_example.tres`로 preview 전용 `WorldStateStore`를 구성하고,
  `DialoguePlayer._ready()` debug 분기가 `start_dialogue` 직전 `_inject_debug_preview_provider()`로 read·
  mutation provider 양쪽에 같은 인스턴스를 주입한다. schema load/타입/init 실패는 `push_error`+null로
  fail-closed(provider 미주입), parse-safe(bare `WorldState` autoload 식별자 0건, `class_name`/string path만).
  헤드리스 `dt010_step1_debug_preview_provider_test` ALL PASS(helper 성공/실패 경로, 실제 debug 분기
  Take→StateAdd+50→state_condition pass→"Rich" / Leave→mutation 0→"Poor", 프로세스 격리 proxy, 일반
  경로 미주입), 회귀 dt008_step3/5·dt009_step2/4 ALL PASS, `--import` 0 에러. 일반 runtime
  `DialogueManager.play(dialogue, WorldState, WorldState)` 경로 무회귀.
  Step 1 코드 리뷰 완료(판정: 완료 — P0/P1/P2 없음, P3 orphan store는 프로세스 격리 lifecycle로 해소).
- **DT-010 Step 2(Preview Lifecycle and Reset Policy) 구현·리뷰 완료(판정: 완료, 제품 코드 변경 없음).**
  ADR-012 D4 프로세스 격리 정책을 검증 전용으로 확정(coordinator/`start_new_game`/별도 reset 로직 미도입).
  헤드리스 `dt010_step2_preview_lifecycle_test` ALL PASS: 반복 boot(연속 프로세스 proxy)에서 매 run
  affinity default 0→Take 50·별도 store 인스턴스(이전 mutation 미오염), store re-init이 default 복귀 직접
  단언, 1회 run 내 add+50×2=100 누적→condition 반영, bare store가 SAVE+SESSION 모두 default(coordinator
  불필요), preview store≠/root/WorldState(실제 save state 격리). 회귀 dt010_step1·dt006_step1 ALL PASS,
  `--import` 0 에러. Step 1 리뷰 P3(orphan store)는 프로세스 격리=정리 경계로 해소(프로세스 내 명시적 free
  없음, teardown 회수).
- **DT-010 Step 3(Editor Play E2E and Docs) 구현·리뷰 완료(판정: 완료).** 실제 `Dialogue_UI.tscn`(child
  `DialoguePlayer`)을 debug-hint로 self-start하는 e2e `dt010_step3_editor_play_e2e_test` ALL PASS:
  Take→StateAdd+50→state_condition true→`ui.say.text` 실제 렌더 "Rich"·affinity 50 / Leave→mutation 0→
  "Poor"·affinity 0, UI 공존에서 provider 주입 + 이중 start/provider 충돌 없음(Step 1 bare player 대비
  UI 렌더 경로 추가 커버). 문서: User Guide 13절 "에디터 Play로 WorldState 미리보기 테스트하기" 추가
  (자동 주입/lifecycle/진단 로그/고정 example schema 한계 = 게임 schema key는 state_missing/unknown_key),
  14절 cross-reference. 옵션 C(게임 schema 경로 debug 주입)는 parse-safe 가능하나 범위 초과로 후속.
  전체 회귀 18/18 ALL PASS(DT-004×5, DT-008×6, DT-009×4, DT-010×3), `--import` 0 에러.
  2026-06-17 완료 리뷰에서 DT-010 step1~3 + dt008_step3 + dt009_step4 선택 회귀 5/5 PASS,
  headless `--import` exit 0을 재확인했다. **DT-010 완료**([[DT-010-Dialogue-Debug-WorldState-Preview-Review]]).
  옵션 C(게임 schema 경로 debug 주입)는 독립 설계가 필요한 확장이므로 [[Open-Tasks]] Later에 유지한다.
- DT-011 Step 0 설계 리뷰 완료(판정: Approved after design fixes,
  [[ADR-011-DialogueWorldState-Addon-Packaging]] accepted). 결합 표면 확정: 제품 코드 결합은 condition
  `class_name` 하나(경로 독립)뿐, mutation/store/runtime은 provider 주입으로 decoupled. 결정: 후보 B
  (`addons/world_core/world_state/` 하위모듈), 런타임 autoload 3종은 호스트 수동 등록(순서 보장),
  addon은 example schema만 포함.
- **DT-011 Step 1 구현·리뷰 완료(판정: 완료).** WorldState 폐쇄집합(state_definition/state_schema/store
  (.gd/.tscn)/runtime/condition/* + WorldState·Condition tests)을 `git mv`로
  `Assets/Script/gds/world_state/` → `addons/world_core/world_state/`로 **이동**(복사·shim 없음,
  `.uid` 동반, 원본 디렉터리 제거 확인). path rewrite: `project.godot` autoload 2종, store.tscn/
  schema.tres ext_resource, 이동한 테스트 `.tscn`/`.tres` ext_resource + `.gd` const
  (`SCHEMA_PATH`/`STORE_SCENE`/`RUNTIME_SCRIPT`/`CLAUSE_SCRIPT`), addon dt008_step1/3/5 `SCHEMA_PATH`,
  `affinity_ge_10.tres`(Step 1 당시 `addons/dialogtool/Test/`, Step 2에서 `examples/`를 거쳐, WC-001에서 `addons/world_core/dialogtool/examples/`로 이동) condition
  ext_resource. schema.tres uid
  `uid://urle8xa2dmc` 보존. headless `--import` 0 parse 에러 + class_name 중복 0, 회귀 20 scene
  (DT-005×6, DT-006×5, DT-007×5, DT-008 step1/3/5, DT-009 step4) ALL PASS. 제품 코드/테스트/리소스에
  stale path 0(`.godot`/`.idea` 캐시 제외).
- **DT-011 Step 2 구현·리뷰 완료(판정: 완료).** dialogtool path 정규화. addon 테스트 `SCHEMA_PATH`는 Step 1에서
  이미 정규화됐고, 고유 작업은 example ConditionSet 이동: `addons/dialogtool/Test/affinity_ge_10.tres`
  → `addons/world_core/dialogtool/examples/affinity_ge_10.tres`(`git mv`, uid `uid://bwsq70tpasvaw` 보존). 비게 된
  `Test/` 디렉터리 제거, 루트 샘플 `test.tres`의 ext_resource path 갱신. 파일 내부 condition ext_resource는
  Step 1에서 이미 새 경로. headless `--import` 0 parse 에러, DT-004/008/009 회귀 15 scene
  (DT-004 step1~4+pipeline, DT-008 step1~5+spike, DT-009 step2/3/3b/4) ALL PASS.
- **DT-011 Step 3 구현·리뷰 완료(판정: 완료).** examples/migration/docs. (1) example schema 개명: `git mv`
  `world_state/world_state_schema.tres` → `examples/world_state_schema_example.tres`(uid `uid://urle8xa2dmc`
  보존), store.tscn + 테스트 6개 `SCHEMA_PATH` 재작성. store.tscn이 example을 가리켜 out-of-box 부팅 유지,
  게임 schema는 호스트가 교체(ADR-011 D5). (2) sample dialogue: 루트 `test.tres` 채택 →
  `examples/sample_dialogues/sample_world_state_dialogue.tres`(state_condition 분기 + state_add(+50 affinity)
  데모, example schema·ConditionSet 소비). (3) `addons/world_core/dialogtool/README.md` 신규: 설치(autoload 순서
  DialogueManager→WorldState→WorldStateRuntime, DialogueToolUtil은 플러그인 자동등록)·게임 schema 교체·기존
  프로젝트 마이그레이션 문서. headless `--import` 0 parse 에러, 명시적 load check(sample 8 nodes/schema
  valid 6 keys/ConditionSet ok) + schema 의존 회귀 8 scene ALL PASS.
- **DT-011 Step 4 구현·최종 리뷰 완료(판정: 완료, 제품 코드 변경 없음).** 통합 matrix + 수용 검증. 전체 DT-004~009
  **32/32 scene ALL PASS** + `--import` 0 parse 에러(stale 경로 0, `.godot`/`.idea` 캐시 제외). **Fresh-project
  수용 테스트**: 임시 빈 프로젝트에 `addons/world_core/dialogtool/`만 복사+autoload 등록+plugin enable → `--import` 0 에러
  → 수용 회귀 7/7 PASS(autoload boot, ConditionSet 분기, StateAdd/Set, Choice→state_add→Branch 전체 경로) →
  검증 후 삭제. autoload 실패 시나리오 크래시 0(잘못된 순서도 Godot 4.6.3 batch _ready로 store 해석됨, WorldState
  누락 시 graceful not-ready, provider_missing fail-closed). **발견**: `dialogue_player/manager.gd`가
  `DialogueToolUtil` autoload에 parse-time 의존(기존 설계) → 순수 헤드리스/CI 설치는 이 autoload도 등록해야
  parse됨 → README "헤드리스/CI 주의"로 문서화. DT-010 재개 구조 확정(addon 내부 example provider로 자급).
  **DT-011 Step 1~4 구현·검증·최종 완료 리뷰 완료**([[DT-011-DialogueWorldState-Addon-Packaging-Review]]).
- **DT-012 Step 0 설계 리뷰 완료(판정: Approved after design fixes).**
  `WorldStateCondition` 노드가 현재 그래프 위에서 `ConditionSet` path/inline 여부만 보여 조건 의미를 즉시
  알기 어렵다. 확정 방향은 provider를 읽지 않는 validate-first ConditionSet summary formatter와
  `WorldStateConditionNode` summary/tooltip/invalid 표시다. invalid/null은 description보다 우선 표시하고,
  valid 조건에서만 description을 우선한다. int/float·String/StringName 표기는 구분하고, 긴 summary는
  노드 폭이 폭주하지 않도록 잘림+tooltip으로 처리한다. inline ConditionSet tree editor, schema-aware picker,
  trace inspector는 후속 범위로 둔다([[DT-012-Condition-Authoring-UX]]).
- **DT-012 Step 1(Condition Summary Formatter) 구현·리뷰 완료(판정: 수정 후 완료).** provider-free helper
  `ConditionSummary`(`addons/world_core/world_state/condition/condition_summary.gd`)를 추가했다.
  public/static `ConditionSummary.summarize(condition_set, options := {}) -> Dictionary`,
  반환 `{ valid, summary, full_summary, tooltip, error_codes, errors }`. validate-first로
  `ConditionValidator.validate`를 먼저 호출하고 null/invalid면 트리를 순회하지 않는다(`condition_set==null`
  → `No ConditionSet`, structural invalid → `Invalid: <대표 code>`). valid일 때만 bounded recursion으로
  leaf=`key <op> literal`, group=`ALL/ANY/NOT(...)`(children 순서 보존). 표시 전용 operator 기호
  (`==`,`!=`,`<`,`<=`,`>`,`>=`)/logic 라벨 맵으로 ADR-008 trace 문자열을 재사용하지 않는다. literal은
  INT `10`/FLOAT `10.0`/String `"x"`/StringName `&"x"`/bool `true|false`를 구분 표기한다. 긴 summary는
  `max_length`(기본 80, options override) 잘림+ellipsis, `full_summary`는 전체 보존. description은
  structural valid일 때만 우선(`summary`=description, `full_summary`=구조 요약), invalid/null은 무관하게
  invalid 우선. provider read 0. String/StringName literal은 `\`/`"`/`\n`/`\r`/`\t` escape로 따옴표·
  제어문자가 들어가도 summary가 깨지지 않는다(코드 리뷰 P2 수정). 검증: `dt012_step1_condition_summary_test`
  (14 시나리오, 완료 조건 1~12 + escaping 회귀) ALL PASS, DT-007 step1/step2 회귀 ALL PASS, `--import`
  0 에러. 코드 리뷰 판정 **수정 후 완료**(P0/P1 없음). UI 표시(`WorldStateConditionNode`)는 Step 2 범위
  ([[DT-012-Condition-Authoring-UX]]).
- **DT-012 Step 2(WorldStateCondition Node Display) 구현·리뷰 완료(판정: 완료).** `WorldStateConditionNode`에
  전용 `SummaryLabel`을 추가하고 Step 1 `ConditionSummary`를 표시에 연결했다. `_refresh_summary()`가
  `ConditionSummary.summarize(picker.condition_set)`로 label.text=요약, tooltip=full summary(+외부 `.tres`
  path), invalid/null은 `modulate` 빨강 계열로 그래프 위 구분. 갱신 시점은 adapter apply/load
  (`set_condition_set`)·picker drop·clear뿐(live external edit 구독 없음). picker는 path 유지, summary는 별도
  label. label `clip_text`+`text_overrun_behavior=ellipsis`+`custom_minimum_size`로 긴 요약에서도 노드 폭
  폭주 방지. SummaryLabel은 delete_button(slot0)/HBoxContainer(slot1 boolean output) 뒤 slot2라 boolean
  output(port 0) 인덱스 회귀 없음. capture/runtime params·adapter 미변경. 검증:
  `dt012_step2_node_display_test`(A~E, 실제 `dialoguetool_main.tscn` fixture) ALL PASS, 회귀 DT-008
  step2/3/5 + DT-012 step1 ALL PASS, `--import` 0 에러. User Guide 갱신은 Step 3 범위
  ([[DT-012-Condition-Authoring-UX]]).
- **DT-012 Step 3(Docs and Completion Review) 구현·리뷰 완료(판정: 완료).** [[DialogueTool-User-Guide]]
  §6 State Condition에 "그래프 위 조건 요약 표시 (DT-012)" 절 추가(자동 요약·literal 표기·description 우선·
  null/invalid 구분·tooltip·갱신 시점·후속 한계), [[DialogueTool]] 시스템 문서에 `ConditionSummary`
  validate-first 표시 사실 추가, Open Tasks Later에 inline tree editor·schema-aware picker·trace inspector를
  DT-012 후속으로 유지. 검증: 전체 회귀 DT-004(5)+DT-007(4)+DT-008(5)+DT-010(3)+DT-012(step1/step2)
  **19/19 scene ALL PASS**, `--import` 0 parse 에러. 2026-06-17 완료 리뷰에서 DT-012 step1/step2 +
  dt007_step1/2 + dt008_step2/5 + dt010_step3 선택 회귀 7/7 PASS, headless `--import` exit 0을 재확인했다.
  **DT-012 완료**([[DT-012-Condition-Authoring-UX-Review]]).
- **DT-013 State Read Data 노드 Step 0 설계 리뷰 완료(판정 Approved after design fixes, design fixes 반영 완료).**
  목표는 WorldState 단일 key 값을 Dialogue Data Flow에 공급하는 `state_read` leaf Data node다
  ([[DT-013-State-Read-Data-Node]], [[ADR-015-State-Read-Data-Node]]). 설계상 output port는 현재 그래프 타입
  체계에 맞춰 generic `data`로 고정하고, `value_type`이 런타임 strict expected type 역할을 한다. provider는
  ADR-009와 동일하게 주입된 read provider만 소비하며, 실패는 구조화 report + Data error-dominance로
  fail-closed한다. 리뷰 후 `read_state` 호출 전 계약 검증, report sentinel(`TYPE_NIL/null`), `StateSchema.KEY_PATTERN`
  기반 key validation, 손상 key Variant fail-closed 테스트 조건을 설계에 반영했다.
  **DT-013 Step 1(Runtime State Read Evaluator) 구현·리뷰 완료(판정: 완료).** `DialoguePlayer`만 변경
  (editor/Definition/Adapter/Registry/`.tscn`/`.tres` 무변경, Step 2 범위 유지). `state_read_evaluated(read_node_id,
  consumer_node_id, report)` signal + `_eval_data`의 `state_read` 분기 + `_evaluate_state_read`/`_finish_state_read`
  helper 추가. read provider 계약은 주입된 `_read_state_provider`만 직접 소비하고(facade 재포장 없음),
  `state_set/state_add`와 같은 **`as Object` 캐스트 없는** 안전 패턴(`_is_valid_read_provider`: typeof +
  is_instance_valid + reflection arity/첫 인자 타입 + has_state 선언 반환형, 런타임 non-bool은 호출부 재확인)으로
  검증한다(`ConditionEvaluator._read_provider_contract_error`는 freed Object를 `p as Object`로 캐스트해 SCRIPT
  ERROR가 나므로 재사용 안 함). 검사 순서 = key 정규화(String/StringName만 StringName, 그 외 `key_invalid`,
  provider 미접촉) → null `provider_missing` → 계약 `provider_contract_invalid`(read_state 위반도 호출 전 차단) →
  has_state 런타임 bool → `state_missing`(read_state 호출 0) → read_state + strict typeof(`actual_type_mismatch`,
  암시 변환 없음). 실패={value:null, errored:true}로 Branch/Choice/Expression error-dominance fail-closed, 성공=
  {value, errored:false}. signal은 평가당 1회 `report.duplicate(true)`(반환값은 발행 전 확정 — listener 변조
  무영향). report sentinel: 값 미읽기 실패 `actual_type=TYPE_NIL/value=null`, type mismatch는 실제 타입/값 보존
  (반환 Data value는 null). 검증: `dt013_step1_state_read_test`(A~N 14 시나리오) ALL PASS, SCRIPT ERROR 0,
  실제 `WorldStateStore` 5타입 success 포함. 회귀 dt008_step1/4/5·dt009_step2·dt010_step1 ALL PASS, `--import`
  0 parse error.
  **DT-013 Step 2(Editor Authoring and Resource Round-Trip) 구현·리뷰 완료(판정: 완료).** editor authoring 표면만
  추가(런타임 무변경). `WorldStateReadDef`(Data Definition, `key`/`value_type`, `get_runtime_params -> {key,
  value_type}`, provider-free `validate_structure` = value_type 허용 5타입 + key empty/`StateSchema.KEY_PATTERN`
  형식, `type_label`/`READ_VALUE_TYPES`), `WorldStateReadNode`(key LineEdit + type OptionButton + summary
  `<key> : <TYPE>`/`No State Key`), `world_state_read_editor_adapter`(generic data output slot + params↔노드 접근자),
  `node_type_registry`에 `state_read` 등록, `editor.gd._validate_runtime_snapshot`에 `WorldStateReadDef.validate_structure`
  저장 차단 분기 추가(StateEffectDef literal 검증과 동일 패턴). output port는 generic `data` 1개 고정(ADR-015 D2),
  `editor.gd` data↔boolean 호환으로 Branch/Choice boolean 입력에 연결. key validation source of truth =
  `StateSchema.KEY_PATTERN`(ConditionValidator 재사용). **명명**: 노드 목록/타이틀은 class_name에서 "Def"를 떼어
  도출되므로 "State Read"(공백) 불가 — WorldState 계열 규칙대로 `WorldStateReadDef → "WorldStateRead"`로 노출
  (ADR "State Read"는 개념 명칭). 검증: `dt013_step2_editor_roundtrip_test`(A~F: 노드 목록/registry, key·type
  params 보존, data output 1개 + data↔boolean + Branch 입력 연결, invalid key matrix(`quest`/`Quest.main`/
  `quest..main`/`1quest.main`/"")·value_type 차단, summary, `.tres` cache-ignore 왕복) ALL PASS, SCRIPT ERROR 0.
  회귀 dt013_step1·dt009_step3·dt008_step2/step5·dt012_step2 ALL PASS, `--import` 0 parse error.
  **DT-013 Step 3(End-to-End Integration) 구현·리뷰 완료(판정: 완료, 제품 코드 변경 없음).** 실제
  `DialogueManager → DialogueUI → DialoguePlayer` provider 주입 경로에서 state_read가 값 supplier로 동작함을
  e2e로 확인(`dt013_step3_e2e_test` A~G ALL PASS, SCRIPT ERROR 0): `State Read(INT)→Expression("x>5")→Branch`
  (7→TRUE/5→FALSE, consumer=expression), `State Read(BOOL)→Branch`(true/false), `State Read(BOOL)→Choice 항목
  조건`(true→["A","B"]/false→["B"]), provider 미지정 `provider_missing` fail-closed, unknown key `state_missing`
  + store 불변, type mismatch(FLOAT를 INT로) `actual_type_mismatch` + store 불변, **debug preview store**
  (`make_preview_store()`)에서 example key 읽힘 + 없는 game key는 `state_missing`으로 닫힘(DT-010 read provider와
  충돌 없음). type mismatch는 errored Data를 Branch에 직접 공급해 검증(비교 연산자가 null에 닿는 Expression
  경로의 error-dominance는 dt013_step1[K] or/not로 별도 검증 — 모든 null Data 입력 공통 엔진 동작). 회귀
  dt013_step1/step2·dt008_step3·dt009_step4·dt010_step3 ALL PASS, `--import` 0 parse error.
  **DT-013 Step 4(Documentation and Completion Review) 완료 — DT-013 전체 완료**([[DT-013-State-Read-Data-Node-Review]]
  Completion Review 판정: 완료). [[DialogueTool]](runtime node 표 + State Read 절 + integration dependency),
  [[World-State-System]](State Read 완료 사실), [[DialogueTool-User-Guide]](§6 State Read 절) 갱신. 최종 회귀
  매트릭스 11/11 GREEN(DT-013 step1/2/3, DT-008 step1/4/5, DT-009 step2/4, DT-010 step1/3, DT-012 step2), 실제
  `SCRIPT ERROR:` 0건, `--import` 0 parse error. 남은 후속은 노드 display name/alias 시스템(Step 2 P3 수용,
  "WorldStateRead" → "State Read" 표시 — [[Open-Tasks]] Later)뿐이다.
- **DT-015 Dialogue Integrated Regression Graph 완료(판정: 완료):**
  `Start + Say + Choice + Variable + Expression + Branch + End` 조합을 한 리소스 안에서 검증하는 headless Step 1 테스트(`dt015_step1_integrated_graph_test`) 및 에디터 노드 구성 및 캡처, round-trip을 검증하는 Step 2 테스트(`dt015_step2_editor_authored_roundtrip_test`)를 추가했다.
  수동 advance와 select_choice를 사용해 Strong/Weak/Leave 3가지 선택 경로의 Say sequence 및 End 도달을 성공적으로 검증했고, 임시 `.tres` 저장/재로드 후 Choice 포트 배선과 Leave 경로의 Branch 우회 여부, 그리고 reloaded 리소스를 이용한 3개 경로 재수행을 완벽히 단언했다.
  또한, Expression 입력 미연결 시 `errored` 전파로 인해 Branch가 Godot Expression ERROR 로그는 발생하지만 SCRIPT ERROR 없이 graceful하게 false 분기(`Strong fail`)로 fail-closed됨을 검증했다.
  에디터 authored Expression의 자동 변수명 `A` 및 Choice 리스트, 포트 연결 정보 보존을 단언하고 런타임 e2e 실행을 완벽히 매칭했다.
  테스트 100% 통과(ALL PASS, SCRIPT ERROR 0) 및 지정 회귀(`dt008_step1_state_condition_test`, `dt014_step1_say_paging_ui_test`)의 무회귀 통과를 확인했다.
- **DT-016 DialogueManager Lifecycle Regression 완료(Step 1~2, 판정: 완료 —
  [[DT-016-DialogueManager-Lifecycle-Regression-Review]]). 제품 코드 변경 없음.**
  게임 코드 진입점 `DialogueManager.play(...)`의 반복 실행/교체/same-frame latest-wins/callback 재진입/
  stale signal 차단/provider tuple isolation 계약을 전용 headless matrix(`dt016_step1_manager_lifecycle_test`,
  8 시나리오)로 고정했다. graph는 runtime-only `DialogueGraphResource`를 코드에서 만들고(영구 `.tres` 없음),
  진행은 `_ui.dialogue_player.advance()`/`select_choice(0)`, 관찰은 `DialogueManager.ui_request`/
  `dialogue_started`/`dialogue_end` log·count로만 한다(렌더 텍스트·Button 클릭 비의존).
  검증: (1) 반복 실행 무누수(2회차 `waiting_for==&"text"`·Say 노드·count == run 수), (2) Say/Choice 대기 중
  교체에서 same-frame valid window(`is_instance_valid(old_player)` true 단언 후 즉시 stale
  `advance()`/`select_choice(0)`)로 호출해도 `_on_ui_request`/`_on_end`의 `source_ui != _ui` guard가
  Manager log를 안 늘림, (3) 같은 프레임 연속 `play()`에서 `cancel_pending_start()` + `_pending_start`
  latest-wins로 OLD 미실행(request log NEW만), (4) `ui_request`/`dialogue_end` callback 재진입에서 새 대화
  보존(`_on_end()`의 `_dismiss()`→`emit()` 순서, one-shot listener), (5) test-only untyped spy mutation
  provider로 same-frame 교체 시 OLD provider 0회 / NEW 1회 격리.
  완료 회귀 matrix 4/4 ALL PASS(`dt016_step1`, `dt015_step1`, `dt004_step4`, `dt009_step4`), 실제
  `SCRIPT ERROR:` 0, `--import` exit 0. 예상 경고는 시나리오 [5]의 빈 `texture_path` `portrait_show`
  경고 1회뿐(portrait 렌더 Non-Goal). `play(null)` negative case는 Godot `ERROR` 로그 회피 위해 기본
  matrix 제외.

## SaveGame

- **SG-001 SaveGame Core 완료**(Step 0~5; Step 1~4 수정 후 완료, Step 5 문서 완료 —
  [[SG-001-SaveGame-Core-Section-System-Review]], 사용법 [[SaveGame-User-Guide]],
  [[SaveGame-System]], [[ADR-013-WorldCore-Umbrella-Packaging]]).
- `addons/world_core/save_game/`(dialogtool/world_state와 분리, SaveGame Core):
  `SaveSection`(Node base: section_id/section_version/restore_order/required + capture/validate/restore_save
  보고형 override)와 `SaveGameManager`(명시 `register_section` 1순위 + 보조 `discover_sections` subtree/group
  helper, deterministic ordering=restore_order→id lexical, `capture_all`/`validate_envelope`/`restore_all`,
  save/load 재진입 busy guard).
- version 3계층 분리: manager `save_version`(envelope), section `section_version`(adapter), payload
  `schema_version`(domain 소유, core 미해석). capture 실패 시 envelope 미생성, validate 실패 시 restore 0회,
  restore 중간 실패 시 `partial_restore` report, unknown saved section은 `ignored_sections`로 report(실패 아님).
- 리뷰 수정: payload JSON 호환을 core가 재귀 강제(`_is_json_compatible`, StringName/Vector*/Object/
  non-String key·int overflow 거부 → `payload_not_json_compatible`). 등록 후 export id/version 변경을 op 전에
  재검증(`_revalidate_sections`: freed/빈 id/고유 rename(`section_id_changed`)/invalid version →
  `sections_invalid`). `_sections`는 등록 id로 keyed인데 plan/restore는 live id로 조회하므로, 고유 rename도
  restore lookup miss/SCRIPT ERROR 전에 거부한다(2차 리뷰 P1 수정).
- 파일 slot store(Step 2): `SaveGameManager.save_slot/load_slot/list_slots/delete_slot/has_slot`
  (`user://saves/<slot>.json`, slot_id `^[a-zA-Z0-9_-]{1,64}$`, atomic write tmp+rename, missing/corrupt 보고,
  per-slot corrupt isolation). Godot JSON은 number를 float로 읽으므로(`7`→`7.0`) core는 JSON number를 그대로
  반환하고 int 정규화는 section 몫(WorldState adapter=Step 3). `save_version`/`section_version`은 `_is_integral_number`로
  정수형 number만 허용(비정수 float `1.5`/string/null 거부 → version contract 우회 차단). 손상 slot 읽기는
  인스턴스 `JSON.parse()`로 엔진 로그 오염 없이 조용히 실패, list_slots는 구조 손상(metadata 비-Dictionary 등)도
  corrupt로 격리. backup(.bak)은 Step 4.
- core는 WorldState/DialogTool을 직접 참조하지 않는다(정적 가드 테스트로 보존). WorldState 통합은 별도
  `addons/world_core/save_game_world_state/world_state_save_section.gd`(`class_name WorldStateSaveSection extends SaveSection`,
  Step 3)에만 격리: `section_id=&"world_state"`, NodePath/주입으로 `WorldStateRuntime` duck-type 호출
  (capture는 store+session ready 선확인, validate=`peek_world_state_compatibility`, restore=`restore_world_state`).
  int/float·String↔StringName 정규화는 Store `import_snapshot`이 처리(adapter 무정규화). `WorldStateRuntime`에
  `peek_world_state_compatibility()` 추가(ADR-007 D5, 기존 메서드 무수정). WorldStateRuntime은 SaveGame 역의존 0.
  duck-type runtime 반환 shape 위반은 `runtime_contract_invalid`로 fail-closed(typed 대입 SCRIPT ERROR 방지, 리뷰 P2).
- backup/recovery(Step 4): overwrite 시 기존 primary가 **유효할 때만** `<slot>.json.bak`으로 회전(한 세대,
  손상 primary는 good bak을 덮지 않음 — 리뷰 P1). load_slot은 primary 없음/손상 시 bak에서 복구하고
  `recovered_from_backup`/`source` 보고, 둘 다 손상이면 실제 원인(`parse_error`/`corrupt`) 보고+실패(restore 0,
  리뷰 P2). delete_slot은 primary+bak 모두 제거. list_slots/has_slot은 primary 기준 불변.
- 검증: `--import` 0 parse error, `sg001_step1_core_test`(26)·`sg001_step1_static_guard_test`·
  `sg001_step2_slot_store_test`(12)·`sg001_step3_world_state_section_test`(6)·`sg001_step4_backup_test`(8) ALL PASS,
  DT-006 step3/step4 회귀 ALL PASS, SCRIPT ERROR/corrupt 로그 0건. 문서/완료 리뷰=Step 5.
- **SG-002 SaveFlow Facade and Metadata Provider Step 0 설계 리뷰 대응 완료**:
  [[SG-002-SaveFlow-Facade-Metadata-Provider-Review]] 판정 Approved after design fixes, design fixes 반영 완료,
  [[ADR-014-SaveFlow-Facade-And-Metadata-Provider]] accepted. 방향은 UI/UX 제외, `SaveFlow` facade,
  metadata provider(base) + caller metadata override, optional save gate provider다. gate 오류 fail-closed,
  `list_slots()` manager unavailable 단일 실패 entry shape, `save_flow.gd` domain-free 정적 가드가 Step 1
  요구사항에 포함됐다.
  - **[DebuggerTree.cs](file:///f:/beestation/GodotAutoCrawler/addons/behaviortree/debugger/DebuggerTree.cs)**: 시나리오 D(노드 삭제/씬 교체) 중 발생하던 freed 인스턴스 접근 및 `Root == null` 크래시(NullReferenceException) 조치 완료.
  - **[BehaviorTreeValidationTest.cs](file:///f:/beestation/GodotAutoCrawler/addons/behaviortree/tests/BehaviorTreeValidationTest.cs)** / **[bt_validation_test.tscn](file:///f:/beestation/GodotAutoCrawler/addons/behaviortree/tests/bt_validation_test.tscn)**: C# 헤드리스 단위 테스트(6개 위반 사례 검증) 성공 확인.
- **SG-002 Step 1(SaveFlow Core) 구현·리뷰 완료(판정: 완료).** `addons/world_core/save_game/save_flow.gd`
  (`class_name SaveFlow extends Node`) 추가. manager를 소유하지 않고 호출마다 lazy resolve(주입 우선 →
  `manager_path` 기본 `/root/SaveGame`, 매번 `is_instance_valid`+`is SaveGameManager` 재확인, 미해석 시
  일반 report `manager_unavailable` / `list_slots()`만 단일 실패 entry `{ ok:false, slot_id:&"", error:&"manager_unavailable" }` /
  `has_slot()`은 false). metadata provider(`make_save_metadata`)와 save gate provider(`query_save_gate`)는
  optional `Object` duck-type — 없음=통과(gate는 allow), freed/non-Object/메서드 없음 또는 반환 shape 위반은
  fail-closed(`metadata_provider_unavailable`/`metadata_provider_contract_invalid`,
  `save_gate_unavailable`/`save_gate_contract_invalid`, 모두 `save_slot` 미호출). metadata는 shallow merge
  (provider base → caller override). `save_manual` 흐름=manager→gate→metadata→`save_slot`, 성공/실패 모두 6키
  (`ok/slot_id/error/metadata/manager_report/gate`) 보존, 미호출 단계 `{}`, manager report passthrough(원본 error
  노출+`manager_report` 보존). `load_manual`은 gate 미확인 + `recovered_from_backup`/`source`/`restore` 손실 없이
  래핑, `delete_slot`/`list_slots`/`has_slot`은 manager 위임, `can_save()`는 manager 가용성과 무관. `save_flow.gd`는
  WorldState/DialogTool 직접 참조 0(SG-002 전용 정적 가드 `sg002_step1_static_guard_test`로 보존). 검증:
  `--import` 0 에러, `sg002_step1_save_flow_test`(A~T 20 시나리오) ALL PASS(SCRIPT ERROR 0), 정적 가드 ALL PASS,
  SG-001 회귀(core/static_guard/slot_store/backup) ALL PASS. 코드 리뷰 [P2] 수정 완료: provider/gate setter·
  저장 변수·`_provider_usable`을 Variant 경계로 열고 검사 순서를 `null→non-Object→freed→method`로 재정렬해
  non-Object provider도 타입 오류 없이 unavailable로 fail-closed(D2/I2 회귀 추가).
- **SG-002 Step 2(WorldState Integration Usage Test) 구현·리뷰 완료(판정: 완료, 제품 코드 변경 없음).**
  `addons/world_core/save_game_world_state/tests/sg002_step2_save_flow_world_state_test`(A~D): `SaveFlow`를
  `SaveGameManager + WorldStateSaveSection`에 주입해 (A) store/session ready 시 `save_manual` 성공 + `load_manual`
  SAVE snapshot 파일 왕복(타입 보존, SESSION default, metadata provider+caller override merge, manager report
  passthrough), (B) store not-ready / (C) session not-ready capture 실패가 manager `capture_failed` +
  `section_reason` 원본으로 전달·파일 미작성, (D) `.bak` 회전 후 primary 제거 시 `load_manual`이
  `recovered_from_backup=true`/`source=&"backup"`/`restore`를 손실 없이 전달하고 bak 값으로 복원됨을 검증.
  ALL PASS, 실제 SCRIPT ERROR 0. 회귀: SG-001 step3/4, DT-006 step3/4, SG-002 step1 ALL PASS, `--import` 0 에러.
  도메인 결합 테스트라 `addons/world_core/save_game/tests/`(domain-free)가 아닌 `addons/world_core/save_game_world_state/tests/`에 둠.
- **SG-002 Step 3(Documentation and Completion Review) 완료 — SG-002 전체 완료**(제품 코드 변경 없음). 문서:
  [[SaveGame-User-Guide]] §8 "SaveFlow facade" 신규(manager 해석/metadata provider/save gate/save_manual 6키
  shape/권장 metadata key/UI raw report 소비 가이드)·§9 설치에 SaveFlow autoload·§10 reason 표에 facade reason 추가,
  `addons/world_core/save_game/README.md` "SaveFlow facade" 절, [[SaveGame-System]] 현재 사실 갱신. 완료 리뷰
  [[SG-002-SaveFlow-Facade-Metadata-Provider-Review]] **판정: 완료**(Step 1~3 완료 조건 충족, Step 1 [P2]
  non-Object fail-closed 수정 확인, P0/P1 없음). 최종 검증 매트릭스 10/10 GREEN(SG-002 step1/step2, SG-001
  step1~4, DT-006 step3/4), 실제 SCRIPT ERROR 0, `--import` 0 에러. 후속(실제 save UI, autosave/quicksave,
  다세대 백업, migration registry, Dialogue SaveEffect, `world_core` 패키징)은 범위 밖으로 [[Open-Tasks]] Later 유지.
- **SG-003 Save Slot UI Host Integration Step 0 설계 리뷰 완료(판정: Approved after design fixes).** 목표는 production UI를 core에 넣지 않고,
  게임별 save/load UI가 `SaveFlow` raw report를 소비하는 host-owned contract를 문서화·검증하는 것이다.
  Task [[SG-003-SaveSlot-UI-Host-Integration]]은 Step 1 Host Integration Guide, Step 2 test-only fake host
  flow 검증, Step 3 completion review로 분해됐다. 설계 수정으로 per-slot failure를 `corrupt` 전용이 아닌
  non-empty `slot_id`를 가진 raw error(`parse_error`/`corrupt` 등) 보존 entry로 확장했고, metadata fallback을
  Step 2 테스트 조건에 추가했다.
- **SG-003 Step 1(Host Integration Guide) 구현·리뷰 완료(판정: 완료, 문서 전용/제품 코드 변경 없음).**
  [[SaveGame-User-Guide]] §12 "Host Save Slot UI Integration" 신규(slot list 분류, manual save/load/delete flow,
  metadata fallback, list/save/load/delete report consumption matrix, 검증 경계)와 `addons/world_core/save_game/README.md`
  host UI 통합 요약 추가. 실제 `SaveFlow`/`SaveGameManager` report shape와 대조해 작성: whole-list
  `manager_unavailable` vs non-empty `slot_id` per-slot `parse_error`/`corrupt`, save 6키 shape + provider/gate
  fail-closed(미저장), load report 키가 실패 종류별로 다름(`recovered_from_backup`/`source`/`restore`/`slot_id`
  일부 누락 → `report.get` 소비), delete primary+`.bak` 동시 제거. `rg`로 핵심 용어 반영 확인,
  `git diff` 제품 코드 0. Godot headless는 문서 전용 Step이라 미실행. 다음은 Step 2 Reference Host Flow Test.
- **SG-003 Step 2(Reference Host Flow Test) 구현·검증·리뷰 완료(판정: 완료, 제품 코드/helper 추가 없음).**
  `addons/world_core/save_game/tests/sg003_step2_host_flow_test.gd`/`.tscn` 신규. 테스트 파일 내부 test-only
  `FakeSaveSlotHostController`(`extends RefCounted`, public API 아님)가 §12 host contract 상태 모델
  (`list_state`/`slot_cards`/`selected_slot_id`/`can_save_state`/`last_action`)을 흉내 내고, 실제
  `SaveFlow + SaveGameManager`(+`SpyManager` save 호출 카운트, duck-type gate provider)로 검증한다. 헤드리스
  `sg003_step2_host_flow_test` ALL PASS(A~H): whole-list `manager_unavailable` slot count 0, per-slot
  `parse_error`+`corrupt` 격리(정상 비차단), `{}`/unknown/wrong-type metadata fallback+raw 보존, gate
  deny/unavailable/contract invalid fail-closed(`save_calls==0`), save 6키 shape 보존,
  load `recovered_from_backup`/`source`/`restore`+raw reason 보존, delete 후 refresh. 회귀 ALL PASS(SG-002
  step1 save_flow/static_guard, SG-001 step1/2/4), `--import` 0 parse error. 다음은 Step 3 Completion Review.
- **SG-003 완료**(Step 0~3, [[SG-003-SaveSlot-UI-Host-Integration-Review]] 판정: 완료). SaveGame core는
  production save/load UI를 제공하지 않고, host가 `SaveFlow` raw report를 직접 소비하는 integration
  contract만 [[SaveGame-User-Guide]] §12 + README에 문서화하고 test-only `FakeSaveSlotHostController`로
  검증했다(제품 코드 변경은 테스트 파일뿐). Step 3 완료 리뷰에서 Step 1~2 완료 조건 대조 + 재실행
  (sg003_step2 + SG-002/SG-001 회귀 ALL PASS, `--import` 0 에러), Task status complete 마감. 후속(production
  save menu UI, quicksave/autosave, thumbnail, Dialogue SaveEffect, migration registry)은 [[Open-Tasks]] Later 유지.

## Known Gaps

- Portrait는 `Say` 요청의 문자열 필드와 별개로, 독립적인 비대기 UI 상태 명령으로 분리됐다.
  DT-002 Portrait State MVP가 완료됐다([[DT-002-Portrait-Review]]).
  - DialoguePlayer가 `portrait_show/hide/expression`을 비대기 `portrait_state` 요청으로 발행한다(Step 1).
  - 세 노드를 에디터에서 생성·편집하고 Definition/runtime snapshot에 저장·재로드한다
    (공통 `PortraitDef` + `portrait_editor_adapter`, Step 2).
  - DialogueUI가 left/center/right 슬롯에서 Portrait 상태를 소유하고 `texture_path` Texture를
    렌더링한다. Say/Choice 전환에도 유지되고 종료/교체 시 정리된다(Step 3).
  - 반복 실행/교체/재진입 수명주기와 기존 리소스 호환을 통합 검증했다(Step 4).
  - MVP 이후 후속: transition 애니메이션, Portrait Focus/dim, actor database/resolver,
    speaker 기반 자동 선택. [[Open-Tasks]] 참고.
- Autoload와 SceneFunction의 안전한 런타임 평가/부작용 정책은 미완성이다.
- DialogueTool 헤드리스 자동 테스트가 고정됐다(`dt004_step1~4`+integration). World State는
  `dt005_step1~6`로 통합 매트릭스까지 검증된다. 영구적인 .tres 파일 형태의 에디터-저장 기반 통합 회귀 샘플은 없으나, DT-015 작업을 통해 임시 .tres 생성 및 에디터-저장 round-trip과 런타임 e2e 실행을 자동 검증하는 헤드리스 테스트(`dt015_step2_editor_authored_roundtrip_test`)가 구축되어 있다.

- **DT-014 Say 줄 누적 표시 기능 구현·검증 완료(판정: 완료).**
  - [[DT-014-Say-Line-Paging-UI-Regression-Review]] (판정: Rework 완료). 타이밍 의존성을 제거한
    Deterministic `DialogueUI` mock 테스트 세트를 추가하여, Say 노드별 누적 표시 상태와 전환
    회귀 검증을 완료했다.
- Portrait와 주 Flow를 같은 실행 지점에 연결하는 비대기 Effect 모델이 완료됐다
  ([[DT-004-Nonblocking-Effect-Flow]], [[ADR-005-Nonblocking-Effect-Connections]], [[DT-004-Effect-Flow-Review]]).
  한 Flow 출력의 다중 주 Flow 대상은 저장 validation으로 차단된다(런타임은 여전히 첫 주 Flow만 실행).
  - Step 1(런타임 계약) 완료: connection의 `kind: "effect"`로 Effect 연결을 식별한다.
    `get_runtime_next_node_id`는 Effect를 건너뛰어 주 Flow만 반환하고, `get_runtime_effect_node_ids`가
    Effect 대상을 저장 순서대로 반환한다. DialoguePlayer가 노드를 떠나는 시점에 Effect들을 비대기로
    발행하며, 순환·잘못된 대상·누락을 경고 후 건너뛴다. Portrait 타입만 Effect 대상으로 허용한다.
  - Step 2(에디터 포트/저장·재로드) 완료: `port_type`에 `effect`(전용 색상) 추가.
    Start/Say에 Effect 출력 포트(port 1), Portrait에 Effect 입력 포트(port 1)를 두되 기존 Flow/Data
    port index는 보존한다. capture가 출력 포트 타입에서 `kind`를 파생해 저장/재로드/재캡처에 보존한다.
    load 시 `kind=="effect"` 연결은 노드의 Effect 포트로 정규화한다(레거시 0→0 리소스 호환).
    런타임 Effect 식별은 port-agnostic(`kind` 기준)으로 조정했다.
  - Step 3(validation·편집 UX) 완료: 저장 validation이 (A) 한 Flow 포트의 주 Flow 대상 2개 이상,
    (B) Portrait 아닌 Effect 대상, (C) Effect 순환을 거부하고, 오류 메시지에 node/type/port를 포함한다.
    Effect→Say는 effect↔flow 카테고리 불일치로 거부된다. Effect 포트는 주황색+라벨+tooltip로 구분한다.
    Effect 대상 화이트리스트는 `DialogueGraphResource.EFFECT_TARGET_TYPES` 단일 정의로 런타임과 공유한다.
    헤드리스 테스트로 런타임(`dt004_step1_*`), 에디터 왕복(`dt004_step2_*`), validation 행렬(`dt004_step3_*`)을 검증.
  - Step 4(통합 회귀·완료 판정) 완료: 두 Effect 지점 시나리오를 DialogueUI/DialogueManager로 실행해
    Effect와 Say/Choice 미간섭·중복 없음, 저장/재로드 왕복, 반복·교체·재진입 수명주기, 직렬/무 Portrait 회귀를
    헤드리스 5개 테스트(`dt004_step1~4_*`)로 검증했다. P0/P1 없음([[DT-004-Effect-Flow-Review]]).
- Definition이 Adapter 조회를 중계하는 점진적 호환 계층이 남아 있다.
- 전투 시스템에는 게임오버 후속 처리와 일부 null 방어 과제가 남아 있다.

## BehaviorTree

- **BT-002 Tactic Board Runtime Extension 완료 (판정: 완료)** ([[BT-002-Tactic-Board-Runtime-Extension]], [[ADR-023-Tactic-Board-Runtime-Execution-Contract]], [[BT-002-Tactic-Board-Runtime-Completion-Review]])
  - 플레이어 택틱보드를 실행하는 전용 노드 계층이 `Assets/Script/AutoCrawlerBehaviorTree/Tactic`에 있다. 기존 일반 BT의 의미는 바뀌지 않고 두 계층이 공존한다. 전역 `BtStatus`는 3값 그대로이며 택틱 내부 결과를 selector 경계에서 변환한다.
  - 엄격 행 우선순위(최초 성립 행 하나), `Running` 행 latch(상위 행 preempt 금지, **접근 단계 포함**), 조건 후보 identity 교집합과 확정 대상 공유, commit 이후 fallback 금지(`CancelledAfterCommit`), `Immediate`/`ApproachAllowed` 접근 정책, 최종 `TacticWait`.
  - 실제 전투 배선: `CharacterTacticAdapter`가 AStar 사본으로 **무변형 feasibility**를 계산하고 이번 턴 이동량은 단일 소스 `Mobility+1`이다(성립 검사와 실제 이동이 같은 규칙 공유). `TurnActionBase`의 명시적 대상 바인딩을 근접 `GetTarget`과 스킬 `SelectTarget`이 모두 소비해 조건이 지목한 적과 실제 피격 대상이 어긋나지 않으며, 무효 대상은 다른 적으로 fallback하지 않는다.
  - `ITurnActionProvider`로 행동을 순회하므로 수제 BT와 택틱 보드의 행동이 ammo 충전·사용 가능 공격 조회에서 함께 발견된다. 멀티턴 스킬은 `CurrentTurnAction`으로 다음 턴 재개되며, 새 자기 턴·사망·tree exit에서 `CharacterArticle.ResetTacticRuntime()`이 latch/문맥/확정 대상/이동 연출을 폐기한다.
  - debug tick payload에 `tactic_row_id`/`tactic_row_result`가 실리고(debug-off면 미수집), 잘못된 택틱 구조는 `BehaviorTreeValidation`이 실행 전 오류로 표면화한다.
  - 검증: BT-002 전용 테스트 5종 231 단언 PASS, 회귀 17종 ALL PASS, `dotnet build` 0경고/0오류.
  - 후속(별도 Task): 택틱보드 Resource/validator/compiler와 compiled snapshot, 편집 UI, H-4 전체 어휘, DungeonRun 연결.

- **BT-001 Step 1: Read-only Graph Viewer 구현 완료 (판정: 완료)**
  - 에디터 내 Inspector에서 `🌵 Open Behavior Tree Editor` 버튼을 복구하여 BehaviorTree 및 BT node 선택 시 디버거 윈도우를 열 수 있는 진입점을 마련함.
  - 디버거 윈도우에 `HSplitContainer`를 배치하여 좌측에는 기존 `DebuggerTree`(Tree 위젯), 우측에는 새로 구현한 GraphEdit 기반 `BehaviorTreeGraphView`를 동시에 표시함.
  - raw `GetChildren()` 필터링 방식으로 노드 트리를 탐색해 parent-child 관계를 GraphEdit connection으로 렌더링하고, sibling order를 노드 내부에 시각화함.
  - Composite, Decorator, Action 노드의 스타일에 시각적 차이를 두고(포트 색상 및 헤더/모듈레이트 색상), 너비 우선 계층 배치 알고리즘(Auto-layout)을 적용해 노드 겹침을 방지함.
  - `BehaviorTreeValidation.cs`를 구현하여 루트 부재, 루트 타입 위반, Decorator 자식 초과, Action 자식 존재, RatingSelector의 비-RatingDecorator 자식 경고 등의 구조적 유효성을 분석하고 노드 내에 상세 Reason을 노출함.
  - C# 헤드리스 테스트(`BehaviorTreeValidationTest.cs` + `bt_validation_test.tscn`)를 통해 6가지 유효성 위반 시나리오에 대해 에러 식별 동작을 완벽히 단언(ALL PASS)하고 regression이 없음을 확인.
- **BT-001 Step 2: Basic Authoring 구현 완료 (판정: 완료)**
  - GraphEdit 상에서 노드 생성/삭제, 연결/해제 및 Sibling Order 이동 조작을 지원하고 씬 트리에 실시간 반영하여 씬 저장 시 영구 보존되도록 구현함.
  - **연결선 차단 규칙**: `connection_request` 시점에 Decorator 자식 1개 제한, Action 자식 연결 금지, Cycle 순환 참조 차단, 다중 부모(Multiple parents) 차단을 사전에 검증하여 잘못된 조작을 사전 차단함.
  - **컨텍스트 메뉴**: GraphEdit 빈 공간 우클릭 시 PopupMenu를 띄워 Selector, Sequence, RatingSelector 및 프로젝트 C# 스크립트 노드를 생성할 수 있는 메뉴를 제공하며, 마우스 클릭 오프셋 위치를 노드 좌표 메타데이터(`bt_graph_position`)에 영구 반영함.
  - **노드 삭제 및 자식 구출**: GraphNode 내 `✕` 삭제 버튼 클릭 시 노드를 삭제하되, 산하의 자식 서브트리가 함께 파괴되는 것을 방지하기 위해 자식 노드들을 `BehaviorTree` 루트의 독립된 자식 노드로 분리 reparent(구출)한 뒤 삭제하도록 안전 장치를 탑재함.
  - **실행 순서 Up/Down 버튼**: GraphNode 내 `▲`, `▼` 버튼을 탑재하여 부모 내에서의 Sibling 인덱스 순서(`MoveChild`)를 갱신하고 `Order: N` 라벨에 실시간 스왑 반영함.
  - **잔여 항목 개선**: `NoRoot` 경고 등급에 맞춰 empty-state 노드의 modulate 색상을 Gold(Warning)로 변경하고, `BehaviorInspectorPlugin` 내의 사용하지 않는 `_node` 멤버 변수를 완전히 제거함.
- **BT-001 Step 3: Inspector 연동 및 설정 UX 구현 완료 (판정: 완료)**
  - **Inspector 연동 (Step 3)**: GraphEdit 상에서 노드 선택 시 `EditorInterface.Singleton.EditNode(node)`를 호출하여 Godot 메인 인스펙터 뷰에 속성이 즉시 연동되도록 구현함. 노드 이름 변경 시 `Renamed` 시그널로 그래프를 실시간 갱신하며, 노드 이동/추가/삭제/연결/해제 시 `MarkSceneAsUnsaved()`를 명시적으로 호출해 변경 정보(`bt_graph_position` 메타데이터)가 디스크에 유실 없이 저장(`Ctrl+S`)되도록 보장함.
  - **직렬화 문제 해결**: `TempArticle3.tscn`이 에디터 버전 드리프트로 인해 광범위하게 재직렬화되었던 오류(P1-1)를 씬 revert 후 메타데이터(`bt_graph_position`) 필드만 선택적으로 정규화 이식하는 방식으로 해결하여 포맷 드리프트를 완전히 제거함.
- **BT-001 Step 4a: Remote Debug Channel + Gating + Discovery 구현 완료 (판정: 완료)**
  - **Dispatcher 및 Registry**: `BehaviorTree` play `_Ready()` 시 registry 등록 및 `register` 알림 송신, `_ExitTree()` 시 `unregister` 및 registry 해제를 통해 active-debugger play discovery 목록을 지원하도록 구현함. 첫 `BehaviorTree` ready 시 `RegisterMessageCapture` dispatcher를 1회만 등록함.
  - **Gating 및 Zero Allocation**: 에디터의 `start` / `stop` 신호에 따라 해당 `BehaviorTree` 인스턴스의 `DebugEnabled` 값을 켜고 끄며, 게이트가 비활성화된 경우 `BehaviorTree_Node.Behave()` 게이트가 조기에 단락되어 디버깅 연산 및 문자열/딕셔너리 할당이 완전히 차단(Zero allocation)되도록 최적화함.
  - **Structure 및 Tick**: `start` 수신 시 `behavior_tree:structure` 페이로드가 1회 전송되어 raw `GetChildren()` 기반 노드 구조를 반환하고, 매 physics tick마다 최적화된 `{ node_path, status, elapsed_time }` 리포트만을 배치 전송함.
  - **검증**: `BehaviorTreeValidationTest.cs` 헤드리스 C# 단위 테스트(K~N 케이스)를 설계 및 병합하여 registry 라이프사이클, 게이트 단락, start/stop 라우팅 및 payload 라운드트립이 오류 없이 완벽하게 PASS됨을 검증함.
- **BT-001 Step 4b: Payload-built Debug Graph + Highlight 구현 완료 (판정: 완료)**
  - **원격 그래프 뷰 격리**: 로컬 authoring 뷰와 격리된 원격 디버그 그래프 전용 `BehaviorTreeDebugGraphView.cs`를 구현하여 structure 페이로드에서 노드/연결/배치(auto-layout fallback)를 자체 구성하도록 설계함.
  - **실시간 하이라이트 및 Stale clear**: 틱 보고에 맞춰 초록(Success)/빨강(Failure)/파랑(Running) 색상을 실시간 변경하고 elapsed_time을 표출하며, 틱 갱신 시작 시 미보고 노드를 디폴트 중립 색상으로 원복(`ResetHighlights()`)함으로써 잔상 누적을 방지함. 타입별 색상은 캐싱해 성능을 확보함.
  - **수동 스모크 및 라이프사이클**: `DebuggerWindow.cs`를 `tree_path` 기반 탭 라우팅으로 교체하고 탭 종료 시 `StopDebugging`을 자동 역송신하게 연동하였으며, 임시 발견 OptionButton UI 패널(TEMP)을 추가해 F5 플레이 스모크 테스트 연동을 완수하고 unregister 시 회색조 Stale 잠금 처리를 확인함.
  - **검증**: C# 단위 테스트(O~Q 케이스)를 병합하여 structure 복원 배치, 하이라이트/복구, structure 누락 틱 방어 동작이 성공함을 확인(ALL PASS).
  - **2026-06-20 P1 포워딩 수정**: `BehaviorTreeEditor.HandleDebugMessage`가 tick만 넘기던 4a stub 상태를 고쳐,
    `register`/`unregister`/`structure`/`tick` 4종을 모두 `DebuggerWindow.HandleDebugMessage(message, payload)`로
    위임한다. 원격 메시지 수신 시 창이 없으면 `EnsureDebuggerWindow()`로 자동 생성하고, 최초 `register`에서
    창을 표시한다. 회귀 테스트 R 케이스(editor -> window forwarding) 추가, A~R ALL PASS.
- **BT-001 Step 5: Battle Debug Integration 완료(판정: 완료, [[BT-001-BehaviorTree-Graph-Editor-Debugger-Review]])**
  - `DebuggerWindow`의 TEMP discovery 패널을 정식 BehaviorTree target selector로 정리했다. 목록은
    `index. articleName — tree_path`로 표시해 같은 article 이름의 다중 인스턴스를 구분한다.
  - `BehaviorTreeEditor.StartDebugging/StopDebugging`은 송신 직전 `BtDebuggerPlugin.RegisterAvailableSessions()`로
    현재 `EditorDebuggerSession`을 재수집하고, `_Capture`도 수신 session을 등록한다.
  - 런타임 dispatcher는 `behavior_tree:start|stop`과 Godot capture-local `start|stop`을 모두 허용한다.
  - 원격 탭 close는 해당 `tree_path` 하나에만 stop을 보내며, stale title 중복 추가를 방지한다.
  - `DebuggerTree`는 로컬 에디터 scene Node 구조 보기 전용으로 유지하고, 원격 payload graph/status는
    `BehaviorTreeDebugGraphView`가 담당한다.
  - 리뷰 P2 수정: `BehaviorTree_Node._elapsedTime`/`Util.BehaviorLog.Time`을 `double`로 바꿔 fractional delta가
    0으로 잘리던 문제를 수정하고, 실제 `Running` action `Behave(0.016)` 3회 누적 회귀를 추가했다.
  - 검증: `dotnet build` 경고/오류 0, `bt_validation_test.tscn` A~T ALL PASS. 실제 `battle_field.tscn` F5 smoke에서
    register discovery, Start 후 remote graph 생성, Stop 후 `[STALE]` 정지를 확인했다. 현 battle smoke에서는
    discovery 대상이 1개라 둘째 캐릭터 수동 Start/시각 multi-tab 확인은 불가했고, tick 색상 및 natural death
    unregister stale는 자동 테스트(P/R/S/T)로 보강했다.

## Verification Baseline

- Godot 4.6.3 headless editor load가 성공해야 한다.
- Dialogue 리소스는 편집 -> 저장 -> 재로드 후 값과 포트 순서를 보존해야 한다.
- `Start -> Say -> Choice -> Branch -> End` 흐름이 종료까지 실행돼야 한다.

## Related

- [[Open-Tasks]]
- [[DialogueTool-Architecture]]
- [[DialogueTool-Step-1-to-8]]




