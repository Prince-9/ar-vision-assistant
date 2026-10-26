using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Spawns a world-space panel near a given anchor point and renders a parsed
/// JSON result (Dictionary/List/primitives from SimpleJsonParser) as readable text.
/// Attach this to an empty GameObject and assign panelPrefab (a World Space
/// Canvas with a single TextMeshProUGUI or Text child named "ResultText").
/// </summary>
public class ResultPanelController : MonoBehaviour
{
    [Header("Setup")]
    [SerializeField] private GameObject panelPrefab;
    [SerializeField] private float distanceFromAnchor = 0.15f;
    [SerializeField] private float panelScale = 0.001f; // world-space canvases are usually huge by default

    [Header("Behaviour")]
    [SerializeField] private bool billboardToCamera = true;
    [SerializeField] private float autoHideAfterSeconds = 0f; // 0 = stays until manually cleared

    private GameObject activePanel;

    /// <summary>
    /// Call this with the raw JSON string returned by VisionAssistant, and
    /// the world position/rotation where the panel should appear (e.g. the
    /// point on the spatial mesh the user was looking at).
    /// </summary>
    public void ShowResult(string rawJson, Vector3 anchorPosition, Quaternion anchorRotation)
    {
        object parsed;
        try
        {
            parsed = SimpleJsonParser.Parse(ExtractJsonFromResponse(rawJson));
        }
        catch (System.Exception e)
        {
            Debug.LogError("Failed to parse LLM response: " + e.Message + "\nRaw: " + rawJson);
            DisplayText("Couldn't parse a result. Try again with a clearer view.", anchorPosition, anchorRotation);
            return;
        }

        string formatted = FormatForDisplay(parsed, 0);
        DisplayText(formatted, anchorPosition, anchorRotation);
    }

    /// <summary>
    /// The Claude API wraps the model's text in a content[] array. This pulls
    /// the actual JSON payload the model produced out of that envelope.
    /// </summary>
    private string ExtractJsonFromResponse(string apiResponseJson)
    {
        var envelope = SimpleJsonParser.Parse(apiResponseJson) as Dictionary<string, object>;
        string modelText = null;

        if (envelope != null)
        {
            // OpenRouter / OpenAI-style shape: {"choices":[{"message":{"content": "..."}}]}
            if (envelope.TryGetValue("choices", out object choicesObj))
            {
                var choicesList = choicesObj as List<object>;
                if (choicesList != null && choicesList.Count > 0)
                {
                    var firstChoice = choicesList[0] as Dictionary<string, object>;
                    if (firstChoice != null && firstChoice.TryGetValue("message", out object messageObj))
                    {
                        var messageDict = messageObj as Dictionary<string, object>;
                        if (messageDict != null && messageDict.TryGetValue("content", out object contentTextObj))
                        {
                            modelText = contentTextObj as string;
                        }
                    }
                }
            }
            // Claude / Anthropic-style shape: {"content":[{"text": "..."}]}
            else if (envelope.TryGetValue("content", out object contentObj))
            {
                var contentList = contentObj as List<object>;
                if (contentList != null && contentList.Count > 0)
                {
                    var firstBlock = contentList[0] as Dictionary<string, object>;
                    if (firstBlock != null && firstBlock.TryGetValue("text", out object textObj))
                    {
                        modelText = textObj as string;
                    }
                }
            }
            // Surface API-level errors clearly instead of failing silently
            else if (envelope.TryGetValue("error", out object errorObj))
            {
                var errorDict = errorObj as Dictionary<string, object>;
                string errorMsg = errorDict != null && errorDict.TryGetValue("message", out object msgObj) ? msgObj as string : errorObj.ToString();
                throw new System.Exception("API error: " + errorMsg);
            }
        }

        if (modelText == null)
        {
            // Fallback: assume the whole string is already the extraction JSON
            return apiResponseJson;
        }

        // Strip markdown code fences if the model added them despite instructions
        modelText = modelText.Trim();
        if (modelText.StartsWith("```"))
        {
            int firstNewline = modelText.IndexOf('\n');
            int lastFence = modelText.LastIndexOf("```");
            modelText = modelText.Substring(firstNewline + 1, lastFence - firstNewline - 1).Trim();
        }
        return modelText;
    }

    private string FormatForDisplay(object node, int depth)
    {
        var sb = new StringBuilder();
        string indent = new string(' ', depth * 2);

        if (node is Dictionary<string, object> dict)
        {
            foreach (var kvp in dict)
            {
                if (kvp.Value is Dictionary<string, object> || kvp.Value is List<object>)
                {
                    sb.AppendLine($"{indent}<b>{Prettify(kvp.Key)}:</b>");
                    sb.Append(FormatForDisplay(kvp.Value, depth + 1));
                }
                else
                {
                    sb.AppendLine($"{indent}<b>{Prettify(kvp.Key)}:</b> {kvp.Value}");
                }
            }
        }
        else if (node is List<object> list)
        {
            foreach (var item in list)
            {
                if (item is Dictionary<string, object>)
                {
                    sb.Append(FormatForDisplay(item, depth));
                    sb.AppendLine();
                }
                else
                {
                    sb.AppendLine($"{indent}- {item}");
                }
            }
        }
        else
        {
            sb.AppendLine($"{indent}{node}");
        }

        return sb.ToString();
    }

    private string Prettify(string key)
    {
        // "notable_details" -> "Notable Details"
        string spaced = key.Replace('_', ' ');
        return CultureInfoTitleCase(spaced);
    }

    private string CultureInfoTitleCase(string s)
    {
        return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s);
    }

    private void DisplayText(string text, Vector3 position, Quaternion rotation)
    {
        if (activePanel != null) Destroy(activePanel);

        Vector3 spawnPos = position + rotation * Vector3.forward * distanceFromAnchor;
        activePanel = Instantiate(panelPrefab, spawnPos, rotation);
        activePanel.transform.localScale = Vector3.one * panelScale;

        var text3D = activePanel.GetComponentInChildren<Text>();
        if (text3D != null)
        {
            text3D.text = text;
        }
        else
        {
            // If you're using TextMeshPro instead, swap the block above for:
            // var tmp = activePanel.GetComponentInChildren<TMPro.TextMeshProUGUI>();
            // if (tmp != null) tmp.text = text;
            Debug.LogWarning("panelPrefab has no Text component named appropriately. Result: " + text);
        }

        if (billboardToCamera)
        {
            var billboard = activePanel.AddComponent<BillboardToCamera>();
        }

        if (autoHideAfterSeconds > 0f)
        {
            Destroy(activePanel, autoHideAfterSeconds);
        }
    }

    public void ClearPanel()
    {
        if (activePanel != null) Destroy(activePanel);
    }
}

/// <summary>
/// Keeps a world-space panel facing the headset camera so it's always readable.
/// </summary>
public class BillboardToCamera : MonoBehaviour
{
    private Transform cam;

    private void Start()
    {
        cam = Camera.main != null ? Camera.main.transform : null;
    }

    private void LateUpdate()
    {
        if (cam == null) return;
        transform.rotation = Quaternion.LookRotation(cam.position - transform.position, Vector3.up);
    }
}
