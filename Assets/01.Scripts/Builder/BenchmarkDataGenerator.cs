using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;

public class BenchmarkGenerationRequest
{
    public TargetFormat Formats { get; set; } = TargetFormat.SQLite_NativeC | TargetFormat.SQLite_ORM | TargetFormat.RocksDB | TargetFormat.LiteDB;
    public TargetShape Shapes { get; set; } = TargetShape.All;
    public TargetScale Scales { get; set; } = TargetScale.All;
    public TargetComplexity Complexities { get; set; } = TargetComplexity.All;
    public int Seed { get; set; } = 42;
    public int MinChildrenPerEntity { get; set; } = 0;
    public int MaxChildrenPerEntity { get; set; } = 10;
}

public static class BenchmarkDataGenerator
{
    private static readonly Dictionary<DataShape, IDataSetGenerator> Generators = new Dictionary<DataShape, IDataSetGenerator>
    {
        { DataShape.Relational, new RelationalDataSetGenerator() },
        { DataShape.Document, new DocumentDataSetGenerator() },
        { DataShape.Sparse, new SparseDataSetGenerator() }
    };

    private static readonly List<IDataFormatBuilder> Builders = new List<IDataFormatBuilder>
    {
        new SqliteFormatBuilder(),
        new LiteDbFormatBuilder(),
        new RocksDbFormatBuilder(),
        new FlatBuffersFormatBuilder(),
        new RawBinaryFormatBuilder(),
        new MessagePackFormatBuilder(),
        new JsonFormatBuilder()
    };

    public static void RegisterGenerator(IDataSetGenerator generator)
    {
        if (generator != null) Generators[generator.Shape] = generator;
    }

    public static void RegisterBuilder(IDataFormatBuilder builder)
    {
        if (builder != null && !Builders.Contains(builder)) Builders.Add(builder);
    }

    /// <summary>
    /// 기존 호출부 호환용 오버로드
    /// </summary>
    public static void ExecuteGeneration(bool includeLegacyFormats = false, DataShape[] targetShapes = null)
    {
        var formats = TargetFormat.SQLite_NativeC | TargetFormat.SQLite_ORM | TargetFormat.RocksDB | TargetFormat.LiteDB | TargetFormat.FlatBuffers | TargetFormat.RawBinary;
        if (includeLegacyFormats)
        {
            formats |= TargetFormat.MessagePack | TargetFormat.JSON;
        }

        TargetShape shapes = TargetShape.None;
        if (targetShapes == null)
        {
            shapes = TargetShape.All;
        }
        else
        {
            foreach (var s in targetShapes)
            {
                if (s == DataShape.Relational) shapes |= TargetShape.Relational;
                else if (s == DataShape.Document) shapes |= TargetShape.Document;
                else if (s == DataShape.Sparse) shapes |= TargetShape.Sparse;
            }
        }

        ExecuteGeneration(new BenchmarkGenerationRequest
        {
            Formats = formats,
            Shapes = shapes,
            Scales = TargetScale.All,
            Complexities = TargetComplexity.All
        });
    }

