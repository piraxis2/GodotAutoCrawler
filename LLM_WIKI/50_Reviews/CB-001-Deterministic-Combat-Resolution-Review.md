---
type: review
task: CB-001
status: completed
reviewed: 2026-07-05
---

# CB-001 Deterministic Combat Resolution Review

현 전투 구조(TurnHelper + CharacterArticle + TurnAction + BehaviorTree)를 유지한 채 "같은 시드 + 같은
초기 배치 → 같은 전투 결과"를 성립시킨 1단계 작업의 완료 리뷰다. 설계 규약은
[[ADR-017-Deterministic-Combat-Resolution]](accepted)으로 확정했다.

## 범위와 산출물

제품 코드 변경:

- `CharacterArticle.TurnPlay()`의 매 physics frame `ApplyAffectingStatuses()` 호출 제거,
  `ITurnAffectedArticle.ApplyTurnStartEffects()` hook + `TurnHelper.AdvanceToNextTurn()` 턴 시작 1회
  적용(Step 1 — 프레임 수/배속 종속 버그 수정이기도 함).
- `TurnHelper` 소유 seed 가능 `CombatRng`(`_combatSeed` export, `ResetCombatRng`, `CombatRandRange`
  오버로드). `PhysicalDamage` 크리티컬/데미지 롤이 전역 `GD.RandRange` 대신 이 RNG를 소비하고, 전투
  context 부재 시 fail-fast(Step 2).
- ADR-017 정규 순서(거리 → Y → X)의 공용 구현을 `SkillUtil`로 집약(`ManhattanDistance`,
  `OrderByCanonical`/`ThenByCanonical`, `SelectCanonicalPath`). `GetAttackRangePositions`는 정렬된
  `IReadOnlyList<Vector2I>` 반환(기존 `HashSet` 열거 순서 의존 제거). 적용:
  `TurnActionBase.GetTarget`, `TurnAction_ChainLightning.GetChainTarget`(미타격 우선 → 정규 순서),
  `BehaviorTree_Move`/`BehaviorTree_MultipleMove` 경로 선택. 턴 순서는 `ArticleBase.SpawnIndex`
  (`ArticlesContainer` 등록 시 씬 트리 순회 순서 부여) 기반 `Priority` → `SpawnIndex` 안정 정렬로
  교체(Step 3).

