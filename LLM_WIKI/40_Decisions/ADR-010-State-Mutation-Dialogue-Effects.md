---
id: ADR-010
type: decision
status: accepted
date: 2026-06-15
system: DialogueTool, WorldState
---

# State Mutation Dialogue Effects

## Context

DialogueTool은 ADR-005의 비대기 Effect 연결과 ADR-009의 read provider 주입을 갖췄지만, Effect로
World State를 **변경**하는 경로가 없다. 현재 `_run_effects`는 whitelist 통과 대상에 무조건 Portrait
UI 요청만 발행하고, Dialogue runtime에는 mutation provider가 주입되지 않는다.

`WorldStateStore`는 strict typeof, JSON-safe 도메인, read-only 거부, 알림 중 재진입 차단(`ERR_BUSY`),
atomic `apply_batch(... ) -> {applied, diff:[{key,old,new}], errors}`를 이미 강제한다. 다만 절대값
변경(set/batch)만 있고 원자적 `Add`가 없다. `Add`를 DialoguePlayer에서 read→calculate→set으로
구현하면 read/mutation provider가 어긋나거나 연산 원자성이 깨진다.

이 ADR은 [[DT-009-State-Mutation-Dialogue-Effects]] Step 0 설계 리뷰의 코드 대조 결과로,
`State Set`/`State Add` Effect와 mutation provider 경계를 구현 가능한 수준으로 확정한다.

## Decision

게임 상태를 바꾸는 두 leaf Effect 노드(`state_set`, `state_add`)를 ADR-005 비대기 Effect 모델에
추가하고, read provider와 분리된 mutation provider를 통해서만 실행한다. 아래 D1~D10으로 확정한다.

### D1. Provider 주입 API — 세 번째 선택 인자

```gdscript
DialogueManager.play(resource, read_state_provider = null, mutation_state_provider = null)
DialogueUI.play(resource, read_state_provider = null, mutation_state_provider = null)
DialoguePlayer.set_mutation_state_provider(provider)   # 별도 _mutation_state_provider 필드
```

기존 API와 호환되는 세 번째 선택 인자를 쓴다. context Resource/Dictionary는 도입하지 않는다(인자가
더 늘면 후속에서 전환). read provider와 별도 필드로 보관한다.

### D2. read provider의 mutation 권한 자동 승격 금지

mutation provider가 생략됐다고 read provider를 mutation으로 승격하지 않는다. mutation provider가
없으면 `state_*` Effect는 `provider_missing` report를 남기고 **Flow는 계속**한다. 읽기 전용 대화가
암시적으로 쓰기 권한을 얻지 못한다.

### D3. 원자적 Add는 Store 소유 보고형 API

`WorldStateStore`에 단일 메서드를 추가한다(일반 operation DSL은 후속 Task).

```gdscript
add_state(key: StringName, delta: Variant) -> Dictionary
```

- 현재 값 read, strict numeric type 확인, overflow/domain 확인, commit을 **Store 내부에서 원자적으로**
  수행한다. DialoguePlayer의 read→calculate→set 방식은 금지한다.
- INT state에는 INT delta, FLOAT state에는 FLOAT delta만 허용한다. int↔float 암시 변환은 없다.
- 미등록/read-only/non-numeric/type mismatch/out-of-domain/not-ready/busy는 **값/시그널 불변**으로
  실패한다.
- 반환은 `Error`가 아니라 report Dictionary다(D4·D10과 일치). 실제로 값이 바뀐 경우에만 기존
  `value_changed` 계약대로 1회 발행한다(delta=0 등 same-value는 무발행).

### D4. authoritative old/new는 mutation provider가 반환한다

report의 old/new를 read provider 사후 read로 만들지 않는다(서로 다른 provider 조합에서 거짓 report
방지, Design Risk 4). provider가 변경 결과를 직접 돌려준다.

- **Set:** 신규 Store API 없이 기존 `apply_state_batch([{key, value}])`를 재사용한다. 값이 바뀌면
  `diff`가 `{old, new}`를 주고, same-value면 `applied==true && diff 비어 있음`이 곧
  `old == new == value`를 authoritatively 보장한다(Store가 stored==value임을 보장하므로 사후 read 불필요).
- **Add:** `add_state` report가 `{old, new}`를 준다.
- 따라서 DialoguePlayer가 소비하는 mutation provider 계약 표면은 `{ apply_state_batch, add_state }`다.

### D5. Effect 실패 시 Flow 계속

비대기 Effect 의미(ADR-005 guardrail)를 유지한다. mutation 실패는 해당 Effect만 fail-closed(값 불변)
하고 주 Flow는 진행한다. provider 누락/계약 위반/Store 오류를 SCRIPT ERROR로 만들지 않고 구조화
report로 변환해 `state_mutation_evaluated`로 노출한다.

### D6. 여러 Effect는 저장 순서의 독립 transaction

