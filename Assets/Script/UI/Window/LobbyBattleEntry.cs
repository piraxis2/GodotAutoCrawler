using AutoCrawler.Assets.Script.Battle;
using Godot;

namespace AutoCrawler.Assets.Script.UI.Window;

// 로비↔전투 v0 왕복 controller. 고정 BattleRequest로 활성 BattleSession 하나를 생성·장착·시작하고(BS-002),
// 완료(Victory/Defeat/Aborted) 시 결과를 소비해 최소 표시 + outgame/hub 복귀 + session cleanup을 하고 같은
// 버튼으로 재전투할 수 있게 한다(BS-003). 정산/이월/DungeonRun은 소유하지 않는다(ADR-022). Shell이 아니라 전용
// entry Node다.
public partial class LobbyBattleEntry : Node
{
    // 세션을 AddChild할 mount. World 창의 SubViewport로 배선한다(OD1 격리). null이면 진입을 거부한다.
    [Export] private Node _battleMount;

    // battle mount 가시성(SubViewportContainer 등). 진입 시 표시, 완료 시 숨김. null이면 토글을 건너뛴다
    // (headless seam 테스트/미배선).
    [Export] private Control _battleMountVisibility;

    // preset 적용용 manager. null이면 preset 적용을 건너뛴다(headless seam 테스트에서 선택).
    [Export] private WorkspaceWindowManager _windowManager;
    [Export] private string _battlePresetId = WorkspaceLayoutPreset.Battle;
    [Export] private string _outgamePresetId = WorkspaceLayoutPreset.Outgame;

    // 로비 버튼. 버튼 Pressed → 고정 v0 request로 진입.
    [Export] private Button _enterBattleButton;

    // 최근 전투 결과 표시용 라벨(OD1). null이면 표시를 건너뛴다(headless seam 테스트/미배선).
    [Export] private Label _resultLabel;

    // v0 고정 request source. encounter/party/seed 생성 정책은 후속 U3 호출자 소유.
    [Export] private PackedScene _battleScene;
    [Export] private string _playerPath = "Articles/Ally/Character";
    [Export] private long _seed = 20260713;

    private BattleSession _activeSession;

    public bool IsBattleActive => _activeSession != null;

    // 마지막 완료 전투 결과(scene-free). 첫 완료 전 null, Completed에서만 갱신하고 진입 시 초기화하지 않는다(OD5).
    public BattleResult LastResult { get; private set; }

    public override void _Ready()
    {
        if (_enterBattleButton != null) _enterBattleButton.Pressed += OnEnterBattlePressed;
        UpdateResultLabel("최근 전투: 없음");
    }

    public override void _ExitTree()
    {
        if (_enterBattleButton != null) _enterBattleButton.Pressed -= OnEnterBattlePressed;
    }

    // 로비 버튼 → 고정 v0 request 진입. 세부 유효성은 TryEnterBattle/BattleSession이 fail-closed로 처리한다.
    private void OnEnterBattlePressed()
    {
        TryEnterBattle(new BattleRequest
        {
            BattleScene = _battleScene,
            Seed = _seed,
            PlayerPath = _playerPath,
        });
    }

    // 활성 세션이 있거나 request/mount가 없으면 false로 fail-closed. 성공 시 active latch를 Start 이전에 동기
    // 설정하고(중복 차단), preset/mount 가시성을 적용한 뒤 세션을 mount 아래에 장착해 Start한다. 세부 request
    // 유효성(씬/노드/PC/진영)은 BattleSession이 검증해 실패 시 deferred Completed(Aborted)로 닫고, 그 cleanup은
    // OnBattleCompleted가 자연 완료와 동일하게 처리한다.
    public bool TryEnterBattle(BattleRequest request)
    {
        if (IsBattleActive || request == null || _battleMount == null) return false;

        // preset 실패(배선 오류로 unknown preset)면 hub가 그대로인 채 전투만 시작되는 상태를 막는다. manager가
        // 있을 때 실패하면 세션을 만들지 않고 fail-closed한다.
        if (_windowManager != null && !_windowManager.ApplyPreset(_battlePresetId)) return false;

        var session = new BattleSession();
        _activeSession = session; // Start 이전에 latch

        if (_battleMountVisibility != null) _battleMountVisibility.Visible = true;
        UpdateResultLabel("전투 진행 중"); // 진입 시 라벨만 바꾸고 LastResult는 보존(OD5)

        // 순서(ADR-022): Configure → Completed 구독 → mount 장착 → Start.
        session.Configure(request);
        session.Completed += OnBattleCompleted;
        _battleMount.AddChild(session);
        session.Start();
        return true;
    }

    // OD3 순서: result 캡처 → (구독 해제 + active 해제 + session QueueFree) → mount 숨김 → outgame preset →
    // 결과 라벨. cleanup을 preset/presentation보다 먼저·독립적으로 수행해 실패가 누수로 이어지지 않게 한다
    // (Finding 2). Completed는 이미 teardown 후 deferred라 handler는 idle에서 실행되고 result는 scene-free다.
    private void OnBattleCompleted(BattleResult result)
    {
        var session = _activeSession;
        if (session == null) return;

        LastResult = result;

        session.Completed -= OnBattleCompleted;
        _activeSession = null;
        session.QueueFree();

        // 빈 SubViewportContainer가 hub 입력/렌더를 가리지 않도록 반드시 숨긴다(Finding 1).
        if (_battleMountVisibility != null) _battleMountVisibility.Visible = false;

        // 로비 복귀. preset 실패는 cleanup을 되돌리지 않고 기록만 한다(OD6).
        if (_windowManager != null && !_windowManager.ApplyPreset(_outgamePresetId))
            GD.PushError($"LobbyBattleEntry: outgame preset '{_outgamePresetId}' 적용 실패(로비 복귀 표시 불완전).");

        // 고정 production request의 Aborted는 구성 오류 신호라 진단을 남긴다(OD4).
        if (result.Outcome == BattleOutcome.Aborted)
            GD.PushError($"LobbyBattleEntry: 전투 시작 실패(Aborted/{result.EndReason}) — 고정 request 구성 오류.");

        UpdateResultLabel(ResultText(result));
    }

    // v0 최소 결과 매핑. 현지화/상세 확장은 후속(controller 비대화 방지).
    private static string ResultText(BattleResult result) => result.Outcome switch
    {
        BattleOutcome.Victory => "최근 전투: 승리",
        BattleOutcome.Defeat => "최근 전투: 패배",
        BattleOutcome.Aborted => "최근 전투: 전투 시작 실패",
        BattleOutcome.Retreat => "최근 전투: 후퇴",
        _ => "최근 전투: 알 수 없음",
    };

    private void UpdateResultLabel(string text)
    {
        if (_resultLabel != null) _resultLabel.Text = text;
    }
}
