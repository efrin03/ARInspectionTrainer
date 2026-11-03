using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace ARInspectionSystem
{
    // =========================================================================
    // 1. DATA MODELS & ENUMS (CONTENT LAYER)
    // =========================================================================

    public enum FaultType
    {
        None,
        AbnormalVibration,
        TemperatureWarning,
        LubricationProblem,
        ComponentMisalignment
    }

    public enum UserGroup
    {
        ARGuided,
        WrittenProcedureControl
    }

    [System.Serializable]
    public class ComponentDefinition
    {
        public string id;
        public string componentName;
        [TextArea(2, 4)]
        public string inspectionProcedure;
        public Vector3 localPosition;
        public Vector3 localScale = Vector3.one * 0.3f;
        public PrimitiveType primitiveShape = PrimitiveType.Cube;
        public Color defaultColor = Color.gray;
    }

    [System.Serializable]
    public class SeededFault
    {
        public string componentId;
        public FaultType faultType;
        public string observableCueText;
    }

    [System.Serializable]
    public class InspectionLogEvent
    {
        public float timestamp;
        public string componentId;
        public string verdictGiven; // "Normal" or "Faulty"
        public string faultDiagnosed;
        public bool isCorrect;
    }

    // =========================================================================
    // 2. MAIN SYSTEM CONTROLLER (SINGLETON ENGINE)
    // =========================================================================

    public class ARInspectionTrainer : MonoBehaviour
    {
        public static ARInspectionTrainer Instance { get; private set; }

        [Header("Session Metadata")]
        public string participantID = "P01";
        public UserGroup group = UserGroup.ARGuided;
        public int attemptNumber = 1;

        [Header("System References")]
        public Transform machineRootContainer;

        // Content Databases
        private List<ComponentDefinition> definitions = new List<ComponentDefinition>();
        private Dictionary<string, InspectableComponent> activeComponents = new Dictionary<string, InspectableComponent>();
        private List<SeededFault> currentFaultSet = new List<SeededFault>();

        // Session Tracking
        private List<InspectionLogEvent> eventTimeline = new List<InspectionLogEvent>();
        private HashSet<string> inspectedComponentIds = new HashSet<string>();
        private InspectableComponent currentlySelectedComponent;

        private float sessionStartTime;
        private bool isSessionActive = false;
        private int correctCount = 0;
        private int incorrectCount = 0;
        private int totalSeededFaults = 0;

        // UI References (Dynamically Built)
        private Canvas mainCanvas;
        private Text statusText;
        private Text detailTitleText;
        private Text detailBodyText;
        private Text scoreSummaryText;
        private GameObject detailPanel;
        private GameObject scorePanel;
        private Button normalBtn;
        private Button faultyBtn;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);

            InitializeContentDefinitions();
            BuildDynamicUI();
        }

        private void Start()
        {
            GenerateMachineModel();
            LoadFaultScenario();
            StartNewSession();
        }

        private void Update()
        {
            if (!isSessionActive) return;

            // PC Mouse Click Simulation & Raycasting Engine
            if (Input.GetMouseButtonDown(0))
            {
                HandleRaycastSelect(Input.mousePosition);
            }

            // Highlighting updates & procedural animations
            AnimateFaultCues();
        }

        // =========================================================================
        // 3. CONTENT LAYER SETUP (MACHINE & FAULTS)
        // =========================================================================

        private void InitializeContentDefinitions()
        {
            definitions = new List<ComponentDefinition>
            {
                new ComponentDefinition { id = "C1", componentName = "Drive Motor", inspectionProcedure = "Inspect housing temperature, listen for uneven hum, check ground wire connectivity.", localPosition = new Vector3(-1.2f, 0, 0), primitiveShape = PrimitiveType.Cylinder, defaultColor = new Color(0.2f, 0.4f, 0.8f) },
                new ComponentDefinition { id = "C2", componentName = "Drive Shaft", inspectionProcedure = "Verify rotational clearance, check for surface scoring or eccentricity.", localPosition = new Vector3(-0.4f, 0, 0), primitiveShape = PrimitiveType.Cylinder, localScale = new Vector3(0.15f, 0.6f, 0.15f), defaultColor = Color.gray },
                new ComponentDefinition { id = "C3", componentName = "Main Bearings", inspectionProcedure = "Check casing temperature, check grease seal integrity and excessive vibration.", localPosition = new Vector3(0.1f, 0, 0), primitiveShape = PrimitiveType.Sphere, localScale = Vector3.one * 0.35f, defaultColor = new Color(0.8f, 0.5f, 0.2f) },
                new ComponentDefinition { id = "C4", componentName = "Protective Housing", inspectionProcedure = "Inspect mounting bolts for tightness, check structural hairline cracks.", localPosition = new Vector3(0.6f, 0, 0), primitiveShape = PrimitiveType.Cube, localScale = Vector3.one * 0.45f, defaultColor = new Color(0.3f, 0.3f, 0.3f) },
                new ComponentDefinition { id = "C5", componentName = "Flexible Coupling", inspectionProcedure = "Measure gap alignment between flanges, inspect rubber insert wear.", localPosition = new Vector3(1.1f, 0, 0), primitiveShape = PrimitiveType.Cylinder, localScale = new Vector3(0.25f, 0.2f, 0.25f), defaultColor = new Color(0.6f, 0.2f, 0.2f) },
                new ComponentDefinition { id = "C6", componentName = "Control Panel", inspectionProcedure = "Verify gauge readings, check indicator lamps and emergency stop engagement.", localPosition = new Vector3(0f, 0.6f, 0), primitiveShape = PrimitiveType.Cube, localScale = new Vector3(0.5f, 0.4f, 0.15f), defaultColor = new Color(0.1f, 0.6f, 0.3f) }
            };
        }

        private void GenerateMachineModel()
        {
            if (machineRootContainer == null)
            {
                GameObject root = new GameObject("MachineRoot_AutoCreated");
                machineRootContainer = root.transform;
            }

            foreach (var def in definitions)
            {
                GameObject obj = GameObject.CreatePrimitive(def.primitiveShape);
                obj.name = def.id + "_" + def.componentName;
                obj.transform.SetParent(machineRootContainer);
                obj.transform.localPosition = def.localPosition;
                obj.transform.localScale = def.localScale;

                Renderer rend = obj.GetComponent<Renderer>();
                if (rend != null) rend.material.color = def.defaultColor;

                InspectableComponent inspectable = obj.AddComponent<InspectableComponent>();
                inspectable.Init(def);
                activeComponents.Add(def.id, inspectable);

                // Add 3D Text Overlay Label
                Create3DWorldLabel(obj.transform, def.componentName);
            }
        }

        private void LoadFaultScenario()
        {
            currentFaultSet = new List<SeededFault>
            {
                new SeededFault { componentId = "C1", faultType = FaultType.TemperatureWarning, observableCueText = "CUE: Thermal camera overlay indicates surface temp elevated to 88°C." },
                new SeededFault { componentId = "C3", faultType = FaultType.AbnormalVibration, observableCueText = "CUE: Severe high-frequency oscillation observed at bearing cap." },
                new SeededFault { componentId = "C5", faultType = FaultType.ComponentMisalignment, observableCueText = "CUE: Angular offset of 3.5 degrees detected across coupling face." }
            };

            totalSeededFaults = currentFaultSet.Count;

            // Apply faults to actual components silently (never giving away the diagnosis directly)
            foreach (var fault in currentFaultSet)
            {
                if (activeComponents.TryGetValue(fault.componentId, out InspectableComponent comp))
                {
                    comp.SetFault(fault.faultType, fault.observableCueText);
                }
            }
        }

        // =========================================================================
        // 4. INTERACTION & INSPECTION ENGINE
        // =========================================================================

        public void StartNewSession()
        {
            sessionStartTime = Time.time;
            isSessionActive = true;
            eventTimeline.Clear();
            inspectedComponentIds.Clear();
            correctCount = 0;
            incorrectCount = 0;

            detailPanel.SetActive(false);
            scorePanel.SetActive(false);
            UpdateHUDStatus();
        }

        private void HandleRaycastSelect(Vector3 screenPoint)
        {
            Ray ray = Camera.main != null ? Camera.main.ScreenPointToRay(screenPoint) : RectTransformUtility.ScreenPointToRay(null, screenPoint);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                InspectableComponent comp = hit.collider.GetComponent<InspectableComponent>();
                if (comp != null)
                {
                    SelectComponent(comp);
                }
            }
        }

        public void SelectComponent(InspectableComponent comp)
        {
            currentlySelectedComponent = comp;
            detailTitleText.text = comp.Definition.componentName;

            string content = "<b>PROCEDURE:</b>\n" + comp.Definition.inspectionProcedure;
            if (group == UserGroup.ARGuided && comp.HasFault)
            {
                content += "\n\n<color=yellow><b>AR SENSOR OBSERVATION:</b>\n" + comp.ObservableCue + "</color>";
            }

            detailBodyText.text = content;
            detailPanel.SetActive(true);
        }

        public void SubmitVerdict(bool markedFaulty)
        {
            if (currentlySelectedComponent == null) return;

            string cId = currentlySelectedComponent.Definition.id;
            bool isActuallyFaulty = currentlySelectedComponent.HasFault;
            bool isCorrect = (markedFaulty == isActuallyFaulty);

            if (isCorrect) correctCount++;
            else incorrectCount++;

            InspectionLogEvent logEvt = new InspectionLogEvent
            {
                timestamp = Time.time - sessionStartTime,
                componentId = cId,
                verdictGiven = markedFaulty ? "Faulty" : "Normal",
                faultDiagnosed = currentlySelectedComponent.ActiveFault.ToString(),
                isCorrect = isCorrect
            };

            eventTimeline.Add(logEvt);
            inspectedComponentIds.Add(cId);

            currentlySelectedComponent.MarkAsInspected(isCorrect);
            detailPanel.SetActive(false);
            currentlySelectedComponent = null;

            UpdateHUDStatus();

            if (inspectedComponentIds.Count >= definitions.Count)
            {
                CompleteSession();
            }
        }

        private void UpdateHUDStatus()
        {
            statusText.text = $"Participant: {participantID} | Group: {group} | Progress: {inspectedComponentIds.Count}/{definitions.Count}";
        }

        // =========================================================================
        // 5. SCORING ENGINE & CSV EXPORTER
        // =========================================================================

        private void CompleteSession()
        {
            isSessionActive = false;
            float totalTimeSec = Time.time - sessionStartTime;

            // Formula: Score = max(0, 100 * (Correct - 0.5 * Incorrect) / TotalFaults - TimePenalty)
            int missedFaults = totalSeededFaults - correctCount;
            float timePenalty = Mathf.Max(0, (totalTimeSec - 60f) * 0.2f); // Reference time 60s
            float rawScore = 100f * (correctCount - (0.5f * incorrectCount)) / Mathf.Max(1, totalSeededFaults);
            float finalScore = Mathf.Clamp(rawScore - timePenalty, 0f, 100f);

            string summary = $"<b>SESSION COMPLETE</b>\n\n" +
                             $"Final Score: <b>{finalScore:F1}</b> / 100\n" +
                             $"Time Taken: {totalTimeSec:F1} s\n" +
                             $"Correct Diagnoses: {correctCount}\n" +
                             $"Incorrect Selections: {incorrectCount}\n" +
                             $"Missed Faults: {missedFaults}";

            scoreSummaryText.text = summary;
            scorePanel.SetActive(true);

            ExportSessionToCSV(totalTimeSec, finalScore, correctCount, missedFaults, incorrectCount);
        }

        private void ExportSessionToCSV(float totalTimeSec, float score, int correct, int missed, int incorrect)
        {
            string folderPath = Application.persistentDataPath;
            string summaryPath = Path.Combine(folderPath, "Session_Summary_Logs.csv");
            string timelinePath = Path.Combine(folderPath, $"Timeline_{participantID}_Att{attemptNumber}.csv");

            // Append Summary Log
            bool summaryExists = File.Exists(summaryPath);
            using (StreamWriter sw = new StreamWriter(summaryPath, true))
            {
                if (!summaryExists)
                {
                    sw.WriteLine("ParticipantID,Group,Attempt,TotalTimeSec,FinalScore,CorrectCount,MissedCount,IncorrectCount,Timestamp");
                }
                sw.WriteLine($"{participantID},{group},{attemptNumber},{totalTimeSec:F2},{score:F2},{correct},{missed},{incorrect},{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            }

            // Write Event Timeline File
            using (StreamWriter sw = new StreamWriter(timelinePath, false))
            {
                sw.WriteLine("TimeSec,ComponentID,Verdict,DiagnosedFault,IsCorrect");
                foreach (var evt in eventTimeline)
                {
                    sw.WriteLine($"{evt.timestamp:F2},{evt.componentId},{evt.verdictGiven},{evt.faultDiagnosed},{evt.isCorrect}");
                }
            }

            Debug.Log($"[LOG ENGINE] Session exported successfully to: {folderPath}");
        }

        // =========================================================================
        // 6. VISUAL CUE ANIMATOR & DYNAMIC UI BUILDER
        // =========================================================================

        private void AnimateFaultCues()
        {
            foreach (var comp in activeComponents.Values)
            {
                if (comp.HasFault && comp.ActiveFault == FaultType.AbnormalVibration)
                {
                    // Simulated physical vibration oscillation
                    Vector3 basePos = comp.Definition.localPosition;
                    comp.transform.localPosition = basePos + new Vector3(Mathf.Sin(Time.time * 40f) * 0.015f, 0, 0);
                }
            }
        }

        private void Create3DWorldLabel(Transform parent, string labelText)
        {
            GameObject labelObj = new GameObject("3D_Label");
            labelObj.transform.SetParent(parent);
            labelObj.transform.localPosition = Vector3.up * 0.4f;
            labelObj.transform.localScale = Vector3.one * 0.05f;

            TextMesh tm = labelObj.AddComponent<TextMesh>();
            tm.text = labelText;
            tm.fontSize = 24;
            tm.alignment = TextAlignment.Center;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.color = Color.yellow;
        }

        private void BuildDynamicUI()
        {
            GameObject canvasObj = new GameObject("InspectionCanvas");
            mainCanvas = canvasObj.AddComponent<Canvas>();
            mainCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObj.AddComponent<CanvasScaler>();
            canvasObj.AddComponent<GraphicRaycaster>();

            // Top Status Bar
            GameObject statusObj = CreateUIElement("StatusText", canvasObj.transform);
            statusText = statusObj.AddComponent<Text>();
            statusText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            statusText.fontSize = 18;
            statusText.alignment = TextAnchor.UpperLeft;
            statusText.color = Color.white;
            RectTransform statusRect = statusObj.GetComponent<RectTransform>();
            statusRect.anchorMin = new Vector2(0, 1);
            statusRect.anchorMax = new Vector2(1, 1);
            statusRect.pivot = new Vector2(0, 1);
            statusRect.anchoredPosition = new Vector2(20, -20);
            statusRect.sizeDelta = new Vector2(-40, 40);

            // Detail Inspector Panel
            detailPanel = CreateUIPanel("DetailPanel", canvasObj.transform, new Vector2(0.5f, 0.3f), new Vector2(450, 260));

            GameObject titleObj = CreateUIElement("Title", detailPanel.transform);
            detailTitleText = titleObj.AddComponent<Text>();
            detailTitleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            detailTitleText.fontSize = 22;
            detailTitleText.fontStyle = FontStyle.Bold;
            detailTitleText.color = Color.cyan;
            RectTransform tRect = titleObj.GetComponent<RectTransform>();
            tRect.anchoredPosition = new Vector2(0, 90);
            tRect.sizeDelta = new Vector2(400, 30);

            GameObject bodyObj = CreateUIElement("Body", detailPanel.transform);
            detailBodyText = bodyObj.AddComponent<Text>();
            detailBodyText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            detailBodyText.fontSize = 14;
            detailBodyText.color = Color.white;
            detailBodyText.supportRichText = true;
            RectTransform bRect = bodyObj.GetComponent<RectTransform>();
            bRect.anchoredPosition = new Vector2(0, 15);
            bRect.sizeDelta = new Vector2(400, 120);

            // Action Buttons
            GameObject nBtnObj = CreateUIButton("NormalBtn", detailPanel.transform, "MARK NORMAL", new Vector2(-100, -80), new Color(0.2f, 0.6f, 0.2f));
            normalBtn = nBtnObj.GetComponent<Button>();
            normalBtn.onClick.AddListener(() => SubmitVerdict(false));

            GameObject fBtnObj = CreateUIButton("FaultyBtn", detailPanel.transform, "MARK FAULTY", new Vector2(100, -80), new Color(0.8f, 0.2f, 0.2f));
            faultyBtn = fBtnObj.GetComponent<Button>();
            faultyBtn.onClick.AddListener(() => SubmitVerdict(true));

            // Score Summary Panel
            scorePanel = CreateUIPanel("ScorePanel", canvasObj.transform, new Vector2(0.5f, 0.5f), new Vector2(400, 300));
            GameObject scoreTextObj = CreateUIElement("ScoreText", scorePanel.transform);
            scoreSummaryText = scoreTextObj.AddComponent<Text>();
            scoreSummaryText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            scoreSummaryText.fontSize = 18;
            scoreSummaryText.alignment = TextAnchor.MiddleCenter;
            scoreSummaryText.color = Color.white;
            scoreSummaryText.supportRichText = true;
            scoreTextObj.GetComponent<RectTransform>().sizeDelta = new Vector2(360, 200);

            GameObject restartBtnObj = CreateUIButton("RestartBtn", scorePanel.transform, "NEW ATTEMPT", new Vector2(0, -100), new Color(0.2f, 0.4f, 0.8f));
            restartBtnObj.GetComponent<Button>().onClick.AddListener(StartNewSession);
        }

        private GameObject CreateUIPanel(string name, Transform parent, Vector2 anchor, Vector2 size)
        {
            GameObject panel = new GameObject(name);
            panel.transform.SetParent(parent);
            Image img = panel.AddComponent<Image>();
            img.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);
            RectTransform rt = panel.GetComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.sizeDelta = size;
            rt.anchoredPosition = Vector2.zero;
            return panel;
        }

        private GameObject CreateUIElement(string name, Transform parent)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchoredPosition = Vector2.zero;
            return go;
        }

        private GameObject CreateUIButton(string name, Transform parent, string label, Vector2 pos, Color btnColor)
        {
            GameObject btnObj = CreateUIPanel(name, parent, new Vector2(0.5f, 0.5f), new Vector2(160, 40));
            btnObj.GetComponent<RectTransform>().anchoredPosition = pos;
            btnObj.GetComponent<Image>().color = btnColor;
            Button b = btnObj.AddComponent<Button>();

            GameObject textObj = CreateUIElement("Label", btnObj.transform);
            Text t = textObj.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.text = label;
            t.fontSize = 14;
            t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.UpperCenter;
            t.color = Color.white;
            textObj.GetComponent<RectTransform>().sizeDelta = new Vector2(160, 40);

            return btnObj;
        }
    }

    // =========================================================================
    // 7. INSPECTABLE COMPONENT BEHAVIOR
    // =========================================================================

    public class InspectableComponent : MonoBehaviour
    {
        public ComponentDefinition Definition { get; private set; }
        public FaultType ActiveFault { get; private set; } = FaultType.None;
        public string ObservableCue { get; private set; } = "";
        public bool HasFault => ActiveFault != FaultType.None;
        public bool IsInspected { get; private set; } = false;

        private Renderer compRenderer;

        public void Init(ComponentDefinition def)
        {
            Definition = def;
            compRenderer = GetComponent<Renderer>();
        }

        public void SetFault(FaultType fault, string cue)
        {
            ActiveFault = fault;
            ObservableCue = cue;

            // Visual indicator applied directly to materials without explicit textual labels
            if (fault == FaultType.TemperatureWarning && compRenderer != null)
            {
                compRenderer.material.color = Color.Lerp(compRenderer.material.color, Color.red, 0.6f);
            }
            else if (fault == FaultType.ComponentMisalignment)
            {
                transform.localRotation = Quaternion.Euler(0, 15f, 0);
            }
        }

        public void MarkAsInspected(bool wasCorrect)
        {
            IsInspected = true;
            if (compRenderer != null)
            {
                compRenderer.material.color = wasCorrect ? Color.green : Color.magenta;
            }
        }
    }
}