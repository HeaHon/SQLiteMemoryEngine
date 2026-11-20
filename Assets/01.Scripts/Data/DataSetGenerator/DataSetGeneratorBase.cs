using System;
using System.Collections.Generic;

public abstract class DataSetGeneratorBase : IDataSetGenerator
{
    private readonly Dictionary<DataComplexity, Func<BenchmarkDataSpec, object>> generators = new Dictionary<DataComplexity, Func<BenchmarkDataSpec, object>>();

    public abstract DataShape Shape { get; }

    protected void Register(DataComplexity complexity, Func<BenchmarkDataSpec, object> generator)
    {
        generators[complexity] = generator;
    }

    public bool Supports(DataComplexity complexity) => generators.ContainsKey(complexity);

    public object Generate(BenchmarkDataSpec spec)
    {
        if (generators.TryGetValue(spec.Complexity, out var generator))
        {
            return generator(spec);
        }

        UnityEngine.Debug.LogWarning($"[{GetType().Name}] 미지원 복잡도: {spec.Complexity}");
        return null;
    }
}