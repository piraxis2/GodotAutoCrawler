---
type: guide
system: DialogueTool
status: active
updated: 2026-06-16
---

# DialogueTool 상세 사용 설명서

## 1. 개요

DialogueTool은 Godot 에디터 안에서 노드를 연결해 대화를 작성하고, `.tres` 리소스로 저장한 뒤
게임에서 실행하는 그래프 기반 대화 도구다.

현재 지원하는 주요 기능은 다음과 같다.

- 화자와 대사 표시
- 줄바꿈 단위의 대사 누적 공개
- 선택지와 선택 결과별 Flow 분기
- Variable/Expression을 이용한 조건 분기
- 좌/중앙/우 Portrait 표시, 변경, 숨김
- Portrait Effect 여러 개를 주 대화 Flow와 함께 실행
- 대화 반복 실행, 실행 중 교체, 종료 callback 재진입
- 실행 중인 노드의 에디터 하이라이트
- GDScript/C#에서 사용할 수 있는 전역 `DialogueManager` API

DialogueTool의 실행 모델은 **주 Flow 하나와 비대기 Effect 여러 개**다. Say나 Choice 같은 대기
노드를 진짜 병렬로 실행하지 않는다.

```text
현재 노드
  -> 연결된 Effect를 저장 순서대로 실행
  -> 주 Flow 하나로 이동
  -> Say 또는 Choice에서 사용자 입력 대기
```

## 2. 플러그인 활성화와 화면 열기

플러그인 설정 파일은 `addons/dialogtool/plugin.cfg`다.

1. Godot에서 `Project > Project Settings > Plugins`를 연다.
2. `DialogueTool`을 활성화한다.
3. 에디터 상단의 `Dialogue` 메인 화면을 연다.

플러그인이 활성화되면 `DialogueToolUtil` 오토로드가 등록된다. 게임 실행 API인
`DialogueManager`도 프로젝트 오토로드에 등록되어 있어야 한다.

Dialogue 화면의 주요 영역은 다음과 같다.

- 노드 목록: 생성 가능한 Definition 목록
- GraphEdit: 노드 배치와 연결
- File 메뉴: Save, Load, New
- Edit 메뉴: 선택 노드 삭제, 저장 상태로 초기화
- 실행 버튼: 선택한 Scene과 Dialogue 리소스를 디버그 모드로 실행

## 3. 새 대화 만들기

1. `Dialogue` 화면을 연다.
2. 새 그래프에는 Start 노드가 자동 생성된다.
3. 왼쪽 노드 목록에서 노드를 GraphEdit으로 드래그한다.
4. 흰색 Flow 포트를 연결한다.
5. 필요한 경우 값 포트와 주황색 Effect 포트를 연결한다.
6. `File > Save`로 `.tres` 파일을 저장한다.

최소 대화는 다음 형태다.

```text
Start -> Say -> End
```

저장된 리소스는 에디터 복원 데이터와 런타임 스냅샷을 함께 가진다.

- `nodes`, `connections`: GraphEdit 재구성용
- `runtime_nodes`, `runtime_connections`: 게임 실행용

## 4. 포트 종류

### Flow 포트

- 색상: 흰색
- 역할: 실행 커서가 이동하는 주 대화 경로
- 한 출력 포트에는 주 Flow 대상을 하나만 연결할 수 있다.
- 두 개 이상 연결하면 저장 validation이 실패한다.

### Value 포트

- 종류: `data`, `boolean`
- 역할: Branch나 Expression에 값을 제공
- `data`와 `boolean`은 값 포트 카테고리 안에서 서로 연결할 수 있다.

### Effect 포트

- 색상: 주황색
- 역할: 실행 커서를 이동시키지 않는 비대기 명령
- 현재 Effect 출력은 Start, Say, Choice(항목별 + 공통)에 있다.
- 현재 Effect 입력은 Portrait Show/Hide/Expression, StateSet, StateAdd에 있다.
- 하나의 Effect 출력에 여러 Effect 대상을 연결할 수 있다.
- 연결 저장 순서가 Effect 실행 순서다(Choice 항목별 Effect는 §14, 상태 변경 Effect는 §14 참고).

