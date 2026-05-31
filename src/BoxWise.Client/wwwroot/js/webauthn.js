window.webauthn = {
    isAvailable: function () { return !!navigator.credentials && !!navigator.credentials.create; },
    base64urlToArrayBuffer: function (b) {
        if (!b) return new Uint8Array(0).buffer;
        var t = b.replace(/-/g, '+').replace(/_/g, '/');
        while (t.length % 4 !== 0) t += '=';
        var d = atob(t), u = new Uint8Array(d.length);
        for (var i = 0; i < d.length; i++) u[i] = d.charCodeAt(i);
        return u.buffer;
    },
    prepareCreationOptions: function (o) {
        var p = JSON.parse(JSON.stringify(o));
        p.challenge = this.base64urlToArrayBuffer(p.challenge);
        p.user.id = this.base64urlToArrayBuffer(p.user.id);
        if (p.excludeCredentials) p.excludeCredentials.forEach(function (c) { c.id = this.base64urlToArrayBuffer(c.id); }, this);
        return p;
    },
    prepareRequestOptions: function (o) {
        var p = JSON.parse(JSON.stringify(o));
        p.challenge = this.base64urlToArrayBuffer(p.challenge);
        if (p.allowCredentials) p.allowCredentials.forEach(function (c) { c.id = this.base64urlToArrayBuffer(c.id); }, this);
        return p;
    },
    createCredential: async function (json) {
        var c = await navigator.credentials.create({ publicKey: this.prepareCreationOptions(JSON.parse(json)) });
        if (!c) throw new Error('用户取消了操作');
        return JSON.stringify(c.toJSON());
    },
    getCredential: async function (json) {
        var c = await navigator.credentials.get({ publicKey: this.prepareRequestOptions(JSON.parse(json)) });
        if (!c) throw new Error('用户取消了操作');
        return JSON.stringify(c.toJSON());
    },
    // Signal API: 告知浏览器哪些凭据仍然有效
    signalAllAccepted: async function (rpId, userId, credentialIds) {
        if (typeof PublicKeyCredential === 'undefined') return;
        if (!PublicKeyCredential.signalAllAcceptedCredentials) return;
        if (!credentialIds || credentialIds.length === 0) return;
        try {
            var ids = credentialIds.map(function (c) { return this.base64urlToArrayBuffer(c); }, this);
            await PublicKeyCredential.signalAllAcceptedCredentials({
                rpId: rpId,
                userId: this.base64urlToArrayBuffer(userId),
                allAcceptedCredentialIds: ids
            });
        } catch (e) { /* 静默降级 */ }
    },
    // Signal API: 告知浏览器特定凭据已失效
    signalUnknown: async function (rpId, credentialId) {
        if (typeof PublicKeyCredential === 'undefined') return;
        if (!PublicKeyCredential.signalUnknownCredential) return;
        try {
            await PublicKeyCredential.signalUnknownCredential({
                rpId: rpId,
                unknownCredentialId: this.base64urlToArrayBuffer(credentialId)
            });
        } catch (e) { /* 静默降级 */ }
    }
};
