Recap — Activity Tracker & Screen Recorder for Windows
Recap is a local, privacy-focused activity tracker. It records your screen, performs Optical Character Recognition (OCR), tracks website usage, and visualizes your productivity. All data remains 100% on your local drive.
✨ Key Features
🧠 Smart Recording
Context-Aware: Recap captures screenshots only when the content on your screen changes.
Quality Settings: Adjust frames per second (FPS) and JPEG quality/compression to find the perfect balance between visual clarity and disk space usage.
🔍 Global Search & OCR
Instant Recall: Find any moment from your past instantly.
OCR Scanning: Every frame is scanned for text. Search for "invoice," "chat with Alex," or any code snippet you saw days ago.
High-Speed Search: Query your entire history database in milliseconds.
🎯 App Filtering & Navigation
App Filter: Isolate history to view usage for specific programs (e.g., only VS Code).
Timeline: Scroll through your day as easily as using a video player.
Activity Heatmap: Visualize your most productive days and hours at a glance.
🌐 Browser Tracking
Works in tandem with a companion browser extension to log visited URLs and YouTube video titles. This enables you to search your history by specific web pages.
📦 Recap Converter
Includes a built-in utility for efficient storage management:
Video Conversion: Converts internal .sch data files into standard MKV video format.
Compression: Leverages FFmpeg (supporting both NVIDIA NVENC and CPU encoding) to significantly reduce file sizes for long-term archiving.
🛠 Tech Stack
Platform: .NET Framework 4.8
UI: Windows Forms (WinForms)
Database: SQLite (high-performance indexing for OCR data)
Video Engine: LibVLCSharp
OCR Engine: Windows Media OCR API
🚀 Installation & Setup
1. Main Application
Navigate to the Releases page.
Download the latest Recap.zip.
Extract the archive.
Run Recap.exe.
2. Browser Extension (Required for URL tracking)
Recap requires a helper extension to accurately read tab titles and URLs.
Open your browser (Chrome/Edge/Brave) and go to chrome://extensions.
Enable Developer mode (toggle in the top right corner).
Click Load unpacked.
Select the browser-extension folder located inside your Recap folder (or this repository).
⚖️ License
Distributed under the MIT License. See LICENSE for more information.
