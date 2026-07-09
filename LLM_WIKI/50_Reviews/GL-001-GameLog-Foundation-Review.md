---
type: review
task: GL-001-GameLog-Foundation
step: 0
status: complete
reviewed: 2026-07-09
tags: [review, gamelog, design-review]
---

# Design Review: GL-001 GameLog Foundation (Step 0)

이 문서는 GL-001 Step 0(Design Review)의 결과다. 제품 코드/`.tscn`/`.tres`는 수정하지 않았고,
Task 문서만 리뷰 결정 반영으로 갱신했다.

## 검토 대상

- Task: [[GL-001-GameLog-Foundation]]
- 기획 근거: `F:\beestation\ref\wizards_climber_wiki\기획서\06_UXUI.md` PART E-9(확정판, 2026-07-09) — repo 밖 외부 문서(SK-001의 `07_수치표`와 동일한 외부 기획 패턴).
- 대조 시스템: [[Workspace-Window-System]]([[WS-001-Native-Window-Workspace-Shell]]), [[Skill-System]]([[SK-001-Data-Driven-Skill-System]]), [[SaveGame-System]].
- 대조 코드: `Assets/Script/SkillSystem/SkillState.cs`, `Assets/Script/SkillSystem/Blocks/*.cs`, `Assets/Scenes/workspace.tscn`.

## E-9 대조 결과

Task는 E-9 확정판을 충실히 전사했다. 아래 항목은 원문과 정확히 일치한다.

- 채널 4종(`Combat`/`Story`/`Reward`/`System`) + `전체`=UI 필터, 등급 3단(`Event`/`Detail`/`Trace`), 강조 3단(`Normal`/`Warning`/`Critical`).
- `severity`(노출 범위)와 `importance`(시각 강조) 분리, 4개 예시(일반 피해 `Combat/Detail/Normal`, 아군이 적 영창 끊음 `Combat/Event/Normal`, 아군 영창 끊김 `Combat/Event/Warning`, 아군 사망 `Combat/Event/Critical`) 모두 일치.
- 기본 `전체` 탭 = `Event`만, `Detail`은 채널 탭, `Trace`는 개발 전용, 전투당 5~8줄 예산.
- raw fact -> adapter -> `GameLogEntry` -> `GameLogModel` -> `LogWindow` 분리.
- `damage`/`miss`/`chain`/`knockback`/`bind`/`stun`/`manadrain` -> `Combat/Detail/Normal`, `cast_cancel` 진영 분기(적 끊음 Event/Normal, 아군 끊김 Event/Warning).
- kill/death는 SkillReport가 아니라 BattleSession/전투 이벤트(Health -> Dead)에서 발행 -> Task Step 3 Out of Scope와 일치.
- `link:{type,id}` 예약, 아웃게임 이벤트는 world_state `value_changed`에서 생성(GL-001 Out of Scope와 일치).

코드 대조: `workspace.tscn`의 `Windows/LogWindow`는 실제로 `Placeholder` Label 상태(Step 2 대상 확인). `SkillState.Reports`는 `List<string>`이고 블록들이 문자열을 직접 append함(Task 현황 서술 정확).

## Findings

1. **[P2] 던전 크롤 집계 정책이 어느 Step에도 배정되지 않았고 Out of Scope에도 없다**
   - 위치: Task `Dungeon Crawl Aggregation Policy`(턴 내 다중 hit 병합, 턴 간 연속 행동 집계, flush 조건, 25% 승격, 스턴/속박 승격). E-9에서 전사됨.
   - 문제: Step 1~4 어디도 집계를 구현하지 않는다. Step 3은 raw report -> Detail 1:1 매핑 + `cast_cancel` Event까지만이다. 집계의 소유 Step이 없고 Out of Scope에도 없어, 구현자가 Step 3에서 집계를 시도해야 하는지 모호하다.
   - 영향: Step 0 완료 조건("구현 가능한 수준")을 흐린다.
   - 권장: v0에서 집계는 불필요하다. 기본 뷰 5~8줄 예산은 **Detail을 기본 뷰에서 숨기는 것만으로** 충족된다(집계는 Detail 채널 탭 가독성용). 집계는 명시적으로 후속(별도 Step/Task)으로 이관한다. -> 반영함(아래 처리).

