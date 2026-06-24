---
id: ADR-015
type: decision
status: accepted
date: 2026-06-18
system: DialogueTool, WorldState
---

# State Read Data Node

## Context

DialogueTool은 WorldState를 이미 두 방식으로 소비한다.

- `state_condition`: 주입된 read provider와 `ConditionSet`으로 boolean Data를 만든다.
- `state_set`/`state_add`: 별도 mutation provider로 WorldState를 변경하는 Effect다.

하지만 대화 그래프에서 단일 상태 값을 Data로 읽어 Expression, Branch, Choice 조건 등에 재사용하는 노드는 없다.
사용자는 `gold`, `affinity`, `quest state` 같은 값을 조건식이나 계산식의 입력으로 직접 쓰고 싶다.

기존 그래프 port category는 `flow`, `data`, `boolean`, `effect`다. `int`, `float`, `string` 같은 세부 typed port는
존재하지 않는다.

## Decision

### D1. State Read는 leaf Data node다

`state_read` runtime type을 갖는 leaf Data node를 추가한다. 이 노드는 Flow/Effect를 갖지 않고, 주입된 read
provider에서 하나의 key 값을 읽어 Data value로 반환한다.

### D2. 타입은 runtime expected type이며 port category가 아니다

노드는 `value_type`으로 `TYPE_BOOL`, `TYPE_INT`, `TYPE_FLOAT`, `TYPE_STRING`, `TYPE_STRING_NAME` 중 하나를 저장한다.
런타임은 `read_state(key)` 결과의 `typeof()`가 expected type과 정확히 일치할 때만 성공한다.

Editor output port는 항상 generic `data`다. BOOL도 dynamic `boolean` port로 바꾸지 않는다. 현재 editor가
`data <-> boolean` 연결을 허용하므로 Branch/Choice 조건 입력에 연결 가능하고, typed port family를 새로
도입하지 않아도 된다.

### D3. provider 주입 경계는 ADR-009와 동일하다

`DialoguePlayer`는 `/root/WorldState`를 직접 조회하지 않는다. State Read는 `_read_state_provider`를 직접
검증하고 소비한다. `DialoguePlayer.read_state()` facade로 재포장하지 않는다.

필수 provider surface:

```gdscript
has_state(key: StringName) -> bool
read_state(key: StringName) -> Variant
```

provider가 없으면 `provider_missing`, provider가 있지만 계약을 만족하지 않으면
`provider_contract_invalid`다. `has_state(key) == false`이면 `state_missing`이고 `read_state`를 호출하지 않는다.
`read_state` method missing/arity mismatch/first-arg type mismatch는 `has_state == true` 경로에서도 호출 전에
`provider_contract_invalid`로 차단한다.

### D4. 실패는 Data error-dominance로 전파한다

State Read 실패는 `{ value: null, errored: true }`를 반환한다. Branch/Choice/Expression은 DT-008에서 확정한
error-dominance 계약에 따라 실패를 false/숨김으로 fail-closed한다. Expression이 `not failed`나
`failed or true` 같은 형태로 오류를 정상 true로 뒤집으면 안 된다.

### D5. 평가 report seam을 둔다

`DialoguePlayer`는 State Read 평가마다 다음 signal을 발행한다.

```gdscript
signal state_read_evaluated(read_node_id: int, consumer_node_id: int, report: Dictionary)
```

report는 성공/실패 모두 구조화한다.

```gdscript
{
    "ok": bool,
    "key": StringName,
    "expected_type": int,
    "actual_type": int,
    "value": Variant,
    "error": StringName,
}
```

signal에는 detached deep copy를 넘긴다. 이 seam은 후속 DialogueHistory/State Inspector/trace UI가 사용할 수
있고, Data 반환값과 report 관찰을 분리한다.

sentinel 정책:

- 성공: `actual_type = typeof(value)`, `value = read_value`, `error = &""`.
- 값을 읽지 않은 실패(`provider_missing`, `provider_contract_invalid`, `key_invalid`, `state_missing` 등):
  `actual_type = TYPE_NIL`, `value = null`.
- 값을 읽은 뒤 expected type과 다른 경우: `actual_type = typeof(read_value)`, `value = read_value`,
  `error = &"actual_type_mismatch"`.
- 성공 report의 `value`는 privacy filter 없이 노출되는 Dialogue runtime 내부 debug/trace seam이다.

### D6. editor validation은 provider-free structural validation만 한다

