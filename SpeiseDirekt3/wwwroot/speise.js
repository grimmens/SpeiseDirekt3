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