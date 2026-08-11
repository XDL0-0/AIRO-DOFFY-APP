using TMPro;
using UnityEngine;
using UnityEngine.UI; // 必须引用 UI 命名空间

public class TactileUIManager : MonoBehaviour
{
    [Header("References")]
    [Tooltip("(SensorArrayRoot")]
    public GameObject sensorRoot;

    [Tooltip("UI Button")]
    public Button toggleButton;

    [Tooltip("TEXT on Button")]

    [SerializeField] TextMeshProUGUI buttonText = null;
    [Header("Settings")]
    public Color disabledColor = Color.gray;
    public Color enabledColor = Color.green;
    public Color activeColor = Color.yellow; // 打开可视化时的颜色

    private bool isDataReceived = false;
    private bool isVisualizationOn = false;

    void Start()
    {
        // 1. 初始状态：隐藏传感器
        if (sensorRoot != null)
            sensorRoot.SetActive(false);

        // 2. 初始状态：按钮不可用 (灰色)
        if (toggleButton != null)
        {
            toggleButton.interactable = false;
            SetButtonColor(disabledColor);
            if (buttonText) buttonText.text = "Tactile Data Receiving...";

            // 绑定点击事件
            toggleButton.onClick.RemoveAllListeners();
            toggleButton.onClick.AddListener(OnToggleClicked);
        }
    }

    /// <summary>
    /// 【重要】请在你的 UDP 接收脚本中调用此方法
    /// 当收到第一帧有效数据时调用
    /// </summary>
    public void EnableVisualizationButton()
    {
        // 如果已经激活了，就不重复执行
        if (isDataReceived) return;

        isDataReceived = true;

        if (toggleButton != null)
        {
            toggleButton.interactable = true;
            SetButtonColor(enabledColor);
            if (buttonText) buttonText.text = "Show Tactile";
        }

        Debug.Log("UDP 数据已连接，可视化按钮已解锁。");
    }

    /// <summary>
    /// 按钮点击逻辑
    /// </summary>
    private void OnToggleClicked()
    {
        isVisualizationOn = !isVisualizationOn;

        // 切换传感器显隐
        if (sensorRoot != null)
            sensorRoot.SetActive(isVisualizationOn);

        // 更新按钮状态文字
        if (buttonText)
        {
            buttonText.text = isVisualizationOn ? "Hide Tactile" : "Show Tactile";
        }

        // 可选：切换颜色表示“正在显示”
        SetButtonColor(isVisualizationOn ? activeColor : enabledColor);
    }

    private void SetButtonColor(Color color)
    {
        if (toggleButton != null)
        {
            var colors = toggleButton.colors;
            colors.normalColor = color;
            colors.highlightedColor = color * 1.2f;
            toggleButton.colors = colors;

            // 同时也改一下 Image 组件的颜色，防止 Button Transition 设置不同导致看不清
            Image btnImage = toggleButton.GetComponent<Image>();
            if (btnImage != null) btnImage.color = color;
        }
    }
}