## 5. Flow 노드

### Start

대화 시작점이다. 그래프에 정확히 하나만 존재해야 하며 삭제할 수 없다.

- 흰색 출력 port 0: 주 Flow
- 주황색 출력 port 1: Portrait Effect

```text
Start.flow -> Say
Start.effect -> PortraitShow(left)
```

### Say

화자와 대사를 표시하고 사용자 입력을 기다린다.

주요 필드:

- `speaker`: 화자 이름
- `say_text`: 대사 본문
- `portrait`: 이전 리소스 호환용 문자열 필드

Say의 기존 `portrait` 필드는 유지되지만 새 Portrait 상태 시스템으로 자동 변환되지는 않는다.
새 대화에서는 별도 Portrait 노드를 사용하는 것이 권장된다.

Say의 흰색 출력은 다음 Flow, 주황색 출력은 Say가 끝날 때 실행할 Portrait Effect다.

#### 줄바꿈 처리

Say 텍스트에 줄바꿈이 있으면 같은 대화창에서 이전 줄을 유지하며 한 줄씩 누적 공개한다.

```text
첫 번째 줄
두 번째 줄
세 번째 줄
```

클릭 순서:

1. 현재 타이핑 중인 줄을 즉시 완성
2. 다음 줄을 기존 줄 아래에 추가하고 타이핑
3. 마지막 줄까지 공개한 다음 클릭에서 다음 Flow로 진행

빈 줄도 페이지 하나로 취급한다. CRLF와 CR 줄바꿈은 LF로 정규화한다.

### Choice

선택지 버튼을 표시하고 선택된 인덱스에 해당하는 출력 Flow로 이동한다.

- 선택지 순서와 출력 포트 인덱스가 대응한다.
- 첫 번째 선택지는 출력 port 0, 두 번째 선택지는 port 1이다.
- 선택지가 없으면 경고 후 대화를 종료한다.

선택지 텍스트를 추가하거나 제거한 뒤 저장하면 포트 구성도 함께 저장된다.

**조건부 선택지(DT-008).** 각 선택지에는 항목별 Data 입력 포트가 있다(항목 i의 Data 입력 = port i+1,
flow 출력 = port i). 여기에 State Condition / Variable / Expression boolean 값을 연결하면, Choice 진입 시
한 번 평가해 `true`인 선택지만 표시한다. Data 입력이 없는 선택지는 항상 표시되어 기존 대화와 호환된다.
중간 선택지가 숨겨져도 사용자가 고른 선택지는 원래 Flow로 정확히 진행한다. 조건이 오류(미등록 key/타입
불일치/provider 미지정 등)면 해당 선택지는 숨겨진다(fail-closed). 모든 선택지가 숨겨지면 경고 후 종료한다.
대기 중 상태가 바뀌어도 현재 목록은 고정되고, 같은 Choice에 다시 진입할 때 재평가한다.

### Branch

입력된 Data 값을 bool로 변환해 두 Flow 중 하나로 이동한다.

- Data 입력 port 0: 조건값
- Flow 출력 port 0: `true`
- Flow 출력 port 1: `false`

값 변환 규칙:

- `null`: false
- bool: 원래 값
- 숫자: 0이면 false, 나머지는 true
- 문자열: 빈 문자열이면 false, 나머지는 true
- 그 외 타입: 경고 후 false

### End

대화를 종료한다. 종료 시 DialogueUI의 대사, 선택지, Portrait 상태가 정리되고
`DialogueManager.dialogue_end`가 발행된다.

## 6. Data 노드

### Variable

Branch 또는 Expression에 정적 값을 제공한다.

지원 타입:

