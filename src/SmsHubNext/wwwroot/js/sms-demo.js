(() => {
    const initialize = () => {
        const phone = document.querySelector('[data-sms-demo]');
        if (!phone || phone.dataset.initialized) return;
        phone.dataset.initialized = 'true';
        const text = phone.querySelector('[data-sms-text]');
        const status = phone.querySelector('[data-sms-status]');
        const messages = [
            text.textContent,
            'پرداخت شما با موفقیت ثبت شد. از همراهی و اعتماد شما متشکریم.',
            'درخواست شما دریافت شد و در حال بررسی است. نتیجه از طریق پیامک به شما اطلاع داده می‌شود.',
            'یادآوری: نوبت شما فردا ساعت ۱۰ صبح است. از حضور به‌موقع شما سپاسگزاریم.',
        ];
        const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
        let messageIndex = 0;
        let timer;
        const finish = () => {
            text.textContent = messages[messageIndex];
            phone.classList.remove('is-typing');
            phone.classList.add('is-complete');
            status.textContent = 'پیامک تحویل شده';
            if (!reducedMotion.matches && phone.isConnected) {
                timer = setTimeout(() => {
                    messageIndex = (messageIndex + 1) % messages.length;
                    play();
                }, 2600);
            }
        };
        const play = () => {
            clearTimeout(timer);
            if (!phone.isConnected) return;
            if (reducedMotion.matches) { finish(); return; }
            const characters = Array.from(messages[messageIndex]);
            let index = 0;
            text.textContent = '';
            phone.classList.remove('is-complete');
            phone.classList.add('is-typing');
            status.textContent = 'در حال نوشتن پیام…';
            const typeNext = () => {
                if (!phone.isConnected) return;
                if (reducedMotion.matches) { finish(); return; }
                text.textContent += characters[index++];
                if (index < characters.length) timer = setTimeout(typeNext, 48);
                else finish();
            };
            timer = setTimeout(typeNext, 500);
        };
        play();
    };
    // Blazor inserts this page after its interactive circuit connects, and again on navigation.
    new MutationObserver(initialize).observe(document.body, { childList: true, subtree: true });
    document.addEventListener('enhancedload', initialize);
    initialize();
})();
