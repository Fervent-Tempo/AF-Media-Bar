namespace AFMediaBar.Classes.Models
{
    /// <summary>
    /// 应用配置模型：定义应用配置文件的路径和文件名。
    /// Application configuration model: defines paths and file names for app configuration.
    ///
    /// ⚠️ 注意 Note:
    /// 此类目前未被使用，可能是遗留代码或为未来功能预留。
    /// This class is currently unused, may be legacy code or reserved for future features.
    /// </summary>
    public class AppConfig
    {
        /// <summary>配置文件夹路径 Configuration folder path</summary>
        public string ConfigurationsFolder { get; set; }

        /// <summary>应用属性文件名 Application properties file name</summary>
        public string AppPropertiesFileName { get; set; }
    }
}
