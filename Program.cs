using System;
using System.IO;
using System.Windows.Forms;
using System.Timers;
using Microsoft.Win32;
using NAudio.Wave;

namespace ReportTimePlayer
{
    // 自定义 ApplicationContext，无窗口运行
    public class ReportTimeContext : ApplicationContext
    {
        private System.Timers.Timer timer;
        private int lastPlayedHour = -1; // 记录上次播放的小时

        // 语音文件存放目录，这里假设放在程序目录下的 "voices" 子文件夹中
        private readonly string voicesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "voices");

        public ReportTimeContext()
        {
            // 设置程序随 Windows 启动（写入注册表）
            SetAutoRun();

            // 初始化计时器，每秒检测一次系统时间
            timer = new System.Timers.Timer(1000);
            timer.Elapsed += Timer_Elapsed;
            timer.AutoReset = true;
            timer.Start();
        }

        private void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            DateTime now = DateTime.Now;
            // 当分钟为 0 且秒数小于2秒（防止漏判），并且当前小时未播放过时触发
            if (now.Minute == 0 && now.Second < 2 && now.Hour != lastPlayedHour)
            {
                lastPlayedHour = now.Hour;
                PlayVoiceForHour(now.Hour);
            }
        }
        private void PlayVoiceForHour(int hour)
        {
            string fileName = $"144-{hour:D2}00.mp3";
            string filePath = Path.Combine(voicesDir, fileName);
            if (File.Exists(filePath))
            {
                try
                {
                    // 使用 NAudio 播放 MP3 文件
                    var waveOut = new WaveOutEvent();
                    var audioFile = new Mp3FileReader(filePath);
                    waveOut.Init(audioFile);
                    waveOut.Play();

                    // 播放完毕后可以添加事件处理关闭设备（根据需要）
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"播放文件 {filePath} 时出错: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine($"未找到文件：{filePath}");
            }
        }

        /// <summary>
        /// 设置程序开机自启动（写入 HKCU\Software\Microsoft\Windows\CurrentVersion\Run）
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
                Console.WriteLine("设置开机自启失败: " + ex.Message);
            }
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // 使用自定义 ApplicationContext 启动程序
            Application.Run(new ReportTimeContext());
        }
    }
}