    /// <summary>
    /// 다중 선택 필터 기반 생성 진입점
    /// </summary>
    public static void ExecuteGeneration(BenchmarkGenerationRequest request, Action<string, float> onProgress = null)
    {
        if (request == null) return;

        var selectedShapes = ExtractShapes(request.Shapes);
        var selectedScales = ExtractScales(request.Scales);
        var selectedComplexities = ExtractComplexities(request.Complexities);
        var selectedBuilders = ResolveBuilders(request.Formats);

        if (selectedShapes.Count == 0 || selectedScales.Count == 0 || selectedComplexities.Count == 0 || selectedBuilders.Count == 0)
        {
            UnityEngine.Debug.LogWarning("[BenchmarkDataGenerator] 선택된 대상이 없어 생성을 건너뜁니다.");
            return;
        }

        string saRoot = Path.Combine(Application.dataPath, "StreamingAssets");
        int totalSteps = selectedShapes.Count * selectedScales.Count * selectedComplexities.Count;
        int currentStep = 0;
        Stopwatch totalStopwatch = Stopwatch.StartNew();

        foreach (var shape in selectedShapes)
        {
            if (!Generators.TryGetValue(shape, out var generator))
            {
                UnityEngine.Debug.LogWarning($"[BenchmarkDataGenerator] 등록된 생성기 없음: {shape}");
                currentStep += selectedScales.Count * selectedComplexities.Count;
                continue;
            }

            foreach (var scale in selectedScales)
            {
                foreach (var complexity in selectedComplexities)
                {
                    currentStep++;
                    if (!generator.Supports(complexity)) continue;

                    var spec = new BenchmarkDataSpec
                    {
                        Scale = scale,
                        Complexity = complexity,
                        Shape = shape,
                        Seed = request.Seed,
                        MinChildrenPerEntity = request.MinChildrenPerEntity,
                        MaxChildrenPerEntity = request.MaxChildrenPerEntity
                    };

                    onProgress?.Invoke($"{spec.BaseFileName} 생성 중... ({currentStep}/{totalSteps})", (float)currentStep / totalSteps);

                    Stopwatch dataStopwatch = Stopwatch.StartNew();
                    object dataContainer = generator.Generate(spec);
                    if (dataContainer == null) continue;
                    UnityEngine.Debug.Log($"[BenchmarkDataGenerator] {spec.BaseFileName} 데이터 생성: {dataStopwatch.Elapsed.TotalSeconds:F2}s");

                    foreach (var builder in selectedBuilders)
                    {
                        if (!builder.Supports(spec.Shape)) continue;

                        string formatShapeScaleDir = EnsureDirectory(
                            Path.Combine(saRoot, builder.FormatName, spec.ShapeStr, spec.ScaleStr));
                        string targetPath = builder.GetTargetPath(formatShapeScaleDir, spec);

                        Stopwatch buildStopwatch = Stopwatch.StartNew();
                        builder.Build(dataContainer, targetPath, spec);
                        UnityEngine.Debug.Log($"[BenchmarkDataGenerator] {spec.BaseFileName}/{builder.FormatName} 저장: {buildStopwatch.Elapsed.TotalSeconds:F2}s");
                    }
                }
            }
        }

        UnityEngine.Debug.Log($"[BenchmarkDataGenerator] 선택 생성 완료: {totalStopwatch.Elapsed.TotalMinutes:F2}분");
    }

    public static List<IDataFormatBuilder> ResolveBuilders(TargetFormat formatFlags)
    {
        var activeNames = new HashSet<string>();

        // SQLite Native C와 ORM은 동일한 .db 파일을 공유하므로 중복 빌드 방지
        if ((formatFlags & (TargetFormat.SQLite_NativeC | TargetFormat.SQLite_ORM)) != 0)
            activeNames.Add("SQLite");
        if ((formatFlags & TargetFormat.RocksDB) != 0) activeNames.Add("RocksDB");
        if ((formatFlags & TargetFormat.LiteDB) != 0) activeNames.Add("LiteDB");
        if ((formatFlags & TargetFormat.FlatBuffers) != 0) activeNames.Add("FlatBuffers");
        if ((formatFlags & TargetFormat.RawBinary) != 0) activeNames.Add("Binary");
        if ((formatFlags & TargetFormat.MessagePack) != 0) activeNames.Add("MessagePack");
        if ((formatFlags & TargetFormat.JSON) != 0) activeNames.Add("JSON");

        var result = new List<IDataFormatBuilder>();
        foreach (var builder in Builders)
        {
            if (activeNames.Contains(builder.FormatName))
            {
                result.Add(builder);
            }
        }
        return result;
    }

    private static List<DataShape> ExtractShapes(TargetShape flags)
    {
        var list = new List<DataShape>();
        if ((flags & TargetShape.Relational) != 0) list.Add(DataShape.Relational);
        if ((flags & TargetShape.Document) != 0) list.Add(DataShape.Document);
        if ((flags & TargetShape.Sparse) != 0) list.Add(DataShape.Sparse);
        return list;
    }

    private static List<DataScale> ExtractScales(TargetScale flags)
    {
        var list = new List<DataScale>();
        if ((flags & TargetScale.S100) != 0) list.Add(DataScale.S100);
        if ((flags & TargetScale.S1K) != 0) list.Add(DataScale.S1K);
        if ((flags & TargetScale.S10K) != 0) list.Add(DataScale.S10K);
        if ((flags & TargetScale.S100K) != 0) list.Add(DataScale.S100K);
        return list;
    }

    private static List<DataComplexity> ExtractComplexities(TargetComplexity flags)
    {
        var list = new List<DataComplexity>();
        if ((flags & TargetComplexity.C1_Low) != 0) list.Add(DataComplexity.C1_Low);
        if ((flags & TargetComplexity.C2_Mid) != 0) list.Add(DataComplexity.C2_Mid);
        if ((flags & TargetComplexity.C3_High) != 0) list.Add(DataComplexity.C3_High);
        return list;
    }

    private static string EnsureDirectory(string path)
    {
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        return path;
    }
}
