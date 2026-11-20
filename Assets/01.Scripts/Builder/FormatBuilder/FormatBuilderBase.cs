using System;
using System.Collections.Generic;
using System.IO;

public abstract class FormatBuilderBase : IDataFormatBuilder
{
    private readonly Dictionary<(DataShape, DataComplexity), Action<object, string, BenchmarkDataSpec>> handlers
        = new Dictionary<(DataShape, DataComplexity), Action<object, string, BenchmarkDataSpec>>();

    public abstract string FormatName { get; }
    public virtual bool IsLegacyFormat => false;

    protected void Register(DataShape shape, DataComplexity complexity, Action<object, string> handler)
    {
        handlers[(shape, complexity)] = (container, path, _) => handler(container, path);
    }

    protected void Register(DataShape shape, DataComplexity complexity, Action<object, string, BenchmarkDataSpec> handler)
    {
        handlers[(shape, complexity)] = handler;
    }

    public virtual bool Supports(DataShape shape)
    {
        foreach (var key in handlers.Keys)
        {
            if (key.Item1 == shape) return true;
        }
        return false;
    }

    public virtual string GetTargetPath(string formatShapeScaleDir, BenchmarkDataSpec spec)
    {
        return Path.Combine(formatShapeScaleDir, $"{spec.BaseFileName}.{GetExtension()}");
    }

    protected abstract string GetExtension();

    public virtual void Build(object container, string targetPathOrFolder, BenchmarkDataSpec spec)
    {
        if (handlers.TryGetValue((spec.Shape, spec.Complexity), out var handler))
        {
            handler(container, targetPathOrFolder, spec);
        }
        else
        {
            UnityEngine.Debug.LogWarning($"[{FormatName}] 미지원 데이터셋 조합: {spec.Shape} / {spec.Complexity}");
        }
    }
}