using UnityEditor;

public class DBGenerator : EditorWindow
{
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
}
