using AutoCrawler.Assets.Script.Battle;
using Godot;

namespace AutoCrawler.Assets.Script.UI.Window;

// BS-002 로비→전투 단방향 진입 controller. 고정 BattleRequest로 활성 BattleSession 하나를 생성·장착·시작하고,
// 완료(Victory/Defeat/Aborted) 시 구독/active/session Node만 정리한다. 결과 표시·정산·로비 복귀는 소유하지 않는다
// (ADR-022, BS-002 Step 0 리뷰 OD1~4). Shell이 아니라 전용 entry Node다(OD2).
public partial class LobbyBattleEntry : Node
{
    // 세션을 AddChild할 mount. Step 2에서 World 창의 SubViewport로 배선한다(OD1 격리). null이면 진입을 거부한다.
    [Export] private Node _battleMount;

    // battle mount 가시성(SubViewportContainer 등). null이면 토글을 건너뛴다(headless seam 테스트/미배선).
    [Export] private Control _battleMountVisibility;

    // battle preset 적용용 manager. null이면 preset 적용을 건너뛴다(OD2 책임, headless seam 테스트에서 선택).
    [Export] private WorkspaceWindowManager _windowManager;
    [Export] private string _battlePresetId = WorkspaceLayoutPreset.Battle;

    // 로비 버튼(OD2: controller가 버튼/고정 request를 소유). 버튼 Pressed → 고정 v0 request로 진입.
    [Export] private Button _enterBattleButton;

    // v0 고정 request source(OD4). encounter/party/seed 생성 정책은 후속 U3 호출자 소유.
    [Export] private PackedScene _battleScene;
    [Export] private string _playerPath = "Articles/Ally/Character";
    [Export] private long _seed = 20260713;

    private BattleSession _activeSession;

    public bool IsBattleActive => _activeSession != null;

    public override void _Ready()
    {
        if (_enterBattleButton != null) _enterBattleButton.Pressed += OnEnterBattlePressed;
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
    // 설정하고(중복 차단, Finding 4), preset/mount 가시성을 적용한 뒤 세션을 mount 아래에 장착해 Start한다.
    // 세부 request 유효성(씬/노드/PC/진영)은 BattleSession이 검증해 실패 시 deferred Completed(Aborted)로 닫고,
    // 그 cleanup은 OnBattleCompleted가 자연 완료와 동일하게 처리한다(Finding 3).
    public bool TryEnterBattle(BattleRequest request)
    {
        if (IsBattleActive || request == null || _battleMount == null) return false;

        // preset 실패(배선 오류로 unknown preset)면 hub가 그대로인 채 전투만 시작되는 상태를 막는다. manager가
        // 있을 때 실패하면 세션을 만들지 않고 fail-closed한다(리뷰 P2).
        if (_windowManager != null && !_windowManager.ApplyPreset(_battlePresetId)) return false;

        var session = new BattleSession();
        _activeSession = session; // Start 이전에 latch

        if (_battleMountVisibility != null) _battleMountVisibility.Visible = true;

        // 순서(ADR-022): Configure → Completed 구독 → mount 장착 → Start.
        session.Configure(request);
        session.Completed += OnBattleCompleted;
        _battleMount.AddChild(session);
        session.Start();
        return true;
    }

    // OD3: outcome(Victory/Defeat/Aborted)을 구분하지 않고 동일하게 구독 해제 → active 해제 → session Node
    // QueueFree만 수행한다. 결과 표시/preset 복귀/상위 상태 변경 없음. World는 battle mode에 그대로 남는다.
    private void OnBattleCompleted(BattleResult result)
    {
        var session = _activeSession;
        if (session == null) return;

        session.Completed -= OnBattleCompleted;
        _activeSession = null;
        session.QueueFree();
    }
}
