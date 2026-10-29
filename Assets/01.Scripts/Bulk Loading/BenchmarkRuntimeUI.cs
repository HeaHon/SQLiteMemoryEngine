using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

public class BenchmarkRuntimeUI : MonoBehaviour
{
    public event Action<DataScale, DataComplexity, TargetFormat> OnRunAllRequested;
    public event Action<TargetFormat, DataScale, DataComplexity, string> OnRunQueryRequested;

    private Dropdown scaleDropdown;
    private Dropdown complexityDropdown;
    private InputField queryInputField;

    // 로그 관리용 컴포넌트
    private List<string> displayLines = new List<string>();
    private Text resultOutputText;
    private ScrollRect scrollRect;

    private Dictionary<TargetFormat, Toggle> formatToggles = new Dictionary<TargetFormat, Toggle>();
    private bool isHandlingToggle = false;

    private void Awake()
    {
        BuildRuntimeUI();
        AppendOutput("System: Benchmark Console Initialized.");
    }

    private void BuildRuntimeUI()
    {
        // 1. Canvas 설정 (Pixel Perfect 활성화로 텍스트 화질 선명하게 개선)
        var canvasObj = new GameObject("Benchmark_Canvas");
        var canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.pixelPerfect = true;

        var scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasObj.AddComponent<GraphicRaycaster>();

        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var eventSystemObj = new GameObject("EventSystem");
            eventSystemObj.AddComponent<EventSystem>();
            eventSystemObj.AddComponent<InputSystemUIInputModule>();
        }

        // 2. 메인 패널
        var panelObj = CreateUIObject("MainPanel", canvasObj.transform);
        panelObj.AddComponent<Image>().color = new Color(0.07f, 0.09f, 0.13f, 0.98f);

