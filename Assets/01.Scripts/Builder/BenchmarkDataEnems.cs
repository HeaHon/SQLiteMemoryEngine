using System;

public enum DataScale
{
    S100 = 100,
    S1K = 1000,
    S10K = 10000,
    S100K = 100000
}

public enum DataComplexity
{
    C1_Low,
    C2_Mid,
    C3_High
}

public enum DataShape
{
    Relational, // 정규화 테이블 구조 (SQL 유리)
    Document,   // 중첩 문서/임베디드 구조 (Document NoSQL 유리)
    Sparse      // 가변/다형성 스키마 구조 (Schema-less NoSQL 유리)
}

[Flags]
public enum TargetFormat
{
    None = 0,
    SQLite_NativeC = 1 << 0,
    SQLite_ORM = 1 << 1,
    RocksDB = 1 << 2,
    LiteDB = 1 << 3,
    FlatBuffers = 1 << 4,
    RawBinary = 1 << 5,
    MessagePack = 1 << 6,
    JSON = 1 << 7,
    All = SQLite_NativeC | SQLite_ORM | RocksDB | LiteDB | FlatBuffers | RawBinary | MessagePack | JSON
}

[Flags]
public enum TargetScale
{
    None = 0,
    S100 = 1 << 0,
    S1K = 1 << 1,
    S10K = 1 << 2,
    S100K = 1 << 3,
    All = S100 | S1K | S10K | S100K
}

[Flags]
public enum TargetComplexity
{
    None = 0,
    C1_Low = 1 << 0,
    C2_Mid = 1 << 1,
    C3_High = 1 << 2,
    All = C1_Low | C2_Mid | C3_High
}

[Flags]
public enum TargetShape
{
    None = 0,
    Relational = 1 << 0,
    Document = 1 << 1,
    Sparse = 1 << 2,
    All = Relational | Document | Sparse
}