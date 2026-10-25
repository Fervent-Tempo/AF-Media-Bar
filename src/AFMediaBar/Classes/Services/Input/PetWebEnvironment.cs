// 共享宠物浏览器环境的惰性创建任务；视图各自拥有并释放 WebView2 控件。
using System.IO;
using Microsoft.Web.WebView2.Core;

namespace AFMediaBar.Classes.Services;

/// <summary>所有宠物视图使用同一环境和应用现有 WebView2 数据目录。</summary>
public sealed class PetWebEnvironment
{
    private readonly string _userDataFolder;
    private Task<CoreWebView2Environment>? _creation;

    public PetWebEnvironment() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AFMediaBar")) { }
    internal PetWebEnvironment(string userDataFolder) => _userDataFolder = userDataFolder;

    internal async Task<CoreWebView2Environment> GetAsync()
    {
        var creation = _creation ??= CoreWebView2Environment.CreateAsync(userDataFolder: _userDataFolder);
        try { return await creation; }
        catch
        {
            if (ReferenceEquals(creation, _creation)) _creation = null;
            throw;
        }
    }
}
