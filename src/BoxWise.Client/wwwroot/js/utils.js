window.downloadFile = function (filename, mimeType, base64Content) {
    const link = document.createElement('a');
    link.download = filename;
    link.href = 'data:' + mimeType + ';base64,' + base64Content;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
};
