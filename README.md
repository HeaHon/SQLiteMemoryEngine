# SQLiteMemoryEngine
SQLite In-Memory DB 및 로컬 스토리지 엔진 기반 게임 데이터 아키텍처 연구

---

## 1. DB 명명 및 경로 규칙 (Standard Naming Convention)

실험용 데이터셋 파일 이름만 보고도 **[용도]**, **[데이터 양]**, **[복잡도 레벨]**, **[데이터 형태]** 를 즉시 식별할 수 있도록 아래 표준 규격을 따릅니다.

### 명명 포맷 (Naming Pattern)

* **파일명:** `GameData_[Scale]_[Complexity]_[Shape].[ext]`
* **저장 경로:** `StreamingAssets/[Format]/[Shape]/[Scale]/GameData_[Scale]_[Complexity]_[Shape].[ext]`
  * *RocksDB는 디렉토리 단위(`GameData_[Scale]_[Complexity]_[Shape]/`)로 저장*

### 구성 요소 명세 (Specification)

#### 1. 데이터 양 (Data Scale)
| 식별자 | 기준 건수 | 비고 |
| :--- | :--- | :--- |
| **`S100`** | ~100건 | 극소규모 테스트 / 단위 실험 |
| **`S1K`** | ~1,000건 (1K) | 소규모 테스트 |
| **`S10K`** | ~10,000건 (10K) | 중규모 성능 테스트 |
| **`S100K`** | ~100,000건 (100K) | 대규모 벤치마크 테스트 |

#### 2. 데이터 복잡도 (Data Complexity)
| 식별자 | 정의 | 특징 및 조건 |
| :--- | :--- | :--- |
| **`C1_Low`** | 단순 구조 | 단일/단순 엔티티, 참조 깊이 0~1 |
| **`C2_Mid`** | 중간 연관 구조 | 1:N / N:M 매핑, 2~3개 관계 탐색(JOIN/중첩), 참조 깊이 2 |
| **`C3_High`** | 고복잡도 구조 | 다중 FK(거미줄) 또는 3단계 이상 깊은 중첩, 팬아웃 유발, 참조 깊이 3 이상 |

#### 3. 데이터 형태 (Data Shape)
SQL과 NoSQL의 구조적 유불리를 공정하게 대조하기 위해 동일한 `Scale × Complexity` 축 위에 3가지 데이터 형태를 분리합니다.

| 식별자 | 구조 유형 | 특징 및 대조 목적 |
| :--- | :--- | :--- |
| **`Relational`** | 정규화 관계형 | 엔티티별 평면 테이블 분리 + FK 연결 (SQL RDB 유리) |
| **`Document`** | 비정규화 중첩 문서형 | 루트 문서 내부에 하위 배열/객체 임베딩 (Document NoSQL 유리) |
| **`Sparse`** | 가변/다형성 스키마형 | 타입별 상이한 동적 속성을 `Dictionary`로 보유 (Schema-less NoSQL 유리) |

### 파일명 예시 (Examples)

* `GameData_S100_C1_Low_Relational.db` : 100건 규모의 단순 정규화 관계형 DB
* `GameData_S10K_C2_Mid_Document.db` : 10,000건 규모의 2단계 중첩 문서형 DB
* `GameData_S100K_C3_High_Sparse.db` : 100,000건 규모의 고복잡도 가변 스키마 DB

---

## 2. 데이터 모델 구조 상세 (Data Models)

### 2.1 `RelDataModels` — 정규화 관계형 (SQL 유리)
중복을 제거하고 엔티티를 테이블 단위로 분리해 외래키(FK)로 연결한 표준 RDB 모델입니다.

| 복잡도 | 포함된 테이블 (`Rel*`) | 관계 구조 |
| :--- | :--- | :--- |
| **`C1_Low`** | `RelMasterItem_C1`, `RelRuntimeCharacter_C1` | 독립 단일 테이블 2종 |
| **`C2_Mid`** | `RelMasterItem_C2`, `RelItemTag_C2`, `RelRuntimeCharacter_C2`, `RelRuntimeInventory_C2` | 아이템–태그(1:N), 캐릭터–인벤토리–아이템(3테이블 JOIN) |
| **`C3_High`** | `RelSystemUser_C3`, `RelRuntimeCharacter_C3`, `RelRuntimeInventory_C3`, `RelMasterItem_C3`, `RelItemEffectLink_C3`, `RelMasterEffect_C3` | 유저 → 캐릭터 → 인벤토리 → 아이템 ↔ 이펙트 (6테이블 거미줄 FK) |

