// WorkSphere Client Scripts

window.downloadFileFromText = (fileName, content, mimeType = 'text/plain') => {
    const blob = new Blob([content], { type: mimeType });
    const url = URL.createObjectURL(blob);
    const anchorElement = document.createElement('a');
    anchorElement.href = url;
    anchorElement.download = fileName;
    document.body.appendChild(anchorElement);
    anchorElement.click();
    document.body.removeChild(anchorElement);
    URL.revokeObjectURL(url);
};

window.getLocalTimezoneOffset = () => {
    const offset = new Date().getTimezoneOffset();
    try {
        document.cookie = "ws_tz_offset=" + offset + ";path=/;max-age=31536000;SameSite=Lax";
    } catch(e) {}
    return offset;
};

window.measureCalendarContextMenuPosition = (element) => {
    const viewportWidth = window.innerWidth || document.documentElement.clientWidth || 0;
    const viewportHeight = window.innerHeight || document.documentElement.clientHeight || 0;

    if (!element) {
        return {
            menuWidth: 0,
            menuHeight: 0,
            viewportWidth,
            viewportHeight
        };
    }

    const rect = element.getBoundingClientRect();

    return {
        menuWidth: rect.width,
        menuHeight: rect.height,
        viewportWidth,
        viewportHeight
    };
};
