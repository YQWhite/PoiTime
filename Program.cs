using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Timers;
using Microsoft.Win32;
using System.Drawing;         // 用于托盘图标
using NAudio.Wave;            // 用于音频播放
using Microsoft.VisualBasic;  // 用于 InputBox

namespace ReportTimePlayer
{
    // 全局配置类：存储当前舰娘ID（应用根目录下的 config.json）
    public class Config
    {
        public string id { get; set; }
    }

    // 单条报时语音配置，包含报时的小时、分钟和对应的文件名
    public class VoiceAnnouncement
    {
        public int hour { get; set; }
        public int minute { get; set; }
        public string fileName { get; set; }
    }

    // 舰娘语音文件夹的配置，增加了 name 属性用于存储舰娘名字
    public class VoiceFolderConfig
    {
        public int voiceCount { get; set; }
        public string name { get; set; }      // 舰娘名字
        public List<VoiceAnnouncement> voices { get; set; }
    }

    // 自定义 ApplicationContext，无前台窗口，使用托盘图标
    public class ReportTimeContext : ApplicationContext
    {
        private System.Timers.Timer timer;
        private string voiceFolder;
        private string currentId = "144";
        private NotifyIcon notifyIcon;
        private readonly string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        private VoiceFolderConfig voiceFolderConfig;
        // 用于记录每个报时语音上次播放时间，避免一分钟内重复播放
        private Dictionary<string, DateTime> lastPlayedAnnouncements = new Dictionary<string, DateTime>();

        public ReportTimeContext()
        {
            InitializeTrayIcon();
            LoadGlobalConfig();
            SetAutoRun();

            // 定时器每 1000 毫秒检测一次系统时间
            timer = new System.Timers.Timer(1000);
            timer.Elapsed += Timer_Elapsed;
            timer.AutoReset = true;
            timer.Start();
        }

        /// <summary>
        /// 初始化托盘图标及右键菜单
        /// </summary>
        private void InitializeTrayIcon()
        {
            notifyIcon = new NotifyIcon();

            // 构造图标文件路径：程序根目录下的 favicon.ico
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "favicon.ico");
            Icon icon;
            if (File.Exists(iconPath))
            {
                icon = new Icon(iconPath);
            }
            else
            {
                // 若未找到 favicon.ico，则使用系统默认图标
                icon = SystemIcons.Application;
            }
            notifyIcon.Icon = icon;
            // 默认显示当前ID，后续加载舰娘配置后会更新为舰娘名字
            notifyIcon.Text = $"PoiTime! (当前ID：{currentId})";
            notifyIcon.Visible = true;

            // 创建右键菜单
            ContextMenuStrip contextMenu = new ContextMenuStrip();

            // “修改 ID”菜单项
            ToolStripMenuItem changeIdMenuItem = new ToolStripMenuItem("修改 ID");
            changeIdMenuItem.Click += (sender, e) => { ChangeId(); };

            // “重载配置”菜单项
            ToolStripMenuItem reloadMenuItem = new ToolStripMenuItem("重载配置");
            reloadMenuItem.Click += (sender, e) => { ReloadAllConfig(); };

            // “退出”菜单项
            ToolStripMenuItem exitMenuItem = new ToolStripMenuItem("退出");
            exitMenuItem.Click += (sender, e) => { ExitApplication(); };

            contextMenu.Items.Add(changeIdMenuItem);
            contextMenu.Items.Add(reloadMenuItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(exitMenuItem);

            // 将右键菜单绑定到托盘图标上
            notifyIcon.ContextMenuStrip = contextMenu;
        }

        /// <summary>
        /// 加载全局配置（config.json），确定当前舰娘ID并更新语音文件夹路径，
        /// 同时加载该文件夹内的舰娘语音配置（包括name）
        /// </summary>
        private void LoadGlobalConfig()
        {
            try
            {
                if (File.Exists(configPath))
                {
                    string json = File.ReadAllText(configPath);
                    Config config = JsonSerializer.Deserialize<Config>(json);
                    if (config != null && !string.IsNullOrWhiteSpace(config.id))
                    {
                        currentId = config.id;
                    }
                }
                else
                {
                    // 若全局配置不存在，则写入默认配置
                    Config defaultConfig = new Config { id = currentId };
                    string defaultJson = JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(configPath, defaultJson);
                }
            }
            catch (Exception ex)
            {
                notifyIcon.ShowBalloonTip(3000, "配置错误", "读取全局 config.json 出错：" + ex.Message, ToolTipIcon.Error);
            }
            // 更新语音文件夹路径：程序根目录下的 voices\<id>
            voiceFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "voices", currentId);

            // 加载舰娘语音配置
            LoadVoiceFolderConfig();

            // 如果读取到了舰娘名字，则显示舰娘名字；否则显示当前ID
            if (voiceFolderConfig != null && !string.IsNullOrEmpty(voiceFolderConfig.name))
            {
                notifyIcon.Text = $"PoiTime! (当前舰娘：{voiceFolderConfig.name})";
            }
            else
            {
                notifyIcon.Text = $"PoiTime! (当前ID：{currentId})";
            }
        }

        /// <summary>
        /// 从舰娘语音文件夹下加载 config.json（包含语音配置和舰娘名字）
        /// </summary>
        private void LoadVoiceFolderConfig()
        {
            string voiceConfigPath = Path.Combine(voiceFolder, "config.json");
            if (File.Exists(voiceConfigPath))
            {
                try
                {
                    string voiceJson = File.ReadAllText(voiceConfigPath);
                    voiceFolderConfig = JsonSerializer.Deserialize<VoiceFolderConfig>(voiceJson);
                }
                catch (Exception ex)
                {
                    notifyIcon.ShowBalloonTip(3000, "语音配置错误", "读取语音 config.json 出错：" + ex.Message, ToolTipIcon.Error);
                }
            }
            else
            {
                notifyIcon.ShowBalloonTip(3000, "语音配置缺失", $"未找到 {voiceConfigPath}", ToolTipIcon.Warning);
            }
        }

