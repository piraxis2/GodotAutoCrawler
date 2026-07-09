using System.Collections.Generic;
using AutoCrawler.Assets.Script.GameLog;
using Godot;

namespace AutoCrawler.Assets.Script.UI.Window;

// Workspace `Log` 창의 읽기 전용 표시 UI(GL-001 Step 2). placeholder Label을 대체한다.
//
// severity 정책(기본 뷰=Event, 채널 탭=Event+Detail, Trace는 미노출)은 GameLogModel이 소유한다(D6).
// 이 컨트롤러는 tab -> 채널 집합 필터와 Godot 위젯 렌더만 담당한다. v0는 읽기 전용이라 링크 클릭으로
// 실제 창을 열지 않는다(Out of Scope) — 대화 아카이브 접힘/펼침만 상호작용한다.
public partial class LogWindowController : Control
{
    // autoload 경로. `GameLogService`(ADR-020)의 공유 모델을 여기서 찾는다.
    private const string ServicePath = "/root/GameLogService";

    // 트리 진입 전에 UseModel로 주입된 모델. 있으면 service lookup을 건너뛴다(테스트 격리용).
    private GameLogModel _injectedModel;

    // 실제 표시에 쓰는 모델. _Ready에서 주입 > service > 로컬 순으로 확정한다.
    private GameLogModel _model;

    // 펼쳐진 접힘-entry의 Id. 없으면 접힘 상태.
    private readonly HashSet<long> _expanded = new();

    // 현재 렌더된 행. 테스트 introspection과 재빌드에 쓴다.
    private readonly Dictionary<long, Row> _rows = new();

    private readonly Dictionary<LogChannelTab, Button> _tabButtons = new();

    private VBoxContainer _entryList;

    private LogChannelTab _tab = LogChannelTab.All;

    // 로그 데이터/append 창구. 샘플 버튼과 (후속) adapter가 여기에 append한다.
    public GameLogModel Model => _model;

    public LogChannelTab CurrentTab => _tab;

    // 트리 진입 전에 명시적 모델을 주입한다. autoload service lookup을 건너뛰어 테스트를 격리한다(ADR-020).
    public void UseModel(GameLogModel model)
    {
        _injectedModel = model;
    }

    public override void _Ready()
    {
        _model = ResolveModel();

        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        BuildUi();

        _model.EntryAdded += OnEntryAdded;
        _model.Cleared += OnCleared;

        SelectTab(LogChannelTab.All);
    }

    // 주입 > autoload service 모델 > 로컬 모델 순. service가 없으면 로컬 모델로 fail-closed한다
    // (LogWindow는 standalone에서도 깨지지 않는다 — 로그가 비어 보일 뿐).
    private GameLogModel ResolveModel()
    {
        if (_injectedModel != null) return _injectedModel;

        var service = GetNodeOrNull<GameLogService>(ServicePath);
        return service?.Model ?? new GameLogModel();
    }

    public override void _ExitTree()
    {
        if (_model == null) return;
        _model.EntryAdded -= OnEntryAdded;
        _model.Cleared -= OnCleared;
    }

    private void OnEntryAdded(GameLogEntry entry)
    {
        // trim으로 사라진 entry의 펼침 상태 id는 남을 수 있으나, 렌더는 현재 entry만 보므로 무해하다
        // (200줄 상한 + 소수의 아카이브라 누수는 무시 가능). 실제 제거는 Clear에서 한다.
        Refresh();
    }

    private void OnCleared()
    {
        _expanded.Clear();
        Refresh();
    }

    private void BuildUi()
    {
        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach (string side in new[] { "margin_left", "margin_top", "margin_right", "margin_bottom" })
            margin.AddThemeConstantOverride(side, 8);
        AddChild(margin);

        var root = new VBoxContainer();
        margin.AddChild(root);

        var tabBar = new HBoxContainer();
        root.AddChild(tabBar);
        AddTabButton(tabBar, LogChannelTab.All, "전체");
        AddTabButton(tabBar, LogChannelTab.Combat, "전투");
        AddTabButton(tabBar, LogChannelTab.Story, "이야기");
        AddTabButton(tabBar, LogChannelTab.RewardSystem, "획득·시스템");

        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        tabBar.AddChild(spacer);

        var sampleButton = new Button { Text = "샘플 주입" };
        sampleButton.Pressed += SeedSampleLog;
        tabBar.AddChild(sampleButton);

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        root.AddChild(scroll);

        _entryList = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(_entryList);
    }

