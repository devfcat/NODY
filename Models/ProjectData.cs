namespace Nody.Models;

/// <summary>노드 간 연결(플로우). 에디터 전용 데이터이며 클라이언트에서는 사용하지 않는다.</summary>
public class FlowModel
{
    public Guid From { get; set; }
    public Guid To { get; set; }
}

public class ViewState
{
    public double Zoom { get; set; } = 1.0;
    public double OffsetX { get; set; } = 80;
    public double OffsetY { get; set; } = 80;
}

/// <summary>프로젝트 폴더의 nody_flow.json 에 저장되는 플로우 데이터 전체.</summary>
public class ProjectData
{
    public int Version { get; set; } = 1;
    public List<NodeModel> Nodes { get; set; } = new();
    public List<FlowModel> Flows { get; set; } = new();
    public ViewState View { get; set; } = new();
}

public class AppSettings
{
    public string LastProject { get; set; } = "";
    public string Theme { get; set; } = "Light";
}
