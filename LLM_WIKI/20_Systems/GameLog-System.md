---
type: system
project: AutoCrawler
system: GameLog
updated: 2026-07-09
tags: [system, gamelog, log, ui, workspace, skill, adapter]
---

# GameLog System

게임 전역 사건을 수집·표시하는 로그 기반이다. 전투/아웃게임/대화/시스템 알림/획득이 같은 sink로 사건을
보내고, Workspace의 `Log` 창이 첫 표시 UI다. 로그 데이터(도메인)와 표시 UI를 분리하고, raw fact와
표시용 entry를 adapter로 나눈다(기획서 06_UXUI E-9).

**GL-001 전체 완료(Step 0~4).** core 도메인 + LogWindow read-only UI + Skill report adapter/service + 시스템 문서/완료 리뷰.
근거·검증은 [[GL-001-GameLog-Foundation]]과 각 Step 리뷰가 보존한다. 설계 판단은
[[GL-001-GameLog-Foundation-Review]](Step 0)와 [[ADR-020-GameLog-Service-Lifetime]]에 있다.

## 위치

- `Assets/Script/GameLog/` — Godot UI를 모르는 순수 C# 도메인 + service + adapter.
  - `GameLogEntry.cs`, `GameLogLink.cs`, `GameLogChannel.cs`, `GameLogSeverity.cs`, `GameLogImportance.cs`, `GameLogLinkType.cs`
  - `GameLogModel.cs`, `IGameLogSink.cs`, `IGameLogInteractionHandler.cs`
  - `GameLogService.cs`(autoload), `ISkillTargetFactionResolver.cs`(+`LogFaction`), `SceneTreeFactionResolver.cs`, `SkillReportToGameLogAdapter.cs`
- `Assets/Script/UI/Window/LogWindowController.cs` + `LogChannelTab.cs` — `Log` 창 표시 UI.
- `Assets/Scenes/workspace.tscn` — `Windows/LogWindow`의 `LogView` 노드가 `LogWindowController`다.
- `project.godot` — `GameLogService` autoload 등록(`/root/GameLogService`).

## 도메인 모델 (순수 C#, D6)

Godot Resource가 아니라 POCO다. headless 검증 가능하고 UI를 모른다. SaveGame `.tres`/SaveSection 통합은 후속.

- **`GameLogChannel`**: `Combat`/`Story`/`Reward`/`System`. `All`은 채널이 아니라 nullable 필터로 표현한다.
- **`GameLogSeverity`**: `Event`(기본 뷰) / `Detail`(채널 탭) / `Trace`(debug 전용). 노출 범위.
- **`GameLogImportance`**: `Normal`/`Warning`/`Critical`. 시각 강조. **노출 범위 판단에 쓰지 않는다(D7).**
- **`GameLogEntry`**: 표시용 항목. `Id`(모델이 부여), `Channel`/`Severity`/`Importance`, `Title`(v0 표시 fallback),
  `TitleKey`+`Args`(현지화 준비, name→value string dict), `Body`(optional), `OccurredAt`(표시용 string),
  `IsCollapsed`(대화 아카이브), `Link`(예약). entry는 데이터만 담고 실행하지 않는다(D2).
- **`GameLogLink`**: 클릭 대상 예약 데이터(`GameLogLinkType Type`, `string TargetId`). v0는 예약만.
- **`IGameLogSink`**: `Append(entry)` 최소 계약(D3).
- **`IGameLogInteractionHandler`**: `CanHandle`/`Handle` 예약 계약. v0 구현/호출부 없음(읽기 전용).

## GameLogModel

- `Append(entry)`: `long` Id를 1부터 증가 부여하고 리스트에 넣는다. `MaxEntries = 200` 초과 시 가장 오래된 것부터 제거.
- `Clear()`: 전부 비운다. Id 카운터는 리셋하지 않는다(모델 수명 동안 단조 증가).
- `GetEntries(GameLogChannel? channel, bool includeDetail, bool includeTrace)`: append 순서를 유지한 스냅샷.
  `Event`는 항상 포함, `Detail`/`Trace`는 옵트인. `channel==null`은 모든 채널.
- `EntryAdded`/`Cleared` 이벤트, `Count`.
- **필터 정책(E-9)**: 기본 뷰 = `GetEntries(null, false, false)`(모든 채널 Event만). 채널 탭 = `GetEntries(ch, true, false)`
  (Event+Detail). `Trace`는 `includeTrace:true`에서만. Importance는 필터에 관여하지 않는다.

## GameLogService (autoload, ADR-020)

- `/root/GameLogService`. `GameLogModel`을 소유하고 `IGameLogSink`를 노출한다(sink/보관만 책임).
- 전투(`battle_field.tscn`)·아웃게임·대화가 서로 다른 씬에서도 같은 sink에 닿게 하는 수명 경계.
- entry **생성 정책**은 producer/adapter가 소유한다. SaveSection 통합은 이 서비스에 붙일 후속 지점.

## LogWindow 표시 UI (`LogWindowController`)

`workspace.tscn` `Log` 창의 placeholder를 대체한 읽기 전용 `Control`.

- **모델 해석**: `UseModel` 주입 > `/root/GameLogService` 모델 > 로컬 `new GameLogModel()`(service 없으면 fail-closed).
  주입 seam은 headless 테스트 격리용이다(ADR-020).
