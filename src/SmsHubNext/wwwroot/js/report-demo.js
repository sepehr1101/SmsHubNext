(() => {
    const fa = new Intl.NumberFormat('fa-IR');
    const datasets = [
        { start: 1, values: [1280, 1540, 1720, 1460, 2100, 1980, 2400], delivered: 12205, pending: 187 },
        { start: 8, values: [1800, 2200, 1900, 2600, 2300, 2800, 3100], delivered: 16366, pending: 217 },
    ];
    const initialize = () => {
        const report = document.querySelector('[data-report-demo]');
        if (!report || report.dataset.initialized) return;
        report.dataset.initialized = 'true';
        const content = report.querySelector('[data-report-content]');
        const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
        let index = 0;
        const render = (data) => {
            const lineView = data.start === 8;
            content.classList.toggle('is-line-view', lineView);
            report.querySelector('.chart-title h3').textContent = lineView ? 'روند ارسال در طول هفته' : 'روند ارسال روزانه';
            const total = data.values.reduce((sum, value) => sum + value, 0);
            const failed = total - data.delivered - data.pending;
            const rate = fa.format(Math.round(data.delivered / total * 1000) / 10);
            const maximum = Math.ceil(Math.max(...data.values) / 500) * 500;
            report.querySelector('.report-period span:last-child').textContent = `${fa.format(data.start)} تا ${fa.format(data.start + 6)} فروردین ۱۴۰۵`;
            const metrics = report.querySelectorAll('.report-metrics strong');
            metrics[0].textContent = fa.format(total);
            metrics[1].firstChild.textContent = rate + ' ';
            metrics[2].firstChild.textContent = fa.format(total * 700) + ' ';
            report.querySelector('.positive').textContent = `${fa.format(data.delivered)} پیام تحویل‌شده`;
            report.querySelector('.delivery-total strong').firstChild.textContent = rate;
            report.querySelectorAll('.delivery-summary dd').forEach((element, i) => { element.textContent = fa.format([data.delivered, data.pending, failed][i]); });
            report.querySelectorAll('.delivery-meter i').forEach((element, i) => { element.style.flex = [data.delivered, data.pending, failed][i]; });
            report.querySelectorAll('.chart-bars rect').forEach((bar, i) => {
                const height = data.values[i] / maximum * 200;
                bar.setAttribute('y', 228 - height);
                bar.setAttribute('height', height);
                bar.querySelector('title').textContent = `${fa.format(data.start + i)} فروردین: ${fa.format(data.values[i])} پیام`;
            });
            report.querySelectorAll('.chart-x-labels text').forEach((label, i) => { label.textContent = `${fa.format(data.start + i)} فروردین`; });
            report.querySelectorAll('.chart-y-labels text').forEach((label, i) => { label.textContent = fa.format(maximum * [1, .6, .2, 0][i]); });
            const points = data.values.map((value, i) => [93 + i * 80, 228 - value / maximum * 200]);
            const line = points.map(([x, y], i) => `${i === 0 ? 'M' : 'L'}${x} ${y}`).join('');
            report.querySelector('.line-stroke').setAttribute('d', line);
            report.querySelector('.line-area').setAttribute('d', `${line}L573 228H93Z`);
            report.querySelectorAll('.line-points circle').forEach((point, i) => { point.setAttribute('cy', points[i][1]); });
            let offset = 0;
            ['delivered', 'pending', 'failed'].forEach((name, i) => {
                const portion = [data.delivered, data.pending, failed][i] / total * 100;
                const segment = report.querySelector(`.donut-${name}`);
                segment.setAttribute('stroke-dasharray', `${portion} ${100 - portion}`);
                segment.setAttribute('stroke-dashoffset', -offset);
                offset += portion;
            });
            const peak = report.querySelector('.peak-label');
            peak.textContent = fa.format(data.values[6]);
            peak.setAttribute('y', 228 - data.values[6] / maximum * 200 - 12);
            report.querySelector('#daily-chart-title').textContent = `تعداد پیام از ${fa.format(data.start)} تا ${fa.format(data.start + 6)} فروردین`;
            report.querySelector('#daily-chart-description').textContent = `داده فرضی: ${data.values.map((value, i) => `روز ${fa.format(data.start + i)}: ${fa.format(value)} پیام`).join('، ')}. مجموع ${fa.format(total)} پیام.`;
        };
        const cycle = () => {
            if (!report.isConnected) return;
            if (reducedMotion.matches) { content.classList.remove('is-changing'); return; }
            content.classList.add('is-changing');
            setTimeout(() => {
                if (!report.isConnected) return;
                index = (index + 1) % datasets.length;
                render(datasets[index]);
                content.classList.remove('is-changing');
                setTimeout(cycle, 6500);
            }, 350);
        };
        setTimeout(cycle, 6500);
    };
    new MutationObserver(initialize).observe(document.body, { childList: true, subtree: true });
    document.addEventListener('enhancedload', initialize);
    initialize();
})();