    private void AddTabButton(Container parent, LogChannelTab tab, string label)
    {
        var button = new Button { Text = label };
        button.Pressed += () => SelectTab(tab);
        parent.AddChild(button);
        _tabButtons[tab] = button;
    }

    public void SelectTab(LogChannelTab tab)
    {
        _tab = tab;
        foreach (KeyValuePair<LogChannelTab, Button> pair in _tabButtons)
        {
            // 활성 탭은 비활성화해 재클릭을 막고 현재 선택을 시각적으로 표시한다.
            pair.Value.Disabled = pair.Key == tab;
        }

        Refresh();
    }

    // 접힘-entry를 펼치거나 접는다. 접힘 가능한 entry(IsCollapsed + Body)가 아니거나 없는 id면 no-op이다
    // — _expanded에 임의 id를 남기지 않는다.
    public void ToggleExpand(long id)
    {
        if (!IsCollapsibleEntry(id)) return;
        if (!_expanded.Remove(id)) _expanded.Add(id);
        Refresh();
    }

    private bool IsCollapsibleEntry(long id)
    {
        foreach (GameLogEntry entry in _model.GetEntries(null, includeDetail: true, includeTrace: true))
            if (entry.Id == id) return entry.IsCollapsed && !string.IsNullOrEmpty(entry.Body);
        return false;
    }

    public bool IsExpanded(long id) => _expanded.Contains(id);

    // 현재 탭/severity 정책으로 표시되는 entry 목록(append 순서). 테스트용.
    public IReadOnlyList<GameLogEntry> CurrentEntries() =>
        _model == null ? System.Array.Empty<GameLogEntry>() : EntriesForTab(_model, _tab, includeTrace: false);

    public int RowCount => _rows.Count;

    // 행 제목 색(importance 강조 + Detail 감광). 테스트가 시각 구분을 검증한다.
    public Color GetRowTitleColor(long id) => _rows.TryGetValue(id, out Row row) ? row.Header.Modulate : Colors.White;

    public bool IsBodyVisible(long id) => _rows.TryGetValue(id, out Row row) && row.Body != null && row.Body.Visible;

    // 수동 GUI 확인용 샘플. 채널/등급/강조/접힘 아카이브를 두루 덮는다.
    public void SeedSampleLog()
    {
        _model.Append(new GameLogEntry(GameLogChannel.Combat, GameLogSeverity.Event,
            GameLogImportance.Normal, "1층 전투 시작", occurredAt: "F1 T1"));
        _model.Append(new GameLogEntry(GameLogChannel.Combat, GameLogSeverity.Detail,
            GameLogImportance.Normal, "전사 → 고블린 피해 12", occurredAt: "F1 T2"));
        _model.Append(new GameLogEntry(GameLogChannel.Combat, GameLogSeverity.Event,
            GameLogImportance.Warning, "PC 영창이 끊김", occurredAt: "F1 T3"));
        _model.Append(new GameLogEntry(GameLogChannel.Combat, GameLogSeverity.Event,
            GameLogImportance.Critical, "아군 사냥꾼 사망", occurredAt: "F1 T5"));
        _model.Append(new GameLogEntry(GameLogChannel.Story, GameLogSeverity.Event,
            GameLogImportance.Normal, "동료 영입: 수호기사", occurredAt: "W2"));
        _model.Append(new GameLogEntry(GameLogChannel.Story, GameLogSeverity.Event,
            GameLogImportance.Normal, "간막 이벤트 — 모닥불의 대화",
            body: "수호기사: 함께 오르겠습니다.\nPC: 뒤를 부탁한다.", isCollapsed: true, occurredAt: "W2"));
        _model.Append(new GameLogEntry(GameLogChannel.Reward, GameLogSeverity.Event,
            GameLogImportance.Normal, "레어 획득: 룬 검", occurredAt: "W2"));
        _model.Append(new GameLogEntry(GameLogChannel.System, GameLogSeverity.Event,
            GameLogImportance.Normal, "세이브 완료", occurredAt: "W2"));
    }

