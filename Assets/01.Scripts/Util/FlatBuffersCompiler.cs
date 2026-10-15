using System.IO;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;

public class FlatBuffersCompilerWindow : EditorWindow
{
    private string flatcPath = "Assets/Tools/FlatBuffers/flatc.exe";
    private string schemaPath = "Assets/GameData/Schemas/GameData.fbs";
    private string outputDir = "Assets/GameData/Generated";

    [MenuItem("Tools/FlatBuffers/Open Compiler Window")]
    public static void ShowWindow()
    {
        GetWindow<FlatBuffersCompilerWindow>("FlatBuffers Compiler");
    }

    private void OnEnable()
    {
        // 기본 프로젝트 경로 자동 보정
        if (string.IsNullOrEmpty(flatcPath))
            flatcPath = Path.Combine(Application.dataPath, "Tools/FlatBuffers/flatc.exe");
        if (string.IsNullOrEmpty(schemaPath))
            schemaPath = Path.Combine(Application.dataPath, "GameData/Schemas/GameData.fbs");
        if (string.IsNullOrEmpty(outputDir))
            outputDir = Path.Combine(Application.dataPath, "GameData/Generated");
    }

    private void OnGUI()
    {
        GUILayout.Label("FlatBuffers 스키마 컴파일러 설정", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // 1. flatc.exe 경로 설정
        EditorGUILayout.BeginHorizontal();
        flatcPath = EditorGUILayout.TextField("Flatc Path", flatcPath);
        if (GUILayout.Button("찾기", GUILayout.Width(50)))
        {
            string selected = EditorUtility.OpenFilePanel("Select flatc.exe", "Assets", "exe");
            if (!string.IsNullOrEmpty(selected))
            {
                if (selected.StartsWith(Application.dataPath))
                    flatcPath = "Assets" + selected.Substring(Application.dataPath.Length);
                else
                    flatcPath = selected;
            }
        }
        EditorGUILayout.EndHorizontal();

        // 2. .fbs 스키마 파일 경로 설정
        EditorGUILayout.BeginHorizontal();
        schemaPath = EditorGUILayout.TextField("Schema (.fbs) Path", schemaPath);
        if (GUILayout.Button("찾기", GUILayout.Width(50)))
        {
            string selected = EditorUtility.OpenFilePanel("Select Schema File", "Assets", "fbs");
            if (!string.IsNullOrEmpty(selected))
            {
                if (selected.StartsWith(Application.dataPath))
                    schemaPath = "Assets" + selected.Substring(Application.dataPath.Length);
                else
                    schemaPath = selected;
            }
        }
        EditorGUILayout.EndHorizontal();

        // 3. 출력 디렉토리 경로 설정
        EditorGUILayout.BeginHorizontal();
        outputDir = EditorGUILayout.TextField("Output Directory", outputDir);
        if (GUILayout.Button("찾기", GUILayout.Width(50)))
        {
            string selected = EditorUtility.OpenFolderPanel("Select Output Directory", "Assets", "");
            if (!string.IsNullOrEmpty(selected))
            {
                if (selected.StartsWith(Application.dataPath))
                    outputDir = "Assets" + selected.Substring(Application.dataPath.Length);
                else
                    outputDir = selected;
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(20);

        // 4. 컴파일 실행 버튼
        if (GUILayout.Button("Compile Schema to C# (-n)", GUILayout.Height(40)))
        {
            ExecuteCompile();
        }
    }

    private void ExecuteCompile()
    {
        string absoluteFlatc = Path.GetFullPath(flatcPath);
        string absoluteSchema = Path.GetFullPath(schemaPath);
        string absoluteOutput = Path.GetFullPath(outputDir);

        if (!File.Exists(absoluteFlatc))
        {
            UnityEngine.Debug.LogError($"[FlatBuffers] flatc.exe 파일을 찾을 수 없습니다: {absoluteFlatc}");
            return;
        }

        if (!File.Exists(absoluteSchema))
        {
            UnityEngine.Debug.LogError($"[FlatBuffers] 스키마 파일을 찾을 수 없습니다: {absoluteSchema}");
            return;
        }

        if (!Directory.Exists(absoluteOutput))
        {
            Directory.CreateDirectory(absoluteOutput);
        }

        // -n 옵션을 사용하여 C# 코드 생성
        string arguments = $"-n -o \"{absoluteOutput}\" \"{absoluteSchema}\"";

        var startInfo = new ProcessStartInfo
        {
            FileName = absoluteFlatc,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using (var process = Process.Start(startInfo))
        {
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode == 0)
            {
                UnityEngine.Debug.Log($"<color=green>[FlatBuffers] 컴파일 성공!</color>\n{output}");
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("성공", "FlatBuffers 스키마가 C# 코드로 성공적으로 컴파일되었습니다.", "OK");
            }
            else
            {
                UnityEngine.Debug.LogError($"[FlatBuffers] 컴파일 실패 (ExitCode: {process.ExitCode})\n{error}");
                EditorUtility.DisplayDialog("실패", $"컴파일 중 에러가 발생했습니다.\n{error}", "OK");
            }
        }
    }
}