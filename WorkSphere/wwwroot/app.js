// WorkSphere Client Scripts

window.downloadFileFromText = (fileName, content) => {
    const blob = new Blob([content], { type: 'text/markdown' });
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
    return new Date().getTimezoneOffset();
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