### 2.2 `DocDataModels` — 비정규화 중첩 문서형 (Document NoSQL 유리)
`RelDataModels`를 비정규화하여, 루트 문서 1건 안에 하위 목록과 참조 정보를 그대로 임베딩한 모델입니다.

| 복잡도 | 루트 문서 및 중첩 구조 (`Doc*`) | 저장 방식 비교 (`LiteDB` vs `SQLite`) |
| :--- | :--- | :--- |
| **`C1_Low`** | `DocItem_C1` (`List<string> tags` 내장) | **LiteDB**: 단일 문서 저장 / **SQLite**: `Doc_Item_C1` + `Doc_Item_Tag_C1` 분해 |
| **`C2_Mid`** | `DocCharacter_C2` → `List<DocInventoryEntry_C2>` (`tags` 내장) | **LiteDB**: 캐릭터 1건에 인벤토리 임베딩 / **SQLite**: 3개 테이블 분해 |
| **`C3_High`** | `DocUser_C3` → `List<DocCharacter_C3>` → `List<DocInventoryItem_C3>` → `List<DocEffect_C3>` | **LiteDB**: 유저 1건에 3단 트리 임베딩 / **SQLite**: 4개 테이블 분해 |

### 2.3 `SparseDataModels` — 가변/다형성 스키마형 (Schema-less NoSQL 유리)
레코드의 타입(`item_type`, `category`, `event_type`)에 따라 보유 필드 구성이 달라지는 비정형 모델입니다.

| 복잡도 | 모델 및 가변 필드 (`Sparse*`) | 저장 방식 비교 (`LiteDB`·`RocksDB` vs `SQLite`) |
| :--- | :--- | :--- |
| **`C1_Low`** | `SparseItem_C1` (`WEAPON`·`ARMOR`·`CONSUMABLE`·`MATERIAL`별 상이한 `attributes`) | **NoSQL**: BSON/JSON 동적 `Dictionary` 그대로 저장<br>**SQLite**: `Sparse_Item_C1` + `Sparse_Attr` (EAV 패턴) 분해 |
| **`C2_Mid`** | `SparseEntity_C2` (`HERO`·`MONSTER`·`NPC`별 상이한 `stats`, `metadata`) | **NoSQL**: 동적 딕셔너리 2종 내장<br>**SQLite**: `Sparse_Entity_C2` + `Sparse_Attr` + `Sparse_Meta` 분해 |
| **`C3_High`** | `SparseEventLog_C3` (`COMBAT`·`GACHA`·`TRADE`·`QUEST`별 상이한 `payload`) | **NoSQL**: JSON 페이로드 + `sparse_ev_by_user_` 프리픽스 키<br>**SQLite**: `Sparse_Event_C3` + `Sparse_Event_Payload` 분해 |

---

## 3. 벤치마크 DB 라인업 (Common DB Lineup)

연구안 A(조회), B(인메모리 Hot/Cold-Path), C(상태 저장 및 롤백) 전반에서 공통으로 비교·검증하는 데이터베이스 및 인메모리 저장소 라인업입니다.

| 분류 | 비교 엔진 | 적용 연구안 | 특징 및 상용 게임 채택 이유 |
| :--- | :--- | :--- | :--- |
| **Relational DB** | **SQLite (Native C)** | A, B, C | C API(`e_sqlite3`) 직접 P/Invoke 매핑 (Zero-Alloc 목표 SQL) |
| | **SQLite (sqlite-net ORM)** | A, B, C | ORM 편의성 대가로 발생하는 Reflection·객체 할당 오버헤드 측정 |
| **NoSQL DB** | **RocksDB (KV Store)** | A, C | SSTable + Bloom Filter 기반 고성능 로컬 KV 엔진 |
| | **LiteDB (Document DB)** | A, B, C | C# 순수 BSON Document DB (파일 및 `MemoryStream` 인메모리 지원) |
| **In-Memory Baseline** | **C# Collection (`Dictionary` + `LINQ`)** | B, C | 기존 게임 클라이언트 인메모리 관리 방식 (조건 검색 GC 및 롤백 한계 대조) |

---

## 4. 연구 A안: Master Data 로드 및 조회 성능 벤치마크