- Nil
- Bool
- Int
- Float
- String
- Vector2
- Vector3
- Color
- Random

Random은 최소값과 최대값을 저장하고 런타임 실행 시 해당 범위의 정수를 생성한다.

### Expression

연결된 Data 입력을 변수로 받아 Godot `Expression`으로 평가한다.

```text
Variable(A) ----\
                 Expression("A > B") -> Branch
Variable(B) ----/
```

Expression의 입력 키 순서와 입력 포트 순서가 대응한다. 식 파싱 또는 실행이 실패하면 경고 후
`null`을 반환하며, Branch에서는 false로 처리된다. 입력 중 하나라도 조건 오류(아래 State Condition)면
식 결과도 오류로 전파되어 Branch false / Choice 숨김으로 fail-closed된다(`not c` 같은 식이 오류 조건을
true로 뒤집지 못한다).

### State Condition (DT-008)

World State `ConditionSet` 하나를 평가해 boolean Data를 제공한다. 노드에 ConditionSet `.tres`를 드롭해
지정하고, boolean output 포트를 Branch의 조건 입력이나 Choice 항목별 Data 입력에 연결한다.

```text
State Condition("quest.main.stage >= 3") -> Branch (true/false Flow)
State Condition("actor.example.affinity >= 10") -> Choice 항목 i Data 입력(port i+1)
```

- 평가는 게임 코드가 주입한 read 상태 provider(예: `WorldStateStore`)로만 수행한다(`/root` 직접 조회 없음).
- ConditionSet이 비었거나 invalid이거나 provider가 없거나 key 누락/타입 불일치면 조용히 true가 되지 않고
  fail-closed된다(Branch false / Choice 숨김). 평가 `report`는 디버거/후속 inspector가 쓸 수 있는
  `condition_evaluated` signal로 노출된다.
- ConditionSet 작성법(leaf/group/ALL·ANY·NOT, operator, 타입 규칙)은 [[World-State-User-Guide]]를 따른다.

#### 그래프 위 조건 요약 표시 (DT-012)

State Condition 노드는 picker의 `.tres` 경로 아래에 **사람이 읽을 수 있는 조건 요약 label**을 보여 준다.
리소스를 따로 열지 않아도 노드만 보고 조건 의미를 알 수 있다.

```text
State Condition
  res://.../affinity_ge_10.tres        <- picker(참조 path 유지)
  actor.example.affinity >= 10         <- summary label(자동 요약)
```

- **자동 요약**: leaf는 `key 기호 literal`(예: `actor.example.affinity >= 10`), group은 `ALL(...)` /
  `ANY(...)` / `NOT(...)`로 표시한다. 표시용 operator 기호(`==`,`!=`,`<`,`<=`,`>`,`>=`)는 평가 trace
  문자열(`greater_equal` 등)과 별개다.
- **literal 표기 구분**: INT `10`과 FLOAT `10.0`, String `"calm"`과 StringName `&"calm"`, bool
  `true`/`false`를 구분해 보여 준다. 문자열 안의 따옴표·줄바꿈은 escape되어 한 줄로 안전하게 표시된다.
- **description 우선**: `ConditionSet.description`을 쓰면 **structural valid일 때만** 그 설명이 요약으로
  우선 표시되고, 자동 구조 요약은 tooltip에 함께 보인다.
- **invalid/null은 항상 구분**: ConditionSet이 없으면 `No ConditionSet`, 구조가 깨졌으면
  `Invalid: <코드>`(예: `root_null`, `cycle_detected`, `group_empty`)를 빨강 계열로 표시한다.
  description이 있어도 invalid/null을 가리지 않는다.
- **tooltip**: 잘리지 않은 full 요약, 외부 `.tres` 경로, invalid면 오류 코드/메시지를 tooltip으로 확인한다.
  긴 요약은 label에서 잘려 노드 폭이 과도하게 커지지 않는다.
