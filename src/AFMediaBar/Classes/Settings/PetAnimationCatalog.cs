// 动画标识是本组件的设置契约；仅描述呈现，不代表应用正在录音、上传或执行任务。
namespace AFMediaBar.Classes.Settings;

/// <summary>宠物可选的自动轮换、表情、形变与短动作。</summary>
public static class PetAnimationCatalog
{
    public static IReadOnlyList<string> Ids { get; } = Array.AsReadOnly(new[]
    {
        "auto", "sleeping", "waking", "idle", "listening", "thinking", "searching", "working",
        "excited", "surprised", "suspicious", "angry", "drowsy", "happy", "curious", "confused", "bored", "proud",
        "shy", "sad", "laughing", "scared", "playful", "celebrate", "orbit", "radar", "progress", "spawning",
        "humming", "loading", "dictating", "writing", "sending", "receiving", "uploading", "notifying", "alerting",
        "dragging", "bouncing", "powering-down", "emphasis", "hop", "spin", "spin-dizzy", "spin-wild", "particles"
    });
}
