﻿using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Timers;
using Microsoft.Win32;
using System.Drawing;         // 用于托盘图标
using NAudio.Wave;            // 用于音频播放
using Microsoft.VisualBasic;  // 用于 InputBox
using System.Threading;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Net.Http;
using System.Threading.Tasks;
using System.Linq;
using System.Runtime.Remoting.Metadata.W3cXsd2001;
using System.Runtime.Remoting.Contexts;

namespace ReportTimePlayer
{
    // 全局配置类：存储当前舰娘ID（应用根目录下的 config.json）
    public class Config
    {
        public string id { get; set; }
        public bool randomVoices { get; set; } // 新增随机语音包配置项
    }

    // 单条报时语音配置，包含报时的小时、分钟和对应的文件名
    public class VoiceAnnouncement
    {
        public int hour { get; set; }
        public int minute { get; set; }
        public string fileName { get; set; }
    }

    // 新增：特殊语音配置，包含指定日期和对应文件名
    public class SpecialAnnouncement
    {
        public string date { get; set; }
        public string fileName { get; set; }
    }

    // 舰娘语音文件夹的配置，增加了 name 属性用于存储舰娘名字，同时增加 special 项
    public class VoiceFolderConfig
    {
        public int voiceCount { get; set; }
        public string name { get; set; }      // 舰娘名字
        public List<VoiceAnnouncement> voices { get; set; }
        // 新增 special 项，兼容旧版配置（若不存在 special 则为 null）
        public SpecialAnnouncement special { get; set; }
    }

    // 自定义 ApplicationContext，无前台窗口，使用托盘图标
    public class ReportTimeContext : ApplicationContext
    {
        public async Task DownloadSingleFile(HttpClient client, string fileName, string targetFolder)
        {
            const string baseUrl = "https://zh.kcwiki.cn/wiki/Special:Redirect/file";
            string fileUrl = $"{baseUrl}/{fileName}";
            string savePath = Path.Combine(targetFolder, fileName);

            try
            {
                using (var response = await client.GetAsync(fileUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    using (var stream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(savePath, FileMode.Create))
                    {
                        await stream.CopyToAsync(fileStream);
                    }
                }
            }
            catch
            {
                if (File.Exists(savePath)) File.Delete(savePath);
                throw;
            }
        }
        private System.Timers.Timer timer;
        private string voiceFolder;
        private string currentId = "144";
        private NotifyIcon notifyIcon;
        private readonly string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        private VoiceFolderConfig voiceFolderConfig;
        // 用于记录每个报时语音上次播放时间，避免一分钟内重复播放
        private Dictionary<string, DateTime> lastPlayedAnnouncements = new Dictionary<string, DateTime>();
        public async Task CheckAndDownloadVoicePackage(string newId)
        {
            string targetFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "voices", newId);
            string configPath = Path.Combine(targetFolder, "config.json");

            if (!Directory.Exists(targetFolder) || !File.Exists(configPath))
            {
                try
                {
                    // 使用 Windows Forms 的方式获取 UI 同步上下文
                    var uiContext = SynchronizationContext.Current;
                    string shipName = "";

                    // 在主线程执行输入对话框
                    await Task.Run(() =>
                    {
                        var resetEvent = new ManualResetEvent(false);
                        uiContext.Post(_ =>
                        {
                            shipName = Interaction.InputBox("请输入舰娘名称：", "语音包名称", "未知舰娘");
                            resetEvent.Set();
                        }, null);
                        resetEvent.WaitOne();
                    });

                    if (string.IsNullOrWhiteSpace(shipName))
                    {
                        throw new OperationCanceledException("用户取消了名称输入");
                    }
                    MessageBox.Show($"语音包配置文件缺失,正在下载完整语音包", "PoiTime!", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    await DownloadVoiceFiles(newId, targetFolder);
                    CreateConfigFile(newId, targetFolder, shipName);
                    MessageBox.Show($"下载完成", "PoiTime!", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"下载失败 {ex.Message}", "PoiTime!", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Directory.Delete(targetFolder, true); // 清理失败下载
                    await CheckAndDownloadVoicePackage("144");
                }
            }
        }
        private async Task DownloadVoiceFiles(string shipId, string targetFolder)
        {
            const string baseUrl = "https://zh.kcwiki.cn/wiki/Special:Redirect/file";
            var tasks = new List<Task>();

            Directory.CreateDirectory(targetFolder);

            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(30);

                for (int hour = 0; hour < 24; hour++)
                {
                    string fileName = $"{shipId}-{hour:D2}00.mp3";
                    string fileUrl = $"{baseUrl}/{fileName}";
                    string savePath = Path.Combine(targetFolder, fileName);

                    tasks.Add(DownloadFileAsync(client, fileUrl, savePath));
                }

                await Task.WhenAll(tasks);
            }
        }
        private async Task DownloadFileAsync(HttpClient client, string url, string savePath)
        {
            try
            {
                using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();

                    using (var stream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(savePath, FileMode.Create))
                    {
                        await stream.CopyToAsync(fileStream);
                    }
                }
            }
            catch
            {
                File.Delete(savePath); // 清理部分下载的文件
                throw;
            }
        }
        public void CreateConfigFile(string shipId, string targetFolder, string name)
        {
            var config = new VoiceFolderConfig
            {
                voiceCount = 24,
                name = name,
                voices = Enumerable.Range(0, 24).Select(hour => new VoiceAnnouncement
                {
                    hour = hour,
                    minute = 0,
                    fileName = $"{shipId}-{hour:D2}00.mp3"
                }).ToList()
            };

            string configJson = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(targetFolder, "config.json"), configJson);
        }
        public ReportTimeContext()
        {
            InitializeTrayIcon();
            LoadGlobalConfig();
            SetAutoRun();

            // 新增：检测并播放特殊语音（仅在指定日期首次运行时播放）
            CheckAndPlaySpecialAnnouncement();

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

            // 图标初始化（保持原有逻辑）
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "favicon.ico");
            notifyIcon.Icon = File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application;
            notifyIcon.Text = $"PoiTime! (当前ID：{currentId})";
            notifyIcon.Visible = true;

