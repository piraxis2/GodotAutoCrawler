---
id: GL-001
type: task
status: complete
system: GameLog
created: 2026-07-09
updated: 2026-07-09
tags: [task, gamelog, log, ui, workspace, report]
---

# GameLog Foundation

## Goal

게임 전역에서 발생하는 사건을 `GameLog`로 수집하고, Workspace의 `Log` 창에서 읽기 전용으로 표시할 수 있는 기반을 만든다.

`LogWindow`는 Workspace의 한 창이지만, 로그 자체는 Workspace 전용이 아니다. 전투, 아웃게임, 대화, 시스템 알림, 획득/해금 이벤트가 모두 같은 로그 기반을 사용한다.

핵심 방향:

- 로그 데이터(`GameLogEntry`)와 로그 표시 UI(`LogWindow`)를 분리한다.
- 로그 항목 클릭 시 필요한 동작은 데이터가 직접 실행하지 않고 interaction handler가 처리한다.
- SkillSystem의 report는 GameLog의 원천 자료가 될 수 있지만, 직접 LogWindow에 들어가지 않고 adapter를 거친다.
- 기획서 E-9 정책에 따라 기본 뷰는 `Event`만 보여주고, `Detail`은 채널 탭에서만, `Trace`는 debug 전용으로 노출한다.

## User Outcome

- 플레이어는 놓친 사건, 전투 결과, 시스템 알림, 완료된 대화 요약을 Log 창에서 다시 확인할 수 있다.
- 기본 로그는 스팸 없이 중요한 사건만 보여준다.
- 나중에 로그 항목 클릭으로 리플레이, 유닛 상세, 택틱 보드, 대화 아카이브 등 관련 창으로 이동할 수 있다.
- 개발자는 전투/아웃게임/대화 시스템이 서로 다른 UI를 직접 알지 않고도 동일한 로그 sink로 사건을 보낼 수 있다.

## Context

- `06_UXUI.md` E-9 로그 정책:
  - 원칙: 로그 창은 진행(tick)이 아니라 사건(상태가 바뀐 것)의 기록이다.
  - 채널: 전체 / 전투 / 이야기 / 획득·시스템.
  - 등급: 사건 / 상세.
  - 기본 뷰 = 전체 탭의 사건 등급만.
  - 개별 타격, 이동, 버프 적용/만료 tick, 명중/빗나감은 `Detail` 등급이다.
  - BT 조건 평가와 보드 행 평가 결과는 `Trace` 등급이다.
  - 대화 아카이브는 제목 1줄 접힘으로 시작한다.
  - 최근 200줄을 저장한다.
  - 항목 클릭 -> 관련 창 열림은 목표 상태이며, v0는 읽기 전용으로 시작하되 `link` 필드는 예약한다.
- WS-001에서 `Assets/Scenes/workspace.tscn`의 `LogWindow`는 placeholder Label 상태다. 이제 이 창에 실제 GameLog 표시 UI를 장착한다.
- 현재 SkillSystem은 `SkillState.Reports : List<string>`에 raw report 문자열을 쌓는다.
  - 예: `damage:physical:{targetPath}`, `miss:magical:{targetPath}`, `stun:{turns}:{targetPath}`, `cast_cancel:{targetPath}`, `chain:{i}:{targetPath}`, `knockback:{moved}:{targetPath}`.
  - 이는 GameLogEntry가 아니라 전투/스킬 raw fact에 가깝다.
- SK-001 설계에는 "효과 블록이 `ctx.Report`에 기록하고, 같은 스트림에서 리포트와 로그가 분기된다"는 방향이 있다. 현재 코드는 아직 문자열 report sink 단계다.
- SaveGame에는 SaveSection 기반 저장 시스템이 있다. 다만 GL-001 v0에서는 최근 200줄 저장 정책을 모델/계약으로 먼저 고정하고, 실제 SaveGame 통합은 후속 Step 또는 별도 Task로 둘 수 있다.

## Scope

- GameLog 전역 도메인 모델 설계.
- `GameLogEntry`, `GameLogLink`, channel/severity/importance enum 정의.
- `IGameLogSink`와 interaction handler 인터페이스 정의.
- 최근 200줄 보존, append/filter/clear를 담당하는 `GameLogModel`.
- Workspace `LogWindow` placeholder를 읽기 전용 로그 UI로 교체.
- 샘플 로그 주입 경로 또는 테스트 fixture.
- SkillSystem raw report와 GameLog의 연결 방식을 문서화하고, 최소 adapter 경계를 정의.

## Out of Scope

- SaveGame `SaveSection` 실제 통합.
- 로그 항목 클릭으로 실제 창을 여는 동작.
- 리플레이 마커 이동, 유닛 상세 창, 택틱 보드 팔레트 이동.
- 전투 리포트의 원인 분석 문장 생성.
- SkillSystem의 모든 `Reports.Add(string)`를 구조화 report로 마이그레이션.
- WorldState 변경 알림에서 아웃게임 로그를 자동 생성하는 통합.
- 던전 크롤식 집계(턴 내 다중 hit 병합, 턴 간 연속 행동 집계, 큰 피해/치명타 Event 승격). 정책은 아래 `Dungeon Crawl Aggregation Policy`에 기록하되 v0에서 구현하지 않는다.
- 최종 레트로 OS 스킨/아이콘/애니메이션.

## Design Principles

### D1. GameLog는 Workspace 소유가 아니다

Workspace의 `Log` 창은 GameLog를 표시하는 첫 번째 UI일 뿐이다. 로그 도메인 타입은 `Assets/Script/GameLog` 같은 독립 위치에 둔다.

