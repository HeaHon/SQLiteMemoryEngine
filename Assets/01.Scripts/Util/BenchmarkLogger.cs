using System;
using System.IO;
using UnityEngine;

public static class BenchmarkLogger
{
    private static readonly string LogFolderPath = Path.Combine(Application.dataPath, "Logs");
    private static readonly string LogFilePath = Path.Combine(LogFolderPath, "Benchmark_Results.txt");

    /// <summary>
    /// 벤치마크 결과를 날짜/시간 스탬프와 함께 텍스트 파일에 추가합니다.
    /// </summary>
    /// <param name="formatType">SQL, JSON, SO, Binary 등 포맷 이름</param>
    /// <param name="scale">S100, S1K, S10K, S100K 등 데이터 규모</param>
    /// <param name="complexity">C1_Low, C2_Mid, C3_High 등 복잡도</param>
    /// <param name="elapsedMs">측정된 소요 시간 (ms)</param>
    /// <param name="allocatedBytes">측정된 메모리 할당량 (Bytes)</param>
    public static void AppendLog(string formatType, string scale, string complexity, double elapsedMs, long allocatedBytes = 0)
    {
        try
        {
            // 1. Logs 폴더가 없으면 자동 생성
            if (!Directory.Exists(LogFolderPath))
            {
                Directory.CreateDirectory(LogFolderPath);
            }

            // 2. 타임스탬프 생성 (예: [2026-10-01 16:30:00])
            string timeStamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            // 3. 로그 텍스트 포맷팅
            string logEntry = $"[{timeStamp}] [{formatType}] [{scale}] [{complexity}] | Execution Time: {elapsedMs:F3} ms | Memory: {allocatedBytes / 1024.0 / 1024.0:F2} MB\n";

            // 4. 기존 파일 내용 유지하며 하단에 추가 (StreamWriter append = true)
            File.AppendAllText(LogFilePath, logEntry, System.Text.Encoding.UTF8);

            Debug.Log($"<color=green>[BenchmarkLogger]</color> 로그 추가 완료: {logEntry.Trim()}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[BenchmarkLogger] 로그 저장 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 새로운 벤치마크 세션 시작 시 구분을 위한 헤더 라인 추가
    /// </summary>
    public static void LogSessionStart(string sessionName = "Benchmark Session")
    {
        if (!Directory.Exists(LogFolderPath))
        {
            Directory.CreateDirectory(LogFolderPath);
        }

        string header = $"\n======================================================\n" +
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] START: {sessionName}\n" +
                        $"======================================================\n";

        File.AppendAllText(LogFilePath, header, System.Text.Encoding.UTF8);
    }
}