# SQLiteMemoryEngine
SQLite In-Memory DB 및 로컬 데이터 스토리지 포맷별 게임 데이터 아키텍처 연구

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

## 3. 벤치마크 데이터 포맷 라인업 (Common Lineup)

| 분류 | 비교 포맷 | 현재 구현 상태 | 특징 및 채택 이유 |
| :--- | :--- | :--- | :--- |
| **Relational DB** | **SQLite (Native C)** | 활성 (빌드/조회) | C API(`e_sqlite3`) 직접 P/Invoke 매핑 (Zero-Alloc 목표 SQL) |
| | **SQLite (sqlite-net)** | 활성 (빌드/조회) | ORM 편의성 대가로 발생하는 객체/Reflection 할당 오버헤드 측정 |
| **NoSQL DB** | **RocksDB** | 활성 (빌드/조회) | SSTable 기반 KV 로컬 DB (단건 `Get` 및 프리픽스 탐색) |
| | **LiteDB** | 활성 (빌드/조회) | C# 순수 Document DB (BSON 중첩 문서 및 동적 스키마 처리) |
| **Zero-Parsing** | **FlatBuffers** | 보류 (별도 최적화 예정) | 역직렬화 없는 Direct Memory Read |
| **Binary Serializer** | **MessagePack (Cysharp)** | 보류 (별도 최적화 예정) | C# 최적화 바이너리 직렬화 |
| | **Raw Binary** | 보류 (별도 최적화 예정) | `MemoryStream` / `BinaryReader` 기반 기준점 |
| **Text Data** | **JSON (Newtonsoft)** | 보류 (별도 최적화 예정) | 가독성 중심 대조군 (Baseline) |

---

## 4. 연구 A안: Master Data 로드 및 조회 성능 벤치마크

### 연구 개요 및 조회 유형
* **연구 목적:** 대용량 게임 데이터의 **전체 로드(Bulk Load)** 및 **부분/조건 조회(Query Lookup)** 시 포맷별 실행 시간과 C# 힙 메모리 할당량을 정량적으로 비교 검증합니다.
* **측정 시나리오:**
  * **Bulk Load:** 선택된 `Shape × Scale × Complexity` 데이터셋 전체를 C# 인메모리 컨테이너로 적재
  * **Case A (PK 단건 조회):** Primary Key 기반 1:1 직접 조회
  * **Case B (범위/복합 조건 조회):** 인덱스 컬럼 기반 범위 및 다중 `AND` 필터링
  * **Case C (연쇄/트리 조회):** `Relational`은 다중 `JOIN`, `Document`는 중첩 하위 트리 조회
  * **Case D (부분 필드 추출):** Projection을 통한 단일 컬럼 추출

### 정밀 측정 프로토콜 (왜곡 방지 설계)
단순 1회 실행 시 발생하는 초기화·박싱·콜드 캐시 왜곡을 제거하기 위해 `IQueryBenchmarkSession` 기반의 2단계 실행 구조를 사용합니다.

1. **사전 정확도 검증 (`ExecuteForVerification`)**: 측정 루프 밖에서 1회 실행해 `QueryResult.IsEquivalentTo`로 기준 결과(`SQLite ORM`)와의 데이터 일치를 검증합니다. (`Document`/`Sparse`는 검증 시에만 `DocumentUnwindHelper`로 중첩 필드를 평면화하여 공통 스칼라 컬럼 기준으로 비교)
2. **커넥션 및 컴파일 분리 (`Prepare`)**: DB 파일 오픈, `sqlite3_prepare_v2`, `BsonExpression` 빌드, RocksDB 키 `byte[]` 인코딩을 측정 루프 밖에서 완료합니다.
3. **반복 측정 및 할당량 산출 (`ExecuteNoAlloc`)**:
   * **Warmup 5회** 후 **100회 반복** 실행하여 `Avg`, `Min`, `P95` (상위 5% 이상치 제외 상한), `Max (ms)` 산출
   * `Dictionary<string, object>` 생성과 박싱을 배제한 네이티브 경로로 `GC.GetAllocatedBytesForCurrentThread()` 기준 **1회당 순수 할당 바이트(`Bytes/op`)** 측정

---

## 5. 비-SQL 포맷의 조회 기능 보완 구현 (Query Capability Emulation)

SQL 엔진이 없는 스토리지 포맷은 동일한 `QuerySpec`(Case A~D)을 실행하기 위해 빌드 시 보조 키 설계와 조회 시 애플리케이션 레이어 에뮬레이션을 적용합니다.

### 5.1 RocksDB (Key-Value Store)
RocksDB는 순수 Key-Value 저장소(`Get`, `Seek`)이므로 SQL 조건 검색·조인·컬럼 추출을 다음과 같이 보완합니다.

| 조회 유형 | 네이티브 한계 | RocksDB 보완 구현 방식 |
| :--- | :--- | :--- |
| **Case A (PK 단건)** | SQL `WHERE PK = ?` 미지원 | `item_{id}`, `doc_char_{id}`, `sparse_ev_{id}` 형태로 PK 프리픽스 키를 구성하고, `Prepare()`에서 `byte[]` 키를 사전 인코딩해 `db.Get()`으로 O(1) 조회 |
| **Case B (범위/조건)** | 보조 B-Tree 인덱스 미지원 | **기본(현재):** 테이블 프리픽스(`item_` 등) 기준 `Iterator.Seek` 풀 스캔 후 `QueryRuntime.Matches` 필터링<br>**확장안:** `idx_c1_ratk_{rarity:D2}_{atk:D5}_{id:D6}`처럼 고정 자릿수 패딩 보조 키를 기록해 사전순 정렬 기반 범위 `Seek` 수행 |
| **Case C (연쇄/JOIN)** | `JOIN` 연산자 미지원 | 빌드 시 `inv_by_char_{character_id}_{instance_id}`, `char_by_user_{user_id}_{character_id}`, `link_{item_id}_{effect_id}` 역인덱스 프리픽스 키를 중복 기록하고, 조회 시 `Seek` + 연쇄 `db.Get()`으로 C#에서 행 조립 (`Document`/`Sparse` 검증 시 `DocumentUnwindHelper`로 평면화) |
| **Case D (부분 추출)** | 컬럼 단위 Projection 불가 | 행 전체 `byte[]`를 읽은 뒤 `BinaryReader`로 필요 필드까지 순차 디코딩 (향후 고정 길이 숫자 필드를 가변 `string` 앞에 배치하면 `BitConverter` 오프셋 직독으로 할당 없는 추출 가능) |