2. **[P2] `cast_cancel` 진영 분기 메커니즘이 정의되지 않았다 (Step 3 선행 조건)**
   - 위치: `StunBlock.cs:33`이 내보내는 raw report는 `cast_cancel:{target.GetPath()}`로 **대상 노드 경로만** 담는다. 진영(적/아군) 정보가 문자열에 없다.
   - 문제: Step 3의 enemy/ally 분기(Event/Normal vs Event/Warning)를 하려면 adapter가 파싱 시점에 노드 경로를 resolve해 진영을 판정해야 한다. 그 시점 노드가 살아있는지(대상이 죽어 freed됐을 수 있음), adapter가 전투 컨텍스트(플레이어 진영 기준)에 접근하는지가 미정이다. OD3(모델 수명)와 노드 유효성 문제가 얽힌다.
   - 영향: Step 1/2는 막지 않으나 Step 3 구현 직전에 반드시 닫아야 한다.
   - 권장: Step 3 전에 진영 판정 경로를 확정한다. 두 방향 — (A) adapter가 전투 컨텍스트를 주입받아 파싱 즉시 진영 태깅, (B) `cast_cancel` report 문자열에 진영/side 토큰을 추가 발행(EffectBlock 소폭 변경, 이 경우 SK-001 문자열 계약 회귀 확인). 후속 구조화 SkillReport 마이그레이션과 정합하는 (B) 쪽을 권장하되 Step 3에서 결정. -> Task Step 3에 선행 조건으로 명시함.

3. **[P2] `GameLogModel` 필터 계약이 정의되지 않았다 (Step 1 테스트 가능성)**
   - 위치: Task `GameLogModel` 책임에 `GetEntries(filter)`만 있고 `filter`의 형태가 없다. Step 1 완료 조건은 `All+Event`, `Channel+Event/Detail`, `Trace=debug filter`를 구분하라고 요구한다.
   - 문제: 필터 시그니처가 없으면 Step 1 headless 테스트의 단언 대상이 모호하다.
   - 권장: Step 1 전에 최소 계약을 고정한다. 예: `GetEntries(GameLogChannel? channel, bool includeDetail, bool includeTrace)` 또는 `GameLogFilter{ Channel?, IncludeDetail, IncludeTrace }`. `전체 기본 뷰`=`channel:null, includeDetail:false, includeTrace:false`, `채널 탭`=`channel:Combat, includeDetail:true, includeTrace:false`. -> Task Step 1에 필터 계약 고정을 명시함.

4. **[P3] Step 3 파싱 어휘가 실제 report emitter와 어긋난다**
   - 위치: 블록이 실제로 내보내는 report 중 `stun_miss:{path}`(`StunBlock.cs:25`)와 `selfbuff:{Kind}:{mult}x{turns}:{path}`(`SelfBuffBlock.cs:27`)가 Task Step 3 파싱 목록(`damage/miss/stun/bind/cast_cancel/chain/knockback/manadrain`)에 없다.
   - 문제: adapter가 unknown kind fail-closed면 두 report는 조용히 버려진다. E-9 Detail 목록은 "버프 적용/만료 tick"을 포함하므로 `selfbuff`를 말없이 버리는 것은 정책과 미세하게 어긋난다.
   - 권장: 두 kind를 명시적으로 처리한다 — `selfbuff` -> `Combat/Detail/Normal`(E-9 버프=Detail), `stun_miss` -> 무시 또는 `Combat/Detail/Normal` 중 의식적 선택. 최소한 "의도적으로 무시"임을 Step 3에 적어 누락이 아닌 결정으로 만든다. -> Task Step 3에 명시함.

5. **[P3] GameLog 도메인/수명에 대한 ADR이 없다**
   - 위치: 비교 시스템(SaveFlow=ADR-014, 결정론 전투=ADR-017, 데이터 스킬=ADR-018)은 각각 ADR을 가진다. 전역 로그 도메인 + sink/handler 추상화 + 모델 수명(OD3)은 동급의 설계 선택인데 ADR이 없다.
   - 권장: 최소한 OD3의 전역 수명(autoload vs shared service) 결정은 Step 3 전에 ADR로 남긴다(AGENTS.md "중요한 설계 선택이 필요하면 ADR"). Step 1~2는 ADR 없이 진행 가능. -> Task Step 3/Related에 ADR 후보를 명시함.

6. **[P3] `IGameLogInteractionHandler`를 Step 1에서 정의하는 것은 경미한 조기 일반화다**
   - 위치: Step 1 scope가 `IGameLogInteractionHandler` 추가를 포함하나, v0는 엄격히 읽기 전용이고 이 인터페이스의 구현/호출부가 v0에 없다.
   - 문제: 실사용 없는 behavior 추상화(Design-Review 점검 항목 7 "과도한 일반화").
   - 판단: `GameLogEntry.Link`(순수 데이터, E-9가 예약)는 유지가 맞다. 그러나 handler **인터페이스**는 첫 실제 클릭 배선 시점으로 미뤄도 Step 1 완료 조건("Entry는 handler를 직접 호출하지 않는다")을 구조적으로 검증하는 데 지장이 없다.
   - 권장(비강제): Step 1에서 handler 인터페이스는 no-op 껍데기로만 두거나 Step 2로 미룬다. Link 데이터는 Step 1 유지. -> 선택 사항으로 Task에 주석.

## Open Decisions (Step 0 종결)

Step 0 완료 조건에 따라 5개 결정을 닫는다.

