# PoiTime! / ポイタイム！

![Shamirone](favicon.ico)  

🎉 **舰娘整点报时工具 | Hourly Voice Announcement Tool for Kantai Collection (KanColle)**



---

## 🌟 简介 / Introduction

**PoiTime!** 是一款为《舰队Collection》（艦隊これくしょん）爱好者设计的整点报时工具。灵感来源于舰娘游戏中的「整点报时」语音功能，程序会在每小时整点播放对应时间的舰娘语音，让你在桌面也能感受到舰娘的陪伴！  

**PoiTime!** is a desktop utility for fans of *Kantai Collection (KanColle)*. Inspired by the "hourly voice" feature in the game, it plays voice lines from shipgirls (艦娘) at every full hour, bringing your favorite characters to your desktop!

---

## 🚀 功能特性 / Features

- **⏰ 整点报时**  
  每小时整点自动播放对应语音（如 `0100.mp3` 对应凌晨1点）。  
  **Hourly Announcement**: Automatically plays voice files (e.g., `0100.mp3` for 1:00 AM).

- **🎵 多角色支持**  
  通过修改 `ID` 切换不同舰娘语音包（如 `144` 对应夕立 `POI`）。  
  **Multiple Characters**: Switch voice packs via configurable `ID` (e.g., `144` for Yuudachi "POI").

- **🖥️ 无界面后台运行**  
  通过系统托盘图标管理，支持开机自启动。  
  **Background Service**: Runs in system tray with auto-start on boot.

- **⚙️ 动态配置**  
  实时修改ID、重载配置，无需重启程序。  
  **Dynamic Configuration**: Update settings without restarting.

---

## 🛠️ 使用方法 / Usage

### 安装步骤 / Installation
1. 下载 [Release](https://github.com/YQWhite/PoiTime/releases) 并解压。  
   Download the release and extract files.
2. 将舰娘语音包放置于 `voices/<ID>/` 目录下（如 `voices/144/0100.mp3`）。  
   Place voice files in `voices/<ID>/` (e.g., `voices/144/0100.mp3`).
3. 运行 `PoiTime.exe`，程序将自动生成默认 `config.json`。  
   Run `PoiTime.exe` to generate the default config.

### 配置语音包 / Voice Packs
- 修改 `config.json` 中的 `id` 字段以切换角色。  
  Edit `id` in `config.json` to switch characters.
- 右键托盘图标 → **修改ID** 可动态更新配置。  
  Right-click tray icon → **Change ID** to update settings.

---

## 🗃️ 语音包说明 / Voice Pack Guide

### 如何获取语音包？
1. 舰娘语音可从游戏资源提取或同人社区获取（请遵守版权规定）。  
   Extract voice files from game resources or download from fan communities (respect copyrights).
2. 格式：`HH00.mp3`（如 `2300.mp3` 对应23点）。  
   File format: `HH00.mp3` (e.g., `2300.mp3` for 23:00).

### 预置ID参考（示例）
- `144`: 夕立（ゆうだち）POI!  
- 更多ID需用户自行探索或自定义！  

---

## 📜 开源协议 / License

本项目基于 MIT 协议开源。  
Licensed under [MIT License](LICENSE).

---

## 🙏 致谢 / Credits

- 灵感来源于《舰队Collection》的整点语音系统。  
  Inspired by the hourly voice feature in *KanColle*.
- 使用 [NAudio](https://github.com/naudio/NAudio) 实现音频播放。  
  Audio playback powered by NAudio.

---

**🛳️ 一緒に艦隊を盛り上げましょう！ Let's sail with your fleet!**  
