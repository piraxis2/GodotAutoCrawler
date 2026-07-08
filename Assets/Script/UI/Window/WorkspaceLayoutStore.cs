using System.Collections.Generic;
using Godot;

namespace AutoCrawler.Assets.Script.UI.Window;

/// <summary>창 하나의 저장 스냅샷이다. position/size는 client 좌표(Godot Window.Position/Size)다.</summary>
public readonly struct WindowSnapshot
{
    public WindowSnapshot(bool visible, Vector2I position, Vector2I size, string contentMode)
    {
        Visible = visible;
        Position = position;
        Size = size;
        ContentMode = contentMode ?? "";
    }

    public bool Visible { get; }
    public Vector2I Position { get; }
    public Vector2I Size { get; }
    public string ContentMode { get; }
}

public enum LayoutLoadStatus
{
    /// <summary>파일을 읽고 스냅샷을 복원했다.</summary>
    Loaded,

    /// <summary>파일이 없다. 첫 실행이다.</summary>
    FirstRun,

    /// <summary>파일을 읽지 못했다(손상/파싱 실패).</summary>
    Corrupt,

    /// <summary>version 키가 없거나 이 빌드가 모르는 값이다.</summary>
    UnsupportedVersion
}

public readonly struct LayoutLoadResult
{
    public LayoutLoadResult(LayoutLoadStatus status, IReadOnlyDictionary<string, WindowSnapshot> snapshots)
    {
        Status = status;
        Snapshots = snapshots;
    }

    public LayoutLoadStatus Status { get; }
    public IReadOnlyDictionary<string, WindowSnapshot> Snapshots { get; }

    /// <summary>복원할 스냅샷이 실제로 있는가. 이게 false면 호출자는 기본 preset으로 fallback한다.</summary>
    public bool HasUsableSnapshots => Status == LayoutLoadStatus.Loaded && Snapshots != null && Snapshots.Count > 0;
}

/// <summary>
/// D7. Window 객체를 모르는 순수 저장 로직이다. `ConfigFile`을 읽고 쓰지만 Window나 DisplayServer는
/// 만지지 않는다. version 정책, 손상 처리, 타입 검증을 여기서 fail-closed로 담당한다.
///
/// 저장 파일은 `user://settings.ini`(전역 video 설정)와 분리한다. 손상 시 이 파일만 폐기해도 다른 설정을
/// 잃지 않는다.
/// </summary>
public class WorkspaceLayoutStore
{
    public const int CurrentVersion = 1;
    public const string DefaultPath = "user://workspace_layout.cfg";

    private const string MetaSection = "meta";
    private const string VersionKey = "version";
    private const string WindowSectionPrefix = "window.";

    private readonly string _path;

    public WorkspaceLayoutStore(string path = DefaultPath)
    {
        _path = path;
    }

    public string Path => _path;

    /// <summary>스냅샷을 파일로 저장한다. `version` 키를 항상 기록한다.</summary>
    /// <returns>Godot `Error.Ok`면 성공.</returns>
    public Error Save(IReadOnlyDictionary<string, WindowSnapshot> snapshots)
    {
        var config = new ConfigFile();
        config.SetValue(MetaSection, VersionKey, CurrentVersion);

        foreach (KeyValuePair<string, WindowSnapshot> entry in snapshots)
        {
            string section = WindowSectionPrefix + entry.Key;
            WindowSnapshot snapshot = entry.Value;
            config.SetValue(section, "visible", snapshot.Visible);
            config.SetValue(section, "position", snapshot.Position);
            config.SetValue(section, "size", snapshot.Size);
            config.SetValue(section, "content_mode", snapshot.ContentMode);
        }

        return config.Save(_path);
    }

    /// <summary>
    /// 파일을 읽는다. 실패는 절대 예외를 내지 않고 status로 보고한다.
    /// - 파일 없음 → FirstRun
    /// - 로드 실패 → Corrupt
    /// - version 누락/미지 → UnsupportedVersion(파일은 파괴하지 않는다)
    /// - 정상 → Loaded, 타입이 깨진 개별 창은 조용히 건너뛴다.
    /// </summary>
    public LayoutLoadResult Load()
    {
        if (!FileAccess.FileExists(_path))
        {
            return new LayoutLoadResult(LayoutLoadStatus.FirstRun, null);
        }

        var config = new ConfigFile();
        Error error = config.Load(_path);
        if (error != Error.Ok)
        {
            GD.PushWarning($"[WS-001] WorkspaceLayoutStore: load failed ({error}) for '{_path}'. Using default layout.");
            return new LayoutLoadResult(LayoutLoadStatus.Corrupt, null);
        }

        if (!TryReadVersion(config, out int version) || version != CurrentVersion)
        {
            GD.PushWarning($"[WS-001] WorkspaceLayoutStore: unsupported version in '{_path}'. Using default layout.");
            return new LayoutLoadResult(LayoutLoadStatus.UnsupportedVersion, null);
        }

        var snapshots = new Dictionary<string, WindowSnapshot>();
        foreach (string section in config.GetSections())
        {
            if (!section.StartsWith(WindowSectionPrefix)) continue;
            string id = section.Substring(WindowSectionPrefix.Length);
            if (id.Length == 0) continue;

            if (TryReadWindow(config, section, out WindowSnapshot snapshot))
            {
                snapshots[id] = snapshot;
            }
            else
            {
                GD.PushWarning($"[WS-001] WorkspaceLayoutStore: skipped malformed window entry '{section}'.");
            }
        }

        return new LayoutLoadResult(LayoutLoadStatus.Loaded, snapshots);
    }

    private static bool TryReadVersion(ConfigFile config, out int version)
    {
        version = 0;
        if (!config.HasSectionKey(MetaSection, VersionKey)) return false;
        Variant value = config.GetValue(MetaSection, VersionKey);
        if (value.VariantType != Variant.Type.Int) return false;
        version = (int)value;
        return true;
    }

    private static bool TryReadWindow(ConfigFile config, string section, out WindowSnapshot snapshot)
    {
        snapshot = default;
        if (!TryGet(config, section, "visible", Variant.Type.Bool, out Variant visible)) return false;
        if (!TryGet(config, section, "position", Variant.Type.Vector2I, out Variant position)) return false;
        if (!TryGet(config, section, "size", Variant.Type.Vector2I, out Variant size)) return false;
        if (!TryGet(config, section, "content_mode", Variant.Type.String, out Variant contentMode)) return false;

        var parsedSize = (Vector2I)size;
        // 0 이하 크기는 잡을 수 없는 창을 만든다. 손상으로 간주한다.
        if (parsedSize.X <= 0 || parsedSize.Y <= 0) return false;

        snapshot = new WindowSnapshot((bool)visible, (Vector2I)position, parsedSize, (string)contentMode);
        return true;
    }

    private static bool TryGet(ConfigFile config, string section, string key, Variant.Type expected, out Variant value)
    {
        value = default;
        if (!config.HasSectionKey(section, key)) return false;
        value = config.GetValue(section, key);
        return value.VariantType == expected;
    }
}
