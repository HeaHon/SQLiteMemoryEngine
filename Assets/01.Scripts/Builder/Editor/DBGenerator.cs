using UnityEditor;
using UnityEngine;

public class DBGenerator : EditorWindow
{
    private TargetFormat _targetFormats = TargetFormat.SQLite_NativeC | TargetFormat.RocksDB | TargetFormat.LiteDB;
    private TargetShape _targetShapes = TargetShape.All;
    private TargetScale _targetScales = TargetScale.S100 | TargetScale.S1K;
    private TargetComplexity _targetComplexities = TargetComplexity.All;

    private int _seed = 42;
    private int _minChildren = 0;
    private int _maxChildren = 10;

    [MenuItem("Tools/Benchmark Data Generator...")]
    public static void ShowWindow()
    {
        var window = GetWindow<DBGenerator>("Benchmark DB Generator");
        window.minSize = new Vector2(420f, 380f);
        window.Show();
    }

    [MenuItem("Tools/Generate All Benchmark Data (Refactored)")]
    public static void GenerateAllData()
    {
        BenchmarkDataGenerator.ExecuteGeneration();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Data Generator", "쿼리 벤치마크 데이터가 성공적으로 빌드되었습니다!", "OK");
    }

    [MenuItem("Tools/Generate All Benchmark Data (Including Legacy Formats)")]
    public static void GenerateAllDataIncludingLegacyFormats()
    {
        BenchmarkDataGenerator.ExecuteGeneration(true);
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Data Generator", "모든 포맷의 벤치마크 데이터가 성공적으로 빌드되었습니다!", "OK");
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("벤치마크 데이터 생성 설정", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            _targetFormats = (TargetFormat)EditorGUILayout.EnumFlagsField("대상 포맷 (Format)", _targetFormats);
            _targetShapes = (TargetShape)EditorGUILayout.EnumFlagsField("데이터 형태 (Shape)", _targetShapes);
            _targetScales = (TargetScale)EditorGUILayout.EnumFlagsField("데이터 스케일 (Scale)", _targetScales);
            _targetComplexities = (TargetComplexity)EditorGUILayout.EnumFlagsField("데이터 복잡도 (Complexity)", _targetComplexities);
        }

        EditorGUILayout.Space(6);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("DB 포맷만 선택"))
            {
                _targetFormats = TargetFormat.SQLite_NativeC | TargetFormat.SQLite_ORM | TargetFormat.RocksDB | TargetFormat.LiteDB;
            }
            if (GUILayout.Button("전체 선택"))
            {
                _targetFormats = TargetFormat.All;
                _targetShapes = TargetShape.All;
                _targetScales = TargetScale.All;
                _targetComplexities = TargetComplexity.All;
            }
            if (GUILayout.Button("선택 초기화"))
            {
                _targetFormats = TargetFormat.None;
                _targetShapes = TargetShape.None;
                _targetScales = TargetScale.None;
                _targetComplexities = TargetComplexity.None;
            }
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("생성 파라미터", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            _seed = EditorGUILayout.IntField("Random Seed", _seed);
            _minChildren = EditorGUILayout.IntField("Min Children (Document)", Mathf.Max(0, _minChildren));
            _maxChildren = EditorGUILayout.IntField("Max Children (Document)", Mathf.Max(_minChildren, _maxChildren));
        }

        EditorGUILayout.Space(8);
        DrawSelectionSummary();

        EditorGUILayout.Space(10);
        bool canGenerate = _targetFormats != TargetFormat.None
                        && _targetShapes != TargetShape.None
                        && _targetScales != TargetScale.None
                        && _targetComplexities != TargetComplexity.None;

        using (new EditorGUI.DisabledScope(!canGenerate))
        {
            if (GUILayout.Button("선택한 조건으로 데이터 생성", GUILayout.Height(36)))
            {
                RunCustomGeneration();
            }
        }
    }

    private void DrawSelectionSummary()
    {
        int shapeCount = CountBits((int)_targetShapes);
        int scaleCount = CountBits((int)_targetScales);
        int compCount = CountBits((int)_targetComplexities);
        int datasetCount = shapeCount * scaleCount * compCount;
        int builderCount = BenchmarkDataGenerator.ResolveBuilders(_targetFormats).Count;

        string msg = $"생성 예정 데이터셋: {datasetCount}종 (Shape {shapeCount} × Scale {scaleCount} × Complexity {compCount})\n"
                   + $"활성 빌더: {builderCount}개 (SQLite Native/ORM은 .db 파일 1개로 공유)";
        EditorGUILayout.HelpBox(msg, MessageType.Info);
    }

    private void RunCustomGeneration()
    {
        var request = new BenchmarkGenerationRequest
        {
            Formats = _targetFormats,
            Shapes = _targetShapes,
            Scales = _targetScales,
            Complexities = _targetComplexities,
            Seed = _seed,
            MinChildrenPerEntity = _minChildren,
            MaxChildrenPerEntity = _maxChildren
        };

        try
        {
            BenchmarkDataGenerator.ExecuteGeneration(request, (info, progress) =>
            {
                EditorUtility.DisplayProgressBar("Benchmark Data Generator", info, progress);
            });
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Data Generator", "선택한 벤치마크 데이터가 성공적으로 빌드되었습니다!", "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private static int CountBits(int value)
    {
        int count = 0;
        while (value != 0)
        {
            count += value & 1;
            value >>= 1;
        }
        return count;
    }
}
