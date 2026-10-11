(() => {
    const initializeLanding = () => {
        const shell = document.querySelector(".landing-shell");
        if (!shell || shell.dataset.motionInitialized === "true") return;

        shell.dataset.motionInitialized = "true";
        shell.classList.add("landing-motion-ready");

        const header = shell.querySelector(".landing-header");
        const updateHeader = () => header?.classList.toggle("scrolled", window.scrollY > 24);
        window.addEventListener("scroll", updateHeader, { passive: true });
        updateHeader();

        const revealElements = shell.querySelectorAll(".reveal");
        if (window.matchMedia("(prefers-reduced-motion: reduce)").matches || !("IntersectionObserver" in window)) {
            revealElements.forEach(element => element.classList.add("visible"));
        } else {
            const observer = new IntersectionObserver(entries => {
                entries.forEach(entry => {
                    if (!entry.isIntersecting) return;
                    const siblings = [...entry.target.parentElement.children].filter(element => element.classList.contains("reveal"));
                    const index = siblings.indexOf(entry.target);
                    entry.target.style.transitionDelay = `${Math.min(Math.max(index, 0), 5) * 65}ms`;
                    entry.target.classList.add("visible");
                    observer.unobserve(entry.target);
                });
            }, { threshold: 0.1, rootMargin: "0px 0px -50px 0px" });
            revealElements.forEach(element => observer.observe(element));
        }

        const dashboard = shell.querySelector(".dashboard-wrap");
        if (dashboard && window.matchMedia("(min-width: 1025px)").matches) {
            window.addEventListener("pointermove", event => {
                const x = (event.clientX / window.innerWidth - .5) * 10;
                const y = (event.clientY / window.innerHeight - .5) * 10;
                dashboard.style.transform = `translate3d(${x}px, ${y}px, 0)`;
            }, { passive: true });
        }
    };

    requestAnimationFrame(initializeLanding);
    document.addEventListener("enhancedload", initializeLanding);
})();
