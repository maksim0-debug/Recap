# 📼 Recap — 一个开源的 "Windows Recall" 替代方案

> **您的私人时光机。本地。私密。免费。**

Recap 是一款注重隐私的 Windows 活动追踪器和屏幕录像工具。它能捕获您的工作流程，提取所见内容中的文本 (OCR)，并允许您立即搜索您的视觉历史记录。 

**与 Windows Recall 不同，Recap 可以在任何 PC 上运行，无需 NPU，并保证您的数据“绝不会”离开您的硬盘。**

---

## ⚡ 为什么选择 Recap？

*   🔒 **100% 隐私保护：** 无需上传云端，无遥测数据。您的截图完全保存在本地硬盘。
*   🧠 **搜索您的记忆：** 忘记了密码、代码片段或聊天记录？输入关键字，Recap 即可找到确切的瞬间。
*   🎮 **游戏友好：** 经过优化，确保在游戏时对 CPU 的影响降至最低。
*   📝 **记录您的一天：** 内置与特定时间戳关联的笔记系统。

---

## ✨ 核心功能

### 🔍 全局搜索与 OCR (Search Everything)
*   **视觉搜索：** 扫描每一帧的文本。您可以搜索“发票”、“会议”或特定的代码语法。
*   **瞬时结果：** 得益于优化的 SQLite 索引，可在毫秒内查询数 GB 的历史数据。
*   **文本高亮：** Recap 会在截图中突出显示搜索文本的确切位置。
  <img width="886" height="658" alt="image" src="https://github.com/user-attachments/assets/7e8626d8-8887-46a8-90db-023bfe1e08b5" />


### 🏷️ 笔记与书签
*   **快速笔记：** 按 `B` 键立即标记重要时刻（例如“发现 Bug”、“进入深度工作状态”）。
*   **导航：** 按 `Ctrl+B` 浏览笔记面板并快速跳转到特定时间点。

### 🧠 智能上下文感知录制
*   **运动检测：** 仅在屏幕内容发生变化时进行截图，从而节省大量磁盘空间。
*   **可自定义质量：** 可调节帧率和 JPEG 压缩级别。

### 🌐 浏览器集成
*   配合可选的扩展程序使用，可记录访问过的 URL 和 YouTube 视频标题。
*   *示例：* 搜索 "youtube.com" 或特定的视频标题来过滤您的时间轴。

### 📊 数据分析与热力图
*   **活动热力图：** 可视化您最高效的日期和时段。
   <img width="868" height="439" alt="image" src="https://github.com/user-attachments/assets/b8530f5a-cfa3-4d40-ac5a-8162e7f8adf6" />

*   **应用使用图表：** 准确查看您在 VS Code、Telegram 或其他应用中花费的时间。
   <img width="851" height="420" alt="image" src="https://github.com/user-attachments/assets/17583931-368d-455f-a297-5f25234e2e38" />

*   **应用别名：** 在报告中重命名应用程序，以便更好地进行组织管理。

---

## 📦 Recap Converter (归档工具)
*包含一个用于长期存储的内置实用程序：*

*   **导出为视频：** 将内部 `.sch` 数据文件转换为标准的 **.MKV** 视频文件（可在应用内查看）。
*   **智能压缩：** 使用 FFmpeg（支持 NVIDIA NVENC 和 CPU 编码）将数天的数据压缩为极小的视频文件。

## 🚀 安装指南

### 1. 主程序
1. 前往 [**发布页面 (Releases Page)**](https://github.com/maksim0-debug/Recap/releases)。
2. 下载 `Recap.zip`。
3. 解压并运行 `Recap.exe`。*(无需安装)*。

### 2. 浏览器扩展（推荐）
用于追踪 URL 和标签页标题：
1. 打开 Chrome/Edge/Brave 并进入 `chrome://extensions`。
2. 启用右上角的**开发者模式 (Developer mode)**。
3. 点击**加载解压的扩展程序 (Load unpacked)**。
4. 选择 Recap 文件夹（或此仓库）内的 `browser-extension` 文件夹。

---

## ⚖️ 许可证
遵循 MIT 许可证开源。详情请参阅 `LICENSE` 文件。