같은 실행 지점의 Effect는 `runtime_connections` 저장 순서대로 실행한다. 각 Effect는 독립 transaction
이며 앞 Effect 성공 뒤 뒤 Effect가 실패해도 앞 변경을 rollback하지 않는다. all-or-nothing 그래프
mutation은 별도 Task다. mutation은 주 Flow 이동 전에 동기 완료되므로 다음 Branch/Choice가 새 값을 읽는다.

#### D6a. Choice 항목별 Effect와 공통 Effect (Step 3b)

Choice의 Effect는 **항목별(per-choice)** 또는 **공통(shared)** 두 의미를 가진다.

- **항목별:** 에디터의 항목별 Effect 출력 포트로 연결한 Effect는 연결에 `choice_index`(항목 index)를
  보존한다. 그 선택지를 고를 때만(`select_choice` → `original_port == choice_index`) 실행된다. 이로써
  "선택 결과에 따라 다른 상태 변경"이라는 핵심 User Outcome을 만족한다. 단일 공유 포트만으로는 어떤
  선택지를 골라도 같은 mutation이 실행돼 부족하다(Step 3 리뷰 P1 판정).
- **공통:** `choice_index`가 없는 Effect 연결(수작업/레거시 리소스 또는 의도된 공유)은 어느 선택지에서도
  실행된다. `get_runtime_effect_node_ids(from, choice_index)`가 `ci == choice_index || ci < 0`으로 둘을
  함께 반환한다(저장 순서 유지). 비-Choice 노드(Start/Say)는 `choice_index = -1`로 전체 Effect를 실행한다.

**전용 공통 포트.** 에디터 Choice 노드는 항목별 effect 출력 포트(`n..2n-1`) 뒤에 **공통 effect 출력
포트(`2n`)** 를 둔다. 공통 연결을 항목0 포트(첫 effect 포트)에 싣지 않는다 — 그러면 recapture 시
`choice_index=0`이 붙어 공통이 "첫 선택지 전용"으로 오염되기 때문이다(Step 3b 리뷰 P1). capture는 항목별
포트에만 `choice_index`를 기록하고 공통 포트에는 기록하지 않는다. load는 `choice_index` 없는 연결을 공통
포트로, 유효한 `choice_index`를 항목 포트로 정규화하며, **잘못된 타입/범위의 `choice_index`는 첫 포트로
fallback하지 않고 오류 후 연결을 건너뛴다**(손상 .tres가 조용히 항목0으로 바뀌지 않게 함).

항목별·공통 Effect 모두 D6의 독립 transaction·저장 순서·Flow 이동 전 동기 완료 규칙을 그대로 따른다.
Choice resize 시 남은 항목의 항목별 연결과 공통 연결을 포트 remap으로 보존하고 삭제 항목 연결만 제거한다.

**`choice_index` 계약(런타임·에디터 동일).** `get_runtime_effect_node_ids`는 `has("choice_index")`로 필드
부재와 명시적 값을 구분한다(typed int 대입을 피해 손상 snapshot의 SCRIPT ERROR도 방지):

- **필드 없음** → 공통(shared): 어느 선택지에서도 실행.
- **유효한 `int`** → 해당 항목(`== choice_index`) 또는 명시적 공통(`< 0`).
- **필드는 있으나 `null`/String/Dictionary 등** → fail-closed로 건너뜀(실행 안 함).

명시적 `null`을 공통으로 취급하지 않는다 — 에디터 load도 명시적 `null`을 거부하므로 런타임/에디터 계약이
일치한다(직접 실행/수작업 snapshot에서도 동일 동작).

### D7. Set은 5타입, Add는 INT/FLOAT strict

- `State Set`: bool/int/float/String/StringName 전체(Store 허용 5타입). literal의 `typeof()`를
  capture→save→cache-ignore reload→runtime snapshot에서 보존한다.
- `State Add`: INT 또는 FLOAT만, 같은 타입의 delta. int↔float 암시 변환 없음. 에디터 UI는 INT/FLOAT
  외 타입을 만들 수 없거나 저장 validation에서 명시적으로 거부한다.

### D8. 오류 정책 표

| 상황 | 결과 코드 | Flow |
| --- | --- | --- |
| read-only key (gameplay set/add) | `read_only`(set은 `ERR_UNAUTHORIZED`) | 계속 |
| store not-ready | `store_not_ready` | 계속 |
| 알림 중(`_in_notification`) | `store_busy`(`ERR_BUSY`) | 계속 |
| 미등록 key | `unknown_key` | 계속 |
| 타입 불일치 | `type_mismatch` | 계속 |
| 도메인/overflow 위반 | `out_of_domain` | 계속 |
| mutation provider 누락/계약 위반 | `provider_missing` / `provider_contract_invalid` | 계속 |

`ERR_BUSY`를 우회하거나 자동 재시도하지 않는다. 모든 실패는 구조화 report 1회로 관찰된다.

