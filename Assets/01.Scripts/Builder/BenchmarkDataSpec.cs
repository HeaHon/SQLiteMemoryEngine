public class BenchmarkDataSpec
{
    public DataScale Scale { get; set; }
    public DataComplexity Complexity { get; set; }
    public DataShape Shape { get; set; }
    public int Seed { get; set; } = 42;

    // 가변 길이 데이터셋(Document/Sparse 등)을 위한 분포 옵션
    public int MinChildrenPerEntity { get; set; } = 0;
    public int MaxChildrenPerEntity { get; set; } = 10;


    public int Count => (int)Scale;
    public string ScaleStr => Scale.ToString();
    public string ComplexityStr => Complexity.ToString();
    public string ShapeStr => Shape.ToString();
    public string BaseFileName => $"GameData_{ScaleStr}_{ComplexityStr}_{ShapeStr}";
}