window.clipboardCopy = {
    copyText: function (text) {
        if (navigator.clipboard && navigator.clipboard.writeText) {
            return navigator.clipboard.writeText(text);
        }

        // Fallback for non-secure contexts (HTTP)
        const textarea = document.createElement('textarea');
        textarea.value = text;
        textarea.style.position = 'fixed';
        textarea.style.opacity = '0';
        document.body.appendChild(textarea);
        textarea.focus();
        textarea.select();
        try {
            document.execCommand('copy');
        } finally {
            document.body.removeChild(textarea);
        }
        return Promise.resolve();
    }
};