- **갱신 시점**: 요약은 ConditionSet 드롭/clear/그래프 load·재로드 시 갱신된다. 외부 `.tres`나
  description을 Inspector에서 바꾼 뒤의 즉시 갱신은 범위 밖이며, 노드를 다시 적용하거나 그래프를 재로드하면
  반영된다. inline ConditionSet tree editor와 schema-aware key picker는 후속 작업이다.

### State Read (DT-013)

World State 단일 key 값을 그대로 Data로 읽어 Branch/Choice 조건과 Expression 입력에 공급한다. 노드 목록에서
**WorldStateRead**로 보인다(설계상 개념 이름은 "State Read"). 조건만 내는 State Condition과 달리 값 자체를
재사용할 수 있다.

노드 필드:

- **key**: 읽을 World State key(예: `player.gold`, `quest.main.stage`). 형식은 `StateSchema`의 key 규칙
  (`소문자.점.경로`, 최소 두 segment)을 따른다.
- **type**: 기대 타입 OptionButton(`BOOL`/`INT`/`FLOAT`/`STRING`/`STRING_NAME`). 런타임에서 읽은 값의 타입이
  이 타입과 정확히 일치할 때만 성공한다.
- summary label은 `<key> : <TYPE>`(예: `player.gold : INT`), key가 없으면 `No State Key`(빨강)로 표시된다.

```text
State Read("quest.main.stage", INT) -> Expression("x > 5") -> Branch (true/false Flow)
State Read("session.intro.seen", BOOL) -> Branch / Choice 항목 i Data 입력(port i+1)
```

- output 포트는 generic **Value(data)** 하나다. BOOL이어도 boolean 포트로 바뀌지 않으며, Value↔Boolean 호환으로
  Branch/Choice의 boolean 조건 입력에 연결된다.
- 평가는 게임 코드가 주입한 read 상태 provider(예: `WorldStateStore`)로만 수행한다(`/root` 직접 조회 없음).
  State Read는 값을 **읽기만** 하며 상태를 바꾸지 않는다.
- 다음은 모두 조용히 true/0이 되지 않고 fail-closed된다(Branch false / Choice 항목 숨김 / Expression 오류 전파):
  provider 미주입, key 형식이 손상된 snapshot, schema에 없는 key(`state_missing`), 읽은 값 타입이 type과 다름
  (`actual_type_mismatch` — `int`와 `float`, `String`과 `StringName`을 섞지 않는다).
- 평가 결과는 `state_read_evaluated(read_node_id, consumer_node_id, report)` signal로 노출된다(디버거/후속
  inspector용).
- **저장 validation**: key가 비었거나 형식이 틀리거나 type이 허용 5타입 밖이면 저장이 중단된다. schema에 key가
  실제로 있는지는 저장 시 검사하지 않고 런타임 provider가 판정한다(에디터는 게임 schema를 모름).

## 7. Portrait 노드

Portrait는 Say와 독립된 상태 명령이다. DialoguePlayer는 요청만 발행하고, 실제 상태와 Texture 렌더링은
DialogueUI가 담당한다.

지원 슬롯:

- `left`
- `center`
- `right`

잘못된 슬롯 값은 런타임에서 경고 후 `center`로 처리한다. 에디터에서 기존의 알 수 없는 슬롯 값을
불러온 경우 사용자가 직접 변경하기 전까지 원본 값을 보존한다.

### 공통 필드

- `slot`: 표시 위치
- `texture_path`: Texture2D의 `res://` 경로
- `actor`: 향후 actor resolver를 위한 메타데이터
- `expression`: 표정 메타데이터
- `transition`: 전환 메타데이터, 기본값 `none`

현재 `actor`, `expression`, `transition`은 일부 상태 데이터로 보존되지만 actor database나 transition
애니메이션을 자동 실행하지 않는다.

### Texture 지정

Portrait Show와 Expression의 `texture_path`에는 다음 두 방식을 사용할 수 있다.

