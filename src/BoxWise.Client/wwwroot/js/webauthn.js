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
    }
};
