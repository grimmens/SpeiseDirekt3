window.downloadFileFromStream = async (fileName, contentStreamReference) => {
    const arrayBuffer = await contentStreamReference.arrayBuffer();
    const blob = new Blob([arrayBuffer], { type: 'application/pdf' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
};

const modalMap = new Map();

window.openModal = (id) => {
    let myModal = modalMap.get(id);
    if (!myModal) {
        myModal = new bootstrap.Modal(document.getElementById(id), {
            keyboard: false
        });
        modalMap.set(id, myModal);
    }
    myModal.show();
};

window.hideModal = (id) => {
    const myModal = modalMap.get(id);
    if (myModal) {
        myModal.toggle();
    }
};