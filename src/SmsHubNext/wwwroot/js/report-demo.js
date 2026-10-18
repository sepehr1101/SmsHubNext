(() => {
    const initialize = () => {
        const report = document.querySelector('[data-report-demo]');
        if (!report || report.dataset.initialized) return;
        report.dataset.initialized = 'true';
        const slides = [...report.querySelectorAll('[data-report-slide]')];
        const dots = [...report.querySelectorAll('[data-report-dot]')];
        const play = report.querySelector('[data-report-play]');
        const motion = window.matchMedia('(prefers-reduced-motion: reduce)');
        const lifecycle = new AbortController();
        let index = 0;
        let paused = motion.matches;
        let hovering = false;
        let timer;
        let journeyTimer;
        let pointerPaused;
        const journey = report.querySelector('[data-message-journey]');
        const visual = journey?.querySelector('.journey-visual');
        const travel = journey?.querySelector('[data-journey-motion]');
        const updateJourney = () => {
            clearTimeout(journeyTimer);
            if (!visual) return;
            visual.classList.remove('is-running');
            travel?.endElement();
            if (!report.isConnected || motion.matches || document.hidden || slides[index].hidden || !slides[index].contains(journey)) return;
            // Repeat the complete sequence, leaving time to read the delivered message.
            void visual.offsetWidth;
            visual.classList.add('is-running');
            travel?.beginElement();
            journeyTimer = setTimeout(updateJourney, 5500);
        };
        const updatePlay = () => {
            play.textContent = paused ? 'پخش خودکار' : 'توقف خودکار';
            play.setAttribute('aria-label', paused ? 'شروع تعویض خودکار اسلایدها' : 'توقف تعویض خودکار اسلایدها');
        };
        const schedule = () => {
            clearTimeout(timer);
            if (paused || hovering || document.hidden || !report.isConnected) return;
            timer = setTimeout(() => {
                if (!report.isConnected) return;
                show((index + 1) % slides.length, false);
            }, 8000);
        };
        const show = (next, announce = true) => {
            index = next;
            slides.forEach((slide, i) => {
                slide.hidden = i !== index;
                slide.inert = i !== index;
            });
            dots.forEach((dot, i) => dot.setAttribute('aria-current', String(i === index)));
            updateJourney();
            if (announce) report.querySelector('[data-report-announcement]').textContent = slides[index].getAttribute('aria-label');
            schedule();
        };
        const manual = next => { paused = true; updatePlay(); show(next); };
        report.querySelector('[data-report-prev]').addEventListener('click', () => manual((index + slides.length - 1) % slides.length));
        report.querySelector('[data-report-next]').addEventListener('click', () => manual((index + 1) % slides.length));
        dots.forEach((dot, i) => dot.addEventListener('click', () => manual(i)));
        report.addEventListener('mouseenter', () => { hovering = true; clearTimeout(timer); });
        report.addEventListener('mouseleave', () => { hovering = false; schedule(); });
        report.addEventListener('focusin', () => { paused = true; updatePlay(); clearTimeout(timer); });
        play.addEventListener('pointerdown', () => { pointerPaused = paused; });
        play.addEventListener('click', event => {
            paused = !(event.detail > 0 && pointerPaused !== undefined ? pointerPaused : paused);
            pointerPaused = undefined;
            updatePlay();
            schedule();
        });
        motion.addEventListener('change', () => {
            if (motion.matches) { paused = true; updatePlay(); clearTimeout(timer); }
            updateJourney();
        }, { signal: lifecycle.signal });
        document.addEventListener('visibilitychange', () => { schedule(); updateJourney(); }, { signal: lifecycle.signal });
        const cleanup = new MutationObserver(() => {
            if (report.isConnected) return;
            clearTimeout(timer);
            clearTimeout(journeyTimer);
            lifecycle.abort();
            cleanup.disconnect();
        });
        cleanup.observe(document.body, { childList: true, subtree: true });
        updatePlay();
        updateJourney();
        schedule();
    };
    new MutationObserver(initialize).observe(document.body, { childList: true, subtree: true });
    document.addEventListener('enhancedload', initialize);
    initialize();
})();
