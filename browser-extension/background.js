const SERVER_URL = "http://127.0.0.1:19999/track";

// Хелпер для извлечения ID видео
function getYouTubeVideoId(url) {
    try {
        const u = new URL(url);
        if (u.hostname.endsWith('youtube.com')) {
            return u.searchParams.get('v');
        } else if (u.hostname.endsWith('youtu.be')) {
            return u.pathname.slice(1);
        }
    } catch (e) {}
    return null;
}

async function sendCurrentTab() {
    try {
        // Получаем активную вкладку
        let [tab] = await chrome.tabs.query({ active: true, lastFocusedWindow: true });

        let data = { domain: "" };

        if (tab && tab.url) {
            const url = tab.url;

            // 1. ЛОГИКА ДЛЯ WEB СТРАНИЦ
            if (url.startsWith('http://') || url.startsWith('https://')) {
                try {
                    const u = new URL(url);
                    const hostname = u.hostname;

                    // --- Специальная обработка для YouTube ---
                    if ((hostname.endsWith('youtube.com') || hostname.endsWith('youtu.be')) && hostname !== 'music.youtube.com') {
                        const vid = getYouTubeVideoId(url);
                        
                        if (vid) {
                            // Если это ВИДЕО: отправляем формат "YouTube|Название [v=ID]"
                            // ID нужен приложению для скачивания превью.
                            // Название берем из заголовка вкладки.
                            let title = tab.title || "Video";
                            
                            // --- ИЗМЕНЕНИЕ: Убираем уведомления вида "(1) " в начале ---
                            title = title.replace(/^\(\d+\)\s*/, "");

                            // Убираем хвост " - YouTube"
                            title = title.replace(/ - YouTube$/, "");
                            
                            // Убираем символы pipe |, так как они используются как разделитель в приложении
                            title = title.replace(/\|/g, " - ");
                            
                            // Формируем строку, которую распарсит AppFilterController
                            data.domain = `YouTube|${title} [v=${vid}]`;
                        } 
                        else {
                            // Если это главная, канал или поиск -> просто группа YouTube
                            data.domain = "YouTube|Home";
                        }
                    }
                    else {
                        // --- Обычные сайты ---
                        data.domain = hostname;
                    }

                } catch (e) {
                    // Если URL некорректный, не отправляем ничего
                }
            } 
            // 2. ЛОГИКА ДЛЯ ВНУТРЕННИХ СТРАНИЦ БРАУЗЕРА
            else if (url.startsWith('chrome://') || url.startsWith('edge://') || url.startsWith('brave://')) {
                try {
                    const parts = url.split('/');
                    if (parts.length > 2) {
                        data.domain = "Browser: " + parts[2];
                        
                        if (parts[2] === 'newtab') data.domain = "New Tab";
                        if (parts[2] === 'extensions') data.domain = "Extensions Manager";
                        if (parts[2] === 'settings') data.domain = "Settings";
                        if (parts[2] === 'history') data.domain = "History";
                        if (parts[2] === 'downloads') data.domain = "Downloads";
                    }
                } catch (e) {}
            }
            // 3. ЛОГИКА ДЛЯ РАСШИРЕНИЙ
            else if (url.startsWith('chrome-extension://')) {
                data.domain = "Extension Page";
            }
            // 4. ЛОГИКА ДЛЯ ЛОКАЛЬНЫХ ФАЙЛОВ
            else if (url.startsWith('file://')) {
                data.domain = "Local File";
            }
        }

        // Отправляем данные на локальный сервер C# приложения
        if (data.domain) {
            fetch(SERVER_URL, {
                method: "POST",
                mode: "no-cors",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(data)
            }).catch(() => {
                // Игнорируем ошибки сети (если приложение закрыто)
            });
        }

    } catch (e) {}
}

// Подписываемся на события переключения и обновления вкладок
chrome.tabs.onActivated.addListener(sendCurrentTab);

chrome.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
    // Отправляем обновление, когда загрузка завершена или поменялся URL (для SPA типа YouTube)
    if (tab.active && (changeInfo.status === 'complete' || changeInfo.url || changeInfo.title)) {
        sendCurrentTab();
    }
});

chrome.windows.onFocusChanged.addListener((windowId) => {
    if (windowId !== chrome.windows.WINDOW_ID_NONE) sendCurrentTab();
});