### D2. Entry는 데이터이고 실행하지 않는다

`GameLogEntry`는 표시, 저장, 필터, 링크 정보를 담는다. 클릭 시 실제 창을 열거나 리플레이로 이동하는 일은 interaction handler가 한다.

### D3. Source보다 Sink를 먼저 둔다

v0에서는 `IGameLogSink.Append(entry)`만으로 충분하다. 전투/대화/아웃게임이 각자 독립적으로 이벤트를 발행하기 시작하면 `IGameLogSource`를 추가한다. 먼저 source 추상화까지 만들면 이른 일반화가 될 수 있다.

### D4. SkillReport와 GameLogEntry는 다른 계층이다

SkillSystem report는 전투/스킬 raw fact다. GameLogEntry는 플레이어에게 보여줄 로그 항목이다. 둘 사이에는 adapter가 있어야 한다.

### D5. 기본 뷰는 사건만 보여준다

기본 `전체` 탭은 `GameLogSeverity.Event`만 표시한다. `Detail`은 해당 채널 탭에서만 볼 수 있다. 전투 1회 기본 뷰 5~8줄 예산을 깨는 설계는 후속 리뷰에서 반려 기준이 된다.

### D6. 저장 가능한 순수 모델을 먼저 만든다

`GameLogModel`은 Godot UI를 몰라야 한다. append/filter/trim 200/clear 같은 정책은 headless 테스트로 검증한다.

### D7. Severity와 Importance를 분리한다

`Severity`는 노출 범위(`Event`, `Detail`, `Trace`)이고, `Importance`는 시각 강조(`Normal`, `Warning`, `Critical`)다. 예를 들어 일반 피해는 `Combat/Detail/Normal`, 아군이 적의 영창을 끊은 경우는 `Combat/Event/Normal`, 아군의 영창이 끊긴 경우는 `Combat/Event/Warning`, 아군 사망은 `Combat/Event/Critical`이다.

## Data Model Draft

### GameLogChannel

- `Combat`
- `Story`
- `Reward`
- `System`

`All`은 저장 데이터의 채널이 아니라 UI 필터다.

### GameLogSeverity

- `Event`: 기본 뷰에 표시되는 사건.
- `Detail`: 채널별 탭에서만 보이는 상세.
- `Trace`: debug toggle 또는 개발 빌드에서만 보이는 내부 추적.

### GameLogImportance

- `Normal`: 일반 표시.
- `Warning`: 플레이어 주의가 필요한 사건.
- `Critical`: 사망, 저장 실패, 치명적 위험처럼 강한 강조가 필요한 사건.

### GameLogEntry

예상 필드:

- `Id`: 안정 식별자. v0는 증가 번호 또는 GUID 중 하나를 선택한다.
- `Channel`: `GameLogChannel`.
- `Severity`: `GameLogSeverity`.
- `Importance`: `GameLogImportance`.
- `Title`: 1줄 제목. v0 표시 fallback이며, 현지화 renderer가 없을 때 그대로 표시한다.
- `TitleKey`: optional localization key. 예: `combat.skill.stun_miss`.
- `Args`: optional localization arguments. v0는 string dictionary 또는 작은 typed arg 객체 중 구현자가 단순한 쪽을 선택하되, 저장 가능한 값만 담는다.
- `Body`: 상세 본문. optional.
- `OccurredAt`: 주차/턴/전투 시각을 담는 표시용 문자열 또는 구조체. Step 1에서 최소 형태 결정.
- `IsCollapsed`: 대화 아카이브처럼 접힘 표시가 필요한 항목.
- `Link`: optional `GameLogLink`.
- `Tags`: optional. v0에서는 생략 가능.

### GameLogLink

클릭 대상 예약 데이터. 직접 메서드를 들고 있지 않는다.

예상 필드:

- `Type`: `ReplayMarker`, `UnitDetail`, `TacticBoardBlock`, `DialogueArchive`, `ItemDetail`, `None`.
- `TargetId`: 대상 id.
- `Payload`: 필요하면 후속에서 추가. v0에서는 string dictionary를 피하고 typed fields 우선.

### IGameLogSink

```csharp
public interface IGameLogSink
{
    void Append(GameLogEntry entry);
}
```

### IGameLogInteractionHandler

```csharp
public interface IGameLogInteractionHandler
{
    bool CanHandle(GameLogLink link);
    void Handle(GameLogLink link);
}
```

v0는 no-op handler 또는 null handler로 읽기 전용이다.

### GameLogModel

책임:

- `Append(entry)`
- 최근 200줄 trim
- `Clear()`
- `GetEntries(filter)`
- `EntryAdded` signal/event 또는 UI 갱신 hook

주의:

- `GameLogModel`은 Godot `Control`을 몰라야 한다.
- 필터에서 `All + Event` 기본 뷰와 `Channel + Event/Detail` 뷰를 구분한다.

## SkillSystem Report Bridge

현재 상태:

- `SkillState.Reports`는 `List<string>`이다.
- 각 `EffectBlock`이 문자열을 직접 추가한다.
- 테스트는 문자열 prefix로 결과를 검증한다.

v0 권장:

- 기존 문자열 report를 즉시 제거하지 않는다.
- `SkillReportParser` 또는 `SkillReportToGameLogAdapter`가 raw report 문자열을 읽어 `GameLogEntry` 후보로 변환한다.
- 변환 정책은 E-9에 맞춘다.

초기 매핑:

| Source | Raw event | GameLog channel | Severity / Importance | 기본 뷰 |
| --- | --- | --- | --- | --- |
| SkillReport | `damage:*` | Combat | Detail / Normal | 숨김 |
| SkillReport | `miss:*` | Combat | Detail / Normal | 숨김 |
| SkillReport | `chain:*` | Combat | Detail / Normal | 숨김 |
| SkillReport | `knockback:*` | Combat | Detail / Normal | 숨김 |
| SkillReport | `bind:*` | Combat | Detail / Normal | 숨김 |
| SkillReport | `stun:*` | Combat | Detail / Normal | 숨김 |
| SkillReport | `stun_miss:*` | Combat | Detail / Normal | 숨김 |
| SkillReport | `selfbuff:*` | Combat | Detail / Normal | 숨김 |
| SkillReport | `cast_cancel:*` + resolved enemy target | Combat | Event / Normal | 표시 |
| SkillReport | `cast_cancel:*` + resolved ally target | Combat | Event / Warning | 표시 |
| Battle event | enemy killed | Combat | Event / Normal | 표시 |
| Battle event | ally death | Combat | Event / Critical | 표시 |

후속 목표:

- `SkillReport` 구조체와 `ISkillReportSink`를 도입한다.
- `SkillContext`에 report sink를 주입한다.
- `EffectBlock`은 문자열 대신 `SkillReport`를 기록한다.
- 전투 리포트와 GameLog는 같은 SkillReport raw fact에서 각자 표현 정책으로 분기한다.


### Dungeon Crawl Aggregation Policy (GL-001 범위 밖 — 후속)

> Step 0 결정([[GL-001-GameLog-Foundation-Review]] Finding 1): 아래 집계는 GL-001 v0 어느 Step에도 구현하지 않는다.
> 기본 뷰 5~8줄 예산은 **Detail을 기본 뷰에서 숨기는 것만으로** 충족되며(집계는 Detail 채널 탭 가독성용),
> 집계는 raw fact -> Event 승격/병합 정책이라 구조화 SkillReport/전투 이벤트 계층이 선행돼야 한다.
> 아래는 후속 Step/Task를 위한 정책 기록이다(E-9 전사).

기본 뷰의 5~8줄 예산을 지키기 위해 집계는 두 메커니즘으로 분리한다.

- 턴 내 다중 hit 병합: `turn + actor + target + action kind`로 묶는다. 연쇄, 다단 hit, 추가타 같은 같은 턴 안의 상세 로그를 합계 1줄로 줄인다.
- 턴 간 연속 행동 집계: `actor + target + action kind`로 묶고 `turn`은 키에서 제외한다. flush 조건은 처치, 다른 Event 개입, 행동 종류 변경, 대상 변경, 전투 종료다.
- 큰 피해/치명타 승격: 아군 대상이고 단일 hit가 최대 HP 25% 이상일 때만 `Event`로 승격한다.
- 중요 상태이상 승격: 스턴/속박만 Event 후보로 둔다. 일반 buff/debuff tick은 Detail이다.
- BT 조건 평가와 보드 행 평가 결과는 Detail이 아니라 Trace다.
## Open Decisions

Step 0(Design Review, [[GL-001-GameLog-Foundation-Review]])에서 5건 모두 종결했다.

1. **Id 정책 — 후보 A(증가 `long`) 확정.**
   - `GameLogModel.Append`가 카운터를 소유하고 append 시 Id를 부여한다. 호출자는 Id를 만들지 않는다.
   - 저장/정렬/테스트 단순. v0는 단일 로그 모델 안에서만 안정하면 된다.

2. **OccurredAt 형태 — 후보 A(표시용 string + 최소 numeric) 확정.**
   - Step 1은 표시용 string(`"W03"`, `"F2 T12"`) + optional numeric 최소화. 전투 리플레이 연결 시 구조화.
   - 집계 키(turn) 결합은 집계(아래 후속)와 함께 이관되므로 v0 string으로 충분.