### 연구 개요 및 조회 유형
* **연구 목적:** 대용량 읽기 전용 Master Data의 **전체 로드(Bulk Load)** 및 **부분/조건 조회(Query Lookup)** 시 DB/포맷별 실행 시간과 C# 힙 메모리 할당량을 정량적으로 비교 검증합니다.
* **측정 시나리오:**
  * **Bulk Load:** 선택된 `Shape × Scale × Complexity` 데이터셋 전체를 C# 인메모리 컨테이너로 적재
  * **Case A (PK 단건 조회):** Primary Key 기반 1:1 직접 조회
  * **Case B (범위/복합 조건 조회):** 인덱스 컬럼 기반 범위 및 다중 `AND` 필터링
  * **Case C (연쇄/트리 조회):** `Relational`은 다중 `JOIN`, `Document`는 중첩 하위 트리 조회
  * **Case D (부분 필드 추출):** Projection을 통한 단일 컬럼 추출

### 연구 A안 대조군 (Comparison Lineup)
| 구분 | 포맷 / 엔진 | 상태 | 역할 및 측정 초점 |
| :--- | :--- | :--- | :--- |
| **실험군 (SQL)** | **SQLite (Native C)** | 활성 | P/Invoke + Prepared Statement 기반 Zero-Alloc 성능 상한선 검증 |
| | **SQLite (sqlite-net ORM)** | 활성 | Reflection·객체 할당 오버헤드 대조 및 정합성 기준(Baseline) |
| **대조군 (NoSQL DB)** | **RocksDB (KV Store)** | 활성 | 단건 PK 및 보조 프리픽스 조회 성능 대조 |
| | **LiteDB (Document)** | 활성 | 중첩 문서(`Document`) 및 동적 스키마(`Sparse`) 조회 대조 |
| **대조군 (비-DB 직렬화)** | **FlatBuffers / MessagePack / Raw Binary / JSON** | 보류 | 읽기 전용 직렬화 대조군 (별도 조회 최적화 후 합류 예정) |

### 정밀 측정 프로토콜 (왜곡 방지 설계)
단순 1회 실행 시 발생하는 초기화·박싱·콜드 캐시 왜곡을 제거하기 위해 `IQueryBenchmarkSession` 기반의 2단계 실행 구조를 사용합니다.

1. **사전 정확도 검증 (`ExecuteForVerification`)**: 측정 루프 밖에서 1회 실행해 `QueryResult.IsEquivalentTo`로 기준 결과(`SQLite ORM`)와의 데이터 일치를 검증합니다. (`Document`/`Sparse`는 검증 시에만 `DocumentUnwindHelper`로 중첩 필드를 평면화하여 공통 스칼라 컬럼 기준으로 비교)
2. **커넥션 및 컴파일 분리 (`Prepare`)**: DB 파일 오픈, `sqlite3_prepare_v2`, `BsonExpression` 빌드, RocksDB 키 `byte[]` 인코딩을 측정 루프 밖에서 완료합니다.
3. **반복 측정 및 할당량 산출 (`ExecuteNoAlloc`)**:
   * **Warmup 5회** 후 **100회 반복** 실행하여 `Avg`, `Min`, `P95` (상위 5% 이상치 제외 상한), `Max (ms)` 산출
   * `Dictionary<string, object>` 생성과 박싱을 배제한 네이티브 경로로 `GC.GetAllocatedBytesForCurrentThread()` 기준 **1회당 순수 할당 바이트(`Bytes/op`)** 측정

---

## 5. 연구 B안: Cold-Path & Hot-Path 조건 검색 및 수정 (In-Memory DB 단독 관리)

### 연구 개요 및 목적
* **연구 목적:** 디스크 파일이 아닌 **In-Memory 상주 상태**에서 게임 런타임의 실시간 읽기·쓰기 혼합 연산(상태 갱신, 조건 검색, 트랜잭션)을 **In-Memory DB 단독 관리**로 처리할 때의 실전 성능과 GC 안정성을 검증합니다.

### 연구 B안 대조군 (In-Memory Comparison Lineup)
| 구분 | 엔진 / 구현 | 역할 및 측정 초점 |
| :--- | :--- | :--- |
| **기준 대조군 (Baseline)** | **C# Native Collection (`Dictionary` + `LINQ`)** | 기존 클라이언트 인메모리 관리 방식 (단건 조회 속도 vs LINQ 조건 검색 GC 비용) |
| **실험군 (SQL)** | **SQLite `:memory:` (Native C)** | `sqlite3_backup` API로 메모리 적재 후 Prepared Statement + Transaction 기반 Zero-Alloc 운용 |
| | **SQLite `:memory:` (sqlite-net ORM)** | 동일 인메모리 DB에서 ORM `Insert`/`Update`/`Query` 할당량 비교 |
| **대조군 (NoSQL)** | **LiteDB (`MemoryStream`)** | `MemoryStream` 기반 인메모리 BSON 문서 수정·업서트(`Upsert`) 및 동적 필터링 대조 |