        /// <summary>
        /// 重载所有配置：全局配置与舰娘语音配置
        /// 修改后的提示为“成功加载&lt;舰娘名称&gt;语音包”
        /// </summary>
        private void ReloadAllConfig()
        {
            LoadGlobalConfig();
            string shipName = (voiceFolderConfig != null && !string.IsNullOrEmpty(voiceFolderConfig.name))
                              ? voiceFolderConfig.name : currentId;
            MessageBox.Show($"成功加载{shipName}语音包", "PoiTime!", MessageBoxButtons.OK, MessageBoxIcon.Information);
            notifyIcon.ShowBalloonTip(2000, "重载配置", $"成功加载{shipName}语音包", ToolTipIcon.Info);
        }

        /// <summary>
        /// 修改舰娘ID，并更新全局配置，然后重载配置
        /// </summary>
        private void ChangeId()
        {
            string newId = Interaction.InputBox("请输入新的 ID：", "修改 ID", currentId);
            if (!string.IsNullOrWhiteSpace(newId) && newId != currentId)
            {
                try
                {
                    // 更新全局 config.json
                    Config newConfig = new Config { id = newId };
                    string json = JsonSerializer.Serialize(newConfig, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(configPath, json);

                    // 立即重载新配置
                    ReloadAllConfig();

                    if (voiceFolderConfig != null && !string.IsNullOrEmpty(voiceFolderConfig.name))
                    {
                        notifyIcon.ShowBalloonTip(2000, "ID 已更新", $"新的舰娘：{voiceFolderConfig.name}", ToolTipIcon.Info);
                    }
                    else
                    {
                        notifyIcon.ShowBalloonTip(2000, "ID 已更新", $"新的 ID: {newId}", ToolTipIcon.Info);
                    }
                }
                catch (Exception ex)
                {
                    notifyIcon.ShowBalloonTip(3000, "更新失败", $"无法更新 ID: {ex.Message}", ToolTipIcon.Error);
                }
            }
        }

        /// <summary>
        /// 设置程序开机自启动（写入注册表 HKCU\Software\Microsoft\Windows\CurrentVersion\Run）
        /// </summary>
        private void SetAutoRun()
        {
            try
            {
                string exePath = Application.ExecutablePath;
                RegistryKey rk = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                rk.SetValue("ReportTimePlayer", exePath);
            }
            catch (Exception ex)
            {
                notifyIcon.ShowBalloonTip(3000, "自启设置错误", "设置开机自启失败：" + ex.Message, ToolTipIcon.Error);
            }
        }

        /// <summary>
        /// 定时器事件处理，每秒检测系统时间，根据舰娘语音配置在指定时间播放对应语音
        /// </summary>
        private void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            DateTime now = DateTime.Now;
            if (voiceFolderConfig != null && voiceFolderConfig.voices != null)
            {
                foreach (var announcement in voiceFolderConfig.voices)
                {
                    // 判断当前时间是否与配置匹配（小时、分钟和秒数容错判断）
                    if (announcement.hour == now.Hour && announcement.minute == now.Minute && now.Second < 2)
                    {
                        // 用 "HH:mm" 作为键，避免同一分钟内重复播放
                        string key = $"{announcement.hour:D2}:{announcement.minute:D2}";
                        if (!lastPlayedAnnouncements.ContainsKey(key) || lastPlayedAnnouncements[key].Minute != now.Minute)
                        {
                            lastPlayedAnnouncements[key] = now;
                            PlayVoiceAnnouncement(announcement);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 根据传入的语音报时配置播放对应的 MP3 文件
        /// </summary>
        /// <param name="announcement">报时语音配置项</param>
        private void PlayVoiceAnnouncement(VoiceAnnouncement announcement)
        {
            string filePath = Path.Combine(voiceFolder, announcement.fileName);
            if (File.Exists(filePath))
            {
                try
                {
                    Mp3FileReader mp3Reader = new Mp3FileReader(filePath);
                    WaveOutEvent waveOut = new WaveOutEvent();
                    waveOut.Init(mp3Reader);
                    waveOut.Play();
                    // 播放完成后释放资源
                    waveOut.PlaybackStopped += (s, eArgs) =>
                    {
                        waveOut.Dispose();
                        mp3Reader.Dispose();
                    };
                }
                catch (Exception ex)
                {
                    notifyIcon.ShowBalloonTip(3000, "播放错误", $"播放 {filePath} 时出错：{ex.Message}", ToolTipIcon.Error);
                }
            }
            else
            {
                notifyIcon.ShowBalloonTip(3000, "文件未找到", $"未找到文件：{filePath}", ToolTipIcon.Warning);
            }
        }

        /// <summary>
        /// 退出程序：停止定时器、隐藏托盘图标、退出应用
        /// </summary>
        private void ExitApplication()
        {
            timer.Stop();
            timer.Dispose();
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
            Application.Exit();
        }

        /// <summary>
        /// 重写 Dispose 方法，确保释放托盘图标和定时器资源
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer?.Dispose();
                notifyIcon?.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    static class Program
    {
        /// <summary>
        /// 应用程序的主入口点
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new ReportTimeContext());
        }
    }
}
