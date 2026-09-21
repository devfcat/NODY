using System.Text.Json.Serialization;

namespace Nody.Models;

/// <summary>대사 파일 노드 / 선택지 파일 노드</summary>
public enum NodeType
{
    Dialogue,
    Choice
}

public class NodeModel
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>엑셀 파일명 (확장자 제외). 노드 이름 = 파일명.</summary>
    public string Name { get; set; } = "";

    public NodeType Type { get; set; } = NodeType.Dialogue;

    public double X { get; set; }
    public double Y { get; set; }

    [JsonIgnore]
    public string FileName => Name + ".xlsx";
}
