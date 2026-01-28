const SERVER_URL = "http://127.0.0.1:19999/track";

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
        let [tab] = await chrome.tabs.query({ active: true, lastFocusedWindow: true });

        let data = { domain: "" };

        if (tab && tab.url) {
            const url = tab.url;

            if (url.startsWith('http://') || url.startsWith('https://')) {
                try {
                    const u = new URL(url);
                    const hostname = u.hostname;

                    if ((hostname.endsWith('youtube.com') || hostname.endsWith('youtu.be')) && hostname !== 'music.youtube.com') {
                        const vid = getYouTubeVideoId(url);
                        
                        if (vid) {
                           
                            let title = tab.title || "Video";
                            
                            title = title.replace(/^\(\d+\)\s*/, "");

                            title = title.replace(/ - YouTube$/, "");
                            
                            title = title.replace(/\|/g, " - ");
                            
                            data.domain = `YouTube|${title} [v=${vid}]`;
                        } 
                        else {
                            data.domain = "YouTube|Home";
                        }
                    }
                    else {
                        data.domain = hostname;
                    }

                } catch (e) {
                }
            } 
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
            else if (url.startsWith('chrome-extension://')) {
                data.domain = "Extension Page";
            }
            else if (url.startsWith('file://')) {
                data.domain = "Local File";
            }
        }

        if (data.domain) {
            fetch(SERVER_URL, {
                method: "POST",
                mode: "no-cors",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(data)
            }).catch(() => {
            });
        }

    } catch (e) {}
}

chrome.tabs.onActivated.addListener(sendCurrentTab);

chrome.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
    if (tab.active && (changeInfo.status === 'complete' || changeInfo.url || changeInfo.title)) {
        sendCurrentTab();
    }
});

chrome.windows.onFocusChanged.addListener((windowId) => {
    if (windowId !== chrome.windows.WINDOW_ID_NONE) sendCurrentTab();
});