        var panelRect = panelObj.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.05f, 0.03f);
        panelRect.anchorMax = new Vector2(0.95f, 0.97f);
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        Transform p = panelObj.transform;

        // --- Header 타이틀 ---
        CreateAbsoluteLabel(p, "연구 A안 : 단건 및 조건 조회 성능 벤치 마크", 16, TextAnchor.MiddleCenter, new Color(0.2f, 0.8f, 1f), new Vector2(20, -15), new Vector2(320, 28), true);

        // --- [추가] 콘솔창 클리어 버튼 (우측 상단 배치) ---
        var clearBtn = CreateAbsoluteButton(p, "Clear Console", new Color(0.55f, 0.2f, 0.2f), new Vector2(-20, -15), new Vector2(120, 28), true);
        clearBtn.onClick.AddListener(() =>
        {
            ClearOutput();
            AppendOutput("System: Console Cleared.");
        });

        // --- 섹션 1: Scale & Complexity 설정 ---
        CreateAbsoluteLabel(p, "Data Scale:", 12, TextAnchor.MiddleRight, Color.gray, new Vector2(20, -55), new Vector2(75, 24));
        scaleDropdown = CreateAbsoluteDropdown(p, new List<string> { "S100", "S1K", "S10K", "S100K" }, new Vector2(100, -55), new Vector2(120, 26));

        CreateAbsoluteLabel(p, "Complexity:", 12, TextAnchor.MiddleRight, Color.gray, new Vector2(240, -55), new Vector2(80, 24));
        complexityDropdown = CreateAbsoluteDropdown(p, new List<string> { "C1_Low", "C2_Mid", "C3_High" }, new Vector2(325, -55), new Vector2(120, 26));

        // --- 섹션 2: 포맷 선택 체크박스들 (All 연동 로직 포함) ---
        CreateAbsoluteLabel(p, "TARGET FORMATS:", 11, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.3f), new Vector2(20, -95), new Vector2(300, 18), true);

        CreateAbsoluteToggle(p, "All", TargetFormat.All, true, new Vector2(20, -120), new Vector2(70, 22));
        CreateAbsoluteToggle(p, "SQLite (ORM)", TargetFormat.SQLite_ORM, true, new Vector2(100, -120), new Vector2(115, 22));
        CreateAbsoluteToggle(p, "SQLite (NativeC)", TargetFormat.SQLite_NativeC, true, new Vector2(225, -120), new Vector2(115, 22));
        CreateAbsoluteToggle(p, "RocksDB", TargetFormat.RocksDB, true, new Vector2(350, -120), new Vector2(95, 22));
        CreateAbsoluteToggle(p, "FlatBuffers", TargetFormat.FlatBuffers, true, new Vector2(455, -120), new Vector2(110, 22));

        CreateAbsoluteToggle(p, "RawBinary", TargetFormat.RawBinary, true, new Vector2(20, -148), new Vector2(105, 22));
        CreateAbsoluteToggle(p, "LiteDB", TargetFormat.LiteDB, true, new Vector2(135, -148), new Vector2(85, 22));
        CreateAbsoluteToggle(p, "MessagePack", TargetFormat.MessagePack, true, new Vector2(230, -148), new Vector2(120, 22));
        CreateAbsoluteToggle(p, "JSON", TargetFormat.JSON, true, new Vector2(360, -148), new Vector2(75, 22));

        foreach (var kvp in formatToggles)
        {
            var format = kvp.Key;
            var toggle = kvp.Value;
            toggle.onValueChanged.AddListener((isOn) => OnToggleChanged(format, isOn));
        }

        // --- 섹션 3: 전체 로드 버튼 ---
        var bulkBtn = CreateAbsoluteButton(p, "RUN BULK LOAD BENCHMARK (선택 포맷 전체 로드 측정)", new Color(0.15f, 0.55f, 0.35f), new Vector2(20, -180), new Vector2(-40, 30));
        bulkBtn.onClick.AddListener(() =>
        {
            var scale = (DataScale)scaleDropdown.value;
            var complexity = (DataComplexity)complexityDropdown.value;
            var selectedFormats = GetSelectedFormats();

            AppendOutput($"=== [전체 로드] 시작 [{scale} / {complexity}] ===");
            OnRunAllRequested?.Invoke(scale, complexity, selectedFormats);
        });

        // --- 섹션 4: Case A ~ D 쿼리 생성 버튼들 ---
        CreateAbsoluteLabel(p, "CASE A ~ D GENERATOR:", 11, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.3f), new Vector2(20, -222), new Vector2(200, 18), true);

        CreateAbsoluteCaseButton(p, "Case A (PK)", new Vector2(20, -244), new Vector2(120, 28), 'A');
        CreateAbsoluteCaseButton(p, "Case B (범위)", new Vector2(148, -244), new Vector2(120, 28), 'B');
        CreateAbsoluteCaseButton(p, "Case C (연쇄)", new Vector2(276, -244), new Vector2(120, 28), 'C');
        CreateAbsoluteCaseButton(p, "Case D (추출)", new Vector2(404, -244), new Vector2(120, 28), 'D');

        scaleDropdown.onValueChanged.AddListener((index) =>
        {
            var scale = (DataScale)scaleDropdown.value;
            var complexity = (DataComplexity)complexityDropdown.value;
            AppendOutput($"<color=#00FFCC>[조건 갱신] Scale이 {scale}(으)로 변경되었습니다.</color>");
        });
        complexityDropdown.onValueChanged.AddListener((index) =>
        {
            var scale = (DataScale)scaleDropdown.value;
            var complexity = (DataComplexity)complexityDropdown.value;
            AppendOutput($"<color=#00FFCC>[조건 갱신] Complexity가 {complexity}(으)로 변경되었습니다.</color>");
        });

        // --- 섹션 5: 개별 SQL 쿼리 입력창 및 선택된 모든 포맷 대상 실행 ---
        queryInputField = CreateAbsoluteInputField(p, "SELECT price FROM Master_Item_C1 WHERE item_id = 1005", new Vector2(20, -282), new Vector2(-155, 32));

        var queryBtn = CreateAbsoluteButton(p, "Execute Query", new Color(0.2f, 0.45f, 0.8f), new Vector2(-20, -282), new Vector2(130, 32), true);
        queryBtn.onClick.AddListener(() =>
        {
            var scale = (DataScale)scaleDropdown.value;
            var complexity = (DataComplexity)complexityDropdown.value;
            string query = queryInputField.text;

            if (string.IsNullOrEmpty(query))
            {
                AppendOutput("실행할 SQL 쿼리를 입력해주세요.");
                return;
            }

            var selectedFormats = GetSelectedFormats();
            CheckAndWarnJsonMessagePack(selectedFormats);

            TargetFormat queryableFormats = selectedFormats & ~(TargetFormat.JSON | TargetFormat.MessagePack);

            AppendOutput($"=== [선택 포맷 쿼리 실행] ===\nQuery: {query}");
            OnRunQueryRequested?.Invoke(queryableFormats, scale, complexity, query);
        });

        // --- 섹션 6: 대형 스크롤 로그 창 및 수직 스크롤 바 생성 ---
        var scrollObj = CreateUIObject("ScrollArea", p);
        scrollRect = scrollObj.AddComponent<ScrollRect>();
        var scrollBg = scrollObj.AddComponent<Image>();
        scrollBg.color = new Color(0.04f, 0.05f, 0.07f, 1f);

        var scrollRectTransform = scrollObj.GetComponent<RectTransform>();
        scrollRectTransform.anchorMin = new Vector2(0, 1);
        scrollRectTransform.anchorMax = new Vector2(1, 1);
        scrollRectTransform.pivot = new Vector2(0.5f, 1);
        scrollRectTransform.anchoredPosition = new Vector2(0, -350); // Pos Y: -350
        scrollRectTransform.sizeDelta = new Vector2(-40, 660);         // Height: 660

        var viewportObj = CreateUIObject("Viewport", scrollObj.transform);
        SetRectTransform(viewportObj.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        viewportObj.AddComponent<RectMask2D>();

        var contentTextObj = CreateUIObject("ContentText", viewportObj.transform);
        var textRect = contentTextObj.GetComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0, 1);
        textRect.anchorMax = new Vector2(1, 1);
        textRect.pivot = new Vector2(0, 1);
        textRect.anchoredPosition = Vector2.zero;
        textRect.sizeDelta = new Vector2(0, 100);

        var sizeFitter = contentTextObj.AddComponent<ContentSizeFitter>();
        sizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        resultOutputText = contentTextObj.AddComponent<Text>();
        resultOutputText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        resultOutputText.fontSize = 13;
        resultOutputText.color = new Color(0.9f, 0.9f, 0.9f);
        resultOutputText.alignment = TextAnchor.UpperLeft;
        resultOutputText.horizontalOverflow = HorizontalWrapMode.Wrap;
        resultOutputText.verticalOverflow = VerticalWrapMode.Truncate;
        resultOutputText.supportRichText = true;

        scrollRect.content = textRect;
        scrollRect.viewport = viewportObj.GetComponent<RectTransform>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;

        // --- [추가] 수직 스크롤 바(Scrollbar) 동적 생성 및 연동 ---
        var scrollbarObj = CreateUIObject("ScrollbarVertical", scrollObj.transform);
        var sbRect = scrollbarObj.GetComponent<RectTransform>();
        sbRect.anchorMin = new Vector2(1, 0);
        sbRect.anchorMax = new Vector2(1, 1);
        sbRect.pivot = new Vector2(1, 1);
        sbRect.sizeDelta = new Vector2(16, 0);
        sbRect.anchoredPosition = Vector2.zero;

        var sbImage = scrollbarObj.AddComponent<Image>();
        sbImage.color = new Color(0.12f, 0.14f, 0.18f, 1f);

        var scrollbar = scrollbarObj.AddComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;

        var slidingAreaObj = CreateUIObject("Sliding Area", scrollbarObj.transform);
        SetRectTransform(slidingAreaObj.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, new Vector2(2, 2), new Vector2(-2, -2));

        var handleObj = CreateUIObject("Handle", slidingAreaObj.transform);
        var handleRect = handleObj.GetComponent<RectTransform>();
        SetRectTransform(handleRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var handleImage = handleObj.AddComponent<Image>();
        handleImage.color = new Color(0.35f, 0.42f, 0.55f, 1f);

        scrollbar.handleRect = handleRect;
        scrollbar.targetGraphic = handleImage;

        // ScrollRect에 스크롤 바 연결
        scrollRect.verticalScrollbar = scrollbar;
        scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
    }

    // JSON 및 MessagePack 체크 여부 검사 후 경고 로그 띄우기
    private void CheckAndWarnJsonMessagePack(TargetFormat formats)
    {
        bool hasJson = (formats & TargetFormat.JSON) != 0;
        bool hasMsgPack = (formats & TargetFormat.MessagePack) != 0;

        if (hasJson || hasMsgPack)
        {
            AppendOutput("<color=#FF4444>[경고]쿼리를 실행할 수 있는 유효하지 않은 포맷이 선택되었습니다. (JSON, MessagePack은 단건 조회 불가)</color>");
        }
    }

    private void OnToggleChanged(TargetFormat format, bool isOn)
    {
        if (isHandlingToggle) return;
        isHandlingToggle = true;

        if (format == TargetFormat.All)
        {
            foreach (var kvp in formatToggles)
            {
                kvp.Value.isOn = isOn;
            }
        }
        else
        {
            if (!isOn && formatToggles.ContainsKey(TargetFormat.All))
            {
                formatToggles[TargetFormat.All].isOn = false;
            }
            else if (isOn)
            {
                bool allOthersOn = true;
                foreach (var kvp in formatToggles)
                {
                    if (kvp.Key != TargetFormat.All && !kvp.Value.isOn)
                    {
                        allOthersOn = false;
                        break;
                    }
                }
                if (allOthersOn && formatToggles.ContainsKey(TargetFormat.All))
                {
                    formatToggles[TargetFormat.All].isOn = true;
                }
            }
        }

        isHandlingToggle = false;
    }

    public void AppendOutput(string message)
    {
        displayLines.Add(message);
        if (displayLines.Count > 200) displayLines.RemoveAt(0);

        if (resultOutputText != null)
        {
            resultOutputText.text = string.Join("\n\n", displayLines);
        }

        if (scrollRect != null)
        {
            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 0f;
        }

        Debug.Log(message);
    }

    public void ClearOutput()
    {
        displayLines.Clear();
        if (resultOutputText != null) resultOutputText.text = string.Empty;
    }

    // DataScale에 따른 최대 아이템/데이터 개수 반환 헬퍼
    private int GetMaxIdByScale(DataScale scale)
    {
        switch (scale)
        {
            case DataScale.S100: return 100;
            case DataScale.S1K: return 1000;
            case DataScale.S10K: return 10000;
            case DataScale.S100K: return 100000;
            default: return 100;
        }
    }

    private string GetCaseQuery(DataScale scale, DataComplexity complexity, char caseType)
    {
        int maxId = GetMaxIdByScale(scale);
        int midId = maxId / 2; // 규모의 중간 지점 ID

        switch (complexity)
        {
            case DataComplexity.C1_Low:
                if (caseType == 'A') return $"SELECT * FROM Master_Item_C1 WHERE item_id = {midId}";
                if (caseType == 'B') return "SELECT * FROM Master_Item_C1 WHERE rarity >= 3 AND atk >= 100";
                if (caseType == 'C') return $"할 수 없음 (단일 테이블 구조)";
                if (caseType == 'D') return $"SELECT price FROM Master_Item_C1 WHERE item_id = {midId}";
                break;

            case DataComplexity.C2_Mid:
                if (caseType == 'A') return $"SELECT * FROM Master_Item_C2 WHERE item_id = {midId}";
                if (caseType == 'B') return "SELECT * FROM Master_Item_C2 WHERE req_level >= 10 AND rarity >= 3";
                if (caseType == 'C') return "SELECT c.character_id, c.name AS character_name, i.item_id, i.name AS item_name, inv.instance_id, inv.quantity FROM Runtime_Inventory_C2 inv JOIN Runtime_Character_C2 c ON inv.character_id = c.character_id JOIN Master_Item_C2 i ON inv.item_id = i.item_id WHERE c.character_id = 1 ORDER BY inv.instance_id";
                if (caseType == 'D') return $"SELECT base_atk FROM Master_Item_C2 WHERE item_id = {midId}";
                break;

            case DataComplexity.C3_High:
                if (caseType == 'A') return $"SELECT * FROM Master_Item_C3 WHERE item_id = {midId}";
                if (caseType == 'B') return "SELECT * FROM Master_Item_C3 WHERE rarity >= 4";
                if (caseType == 'C') return "SELECT u.user_id, u.account_name, c.character_id, c.name AS character_name, i.item_id, i.name AS item_name, inv.instance_id, e.effect_type FROM System_User_C3 u JOIN Runtime_Character_C3 c ON c.user_id = u.user_id JOIN Runtime_Inventory_C3 inv ON inv.character_id = c.character_id JOIN Master_Item_C3 i ON i.item_id = inv.item_id LEFT JOIN Item_Effect_Link_C3 l ON l.item_id = i.item_id LEFT JOIN Master_Effect_C3 e ON e.effect_id = l.effect_id WHERE u.user_id = 1 ORDER BY inv.instance_id";
                if (caseType == 'D') return $"SELECT name FROM Master_Item_C3 WHERE item_id = {midId}";
                break;
        }
        return "SELECT 1";
    }

    private TargetFormat GetSelectedFormats()
    {
        TargetFormat formats = TargetFormat.None;
        foreach (var pair in formatToggles)
        {
            if (pair.Key != TargetFormat.All && pair.Value.isOn)
            {
                formats |= pair.Key;
            }
        }
        return formats == TargetFormat.None ? TargetFormat.All : formats;
    }

    #region 절대 좌표 기반 정밀 UI 생성 헬퍼
    private GameObject CreateUIObject(string name, Transform parent)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.AddComponent<RectTransform>();
        return obj;
    }

    private void SetRectTransform(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }

    private void CreateAbsoluteLabel(Transform parent, string text, int fontSize, TextAnchor alignment, Color color, Vector2 anchoredPos, Vector2 size, bool isBold = false)
    {
        var obj = CreateUIObject("Label", parent);
        var rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;

        var label = obj.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.text = text;
        label.fontSize = fontSize;
        label.color = color;
        label.alignment = alignment;
        if (isBold) label.fontStyle = FontStyle.Bold;
    }

    private Dropdown CreateAbsoluteDropdown(Transform parent, List<string> options, Vector2 anchoredPos, Vector2 size)
    {
        var dropObj = CreateUIObject("Dropdown", parent);
        var rect = dropObj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;

        dropObj.AddComponent<Image>().color = new Color(0.18f, 0.22f, 0.28f);
        var dropdown = dropObj.AddComponent<Dropdown>();

        var labelObj = CreateUIObject("Label", dropObj.transform);
        var labelRect = labelObj.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(5, 0);
        labelRect.offsetMax = new Vector2(-20, 0);

        var labelText = labelObj.AddComponent<Text>();
        labelText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        labelText.fontSize = 12;
        labelText.color = Color.white;
        labelText.alignment = TextAnchor.MiddleCenter;
        dropdown.captionText = labelText;

        var templateObj = CreateUIObject("Template", dropObj.transform);
        var templateRect = templateObj.GetComponent<RectTransform>();
        templateRect.anchorMin = new Vector2(0, 0);
        templateRect.anchorMax = new Vector2(1, 0);
        templateRect.pivot = new Vector2(0.5f, 1);
        templateRect.anchoredPosition = new Vector2(0, -2);
        templateRect.sizeDelta = new Vector2(0, 110);

        templateObj.AddComponent<Image>().color = new Color(0.14f, 0.17f, 0.23f);
        templateObj.AddComponent<ScrollRect>();
        templateObj.SetActive(false);
        dropdown.template = templateRect;

        var itemObj = CreateUIObject("Item", templateObj.transform);
        var itemRect = itemObj.GetComponent<RectTransform>();
        SetRectTransform(itemRect, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -25), new Vector2(0, 0));

        var toggle = itemObj.AddComponent<Toggle>();
        var itemBg = itemObj.AddComponent<Image>();
        itemBg.color = new Color(0.22f, 0.26f, 0.34f, 1f);
        toggle.targetGraphic = itemBg;

        var itemLabelObj = CreateUIObject("Item Label", itemObj.transform);
        SetRectTransform(itemLabelObj.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, new Vector2(8, 0), new Vector2(-8, 0));
        var itemLabelText = itemLabelObj.AddComponent<Text>();
        itemLabelText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        itemLabelText.fontSize = 12;
        itemLabelText.color = Color.white;
        itemLabelText.alignment = TextAnchor.MiddleLeft;
        toggle.graphic = itemBg;
        dropdown.itemText = itemLabelText;

        dropdown.options = new List<Dropdown.OptionData>();
        foreach (var opt in options) dropdown.options.Add(new Dropdown.OptionData(opt));

        return dropdown;
    }

    private void CreateAbsoluteToggle(Transform parent, string labelText, TargetFormat formatEnum, bool defaultIsOn, Vector2 anchoredPos, Vector2 size)
    {
        var togObj = CreateUIObject($"Toggle_{labelText}", parent);
        var rect = togObj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;

        var toggle = togObj.AddComponent<Toggle>();

        var boxObj = CreateUIObject("Box", togObj.transform);
        SetRectTransform(boxObj.GetComponent<RectTransform>(), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, -7), new Vector2(14, 7));
        boxObj.AddComponent<Image>().color = new Color(0.22f, 0.26f, 0.34f);

        var checkObj = CreateUIObject("Check", boxObj.transform);
        SetRectTransform(checkObj.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, new Vector2(2, 2), new Vector2(-2, -2));
        var checkImg = checkObj.AddComponent<Image>();
        checkImg.color = new Color(0.15f, 0.8f, 0.4f);
        toggle.graphic = checkImg;

        var lblObj = CreateUIObject("Label", togObj.transform);
        SetRectTransform(lblObj.GetComponent<RectTransform>(), new Vector2(0, 0), new Vector2(1, 1), new Vector2(18, 0), Vector2.zero);
        var txt = lblObj.AddComponent<Text>();
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.text = labelText;
        txt.fontSize = 11;
        txt.color = new Color(0.9f, 0.9f, 0.9f);
        txt.alignment = TextAnchor.MiddleLeft;

        toggle.isOn = defaultIsOn;
        formatToggles[formatEnum] = toggle;
    }

    private Button CreateAbsoluteButton(Transform parent, string buttonText, Color btnColor, Vector2 anchoredPos, Vector2 size, bool isRightAnchor = false)
    {
        var btnObj = CreateUIObject("Button", parent);
        var rect = btnObj.GetComponent<RectTransform>();

        if (!isRightAnchor)
        {
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
        }
        else
        {
            rect.anchorMin = new Vector2(1, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(1, 1);
        }

        rect.anchoredPosition = anchoredPos;

        if (size.x < 0)
        {
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.sizeDelta = size;
        }
        else
        {
            rect.sizeDelta = size;
        }

        btnObj.AddComponent<Image>().color = btnColor;
        var button = btnObj.AddComponent<Button>();

        var textObj = CreateUIObject("Text", btnObj.transform);
        SetRectTransform(textObj.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var text = textObj.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = buttonText;
        text.fontSize = 12;
        text.fontStyle = FontStyle.Bold;
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleCenter;

        return button;
    }

    private void CreateAbsoluteCaseButton(Transform parent, string buttonText, Vector2 anchoredPos, Vector2 size, char queryKey)
    {
        var btn = CreateAbsoluteButton(parent, buttonText, new Color(0.22f, 0.32f, 0.45f), anchoredPos, size);

        btn.onClick.AddListener(() =>
        {
            if (queryInputField != null)
            {
                var scale = (DataScale)scaleDropdown.value;
                var complexity = (DataComplexity)complexityDropdown.value;

                string q = GetCaseQuery(scale, complexity, queryKey);
                queryInputField.text = q;

                AppendOutput($"<color=#00FFFF>[Case 쿼리 장착]</color> {q}");
            }
        });
    }

    private InputField CreateAbsoluteInputField(Transform parent, string placeholderText, Vector2 anchoredPos, Vector2 size)
    {
        var inputObj = CreateUIObject("InputField", parent);
        var rect = inputObj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(1, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;

        inputObj.AddComponent<Image>().color = new Color(0.14f, 0.17f, 0.23f);
        var inputField = inputObj.AddComponent<InputField>();

        var textObj = CreateUIObject("Text", inputObj.transform);
        SetRectTransform(textObj.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, new Vector2(8, 3), new Vector2(-8, -3));
        var text = textObj.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 12;
        text.color = Color.white;
        inputField.textComponent = text;

        var placeholderObj = CreateUIObject("Placeholder", inputObj.transform);
        SetRectTransform(placeholderObj.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, new Vector2(8, 3), new Vector2(-8, -3));
        var placeText = placeholderObj.AddComponent<Text>();
        placeText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        placeText.fontSize = 12;
        placeText.color = new Color(0.45f, 0.5f, 0.65f, 0.8f);
        placeText.text = placeholderText;
        inputField.placeholder = placeText;

        return inputField;
    }
    #endregion
}
