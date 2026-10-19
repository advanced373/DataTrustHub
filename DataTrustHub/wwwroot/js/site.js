// Blocks files over the size limit before they are sent, so large uploads fail fast with a clear message
document.querySelectorAll('input[type="file"][data-max-bytes]').forEach(input => {
    const isWithinLimit = () => {
        const file = input.files[0];
        const tooLarge = file !== undefined && file.size > Number(input.dataset.maxBytes);
        input.setCustomValidity(tooLarge ? input.dataset.maxMessage : '');
        return !tooLarge;
    };

    input.addEventListener('change', () => {
        isWithinLimit();
        input.reportValidity();
    });

    input.form?.addEventListener('submit', event => {
        if (isWithinLimit()) return;
        event.preventDefault();
        input.reportValidity();
    });
});
