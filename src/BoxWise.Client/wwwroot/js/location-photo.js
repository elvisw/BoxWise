window.boxwiseLocationPhoto = {
    focusById: function (id) {
        const element = document.getElementById(id);
        if (!element) return false;
        element.focus({ preventScroll: true });
        return document.activeElement === element;
    },

    // MudDialog 9.x has no AriaDescribedBy property; unmatched attributes land on
    // .mud-dialog-content. Resolve the real role=dialog from this component's unique
    // content root and set the description only while that dialog is visible.
    setDialogDescribedBy: function (element, descriptionId) {
        if (!element
            || !descriptionId
            || typeof element.closest !== 'function') {
            return false;
        }

        const dialog = element.closest('.mud-dialog[role="dialog"]');
        if (!dialog
            || dialog.getAttribute('aria-hidden') === 'true'
            || dialog.getClientRects().length === 0) {
            return false;
        }

        if (dialog.getAttribute('aria-describedby') !== descriptionId) {
            dialog.setAttribute('aria-describedby', descriptionId);
        }

        return true;
    },

    clearDialogDescribedBy: function (descriptionId) {
        if (!descriptionId) return;

        const dialogs = document.querySelectorAll('.mud-dialog[role="dialog"]');
        for (const dialog of dialogs) {
            if (dialog.getAttribute('aria-describedby') === descriptionId) {
                dialog.removeAttribute('aria-describedby');
            }
        }
    },

    // Passive camera probe. It never calls getUserMedia and never runs from the
    // capture button, so it cannot consume the native chooser's user activation.
    // Some browsers do not support permissions.query({ name: 'camera' }) or hide
    // videoinput entries until permission is granted. When that makes the result
    // ambiguous we return 'unknown' rather than asserting a denial.
    getCameraAvailability: async function () {
        if (!navigator.mediaDevices
            || typeof navigator.mediaDevices.enumerateDevices !== 'function') {
            return 'unsupported';
        }

        let permission = 'unknown';
        try {
            if (navigator.permissions && typeof navigator.permissions.query === 'function') {
                const status = await navigator.permissions.query({ name: 'camera' });
                if (status
                    && (status.state === 'granted'
                        || status.state === 'denied'
                        || status.state === 'prompt')) {
                    permission = status.state;
                }
            }
        } catch {
            // The camera permission name is not supported everywhere.
        }

        let hasCamera;
        try {
            const devices = await navigator.mediaDevices.enumerateDevices();
            hasCamera = devices.some(device => device.kind === 'videoinput');
        } catch {
            return permission === 'denied' ? 'denied' : 'unknown';
        }

        if (permission === 'denied') return 'denied';

        if (!hasCamera) {
            // Only an explicitly granted permission plus an empty list is positive
            // proof that no camera exists. prompt/unknown can hide videoinput until
            // permission is granted, so they remain unknown.
            return permission === 'granted' ? 'no-device' : 'unknown';
        }

        return 'available';
    }
};
