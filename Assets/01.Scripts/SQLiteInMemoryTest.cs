using System;
using System.Runtime.InteropServices;
using UnityEngine;

public class SQLiteInMemoryTest : MonoBehaviour
{
    #region SQLite C API (P/Invoke)

    [DllImport("sqlite3", EntryPoint = "sqlite3_open", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_open([MarshalAs(UnmanagedType.LPStr)] string filename, out IntPtr db);

    [DllImport("sqlite3", EntryPoint = "sqlite3_close", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_close(IntPtr db);

    [DllImport("sqlite3", EntryPoint = "sqlite3_exec", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_exec(IntPtr db, [MarshalAs(UnmanagedType.LPStr)] string sql, IntPtr callback, IntPtr arg, out IntPtr errmsg);

    // SQLite Backup API (인메모리 <-> 디스크 DB 동기화 핵심 함수)
    [DllImport("sqlite3", EntryPoint = "sqlite3_backup_init", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_backup_init(IntPtr destDb, [MarshalAs(UnmanagedType.LPStr)] string destName, IntPtr sourceDb, [MarshalAs(UnmanagedType.LPStr)] string sourceName);

    [DllImport("sqlite3", EntryPoint = "sqlite3_backup_step", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_backup_step(IntPtr backup, int pages);

    [DllImport("sqlite3", EntryPoint = "sqlite3_backup_finish", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_backup_finish(IntPtr backup);

    private const int SQLITE_OK = 0;
    private const int SQLITE_DONE = 101;

    // 콜백 함수 포인터 어트리뷰트 (IL2CPP 호환용)
    private delegate int SQLiteCallback(IntPtr arg, int colCount, IntPtr colValues, IntPtr colNames);

    #endregion

    private string diskDbPath;

    private void Start()
    {
        // 1. 디스크 DB 경로 설정 (persistentDataPath 사용 - 없으면 sqlite3_open이 자동 생성함)
        //diskDbPath = $"{Application.persistentDataPath}/GameData.db";
        diskDbPath = $"{Application.streamingAssetsPath}/GameData.db";
        Debug.Log($"[SQLite] 디스크 DB 경로: {diskDbPath}");

        // 2. 파이프라인 실행: 디스크 -> RAM 로드 -> RAM 데이터 조작 -> RAM -> 디스크 저장
        RunInMemoryWorkflow();
    }

    private void RunInMemoryWorkflow()
    {
        IntPtr diskDb = IntPtr.Zero;
        IntPtr memoryDb = IntPtr.Zero;

        try
        {
            // --- STEP 1: 디스크 DB 및 인메모리 DB 오픈 ---
            if (sqlite3_open(diskDbPath, out diskDb) != SQLITE_OK)
            {
                Debug.LogError("[SQLite] 디스크 DB 오픈 실패");
                return;
            }

            if (sqlite3_open(":memory:", out memoryDb) != SQLITE_OK)
            {
                Debug.LogError("[SQLite] 인메모리 DB 생성 실패");
                return;
            }

            // --- STEP 2: 디스크 DB 데이터를 인메모리 DB로 로드 (Copy Disk -> RAM) ---
            CopyDatabase(diskDb, memoryDb);
            Debug.Log("[SQLite] Step 1 & 2 완료: 디스크 DB를 인메모리(RAM)로 로드함.");

            // --- STEP 3: 인메모리 DB에서 CRUD 쿼리 테스트 ---
            ExecuteInMemoryQueries(memoryDb);

            // --- STEP 4: 작업 완료된 인메모리 DB를 다시 디스크 DB로 저장 (Copy RAM -> Disk) ---
            CopyDatabase(memoryDb, diskDb);
            Debug.Log("[SQLite] Step 4 완료: 인메모리 변경사항을 디스크 DB로 동기화(저장) 완료!");
        }
        finally
        {
            // --- STEP 5: DB 핸들 닫기 및 RAM 메모리 해제 ---
            if (memoryDb != IntPtr.Zero) sqlite3_close(memoryDb);
            if (diskDb != IntPtr.Zero) sqlite3_close(diskDb);
            Debug.Log("[SQLite] DB 연결 종료 및 인메모리 리소스 해제 완료.");
        }
    }

    /// <summary>
    /// SQLite Backup API를 이용해 DB간 전체 데이터 복사 (Disk <-> RAM)
    /// </summary>
    private void CopyDatabase(IntPtr sourceDb, IntPtr destDb)
    {
        IntPtr backup = sqlite3_backup_init(destDb, "main", sourceDb, "main");
        if (backup != IntPtr.Zero)
        {
            sqlite3_backup_step(backup, -1); // -1: 전체 페이지 한 번에 복사
            sqlite3_backup_finish(backup);
        }
        else
        {
            Debug.LogError("[SQLite] DB 복사(Backup) 실패!");
        }
    }

    /// <summary>
    /// 인메모리 DB 상에서 SQL CRUD 테스트 수행
    /// </summary>
    private void ExecuteInMemoryQueries(IntPtr db)
    {
        // 1. 테이블 생성 (없는 경우)
        string createTableSql = @"
            CREATE TABLE IF NOT EXISTS PlayerCards (
                CardId INTEGER PRIMARY KEY AUTOINCREMENT,
                CardName TEXT NOT NULL,
                AttackPower INTEGER NOT NULL,
                Cost INTEGER NOT NULL
            );";
        ExecuteSql(db, createTableSql, "테이블 생성");

        // 2. 데이터 추가 (INSERT)
        string insertSql = @"
            INSERT INTO PlayerCards (CardName, AttackPower, Cost) VALUES ('Fireball', 12, 3);
            INSERT INTO PlayerCards (CardName, AttackPower, Cost) VALUES ('Ice Shield', 0, 2);
            INSERT INTO PlayerCards (CardName, AttackPower, Cost) VALUES ('Slash', 6, 1);";
        ExecuteSql(db, insertSql, "게임 데이터(카드 3종) 추가");

        // 3. 데이터 조회 (SELECT) - 수정을 거치기 전 조회
        Debug.Log("=== [초기 데이터 조회] ===");
        string selectAllSql = "SELECT * FROM PlayerCards;";
        ExecuteSelectSql(db, selectAllSql);

        // 4. 데이터 수정 (UPDATE) - Fireball 카드의 데미지를 12 -> 18로 강화
        string updateSql = "UPDATE PlayerCards SET AttackPower = 18 WHERE CardName = 'Fireball';";
        ExecuteSql(db, updateSql, "Fireball 카드 공격력 강화 (UPDATE)");

        // 5. 조건별 조회 (SELECT + WHERE) - 공격력 5 이상인 카드만 검색
        Debug.Log("=== [공격력 5 이상 카드 검색 (WHERE)] ===");
        string selectWhereSql = "SELECT CardId, CardName, AttackPower FROM PlayerCards WHERE AttackPower >= 5;";
        ExecuteSelectSql(db, selectWhereSql);
    }

    #region Helper Methods (SQL Execution)

    private void ExecuteSql(IntPtr db, string sql, string logMsg)
    {
        int result = sqlite3_exec(db, sql, IntPtr.Zero, IntPtr.Zero, out IntPtr errMsgPtr);
        if (result == SQLITE_OK)
        {
            Debug.Log($"[SQLite Success] {logMsg}");
        }
        else
        {
            string error = Marshal.PtrToStringAnsi(errMsgPtr);
            Debug.LogError($"[SQLite Error] {logMsg}: {error}");
        }
    }

    private void ExecuteSelectSql(IntPtr db, string sql)
    {
        // SELECT 조회 결과를 콘솔 로그로 출력하기 위한 콜백 함수
        SQLiteCallback callback = (arg, colCount, colValues, colNames) =>
        {
            string rowStr = "";
            for (int i = 0; i < colCount; i++)
            {
                string colName = Marshal.PtrToStringAnsi(Marshal.ReadIntPtr(colNames, i * IntPtr.Size));
                string colVal = Marshal.PtrToStringAnsi(Marshal.ReadIntPtr(colValues, i * IntPtr.Size));
                rowStr += $"[{colName}: {colVal}] ";
            }
            Debug.Log($"  > {rowStr}");
            return 0;
        };

        IntPtr callbackPtr = Marshal.GetFunctionPointerForDelegate(callback);
        sqlite3_exec(db, sql, callbackPtr, IntPtr.Zero, out _);
    }

    #endregion
}