            // 创建现代化菜单
            var contextMenu = new ContextMenuStrip();
            contextMenu.Font = new Font("微软雅黑", 9, FontStyle.Regular); // Win11默认字体
            contextMenu.ShowImageMargin = false;
            contextMenu.Padding = new Padding(2);
            contextMenu.Renderer = new WinUIMenuRenderer();

            // 菜单项配置
            var menuItems = new ToolStripItem[]
            {
                CreateMenuItem("修改语音包", "\uE8AC", (sender, e) => ChangeId()),
                CreateMenuItem("重载配置", "\uE117", (sender, e) => ReloadAllConfig()),
                new ToolStripSeparator(),
                CreateMenuItem("退出", "\uE8BB", (sender, e) => ExitApplication())
            };

            contextMenu.Items.AddRange(menuItems);
            notifyIcon.ContextMenuStrip = contextMenu;
        }

        private ToolStripMenuItem CreateMenuItem(string text, string iconChar, EventHandler handler)
        {
            var item = new ToolStripMenuItem(text);
            item.Click += handler;
            item.Image = CreateIconImage(iconChar);
            item.Padding = new Padding(6, 4, 12, 4); // 增加横向间距
            return item;
        }

        private Bitmap CreateIconImage(string iconChar)
        {
            var bmp = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(bmp))
            using (Font font = new Font("Segoe MDL2 Assets", 10))
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                TextRenderer.DrawText(g, iconChar, font, new Point(0, 2),
                    Color.FromArgb(0, 102, 204), TextFormatFlags.NoPadding);
            }
            return bmp;
        }

        // 现代化渲染器（WinUI风格）
        private class WinUIMenuRenderer : ToolStripProfessionalRenderer
        {
            public WinUIMenuRenderer() : base(new WinUIColorTable()) { }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = e.Item.Selected ?
                    Color.White : Color.FromArgb(30, 30, 30);
                base.OnRenderItemText(e);
            }

            protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
            {
                e.ArrowColor = e.Item.Selected ? Color.White : Color.DimGray;
                base.OnRenderArrow(e);
            }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                // 隐藏默认边框
            }
        }

        private class WinUIColorTable : ProfessionalColorTable
        {
            public override Color MenuBorder => Color.Transparent;
            public override Color MenuItemBorder => Color.Transparent;
            public override Color MenuItemSelected => Color.FromArgb(0, 102, 204);
            public override Color MenuItemSelectedGradientBegin => Color.FromArgb(0, 102, 204);
            public override Color MenuItemSelectedGradientEnd => Color.FromArgb(0, 102, 204);
            public override Color ImageMarginGradientBegin => Color.White;
            public override Color ImageMarginGradientEnd => Color.White;
            public override Color SeparatorDark => Color.FromArgb(240, 240, 240);
        }

        /// <summary>
        /// 加载全局配置（config.json），确定当前舰娘ID并更新语音文件夹路径，
        /// 同时加载该文件夹内的舰娘语音配置（包括name与 special 项）
        /// </summary>
        private void LoadGlobalConfig()
        {
            try
            {
                if (File.Exists(configPath))
                {
                    string json = File.ReadAllText(configPath);
                    Config config = JsonSerializer.Deserialize<Config>(json);
                    if (config != null)
                    {
                        // 新增随机语音包逻辑
                        if (config.randomVoices)
                        {
                            var availableIds = GetAvailableVoicePackIds();
                            if (availableIds.Count > 0)
                            {
                                currentId = availableIds[new Random().Next(availableIds.Count)];
                            }
                        }
                        else if (!string.IsNullOrWhiteSpace(config.id))
                        {
                            currentId = config.id;
                        }
                    }
                }
                else
                {
                    Config defaultConfig = new Config { id = currentId, randomVoices = false };
                    File.WriteAllText(configPath, JsonSerializer.Serialize(defaultConfig));
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
            bool randomMode = File.ReadAllText(configPath).Contains("\"randomVoices\": true");
            string suffix = randomMode ? "（随机模式）" : "";
            
            if (voiceFolderConfig != null && !string.IsNullOrEmpty(voiceFolderConfig.name))
            {
                notifyIcon.Text = $"PoiTime! (当前语音包：{voiceFolderConfig.name}{suffix})";
            }
            else
            {
                notifyIcon.Text = $"PoiTime! (当前ID：{currentId}{suffix})";
            }
        }
        private List<string> GetAvailableVoicePackIds()
        {
            var ids = new List<string>();
            string voicesRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "voices");
    
            if (Directory.Exists(voicesRoot))
            {
                foreach (var dir in Directory.GetDirectories(voicesRoot))
                {
                    string configPath = Path.Combine(dir, "config.json");
                    if (File.Exists(configPath))
                    {
                        ids.Add(Path.GetFileName(dir));
                    }
                }
            }
            return ids;
}

        /// <summary>
        /// 从舰娘语音文件夹下加载 config.json（包含语音配置、舰娘名字以及 special 项）
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
            string shipName = voiceFolderConfig?.name ?? currentId;
            string modeInfo = File.ReadAllText(configPath).Contains("\"randomVoices\": true") 
                ? "（随机模式）" : "";
            MessageBox.Show($"成功加载{shipName}语音包{modeInfo}", "PoiTime!"); 
            notifyIcon.ShowBalloonTip(2000, "重载配置", $"成功加载{shipName}语音包", ToolTipIcon.Info);
        }

        /// <summary>
        /// 修改舰娘ID，并更新全局配置，然后重载配置
        /// </summary>
        private async void ChangeId()
        {
            var voicePacks = GetAvailableVoicePacks();
            if (voicePacks.Count == 0)
            {
                MessageBox.Show("未找到本地语音包，请手动输入ID下载", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var selector = new VoicePackSelector(voicePacks, this))
            {
                if (selector.ShowDialog() == DialogResult.OK)
                {
                    string newId = selector.SelectedId;
                    await ProcessIdChange(newId);
                }
                else
                {
                    notifyIcon.ShowBalloonTip(2000, "操作取消", "已取消修改语音包操作", ToolTipIcon.Info);
                }
            }
        }


        // 新增方法：获取所有本地语音包
        public Dictionary<string, string> GetAvailableVoicePacks()
        {
            var packs = new Dictionary<string, string>();
            string voicesRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "voices");

            if (!Directory.Exists(voicesRoot)) return packs;

            foreach (var dir in Directory.GetDirectories(voicesRoot))
            {
                string id = Path.GetFileName(dir);
                string configPath = Path.Combine(dir, "config.json");

                if (File.Exists(configPath))
                {
                    try
                    {
                        var config = JsonSerializer.Deserialize<VoiceFolderConfig>(File.ReadAllText(configPath));
                        packs[id] = config?.name ?? "未知语音包";
                    }
                    catch
                    {
                        packs[id] = "配置错误";
                    }
                }
                else
                {
                    packs[id] = "无配置文件";
                }
            }
            return packs;
        }

        // 提取原ChangeId中的处理逻辑
        private async Task ProcessIdChange(string newId)
        {
            try
            {
                bool isRandomMode = newId == "RANDOM";
                
                if (isRandomMode)
                {
                    var availableIds = GetAvailableVoicePackIds();
                    newId = availableIds[new Random().Next(availableIds.Count)];
                }

                await CheckAndDownloadVoicePackage(newId);
                
                Config newConfig = new Config 
                { 
                    id = isRandomMode ? "" : newId,  // 随机模式时清空ID
                    randomVoices = isRandomMode       // 设置随机模式标志
                };
                
                string json = JsonSerializer.Serialize(newConfig);
                File.WriteAllText(configPath, json);
                ReloadAllConfig();
            }
            catch (Exception ex)
            {
                notifyIcon.ShowBalloonTip(3000, "更新失败", $"无法更新语音包: {ex.Message}", ToolTipIcon.Error);
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
        /// 根据传入的语音报时配置播放对应的 MP3 文件（用于整点报时）
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
        /// 新增：播放特殊语音，不使用 lastPlayedAnnouncements 逻辑
        /// </summary>
        /// <param name="fileName">特殊语音文件名</param>
        private void PlaySpecialVoiceAnnouncement(string fileName)
        {
            string filePath = Path.Combine(voiceFolder, fileName);
            if (File.Exists(filePath))
            {
                try
                {
                    Mp3FileReader mp3Reader = new Mp3FileReader(filePath);
                    WaveOutEvent waveOut = new WaveOutEvent();
                    waveOut.Init(mp3Reader);
                    waveOut.Play();
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
        /// 检查 voiceFolderConfig.special，如果存在且日期为今天，
        /// 则在首次运行时播放对应特殊语音，并记录已播放（避免重复播放）
        /// </summary>
        private void CheckAndPlaySpecialAnnouncement()
        {
            if (voiceFolderConfig != null && voiceFolderConfig.special != null &&
                !string.IsNullOrWhiteSpace(voiceFolderConfig.special.date))
            {
                // 尝试解析配置中的日期
                if (DateTime.TryParse(voiceFolderConfig.special.date, out DateTime specialDate))
                {
                    if (specialDate.Date == DateTime.Today)
                    {
                        // 用一个标记文件记录是否已播放，保存在语音文件夹内
                        string specialRecordPath = Path.Combine(voiceFolder, "lastSpecialPlayed.txt");
                        bool alreadyPlayed = false;
                        if (File.Exists(specialRecordPath))
                        {
                            try
                            {
                                string lastPlayed = File.ReadAllText(specialRecordPath);
                                if (DateTime.TryParse(lastPlayed, out DateTime lastPlayedDate) &&
                                    lastPlayedDate.Date == DateTime.Today)
                                {
                                    alreadyPlayed = true;
                                }
                            }
                            catch { }
                        }
                        if (!alreadyPlayed)
                        {
                            // 播放特殊语音
                            PlaySpecialVoiceAnnouncement(voiceFolderConfig.special.fileName);
                            // 记录已播放日期
                            try
                            {
                                File.WriteAllText(specialRecordPath, DateTime.Today.ToString("yyyy-MM-dd"));
                            }
                            catch { }
                        }
                    }
                }
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
            bool createdNew;

            // 创建 Mutex 安全对象，并允许所有用户访问
            MutexSecurity mSec = new MutexSecurity();
            SecurityIdentifier everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
            MutexAccessRule rule = new MutexAccessRule(everyoneSid, MutexRights.FullControl, AccessControlType.Allow);
            mSec.AddAccessRule(rule);

            // 使用 Global 前缀确保跨进程、跨权限的唯一性
            using (Mutex mutex = new Mutex(true, "Global\\ReportTimePlayerMutex", out createdNew, mSec))
            {
                if (!createdNew)
                {
                    MessageBox.Show("程序已经在运行中！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new ReportTimeContext());
            }
        }
    }
    public class VoicePackSelector : Form
    {
        private CheckBox chkRandomMode;
        private ReportTimeContext _context;
        private ListView listView;
        private string selectedId;
        private Button btnDownload;
    
        public string SelectedId => selectedId;
    
        public VoicePackSelector(Dictionary<string, string> voicePacks, ReportTimeContext context)
        {
            _context = context;
            InitializeComponents(voicePacks);
        }
    
        private void InitializeComponents(Dictionary<string, string> voicePacks)
        {
            // 初始化控件
            this.Text = "选择语音包";
            this.Size = new Size(400, 340);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font = new Font("微软雅黑", 10);
    
            // 列表视图
            listView = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                Size = new Size(380, 250),
                Location = new Point(10, 10)
            };
            listView.Columns.Add("ID", 100);
            listView.Columns.Add("语音包名称", 260);
            listView.MouseDoubleClick += ListView_MouseDoubleClick;
    
            // 填充数据
            foreach (var pack in voicePacks)
            {
                var item = new ListViewItem(pack.Key);
                item.SubItems.Add(pack.Value);
                listView.Items.Add(item);
            }
    
            // 随机模式复选框初始化
            chkRandomMode = new CheckBox
            {
                Text = "随机模式",
                AutoSize = true,
                Location = new Point(20, 270)
            };
    
            // 下载按钮初始化
            btnDownload = new Button
            {
                Text = "下载新语音包",
                Size = new Size(120, 30),
                Location = new Point(260, 270),
                Font = new Font("微软雅黑", 9)
            };
            btnDownload.Click += BtnDownload_Click;
    
            // 添加控件到窗体
            this.Controls.Add(listView);
            this.Controls.Add(chkRandomMode);
            this.Controls.Add(btnDownload);
        }
    
        private void ListView_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (listView.SelectedItems.Count > 0)
            {
                selectedId = listView.SelectedItems[0].Text;
                
                // 正确设置随机模式标识
                if (chkRandomMode.Checked)
                {
                    selectedId = "RANDOM";
                }
                
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
        }
    
        private void BtnDownload_Click(object sender, EventArgs e)
        {
            string newId = Interaction.InputBox("请输入语音包ID：", "下载新语音包", "144");
            if (string.IsNullOrWhiteSpace(newId)) return;
    
            string shipName = Interaction.InputBox("请输入语音包名称：", "语音包名称", "未知语音包");
            if (string.IsNullOrWhiteSpace(shipName)) return;
    
            var downloadForm = new DownloadProgressForm(_context, newId, shipName);
            if (downloadForm.ShowDialog() == DialogResult.OK)
            {
                // 刷新列表
                this.listView.Items.Clear();
                var updatedPacks = _context.GetAvailableVoicePacks();
                foreach (var pack in updatedPacks)
                {
                    var item = new ListViewItem(pack.Key);
                    item.SubItems.Add(pack.Value);
                    listView.Items.Add(item);
                }
            }
        }
    }
    public class DownloadProgressForm : Form
    {
        private ProgressBar progressBar;
        private Label lblStatus;
        private ReportTimeContext _context;
        private string _shipId;
        private string _shipName;

        public DownloadProgressForm(ReportTimeContext context, string shipId, string shipName)
        {
            _context = context;
            _shipId = shipId;
            _shipName = shipName;
            InitializeComponents();
            StartDownload();
        }

        private void InitializeComponents()
        {
            this.Text = "下载语音包";
            this.Size = new Size(300, 150);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;

            progressBar = new ProgressBar
            {
                Minimum = 0,
                Maximum = 24,
                Value = 0,
                Size = new Size(260, 20),
                Location = new Point(10, 20)
            };

            lblStatus = new Label
            {
                Text = "准备下载...",
                Location = new Point(10, 50),
                Size = new Size(260, 20)
            };

            this.Controls.Add(progressBar);
            this.Controls.Add(lblStatus);
        }

        private async void StartDownload()
        {
            try
            {
                string targetFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "voices", _shipId);
                Directory.CreateDirectory(targetFolder);

                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(30);
                    for (int hour = 0; hour < 24; hour++)
                    {
                        string fileName = $"{_shipId}-{hour:D2}00.mp3";
                        await _context.DownloadSingleFile(client, fileName, targetFolder);
                        progressBar.Value++;
                        lblStatus.Text = $"正在下载 {fileName} ({progressBar.Value}/24)";
                    }
                }

                _context.CreateConfigFile(_shipId, targetFolder, _shipName);
                // 移除以下错误行
                // if (chkRandomMode.Checked) selectedId = "RANDOM";
                this.DialogResult = DialogResult.OK;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"下载失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.DialogResult = DialogResult.Cancel;
            }
            finally
            {
                this.Close();
            }
        }
    }
}