### D9. latest-wins 번들 캡처와 폐기 provider mutation 차단

`DialogueUI._pending_start`에 mutation provider를 추가해 resource + read provider + mutation provider를
**한 묶음**으로 deferred 캡처한다. 같은 프레임 연속 `play()`는 마지막 번들만 시작한다(latest-wins).
폐기 UI는 `cancel_pending_start`가 번들 전체를 비우므로 폐기된 provider가 mutation하지 않는다.

직접 mutation 경로는 Portrait의 `ui_request` source-guard를 통과하지 않는다. 진행 중인 동기 effect
체인은 자기 자신이 캡처한 mutation provider(=자기 store)만 변경하므로 "잘못된 store"가 아니다.

### D10. report seam과 재진입/변조 방어

```gdscript
signal state_mutation_evaluated(effect_node_id: int, report: Dictionary)
# report: { applied: bool, operation: "set"|"add", key: StringName,
#           old_value: Variant, new_value: Variant, error: StringName }
```

- 성공/실패 모두 실행 1회당 정확히 1회 발행한다.
- mutation **commit 후** 발행하고, authoritative 결과를 capture-before-emit한 뒤 signal에는
  `report.duplicate(true)` detached deep copy를 넘긴다(DT-008 `condition_evaluated` 패턴 재사용).
- signal listener가 report나 실행 상태를 변조하거나, listener에서 `play()`를 재진입 호출해도 이미
  commit된 mutation 결과와 이후 독립 Effect를 바꾸지 못한다.

### 런타임 디스패치(필수 구현 제약)

`is_effect_target_type`/`EFFECT_TARGET_TYPES`는 런타임 `_run_effects`와 에디터 validation이 공유한다.
`state_set`/`state_add`를 whitelist에만 추가하면 `_run_effects`가 state 노드에 garbage Portrait 요청을
발행한다(저장은 되나 실행은 틀림 — Design Risk 7). 따라서 Step 2에서 `_run_effects` 루프에 **타입
디스패치**를 도입해 `portrait_*`는 `_build_portrait_request`로, `state_set`/`state_add`는 mutation
provider 호출 + report signal로 분기한다. whitelist·런타임 dispatch·에디터 validation 세 곳을 같은
Step에서 함께 갱신한다.

### Add 오버플로 안전성

INT 피연산자는 모두 `±(2^53-1)` 안이므로 합은 최대 `±(2^54-2)` < int64로 int64 자체 오버플로는
불가능하다. 결과를 int64로 계산한 뒤 `_value_in_domain`(JSON-safe)만 검사하면 충분하며, 경계 초과는
wrap이 아니라 명시적 `out_of_domain` 실패로 처리한다.

## Alternatives Rejected

- **`add_state(key, delta) -> Error`:** report old/new를 만들 수 없어 read provider 사후 read가
  필요해지고, read≠mutation provider일 때 거짓 report가 된다(D4로 거부).
- **Player에서 read→calc→set Add:** 비원자적이고 provider 불일치를 만든다(Design Risk 1).
- **Set 전용 신규 보고 Store API:** 기존 `apply_state_batch`가 authoritative diff와 same-value 보장을
  이미 제공하므로 불필요한 API 추가다.
- **read provider 자동 승격:** 읽기 전용 대화가 쓰기 권한을 얻는다(D2로 거부).
- **여러 Effect를 source-node 단위 all-or-nothing batch:** 그래프 문법과 rollback 의미가 커진다.
  독립 transaction으로 두고 별도 Task로 미룬다(D6).
- **whitelist만 추가:** 런타임 디스패치 없이는 실행이 틀린다(필수 디스패치 제약으로 거부).

## Consequences

### Positive

- 선택 결과/대사 진행이 타입 안전하게 상태를 바꾸고 다음 조건 분기/선택지가 즉시 반영한다.
- Add 원자성과 authoritative report가 Store 내부에 있어 provider 조합과 무관하게 정확하다.
- 기존 Portrait Effect, Store batch, Condition 소비 계약을 변경 없이 재사용한다.
- mutation 결과/실패가 listener 변조에 안전한 report seam으로 관찰된다(후속 DialogueHistory/Inspector seam).

### Negative

- `_run_effects`가 단일 Portrait 발행에서 타입 디스패치로 복잡해진다.
- mutation 계약 표면이 `{apply_state_batch, add_state}` 두 메서드로 늘어 duck-type 검증이 추가된다.
- typed literal `.tres` 왕복 보존을 Step 3에서 별도로 확인해야 한다.

## Related

- [[DT-009-State-Mutation-Dialogue-Effects]]
- [[ADR-005-Nonblocking-Effect-Connections]]
- [[ADR-006-Typed-World-State]]
- [[ADR-009-State-Condition-Dialogue-Consumption]]
- [[DialogueTool]]
- [[World-State-System]]
