using System.Collections.Generic;
using Godot;
using AutoCrawler.Assets.Script;

namespace AutoCrawler.Assets.Script.Util;

public static class GlobalUtil
{
    public static Node ReloadPackedScene(Node nodeToReload)
    {
        // 1. 유효성 검사
        if (!GodotObject.IsInstanceValid(nodeToReload))
        {
            GD.PrintErr("SceneReloader: 리로드할 노드가 유효하지 않습니다.");
            return null;
        }

        // PackedScene 원본 파일 경로 가져오기
        string scenePath = nodeToReload.SceneFilePath;
        if (string.IsNullOrEmpty(scenePath))
        {
            GD.PrintErr("SceneReloader: 노드에 SceneFilePath가 없습니다. 스크립트로 생성된 노드는 리로드할 수 없습니다.");
            return null;
        }

        // 2. 부모와 위치(인덱스) 기억
        Node parent = nodeToReload.GetParent();
        if (!GodotObject.IsInstanceValid(parent))
        {
            GD.PrintErr("SceneReloader: 노드에 부모가 없습니다.");
            return null;
        }

        int index = nodeToReload.GetIndex();
        StringName nodeName = nodeToReload.Name;

        // 3. 기존 노드 제거
        nodeToReload.QueueFree();

        // 4. PackedScene에서 새 인스턴스 생성
        var packedScene = GD.Load<PackedScene>(scenePath);
        if (packedScene == null)
        {
            GD.PrintErr($"SceneReloader: PackedScene을 로드할 수 없습니다: {scenePath}");
            return null;
        }

        Node newInstance = packedScene.Instantiate();
        GD.Print("hi");
        newInstance.Name = nodeName;

        // 5. 기억해 둔 위치에 새 인스턴스 추가
        parent.AddChild(newInstance);
        parent.MoveChild(newInstance, index);

        GD.Print($"Scene reloaded: {scenePath}");
        return newInstance;
    }
}