1. `res://...` 경로 직접 입력
2. FileSystem Dock의 Texture2D 파일을 필드로 드래그 앤 드롭

단일 Texture2D 리소스만 드롭할 수 있다. 경로가 비었거나 리소스를 찾지 못해도 대화 Flow는 중단되지
않으며, 경고 후 Texture 없이 상태 요청을 처리한다.

### PortraitShow

지정 슬롯의 상태를 새 값으로 갱신하고 Texture를 표시한다.

### PortraitExpression

기존 슬롯 상태를 기준으로 비어 있지 않은 필드만 변경한다.

- 빈 `texture_path`: 기존 Texture 유지
- 새 `texture_path`: Texture 교체
- 빈 actor/expression: 기존 값 유지
- Show 이전에도 안전하게 새 슬롯 상태를 만들 수 있음

### PortraitHide

지정 슬롯만 숨기고 해당 슬롯 상태를 제거한다. 다른 슬롯에는 영향을 주지 않는다.

## 8. Portrait Effect 연결

Portrait 노드는 기존처럼 직렬 Flow로 사용할 수 있다.

```text
Start -> PortraitShow -> Say
```

여러 Portrait를 같은 시점에 처리하려면 Effect 포트를 사용한다.

```text
Start.effect -> PortraitShow(left)
Start.effect -> PortraitShow(right)
Start.flow   -> Say
```

실행 순서:

```text
PortraitShow(left)
PortraitShow(right)
Say
```

Say가 끝난 시점에 Portrait를 바꾸는 예:

```text
Say.effect -> PortraitExpression(left)
Say.effect -> PortraitHide(right)
Say.flow   -> Choice
```

Effect는 스레드나 coroutine 기반 병렬 실행이 아니다. 연결된 비대기 요청을 순서대로 모두 적용한 뒤
주 Flow 하나를 실행한다.

## 9. 저장 Validation

저장 시 다음 조건을 검사한다. 치명적인 오류가 있으면 `.tres` 저장을 중단한다.

- Start 노드가 정확히 하나인가
- 연결 양 끝의 노드가 존재하는가
- Flow, Value, Effect 포트 카테고리가 일치하는가
- 한 Flow 출력에 주 Flow 대상이 둘 이상 연결되지 않았는가
- Effect 대상이 Portrait Show/Hide/Expression인가
- Effect 연결에 순환이 없는가

오류 메시지에는 문제 연결의 node ID, runtime type, output port, input port가 표시된다.

다음 항목은 warning이며 저장을 막지 않는다.

- Start에서 나가는 Flow가 없음
- 도달할 수 없는 Flow 노드
- 런타임의 잘못된 Portrait 슬롯 또는 Texture 경로

## 10. 리소스 저장과 불러오기

### 저장

- `File > Save`
- 이미 경로가 있는 그래프에서는 `Ctrl+S`

저장 확장자는 `.tres`다. 저장 직전에 현재 GraphEdit 상태를 캡처하고 runtime snapshot을 생성한 뒤
validation을 수행한다.

### 불러오기

- `File > Load`에서 `.tres` 선택
- FileSystem의 `.tres`를 GraphEdit으로 드롭

이전 Effect 리소스의 `kind: "effect"`가 구형 port 0에 저장되어 있어도 로드 시 현재 Effect 포트로
정규화한다. Effect 포트를 찾을 수 없으면 Flow로 조용히 변환하지 않고 오류를 출력한 뒤 연결을 제외한다.

### 새 그래프

`File > New`는 현재 그래프를 초기화하고 Start만 남긴다.

## 11. 게임에서 실행하기

### GDScript

```gdscript
@export var dialogue_resource: DialogueGraphResource

func start_dialogue() -> void:
    if dialogue_resource == null:
        return
    DialogueManager.dialogue_end.connect(_on_dialogue_end, CONNECT_ONE_SHOT)
    DialogueManager.play(dialogue_resource)

func _on_dialogue_end() -> void:
    print("dialogue finished")
```