Editor save validation은 key 문자열과 expected type 구조만 검사한다. key pattern source of truth는
`StateSchema.KEY_PATTERN` 또는 같은 helper다. 실제 schema에 key가 존재하는지는
runtime provider가 판단한다.

이유:

- DialogueTool addon은 게임별 schema 경로를 소유하지 않는다.
- debug preview는 고정 example schema를 쓰므로 production schema 존재 여부와 다를 수 있다.
- provider-free validation을 유지해야 fresh project와 테스트 fixture가 안정적이다.

### D7. 구현은 runtime과 editor를 분리한 Step으로 진행한다

Step 1은 runtime evaluator와 report contract만 구현하고, runtime snapshot을 직접 구성해 테스트한다.
Step 2에서 Definition/GraphNode/Adapter/Registry를 추가해 editor authoring을 연다.

## Alternatives Rejected

- **Branch/Expression 안에서 직접 provider를 읽기**: Data supplier 재사용성이 사라지고 consumer가 WorldState
  계약을 알게 되어 거부한다.
- **State Condition을 확장해 값도 반환**: ConditionSet은 boolean predicate와 trace에 최적화되어 있다. 단일
  value read와 책임이 다르다.
- **BOOL은 boolean port, 나머지는 data port**: 노드 설정 변경이 포트 category를 바꾸면 기존 연결의 안정성이
  흔들린다. MVP에서는 summary/type label로 충분하다.
- **int/float/string 전용 port type 추가**: 그래프 validation, 연결 호환성, 기존 노드 UI까지 퍼지는 큰 작업이다.
  DT-013의 목표를 넘는다.
- **autoload fallback read**: provider 누락이 조용히 `/root/WorldState`로 승격되면 테스트와 host 제어권이
  깨진다. ADR-009 provider 주입 경계를 유지한다.
- **editor에서 schema key 존재까지 검증**: 게임별 schema 소유권과 debug preview schema 차이 때문에 거부한다.

## Consequences

### Positive

- 조건 자산을 만들지 않고도 단일 WorldState 값을 Expression/Branch/Choice에서 재사용할 수 있다.
- provider 주입, strict typeof, fail-closed, report seam이 기존 State Condition/Mutation 설계와 일관된다.
- typed port refactor 없이 작은 단계로 구현 가능하다.

### Negative

- 그래프 위 포트 색만으로 INT/FLOAT/STRING 차이를 알 수 없다. 노드 summary/type label에 의존한다.
- schema-aware key picker가 없으므로 key 오타는 runtime `state_missing`으로 드러난다.
- `data` output이므로 잘못된 consumer 연결은 runtime type mismatch 또는 consumer 변환 정책으로 확인된다.

## Review Gate

[[DT-013-State-Read-Data-Node]] Step 0 설계 리뷰에서 다음을 확인한 뒤 accepted로 전환한다.

- 현재 `DialoguePlayer._eval_data` 구조에서 `{ value, errored }` 전파와 signal 발행 위치가 안전한지.
- provider duck-type 검증이 SCRIPT ERROR 없이 가능한지.
- `data` output 고정이 Branch/Choice boolean 입력과 실제 editor connection validation에 맞는지.
- Step 1 runtime-only 구현이 editor 노출 없이 독립 검증 가능한지.

### Resolutions (Step 0 Design Review, 2026-06-18)

판정: **Approved after design fixes**([[DT-013-State-Read-Data-Node-Review]]). P0/P1은 없고 위 D1~D7은 현재
코드 구조에서 구현 가능하다고 확인했다.

반영한 수정:

- `read_state` 계약 위반은 호출 전 차단해야 하며 Step 1 테스트에 method missing/arity mismatch/first-arg
  mismatch를 포함한다.
- report sentinel을 고정했다. 값 미읽기 실패는 `TYPE_NIL/null`, type mismatch는 실제 타입/값을 보존한다.
- editor key validation은 `StateSchema.KEY_PATTERN` 계열을 source of truth로 삼고 invalid key matrix를 테스트한다.
- key param은 String/StringName만 정규화하고 그 외 손상 Variant는 `key_invalid`로 fail-closed한다.

## Related

- [[DT-013-State-Read-Data-Node]]
- [[ADR-009-State-Condition-Dialogue-Consumption]]
- [[ADR-010-State-Mutation-Dialogue-Effects]]
- [[ADR-006-Typed-World-State]]
- [[DialogueTool]]
- [[World-State-System]]
