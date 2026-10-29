using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

public class SqliteNativeConnection : IDisposable
{
    private IntPtr dbPtr = IntPtr.Zero;
    private bool isOpen;

    public bool IsOpen => isOpen;
    public string LastError { get; private set; }

    [DllImport("e_sqlite3", EntryPoint = "sqlite3_open")]
    private static extern int sqlite3_open(string filename, out IntPtr db);

    [DllImport("e_sqlite3", EntryPoint = "sqlite3_close")]
    private static extern int sqlite3_close(IntPtr db);

    [DllImport("e_sqlite3", EntryPoint = "sqlite3_prepare_v2")]
    private static extern int sqlite3_prepare_v2(IntPtr db, string zSql, int nByte, out IntPtr ppStmt, IntPtr pzTail);

    [DllImport("e_sqlite3", EntryPoint = "sqlite3_step")]
    private static extern int sqlite3_step(IntPtr stmt);

    [DllImport("e_sqlite3", EntryPoint = "sqlite3_column_count")]
    private static extern int sqlite3_column_count(IntPtr stmt);

    [DllImport("e_sqlite3", EntryPoint = "sqlite3_column_name")]
    private static extern IntPtr sqlite3_column_name(IntPtr stmt, int N);

    [DllImport("e_sqlite3", EntryPoint = "sqlite3_column_type")]
    private static extern int sqlite3_column_type(IntPtr stmt, int iCol);

    [DllImport("e_sqlite3", EntryPoint = "sqlite3_column_int")]
    private static extern int sqlite3_column_int(IntPtr stmt, int iCol);

    [DllImport("e_sqlite3", EntryPoint = "sqlite3_column_int64")]
    private static extern long sqlite3_column_int64(IntPtr stmt, int iCol);

    [DllImport("e_sqlite3", EntryPoint = "sqlite3_column_double")]
    private static extern double sqlite3_column_double(IntPtr stmt, int iCol);

    [DllImport("e_sqlite3", EntryPoint = "sqlite3_column_text")]
    private static extern IntPtr sqlite3_column_text(IntPtr stmt, int iCol);

    [DllImport("e_sqlite3", EntryPoint = "sqlite3_column_bytes")]
    private static extern int sqlite3_column_bytes(IntPtr stmt, int iCol);

    [DllImport("e_sqlite3", EntryPoint = "sqlite3_column_blob")]
    private static extern IntPtr sqlite3_column_blob(IntPtr stmt, int iCol);

    [DllImport("e_sqlite3", EntryPoint = "sqlite3_finalize")]
    private static extern int sqlite3_finalize(IntPtr stmt);

    [DllImport("e_sqlite3", EntryPoint = "sqlite3_errmsg")]
    private static extern IntPtr sqlite3_errmsg(IntPtr db);

    [DllImport("e_sqlite3", EntryPoint = "sqlite3_bind_int")]
    private static extern int sqlite3_bind_int(IntPtr stmt, int index, int value);

    [DllImport("e_sqlite3", EntryPoint = "sqlite3_bind_int64")]
    private static extern int sqlite3_bind_int64(IntPtr stmt, int index, long value);

    [DllImport("e_sqlite3", EntryPoint = "sqlite3_bind_double")]
    private static extern int sqlite3_bind_double(IntPtr stmt, int index, double value);

    [DllImport("e_sqlite3", EntryPoint = "sqlite3_bind_text")]
    private static extern int sqlite3_bind_text(IntPtr stmt, int index, string value, int length, IntPtr destructor);

    [DllImport("e_sqlite3", EntryPoint = "sqlite3_bind_null")]
    private static extern int sqlite3_bind_null(IntPtr stmt, int index);

    private const int SQLITE_ROW = 100;
    private const int SQLITE_DONE = 101;

    public SqliteNativeConnection(string filePath)
    {
        int result = sqlite3_open(filePath, out dbPtr);
        if (result != 0)
        {
            LastError = GetErrorMessage();
            Debug.LogError($"[SqliteNativeManager] DB 열기 실패: {filePath} ({LastError}, Error Code: {result})");
            if (dbPtr != IntPtr.Zero) sqlite3_close(dbPtr);
            dbPtr = IntPtr.Zero;
            return;
        }

        isOpen = true;
    }

    public bool ExecuteQuery(string query, Action<IntPtr> rowCallback)
    {
        return ExecuteQuery(query, null, rowCallback);
    }

