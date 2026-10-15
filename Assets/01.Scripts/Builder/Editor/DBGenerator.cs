using UnityEditor;

public class DBGenerator : EditorWindow
{
    [MenuItem("Tools/Generate All Benchmark Data (Refactored)")]
    public static void GenerateAllData()
    {
        BenchmarkDataGenerator.ExecuteGeneration();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Data Generator", "모든 벤치마크 데이터가 SOLID 아키텍처 기반으로 성공적으로 빌드되었습니다!", "OK");
    }
}