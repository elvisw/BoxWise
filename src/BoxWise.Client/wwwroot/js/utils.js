window.downloadFile = function (filename, mimeType, base64Content) {
    const link = document.createElement('a');
    link.download = filename;
    link.href = 'data:' + mimeType + ';base64,' + base64Content;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
};

window.copyTextToClipboard = function (text) {
    if (navigator.clipboard && window.isSecureContext) return navigator.clipboard.writeText(text);
    // 非安全上下文回退
    const ta = document.createElement('textarea');
    ta.value = text; ta.style.position = 'fixed'; ta.style.opacity = '0';
    document.body.appendChild(ta); ta.select();
    try { document.execCommand('copy'); } finally { document.body.removeChild(ta); }
    return Promise.resolve();
};