신규 테스트(모두 `Assets/Script/Tests/`, C# 헤드리스):

- `cb001_step1_turn_effect_test` — 턴 시작 효과 1회 적용, Running frame/배속 재적용 방지, game over 미적용.
- `cb001_step2_combat_rng_test` — 같은 seed RNG/PhysicalDamage 시퀀스 재현, 금지 난수 API 정적 가드.
- `cb001_step3_canonical_order_test` — 사거리 오프셋 정규 순서, 타겟/체인 동점자, 턴 순서 안정 정렬,
  경로 동점자.
- `cb001_step4_determinism_test` — 실제 `battle_field.tscn` 결정론 회귀(아래).

## 발견 사항

P0/P1 결함 없음. 진행 중 리뷰에서 나온 지적과 처리:

- **[P2, Step 3 리뷰] 정규 순서 프리미티브의 배선 레벨 보증 부재** → Step 4가 실제 전투 씬에서
  근접(`GetTarget`)/체인(`GetChainTarget`)/단일 이동(`FindTarget`)/다중 이동(`FindPath`) 소비를 씬
  레벨로 보증해 종결했다.
- **[P3] 프로덕션 시드 고정(기본 1), 정적 가드 우회 패턴(`new System.Random(`/`Random.Shared`),
  `CharacterArticle.AttackRangePositions` count 동점자, `SpawnIndex` ready 순서 의존** → 모두 Task
  Follow-ups와 [[Turn-System]] Known Gaps에 기록. 1단계 결정론 성립에는 영향 없음.
- **씬 구성 사실(Step 4 발견):** 출하된 `battle_field.tscn`의 Opponent Puppet 4기는 BehaviorTree가
  비어 있어 근접 공격·단일 이동·`PhysicalDamage`(전투 RNG) 경로가 프로덕션 씬에서 실행되지 않는다.
  Step 4 테스트가 Puppet 1기에 근접 BT를 프로그램적으로 구성해 커버했다. 게임 콘텐츠 관점의 공백은
  CB-001 범위 밖 사실로 기록만 한다.
- **테스트 설계 이슈 2건(제품 결함 아님, Step 4 Result에 기록):** 근접 롤 표본 1회로 인한 시드 스모크
  우연 일치 → HP 버프로 롤 ~30회 확보. 근접 유닛 재배치로 인한 사망 기록 소실 → 원위치 유지.

## 예상된 관찰 변화(Failure Policy 기록)

- 사거리 내 적이 여럿일 때 첫 타겟이 해시 열거 순서 대신 정규 순서 대상으로 바뀔 수 있다.
- 같은 Priority 유닛의 턴 순서가 스폰 순번으로 고정된다.
- 지속 효과가 프레임당 중복 적용에서 턴당 1회로 줄어 효과가 약해진다(의도된 버그 수정).
  기존 순서/횟수는 런타임 내부 상태 의존이라 기준선 비교는 하지 않았고, Step 4 회귀가 새 규칙 기준의
  기준선을 고정한다.

## 검증 결과

### Completion 회귀 matrix (2026-07-05 재실행)

- `dotnet build AutoCrawler.sln -c Debug` — 경고 0, 오류 0
- `godot --headless --path . --import` — exit 0, 0 parse error
- `cb001_step1_turn_effect_test.tscn` — **ALL PASS**, SCRIPT ERROR 0
- `cb001_step2_combat_rng_test.tscn` — **ALL PASS**, SCRIPT ERROR 0
- `cb001_step3_canonical_order_test.tscn` — **ALL PASS**(12/12), SCRIPT ERROR 0
- `cb001_step4_determinism_test.tscn` — **ALL PASS**(8/8), SCRIPT ERROR 0
- `addons/behaviortree/tests/bt_validation_test.tscn`(BT-001) — **ALL PASS**, SCRIPT ERROR 0

### Completion Criteria 대조

| 완료 조건 | 결과 |
| --- | --- |
| 같은 시드 + 같은 초기 배치 → 같은 전투 결과 | Step 4 [A]: seed 777 × 2회 이벤트 로그(턴 순서/HP 변화/사망 순서/최종 HP·타일) 완전 일치 |
| `Speed` 배속·프레임 수가 결과에 영향 없음 | Step 1 [B](Speed 1/4 효과 횟수 동일) + Step 4 [C](Speed 2 vs 6 로그 완전 일치) |
| 전투 코드 `GD.Rand*` 직접 사용 금지 정적 가드 | Step 2 [C] PASS(+ `new Random(`/`Guid.NewGuid`) |
| 동점자 규칙 ADR 문서화 + 테스트 검증 | ADR-017 accepted, Step 3 12/12 + Step 4 [D] 배선 보증 |
| SCRIPT ERROR 0, `--import` 0 parse error, BT-001 회귀 GREEN | 위 matrix 전부 충족 |

## Remaining Risk

- 프로덕션 전투는 아직 고정 seed(1)로 돈다. seed 생성/기록 정책은 Follow-up(2단계 리플레이 포맷과 함께
  결정 권장).
- `AStarGrid2D` 경로의 동일 비용 타이브레이킹은 엔진 내부 구현에 의존한다. 같은 엔진 버전에서는
  결정적이며, 엔진 업그레이드 시 Step 4 회귀가 깨짐을 감지한다.
- `Damage.ApplyImmediately`의 UI 직접 호출(`DamageFloater`/`Hit`)이 남아 있어 순수 헤드리스 판정
  테스트는 불가하고 씬 포함 테스트로 검증한다(2단계 선행 과제).
- Step 4는 대표 씬 1개 조합 기준이다. 새 스킬/새 씬 추가 시 같은 패턴의 회귀 추가가 필요하다.

## 판정

**완료** — Step 0~5 완료 조건 충족, P0/P1/P2 미해결 없음(P2는 Step 4로 종결). 전투 결과가
{시드, 초기 배치} 입력만으로 재현되고, 배속·프레임 수 불변이 실제 전투 씬 레벨 회귀로 고정되었다.

## Related

- [[CB-001-Deterministic-Combat-Resolution]]
- [[ADR-017-Deterministic-Combat-Resolution]]
- [[Turn-System]]
- [[Article-Status-System]]
- [[BehaviorTree-System]]
- [[STEP_REVIEW_WORKFLOW]]
