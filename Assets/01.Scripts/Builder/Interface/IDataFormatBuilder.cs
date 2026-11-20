public interface IDataFormatBuilder
{
    string FormatName { get; }
    bool IsLegacyFormat { get; }
    bool Supports(DataShape shape);
    string GetTargetPath(string formatShapeScaleDir, BenchmarkDataSpec spec);
    void Build(object container, string targetPathOrFolder, BenchmarkDataSpec spec);
}