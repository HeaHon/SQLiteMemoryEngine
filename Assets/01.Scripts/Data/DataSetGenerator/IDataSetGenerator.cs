public interface IDataSetGenerator
{
    DataShape Shape { get; }
    bool Supports(DataComplexity complexity);
    object Generate(BenchmarkDataSpec spec);
}
