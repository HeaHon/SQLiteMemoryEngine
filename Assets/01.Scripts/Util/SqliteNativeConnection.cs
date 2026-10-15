using System;
using System.Runtime.InteropServices;
using UnityEngine;

public class SqliteNativeConnection : IDisposable
{
    private IntPtr dbPtr = IntPtr.Zero;
    private bool isOpen = false;

    public bool IsOpen => isOpen;

    // --- P/Invoke 선언 (SQLite C API) ---
    [DllImport("e_sqlite3", EntryPoint = "sqlite3_open")]
    private static extern int sqlite3_open(string filename, out IntPtr db);

    [DllImport("e_sqlite3", EntryPoint = "sqlite3_close")]
    private static extern int sqlite3_close(IntPtr db);

    [DllImport("e_sqlite3", EntryPoint = "sqlite3_prepare_v2")]
    private static extern int sqlite3_prepare_v2(IntPtr db, string zSql, int nByte, out IntPtr ppStmt, IntPtr pzTail);

    [DllImport("e_sqlite3", EntryPoint = "sqlite3_step")]
    private static extern int sqlite3_step(IntPtr stmt);

    [DllImport("e_sqlite3", EntryPoint = "sqlite3_column_int")]
    private static extern int sqlite3_column_int(IntPtr stmt, int iCol);

    [DllImport("e_sqlite3", EntryPoint = "sqlite3_column_int64")]
    private static extern long sqlite3_column_int64(IntPtr stmt, int iCol);

    [DllImport("e_sqlite3", EntryPoint = "sqlite3_column_double")]
    private static extern double sqlite3_column_double(IntPtr stmt, int iCol);

    [DllImport("e_sqlite3", EntryPoint = "sqlite3_column_text")]
    private static extern IntPtr sqlite3_column_text(IntPtr stmt, int iCol);

    [DllImport("e_sqlite3", EntryPoint = "sqlite3_finalize")]
    private static extern int sqlite3_finalize(IntPtr stmt);

    private const int SQLITE_ROW = 100;
    private const int SQLITE_DONE = 101;

    public SqliteNativeConnection(string filePath)
    {
        int result = sqlite3_open(filePath, out dbPtr);
        if (result != 0)
        {
            Debug.LogError($"[SqliteNativeManager] DB 열기 실패: {filePath} (Error Code: {result})");
            isOpen = false;
        }
        else
        {
            isOpen = true;
        }
    }

    /// <label>안전한 SQL 쿼리 실행 및 데이터 바인딩 템플릿</label>
    public void ExecuteQuery(string query, Action<IntPtr> rowCallback)
    {
        if (!isOpen || dbPtr == IntPtr.Zero) return;

        IntPtr stmt = IntPtr.Zero;
        int prepareResult = sqlite3_prepare_v2(dbPtr, query, -1, out stmt, IntPtr.Zero);

        if (prepareResult != 0)
        {
            Debug.LogError($"[SqliteNativeManager] 쿼리 준비 실패: {query}");
            return;
        }

        try
        {
            while (sqlite3_step(stmt) == SQLITE_ROW)
            {
                rowCallback?.Invoke(stmt);
            }
        }
        finally
        {
            sqlite3_finalize(stmt);
        }
    }

    // 컬럼 값 추출 헬퍼 메서드들
    public static int GetInt(IntPtr stmt, int colIndex) => sqlite3_column_int(stmt, colIndex);
    public static long GetLong(IntPtr stmt, int colIndex) => sqlite3_column_int64(stmt, colIndex);
    public static double GetDouble(IntPtr stmt, int colIndex) => sqlite3_column_double(stmt, colIndex);

    public static string GetString(IntPtr stmt, int colIndex)
    {
        IntPtr ptr = sqlite3_column_text(stmt, colIndex);
        return ptr == IntPtr.Zero ? string.Empty : Marshal.PtrToStringAnsi(ptr);
    }

    public void Dispose()
    {
        if (isOpen && dbPtr != IntPtr.Zero)
        {
            sqlite3_close(dbPtr);
            dbPtr = IntPtr.Zero;
            isOpen = false;
        }
    }
}