### Hot-Path vs Cold-Path 시나리오 (Test Scenarios)
| 경로 구분 | 요구 조건 | 대표 게임 연산 시나리오 | 핵심 측정 지표 |
| :--- | :--- | :--- | :--- |
| **Hot-Path** (매 프레임/틱) | `< 0.1~0.5 ms`<br>**GC 0 Bytes** | 1. **단건 스탯/좌표 갱신:** 피격 HP 차감, 이동 좌표(`pos_x`, `pos_y`) 업데이트<br>2. **단건 전투 참조:** 공격 시 장착 무기·캐릭터 스탯 즉시 조회<br>3. **배치 틱 갱신:** N개 유닛 상태를 단일 트랜잭션으로 일괄 업데이트 | `Avg / P95 / P99 (ms)`, `Bytes/op`, **60fps 프레임 예산(16.67ms) 대비 점유율(%)** |
| **Cold-Path** (비정기/UI) | 안정적 `P95`<br>트랜잭션 정합성 | 1. **복합 조건 검색+정렬:** 상점/인벤토리 다중 필터(`rarity >= 3 AND req_level <= 50 ORDER BY price`)<br>2. **인벤토리 증감 트랜잭션:** 아이템 획득(`Insert`) + 수량 차감(`Update`) + 소진 삭제(`Delete`)<br>3. **집계/연쇄 갱신:** 유저·길드 단위 합산(`SUM`/`COUNT`) 및 일괄 내구도 감소 | `Avg / P95 (ms)`, `Bytes/op`, 트랜잭션 묶음 유무에 따른 쓰기 지연 차이 |

### 정밀 측정 설계 원칙 (`IRuntimeDbSession`)
1. **초기 적재 분리 (`HydrateFromDisk`)**: 디스크 `.db`를 인메모리 DB로 복제하는 시간(`Hydration Time`, SQLite는 `sqlite3_backup` 활용)을 별도 1회 지표로 분리하고, Hot/Cold 측정에는 포함하지 않습니다.
2. **세션 및 Statement 재사용 (`Prepare`)**: 커넥션 유지 상태에서 `sqlite3_prepare_v2` 및 컬렉션 참조를 사전 바인딩해 순수 연산 비용만 측정합니다.
3. **반복 측정 시 상태 보존**: `Insert`/`Delete`가 포함된 Cold-Path는 100회 반복 측정 시 데이터 크기가 변하지 않도록 루프 내 `ROLLBACK` 또는 대칭 연산(`+1` 후 `-1`)을 적용합니다.
4. **상태 정합성 검증 (`VerifyStateChecksum`)**: 쓰기 연산 완료 후 총 행 수·HP 합계 등 체크섬을 대조해 데이터 무결성을 검증합니다.

---

## 6. 연구 C안: 트랜잭션 기반 상태 저장 및 롤백(Undo/Redo) 시스템

### 연구 개요 및 목적
* **연구 목적:** 턴제 게임, 시뮬레이션, 미스터리 장르에서 **N턴 전 또는 N초 전의 데이터 상태로 복구(Undo/Redo)** 할 때, DB 트랜잭션/Savepoint 기반 상태 롤백과 C# 인메모리 수동 복구 방식 간의 **복구 속도, 메모리 점유율, 힙 파편화(Fragmentation)** 를 비교 검증합니다.