    public bool ExecuteQuery(string query, object[] parameters, Action<IntPtr> rowCallback)
    {
        if (!isOpen || dbPtr == IntPtr.Zero)
        {
            LastError = "SQLite connection is not open.";
            return false;
        }

        IntPtr stmt = IntPtr.Zero;
        int prepareResult = sqlite3_prepare_v2(dbPtr, query, -1, out stmt, IntPtr.Zero);
        if (prepareResult != 0)
        {
            LastError = GetErrorMessage();
            Debug.LogError($"[SqliteNativeManager] 쿼리 준비 실패: {LastError} (Error Code: {prepareResult})");
            return false;
        }

        try
        {
            if (!BindParameters(stmt, parameters)) return false;

            while (true)
            {
                int stepResult = sqlite3_step(stmt);
                if (stepResult == SQLITE_ROW)
                {
                    rowCallback?.Invoke(stmt);
                    continue;
                }

                if (stepResult == SQLITE_DONE) return true;

                LastError = GetErrorMessage();
                Debug.LogError($"[SqliteNativeManager] 쿼리 실행 실패: {LastError} (Error Code: {stepResult})");
                return false;
            }
        }
        finally
        {
            sqlite3_finalize(stmt);
        }
    }

    private bool BindParameters(IntPtr stmt, object[] parameters)
    {
        if (parameters == null) return true;

        for (int i = 0; i < parameters.Length; i++)
        {
            int bindResult;
            object parameter = parameters[i];
            if (parameter == null)
            {
                bindResult = sqlite3_bind_null(stmt, i + 1);
            }
            else if (parameter is int || parameter is short || parameter is byte || parameter is bool)
            {
                bindResult = sqlite3_bind_int(stmt, i + 1, Convert.ToInt32(parameter, CultureInfo.InvariantCulture));
            }
            else if (parameter is long)
            {
                bindResult = sqlite3_bind_int64(stmt, i + 1, (long)parameter);
            }
            else if (parameter is float || parameter is double || parameter is decimal)
            {
                bindResult = sqlite3_bind_double(stmt, i + 1, Convert.ToDouble(parameter, CultureInfo.InvariantCulture));
            }
            else
            {
                string text = Convert.ToString(parameter, CultureInfo.InvariantCulture);
                bindResult = sqlite3_bind_text(stmt, i + 1, text, -1, new IntPtr(-1));
            }

            if (bindResult != 0)
            {
                LastError = GetErrorMessage();
                return false;
            }
        }

        return true;
    }

    private string GetErrorMessage()
    {
        if (dbPtr == IntPtr.Zero) return "Unknown SQLite error.";
        IntPtr errorPtr = sqlite3_errmsg(dbPtr);
        return errorPtr == IntPtr.Zero ? "Unknown SQLite error." : Marshal.PtrToStringAnsi(errorPtr);
    }

    public static int GetColumnCount(IntPtr stmt) => sqlite3_column_count(stmt);

    public static string GetColumnName(IntPtr stmt, int colIndex)
    {
        IntPtr ptr = sqlite3_column_name(stmt, colIndex);
        return ptr == IntPtr.Zero ? string.Empty : Marshal.PtrToStringAnsi(ptr);
    }

    public static int GetColumnType(IntPtr stmt, int colIndex) => sqlite3_column_type(stmt, colIndex);
    public static int GetInt(IntPtr stmt, int colIndex) => sqlite3_column_int(stmt, colIndex);
    public static long GetLong(IntPtr stmt, int colIndex) => sqlite3_column_int64(stmt, colIndex);
    public static double GetDouble(IntPtr stmt, int colIndex) => sqlite3_column_double(stmt, colIndex);

    public static string GetString(IntPtr stmt, int colIndex)
    {
        IntPtr ptr = sqlite3_column_text(stmt, colIndex);
        if (ptr == IntPtr.Zero) return null;

        int byteCount = sqlite3_column_bytes(stmt, colIndex);
        byte[] bytes = new byte[byteCount];
        Marshal.Copy(ptr, bytes, 0, byteCount);
        return Encoding.UTF8.GetString(bytes);
    }

    public static object GetValue(IntPtr stmt, int colIndex)
    {
        switch (sqlite3_column_type(stmt, colIndex))
        {
            case 1: return sqlite3_column_int64(stmt, colIndex);
            case 2: return sqlite3_column_double(stmt, colIndex);
            case 3: return GetString(stmt, colIndex);
            case 4:
                IntPtr blob = sqlite3_column_blob(stmt, colIndex);
                int length = sqlite3_column_bytes(stmt, colIndex);
                if (blob == IntPtr.Zero || length <= 0) return new byte[0];
                byte[] bytes = new byte[length];
                Marshal.Copy(blob, bytes, 0, length);
                return bytes;
            default: return null;
        }
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