    private void Refresh()
    {
        if (_entryList == null) return; // _Ready 전에는 아무것도 하지 않는다.

        foreach (Node child in _entryList.GetChildren())
        {
            _entryList.RemoveChild(child);
            child.QueueFree();
        }

        _rows.Clear();

        IReadOnlyList<GameLogEntry> entries = CurrentEntries();
        if (entries.Count == 0)
        {
            var emptyHint = new Label { Text = "(로그 없음)" };
            emptyHint.Modulate = new Color(0.6f, 0.6f, 0.6f);
            _entryList.AddChild(emptyHint);
            return;
        }

        foreach (GameLogEntry entry in entries)
        {
            AddRow(entry);
        }
    }

    private void AddRow(GameLogEntry entry)
    {
        Color color = ComputeTitleColor(entry);
        bool collapsible = entry.IsCollapsed && !string.IsNullOrEmpty(entry.Body);
        bool expanded = _expanded.Contains(entry.Id);

        string indicator = collapsible ? (expanded ? "▾ " : "▸ ") : "";
        string occ = string.IsNullOrEmpty(entry.OccurredAt) ? "" : $"[{entry.OccurredAt}] ";
        string detailMark = entry.Severity == GameLogSeverity.Detail ? "· " : "";
        string headerText = $"{indicator}{occ}{detailMark}{entry.Title}";

        var rowBox = new VBoxContainer();

        Control header;
        if (collapsible)
        {
            var button = new Button { Text = headerText, Flat = true, Alignment = HorizontalAlignment.Left };
            long id = entry.Id;
            button.Pressed += () => ToggleExpand(id);
            header = button;
        }
        else
        {
            header = new Label { Text = headerText };
        }

        header.Modulate = color;
        rowBox.AddChild(header);

        Label body = null;
        if (collapsible)
        {
            // 대화 아카이브는 제목 1줄 접힘으로 시작하고, 펼치면 본문을 보여준다.
            body = new Label
            {
                Text = entry.Body,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                Visible = expanded,
            };
            body.Modulate = color.Darkened(0.3f);
            rowBox.AddChild(body);
        }

        _entryList.AddChild(rowBox);
        _rows[entry.Id] = new Row { Header = header, Body = body };
    }

    // importance = 시각 강조(D7), Detail = 감광으로 시각 구분(Step 2). severity/importance를 색으로만 섞고
    // 노출 범위 판단에는 쓰지 않는다.
    private static Color ComputeTitleColor(GameLogEntry entry)
    {
        Color color = entry.Importance switch
        {
            GameLogImportance.Warning => new Color(1f, 0.82f, 0.35f),
            GameLogImportance.Critical => new Color(1f, 0.45f, 0.45f),
            _ => new Color(0.9f, 0.9f, 0.9f),
        };

        if (entry.Severity == GameLogSeverity.Detail) color = color.Darkened(0.45f);
        return color;
    }

    // tab -> 채널 집합 + severity 정책. severity 필터는 모델이 적용하고, 채널 집합만 여기서 좁힌다.
    // 전체 탭 = 모든 채널 Event만. 채널 탭 = 해당 채널 Event+Detail. Trace는 v0 LogWindow에 노출하지 않는다.
    public static IReadOnlyList<GameLogEntry> EntriesForTab(GameLogModel model, LogChannelTab tab, bool includeTrace)
    {
        bool includeDetail = tab != LogChannelTab.All;
        IReadOnlyList<GameLogEntry> candidates = model.GetEntries(null, includeDetail, includeTrace);

        switch (tab)
        {
            case LogChannelTab.All:
                return candidates;
            case LogChannelTab.Combat:
                return FilterChannels(candidates, GameLogChannel.Combat);
            case LogChannelTab.Story:
                return FilterChannels(candidates, GameLogChannel.Story);
            case LogChannelTab.RewardSystem:
                return FilterChannels(candidates, GameLogChannel.Reward, GameLogChannel.System);
            default:
                return candidates;
        }
    }

    private static IReadOnlyList<GameLogEntry> FilterChannels(
        IReadOnlyList<GameLogEntry> entries, params GameLogChannel[] channels)
    {
        var allowed = new HashSet<GameLogChannel>(channels);
        var result = new List<GameLogEntry>();
        foreach (GameLogEntry entry in entries)
            if (allowed.Contains(entry.Channel))
                result.Add(entry);
        return result;
    }

    private sealed class Row
    {
        public Control Header;
        public Label Body;
    }
}