- **탭**: `전체`/`전투`/`이야기`/`획득·시스템`(`LogChannelTab`). severity 정책은 모델이 소유하고, 컨트롤러는
  tab→채널 집합만 좁힌다. `획득·시스템`은 Reward+System 두 채널을 병합(모델 단일 채널 API 불변, Id 순서 보존).
- **시각 구분**: `Detail`은 제목 색 감광, importance는 색 강조(Warning 노랑/Critical 빨강). `Trace`는 미노출.
- **대화 아카이브**: `IsCollapsed && Body`는 제목 1줄 접힘으로 시작하고 헤더 클릭(`ToggleExpand`)으로 본문을 펼친다.
  `ToggleExpand`는 collapsible entry가 아니면 no-op이다.
- **200 trim은 UI에 자동 반영**(append/clear에서 rebuild). `샘플 주입` 버튼(`SeedSampleLog`)으로 GUI 확인.

## Skill Report Adapter (`SkillReportToGameLogAdapter`)

`SkillState.Reports`의 raw 문자열을 표시용 `GameLogEntry`로 변환하는 경계(D4). EffectBlock을 구조화하지 않고
문자열 계약을 읽어 E-9로 매핑한다. `Convert(raw, resolver)`는 로그를 만들지 않아야 하면 `null`(fail-closed).

| raw kind | channel / severity / importance | 비고 |
| --- | --- | --- |
| `damage` `miss` `stun` `stun_miss` `bind` `chain` `knockback` `manadrain` `selfbuff` | Combat / Detail / Normal | 기본 뷰 숨김 |
| `cast_cancel` + enemy target | Combat / Event / Normal | 아군이 적 영창 끊음 |
| `cast_cancel` + ally target | Combat / Event / Warning | 아군 영창 끊김 |

- **진영 판정**: `ISkillTargetFactionResolver`. production은 `SceneTreeFactionResolver`가 target path를 씬 트리에서
  resolve해 Article 부모 노드명("Opponent"→Enemy, "Ally"→Ally)으로 판정한다(`ArticlesContainer` 규약 재사용).
  없음/freed/Neutral·기타/`cast_cancel`에 resolver 없음 = `Unknown` → fail-closed.
- **파싱**: Godot 노드명에 `:`가 불가하므로 `Split(':')`의 마지막 토큰이 항상 target path. kind별 정확한 토큰 수로
  malformed(토큰 수 불일치·빈 path·unknown kind)를 걸러 fail-closed한다.
- **현지화**: 각 entry에 `TitleKey`(`combat.skill.*`) + `Args`(named) + `Title`(fallback). v0 UI는 fallback을 표시.
- **`PublishReports(reports, resolver, sink)`**: 변환+append 발행 경계. 미래의 전투 hook이 부를 지점이며,
  **v0는 live battle 실행 흐름에 배선하지 않았다**(아래 경계).

## 검증

헤드리스 테스트(`Assets/Script/Tests/gl001_step1~3`):

- `gl001_step1_game_log_model_test`: append 순서·Id, 200 trim, severity/channel 필터, importance 독립, link 보존, clear.
- `gl001_step2_log_window_test`: 탭 필터, Detail/importance 시각 구분, 대화 접힘/펼침, 200 trim UI 반영, 샘플 seed,
  `workspace.tscn` LogView 배선, `EntriesForTab` 순수 함수.
- `gl001_step3_adapter_test`: 변환 매트릭스(9종), cast_cancel enemy/ally/unresolved, malformed/unknown fail-closed,
  `PublishReports`, `SceneTreeFactionResolver` 진영, `GameLogService` autoload+`IGameLogSink`, 컨트롤러 service 구독/주입 격리.

회귀: SK-001(`sk001_step3_mana_hit`, report 문자열 무손상)·WS-001(`ws001_step1`/`step3`, `workspace.tscn` 로드) PASS.

## 설계 경계 (GL-001 범위 밖 — 후속)

- **adapter live 배선**: `PublishReports`를 실제 전투 실행(TurnAction/turn flow)에 hook하는 작업. kill/death 전투
  이벤트(Health→Dead) 발행과 함께 별도 Step/Task. CB-001 결정론 회귀 위험을 피해 v0에서 미배선.
- **SaveGame SaveSection 통합**: 최근 200개 entry 저장(E-9 결정 ④). `GameLogService`에 붙일 후속.
- **interaction handler 실제 연결**: link 클릭 → 창 열림(리플레이/유닛 상세/보드/대화 아카이브). v0는 예약만.
- **던전 크롤식 집계**: 턴 내/턴 간 병합, 큰 피해 Event 승격. 정책만 [[GL-001-GameLog-Foundation]]에 기록, 미구현.
- **구조화 `SkillReport` 마이그레이션**: EffectBlock이 문자열 대신 `SkillReport`를 발행하도록 전환.
- **현지화 renderer**: `TitleKey`+`Args`로 실제 지역화 문자열을 만드는 표시 계층. v0는 fallback title만 표시.
- **아웃게임 이벤트 자동 생성**: WorldState `value_changed`에서 Story/Reward/System 로그 생성.

## Related

- [[GL-001-GameLog-Foundation]]
- [[GL-001-GameLog-Foundation-Review]]
- [[ADR-020-GameLog-Service-Lifetime]]
- [[Workspace-Window-System]]
- [[Skill-System]]
- [[SaveGame-System]]
- [[Current-State]]
