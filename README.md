<div align="center">
  <a href="README.md"><img src="https://img.shields.io/badge/lang-English-blue.svg" alt="English"></a>
  <a href="README.ru.md"><img src="https://img.shields.io/badge/lang-Русский-green.svg" alt="Russian"></a>
  <a href="README.uk.md"><img src="https://img.shields.io/badge/lang-Українська-yellow.svg" alt="Ukrainian"></a>
  <a href="README.zh-CN.md"><img src="https://img.shields.io/badge/lang-简体中文-red.svg" alt="Chinese"></a>
</div>

<br/>

# 📼 Recap — An Open-Source Alternative to "Windows Recall"

> **Your personal time machine. Local. Private. Free.**

Recap is a privacy-focused activity tracker and screen recorder for Windows. It captures your workflow, extracts text (OCR) from everything you see, and allows you to instantly search through your visual history. 

**Unlike Windows Recall, Recap runs on any PC, requires no NPU, and guarantees that your data NEVER leaves your hard drive.**

---

## ⚡ Why choose Recap?

*   🔒 **100% Privacy:** No cloud uploads. No telemetry. Your screenshots stay on your drive.
*   🧠 **Search Your Memory:** Forgot a password, a code snippet, or a chat message? Type a keyword, and Recap finds the exact moment.
*   🎮 **Gaming Friendly:** Optimized for minimal CPU impact while gaming.
*   📝 **Annotate Your Day:** Built-in note-taking system tied to specific timestamps.

---

## ✨ Key Features

### 🔍 Search Everything (Global Search & OCR)
*   **Visual Search:** Every frame is scanned for text. Search for "invoice", "meeting", or specific code syntax.
*   **Instant Results:** Query gigabytes of history in milliseconds thanks to optimized SQLite indexing.
*   **Text Highlighting:** Recap highlights the exact location of the searched text on the screenshot.
  <img width="886" height="658" alt="image" src="https://github.com/user-attachments/assets/7e8626d8-8887-46a8-90db-023bfe1e08b5" />


### 🏷️ Notes & Bookmarks
*   **Quick Notes:** Press `B` to instantly mark an important moment (e.g., "Bug found", "Deep work started").
*   **Navigation:** Press `Ctrl+B` to browse the notes panel and jump to specific timestamps.

### 🧠 Intelligent Context-Aware Recording
*   **Motion Detection:** Screenshots are only taken when screen content changes, saving massive amounts of disk space.
*   **Customizable Quality:** Adjustable frame rates and JPEG compression levels.

### 🌐 Browser Integration
*   Works with an optional extension to log visited URLs and YouTube video titles.
*   *Example:* Search for "youtube.com" or a specific video title to filter your timeline.

### 📊 Analytics & Heatmaps
*   **Activity Heatmap:** Visualize your most productive days and hours.
   <img width="868" height="439" alt="image" src="https://github.com/user-attachments/assets/b8530f5a-cfa3-4d40-ac5a-8162e7f8adf6" />



*   **App Usage Charts:** See exactly how much time you spent in VS Code, Telegram, or other apps.
   <img width="851" height="420" alt="image" src="https://github.com/user-attachments/assets/17583931-368d-455f-a297-5f25234e2e38" />

*   **App Aliases:** Rename applications in reports for better organization.

---

## 📦 Recap Converter (Archiving Tool)
*Includes a built-in utility for long-term storage:*

*   **Export to Video:** Converts internal `.sch` data files into standard **.MKV** video files (viewable within the app).
*   **Smart Compression:** Uses FFmpeg (supports NVIDIA NVENC and CPU encoding) to compress days of data into tiny video files.

## 🚀 Installation

### 1. Main Application
1. Go to the [**Releases Page**](https://github.com/maksim0-debug/Recap/releases).
2. Download `Recap.zip`.
3. Extract and run `Recap.exe`. *(No installation required)*.

### 2. Browser Extension (Recommended)
To track URLs and tab titles:
1. Open Chrome/Edge/Brave and go to `chrome://extensions`.
2. Enable **Developer mode** (top right).
3. Click **Load unpacked**.
4. Select the `browser-extension` folder located inside your Recap folder (or this repository).

---

## ⚖️ License
Distributed under the MIT License. See `LICENSE` for more information.