3. **GameLogModel 수명 — Step 1~2는 후보 C(순수 C# model + LogWindowController 소유) 확정. 전역 수명은 Step 3 선행 결정으로 재오픈.**
   - 전투는 `battle_field.tscn`(별도 scene, 여전히 `run/main_scene`)에서 일어나고 LogWindow는 `workspace.tscn`에 있어, Workspace 소유 모델은 전투 중 존재하지 않는다.
   - Step 3 adapter가 전투 계층에서 모델에 닿는 경로(autoload `GameLog` vs shared service)는 Step 3 전에 ADR로 확정한다.

4. **SaveGame 통합 시점 — 후보 B 확정.**
   - GL-001은 모델의 trim-200 계약 + entry 직렬화 형태 고정까지. 실제 SaveSection 등록(E-9 결정 ④, `SaveGameManager` 경유)은 후속 Task.

5. **Skill report bridge 시점 — 후보 B 확정.**
   - Step 1은 GameLog core만, adapter는 Step 3. 먼저 데이터/필터/표시 정책을 고정한다.


## Step 3 선행 결정

2026-07-09 사용자 결정으로 Step 3 착수 전 선행 조건 3건을 닫았다.

1. **`cast_cancel` 진영 판정 — A안 확정: adapter가 target path를 resolve한다.**
   - raw report 문자열은 현재 emitter 형태(`cast_cancel:{target.GetPath()}`)를 유지한다.
   - adapter는 전투 컨텍스트를 주입받아 `targetPath`를 resolve하고, 대상이 적이면 `Combat/Event/Normal`, 아군이면 `Combat/Event/Warning`으로 변환한다.
   - target resolve 실패, freed node, 진영 판정 불가, malformed path는 fail-closed한다. gameplay는 계속 진행하고 로그 entry만 만들지 않는다.
   - Step 3 테스트는 enemy target, ally target, missing/freed/unresolved target을 모두 단언한다.

2. **GameLog 전역 수명 — A안 확정: `GameLogService` autoload.**
   - 결정: [[ADR-020-GameLog-Service-Lifetime]].
   - `GameLogService`가 `GameLogModel`을 소유하고 `IGameLogSink` 진입점을 제공한다.
   - `LogWindowController`는 Step 3에서 자기 로컬 모델 소유에서 service 모델 구독으로 승격한다.
   - 순수 모델 테스트는 계속 autoload 없이 가능해야 한다.

3. **`stun_miss`/`selfbuff` 어휘와 현지화 준비 — 처리 확정.**
   - `stun_miss` -> `Combat/Detail/Normal`.
   - `selfbuff` -> `Combat/Detail/Normal`.
   - unknown kind와 malformed report는 fail-closed한다.
   - adapter는 표시 문장만 만들지 않고 `TitleKey + Args + FallbackTitle`을 함께 만든다.
   - v0 LogWindow는 `FallbackTitle`/`Title`을 표시한다. 후속 localization renderer는 `TitleKey/Args`로 현재 언어 문장을 렌더한다.
   - `target.GetPath()`는 resolve용 입력이며, localization args에는 가능한 한 안정적인 target id/display id를 넣는다. path를 그대로 플레이어 표시 문자열로 쓰지 않는다.

## Step Plan

### Step 0: Design Review — 완료(판정: Approved after design fixes)

결과: [[GL-001-GameLog-Foundation-Review]]. E-9 확정판 대조 통과, Open Decision 5건 종결, 집계 정책 후속 이관,
Step 1 필터 계약·Step 3 선행 조건 명시. P0/P1 없음. Step 1은 착수 가능, Step 3은 선행 조건 3건 종결 후 착수.

목표:
- GameLog가 전역 로그 도메인으로 적절히 분리되어 있는지 검토한다.

작업 범위:
- 이 Task 문서 검토.
- `06_UXUI.md` E-9, WS-001 `Workspace-Window-System`, SkillSystem report 코드, SaveGame section 구조 대조.
- 필요하면 Task 문서만 수정.

제외 범위:
- 제품 코드, `.tscn`, `.tres` 구현 변경.

완료 조건:
- 데이터 모델, sink/handler 책임, SkillSystem report bridge, LogWindow UI Step이 구현 가능한 수준이다.
- Open Decision이 구현 전에 닫혀 있다.

검증 방법:
- 설계 리뷰 문서 작성.
- 판정: Approved / Approved after design fixes / Rework required.

### Step 1: GameLog Core Model

목표:
- Window/UI 없이 GameLog의 데이터와 필터 정책을 headless로 검증한다.

작업 범위:
- `Assets/Script/GameLog` 신규.
- `GameLogEntry`, `GameLogLink`, `GameLogChannel`, `GameLogSeverity`, `GameLogImportance`, `GameLogModel`, `IGameLogSink` 추가.
- append/filter/trim 200/clear 구현.
- `GameLogModel.Append`가 `long` Id를 부여(OD1). 호출자는 Id를 만들지 않는다.
- 대화 아카이브용 collapsed flag와 link 예약 필드 추가.
- **필터 계약 고정([[GL-001-GameLog-Foundation-Review]] Finding 3)**: 예) `GetEntries(GameLogChannel? channel, bool includeDetail, bool includeTrace)` 또는 동등한 `GameLogFilter`.
  - 기본 뷰 = `channel:null, includeDetail:false, includeTrace:false` (Event만).
  - 채널 탭 = `channel:Combat, includeDetail:true, includeTrace:false`.
  - debug = `includeTrace:true`.
- `IGameLogInteractionHandler`는 v0 읽기 전용이므로 no-op 껍데기로만 두거나 Step 2로 미룬다(Finding 6, 선택). `GameLogLink` 데이터 필드는 Step 1에서 예약한다.

제외 범위:
- Godot UI 표시.
- SaveGame 저장.
- SkillSystem report adapter.

완료 조건:
- append한 순서가 보존된다.
- 200줄 초과 시 오래된 entry부터 제거된다.
- 기본 뷰(`All + Event`)는 사건만 반환하고 `Detail`/`Trace`를 제외한다.
- 채널 필터는 해당 channel의 `Event`/`Detail`을 반환하고, `Trace`는 debug filter에서만 반환한다.
- Entry는 interaction handler를 직접 호출하지 않는다.

검증 방법:
- C# headless test scene `gl001_step1_game_log_model_test.tscn`.
- `dotnet build`.
- Godot `--import` exit 0.

#### 구현 결과 (2026-07-09, 코드 리뷰 완료)