1. **Id 정책 -> 후보 A(증가 `long`) 확정.** `GameLogModel.Append`가 카운터를 소유하고 append 시 Id를 부여한다(호출자가 Id를 만들지 않는다). 저장/정렬/테스트 단순.
2. **OccurredAt -> 후보 A(표시용 string + 최소 numeric) 확정.** 리플레이 연결 시 구조화. 집계 키(turn)와의 결합은 집계와 함께 후속으로 이관되므로 v0 string으로 충분.
3. **GameLogModel 수명 -> Step 1~2는 후보 C(순수 C# model, LogWindowController 소유) 확정.** 단, 전역 수명(autoload `GameLog` vs shared service)은 Step 3 선행 결정으로 **재오픈**한다 — 전투는 `battle_field.tscn`(별도 scene, 여전히 `run/main_scene`)에서 일어나고 LogWindow는 `workspace.tscn`에 있어, Workspace 소유 모델은 전투 중 존재하지 않는다. Step 3 adapter가 전투 계층에서 모델에 닿는 경로를 ADR(Finding 5)로 확정한다.
4. **SaveGame 통합 시점 -> 후보 B 확정.** GL-001은 모델의 trim-200 계약까지. 실제 SaveSection 등록(E-9 결정 ④, `SaveGameManager` 경유)은 후속. v0에서 200 trim 정책과 entry 직렬화 형태는 모델에서 고정한다.
5. **Skill report bridge 시점 -> 후보 B 확정.** Step 1은 GameLog core만, adapter는 Step 3.

## Step Assessment

- **Step 1 (Core Model)**: 입력=없음, 출력=`Assets/Script/GameLog` 도메인 + headless 테스트. 완료 조건 관찰 가능(순서 보존, 200 trim, `All+Event` 필터, 채널 필터, Trace 격리, entry가 handler 미호출). Finding 3(필터 계약)만 닫으면 구현 준비 완료. Finding 6은 선택.
- **Step 2 (LogWindow UI)**: 입력=Step 1 모델. 출력=`workspace.tscn` Log 창 실물 UI + 탭/필터/대화 접힘. 헤드리스 controller/model 테스트 + 씬 smoke로 검증 가능. 선행 조건 명확.
- **Step 3 (Skill Report Adapter)**: 입력=Step 1 모델 + `SkillState.Reports` 문자열. **선행 조건 3건이 열려 있다** — Finding 2(진영 판정), Finding 4(어휘), Finding 5(수명 ADR)/OD3 재오픈. 이들을 Step 3 진입 전에 닫아야 한다. 그 외 매핑은 E-9와 정합.
- **Step 4 (Docs/Review)**: 정적 검토. 문제 없음.
- **집계 정책**: 위 Finding 1로 별도 후속 이관. 어느 Step에도 in-scope로 두지 않는다.

Step 분해 자체는 적절하다. 각 Step이 독립 검증 가능하고 구조 경계(모델/UI/adapter)를 Step 사이에 섞지 않는다.

## Verification Assessment

- **Step 1**: 필요한 테스트 — append 순서 보존, 200 초과 trim(오래된 것부터), `All+Event`가 Detail/Trace 제외, 채널 필터가 해당 채널 Event/Detail 반환·Trace 격리, 빈 title/body·null link 경계, Importance가 노출 판단에 안 쓰임. 누락 시나리오 없음(필터 계약 확정 전제).
- **Step 3**: SK-001 문자열 report 회귀가 깨지지 않아야 한다(기존 테스트가 문자열 prefix 검증). malformed report fail-closed, unknown kind(`stun_miss`/`selfbuff` 포함) 처리, `cast_cancel` 양방향, freed 대상 노드 resolve 실패 경로.
- **외부 기획 대조 한계**: E-9 원문이 repo 밖(`ref/wizards_climber_wiki`)이라 이 리뷰는 사용자 제공 경로로 대조했다. 기획 개정 시 Task/코드가 자동으로 따라가지 않는다 — SK-001과 동일한 기존 리스크.

## Verdict

**Approved after design fixes.**

설계는 E-9에 충실하고 Step 분해·경계·완료 조건이 대체로 탄탄하다. P0/P1 없음. Step 1 착수를 막는 blocker는 없다. 아래 문서 수정(이 세션에서 반영)으로 Step 0 완료 조건을 충족한다:

- Open Decision 5건 종결(위 판정).
- 집계 정책을 in-scope 오해가 없도록 후속으로 이관(Finding 1).
- Step 1 필터 계약 고정(Finding 3).
- Step 3 선행 조건 명시: 진영 판정 메커니즘(Finding 2), 파싱 어휘 `stun_miss`/`selfbuff`(Finding 4), 수명 ADR(Finding 5, OD3 재오픈).

Step 3는 위 선행 조건 3건을 닫기 전에는 착수하지 않는다. Step 1은 필터 계약 확정 후 즉시 착수 가능.

## Related

- [[GL-001-GameLog-Foundation]]
- [[Workspace-Window-System]]
- [[Skill-System]]
- [[SaveGame-System]]
- [[STEP_REVIEW_WORKFLOW]]