### 연구 C안 대조군 (Comparison Lineup)
| 구분 | 방식 / 엔진 | 역할 및 측정 초점 |
| :--- | :--- | :--- |
| **실험군 (DB 트랜잭션)** | **SQLite `:memory:` (`SAVEPOINT` / `ROLLBACK`)** | 중첩 `SAVEPOINT` 기반 N단계 상태 되돌리기 속도 및 메모리 페이지 관리 효율 검증 |
| | **LiteDB (`Transaction`)** | Document 단위 트랜잭션 롤백 비용 및 BSON 스냅샷 오버헤드 비교 |
| **대조군 (C# 수동 복구)** | **C# Snapshot (Deep Copy / Memento)** | 턴/시간마다 상태 스냅샷 복제 시 발생하는 LOH·힙 메모리 파편화 및 GC 압력 측정 |
| | **C# Command Pattern (역연산 스택)** | 상태 변경 커맨드 기록 및 역실행(`Undo`) 구현 복잡도 대비 실행 속도 대조 |

### 핵심 측정 지표 (Metrics)
* **Checkpoint / Save Cost (ms):** N턴·N초 간격으로 복구 지점을 기록할 때의 지연 시간
* **Rollback Latency (ms):** 1턴 전 ~ N턴 전 상태로 되돌리는 데 걸리는 소요 시간
* **Memory Fragmentation & GC:** 반복 Undo/Redo 사이클 수행 후 C# 힙 할당량(`Bytes/op`) 및 파편화 추이

---

## 7. 비-SQL 포맷의 조회 기능 보완 구현 (Query Capability Emulation)

SQL 엔진이 없는 스토리지 포맷은 동일한 `QuerySpec`(Case A~D)을 실행하기 위해 빌드 시 보조 키 설계와 조회 시 애플리케이션 레이어 에뮬레이션을 적용합니다.

### 7.1 RocksDB (Key-Value Store)
RocksDB는 순수 Key-Value 저장소(`Get`, `Seek`)이므로 SQL 조건 검색·조인·컬럼 추출을 다음과 같이 보완합니다.

| 조회 유형 | 네이티브 한계 | RocksDB 보완 구현 방식 |
| :--- | :--- | :--- |
| **Case A (PK 단건)** | SQL `WHERE PK = ?` 미지원 | `item_{id}`, `doc_char_{id}`, `sparse_ev_{id}` 형태로 PK 프리픽스 키를 구성하고, `Prepare()`에서 `byte[]` 키를 사전 인코딩해 `db.Get()`으로 O(1) 조회 |
| **Case B (범위/조건)** | 보조 B-Tree 인덱스 미지원 | **기본(현재):** 테이블 프리픽스(`item_` 등) 기준 `Iterator.Seek` 풀 스캔 후 `QueryRuntime.Matches` 필터링<br>**확장안:** `idx_c1_ratk_{rarity:D2}_{atk:D5}_{id:D6}`처럼 고정 자릿수 패딩 보조 키를 기록해 사전순 정렬 기반 범위 `Seek` 수행 |
| **Case C (연쇄/JOIN)** | `JOIN` 연산자 미지원 | 빌드 시 `inv_by_char_{character_id}_{instance_id}`, `char_by_user_{user_id}_{character_id}`, `link_{item_id}_{effect_id}` 역인덱스 프리픽스 키를 중복 기록하고, 조회 시 `Seek` + 연쇄 `db.Get()`으로 C#에서 행 조립 (`Document`/`Sparse` 검증 시 `DocumentUnwindHelper`로 평면화) |
| **Case D (부분 추출)** | 컬럼 단위 Projection 불가 | 행 전체 `byte[]`를 읽은 뒤 `BinaryReader`로 필요 필드까지 순차 디코딩 (향후 고정 길이 숫자 필드를 가변 `string` 앞에 배치하면 `BitConverter` 오프셋 직독으로 할당 없는 추출 가능) |

---

## 8. 실험 통제 변수 및 결과 해석 기준 (Reproducibility & Trade-offs)

### 실험 환경 및 통제 변수 (측정 시 기록 필수)
* **런타임 환경:** Unity 버전, OS, Scripting Backend (`Mono` vs `IL2CPP`), 실행 스레드(`UniTask.RunOnThreadPool`)
* **데이터 생성 통제:** 고정 `Seed = 42`, 동일 난수 분포로 모든 포맷에 동일한 논리 데이터 생성
* **측정 통제:** 사전 `GC.Collect()` 정리, `Warmup = 5회`, `Measure = 100회` 반복, 결과는 `Benchmark_Results.txt`에 자동 누적 기록

### 아키텍처 선택 트레이드오프 (해석 가이드)
* **정규화 조회·복합 필터·Zero-Alloc 우선** → `SQLite (Native C)`
* **중첩 객체 그래프 1-shot 로드·동적 스키마 유연성 우선** → `LiteDB` (단, C# Managed BSON 파싱·할당 비용 감수)
* **대규모(`S100K`+) 단건 KV 포인트 조회 및 대량 순차 쓰기 우선** → `RocksDB`
* **복합 상태 수정의 원자성·롤백 안정성 + 조건 검색 동시 충족** → `SQLite :memory:` 단독 관리 (연구 B·C안으로 정량 검증)
