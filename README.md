# SQLiteMemoryEngine
SQLite In-Memory DB로 통합 관리하는 게임 데이터 아키텍처 연구

## 1. DB 명명 규칙 (Standard Naming Convention)

실험용 데이터베이스 파일 이름만 보고도 **[용도]**, **[데이터 양]**, **[복잡도 레벨]** 을 즉시 식별할 수 있도록 아래 표준 규격을 따릅니다.

### 명명 포맷 (Naming Pattern)

`GameData_[Scale]_[Complexity].db`

### 구성 요소 명세 (Specification)

#### 1. 데이터 양 (Data Scale)
| 식별자 | 기준 건수 | 비고 |
| :--- | :--- | :--- |
| **`S100`** | ~100건 | 극소규모 테스트 / 단단위 실험 |
| **`S1K`** | ~1,000건 (1K) | 소규모 테스트 |
| **`S10K`** | ~10,000건 (10K) | 중규모 성능 테스트 |
| **`S100K`** | ~100,000건 (100K) | 대규모 벤치마크 테스트 |

#### 2. 데이터 복잡도 (Data Complexity)
| 식별자 | 정의 | 특징 및 조건 |
| :--- | :--- | :--- |
| **`C1_Low`** | 단순 구조 | 단일/단순 테이블, 참조 깊이 0~1 |
| **`C2_Mid`** | 중간 연관 구조 | N:M 매핑, 2~3개 JOIN 필요, 참조 깊이 2 |
| **`C3_High`** | 고복잡도 구조 | 복잡한 FK(거미줄), 4개 이상 JOIN, 서브쿼리/팬아웃 유발, 참조 깊이 3 이상 |

### 파일명 예시 (Examples)

* `GameData_S100_C1_Low.db` : 데이터 100건 이하의 단순 구조 DB
* `GameData_S10K_C2_Mid.db` : 데이터 10,000건 수준의 N:M 연관 구조 DB
* `GameData_S100K_C3_High.db` : 데이터 100,000건 수준의 고복잡도 관계형 DB

## 2. 벤치마크 데이터 포맷 라인업 (Common Lineup)

모든 연구안(A, B, C)에서 공통으로 대조 및 검증에 사용하는 전체 데이터 포맷 및 라이브러리 사양입니다.

| 분류 | 비교 포맷 | 특징 및 상용 게임 채택 이유 |
| :--- | :--- | :--- |
| **Relational DB** | **SQLite (Native C)** | C API 직접 P/Invoke 매핑 (Zero-Alloc 목표 최적화 SQL) |
| | **SQLite (sqlite-net)** | ORM 편의성 제공 대가로 발생하는 객체/Reflection 할당 오버헤드 측정 |
| **NoSQL / (KV, Document) DB** | **RocksDB** | SSTable 기반 고성능 Key-Value 로컬 DB (단건 KV 고속 조회) |
| | **LiteDB** | C# 순수 NoSQL Document DB (B-Tree 인덱스 기반 Document 검색) |
| **Zero-Parsing** | **FlatBuffers** | 역직렬화(Deserialization) 없는 Direct Memory Read (상용 AAA급 필수) |
| **Binary Serializer** | **MessagePack (Cysharp)** | C# 최적화 바이너리 직렬화 (JSON 완벽 대체 표준) |
| | **Raw Binary** | MemoryStream / BinaryReader 기반 Direct Stream 기준점 |
| **Text Data** | **JSON (Newtonsoft / System.Text)** | 가독성 중심 표준 포맷 (최약 대조군 Baseline) |

## 3. 연구 A안: Master Data 단건 및 조건 조회 성능 벤치마크

### 연구 개요 및 목적
* **연구 목적:** 대용량 Master Data 조회 시, 데이터 포맷별 **조회 속도(Execution Time)** 및 **메모리 할당량(GC Alloc)** 을 정량적 비교 검증합니다.
* **조회 유형 (Lookup Types):**
  1. **단순 PK 조회:** Primary Key / Key-Value 기반 1:1 고속 Target Lookup
  2. **단일 조건 조회:** Indexed Column 대상 조건 검색 및 범위 필터링
  3. **복합 조건 조회:** 2개 이상의 다중 속성 조합 필터링 (`AND` / `OR` 조건)


### 실험 측정 조건 (Test Matrix)

모든 데이터 포맷은 동일한 데이터 세트 환경에서 **데이터 스케일(S)** 과 **복잡도(C)** 전 구간을 전수 측정합니다.

* **데이터 스케일 (Scale):** `S100` / `S1K` / `S10K` / `S100K`
* **데이터 복잡도 (Complexity):** `C1_Low` / `C2_Mid` / `C3_High`
* **측정 항목 (Metrics):**
  * **Execution Time (ms):** N회 반복 조회 시 평균 및 최저/최고 실행 시간
  * **GC Alloc (Bytes):** 쿼리 및 조회 연산 1회당 발생한 C# 힙 메모리 할당량
  * **RAM Footprint (MB):** 데이터 탑재 시 클라이언트 메모리 점유율
