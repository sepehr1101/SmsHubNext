window.adminDownloads = {
    excel(name, base64) {
        const bytes = Uint8Array.from(atob(base64), character => character.charCodeAt(0));
        const url = URL.createObjectURL(new Blob([bytes], {type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'}));
        const link = document.createElement('a');
        link.href = url;
        link.download = name;
        link.click();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    }
};
