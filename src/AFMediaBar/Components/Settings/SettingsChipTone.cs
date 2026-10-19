namespace AFMediaBar.Components;

/// <summary>
/// 设置页状态芯片的语气。芯片用于表达“当前模式 / 未实现 / 需先启用”这类状态，
/// 取代把状态塞进卡片描述句或标题括号里的做法：句子读起来像说明，芯片读起来像状态。
/// Tone of a settings status chip. Chips state conditions such as "current / not implemented /
/// needs enabling" so that a condition is not buried inside a description sentence, where it reads as
/// an explanation instead of a state.
/// </summary>
public enum SettingsChipTone
{
    /// <summary>中性：尚未实现、按版本预留等既非错误也非强调的状态。/ Neutral: not implemented or reserved, neither an error nor an emphasis.</summary>
    Neutral,

    /// <summary>主色：当前生效或已启用的状态。/ Accent: the state that is currently in effect or enabled.</summary>
    Accent,

    /// <summary>注意：需要用户先满足前置条件才能改动。/ Attention: a precondition must be met before this can change.</summary>
    Attention,
}
