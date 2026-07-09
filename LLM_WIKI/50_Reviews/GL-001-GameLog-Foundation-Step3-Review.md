---
id: GL-001-Step3-Review
type: review
task: GL-001
step: 3
status: complete
reviewed: 2026-07-09
---

# GL-001 GameLog Foundation Step 3 Review

## Verdict

완료. P0/P1/P2/P3 발견 사항 없음.

Step 3의 목표였던 SkillSystem raw report -> GameLogEntry adapter, `GameLogService` autoload 수명, `LogWindowController` service 구독 승격이 Task/ADR-020과 정합한다. live 전투 hook은 Task에서 명시한 범위 밖이며, `PublishReports`가 후속 배선 경계로 남아 있다.

## Findings

없음.

## Review Notes

- `GameLogEntry`의 `TitleKey`/`Args` 추가는 additive이며 기존 Step 1/2 호출부를 깨지 않는다.
- `GameLogService`는 `GameLogModel` 소유와 `IGameLogSink.Append` 위임만 담당해 ADR-020의 "service는 sink/보관 책임만 가진다" 경계를 지킨다.
- `SkillReportToGameLogAdapter`는 실제 emitter 문자열(`damage`, `miss`, `stun`, `stun_miss`, `bind`, `cast_cancel`, `chain`, `knockback`, `manadrain`, `selfbuff`)과 토큰 수가 맞다.
- `cast_cancel`은 resolver 주입으로 enemy -> `Event/Normal`, ally -> `Event/Warning`, unknown/unresolved/null resolver -> fail-closed로 처리한다.
- `SceneTreeFactionResolver`의 `"Opponent"`/`"Ally"` 부모 노드명 규약은 `ArticlesContainer`와 `battle_field.tscn`의 현재 구조와 일치한다.
- `LogWindowController`는 주입 모델 우선, 없으면 `/root/GameLogService`, 없으면 로컬 모델 fallback 순서로 동작해 테스트 격리와 standalone 안전성을 같이 유지한다.

## Verification

- `git status --short`: GL-001/WS-001 관련 미커밋 변경이 다수 있음을 확인. 리뷰 중 기존 변경은 되돌리지 않음.
- `dotnet build AutoCrawler.sln -c Debug`: PASS, 경고 0 / 오류 0.
- Godot `--headless --path . --import`: PASS, exit 0. 기존 benign shutdown noise(`ObjectDB instances leaked`, resources in use)만 확인.
- `Assets/Script/Tests/gl001_step3_adapter_test.tscn`: PASS, ALL PASS.
- `Assets/Script/Tests/gl001_step1_game_log_model_test.tscn`: PASS, ALL PASS.
- `Assets/Script/Tests/gl001_step2_log_window_test.tscn`: PASS, ALL PASS.
- `Assets/Script/Tests/sk001_step3_mana_hit_test.tscn`: PASS, ALL PASS.
- `Assets/Script/Tests/ws001_step3_persistence_test.tscn`: PASS, ALL PASS. 테스트가 의도적으로 발생시키는 ConfigFile parse/warning diagnostics는 기존 expected path다.

## Not Verified

- 실제 전투 중 adapter live hook은 Step 3 범위 밖이라 검증하지 않았다.
- `battle_field.tscn` 실전 Article 트리에 `SceneTreeFactionResolver`를 붙여 raw `target.GetPath()`를 직접 resolve하는 통합 경로는 아직 없음. 후속 live 배선 시 확인한다.
- GUI screenshot/시각 레이아웃은 실행하지 않았다. Step 3은 adapter/service 수명 변경이며, Step 2 headless UI 회귀로 대체했다.

## Next

- Step 4에서 `20_Systems/GameLog-System.md`를 작성하고 GL-001 완료 리뷰를 진행한다.
- 후속 Task에서 live 전투 hook, kill/death 전투 이벤트 발행, 구조화 `SkillReport` 마이그레이션, SaveGame 저장, interaction handler 실제 연결을 분리한다.
