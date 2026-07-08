#if TOOLS
using Godot;

namespace AutoCrawler.Assets.Script.Tests;

// Phase1 12종 .tres 부트스트랩 생성기. 회귀 스위트에 포함되지 않는다 — 데이터 팩을 새로 만들거나
// 스펙(Phase1SkillSpec) 갱신을 반영할 때만 수동 실행한다.
//   godot --headless --path . res://Assets/Script/Tests/sk001_phase1_data_generator.tscn
public partial class Sk001Phase1DataGenerator : Node
{
    public override void _Ready()
    {
        if (!(OS.HasFeature("dedicated_server") || DisplayServer.GetName() == "headless")) return;

        DirAccess.MakeDirRecursiveAbsolute(Phase1SkillSpec.DataDir);
        int written = 0, failed = 0;
        foreach (var (id, def) in Phase1SkillSpec.Build())
        {
            string path = $"{Phase1SkillSpec.DataDir}/{id}.tres";
            Error err = ResourceSaver.Save(def, path);
            if (err == Error.Ok) { written++; GD.Print($"  wrote {path}"); }
            else { failed++; GD.PrintErr($"  save {id} -> {err}"); }
        }

        GD.Print($"[Phase1 Generator] written={written} failed={failed}");
        GetTree().Quit(failed == 0 ? 0 : 1);
    }
}
#endif
