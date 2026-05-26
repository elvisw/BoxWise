const MAX_FILE_SIZE = 10 * 1024 * 1024; // 10MB
let _pending = false;

export function capturePhoto(dotNetHelper) {
    if (_pending) return;
    _pending = true;

    const input = document.createElement('input');
    input.type = 'file';
    input.accept = 'image/*';
    input.capture = 'environment';

    input.onchange = (e) => {
        _pending = false;
        const file = e.target.files[0];
        if (!file) {
            dotNetHelper.invokeMethodAsync('OnPhotoCaptured', null, null, null);
            return;
        }
        if (file.size > MAX_FILE_SIZE) {
            dotNetHelper.invokeMethodAsync('OnPhotoError', '照片不能超过10MB');
            return;
        }
        const reader = new FileReader();
        reader.onload = () => {
            dotNetHelper.invokeMethodAsync('OnPhotoCaptured', file.name, file.type, reader.result);
        };
        reader.onerror = () => {
            dotNetHelper.invokeMethodAsync('OnPhotoError', '照片读取失败');
        };
        reader.readAsDataURL(file);
    };

    input.click();
}