변경 파일(전부 신규, 기존 코드 무변경 = 회귀 표면 없음):
- `Assets/Script/GameLog/GameLogChannel.cs` / `GameLogSeverity.cs` / `GameLogImportance.cs` / `GameLogLinkType.cs`: 분류 enum. `All`은 채널 enum에 없다(nullable channel 인자로 표현).
- `Assets/Script/GameLog/GameLogLink.cs`: 예약 클릭 데이터(`Type`, `TargetId`). Payload 없음(v0 typed field 우선).
- `Assets/Script/GameLog/GameLogEntry.cs`: 표시용 entry. `Id`는 모델이 부여(internal set), `Title` null->"" 정규화, `Body`/`OccurredAt`/`Link` nullable, `IsCollapsed`만 mutable(UI 토글용).
- `Assets/Script/GameLog/IGameLogSink.cs` / `IGameLogInteractionHandler.cs`: sink 계약 + 예약 handler 계약(v0 구현/호출부 없음).
- `Assets/Script/GameLog/GameLogModel.cs`: 순수 C# 모델(Godot 미의존, D6). `Append`(Id 부여 + `MaxEntries=200` trim, 오래된 것부터), `Clear`(Id 카운터 미리셋), `GetEntries(GameLogChannel? channel, bool includeDetail, bool includeTrace)`(Finding 3 필터 계약, Event 항상 포함), `EntryAdded`/`Cleared` 이벤트, `Count`.
- `Assets/Script/Tests/Gl001Step1GameLogModelTest.cs` + `.tscn`: headless 테스트 A~K(48 assertions).