### C#

```csharp
public partial class DialogueEvent : Node
{
    [Export] public Resource DialogueResource { get; set; }

    public void StartDialogue()
    {
        var manager = GetNode("/root/DialogueManager");
        manager.Connect(
            "dialogue_end",
            Callable.From(OnDialogueEnd),
            (uint)GodotObject.ConnectFlags.OneShot
        );
        manager.Call("play", DialogueResource);
    }

    private void OnDialogueEnd()
    {
        GD.Print("dialogue finished");
    }
}
```

### DialogueManager Signal

- `dialogue_started`: 대화 시작
- `dialogue_end`: 정상 종료 후 UI 정리 완료
- `ui_request(request_data)`: 표시 요청 중계

`DialogueManager.play()` 호출 시 진행 중인 기존 대화 UI는 교체된다. 이전 UI에서 늦게 도착한
`ui_request`와 `dialogue_end`는 source guard로 무시한다. Effect callback 안에서 즉시 새 대화를
시작해도 이전 Player의 후속 요청이 새 대화로 전달되지 않는다.

## 12. UI 요청 계약

### 대사

```gdscript
{
    "type": "display_text",
    "speaker": "Elizabeth",
    "say": "Hello",
    "portrait": "empty"
}
```

### 선택지

```gdscript
{
    "type": "offer_choice",
    "choices": ["Yes", "No"]
}
```

### Portrait 상태

```gdscript
{
    "type": "portrait_state",
    "action": "show", # show | hide | expression
    "slot": "left",   # left | center | right
    "texture_path": "res://Assets/Textures/Portraits/example.png",
    "actor": "",
    "expression": "happy",
    "transition": "none"
}
```

게임 코드가 별도 연출을 추가해야 할 때만 `DialogueManager.ui_request`를 구독한다. 기본 대사,
선택지, Portrait 처리는 DialogueUI가 이미 수행한다.

## 13. 디버그 실행과 노드 하이라이트

Dialogue 화면의 실행 기능은 선택한 Scene과 저장된 Dialogue 리소스를 별도 Godot 프로세스로 실행한다.

필수 조건:

- 실행할 Scene 경로가 설정되어 있어야 한다.
- Dialogue 리소스가 먼저 저장되어 있어야 한다.

디버그 실행 중 DialoguePlayer가 `current_node_changed`를 발행하고 원격 디버거가 해당 node ID를
에디터에 전달한다. GraphEdit은 현재 실행 노드를 강조하고 대화 종료 시 강조를 해제한다.

### 에디터 Play로 WorldState 미리보기 테스트하기 (DT-010)

State Condition / StateSet / StateAdd가 들어간 대화를 에디터 Play로 바로 확인할 수 있다. 게임 코드처럼
`DialogueManager.play(dialogue, WorldState, WorldState)`로 provider를 직접 넘기지 않아도 된다.

동작:

- debug Play 서브프로세스의 DialoguePlayer가 addon 동봉 example schema
  (`addons/dialogtool/examples/world_state_schema_example.tres`)로 **preview 전용 `WorldStateStore`**를
  구성해 read·mutation provider 양쪽으로 자동 주입한다.
- 따라서 example schema의 key(예: `actor.example.affinity` INT, `quest.main.stage` INT 등)를 사용하는
  대화는 Play에서 condition 분기와 state 변경이 실제로 동작한다.
- 동봉 sample `examples/sample_dialogues/sample_world_state_dialogue.tres`를 Play하면
  `Take -> StateAdd(actor.example.affinity, +50) -> Branch(affinity >= 10) -> Rich`,
  `Leave -> 변경 없음 -> Poor`를 재현한다.

lifecycle:

- Play마다 별도 Godot 프로세스가 뜨므로 preview 상태는 매번 example schema default에서 시작한다(결정론적).
- 한 번의 Play 안에서 일어난 mutation은 누적되어 이후 Branch/Condition이 변경값을 읽는다.
- preview store는 게임 `/root/WorldState`(있다면)와 별도 인스턴스라 실제 save 상태를 건드리지 않는다.

진단 로그:

- example schema load 실패 / 형식 오류 / 초기화 실패 시 명확한 `push_error`를 남기고 provider를 주입하지
  않는다. 이 경우 기존 fail-closed 계약을 유지한다(condition은 false, mutation은 `provider_missing`).
  자동으로 true가 되거나 자동으로 mutation이 성공 처리되는 일은 없다.
- 이 로그는 `--remote-debug`를 통해 에디터 Output/Debugger 패널로 전달된다.

**고정 example schema 한계(중요).** preview store는 **고정된 example schema**만 사용한다. 사용자가 자기
게임 schema의 key로 작성한 대화는 preview store에 그 key가 없으므로:

- State Condition은 `state_missing`으로 **fail-closed(false)** 된다.
- StateSet/StateAdd는 `unknown_key` report로 변경되지 않는다.

게임 schema key를 에디터 Play로 미리보기하려면 현재는 그 key를 example schema에 추가해야 한다. 게임 schema
경로를 debug 설정으로 직접 주입하는 옵션(옵션 C)은 parse-safe하게 구현 가능하지만(autoload는
`get_node_or_null` 런타임 lookup) 범위 초과로 **후속 작업**으로 미뤘다([[DT-010-Dialogue-Debug-WorldState-Preview]],
[[ADR-012-Dialogue-Debug-Preview-Provider]] D1).

## 14. State Condition과 State Effect

### 상태로 Branch/Choice 제어하기

State Condition 노드를 만들고 `ConditionSet` `.tres`를 지정한다. boolean output을 Branch 조건 입력 또는
Choice 항목별 Data 입력에 연결하면, 주입된 read provider로 조건을 평가한다.

게임 실행 시에는 read provider를 넘긴다.

```gdscript
var dialogue := load("res://Dialogue/example.tres") as DialogueGraphResource
DialogueManager.play(dialogue, WorldState)
```

### 상태 변경 Effect 사용하기

StateSet/StateAdd 노드는 비대기 Effect다. Start/Say의 주황색 Effect 출력, 또는 Choice의 항목별/공통 Effect
출력에서 StateSet/StateAdd의 Effect 입력으로 연결한다.

- StateSet: key, type, value를 지정한다. bool/int/float/String/StringName을 지원한다.
- StateAdd: key, type, delta를 지정한다. INT/FLOAT만 지원하고 암시적 int↔float 변환은 없다.
- 잘못된 literal은 저장 validation에서 차단되며, 런타임에서도 Store strict typing이 실패 report로 거부한다.
- Choice 항목별 Effect는 선택된 항목의 Effect만 실행한다. 공통 Effect 포트에 연결한 Effect는 모든 선택에서 실행된다.

런타임에서는 read provider와 mutation provider를 명시적으로 넘긴다. 같은 `WorldState`를 둘 다 사용할 수 있지만,
자동 승격은 없다.

```gdscript
DialogueManager.play(dialogue, WorldState, WorldState)
```

mutation 결과를 관찰하려면 `DialoguePlayer.state_mutation_evaluated(effect_node_id, report)`를 구독한다.
report에는 `operation`, `key`, `old_value`, `new_value`, `error`가 포함된다.

> 에디터 Play(디버그 실행)에서는 provider를 직접 넘기지 않아도 addon example schema 기반 preview store가
> 자동 주입된다. 자세한 내용과 고정 example schema 한계는 13절 "에디터 Play로 WorldState 미리보기
> 테스트하기"를 참고한다.

## 15. 현재 미완성 또는 제한된 기능

