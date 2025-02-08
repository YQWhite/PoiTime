using System;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;
using System.Timers;
using Microsoft.Win32;
using System.Drawing;         // 用于托盘图标
using NAudio.Wave;            // 用于音频播放
using Microsoft.VisualBasic;


namespace ReportTimePlayer
{
    // 用于读取配置的简单类，对应 config.json 格式
    public class Config
    {
        public string id { get; set; }
    }

    // 自定义 ApplicationContext，无前台窗口
    public class ReportTimeContext : ApplicationContext
    {
        // 定时器，每秒检测系统时间
        private System.Timers.Timer timer;
        // 记录上次播放报时的小时，防止重复播放
        private int lastPlayedHour = -1;
        // 当前音频文件所在目录，读取 config.json 后确定
        private string voiceFolder;
        // 存储当前配置中的 id 值
        private string currentId = "144";

        // 托盘图标
        private NotifyIcon notifyIcon;

        // config.json 文件路径
        private readonly string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

        public ReportTimeContext()
        {
            // 初始化托盘图标
            InitializeTrayIcon();

            // 读取配置文件，设置当前 id 及音频文件夹
            LoadConfig();

            // 设置程序随 Windows 启动（写入注册表 HKCU）
            SetAutoRun();

            // 初始化定时器，每 1000 毫秒检测一次系统时间
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
            // 创建 NotifyIcon 实例
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
            notifyIcon.Text = "PoiTime!"; // 鼠标悬浮显示的文字
            notifyIcon.Visible = true;

            // 创建右键菜单
            ContextMenuStrip contextMenu = new ContextMenuStrip();

            // “重载配置”菜单项，点击后调用 ReloadConfig() 方法
            ToolStripMenuItem reloadMenuItem = new ToolStripMenuItem("重载配置");
            reloadMenuItem.Click += (sender, e) => { ReloadConfig(); };

            // “修改 ID”菜单项
            ToolStripMenuItem changeIdMenuItem = new ToolStripMenuItem("修改 ID");
            changeIdMenuItem.Click += (sender, e) => { ChangeId(); };

            // “退出”菜单项，点击后调用 ExitApplication() 方法退出程序
            ToolStripMenuItem exitMenuItem = new ToolStripMenuItem("退出");
            exitMenuItem.Click += (sender, e) => { ExitApplication(); };

            // 添加菜单项到右键菜单中
            contextMenu.Items.Add(changeIdMenuItem);
            contextMenu.Items.Add(reloadMenuItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(exitMenuItem);

            // 将右键菜单绑定到托盘图标上
            notifyIcon.ContextMenuStrip = contextMenu;
        }


        /// <summary>
        /// 定时器事件处理，每秒检测系统时间
        /// </summary>
        private void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            DateTime now = DateTime.Now;
            // 当分钟为 0 且秒数小于 2（防止漏判），并且当前小时未播放过时
            if (now.Minute == 0 && now.Second < 2 && now.Hour != lastPlayedHour)
            {
                lastPlayedHour = now.Hour;
                PlayVoiceForHour(now.Hour);
            }
        }

        /// <summary>
        /// 根据当前小时播放对应的报时语音
        /// </summary>
        /// <param name="hour">当前小时</param>
        private void PlayVoiceForHour(int hour)
        {
            // 构造文件名格式，如 "0000.mp3", "0100.mp3", … "2300.mp3"
            string fileName = $"{hour:D2}00.mp3";
            string filePath = Path.Combine(voiceFolder, fileName);
            if (File.Exists(filePath))
            {
                try
                {
                    // 使用 NAudio 播放 MP3 文件
                    Mp3FileReader mp3Reader = new Mp3FileReader(filePath);
                    WaveOutEvent waveOut = new WaveOutEvent();
                    waveOut.Init(mp3Reader);
                    waveOut.Play();
                    // 当播放完成后释放资源
                    waveOut.PlaybackStopped += (s, eArgs) =>
                    {
                        waveOut.Dispose();
                        mp3Reader.Dispose();
                    };
                }
                catch (Exception ex)
                {
                    // 播放失败时输出错误信息
                    notifyIcon.ShowBalloonTip(3000, "播放错误", $"播放 {filePath} 时出错：{ex.Message}", ToolTipIcon.Error);
                }
            }
            else
            {
                notifyIcon.ShowBalloonTip(3000, "文件未找到", $"未找到文件：{filePath}", ToolTipIcon.Warning);
            }
        }

        /// <summary>
        /// 从 config.json 读取配置，更新 currentId 及 voiceFolder
        /// </summary>
        private void LoadConfig()
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
                    // 如果 config.json 不存在，写入默认配置
                    Config defaultConfig = new Config { id = currentId };
                    string defaultJson = JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(configPath, defaultJson);
                }
            }
            catch (Exception ex)
            {
                notifyIcon.ShowBalloonTip(3000, "配置错误", "读取 config.json 出错：" + ex.Message, ToolTipIcon.Error);
            }
            // 更新音频文件目录：程序根目录下的 voices\<id>
            voiceFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "voices", currentId);
            notifyIcon.Text = $"PoiTime! (当前ID：{currentId})";
        }

        /// <summary>
        /// 重载配置（读取 config.json），更新音频文件目录
        /// </summary>
        private void ReloadConfig()
        {
            LoadConfig();
            MessageBox.Show("配置重载成功!", "PoiTime!", MessageBoxButtons.OK, MessageBoxIcon.Information);
            notifyIcon.ShowBalloonTip(2000, "重载配置", $"配置已重载，当前ID：{currentId}", ToolTipIcon.Info);
        }
        /// <summary>
        /// 修改 ID，并自动更新 config.json 和重载配置
        /// </summary>
        private void ChangeId()
        {
            string newId = Microsoft.VisualBasic.Interaction.InputBox(
                "请输入新的 ID：", "修改 ID", currentId);

            if (!string.IsNullOrWhiteSpace(newId) && newId != currentId)
            {
                try
                {
                    // 更新 config.json
                    Config newConfig = new Config { id = newId };
                    string json = JsonSerializer.Serialize(newConfig, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(configPath, json);

                    // 立即重载新配置
                    ReloadConfig();

                    // 显示通知
                    notifyIcon.ShowBalloonTip(2000, "ID 已更新", $"新的 ID: {newId}", ToolTipIcon.Info);
                }
                catch (Exception ex)
                {
                    notifyIcon.ShowBalloonTip(3000, "更新失败", $"无法更新 ID: {ex.Message}", ToolTipIcon.Error);
                }
            }
        }

        /// <summary>
        /// 设置程序开机自启动，写入注册表 HKCU\Software\Microsoft\Windows\CurrentVersion\Run
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
        /// 重写 Dispose 方法，确保释放托盘图标
        /// </summary>
        /// <param name="disposing"></param>
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