설계 판단:
- 도메인 타입은 Godot Resource가 아니라 POCO(순수 C#)로 두었다 — headless 검증과 D6("모델은 UI를 모른다")에 부합. SaveGame(`.tres`/SaveSection)은 후속이라 Resource 강제가 없다.
- `IGameLogInteractionHandler`는 Finding 6 권고대로 계약만 두고 구현/호출부 없음. entry가 handler를 부르지 않음을 SpyHandler로 행위 검증([K]).
- 필터는 severity 집합(Event 항상 + Detail/Trace 옵트인) × nullable channel 단일 메서드로 표현. 기본 뷰=`(null,false,false)`, 채널 탭=`(Combat,true,false)`, debug=`includeTrace:true`.

검증:
- `dotnet build AutoCrawler.sln -c Debug`: 경고 0 / 오류 0.
- `godot --headless --path . --import`: editor load 성공(leaked/resources-in-use 경고는 clean import에도 나오는 benign shutdown noise).
- `gl001_step1_game_log_model_test.tscn`: **48/48 ALL PASS, exit 0**. 완료 조건 5건(순서 보존[A], 200 trim 오래된 것부터[B], 기본 뷰 Event-only[C], 채널 탭 Event/Detail·Trace debug 전용[D][E], entry가 handler 미호출[K]) + Verification Matrix(importance 노출 무관[F], 채널 격리[G], 빈 title/null body·link/null entry 경계[H], clear/id 단조[I], EntryAdded[J]) 커버.

남은 위험:
- 자체 최종 승인 안 함 — 별도 코드 리뷰 대기(Implementation-Prompt 규칙).
- Step 2(UI)/Step 3(adapter)은 미착수. Step 3은 선행 조건 3건 종결 후.

#### 코드 리뷰 결과 (2026-07-09, 판정: 완료)

결과: [[GL-001-GameLog-Foundation-Step1-Review]]. P0/P1/P2/P3 없음. dotnet build, Godot --import, gl001_step1_game_log_model_test.tscn 48/48 assertions 재실행 PASS. Step 2 착수 가능.

### Step 2: LogWindow Read-only UI

목표:
- Workspace `LogWindow` placeholder를 GameLog 표시 UI로 교체한다.

작업 범위:
- `LogWindowView`/`LogWindowController` 또는 동등한 UI script 추가.
- 탭/필터: 전체, 전투, 이야기, 획득·시스템.
- 기본 전체 탭은 Event만 표시.
- 채널 탭은 Event와 Detail을 표시하되, Detail은 시각적으로 구분한다.
- 대화 아카이브 entry는 제목 1줄 접힘으로 표시한다.
- 샘플 로그 주입 버튼 또는 fixture 전용 seed로 UI 확인.

제외 범위:
- 클릭으로 실제 창 열기.
- SaveGame 저장.
- 실제 전투/대화 시스템 연결.
- 최종 스킨.

완료 조건:
- `workspace.tscn`에서 Log 창을 열면 placeholder가 아니라 로그 목록이 보인다.
- Event/Detail 필터가 UI에서 동작한다.
- 대화 archive entry는 접힘 상태로 시작하고 펼칠 수 있다.
- 200줄 trim이 UI에도 반영된다.

검증 방법:
- headless 가능한 controller/model 테스트.
- `workspace.tscn` smoke.
- 필요 시 GUI probe 또는 screenshot.
- `dotnet build`, `--import`.

#### 구현 결과 (2026-07-09, 코드 리뷰 완료)

변경 파일:
- `Assets/Script/UI/Window/LogWindowController.cs` (신규): Log 창 읽기 전용 표시 UI(`Control`). `GameLogModel`을 소유(OD3 Step1-2)하고 tab 버튼(전체/전투/이야기/획득·시스템) + 스크롤 목록 + `샘플 주입` 버튼을 프로그램적으로 빌드. `Model`/`SelectTab`/`CurrentTab`/`CurrentEntries`/`RowCount`/`ToggleExpand`/`IsExpanded`/`IsBodyVisible`/`GetRowTitleColor`/`SeedSampleLog` 노출. `EntriesForTab`는 순수 static.
- `Assets/Script/UI/Window/LogChannelTab.cs` (신규): 필터 탭 enum(`All`/`Combat`/`Story`/`RewardSystem`).
- `Assets/Scenes/workspace.tscn` (수정): `Windows/LogWindow`의 placeholder Label(`append-only log dummy`)을 `LogView`(`LogWindowController`)로 교체. ext_resource `4_logctrl` 추가. **다른 창/노드 무변경**(managed window 등록·WS-001 배선 영향 없음).
- `Assets/Script/Tests/Gl001Step2LogWindowTest.cs` + `.tscn` (신규): headless 테스트 A~I(51 assertions).

설계 판단:
- **severity 정책은 Step 1 `GameLogModel`이 그대로 소유**(D6). 컨트롤러는 tab -> 채널 집합 필터(전체=Event만·모든 채널, 채널 탭=Event+Detail)와 위젯 렌더만 담당. Step 1 모델 API는 불변으로 뒀다.
- **"획득·시스템" 탭 = Reward+System 두 채널 병합**. 모델의 단일 채널 API를 확장하지 않고, `GetEntries(null, includeDetail, ...)`(severity는 모델이 적용) 결과를 컨트롤러가 채널 집합으로 좁힌다. append 순서(Id) 보존.
- Detail = 제목 색 감광(`Darkened`), importance = 색 강조(Warning 노랑/Critical 빨강). 색은 시각 구분에만 쓰고 노출 판단엔 안 씀(D7). Trace는 v0 LogWindow에 미노출(`includeTrace:false` 고정).
- 대화 아카이브(`IsCollapsed && Body`)는 접힘 시작, 헤더 클릭(또는 `ToggleExpand`)으로 본문 Label을 펼침. 그 외 entry는 제목 1줄.
- 컨트롤러는 `new` 가능해 headless 단위 테스트 가능. 링크 클릭으로 실제 창 열기는 Out of Scope(v0 읽기 전용).

검증:
- `dotnet build AutoCrawler.sln -c Debug`: 경고 0 / 오류 0.
- `godot --headless --path . --import`: exit 0(benign shutdown noise만).
- `gl001_step2_log_window_test.tscn`: **51/51 ALL PASS, exit 0**. 완료 조건 4건(workspace.tscn Log 창이 실제 controller로 배선·목록 표시[I], Event/Detail 탭 필터[B][H], 대화 archive 접힘 시작·펼침[D], 200 trim UI 반영[E]) + Detail/importance 시각 구분[C], append/clear 자동 갱신[F], 샘플 seed[G] 커버.
- 회귀: `ws001_step1_registry_test`(workspace.tscn 로드)·`ws001_step3_persistence_test`(workspace.tscn 다회 인스턴스) 모두 ALL PASS — 씬 로드 정상 + WS-001 무영향.

남은 위험:
- 자체 최종 승인 안 함 — 별도 코드 리뷰 대기.
- 실제 GUI 렌더(색/레이아웃 시각)는 headless 단언(색 값·Visible·RowCount)으로 대체했고 screenshot은 안 함. 최종 스킨은 Out of Scope.
- `_expanded` set은 trim된 id를 즉시 비우지 않음(렌더는 현재 entry만 보므로 무해, 200 상한). Clear에서만 정리.

#### 코드 리뷰 결과 (2026-07-09, 판정: 완료)

결과: [[GL-001-GameLog-Foundation-Step2-Review]]. P0/P1/P2 없음, P3 1건(`ToggleExpand` public API 계약 정리). `dotnet build`, Godot `--import`, `gl001_step2_log_window_test.tscn` 51/51, WS-001 step1/step3 회귀 재실행 PASS. Step 2 완료.

P3 후속 처리(2026-07-09): `ToggleExpand`가 collapsible entry(`IsCollapsed`+`Body`)가 아니거나 없는 id면 no-op이 되도록 `IsCollapsibleEntry` 가드를 추가해 주석 계약과 일치시켰다(`_expanded`에 임의 id 미적재). 테스트 [D]에 non-collapsible/unknown id no-op 단언 2건 추가. 재검증: `dotnet build` 0/0, `gl001_step2_log_window_test.tscn` 53/53 ALL PASS. `.tscn`/모델 무변경이라 WS-001 회귀 영향 없음.

### Step 3: Skill Report Adapter

목표:
- 현재 SkillSystem raw report 문자열을 GameLogEntry로 변환하는 경계를 만든다.

선행 조건(2026-07-09 종결):
- **진영 판정 메커니즘(Finding 2)**: A안 확정. adapter가 전투 컨텍스트를 주입받아 `cast_cancel:{target.GetPath()}`의 target path를 resolve한다. enemy target은 `Event/Normal`, ally target은 `Event/Warning`. resolve 실패/freed/진영 불명은 fail-closed.
- **전역 수명 ADR(Finding 5 / OD3 재오픈)**: A안 확정. [[ADR-020-GameLog-Service-Lifetime]]에 따라 `GameLogService` autoload가 `GameLogModel`을 소유한다.
- **어휘/현지화 준비(Finding 4)**: `stun_miss`와 `selfbuff`는 `Combat/Detail/Normal`으로 처리한다. adapter는 `TitleKey + Args + FallbackTitle`을 만든다. unknown/malformed는 fail-closed.

작업 범위:
- `GameLogService` autoload 추가 및 `project.godot` 등록([[ADR-020-GameLog-Service-Lifetime]]).
- `LogWindowController`를 service 모델 구독으로 승격하되, 테스트용 순수 모델/주입 경로는 유지.
- `GameLogEntry`에 localization-ready 필드(`TitleKey`, `Args`, `FallbackTitle` 또는 동등한 계약) 추가. v0 UI는 fallback title을 표시.
- `SkillReportToGameLogAdapter` 또는 `SkillRawReportParser` 추가.
- `SkillState.Reports` 문자열 형식 중 v0 주요 항목(`damage`, `miss`, `stun`, `stun_miss`, `bind`, `cast_cancel`, `chain`, `knockback`, `manadrain`, `selfbuff`) 파싱.
- **실제 emitter와 어휘 정합(Finding 4)**: 블록이 내보내는 `stun_miss:`와 `selfbuff:`를 처리한다 — 둘 다 `Combat/Detail/Normal`.
- E-9 정책에 맞춰 channel/severity/importance 매핑.
- `cast_cancel`은 adapter가 target path를 resolve해 enemy target = Event/Normal, ally target = Event/Warning으로 분기. resolve 실패/freed/진영 불명은 fail-closed.
- malformed raw report fail-closed.

제외 범위:
- EffectBlock들이 직접 구조화 `SkillReport`를 발행하도록 바꾸는 마이그레이션.
- BattleSession/전투 이벤트 레벨의 kill/death 발행 구현. GL-001 Step 3은 SkillReport adapter까지만 다루며, 사망/처치는 Health -> Dead 사건 기반 별도 Step/Task에서 연결한다.
- 전투 리포트 문장 생성.

완료 조건:
- `damage`/`miss` 등 상세 report는 Combat Detail로 변환된다.
- `cast_cancel`은 대상 진영에 따라 Combat Event/Normal 또는 Combat Event/Warning으로 변환된다.
- `stun_miss`/`selfbuff`는 Combat Detail/Normal으로 변환된다.
- adapter가 만든 entry는 `TitleKey`/`Args`/fallback title을 보존한다.
- malformed/unknown/unresolved raw report는 로그 생성 없이 안전하게 건너뛴다.
- 기존 SK-001 테스트의 문자열 report 검증은 깨지지 않는다.

검증 방법:
- parser/adapter headless test(enemy/ally `cast_cancel`, unresolved target fail-closed, `stun_miss`, `selfbuff`, unknown/malformed, localization key/args 보존).
- GameLogService autoload/service wiring test.
- LogWindowController service 구독 회귀.
- SK-001 관련 최소 회귀.
- `dotnet build`, `--import`.

#### 구현 결과 (2026-07-09, 코드 리뷰 완료)

변경 파일:
- `Assets/Script/GameLog/GameLogEntry.cs` (수정): `TitleKey`(현지화 키)·`Args`(name→value string dict, null이면 빈 dict) 추가. `Title`은 v0 표시 fallback으로 유지. 생성자에 optional 인자 뒤에 추가라 Step 1/2 호출부 무변경(additive).
- `Assets/Script/GameLog/GameLogService.cs` (신규): autoload Node, `GameLogModel` 소유 + `IGameLogSink`(ADR-020). sink/보관만 책임(entry 생성은 producer).
- `Assets/Script/GameLog/ISkillTargetFactionResolver.cs` (신규): `LogFaction`(Unknown/Ally/Enemy) enum + resolver 계약.
- `Assets/Script/GameLog/SceneTreeFactionResolver.cs` (신규): production resolver. target path를 씬 트리에서 resolve → Article 부모 노드명("Opponent"/"Ally")으로 진영 판정(`ArticlesContainer` 규약 재사용). 없음/freed/Neutral·기타 = Unknown.
- `Assets/Script/GameLog/SkillReportToGameLogAdapter.cs` (신규): `Convert(raw, resolver)` + `PublishReports(reports, resolver, sink)`. E-9 매핑, fail-closed.
- `Assets/Script/UI/Window/LogWindowController.cs` (수정): 모델 해석을 `주입 > autoload service > 로컬` 순으로 승격(ADR-020). `UseModel` 주입 seam 추가. service 없으면 로컬 모델로 fail-closed.
- `project.godot` (수정): autoload에 `GameLogService` 등록.
- `Assets/Script/Tests/Gl001Step2LogWindowTest.cs` (수정): `NewController`가 `UseModel(new GameLogModel())`로 격리(autoload 공유 모델과 분리).
- `Assets/Script/Tests/Gl001Step3AdapterTest.cs` + `.tscn` (신규): headless 테스트 A~G.

설계 판단:
- **진영 판정 = A안**: adapter가 resolver를 주입받아 `cast_cancel:{path}`의 target path를 resolve. enemy→Event/Normal, ally→Event/Warning, 그 외(unresolved/freed/Neutral/null resolver)→fail-closed. 진영 규약은 `ArticlesContainer`의 부모 노드명 방식을 그대로 재사용해 중복 정의 회피.
- **어휘 정합**: `stun_miss`/`selfbuff` 포함 9종 raw kind를 명시 처리. 모두 Combat/Detail/Normal(단 cast_cancel만 Event). unknown kind/토큰 수 불일치/빈 path는 fail-closed.
- **현지화 준비**: adapter가 `TitleKey`(`combat.skill.*`) + `Args`(named) + `Title`(fallback)을 만든다. v0 UI는 fallback을 표시(renderer 후속).
- **live battle 미배선(범위 경계)**: `PublishReports`가 발행 경계지만, TurnAction/전투 흐름에 hook을 심지 않았다. Step 3 완료 조건은 변환/서비스/구독 정확성이며, live 배선은 kill/death와 동급의 전투 이벤트 통합으로 후속. CB-001 결정론 회귀 위험을 피한다.
- **path 파싱**: Godot 노드명에 ':'가 불가하므로 `Split(':')`의 마지막 토큰이 항상 target path. kind별 정확한 토큰 수로 malformed 판정.

검증:
- `dotnet build AutoCrawler.sln -c Debug`: 경고 0 / 오류 0.
- `godot --headless --path . --import`: exit 0(benign noise만). `GameLogService` autoload 로드 확인.
- `gl001_step3_adapter_test.tscn`: **ALL PASS**(A 변환 9종+선택 인자, B cast_cancel enemy/ally/불명/null-resolver, C malformed/unknown 7종, D PublishReports, E SceneTreeFactionResolver 진영, F service autoload+IGameLogSink, G 컨트롤러 service 구독 & 주입 격리).
- 회귀: `gl001_step1`(loc 필드 additive)·`gl001_step2`(autoload 등장 후 주입 격리·workspace service 구독)·`sk001_step3_mana_hit`(report 문자열 무손상)·`ws001_step3`(workspace.tscn 로드) 모두 ALL PASS.

남은 위험:
- 자체 최종 승인 안 함 — 별도 코드 리뷰 대기.
- adapter가 live 전투에 미배선(위 설계 판단). 실제 전투 중 로그 표출은 후속(전투 이벤트 통합 + kill/death 발행과 함께).
- `SceneTreeFactionResolver`는 `battle_field.tscn` 실제 Article 트리에서 headless로 직접 검증하지 않고, 부모 노드명 규약을 순수 Node 트리로 검증했다(실제 씬 배선 확인은 live 배선 시).


#### 코드 리뷰 결과 (2026-07-09, 판정: 완료)

결과: [[GL-001-GameLog-Foundation-Step3-Review]]. P0/P1/P2/P3 없음. `dotnet build`, Godot `--import`, `gl001_step3_adapter_test.tscn`, GL-001 step1/step2 회귀, `sk001_step3_mana_hit_test.tscn`, `ws001_step3_persistence_test.tscn` 재실행 PASS. Step 4 문서/완료 리뷰 착수 가능.

### Step 4: Docs and Completion Review

목표:
- GameLog 기반의 현재 사실과 후속 작업을 Wiki에 정리한다.

작업 범위:
- `20_Systems/GameLog-System.md` 신규.
- `Current-State`, `Open-Tasks`, 이 Task 문서 갱신.
- Review 문서 작성.

제외 범위:
- 새 기능 구현.

완료 조건:
- GameLog core, LogWindow v0, Skill report adapter의 완료 조건과 검증 결과가 리뷰에 대조되어 있다.
- SaveGame 저장, interaction handler 실제 연결, 구조화 SkillReport 마이그레이션이 후속으로 남아 있다.

검증 방법:
- 문서 링크/상태 정적 검토.

#### 구현 결과 (2026-07-09, 완료)

변경 문서(제품 코드 무변경):
- `LLM_WIKI/20_Systems/GameLog-System.md` (신규): GameLog 현재 사실 — 위치, 순수 C# 도메인, `GameLogModel` 필터 정책, `GameLogService` autoload, `LogWindowController` UI, `SkillReportToGameLogAdapter`+진영 resolver 매핑표, 검증 자산, 설계 경계(후속).
- `LLM_WIKI/50_Reviews/GL-001-GameLog-Foundation-Completion-Review.md` (신규): Step 1~4 완료 대조 + Verification Matrix 대조 + 검증 재확인 + 후속. 판정 완료.
- `LLM_WIKI/00_Index/Home.md`: 시스템 목록에 `[[GameLog-System]]`, 리뷰 목록에 완료 리뷰 추가.
- `LLM_WIKI/00_Index/Current-State.md`: GameLog 절을 GL-001 전체 완료로 갱신, 사실은 `[[GameLog-System]]`이 보존.
- `LLM_WIKI/00_Index/Open-Tasks.md`: GL-001을 Recently Completed로 이동.

완료 조건 충족: core/LogWindow v0/Skill adapter의 완료 조건·검증이 완료 리뷰에 대조됨. SaveGame 저장, interaction handler 실제 연결, 구조화 SkillReport 마이그레이션, adapter live 배선, 집계, 현지화 renderer가 후속으로 명시됨.

**GL-001 Step 0~4 전체 완료.** 사실은 [[GameLog-System]], 판정은 [[GL-001-GameLog-Foundation-Completion-Review]]가 보존한다. 이 Task는 `complete`로 닫는다.

## Verification Matrix

| 영역 | 정상 | 실패/경계 |
| --- | --- | --- |
| Core model | append/filter/clear/trim | 200 초과, 빈 title/body, null link |
| Severity policy | Event 기본 뷰 표시, Detail 채널 탭 표시, Trace debug 전용 | Detail/Trace가 전체 기본 뷰에 섞이지 않음 |
| Importance policy | Normal/Warning/Critical 강조 분리 | Importance가 노출 범위 판단에 쓰이지 않음 |
| Channel policy | Combat/Story/Reward/System 필터 | All은 저장 채널로 쓰지 않음 |
| Interaction | link 데이터 보존 | Entry가 직접 메서드 실행하지 않음 |
| UI | 탭 전환, 대화 접힘/펼침 | 긴 텍스트, 빈 로그, 200줄 표시 |
| Skill adapter | raw report -> GameLogEntry | malformed raw report, unknown kind |
| Scope boundary | 읽기 전용 v0 | SaveGame/리플레이/창 열기 미연결 |

## Related

- [[Workspace-Window-System]]
- [[WS-001-Native-Window-Workspace-Shell]]
- [[Skill-System]]
- [[SK-001-Data-Driven-Skill-System]]
- [[SaveGame-System]]
- [[GL-001-GameLog-Foundation-Review]]
- [[Open-Tasks]]
- [[Current-State]]