- Portrait transition 애니메이션
- 화자에 따른 Portrait 자동 focus/dim
- actor/expression에서 Texture를 찾는 actor database/resolver
- 기존 `SayDef.portrait`를 새 Portrait 노드로 바꾸는 마이그레이션 도구
- Autoload와 SceneFunction의 안전한 런타임 실행 정책
- Wait, Sound, Emit Event 등 추가 Effect 노드
- Branch, End의 Effect 출력 포트
- 일반 Flow의 병렬 실행
- named entry 또는 Entry Point

## 16. 문제 해결

### 저장되지 않는다

Godot Output의 `DialogueTool 검증` 오류를 확인한다. 가장 흔한 원인은 다음과 같다.

- 하나의 흰색 Flow 출력에 노드 두 개 연결
- 흰색 Flow와 주황색 Effect를 서로 연결
- Effect를 Say나 Choice에 연결
- Effect 순환

### Portrait가 보이지 않는다

- `texture_path`가 `res://` 경로인지 확인한다.
- 리소스가 Texture2D인지 확인한다.
- Show의 슬롯과 Hide의 슬롯이 같은지 확인한다.
- Portrait를 Effect로 연결했다면 주황색 포트끼리 연결했는지 확인한다.
- 빈 Texture 경로도 상태는 생성하지만 실제 이미지는 렌더링되지 않는다.

### 여러 Portrait 중 하나만 실행된다

Portrait를 흰색 Flow 출력에 나란히 연결하지 말고 Start 또는 Say의 주황색 Effect 출력에 연결한다.

### State Effect가 실행되지 않는다

- `DialogueManager.play(dialogue, WorldState, WorldState)`처럼 mutation provider를 세 번째 인자로 넘겼는지 확인한다.
- read-only key는 `read_only` report를 내고 값이 바뀌지 않는다.
- StateAdd는 int key에는 int delta, float key에는 float delta만 허용한다.
- Choice에 연결했다면 항목별 Effect 포트와 공통 Effect 포트를 구분한다.

### 선택 후 잘못된 노드로 이동한다

Choice 항목 순서와 오른쪽 Flow 출력 포트 순서가 일치하는지 확인한다.

### Expression이 항상 false가 된다

- Expression 입력 포트와 변수 키 순서를 확인한다.
- Godot Expression 문법 오류를 Output에서 확인한다.
- 연결되지 않은 입력은 `null`이며 Branch에서 false로 처리된다.

## 17. 주요 코드 위치

| 역할 | 경로 |
| --- | --- |
| EditorPlugin 진입점 | `addons/dialogtool/dialoguetool.gd` |
| Dialogue 메인 화면 | `addons/dialogtool/dialoguetool_main.tscn` |
| GraphEdit 저장/로드/validation | `addons/dialogtool/Editor/editor.gd` |
| 노드 Adapter Registry | `addons/dialogtool/Editor/Adapter/node_type_registry.gd` |
| 그래프 Resource | `addons/dialogtool/Resource/dialogue_graph_resource.gd` |
| 런타임 Player | `addons/dialogtool/RunTime/dialogue_player.gd` |
| 전역 Manager | `addons/dialogtool/RunTime/dialogue_manager.gd` |
| Dialogue UI | `addons/dialogtool/UI/dialogue_ui.gd` |
| Portrait Adapter | `addons/dialogtool/Editor/Adapter/portrait_editor_adapter.gd` |
| State Effect Adapter | `addons/dialogtool/Editor/Adapter/state_effect_editor_adapter.gd` |
| 런타임 회귀 테스트 | `addons/dialogtool/RunTime/tests/` |

## Related

- [[DialogueTool]]
- [[DialogueTool-Architecture]]
- [[Runtime-Data-Flow]]
- [[DT-002-Portrait-State]]
- [[DT-003-Say-Line-Paging]]
- [[DT-004-Nonblocking-Effect-Flow]]
- [[ADR-005-Nonblocking-Effect-Connections]]
